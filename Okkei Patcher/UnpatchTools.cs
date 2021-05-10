using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class UnpatchTools : ToolsBase
	{
		public UnpatchTools(Utils utils) : base(utils)
		{
		}

		protected override async Task OnInstallSuccessProtected(CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				ResetProgress();

				await RestoreObb(token);
				await RestoreSavedata(token);
				await RecoverPreviousSavedataBackup(token);
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
				ResetProgress();
				IsRunning = false;
			}
		}

		private async Task RestoreObb(CancellationToken token)
		{
			UpdateStatus(Resource.String.restore_obb);

			if (!File.Exists(FilePaths[Files.BackupObb]))
			{
				DisplayMessage(Resource.String.error, Resource.String.obb_not_found_unpatch, Resource.String.dialog_ok,
					null);
				NotifyAboutError();
				token.Throw();
			}

			await UtilsInstance.CopyFile(FilePaths[Files.BackupObb], ObbPath, ObbFileName, token).ConfigureAwait(false);
		}

		private async Task RestoreSavedata(CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata) return;

			if (File.Exists(FilePaths[Files.BackupSavedata]))
			{
				UpdateStatus(Resource.String.restore_saves);

				await UtilsInstance.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath, SavedataFileName, token)
					.ConfigureAwait(false);
				return;
			}

			DisplayMessage(Resource.String.warning, Resource.String.saves_backup_not_found, Resource.String.dialog_ok,
				null);
		}

		private async Task RecoverPreviousSavedataBackup(CancellationToken token)
		{
			if (!File.Exists(FilePaths[Files.BackupSavedata])) return;

			File.Delete(FilePaths[Files.BackupSavedata]);

			if (File.Exists(FilePaths[Files.SAVEDATA_BACKUP]))
			{
				File.Move(FilePaths[Files.SAVEDATA_BACKUP], FilePaths[Files.BackupSavedata]);

				UpdateStatus(Resource.String.write_saves_md5);
				Preferences.Set(Prefkey.savedata_md5.ToString(),
					await UtilsInstance.CalculateMD5(FilePaths[Files.BackupSavedata], token).ConfigureAwait(false));
			}
		}

		private static void ClearBackup()
		{
			if (File.Exists(FilePaths[Files.BackupApk])) File.Delete(FilePaths[Files.BackupApk]);
			if (File.Exists(FilePaths[Files.BackupObb])) File.Delete(FilePaths[Files.BackupObb]);
		}

		public void Unpatch(Activity activity, ProcessState processState, CancellationToken token)
		{
			Task.Run(() => UnpatchPrivate(activity, processState, token).OnException(WriteBugReport));
		}

		private async Task UnpatchPrivate(Activity activity, ProcessState processState, CancellationToken token)
		{
			IsRunning = true;
			ProcessState = processState;

			try
			{
				if (!CheckIfCouldUnpatch()) token.Throw();

				await BackupSavedata(token);

				ClearStatus();
				SetIndeterminateProgress();

				if (Utils.IsAppInstalled(ChaosChildPackageName))
				{
					UninstallPatchedPackage(activity);
					return;
				}

				InstallBackupApk(activity);
			}
			catch (OperationCanceledException)
			{
				SetStatusToAborted();
				ResetProgress();
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

			if (!Utils.IsBackupAvailable())
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

		private async Task BackupSavedata(CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata) return;

			if (File.Exists(FilePaths[Files.OriginalSavedata]))
			{
				UpdateStatus(Resource.String.backup_saves);

				await UtilsInstance
					.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup, SavedataBackupFileName, token)
					.ConfigureAwait(false);
				return;
			}

			DisplayMessage(Resource.String.warning, Resource.String.saves_not_found_unpatch, Resource.String.dialog_ok,
				null);
		}

		private void UninstallPatchedPackage(Activity activity)
		{
			DisplayMessage(Resource.String.attention, Resource.String.uninstall_prompt_unpatch,
				Resource.String.dialog_ok, () => Utils.UninstallPackage(activity, ChaosChildPackageName));
		}

		private void InstallBackupApk(Activity activity)
		{
			UpdateStatus(Resource.String.installing);

			DisplayMessage(Resource.String.attention, Resource.String.install_prompt_unpatch, Resource.String.dialog_ok,
				() =>
				{
					MainThread.BeginInvokeOnMainThread(() => UtilsInstance.InstallPackage(activity,
						Android.Net.Uri.FromFile(new Java.IO.File(FilePaths[Files.BackupApk]))));
				});
		}

		protected override async Task OnUninstallResultProtected(Activity activity, CancellationToken token)
		{
			if (!CheckUninstallSuccess()) return;

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

				var apkFileMd5 = await UtilsInstance.CalculateMD5(path, token).ConfigureAwait(false);

				if (apkMd5 == apkFileMd5)
				{
					SetIndeterminateProgress();
					UpdateStatus(Resource.String.installing);
					DisplayMessage(Resource.String.attention, Resource.String.install_prompt_unpatch,
						Resource.String.dialog_ok,
						() => MainThread.BeginInvokeOnMainThread(() =>
							UtilsInstance.InstallPackage(activity, Android.Net.Uri.FromFile(new Java.IO.File(path)))));
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
				ResetProgress();
				IsRunning = false;
			}
		}
	}
}