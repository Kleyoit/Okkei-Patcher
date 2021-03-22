using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class UnpatchTools : ToolsBase
	{
		protected override async Task Finish(bool processSavedata, bool scriptsUpdate, bool obbUpdate,
			CancellationToken token)
		{
			if (!IsRunning) return;

			try
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.restore_obb));

				if (!System.IO.File.Exists(FilePaths[Files.BackupObb]))
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.obb_not_found_unpatch),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
					OnErrorOccurred(this, EventArgs.Empty);
					throw new OperationCanceledException("The operation was canceled.", token);
				}

				await UtilsInstance.Value.CopyFile(FilePaths[Files.BackupObb], ObbPath, ObbFileName, token)
					.ConfigureAwait(false);

				if (processSavedata)
				{
					if (System.IO.File.Exists(FilePaths[Files.BackupSavedata]))
					{
						OnStatusChanged(this,
							Application.Context.Resources.GetText(Resource.String.restore_saves));

						await UtilsInstance.Value.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath,
								SavedataFileName, token)
							.ConfigureAwait(false);
					}
					else
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
								Application.Context.Resources.GetText(Resource.String.saves_backup_not_found),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
					}
				}

				// Clear backup
				if (System.IO.File.Exists(FilePaths[Files.BackupApk]))
					System.IO.File.Delete(FilePaths[Files.BackupApk]);
				if (System.IO.File.Exists(FilePaths[Files.BackupObb]))
					System.IO.File.Delete(FilePaths[Files.BackupObb]);

				if (System.IO.File.Exists(FilePaths[Files.BackupSavedata]))
				{
					System.IO.File.Delete(FilePaths[Files.BackupSavedata]);
					if (System.IO.File.Exists(FilePaths[Files.SAVEDATA_BACKUP]))
					{
						System.IO.File.Move(FilePaths[Files.SAVEDATA_BACKUP], FilePaths[Files.BackupSavedata]);

						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_saves_md5));
						Preferences.Set(Prefkey.savedata_md5.ToString(),
							await UtilsInstance.Value.CalculateMD5(FilePaths[Files.BackupSavedata], token)
								.ConfigureAwait(false));
					}
				}

				// Finish unpatch
				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.unpatch_success));
			}
			catch (OperationCanceledException)
			{
				if (System.IO.File.Exists(FilePaths[Files.BackupSavedata]))
					System.IO.File.Delete(FilePaths[Files.BackupSavedata]);
				if (System.IO.File.Exists(FilePaths[Files.OriginalSavedata]))
					System.IO.File.Delete(FilePaths[Files.OriginalSavedata]);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
			}
			finally
			{
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
				IsRunning = false;
			}
		}

		public void Start(Activity activity, bool processSavedata, CancellationToken token)
		{
			Task.Run(() => StartPrivate(activity, processSavedata, token).OnException(WriteBugReport));
		}

		private async Task StartPrivate(Activity activity, bool processSavedata, CancellationToken token)
		{
			IsRunning = true;

			var isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

			try
			{
				if (!isPatched)
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.error_not_patched),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
					OnErrorOccurred(this, EventArgs.Empty);
					throw new OperationCanceledException("The operation was canceled.", token);
				}

				if (!Utils.IsBackupAvailable())
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.backup_not_found),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
					OnErrorOccurred(this, EventArgs.Empty);
					throw new OperationCanceledException("The operation was canceled.", token);
				}

				if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.no_free_space_unpatch),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
					OnErrorOccurred(this, EventArgs.Empty);
					throw new OperationCanceledException("The operation was canceled.", token);
				}

				if (processSavedata)
				{
					if (System.IO.File.Exists(FilePaths[Files.OriginalSavedata]))
					{
						// Backup save data
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.backup_saves));

						await UtilsInstance.Value.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup,
							SavedataBackupFileName, token).ConfigureAwait(false);
					}
					else
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
								Application.Context.Resources.GetText(Resource.String.saves_not_found_unpatch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
					}
				}

				OnStatusChanged(this, string.Empty);
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, true));


				// Uninstall CHAOS;CHILD and install backup, then restore OBB and save data if checked
				// Install backup immediately if CHAOS;CHILD is not installed
				if (UtilsInstance.Value.IsAppInstalled(ChaosChildPackageName))
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
							Application.Context.Resources.GetText(Resource.String.uninstall_prompt_unpatch),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							() => UtilsInstance.Value.UninstallPackage(activity, ChaosChildPackageName), null));
					return;
				}

				OnStatusChanged(null, Application.Context.Resources.GetText(Resource.String.installing));

				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
						Application.Context.Resources.GetText(Resource.String.install_prompt_unpatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
						() =>
						{
							MainThread.BeginInvokeOnMainThread(() => UtilsInstance.Value.InstallPackage(activity,
								Android.Net.Uri.FromFile(new Java.IO.File(FilePaths[Files.BackupApk]))));
						}, null));
			}
			catch (OperationCanceledException)
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
				IsRunning = false;
			}
		}

		public override async Task OnUninstallResult(Activity activity, bool scriptsUpdate, CancellationToken token)
		{
			if (!CheckUninstallSuccess(scriptsUpdate)) return;

			// Install APK
			var apkMd5 = string.Empty;

			if (!IsRunning) return;

			if (Preferences.ContainsKey(Prefkey.backup_apk_md5.ToString()))
				apkMd5 = Preferences.Get(Prefkey.backup_apk_md5.ToString(), string.Empty);
			var path = FilePaths[Files.BackupApk];
			var message = Application.Context.Resources.GetText(Resource.String.install_prompt_unpatch);

			try
			{
				if (System.IO.File.Exists(path))
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));

					var apkFileMd5 = await UtilsInstance.Value.CalculateMD5(path, token).ConfigureAwait(false);

					if (apkMd5 == apkFileMd5)
					{
						OnStatusChanged(this,
							Application.Context.Resources.GetText(Resource.String.installing));

						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
								message, Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								() => MainThread.BeginInvokeOnMainThread(() =>
									UtilsInstance.Value.InstallPackage(activity,
										Android.Net.Uri.FromFile(new Java.IO.File(path)))),
								null));
						return;
					}

					System.IO.File.Delete(path);

					OnMessageGenerated(this, new MessageBox.Data(
						Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.not_trustworthy_apk_unpatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok),
						null,
						null, null));

					OnErrorOccurred(this, EventArgs.Empty);
					throw new OperationCanceledException("The operation was canceled.", token);
				}

				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.apk_not_found_unpatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
						null, null));

				OnErrorOccurred(this, EventArgs.Empty);
				throw new OperationCanceledException("The operation was canceled.", token);
			}
			catch (OperationCanceledException)
			{
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
				IsRunning = false;
			}
		}
	}
}