using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.DTO.Impl.English;
using OkkeiPatcher.Model.Files;
using OkkeiPatcher.Utils;
using OkkeiPatcher.Utils.Extensions;
using Xamarin.Essentials;

namespace OkkeiPatcher.Core.Impl.English
{
	internal class Unpatcher : Base.Unpatcher
	{
		private PatchUpdates PatchUpdates => ProcessState.PatchUpdates as PatchUpdates;

		protected override async Task InternalOnInstallSuccessAsync(IProgress<ProgressInfo> progress,
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

				Preferences.Set(AppPrefkey.apk_is_patched.ToString(), false);

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
				DisplayErrorMessage(Resource.String.error, Resource.String.obb_not_found_unpatch,
					Resource.String.dialog_ok);
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

			DisplayMessage(Resource.String.warning, Resource.String.saves_backup_not_found, Resource.String.dialog_ok);
		}

		private async Task RecoverPreviousSavedataBackupAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!Files.BackupSavedata.Exists) return;

			Files.BackupSavedata.DeleteIfExists();

			if (Files.TempSavedata.Exists)
			{
				Files.TempSavedata.MoveTo(Files.BackupSavedata);

				UpdateStatus(Resource.String.write_saves_md5);
				Preferences.Set(FilePrefkey.savedata_md5.ToString(),
					await Md5Utils.ComputeMd5Async(Files.BackupSavedata, progress, token)
						.ConfigureAwait(false));
			}
		}

		private static void ClearBackup()
		{
			Files.BackupApk.DeleteIfExists();
			Files.BackupObb.DeleteIfExists();
		}

		protected override async Task InternalUnpatchAsync(ProcessState processState, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			IsRunning = true;
			ProcessState = processState;

			try
			{
				if (!CanUnpatch()) token.Throw();

				await BackupSavedataAsync(progress, token);

				ClearStatus();
				progress.MakeIndeterminate();

				if (PackageManagerUtils.IsAppInstalled(ChaosChildPackageName))
				{
					UninstallPatchedPackage();
					return;
				}

				InstallBackupApk();
			}
			catch (OperationCanceledException)
			{
				Files.TempSavedata.DeleteIfExists();

				SetStatusToAborted();
				progress.Reset();
				IsRunning = false;
			}
		}

		private bool CanUnpatch()
		{
			bool isPatched = Preferences.Get(AppPrefkey.apk_is_patched.ToString(), false);
			if (!isPatched)
			{
				DisplayErrorMessage(Resource.String.error, Resource.String.error_not_patched,
					Resource.String.dialog_ok);
				return false;
			}

			if (!IsBackupAvailable())
			{
				DisplayErrorMessage(Resource.String.error, Resource.String.backup_not_found, Resource.String.dialog_ok);
				return false;
			}

			if (FileUtils.IsEnoughSpace()) return true;

			DisplayErrorMessage(Resource.String.error, Resource.String.no_free_space_unpatch,
				Resource.String.dialog_ok);
			return false;
		}

		private static bool IsBackupAvailable()
		{
			return Files.BackupApk.Exists && Files.BackupObb.Exists;
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

			DisplayMessage(Resource.String.warning, Resource.String.saves_not_found_unpatch, Resource.String.dialog_ok);
		}

		private void UninstallPatchedPackage()
		{
			DisplayUninstallMessage(Resource.String.attention, Resource.String.uninstall_prompt_unpatch,
				Resource.String.dialog_ok, ChaosChildPackageName);
		}

		private void InstallBackupApk()
		{
			UpdateStatus(Resource.String.installing);
			DisplayInstallMessage(Resource.String.attention, Resource.String.install_prompt_unpatch,
				Resource.String.dialog_ok,
				Files.BackupApk.FullPath);
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
				if (!Files.BackupApk.Exists)
				{
					DisplayErrorMessage(Resource.String.error, Resource.String.apk_not_found_unpatch,
						Resource.String.dialog_ok);
					token.Throw();
				}

				UpdateStatus(Resource.String.compare_apk);

				if (await Files.BackupApk.VerifyAsync(progress, token))
				{
					progress.MakeIndeterminate();
					UpdateStatus(Resource.String.installing);
					DisplayInstallMessage(Resource.String.attention, Resource.String.install_prompt_unpatch,
						Resource.String.dialog_ok, Files.BackupApk.FullPath);
					return;
				}

				Files.BackupApk.DeleteIfExists();

				DisplayErrorMessage(Resource.String.error, Resource.String.not_trustworthy_apk_unpatch,
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