using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using OkkeiPatcher.Model;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.DTO.Impl.English;
using OkkeiPatcher.Model.Exceptions;
using OkkeiPatcher.Model.Files;
using OkkeiPatcher.Model.Manifest;
using OkkeiPatcher.Utils;
using OkkeiPatcher.Utils.Extensions;
using Xamarin.Essentials;
using FileInfo = OkkeiPatcher.Model.Manifest.FileInfo;

namespace OkkeiPatcher.Core.Impl.English
{
	internal class Patcher : Base.Patcher
	{
		private Dictionary<string, FileInfo> PatchFiles => Manifest.Patches[Language.English];
		private PatchUpdates PatchUpdates => ProcessState.PatchUpdates as PatchUpdates;

		protected override async Task InternalOnInstallSuccessAsync(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				progress.Reset();

				Files.SignedApk.DeleteIfExists();

				await RestoreSavedataBackupAsync(progress, token);
				await DownloadObbAsync(progress, token);
				Preferences.Set(AppPrefkey.apk_is_patched.ToString(), true);

				UpdateStatus(Resource.String.patch_success);
			}
			catch (OperationCanceledException)
			{
				if (SaveDataBackupFromOldPatch && ProcessState.ProcessSavedata && !PatchUpdates.Available)
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
			if (!ProcessState.ProcessSavedata || PatchUpdates.Available || !Files.TempSavedata.Exists) return;

			if (SaveDataBackupFromOldPatch)
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

			if (PatchUpdates.Obb) Files.ObbToReplace.DeleteIfExists();

			if (!PatchUpdates.Available) UpdateStatus(Resource.String.compare_obb);

			if (!PatchUpdates.Obb && PatchUpdates.Scripts ||
			    await Files.ObbToReplace.VerifyAsync(progress, token).ConfigureAwait(false))
				return;

			UpdateStatus(Resource.String.download_obb);

			try
			{
				await IOUtils.DownloadFileAsync(PatchFiles[PatchFile.Obb.ToString()].URL, Files.ObbToReplace, progress,
						token)
					.ConfigureAwait(false);
			}
			catch (HttpStatusCodeException ex)
			{
				DisplayErrorMessage(Resource.String.error, Resource.String.http_file_access_error,
					Resource.String.dialog_ok,
					ex.StatusCode.ToString());
				token.Throw();
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
			{
				DisplayErrorMessage(Resource.String.error, Resource.String.http_file_download_error,
					Resource.String.dialog_ok);
				token.Throw();
			}

			UpdateStatus(Resource.String.write_obb_md5);

			string obbHash = await Md5Utils.ComputeMd5Async(Files.ObbToReplace, progress, token)
				.ConfigureAwait(false);
			if (obbHash != PatchFiles[PatchFile.Obb.ToString()].MD5)
			{
				SetStatusToAborted();
				DisplayErrorMessage(Resource.String.error, Resource.String.hash_obb_mismatch,
					Resource.String.dialog_ok);
				token.Throw();
			}

			Preferences.Set(FilePrefkey.downloaded_obb_md5.ToString(), obbHash);
			Preferences.Set(FileVersionPrefkey.obb_version.ToString(), PatchFiles[PatchFile.Obb.ToString()].Version);
		}

		protected override async Task InternalPatchAsync(ProcessState processState, OkkeiManifest manifest,
			IProgress<ProgressInfo> progress, CancellationToken token)
		{
			IsRunning = true;
			SaveDataBackupFromOldPatch = false;
			ProcessState = processState;
			Manifest = manifest;

			try
			{
				progress.Reset();

				if (!CanPatch()) token.Throw();

				await BackupSavedataAsync(progress, token);

				// If patching for the first time or updating scripts and if there is no patched or backup APK
				if ((!PatchUpdates.Obb || PatchUpdates.Scripts) && (!Files.SignedApk.Exists || !Files.BackupApk.Exists))
				{
					string originalApkPath = RetrieveOriginalApkPath();
					await RetrieveOriginalApkAsync(originalApkPath, progress, token);
					await BackupApkAsync(originalApkPath, progress, token);

					if (PatchUpdates.Scripts || !PatchUpdates.Obb)
					{
						await DownloadScriptsAsync(progress, token);

						string extractedScriptsPath = Path.Combine(OkkeiPaths.Root, "scripts");
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

				if (!PatchUpdates.Available)
				{
					UninstallOriginalPackage();
					return;
				}

				if (PatchUpdates.Scripts)
				{
					await InstallUpdatedApkAsync(progress, token);
					return;
				}

				if (PatchUpdates.Obb) FinishPatch(progress, token);
			}
			catch (OperationCanceledException)
			{
				Files.TempSavedata.DeleteIfExists();

				SetStatusToAborted();
				progress.Reset();
				IsRunning = false;
			}
			finally
			{
				Files.Scripts.DeleteIfExists();
			}
		}

		private bool CanPatch()
		{
			UpdateStatus(Resource.String.checking);

			bool isPatched = Preferences.Get(AppPrefkey.apk_is_patched.ToString(), false);

			if (isPatched && !PatchUpdates.Available)
			{
				DisplayErrorMessage(Resource.String.error, Resource.String.error_patched, Resource.String.dialog_ok);
				return false;
			}

			if (!PackageManagerUtils.IsAppInstalled(ChaosChildPackageName))
			{
				DisplayErrorMessage(Resource.String.error, Resource.String.cc_not_found, Resource.String.dialog_ok);
				return false;
			}

			if (FileUtils.IsEnoughSpace()) return true;

			DisplayErrorMessage(Resource.String.error, Resource.String.no_free_space_patch, Resource.String.dialog_ok);
			return false;
		}

		private async Task BackupSavedataAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata || PatchUpdates.Available) return;

			progress.Reset();

			SaveDataBackupFromOldPatch = await Files.BackupSavedata.VerifyAsync(progress, token)
				.ConfigureAwait(false);

			if (Files.OriginalSavedata.Exists)
			{
				UpdateStatus(Resource.String.compare_saves);

				if (await Files.OriginalSavedata.VerifyAsync(progress, token).ConfigureAwait(false)) return;

				UpdateStatus(Resource.String.backup_saves);

				await Files.OriginalSavedata.CopyToAsync(Files.TempSavedata, progress, token)
					.ConfigureAwait(false);

				UpdateStatus(Resource.String.write_saves_md5);

				Preferences.Set(FilePrefkey.savedata_md5.ToString(),
					await Md5Utils.ComputeMd5Async(Files.OriginalSavedata, progress, token)
						.ConfigureAwait(false));

				return;
			}

			DisplayMessage(Resource.String.warning, Resource.String.saves_not_found_patch, Resource.String.dialog_ok);
		}

		private static string RetrieveOriginalApkPath()
		{
			return PackageManagerUtils.GetPackagePublicSourceDir(ChaosChildPackageName);
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
			if (!Files.TempApk.Exists || PatchUpdates.Available) return;

			UpdateStatus(Resource.String.compare_apk);

			if (Files.BackupApk.Exists &&
			    await Files.TempApk.VerifyAsync(progress, token).ConfigureAwait(false))
				return;

			UpdateStatus(Resource.String.backup_apk);

			await IOUtils.CopyFileAsync(originalApkPath, Files.BackupApk, progress, token)
				.ConfigureAwait(false);

			UpdateStatus(Resource.String.write_apk_md5);

			Preferences.Set(FilePrefkey.backup_apk_md5.ToString(),
				await Md5Utils.ComputeMd5Async(Files.BackupApk, progress, token).ConfigureAwait(false));
		}

		private async Task DownloadScriptsAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			progress.Reset();
			UpdateStatus(Resource.String.compare_scripts);

			if (await Files.Scripts.VerifyAsync(progress, token).ConfigureAwait(false)) return;

			UpdateStatus(Resource.String.download_scripts);

			try
			{
				await IOUtils.DownloadFileAsync(PatchFiles[PatchFile.Scripts.ToString()].URL, Files.Scripts, progress,
						token)
					.ConfigureAwait(false);
			}
			catch (HttpStatusCodeException ex)
			{
				SetStatusToAborted();
				DisplayErrorMessage(Resource.String.error, Resource.String.http_file_access_error,
					Resource.String.dialog_ok, ex.StatusCode.ToString());
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
			{
				DisplayErrorMessage(Resource.String.error, Resource.String.http_file_download_error,
					Resource.String.dialog_ok);
				token.Throw();
			}

			UpdateStatus(Resource.String.write_scripts_md5);

			string scriptsHash = await Md5Utils.ComputeMd5Async(Files.Scripts, progress, token)
				.ConfigureAwait(false);
			if (scriptsHash != PatchFiles[PatchFile.Scripts.ToString()].MD5)
			{
				SetStatusToAborted();
				DisplayErrorMessage(Resource.String.error, Resource.String.hash_scripts_mismatch,
					Resource.String.dialog_ok);
				token.Throw();
			}

			Preferences.Set(FilePrefkey.scripts_md5.ToString(), scriptsHash);
			Preferences.Set(FileVersionPrefkey.scripts_version.ToString(),
				PatchFiles[PatchFile.Scripts.ToString()].Version);
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

			string[] filePaths = Directory.GetFiles(scriptsPath);
			int scriptsCount = filePaths.Length;

			UpdateStatus(Resource.String.replace_scripts);

			apkZipFile.BeginUpdate();

			var currentProgress = 0;
			foreach (string scriptfile in filePaths)
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

			SigningCertificate ??= CertificateUtils.GetSigningCertificate();

			var apkToSign = new FileStream(Files.TempApk.FullPath, FileMode.Open);
			var signedApkStream = new FileStream(Files.SignedApk.FullPath, FileMode.OpenOrCreate);
			const bool signWholeFile = false;

			SignApk.SignApk.SignPackage(apkToSign, SigningCertificate, signedApkStream, signWholeFile);

			apkToSign.Dispose();
			signedApkStream.Dispose();

			UpdateStatus(Resource.String.write_patched_apk_md5);

			Preferences.Set(FilePrefkey.signed_apk_md5.ToString(),
				await Md5Utils.ComputeMd5Async(Files.SignedApk, progress, token).ConfigureAwait(false));

			Files.TempApk.DeleteIfExists();
		}

		private async Task BackupObbAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (PatchUpdates.Available) return;

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

				Preferences.Set(FilePrefkey.backup_obb_md5.ToString(),
					await Md5Utils.ComputeMd5Async(Files.BackupObb, progress, token).ConfigureAwait(false));

				return;
			}

