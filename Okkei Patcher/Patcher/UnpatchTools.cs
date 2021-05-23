using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using OkkeiPatcher.Extensions;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Files;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Patcher
{
	internal class UnpatchTools : ToolsBase, IInstallHandler, IUninstallHandler
	{
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

		private async Task InternalOnInstallSuccessAsync(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				progress.Reset();

				await RestoreObbAsync(progress, token);
				await RestoreSavedataAsync(progress, token);
				await RecoverPreviousSavedataBackupAsync(progress, token);
				ClearBackup();

				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);

				UpdateStatus(Resource.String.unpatch_success);
			}
			catch (OperationCanceledException)
			{
				Files.BackupSavedata.DeleteIfExists();
				Files.OriginalSavedata.DeleteIfExists();

				SetStatusToAborted();
			}
			finally
			{
				progress.Reset();
				IsRunning = false;
			}
		}

		private async Task RestoreObbAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			UpdateStatus(Resource.String.restore_obb);

			if (!Files.BackupObb.Exists)
			{
				DisplayMessage(Resource.String.error, Resource.String.obb_not_found_unpatch, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				token.Throw();
			}

			await Files.BackupObb.CopyToAsync(Files.ObbToReplace, progress, token)
				.ConfigureAwait(false);
		}

		private async Task RestoreSavedataAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata) return;

			if (Files.BackupSavedata.Exists)
			{
				UpdateStatus(Resource.String.restore_saves);

				await Files.BackupSavedata.CopyToAsync(Files.OriginalSavedata, progress, token)
					.ConfigureAwait(false);
				return;
			}

			DisplayMessage(Resource.String.warning, Resource.String.saves_backup_not_found, Resource.String.dialog_ok,
				null);
		}

		private async Task RecoverPreviousSavedataBackupAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!Files.BackupSavedata.Exists) return;

			Files.BackupSavedata.DeleteIfExists();

			if (Files.TempSavedata.Exists)
			{
				Files.TempSavedata.MoveTo(Files.BackupSavedata);

				UpdateStatus(Resource.String.write_saves_md5);
				Preferences.Set(Prefkey.savedata_md5.ToString(),
					await MD5Utils.ComputeMD5Async(Files.BackupSavedata, progress, token)
						.ConfigureAwait(false));
			}
		}

		private static void ClearBackup()
		{
			Files.BackupApk.DeleteIfExists();
			Files.BackupObb.DeleteIfExists();
		}

		public void Unpatch(Activity activity, ProcessState processState, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			Task.Run(() => InternalUnpatchAsync(activity, processState, progress, token).OnException(WriteBugReport));
		}

		private async Task InternalUnpatchAsync(Activity activity, ProcessState processState,
			IProgress<ProgressInfo> progress, CancellationToken token)
		{
			IsRunning = true;
			ProcessState = processState;

			try
			{
				if (!CheckIfCouldUnpatch()) token.Throw();

				await BackupSavedataAsync(progress, token);

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

		private async Task BackupSavedataAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata) return;

			if (Files.OriginalSavedata.Exists)
			{
				UpdateStatus(Resource.String.backup_saves);

				await Files.OriginalSavedata.CopyToAsync(Files.TempSavedata, progress, token)
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
						Android.Net.Uri.FromFile(new Java.IO.File(Files.BackupApk.FullPath))));
				});
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
				if (!Files.BackupApk.Exists)
				{
					DisplayMessage(Resource.String.error, Resource.String.apk_not_found_unpatch,
						Resource.String.dialog_ok, null);
					NotifyAboutError();
					token.Throw();
				}

				UpdateStatus(Resource.String.compare_apk);

				if (await Files.BackupApk.VerifyAsync(progress, token))
				{
					progress.MakeIndeterminate();
					var installer = new PackageInstaller(progress);
					installer.InstallFailed += PackageInstallerOnInstallFailed;
					UpdateStatus(Resource.String.installing);
					DisplayMessage(Resource.String.attention, Resource.String.install_prompt_unpatch,
						Resource.String.dialog_ok,
						() => MainThread.BeginInvokeOnMainThread(() =>
							installer.InstallPackage(activity,
								Android.Net.Uri.FromFile(new Java.IO.File(Files.BackupApk.FullPath)))));
					return;
				}

				Files.BackupApk.DeleteIfExists();

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