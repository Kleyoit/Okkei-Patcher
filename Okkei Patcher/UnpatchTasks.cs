using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class UnpatchTasks : BaseTasks
	{
		private static readonly Lazy<UnpatchTasks> instance = new Lazy<UnpatchTasks>(() => new UnpatchTasks());

		private UnpatchTasks()
		{
		}

		public static bool IsInstantiated => instance.IsValueCreated;

		public static UnpatchTasks Instance => instance.Value;

		public async Task RestoreFiles(bool processSavedata, CancellationToken token)
		{
			IsRunning = true;

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

				await Utils.CopyFile(FilePaths[Files.BackupObb], ObbPath, ObbFileName, token).ConfigureAwait(false);

				if (processSavedata)
				{
					if (System.IO.File.Exists(FilePaths[Files.BackupSavedata]))
					{
						OnStatusChanged(this,
							Application.Context.Resources.GetText(Resource.String.restore_saves));

						await Utils.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath, SavedataFileName, token)
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
							await Utils.CalculateMD5(FilePaths[Files.BackupSavedata], token).ConfigureAwait(false));
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

		public async Task UnpatchTask(Activity activity, bool processSavedata, CancellationToken token)
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

						await Utils.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup,
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
				if (Utils.IsAppInstalled(ChaosChildPackageName))
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
							Application.Context.Resources.GetText(Resource.String.uninstall_prompt_unpatch),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							() => Utils.UninstallPackage(activity, ChaosChildPackageName), null));
					return;
				}

				OnStatusChanged(null, Application.Context.Resources.GetText(Resource.String.installing));

				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
						Application.Context.Resources.GetText(Resource.String.install_prompt_unpatch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
						() =>
						{
							MainThread.BeginInvokeOnMainThread(() => Utils.InstallPackage(activity,
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
	}
}