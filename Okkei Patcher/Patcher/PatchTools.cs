using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using ICSharpCode.SharpZipLib.Zip;
using OkkeiPatcher.Exceptions;
using OkkeiPatcher.Extensions;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Manifest;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static SignApk.SignApk;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher.Patcher
{
	internal class PatchTools : ToolsBase
	{
		private OkkeiManifest _manifest;
		private bool _saveDataBackupFromOldPatch;
		private X509Certificate2 _signingCertificate;

		protected override async Task InternalOnInstallSuccess(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				progress.Reset();

				if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);

				await RestoreSavedataBackup(progress, token);
				await DownloadObb(progress, token);
				Preferences.Set(Prefkey.apk_is_patched.ToString(), true);

				UpdateStatus(Resource.String.patch_success);
			}
			catch (OperationCanceledException)
			{
				if (_saveDataBackupFromOldPatch && ProcessState.ProcessSavedata && !ProcessState.PatchUpdate &&
				    File.Exists(FilePaths[Files.BackupSavedata]))
					File.Delete(FilePaths[Files.BackupSavedata]);

				SetStatusToAborted();
			}
			finally
			{
				progress.Reset();
				IsRunning = false;
			}
		}

		private async Task RestoreSavedataBackup(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata || ProcessState.PatchUpdate ||
			    !File.Exists(FilePaths[Files.SAVEDATA_BACKUP])) return;

			if (_saveDataBackupFromOldPatch)
			{
				UpdateStatus(Resource.String.restore_old_saves);

				if (File.Exists(FilePaths[Files.BackupSavedata]))
					await IOUtils.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath, SavedataFileName, progress,
							token)
						.ConfigureAwait(false);
			}

			if (File.Exists(FilePaths[Files.BackupSavedata])) File.Delete(FilePaths[Files.BackupSavedata]);
			File.Move(FilePaths[Files.SAVEDATA_BACKUP], FilePaths[Files.BackupSavedata]);
		}

		private async Task DownloadObb(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			progress.Reset();

			if (ProcessState.ObbUpdate && File.Exists(FilePaths[Files.ObbToReplace]))
				File.Delete(FilePaths[Files.ObbToReplace]);

			if (!ProcessState.PatchUpdate)
				UpdateStatus(Resource.String.compare_obb);

			if (!ProcessState.ObbUpdate && ProcessState.ScriptsUpdate ||
			    File.Exists(FilePaths[Files.ObbToReplace]) &&
			    await MD5Utils.CompareMD5(Files.ObbToReplace, progress, token).ConfigureAwait(false))
				return;

			UpdateStatus(Resource.String.download_obb);

			try
			{
				await IOUtils.DownloadFile(_manifest.Obb.URL, ObbPath, ObbFileName, progress, token)
					.ConfigureAwait(false);
			}
			catch (HttpStatusCodeException ex)
			{
				DisplayMessage(OkkeiUtils.GetText(Resource.String.error),
					string.Format(OkkeiUtils.GetText(Resource.String.http_file_access_error),
						ex.StatusCode.ToString()), OkkeiUtils.GetText(Resource.String.dialog_ok), null);
				NotifyAboutError();
				token.Throw();
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
			{
				DisplayMessage(Resource.String.error, Resource.String.http_file_download_error,
					Resource.String.dialog_ok, null);
				NotifyAboutError();
				token.Throw();
			}

			UpdateStatus(Resource.String.write_obb_md5);

			var obbHash = await MD5Utils.CalculateMD5(FilePaths[Files.ObbToReplace], progress, token)
				.ConfigureAwait(false);
			if (obbHash != _manifest.Obb.MD5)
			{
				SetStatusToAborted();
				DisplayMessage(Resource.String.error, Resource.String.hash_obb_mismatch, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				token.Throw();
			}

			Preferences.Set(Prefkey.downloaded_obb_md5.ToString(), obbHash);
			Preferences.Set(Prefkey.obb_version.ToString(), _manifest.Obb.Version);
		}

		public void Patch(Activity activity, ProcessState processState, OkkeiManifest manifest,
			IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() =>
				InternalPatch(activity, processState, manifest, progress, token).OnException(WriteBugReport));
		}

		private async Task InternalPatch(Activity activity, ProcessState processState, OkkeiManifest manifest,
			IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			IsRunning = true;
			_saveDataBackupFromOldPatch = false;
			ProcessState = processState;
			_manifest = manifest;

			try
			{
				progress.Reset();

				if (!CheckIfCouldApplyPatch()) token.Throw();

				await BackupSavedata(progress, token);

				// If patching for the first time or updating scripts and if there is no patched or backup APK
				if ((!ProcessState.ObbUpdate || ProcessState.ScriptsUpdate) &&
				    (!File.Exists(FilePaths[Files.SignedApk]) || !File.Exists(FilePaths[Files.BackupApk])))
				{
					var originalApkPath = RetrieveOriginalApkPath();
					await RetrieveOriginalApk(originalApkPath, progress, token);
					await BackupApk(originalApkPath, progress, token);

					if (ProcessState.ScriptsUpdate || !ProcessState.ObbUpdate)
					{
						await DownloadScripts(progress, token);

						var extractedScriptsPath = Path.Combine(OkkeiFilesPath, "scripts");
						ExtractScripts(extractedScriptsPath, progress);

						var apkZipFile = new ZipFile(FilePaths[Files.TempApk]);
						ReplaceScripts(extractedScriptsPath, apkZipFile, progress);

						progress.MakeIndeterminate();

						ZipUtils.RemoveApkSignature(apkZipFile);
						ZipUtils.UpdateZip(apkZipFile);
						FileUtils.DeleteFolder(extractedScriptsPath);

						if (token.IsCancellationRequested)
						{
							if (File.Exists(FilePaths[Files.TempApk])) File.Delete(FilePaths[Files.TempApk]);
							token.Throw();
						}

						await SignApk(progress, token);

						if (token.IsCancellationRequested)
						{
							if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);
							token.Throw();
						}
					}
				}

				await BackupObb(progress, token);

				ClearStatus();
				progress.MakeIndeterminate();

				if (!ProcessState.PatchUpdate)
				{
					UninstallOriginalPackage(activity);
					return;
				}

				if (ProcessState.ScriptsUpdate)
				{
					await InstallUpdatedApk(activity, progress, token);
					return;
				}

				if (ProcessState.ObbUpdate) FinishPatch(progress, token);
			}
			catch (OperationCanceledException)
			{
				SetStatusToAborted();
				progress.Reset();
				IsRunning = false;
			}
			finally
			{
				if (File.Exists(FilePaths[Files.Scripts])) File.Delete(FilePaths[Files.Scripts]);
			}
		}

		private bool CheckIfCouldApplyPatch()
		{
			UpdateStatus(Resource.String.checking);

			var isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

			if (isPatched && !ProcessState.PatchUpdate)
			{
				DisplayMessage(Resource.String.error, Resource.String.error_patched, Resource.String.dialog_ok, null);
				NotifyAboutError();
				return false;
			}

			if (!PackageManagerUtils.IsAppInstalled(ChaosChildPackageName))
			{
				DisplayMessage(Resource.String.error, Resource.String.cc_not_found, Resource.String.dialog_ok, null);
				NotifyAboutError();
				return false;
			}

			if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
			{
				DisplayMessage(Resource.String.error, Resource.String.no_free_space_patch, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				return false;
			}

			return true;
		}

		private async Task BackupSavedata(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata || ProcessState.PatchUpdate) return;

			progress.Reset();

			_saveDataBackupFromOldPatch = File.Exists(FilePaths[Files.BackupSavedata]) &&
			                              await MD5Utils.CompareMD5(Files.BackupSavedata, progress, token)
				                              .ConfigureAwait(false);

			if (File.Exists(FilePaths[Files.OriginalSavedata]))
			{
				UpdateStatus(Resource.String.compare_saves);

				if (await MD5Utils.CompareMD5(Files.OriginalSavedata, progress, token).ConfigureAwait(false)) return;

				UpdateStatus(Resource.String.backup_saves);

				await IOUtils
					.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup, SavedataBackupFileName, progress,
						token)
					.ConfigureAwait(false);

				UpdateStatus(Resource.String.write_saves_md5);

				Preferences.Set(Prefkey.savedata_md5.ToString(),
					await MD5Utils.CalculateMD5(FilePaths[Files.OriginalSavedata], progress, token)
						.ConfigureAwait(false));

				return;
			}

			DisplayMessage(Resource.String.warning, Resource.String.saves_not_found_patch, Resource.String.dialog_ok,
				null);
		}

		private static string RetrieveOriginalApkPath()
		{
			return Application.Context.PackageManager
				?.GetPackageInfo(ChaosChildPackageName, 0)
				?.ApplicationInfo
				?.PublicSourceDir;
		}

		private async Task RetrieveOriginalApk(string originalApkPath, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			UpdateStatus(Resource.String.copy_apk);

			await IOUtils.CopyFile(originalApkPath, OkkeiFilesPath, TempApkFileName, progress, token)
				.ConfigureAwait(false);
		}

		private async Task BackupApk(string originalApkPath, IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!File.Exists(FilePaths[Files.TempApk]) || ProcessState.PatchUpdate) return;

			UpdateStatus(Resource.String.compare_apk);

			if (File.Exists(FilePaths[Files.BackupApk]) &&
			    await MD5Utils.CompareMD5(Files.TempApk, progress, token).ConfigureAwait(false))
				return;

			UpdateStatus(Resource.String.backup_apk);

			await IOUtils.CopyFile(originalApkPath, OkkeiFilesPathBackup, BackupApkFileName, progress, token)
				.ConfigureAwait(false);

			UpdateStatus(Resource.String.write_apk_md5);

			Preferences.Set(Prefkey.backup_apk_md5.ToString(),
				await MD5Utils.CalculateMD5(FilePaths[Files.BackupApk], progress, token).ConfigureAwait(false));
		}

		private async Task DownloadScripts(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			progress.Reset();
			UpdateStatus(Resource.String.compare_scripts);

			if (File.Exists(FilePaths[Files.Scripts]) &&
			    await MD5Utils.CompareMD5(Files.Scripts, progress, token).ConfigureAwait(false))
				return;

			UpdateStatus(Resource.String.download_scripts);

			try
			{
				await IOUtils.DownloadFile(_manifest.Scripts.URL, OkkeiFilesPath, ScriptsFileName, progress, token)
					.ConfigureAwait(false);
			}
			catch (HttpStatusCodeException ex)
			{
				SetStatusToAborted();
				DisplayMessage(OkkeiUtils.GetText(Resource.String.error),
					string.Format(OkkeiUtils.GetText(Resource.String.http_file_access_error),
						ex.StatusCode.ToString()), OkkeiUtils.GetText(Resource.String.dialog_ok), null);
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
			{
				DisplayMessage(Resource.String.error, Resource.String.http_file_download_error,
					Resource.String.dialog_ok, null);
				NotifyAboutError();
				token.Throw();
			}

			UpdateStatus(Resource.String.write_scripts_md5);

			var scriptsHash = await MD5Utils.CalculateMD5(FilePaths[Files.Scripts], progress, token)
				.ConfigureAwait(false);
			if (scriptsHash != _manifest.Scripts.MD5)
			{
				SetStatusToAborted();
				DisplayMessage(Resource.String.error, Resource.String.hash_scripts_mismatch, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				token.Throw();
			}

			Preferences.Set(Prefkey.scripts_md5.ToString(), scriptsHash);
			Preferences.Set(Prefkey.scripts_version.ToString(), _manifest.Scripts.Version);
		}

		private void ExtractScripts(string extractPath, IProgress<ProgressInfo> progress)
		{
			progress.Reset();
			UpdateStatus(Resource.String.extract_scripts);

			ZipUtils.ExtractZip(FilePaths[Files.Scripts], extractPath);
		}

		private void ReplaceScripts(string scriptsPath, ZipFile apkZipFile, IProgress<ProgressInfo> progress)
		{
			progress.Reset();

			var filePaths = Directory.GetFiles(scriptsPath);
			var scriptsCount = filePaths.Length;

			UpdateStatus(Resource.String.replace_scripts);

			apkZipFile.BeginUpdate();

			var currentProgress = 0;
			foreach (var scriptfile in filePaths)
			{
				apkZipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));
				++currentProgress;
				progress.Report(currentProgress, scriptsCount);
			}
		}

		private async Task SignApk(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			UpdateStatus(Resource.String.sign_apk);
			progress.MakeIndeterminate();

			_signingCertificate ??= CertificateUtils.GetSigningCertificate();

			var apkToSign = new FileStream(FilePaths[Files.TempApk], FileMode.Open);
			var signedApkStream = new FileStream(FilePaths[Files.SignedApk], FileMode.OpenOrCreate);
			const bool signWholeFile = false;

			SignPackage(apkToSign, _signingCertificate, signedApkStream, signWholeFile);

			apkToSign.Dispose();
			signedApkStream.Dispose();

			UpdateStatus(Resource.String.write_patched_apk_md5);

			Preferences.Set(Prefkey.signed_apk_md5.ToString(),
				await MD5Utils.CalculateMD5(FilePaths[Files.SignedApk], progress, token).ConfigureAwait(false));

			if (File.Exists(FilePaths[Files.TempApk])) File.Delete(FilePaths[Files.TempApk]);
		}

		private async Task BackupObb(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (ProcessState.PatchUpdate) return;

			progress.Reset();

			if (File.Exists(FilePaths[Files.ObbToBackup]))
			{
				UpdateStatus(Resource.String.compare_obb);

				if (File.Exists(FilePaths[Files.BackupObb]) &&
				    await MD5Utils.CompareMD5(Files.ObbToBackup, progress, token).ConfigureAwait(false))
					return;

				UpdateStatus(Resource.String.backup_obb);

				await IOUtils.CopyFile(FilePaths[Files.ObbToBackup], OkkeiFilesPathBackup, ObbFileName, progress, token)
					.ConfigureAwait(false);

				UpdateStatus(Resource.String.write_obb_md5);

				Preferences.Set(Prefkey.backup_obb_md5.ToString(),
					await MD5Utils.CalculateMD5(FilePaths[Files.BackupObb], progress, token).ConfigureAwait(false));

				return;
			}

			DisplayMessage(Resource.String.error, Resource.String.obb_not_found_patch, Resource.String.dialog_ok, null);
			NotifyAboutError();
			token.Throw();
		}

		private void UninstallOriginalPackage(Activity activity)
		{
			DisplayMessage(Resource.String.attention, Resource.String.uninstall_prompt_patch, Resource.String.dialog_ok,
				() => PackageManagerUtils.UninstallPackage(activity, ChaosChildPackageName));
		}

		private async Task InstallUpdatedApk(Activity activity, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			await InternalOnUninstallResult(activity, progress, token).ConfigureAwait(false);
		}

		private void FinishPatch(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			OnInstallSuccess(progress, token);
		}

		protected override async Task InternalOnUninstallResult(Activity activity, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!CheckUninstallSuccess(progress)) return;

			var apkMd5 = string.Empty;

			if (!IsRunning) return;

			if (Preferences.ContainsKey(Prefkey.signed_apk_md5.ToString()))
				apkMd5 = Preferences.Get(Prefkey.signed_apk_md5.ToString(), string.Empty);
			var path = FilePaths[Files.SignedApk];

			try
			{
				if (!File.Exists(path))
				{
					DisplayMessage(Resource.String.error, Resource.String.apk_not_found_patch,
						Resource.String.dialog_ok, null);
					NotifyAboutError();
					token.Throw();
				}

				UpdateStatus(Resource.String.compare_apk);

				var apkFileMd5 = await MD5Utils.CalculateMD5(path, progress, token).ConfigureAwait(false);

				if (apkMd5 == apkFileMd5)
				{
					progress.MakeIndeterminate();
					var installer = new PackageInstaller(progress);
					installer.InstallFailed += PackageInstallerOnInstallFailed;
					UpdateStatus(Resource.String.installing);
					DisplayMessage(Resource.String.attention, Resource.String.install_prompt_patch,
						Resource.String.dialog_ok,
						() => MainThread.BeginInvokeOnMainThread(() =>
							installer.InstallPackage(activity, Android.Net.Uri.FromFile(new Java.IO.File(path)))));
					return;
				}

				File.Delete(path);

				DisplayMessage(Resource.String.error, Resource.String.not_trustworthy_apk_patch,
					Resource.String.dialog_ok, null);
				NotifyAboutError();
				token.Throw();
			}
			catch (OperationCanceledException)
			{
				SetStatusToAborted();
				progress.Reset();
				IsRunning = false;
			}
		}
	}
}