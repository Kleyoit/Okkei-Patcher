using System;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Widget;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal static class UnpatchTasks
	{
		public static bool IsAnyRunning = false;

		public static async Task RestoreFiles(Activity callerActivity)
		{
			try
			{
				UnpatchTasks.IsAnyRunning = true;

				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				Button unpatch = callerActivity.FindViewById<Button>(Resource.Id.Unpatch);
				TextView info = callerActivity.FindViewById<TextView>(Resource.Id.Status);
				ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);
				CheckBox checkBoxSavedata = callerActivity.FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);

				var backupedSavedata = new Java.IO.File(FilePaths[Files.SAVEDATA_BACKUP]);
				var appSavedata = new Java.IO.File(FilePaths[Files.OriginalSavedata]);

				try
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.restore_obb);
					});

					await Utils.CopyFile(callerActivity, FilePaths[Files.BackupObb], ObbPath, ObbFileName);
					token.ThrowIfCancellationRequested();

					if (checkBoxSavedata.Checked)
					{
						if (new Java.IO.File(FilePaths[Files.BackupSavedata]).Exists())
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.restore_saves);
							});

							await Utils.CopyFile(callerActivity, FilePaths[Files.BackupSavedata], SavedataPath,
								SavedataFileName);
							token.ThrowIfCancellationRequested();
						}
						else
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								MessageBox.Show(callerActivity,
									callerActivity.Resources.GetText(Resource.String.warning),
									callerActivity.Resources.GetText(Resource.String.saves_backup_not_found),
									MessageBox.Code.OK);
							});
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

					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.unpatch_success);
					});
				}
				catch (System.OperationCanceledException)
				{
					if (backupedSavedata.Exists()) backupedSavedata.Delete();
					if (appSavedata.Exists()) appSavedata.Delete();

					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.aborted);
					});
				}
				finally
				{
					TokenSource = new CancellationTokenSource();
					UnpatchTasks.IsAnyRunning = false;
					MainThread.BeginInvokeOnMainThread(() =>
					{
						unpatch.Text = callerActivity.Resources.GetText(Resource.String.unpatch);
					});
					MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(callerActivity, ex);
			}
		}

		public static async Task UnpatchTask(Activity callerActivity)
		{
			try
			{
				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				Button unpatch = callerActivity.FindViewById<Button>(Resource.Id.Unpatch);
				TextView info = callerActivity.FindViewById<TextView>(Resource.Id.Status);
				ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);
				CheckBox checkBoxSavedata = callerActivity.FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);

				var backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);

				bool isPatched =
					Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

				try
				{
					if (!isPatched)
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
								callerActivity.Resources.GetText(Resource.String.error_not_patched),
								MessageBox.Code.OK);
						});
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (!Utils.IsAppInstalled(ChaosChildPackageName))
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
								callerActivity.Resources.GetText(Resource.String.cc_not_found), MessageBox.Code.OK);
						});
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (!backupApk.Exists())
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
								callerActivity.Resources.GetText(Resource.String.backup_not_found),
								MessageBox.Code.OK);
						});
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
								callerActivity.Resources.GetText(Resource.String.no_free_space_unpatch),
								MessageBox.Code.OK);
						});
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					if (checkBoxSavedata.Checked)
					{
						if (new Java.IO.File(FilePaths[Files.OriginalSavedata]).Exists())
						{
							// Backup save data
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.backup_saves);
							});

							await Utils.CopyFile(callerActivity, FilePaths[Files.OriginalSavedata],
								OkkeiFilesPathBackup, SavedataBackupFileName);
							token.ThrowIfCancellationRequested();
						}
						else
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								MessageBox.Show(callerActivity,
									callerActivity.Resources.GetText(Resource.String.warning),
									callerActivity.Resources.GetText(Resource.String.saves_not_found_unpatch),
									MessageBox.Code.OK);
							});
						}
					}

					MainThread.BeginInvokeOnMainThread(() => { info.Text = ""; });


					// Uninstall and reinstall backed up CHAOS;CHILD, then restore OBB and, if checked, save data
					Utils.UninstallPackage(callerActivity, ChaosChildPackageName);
				}
				catch (System.OperationCanceledException)
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.aborted);
					});
					MainThread.BeginInvokeOnMainThread(() =>
					{
						unpatch.Text = callerActivity.Resources.GetText(Resource.String.unpatch);
					});
					TokenSource = new CancellationTokenSource();
					UnpatchTasks.IsAnyRunning = false;
				}
				finally
				{
					backupApk.Dispose();
					MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(callerActivity, ex);
			}
		}
	}
}