			DisplayErrorMessage(Resource.String.error, Resource.String.obb_not_found_patch, Resource.String.dialog_ok);
			token.Throw();
		}

		private void UninstallOriginalPackage()
		{
			DisplayUninstallMessage(Resource.String.attention, Resource.String.uninstall_prompt_patch,
				Resource.String.dialog_ok, ChaosChildPackageName);
		}

		private async Task InstallUpdatedApkAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			await InternalOnUninstallResultAsync(progress, token).ConfigureAwait(false);
		}

		private void FinishPatch(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			OnInstallSuccess(progress, token);
		}

		protected override async Task InternalOnUninstallResultAsync(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!IsRunning) return;
			if (PackageManagerUtils.IsAppInstalled(ChaosChildPackageName) && !PatchUpdates.Scripts)
			{
				OnUninstallFail(progress);
				return;
			}

			try
			{
				if (!Files.SignedApk.Exists)
				{
					DisplayErrorMessage(Resource.String.error, Resource.String.apk_not_found_patch,
						Resource.String.dialog_ok);
					token.Throw();
				}

				UpdateStatus(Resource.String.compare_apk);

				if (await Files.SignedApk.VerifyAsync(progress, token))
				{
					progress.MakeIndeterminate();
					UpdateStatus(Resource.String.installing);
					DisplayInstallMessage(Resource.String.attention, Resource.String.install_prompt_patch,
						Resource.String.dialog_ok, Files.SignedApk.FullPath);
					return;
				}

				Files.SignedApk.DeleteIfExists();

				DisplayErrorMessage(Resource.String.error, Resource.String.not_trustworthy_apk_patch,
					Resource.String.dialog_ok);
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