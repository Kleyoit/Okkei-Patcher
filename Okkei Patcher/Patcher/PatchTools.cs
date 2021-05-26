using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using ICSharpCode.SharpZipLib.Zip;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Exceptions;
using OkkeiPatcher.Model.Files;
using OkkeiPatcher.Model.Manifest;
using OkkeiPatcher.Utils;
using OkkeiPatcher.Utils.Extensions;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Patcher
{
	internal class PatchTools : ToolsBase, IInstallHandler, IUninstallHandler
	{
		private OkkeiManifest _manifest;
		private bool _saveDataBackupFromOldPatch;
		private X509Certificate2 _signingCertificate;

		public void NotifyInstallFailed()
		{
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.install_error, Resource.String.dialog_ok, null);
			IsRunning = false;
		}

		public void OnInstallSuccess(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalOnInstallSuccessAsync(progress, token).OnException(WriteBugReport));
		}

		public void OnUninstallResult(Activity activity, IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalOnUninstallResultAsync(activity, progress, token).OnException(WriteBugReport));
		}

		private async Task InternalOnInstallSuccessAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				progress.Reset();

				Files.SignedApk.DeleteIfExists();

				await RestoreSavedataBackupAsync(progress, token);
				await DownloadObbAsync(progress, token);
				Preferences.Set(Prefkey.apk_is_patched.ToString(), true);

				UpdateStatus(Resource.String.patch_success);
			}
			catch (OperationCanceledException)
			{
				if (_saveDataBackupFromOldPatch && ProcessState.ProcessSavedata && !ProcessState.PatchUpdate)
					Files.BackupSavedata.DeleteIfExists();

				SetStatusToAborted();
			}
			finally
			{
				progress.Reset();
				IsRunning = false;
			}
		}

		private async Task RestoreSavedataBackupAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata || ProcessState.PatchUpdate || !Files.TempSavedata.Exists) return;

			if (_saveDataBackupFromOldPatch)
			{
				UpdateStatus(Resource.String.restore_old_saves);

				if (Files.BackupSavedata.Exists)
					await Files.BackupSavedata.CopyToAsync(Files.OriginalSavedata, progress, token)
						.ConfigureAwait(false);
			}

			Files.BackupSavedata.DeleteIfExists();
			Files.TempSavedata.MoveTo(Files.BackupSavedata);
		}

		private async Task DownloadObbAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			progress.Reset();

			if (ProcessState.ObbUpdate) Files.ObbToReplace.DeleteIfExists();

			if (!ProcessState.PatchUpdate) UpdateStatus(Resource.String.compare_obb);

			if (!ProcessState.ObbUpdate && ProcessState.ScriptsUpdate ||
			    await Files.ObbToReplace.VerifyAsync(progress, token).ConfigureAwait(false))
				return;

			UpdateStatus(Resource.String.download_obb);

			try
			{
				await IOUtils.DownloadFileAsync(_manifest.Obb.URL, Files.ObbToReplace, progress, token)
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

			var obbHash = await MD5Utils.ComputeMD5Async(Files.ObbToReplace, progress, token)
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
				InternalPatchAsync(activity, processState, manifest, progress, token).OnException(WriteBugReport));
		}

		private async Task InternalPatchAsync(Activity activity, ProcessState processState, OkkeiManifest manifest,
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

				await BackupSavedataAsync(progress, token);

				// If patching for the first time or updating scripts and if there is no patched or backup APK
				if ((!ProcessState.ObbUpdate || ProcessState.ScriptsUpdate) &&
				    (!Files.SignedApk.Exists || !Files.BackupApk.Exists))
				{
					var originalApkPath = RetrieveOriginalApkPath();
					await RetrieveOriginalApkAsync(originalApkPath, progress, token);
					await BackupApkAsync(originalApkPath, progress, token);

					if (ProcessState.ScriptsUpdate || !ProcessState.ObbUpdate)
					{
						await DownloadScriptsAsync(progress, token);

						var extractedScriptsPath = Path.Combine(OkkeiFilesPath, "scripts");
						ExtractScripts(extractedScriptsPath, progress);

						var apkZipFile = new ZipFile(Files.TempApk.FullPath);
						ReplaceScripts(extractedScriptsPath, apkZipFile, progress);

						progress.MakeIndeterminate();

						ZipUtils.RemoveApkSignature(apkZipFile);
						ZipUtils.UpdateZip(apkZipFile);
						FileUtils.DeleteFolder(extractedScriptsPath);

						if (token.IsCancellationRequested)
						{
							Files.TempApk.DeleteIfExists();
							token.Throw();
						}

						await SignApkAsync(progress, token);

						if (token.IsCancellationRequested)
						{
							Files.SignedApk.DeleteIfExists();
							token.Throw();
						}
					}
				}

				await BackupObbAsync(progress, token);

				ClearStatus();
				progress.MakeIndeterminate();

				if (!ProcessState.PatchUpdate)
				{
					UninstallOriginalPackage(activity);
					return;
				}

				if (ProcessState.ScriptsUpdate)
				{
					await InstallUpdatedApkAsync(activity, progress, token);
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
				Files.Scripts.DeleteIfExists();
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

		private async Task BackupSavedataAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata || ProcessState.PatchUpdate) return;

			progress.Reset();

			_saveDataBackupFromOldPatch = await Files.BackupSavedata.VerifyAsync(progress, token)
				.ConfigureAwait(false);

			if (Files.OriginalSavedata.Exists)
			{
				UpdateStatus(Resource.String.compare_saves);

				if (await Files.OriginalSavedata.VerifyAsync(progress, token).ConfigureAwait(false)) return;

				UpdateStatus(Resource.String.backup_saves);

				await Files.OriginalSavedata.CopyToAsync(Files.TempSavedata, progress, token)
					.ConfigureAwait(false);

				UpdateStatus(Resource.String.write_saves_md5);

				Preferences.Set(Prefkey.savedata_md5.ToString(),
					await MD5Utils.ComputeMD5Async(Files.OriginalSavedata, progress, token)
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

		private async Task RetrieveOriginalApkAsync(string originalApkPath, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			UpdateStatus(Resource.String.copy_apk);

			await IOUtils.CopyFileAsync(originalApkPath, Files.TempApk, progress, token)
				.ConfigureAwait(false);
		}

		private async Task BackupApkAsync(string originalApkPath, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!Files.TempApk.Exists || ProcessState.PatchUpdate) return;

			UpdateStatus(Resource.String.compare_apk);

			if (Files.BackupApk.Exists &&
			    await Files.TempApk.VerifyAsync(progress, token).ConfigureAwait(false))
				return;

			UpdateStatus(Resource.String.backup_apk);

			await IOUtils.CopyFileAsync(originalApkPath, Files.BackupApk, progress, token)
				.ConfigureAwait(false);

			UpdateStatus(Resource.String.write_apk_md5);

			Preferences.Set(Prefkey.backup_apk_md5.ToString(),
				await MD5Utils.ComputeMD5Async(Files.BackupApk, progress, token).ConfigureAwait(false));
		}

		private async Task DownloadScriptsAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			progress.Reset();
			UpdateStatus(Resource.String.compare_scripts);

			if (await Files.Scripts.VerifyAsync(progress, token).ConfigureAwait(false)) return;

			UpdateStatus(Resource.String.download_scripts);

			try
			{
				await IOUtils.DownloadFileAsync(_manifest.Scripts.URL, Files.Scripts, progress, token)
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

			var scriptsHash = await MD5Utils.ComputeMD5Async(Files.Scripts, progress, token)
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

			ZipUtils.ExtractZip(Files.Scripts.FullPath, extractPath);
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

		private async Task SignApkAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			UpdateStatus(Resource.String.sign_apk);
			progress.MakeIndeterminate();

			_signingCertificate ??= CertificateUtils.GetSigningCertificate();

			var apkToSign = new FileStream(Files.TempApk.FullPath, FileMode.Open);
			var signedApkStream = new FileStream(Files.SignedApk.FullPath, FileMode.OpenOrCreate);
			const bool signWholeFile = false;

			SignApk.SignApk.SignPackage(apkToSign, _signingCertificate, signedApkStream, signWholeFile);

			apkToSign.Dispose();
			signedApkStream.Dispose();

			UpdateStatus(Resource.String.write_patched_apk_md5);

			Preferences.Set(Prefkey.signed_apk_md5.ToString(),
				await MD5Utils.ComputeMD5Async(Files.SignedApk, progress, token).ConfigureAwait(false));

			Files.TempApk.DeleteIfExists();
		}

		private async Task BackupObbAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (ProcessState.PatchUpdate) return;

			progress.Reset();

			if (Files.ObbToBackup.Exists)
			{
				UpdateStatus(Resource.String.compare_obb);

				if (Files.BackupObb.Exists &&
				    await Files.ObbToBackup.VerifyAsync(progress, token).ConfigureAwait(false))
					return;

				UpdateStatus(Resource.String.backup_obb);

				await Files.ObbToBackup.CopyToAsync(Files.BackupObb, progress, token)
					.ConfigureAwait(false);

				UpdateStatus(Resource.String.write_obb_md5);

				Preferences.Set(Prefkey.backup_obb_md5.ToString(),
					await MD5Utils.ComputeMD5Async(Files.BackupObb, progress, token).ConfigureAwait(false));

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

		private async Task InstallUpdatedApkAsync(Activity activity, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			await InternalOnUninstallResultAsync(activity, progress, token).ConfigureAwait(false);
		}

		private void FinishPatch(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			OnInstallSuccess(progress, token);
		}

		protected virtual void PackageInstallerOnInstallFailed(object sender, EventArgs e)
		{
			if (!(sender is PackageInstaller installer)) return;
			installer.InstallFailed -= PackageInstallerOnInstallFailed;
			NotifyInstallFailed();
		}

		private bool CheckUninstallSuccess(IProgress<ProgressInfo> progress)
		{
			if (!PackageManagerUtils.IsAppInstalled(ChaosChildPackageName) || ProcessState.ScriptsUpdate) return true;

			progress.Reset();
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.uninstall_error, Resource.String.dialog_ok, null);
			IsRunning = false;
			return false;
		}

		private async Task InternalOnUninstallResultAsync(Activity activity, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!IsRunning || !CheckUninstallSuccess(progress)) return;

			try
			{
				if (!Files.SignedApk.Exists)
				{
					DisplayMessage(Resource.String.error, Resource.String.apk_not_found_patch,
						Resource.String.dialog_ok, null);
					NotifyAboutError();
					token.Throw();
				}

				UpdateStatus(Resource.String.compare_apk);

				if (await Files.SignedApk.VerifyAsync(progress, token))
				{
					progress.MakeIndeterminate();
					var installer = new PackageInstaller(progress);
					installer.InstallFailed += PackageInstallerOnInstallFailed;
					UpdateStatus(Resource.String.installing);
					DisplayMessage(Resource.String.attention, Resource.String.install_prompt_patch,
						Resource.String.dialog_ok,
						() => MainThread.BeginInvokeOnMainThread(() =>
							installer.InstallPackage(activity,
								Android.Net.Uri.FromFile(new Java.IO.File(Files.SignedApk.FullPath)))));
					return;
				}

				Files.SignedApk.DeleteIfExists();

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