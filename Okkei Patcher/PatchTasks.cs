using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Widget;
using ICSharpCode.SharpZipLib.Zip;
using Xamarin.Essentials;
using static SignApk.SignApk;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal static class PatchTasks
	{
		public static bool IsAnyRunning = false;
		private static bool _saveDataBackupFromOldPatch = false;

		public static async Task FinishPatch(Activity callerActivity)
		{
			try
			{
				PatchTasks.IsAnyRunning = true;

				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				Button patch = callerActivity.FindViewById<Button>(Resource.Id.Patch);
				TextView info = callerActivity.FindViewById<TextView>(Resource.Id.Status);
				ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);
				CheckBox checkBoxSavedata = callerActivity.FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);

				MainThread.BeginInvokeOnMainThread(() =>
				{
					patch.Text = callerActivity.Resources.GetText(Resource.String.abort);
				});

				Java.IO.File backupSavedata = null;

				try
				{
					if (_saveDataBackupFromOldPatch && checkBoxSavedata.Checked)
					{
						backupSavedata = new Java.IO.File(Path.Combine(OkkeiFilesPathBackup, SavedataBackupFileName));

						if (backupSavedata.Exists())
						{
							MainThread.BeginInvokeOnMainThread(
								() =>
								{
									info.Text = callerActivity.Resources.GetText(Resource.String.restore_old_saves);
								});

							backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.BackupSavedata]));
							backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);

							await Utils.CopyFile(callerActivity, backupSavedata.Path, SavedataPath, SavedataFileName);
							token.ThrowIfCancellationRequested();
						}
					}

					MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });

					Java.IO.File installedObb = new Java.IO.File(FilePaths[Files.ObbToReplace]);

					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.compare_obb);
					});

					if (!Utils.CompareMD5(Files.ObbToReplace) || !installedObb.Exists())
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.download_obb);
						});

						await Utils.DownloadFile(callerActivity, ObbUrl, ObbPath, ObbFileName);
						token.ThrowIfCancellationRequested();

						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.write_obb_md5);
						});

						Preferences.Set(Prefkey.downloaded_obb_md5.ToString(), Utils.CalculateMD5(installedObb.Path));
						Preferences.Set(Prefkey.apk_is_patched.ToString(), true);
					}

					installedObb.Dispose();

					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.patch_success);
					});
				}
				catch (System.OperationCanceledException)
				{
					backupSavedata?.Delete();

					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.aborted);
					});
				}
				finally
				{
					backupSavedata?.Dispose();
					TokenSource = new CancellationTokenSource();
					PatchTasks.IsAnyRunning = false;
					MainThread.BeginInvokeOnMainThread(() =>
					{
						patch.Text = callerActivity.Resources.GetText(Resource.String.patch);
					});
					MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(callerActivity, ex);
			}
		}

		public static async Task PatchTask(Activity callerActivity)
		{
			try
			{
				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				Button patch = callerActivity.FindViewById<Button>(Resource.Id.Patch);
				TextView info = callerActivity.FindViewById<TextView>(Resource.Id.Status);
				ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);
				CheckBox checkBoxSavedata = callerActivity.FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);

				MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = 100; });

				try
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.checking);
					});

					bool isPatched =
						Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

					if (isPatched)
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
								callerActivity.Resources.GetText(Resource.String.error_patched),
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

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
								callerActivity.Resources.GetText(Resource.String.no_free_space_patch),
								MessageBox.Code.OK);
						});
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}


					// Backup save data
					if (checkBoxSavedata.Checked)
					{
						MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });

						Java.IO.File originalSavedata = new Java.IO.File(FilePaths[Files.OriginalSavedata]);
						Java.IO.File backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);

						if (backupSavedata.Exists())
						{
							if (Utils.CompareMD5(Files.BackupSavedata))
							{
								PatchTasks._saveDataBackupFromOldPatch = true;
								backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.SAVEDATA_BACKUP]));
								backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
							}
						}

						if (originalSavedata.Exists())
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.compare_saves);
							});

							if (!Utils.CompareMD5(Files.OriginalSavedata))
							{
								MainThread.BeginInvokeOnMainThread(() =>
								{
									info.Text = callerActivity.Resources.GetText(Resource.String.backup_saves);
								});

								await Utils.CopyFile(callerActivity, originalSavedata.Path,
									backupSavedata.Parent, backupSavedata.Name);
								if (token.IsCancellationRequested) originalSavedata.Dispose();
								token.ThrowIfCancellationRequested();

								MainThread.BeginInvokeOnMainThread(() =>
								{
									info.Text = callerActivity.Resources.GetText(Resource.String.write_saves_md5);
								});

								Preferences.Set(Prefkey.savedata_md5.ToString(),
									Utils.CalculateMD5(originalSavedata.Path));
								originalSavedata.Dispose();
							}
						}
						else
							MainThread.BeginInvokeOnMainThread(() =>
							{
								MessageBox.Show(callerActivity,
									callerActivity.Resources.GetText(Resource.String.warning),
									callerActivity.Resources.GetText(Resource.String.saves_not_found_patch),
									MessageBox.Code.OK);
							});

						backupSavedata.Dispose();
					}

					if (!new Java.IO.File(FilePaths[Files.SignedApk]).Exists())
					{
						// Get installed CHAOS;CHILD APK
						string originalApkPath = callerActivity.PackageManager.GetPackageInfo(ChaosChildPackageName, 0)
							.ApplicationInfo
							.PublicSourceDir;
						Java.IO.File unpatchedApk = new Java.IO.File(FilePaths[Files.TempApk]);

						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.copy_apk);
						});

						await Utils.CopyFile(callerActivity, originalApkPath, unpatchedApk.Parent,
							unpatchedApk.Name);
						token.ThrowIfCancellationRequested();


						// Backup APK
						Java.IO.File backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);

						if (unpatchedApk.Exists())
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.compare_apk);
							});

							if (!Utils.CompareMD5(Files.TempApk) || !backupApk.Exists())
							{
								MainThread.BeginInvokeOnMainThread(() =>
								{
									info.Text = callerActivity.Resources.GetText(Resource.String.backup_apk);
								});

								await Utils.CopyFile(callerActivity, originalApkPath, backupApk.Parent,
									backupApk.Name);
								token.ThrowIfCancellationRequested();

								MainThread.BeginInvokeOnMainThread(() =>
								{
									info.Text = callerActivity.Resources.GetText(Resource.String.write_apk_md5);
								});

								Preferences.Set(Prefkey.backup_apk_md5.ToString(), Utils.CalculateMD5(backupApk.Path));
							}
						}

						backupApk.Dispose();


						// Download scripts
						MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });

						Java.IO.File scriptsZip = new Java.IO.File(FilePaths[Files.Scripts]);

						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.compare_scripts);
						});

						if (!Utils.CompareMD5(Files.Scripts) || !scriptsZip.Exists())
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.download_scripts);
							});

							await Utils.DownloadFile(callerActivity, ScriptsUrl, scriptsZip.Parent,
								scriptsZip.Name);
							token.ThrowIfCancellationRequested();

							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.write_scripts_md5);
							});

							Preferences.Set(Prefkey.scripts_md5.ToString(), Utils.CalculateMD5(scriptsZip.Path));
						}


						// Extract scripts
						MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = 100; });

						FastZip fastZip = new FastZip();
						string fileFilter = null;

						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.extract_scripts);
						});

						fastZip.ExtractZip(scriptsZip.Path, Path.Combine(OkkeiFilesPath, "scripts"),
							fileFilter);

						MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });


						// Replace scripts
						string[] filePaths = Directory.GetFiles(Path.Combine(OkkeiFilesPath, "scripts"));
						int scriptsCount = filePaths.Length;

						MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = scriptsCount; });
						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.replace_scripts);
						});

						ZipFile zipFile = new ZipFile(unpatchedApk.Path);

						zipFile.BeginUpdate();

						foreach (string scriptfile in filePaths)
						{
							zipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));

							MainThread.BeginInvokeOnMainThread(() => { progressBar.IncrementProgressBy(1); });
						}


						// Remove APK signature
						foreach (ZipEntry ze in zipFile)
						{
							if (ze.Name.StartsWith("META-INF/"))
								zipFile.Delete(ze);
						}


						// Update APK
						zipFile.CommitUpdate();
						zipFile.Close();


						// Delete temp files
						foreach (string file in filePaths) File.Delete(file);
						Directory.Delete(Path.Combine(OkkeiFilesPath, "scripts"));
						scriptsZip?.Dispose();

						if (token.IsCancellationRequested)
						{
							if (unpatchedApk.Exists()) unpatchedApk.Delete();
							unpatchedApk?.Dispose();
							token.ThrowIfCancellationRequested();
						}


						// Sign APK
						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.sign_apk);
						});

						FileStream apkToSign = new FileStream(FilePaths[Files.TempApk], FileMode.Open);
						FileStream signedApkStream =
							new FileStream(FilePaths[Files.SignedApk], FileMode.OpenOrCreate);
						bool signWholeFile = false;

						SignPackage(apkToSign, testkey, signedApkStream, signWholeFile);

						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.write_patched_apk_md5);
						});

						Preferences.Set(Prefkey.signed_apk_md5.ToString(),
							Utils.CalculateMD5(FilePaths[Files.SignedApk]));

						if (unpatchedApk.Exists()) unpatchedApk.Delete();
						unpatchedApk?.Dispose();

						if (token.IsCancellationRequested)
						{
							var signedApk = new Java.IO.File(FilePaths[Files.SignedApk]);
							if (signedApk.Exists()) signedApk.Delete();
							signedApk?.Dispose();

							token.ThrowIfCancellationRequested();
						}
					}


					// Backup OBB
					MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });

					Java.IO.File originalObb = new Java.IO.File(FilePaths[Files.ObbToBackup]);
					Java.IO.File backupObb = new Java.IO.File(FilePaths[Files.BackupObb]);

					if (originalObb.Exists())
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.compare_obb);
						});

						if (!Utils.CompareMD5(Files.ObbToBackup) || !backupObb.Exists())
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.backup_obb);
							});

							await Utils.CopyFile(callerActivity, originalObb.Path, backupObb.Parent,
								backupObb.Name);
							originalObb?.Dispose();
							if (token.IsCancellationRequested) backupObb?.Dispose();
							token.ThrowIfCancellationRequested();

							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = callerActivity.Resources.GetText(Resource.String.write_obb_md5);
							});

							Preferences.Set(Prefkey.backup_obb_md5.ToString(), Utils.CalculateMD5(backupObb.Path));
							backupObb?.Dispose();
						}
					}
					else
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
								callerActivity.Resources.GetText(Resource.String.obb_not_found),
								MessageBox.Code.OK);
						});
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					MainThread.BeginInvokeOnMainThread(() => { info.Text = ""; });


					// Uninstall and install patched CHAOS;CHILD, then restore save data if exists and checked, after that download OBB
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
						patch.Text = callerActivity.Resources.GetText(Resource.String.patch);
					});
					TokenSource = new CancellationTokenSource();
					PatchTasks.IsAnyRunning = false;
				}
				finally
				{
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