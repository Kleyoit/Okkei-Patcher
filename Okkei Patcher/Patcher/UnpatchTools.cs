using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using OkkeiPatcher.Extensions;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher.Patcher
{
	internal class UnpatchTools : ToolsBase
	{
		protected override async Task InternalOnInstallSuccess(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				progress.Reset();

				await RestoreObb(progress, token);
				await RestoreSavedata(progress, token);
				await RecoverPreviousSavedataBackup(progress, token);
				ClearBackup();

				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);

				UpdateStatus(Resource.String.unpatch_success);
			}
			catch (OperationCanceledException)
			{
				if (File.Exists(FilePaths[Files.BackupSavedata])) File.Delete(FilePaths[Files.BackupSavedata]);
				if (File.Exists(FilePaths[Files.OriginalSavedata])) File.Delete(FilePaths[Files.OriginalSavedata]);

				SetStatusToAborted();
			}
			finally
			{
				progress.Reset();
				IsRunning = false;
			}
		}

		private async Task RestoreObb(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			UpdateStatus(Resource.String.restore_obb);

			if (!File.Exists(FilePaths[Files.BackupObb]))
			{
				DisplayMessage(Resource.String.error, Resource.String.obb_not_found_unpatch, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				token.Throw();
			}

			await IOUtils.CopyFile(FilePaths[Files.BackupObb], ObbPath, ObbFileName, progress, token)
				.ConfigureAwait(false);
		}

		private async Task RestoreSavedata(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata) return;

			if (File.Exists(FilePaths[Files.BackupSavedata]))
			{
				UpdateStatus(Resource.String.restore_saves);

				await IOUtils.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath, SavedataFileName, progress, token)
					.ConfigureAwait(false);
				return;
			}

			DisplayMessage(Resource.String.warning, Resource.String.saves_backup_not_found, Resource.String.dialog_ok,
				null);
		}

		private async Task RecoverPreviousSavedataBackup(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!File.Exists(FilePaths[Files.BackupSavedata])) return;

			File.Delete(FilePaths[Files.BackupSavedata]);

			if (File.Exists(FilePaths[Files.SAVEDATA_BACKUP]))
			{
				File.Move(FilePaths[Files.SAVEDATA_BACKUP], FilePaths[Files.BackupSavedata]);

				UpdateStatus(Resource.String.write_saves_md5);
				Preferences.Set(Prefkey.savedata_md5.ToString(),
					await MD5Utils.CalculateMD5(FilePaths[Files.BackupSavedata], progress, token)
						.ConfigureAwait(false));
			}
		}

		private static void ClearBackup()
		{
			if (File.Exists(FilePaths[Files.BackupApk])) File.Delete(FilePaths[Files.BackupApk]);
			if (File.Exists(FilePaths[Files.BackupObb])) File.Delete(FilePaths[Files.BackupObb]);
		}

		public void Unpatch(Activity activity, ProcessState processState, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			Task.Run(() => InternalUnpatch(activity, processState, progress, token).OnException(WriteBugReport));
		}

		private async Task InternalUnpatch(Activity activity, ProcessState processState,
			IProgress<ProgressInfo> progress, CancellationToken token)
		{
			IsRunning = true;
			ProcessState = processState;

			try
			{
				if (!CheckIfCouldUnpatch()) token.Throw();

				await BackupSavedata(progress, token);

				ClearStatus();
				progress.MakeIndeterminate();

				if (PackageManagerUtils.IsAppInstalled(ChaosChildPackageName))
				{
					UninstallPatchedPackage(activity);
					return;
				}

				InstallBackupApk(activity, progress);
			}
			catch (OperationCanceledException)
			{
				SetStatusToAborted();
				progress.Reset();
				IsRunning = false;
			}
		}

		private bool CheckIfCouldUnpatch()
		{
			var isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);
			if (!isPatched)
			{
				DisplayMessage(Resource.String.error, Resource.String.error_not_patched, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				return false;
			}

			if (!OkkeiUtils.IsBackupAvailable())
			{
				DisplayMessage(Resource.String.error, Resource.String.backup_not_found, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				return false;
			}

			if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
			{
				DisplayMessage(Resource.String.error, Resource.String.no_free_space_unpatch, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				return false;
			}

			return true;
		}

		private async Task BackupSavedata(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata) return;

			if (File.Exists(FilePaths[Files.OriginalSavedata]))
			{
				UpdateStatus(Resource.String.backup_saves);

				await IOUtils
					.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup, SavedataBackupFileName, progress,
						token)
					.ConfigureAwait(false);
				return;
			}

			DisplayMessage(Resource.String.warning, Resource.String.saves_not_found_unpatch, Resource.String.dialog_ok,
				null);
		}

		private void UninstallPatchedPackage(Activity activity)
		{
			DisplayMessage(Resource.String.attention, Resource.String.uninstall_prompt_unpatch,
				Resource.String.dialog_ok, () => PackageManagerUtils.UninstallPackage(activity, ChaosChildPackageName));
		}

		private void InstallBackupApk(Activity activity, IProgress<ProgressInfo> progress)
		{
			UpdateStatus(Resource.String.installing);
			var installer = new PackageInstaller(progress);
			installer.InstallFailed += PackageInstallerOnInstallFailed;
			DisplayMessage(Resource.String.attention, Resource.String.install_prompt_unpatch, Resource.String.dialog_ok,
				() =>
				{
					MainThread.BeginInvokeOnMainThread(() => installer.InstallPackage(activity,
						Android.Net.Uri.FromFile(new Java.IO.File(FilePaths[Files.BackupApk]))));
				});
		}

		protected override async Task InternalOnUninstallResult(Activity activity, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!CheckUninstallSuccess(progress)) return;

			var apkMd5 = string.Empty;

			if (!IsRunning) return;

			if (Preferences.ContainsKey(Prefkey.backup_apk_md5.ToString()))
				apkMd5 = Preferences.Get(Prefkey.backup_apk_md5.ToString(), string.Empty);
			var path = FilePaths[Files.BackupApk];

			try
			{
				if (!File.Exists(path))
				{
					DisplayMessage(Resource.String.error, Resource.String.apk_not_found_unpatch,
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
					DisplayMessage(Resource.String.attention, Resource.String.install_prompt_unpatch,
						Resource.String.dialog_ok,
						() => MainThread.BeginInvokeOnMainThread(() =>
							installer.InstallPackage(activity, Android.Net.Uri.FromFile(new Java.IO.File(path)))));
					return;
				}

				File.Delete(path);

				DisplayMessage(Resource.String.error, Resource.String.not_trustworthy_apk_unpatch,
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