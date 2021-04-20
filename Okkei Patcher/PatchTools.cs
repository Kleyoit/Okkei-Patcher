using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using ICSharpCode.SharpZipLib.Zip;
using Xamarin.Essentials;
using static SignApk.SignApk;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class PatchTools : ToolsBase
	{
		private OkkeiManifest _manifest;
		private bool _saveDataBackupFromOldPatch;
		private X509Certificate2 _signingCertificate;

		public PatchTools(Utils utils) : base(utils)
		{
		}

		protected override async Task OnInstallSuccessProtected(CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				ResetProgress();

				if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);

				await RestoreSavedataBackup(token);
				await DownloadObb(token);
				Preferences.Set(Prefkey.apk_is_patched.ToString(), true);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.patch_success));
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
				ResetProgress();
				IsRunning = false;
			}
		}

		private async Task RestoreSavedataBackup(CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata || ProcessState.PatchUpdate ||
			    !File.Exists(FilePaths[Files.SAVEDATA_BACKUP])) return;

			if (_saveDataBackupFromOldPatch)
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.restore_old_saves));

				if (File.Exists(FilePaths[Files.BackupSavedata]))
					await UtilsInstance.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath, SavedataFileName, token)
						.ConfigureAwait(false);
			}

			if (File.Exists(FilePaths[Files.BackupSavedata])) File.Delete(FilePaths[Files.BackupSavedata]);
			File.Move(FilePaths[Files.SAVEDATA_BACKUP], FilePaths[Files.BackupSavedata]);
		}

		private async Task DownloadObb(CancellationToken token)
		{
			ResetProgress();

			if (ProcessState.ObbUpdate && File.Exists(FilePaths[Files.ObbToReplace]))
				File.Delete(FilePaths[Files.ObbToReplace]);

			if (!ProcessState.PatchUpdate)
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_obb));

			if (!ProcessState.ObbUpdate && ProcessState.ScriptsUpdate ||
			    File.Exists(FilePaths[Files.ObbToReplace]) &&
			    await UtilsInstance.CompareMD5(Files.ObbToReplace, token).ConfigureAwait(false))
				return;

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.download_obb));

			try
			{
				await UtilsInstance.DownloadFile(_manifest.Obb.URL, ObbPath, ObbFileName, token).ConfigureAwait(false);
			}
			catch (HttpRequestException)
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.http_file_download_error),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				token.Throw();
			}

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_obb_md5));

			var obbHash = await UtilsInstance.CalculateMD5(FilePaths[Files.ObbToReplace], token).ConfigureAwait(false);
			if (obbHash != _manifest.Obb.MD5)
			{
				SetStatusToAborted();
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.hash_obb_mismatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				token.Throw();
			}

			Preferences.Set(Prefkey.downloaded_obb_md5.ToString(), obbHash);
			Preferences.Set(Prefkey.obb_version.ToString(), _manifest.Obb.Version);
		}

		public void Patch(Activity activity, ProcessState processState, OkkeiManifest manifest, CancellationToken token)
		{
			Task.Run(() => PatchPrivate(activity, processState, manifest, token).OnException(WriteBugReport));
		}

		private async Task PatchPrivate(Activity activity, ProcessState processState, OkkeiManifest manifest,
			CancellationToken token)
		{
			IsRunning = true;
			_saveDataBackupFromOldPatch = false;
			ProcessState = processState;
			_manifest = manifest;

			try
			{
				ResetProgress();

				if (!CheckIfCouldApplyPatch()) token.Throw();

				await BackupSavedata(token);

				// If patching for the first time or updating scripts and if there is no patched or backup APK
				if ((!ProcessState.ObbUpdate || ProcessState.ScriptsUpdate) &&
				    (!File.Exists(FilePaths[Files.SignedApk]) || !File.Exists(FilePaths[Files.BackupApk])))
				{
					var originalApkPath = RetrieveOriginalApkPath();
					await RetrieveOriginalApk(token, originalApkPath);
					await BackupApk(token, originalApkPath);

					if (ProcessState.ScriptsUpdate || !ProcessState.ObbUpdate)
					{
						await DownloadScripts(token);

						var extractedScriptsPath = Path.Combine(OkkeiFilesPath, "scripts");
						ExtractScripts(extractedScriptsPath);

						var apkZipFile = new ZipFile(FilePaths[Files.TempApk]);
						ReplaceScripts(extractedScriptsPath, apkZipFile);

						SetIndeterminateProgress();

						Utils.RemoveApkSignature(apkZipFile);
						Utils.UpdateZip(apkZipFile);
						Utils.DeleteFolder(extractedScriptsPath);

						if (token.IsCancellationRequested)
						{
							if (File.Exists(FilePaths[Files.TempApk])) File.Delete(FilePaths[Files.TempApk]);
							token.Throw();
						}

						await SignApk(token);

						if (token.IsCancellationRequested)
						{
							if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);
							token.Throw();
						}
					}
				}

				await BackupObb(token);

				ClearStatus();
				SetIndeterminateProgress();

				if (!ProcessState.PatchUpdate)
				{
					UninstallOriginalPackage(activity);
					return;
				}

				if (ProcessState.ScriptsUpdate)
				{
					await InstallUpdatedApk(activity, token);
					return;
				}

				if (ProcessState.ObbUpdate) FinishPatch(token);
			}
			catch (OperationCanceledException)
			{
				SetStatusToAborted();
				ResetProgress();
				IsRunning = false;
			}
			finally
			{
				if (File.Exists(FilePaths[Files.Scripts])) File.Delete(FilePaths[Files.Scripts]);
			}
		}

		private bool CheckIfCouldApplyPatch()
		{
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.checking));

			var isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

			if (isPatched && !ProcessState.PatchUpdate)
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.error_patched),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				return false;
			}

			if (!Utils.IsAppInstalled(ChaosChildPackageName))
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.cc_not_found),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				return false;
			}

			if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.no_free_space_patch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				return false;
			}

			return true;
		}

		private async Task BackupSavedata(CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata || ProcessState.PatchUpdate) return;

			ResetProgress();

			_saveDataBackupFromOldPatch = File.Exists(FilePaths[Files.BackupSavedata]) &&
			                              await UtilsInstance.CompareMD5(Files.BackupSavedata, token)
				                              .ConfigureAwait(false);

			if (File.Exists(FilePaths[Files.OriginalSavedata]))
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_saves));

				if (await UtilsInstance.CompareMD5(Files.OriginalSavedata, token).ConfigureAwait(false)) return;

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.backup_saves));

				await UtilsInstance
					.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup, SavedataBackupFileName, token)
					.ConfigureAwait(false);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_saves_md5));

				Preferences.Set(Prefkey.savedata_md5.ToString(),
					await UtilsInstance.CalculateMD5(FilePaths[Files.OriginalSavedata], token)
						.ConfigureAwait(false));

				return;
			}

			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
					Application.Context.Resources.GetText(Resource.String.saves_not_found_patch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
		}

		private static string RetrieveOriginalApkPath()
		{
			return Application.Context.PackageManager
				.GetPackageInfo(ChaosChildPackageName, 0)
				.ApplicationInfo
				.PublicSourceDir;
		}

		private async Task RetrieveOriginalApk(CancellationToken token, string originalApkPath)
		{
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.copy_apk));

			await UtilsInstance.CopyFile(originalApkPath, OkkeiFilesPath, TempApkFileName, token).ConfigureAwait(false);
		}

		private async Task BackupApk(CancellationToken token, string originalApkPath)
		{
			if (!File.Exists(FilePaths[Files.TempApk]) || ProcessState.PatchUpdate) return;

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));

			if (File.Exists(FilePaths[Files.BackupApk]) &&
			    await UtilsInstance.CompareMD5(Files.TempApk, token).ConfigureAwait(false))
				return;

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.backup_apk));

			await UtilsInstance.CopyFile(originalApkPath, OkkeiFilesPathBackup, BackupApkFileName, token)
				.ConfigureAwait(false);

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_apk_md5));

			Preferences.Set(Prefkey.backup_apk_md5.ToString(),
				await UtilsInstance.CalculateMD5(FilePaths[Files.BackupApk], token).ConfigureAwait(false));
		}

		private async Task DownloadScripts(CancellationToken token)
		{
			ResetProgress();
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_scripts));

			if (File.Exists(FilePaths[Files.Scripts]) &&
			    await UtilsInstance.CompareMD5(Files.Scripts, token).ConfigureAwait(false))
				return;

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.download_scripts));

			try
			{
				await UtilsInstance.DownloadFile(_manifest.Scripts.URL, OkkeiFilesPath, ScriptsFileName, token)
					.ConfigureAwait(false);
			}
			catch (HttpRequestException)
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.http_file_download_error),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				token.Throw();
			}

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_scripts_md5));

			var scriptsHash = await UtilsInstance.CalculateMD5(FilePaths[Files.Scripts], token).ConfigureAwait(false);
			if (scriptsHash != _manifest.Scripts.MD5)
			{
				SetStatusToAborted();
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.hash_scripts_mismatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				token.Throw();
			}

			Preferences.Set(Prefkey.scripts_md5.ToString(), scriptsHash);
			Preferences.Set(Prefkey.scripts_version.ToString(), _manifest.Scripts.Version);
		}

		private void ExtractScripts(string extractPath)
		{
			ResetProgress();
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.extract_scripts));

			Utils.ExtractZip(FilePaths[Files.Scripts], extractPath);
		}

		private void ReplaceScripts(string scriptsPath, ZipFile apkZipFile)
		{
			ResetProgress();

			var filePaths = Directory.GetFiles(scriptsPath);
			var scriptsCount = filePaths.Length;

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.replace_scripts));

			apkZipFile.BeginUpdate();

			var progress = 0;
			foreach (var scriptfile in filePaths)
			{
				apkZipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));
				++progress;
				OnProgressChanged(this, new ProgressChangedEventArgs(progress, scriptsCount, false));
			}
		}

		private async Task SignApk(CancellationToken token)
		{
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.sign_apk));
			SetIndeterminateProgress();

			_signingCertificate ??= UtilsInstance.GetSigningCertificate();

			var apkToSign = new FileStream(FilePaths[Files.TempApk], FileMode.Open);
			var signedApkStream = new FileStream(FilePaths[Files.SignedApk], FileMode.OpenOrCreate);
			const bool signWholeFile = false;

			SignPackage(apkToSign, _signingCertificate, signedApkStream, signWholeFile);

			apkToSign.Dispose();
			signedApkStream.Dispose();

			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_patched_apk_md5));

			Preferences.Set(Prefkey.signed_apk_md5.ToString(),
				await UtilsInstance.CalculateMD5(FilePaths[Files.SignedApk], token).ConfigureAwait(false));

			if (File.Exists(FilePaths[Files.TempApk])) File.Delete(FilePaths[Files.TempApk]);
		}

		private async Task BackupObb(CancellationToken token)
		{
			if (ProcessState.PatchUpdate) return;

			ResetProgress();

			if (File.Exists(FilePaths[Files.ObbToBackup]))
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_obb));

				if (File.Exists(FilePaths[Files.BackupObb]) &&
				    await UtilsInstance.CompareMD5(Files.ObbToBackup, token).ConfigureAwait(false))
					return;

				OnStatusChanged(this,
					Application.Context.Resources.GetText(Resource.String.backup_obb));

				await UtilsInstance.CopyFile(FilePaths[Files.ObbToBackup], OkkeiFilesPathBackup, ObbFileName, token)
					.ConfigureAwait(false);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_obb_md5));

				Preferences.Set(Prefkey.backup_obb_md5.ToString(),
					await UtilsInstance.CalculateMD5(FilePaths[Files.BackupObb], token).ConfigureAwait(false));

				return;
			}

			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
					Application.Context.Resources.GetText(Resource.String.obb_not_found_patch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null,
					null));
			NotifyAboutError();
			token.Throw();
		}

		private void UninstallOriginalPackage(Activity activity)
		{
			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
					Application.Context.Resources.GetText(Resource.String.uninstall_prompt_patch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
					() => Utils.UninstallPackage(activity, ChaosChildPackageName), null));
		}

		private async Task InstallUpdatedApk(Activity activity, CancellationToken token)
		{
			await OnUninstallResultProtected(activity, token).ConfigureAwait(false);
		}

		private void FinishPatch(CancellationToken token)
		{
			OnInstallSuccess(token);
		}

		protected override async Task OnUninstallResultProtected(Activity activity, CancellationToken token)
		{
			if (!CheckUninstallSuccess()) return;
			
			var apkMd5 = string.Empty;

			if (!IsRunning) return;

			if (Preferences.ContainsKey(Prefkey.signed_apk_md5.ToString()))
				apkMd5 = Preferences.Get(Prefkey.signed_apk_md5.ToString(), string.Empty);
			var path = FilePaths[Files.SignedApk];
			var message = Application.Context.Resources.GetText(Resource.String.install_prompt_patch);

			try
			{
				if (!File.Exists(path))
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.apk_not_found_patch),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
					NotifyAboutError();
					token.Throw();
				}

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));

				var apkFileMd5 = await UtilsInstance.CalculateMD5(path, token).ConfigureAwait(false);

				if (apkMd5 == apkFileMd5)
				{
					SetIndeterminateProgress();
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.installing));
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
							message, Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							() => MainThread.BeginInvokeOnMainThread(() =>
								UtilsInstance.InstallPackage(activity,
									Android.Net.Uri.FromFile(new Java.IO.File(path)))), null));
					return;
				}

				File.Delete(path);

				OnMessageGenerated(this, new MessageBox.Data(
					Application.Context.Resources.GetText(Resource.String.error),
					Application.Context.Resources.GetText(Resource.String.not_trustworthy_apk_patch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				token.Throw();
			}
			catch (OperationCanceledException)
			{
				SetStatusToAborted();
				ResetProgress();
				IsRunning = false;
			}
		}
	}
}