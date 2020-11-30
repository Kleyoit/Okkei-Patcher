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

		private bool _isRunning;

		private UnpatchTasks()
		{
			Utils.StatusChanged += UtilsOnStatusChanged;
			Utils.ProgressChanged += UtilsOnProgressChanged;
			Utils.TokenErrorOccurred += UtilsOnTokenErrorOccurred;
			Utils.TaskErrorOccurred += UtilsOnTaskErrorOccurred;
		}

		public static UnpatchTasks Instance => instance.Value;

		public bool IsRunning
		{
			get => _isRunning;
			private set
			{
				if (value != _isRunning)
				{
					_isRunning = value;
					NotifyPropertyChanged();
				}
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public event EventHandler<StatusChangedEventArgs> StatusChanged;
		public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public event EventHandler ErrorOccurred;

		private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private void UtilsOnStatusChanged(object sender, StatusChangedEventArgs e)
		{
			if (IsRunning) StatusChanged?.Invoke(this, e);
		}

		private void UtilsOnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (IsRunning) ProgressChanged?.Invoke(this, e);
		}

		private void UtilsOnTokenErrorOccurred(object sender, EventArgs e)
		{
			if (IsRunning) ErrorOccurred?.Invoke(this, e);
		}

		private void UtilsOnTaskErrorOccurred(object sender, EventArgs e)
		{
			if (IsRunning) IsRunning = false;
		}

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
					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.restore_obb),
							MessageBox.Data.Empty));

					if (!backupObb.Exists())
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.obb_not_found_unpatch),
									MessageBox.Code.OK)));
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}

					await Utils.CopyFile(backupObb.Path, ObbPath, ObbFileName, token);
					token.ThrowIfCancellationRequested();

					if (processSavedata)
					{
						if (backupSavedata.Exists())
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.restore_saves),
									MessageBox.Data.Empty));

							await Utils.CopyFile(backupSavedata.Path, SavedataPath,
								SavedataFileName, token);
							token.ThrowIfCancellationRequested();
						}
						else
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(null,
									new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
										Application.Context.Resources.GetText(Resource.String.saves_backup_not_found),
										MessageBox.Code.OK)));
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
								Utils.CalculateMD5(backupSavedataCopy.Path));
						}
					}

					// Finish unpatch
					Preferences.Set(Prefkey.apk_is_patched.ToString(), false);

					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.unpatch_success),
							MessageBox.Data.Empty));
				}
				catch (OperationCanceledException)
				{
					if (backupSavedataCopy.Exists()) backupSavedataCopy.Delete();
					if (appSavedata.Exists()) appSavedata.Delete();

					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.aborted),
							MessageBox.Data.Empty));
				}
				finally
				{
					backupApk.Dispose();
					backupObb.Dispose();
					backupSavedata.Dispose();
					backupSavedataCopy.Dispose();
					appSavedata.Dispose();

					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}

		public async Task UnpatchTask(Activity callerActivity, bool processSavedata, CancellationToken token)
		{
			try
			{
				IsRunning = true;

				var backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);

				var isPatched =
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
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}

					if (!backupApk.Exists())
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.backup_not_found),
									MessageBox.Code.OK)));
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.no_free_space_unpatch),
									MessageBox.Code.OK)));
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
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
								OkkeiFilesPathBackup, SavedataBackupFileName, token);
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


					// Uninstall CHAOS;CHILD and install backup, then restore OBB and save data if checked
					// Install backup immediately if CHAOS;CHILD is not installed
					if (Utils.IsAppInstalled(ChaosChildPackageName))
						Utils.UninstallPackage(callerActivity, ChaosChildPackageName);
					else
						Utils.InstallPackage(callerActivity,
							Android.Net.Uri.FromFile(new Java.IO.File(FilePaths[Files.BackupApk])));

					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
				}
				catch (OperationCanceledException)
				{
					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.aborted),
							MessageBox.Data.Empty));
					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
				finally
				{
					backupApk.Dispose();
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
			finally
			{
				callerActivity.Dispose();
			}
		}
	}
}