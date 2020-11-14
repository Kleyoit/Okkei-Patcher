using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class UnpatchTasks : INotifyPropertyChanged
	{
		private static readonly Lazy<UnpatchTasks> instance = new Lazy<UnpatchTasks>(() => new UnpatchTasks());

		private UnpatchTasks()
		{
			Utils.StatusChanged += UtilsOnStatusChanged;
			Utils.ProgressChanged += UtilsOnProgressChanged;
		}

		public static UnpatchTasks Instance => instance.Value;

		private bool isAnyRunning = false;

		public bool IsAnyRunning
		{
			get => isAnyRunning;
			set
			{
				if (value != isAnyRunning)
				{
					isAnyRunning = value;
					NotifyPropertyChanged();
				}
			}
		}

		public event EventHandler<StatusChangedEventArgs> StatusChanged;
		public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public event PropertyChangedEventHandler PropertyChanged;

		private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") =>
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		private void UtilsOnStatusChanged(object sender, StatusChangedEventArgs e) =>
			StatusChanged?.Invoke(this, e);

		private void UtilsOnProgressChanged(object sender, ProgressChangedEventArgs e) =>
			ProgressChanged?.Invoke(this, e);

		public async Task RestoreFiles(bool processSavedata)
		{
			try
			{
				this.IsAnyRunning = true;

				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				var backupedSavedata = new Java.IO.File(FilePaths[Files.SAVEDATA_BACKUP]);
				var appSavedata = new Java.IO.File(FilePaths[Files.OriginalSavedata]);

				try
				{
					StatusChanged?.Invoke(null,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.restore_obb),
							MessageBox.Data.Empty));

					await Utils.CopyFile(FilePaths[Files.BackupObb], ObbPath, ObbFileName);
					token.ThrowIfCancellationRequested();

					if (processSavedata)
					{
						if (new Java.IO.File(FilePaths[Files.BackupSavedata]).Exists())
						{
							StatusChanged?.Invoke(null,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.restore_saves),
									MessageBox.Data.Empty));

							await Utils.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath,
								SavedataFileName);
							token.ThrowIfCancellationRequested();
						}
						else
						{
							StatusChanged?.Invoke(null,
								new StatusChangedEventArgs(null,
									new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
										Application.Context.Resources.GetText(Resource.String.saves_backup_not_found),
										MessageBox.Code.OK)));
						}
					}

					// Clear backup
					Java.IO.File apk = new Java.IO.File(FilePaths[Files.BackupApk]);
					if (apk.Exists()) apk.Delete();

					Java.IO.File obb = new Java.IO.File(FilePaths[Files.BackupObb]);
					if (obb.Exists()) obb.Delete();

					Java.IO.File savedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
					if (savedata.Exists())
					{
						savedata.Delete();
						if (backupedSavedata.Exists())
						{
							backupedSavedata.RenameTo(new Java.IO.File(FilePaths[Files.BackupSavedata]));
							backupedSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);

							Preferences.Set(Prefkey.savedata_md5.ToString(), Utils.CalculateMD5(backupedSavedata.Path));
						}
					}

					// Finish unpatch
					apk.Dispose();
					obb.Dispose();
					savedata.Dispose();
					backupedSavedata.Dispose();
					appSavedata.Dispose();

					Preferences.Set(Prefkey.apk_is_patched.ToString(), false);

					StatusChanged?.Invoke(null,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.unpatch_success),
							MessageBox.Data.Empty));
				}
				catch (System.OperationCanceledException)
				{
					if (backupedSavedata.Exists()) backupedSavedata.Delete();
					if (appSavedata.Exists()) appSavedata.Delete();

					StatusChanged?.Invoke(null,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.aborted),
							MessageBox.Data.Empty));
				}
				finally
				{
					TokenSource = new CancellationTokenSource();
					this.IsAnyRunning = false;
					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}

		public async Task UnpatchTask(Activity callerActivity, bool processSavedata)
		{
			try
			{
				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				var backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);

				bool isPatched =
					Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

				try
				{
					if (!isPatched)
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.error_not_patched),
									MessageBox.Code.OK)));
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (!Utils.IsAppInstalled(ChaosChildPackageName))
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.cc_not_found),
									MessageBox.Code.OK)));
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (!backupApk.Exists())
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.backup_not_found),
									MessageBox.Code.OK)));
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.no_free_space_unpatch),
									MessageBox.Code.OK)));
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (processSavedata)
					{
						if (new Java.IO.File(FilePaths[Files.OriginalSavedata]).Exists())
						{
							// Backup save data
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.backup_saves),
									MessageBox.Data.Empty));

							await Utils.CopyFile(FilePaths[Files.OriginalSavedata],
								OkkeiFilesPathBackup, SavedataBackupFileName);
							token.ThrowIfCancellationRequested();
						}
						else
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(null,
									new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
										Application.Context.Resources.GetText(Resource.String.saves_not_found_unpatch),
										MessageBox.Code.OK)));
						}
					}

					StatusChanged?.Invoke(this, new StatusChangedEventArgs("", MessageBox.Data.Empty));


					// Uninstall and reinstall backed up CHAOS;CHILD, then restore OBB and, if checked, save data
					Utils.UninstallPackage(callerActivity, ChaosChildPackageName);
				}
				catch (System.OperationCanceledException)
				{
					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.aborted),
							MessageBox.Data.Empty));
					TokenSource = new CancellationTokenSource();
					this.IsAnyRunning = false;
				}
				finally
				{
					backupApk.Dispose();
					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}
	}
}