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
			try
			{
				IsRunning = true;

				var backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);
				var backupObb = new Java.IO.File(FilePaths[Files.BackupObb]);
				var backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
				var backupSavedataCopy = new Java.IO.File(FilePaths[Files.SAVEDATA_BACKUP]);
				var appSavedata = new Java.IO.File(FilePaths[Files.OriginalSavedata]);

				try
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.restore_obb));

					if (!backupObb.Exists())
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.obb_not_found_unpatch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						OnErrorOccurred(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					await Utils.CopyFile(backupObb.Path, ObbPath, ObbFileName, token).ConfigureAwait(false);

					if (processSavedata)
					{
						if (backupSavedata.Exists())
						{
							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.restore_saves));

							await Utils.CopyFile(backupSavedata.Path, SavedataPath, SavedataFileName, token)
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
					if (backupApk.Exists()) backupApk.Delete();
					if (backupObb.Exists()) backupObb.Delete();

					if (backupSavedata.Exists())
					{
						backupSavedata.Delete();
						if (backupSavedataCopy.Exists())
						{
							backupSavedataCopy.RenameTo(new Java.IO.File(FilePaths[Files.BackupSavedata]));
							backupSavedataCopy = new Java.IO.File(FilePaths[Files.BackupSavedata]);

							Preferences.Set(Prefkey.savedata_md5.ToString(),
								await Utils.CalculateMD5(backupSavedataCopy.Path, token).ConfigureAwait(false));
						}
					}

					// Finish unpatch
					Preferences.Set(Prefkey.apk_is_patched.ToString(), false);

					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.unpatch_success));
				}
				catch (OperationCanceledException)
				{
					if (backupSavedataCopy.Exists()) backupSavedataCopy.Delete();
					if (appSavedata.Exists()) appSavedata.Delete();

					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
				}
				finally
				{
					backupApk.Dispose();
					backupObb.Dispose();
					backupSavedata.Dispose();
					backupSavedataCopy.Dispose();
					appSavedata.Dispose();

					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}

		public async Task UnpatchTask(Activity activity, bool processSavedata, CancellationToken token)
		{
			try
			{
				IsRunning = true;

				var isPatched =
					Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

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
						if (new Java.IO.File(FilePaths[Files.OriginalSavedata]).Exists())
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


					// Uninstall CHAOS;CHILD and install backup, then restore OBB and save data if checked
					// Install backup immediately if CHAOS;CHILD is not installed
					if (Utils.IsAppInstalled(ChaosChildPackageName))
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
								Application.Context.Resources.GetText(Resource.String.uninstall_prompt_unpatch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								() => Utils.UninstallPackage(activity, ChaosChildPackageName), null));
					}
					else
					{
						OnStatusChanged(null, Application.Context.Resources.GetText(Resource.String.installing));

						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
								Application.Context.Resources.GetText(Resource.String.install_prompt_unpatch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								() =>
								{
									MainThread.BeginInvokeOnMainThread(() => Utils.InstallPackage(activity,
										Android.Net.Uri.FromFile(new Java.IO.File(FilePaths[Files.BackupApk]))));
								}, null));
					}

					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
				}
				catch (OperationCanceledException)
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}
	}
}