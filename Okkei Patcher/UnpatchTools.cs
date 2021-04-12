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

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.unpatch_success));
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
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.restore_obb));

			if (!File.Exists(FilePaths[Files.BackupObb]))
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.obb_not_found_unpatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				Utils.ThrowOperationCanceledException(token);
			}

			await UtilsInstance.CopyFile(FilePaths[Files.BackupObb], ObbPath, ObbFileName, token).ConfigureAwait(false);
		}

		private async Task RestoreSavedata(CancellationToken token)
		{
			if (!ProcessState.ProcessSavedata) return;

			if (File.Exists(FilePaths[Files.BackupSavedata]))
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.restore_saves));

				await UtilsInstance.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath, SavedataFileName, token)
					.ConfigureAwait(false);
				return;
			}

			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
					Application.Context.Resources.GetText(Resource.String.saves_backup_not_found),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
		}

		private async Task RecoverPreviousSavedataBackup(CancellationToken token)
		{
			if (!File.Exists(FilePaths[Files.BackupSavedata])) return;

			File.Delete(FilePaths[Files.BackupSavedata]);

			if (File.Exists(FilePaths[Files.SAVEDATA_BACKUP]))
			{
				File.Move(FilePaths[Files.SAVEDATA_BACKUP], FilePaths[Files.BackupSavedata]);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_saves_md5));
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
				if (!CheckIfCouldUnpatch()) Utils.ThrowOperationCanceledException(token);

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
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.error_not_patched),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
						null, null));
				NotifyAboutError();
				return false;
			}

			if (!Utils.IsBackupAvailable())
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.backup_not_found),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
						null, null));
				NotifyAboutError();
				return false;
			}

			if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
			{
				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.no_free_space_unpatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
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
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.backup_saves));

				await UtilsInstance
					.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup, SavedataBackupFileName, token)
					.ConfigureAwait(false);
				return;
			}

			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
					Application.Context.Resources.GetText(Resource.String.saves_not_found_unpatch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
		}

		private void UninstallPatchedPackage(Activity activity)
		{
			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
					Application.Context.Resources.GetText(Resource.String.uninstall_prompt_unpatch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
					() => Utils.UninstallPackage(activity, ChaosChildPackageName), null));
		}

		private void InstallBackupApk(Activity activity)
		{
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.installing));

			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
					Application.Context.Resources.GetText(Resource.String.install_prompt_unpatch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
					() =>
					{
						MainThread.BeginInvokeOnMainThread(() => UtilsInstance.InstallPackage(activity,
							Android.Net.Uri.FromFile(new Java.IO.File(FilePaths[Files.BackupApk]))));
					}, null));
		}

		protected override async Task OnUninstallResultProtected(Activity activity, CancellationToken token)
		{
			if (!CheckUninstallSuccess()) return;

			// Install APK
			var apkMd5 = string.Empty;

			if (!IsRunning) return;

			if (Preferences.ContainsKey(Prefkey.backup_apk_md5.ToString()))
				apkMd5 = Preferences.Get(Prefkey.backup_apk_md5.ToString(), string.Empty);
			var path = FilePaths[Files.BackupApk];
			var message = Application.Context.Resources.GetText(Resource.String.install_prompt_unpatch);

			try
			{
				if (!File.Exists(path))
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.apk_not_found_unpatch),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
					NotifyAboutError();
					Utils.ThrowOperationCanceledException(token);
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
					Application.Context.Resources.GetText(Resource.String.not_trustworthy_apk_unpatch),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
				NotifyAboutError();
				Utils.ThrowOperationCanceledException(token);
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