using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using ICSharpCode.SharpZipLib.Zip;
using Xamarin.Essentials;
using static SignApk.SignApk;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class PatchTasks : INotifyPropertyChanged
	{
		private static readonly Lazy<PatchTasks> instance = new Lazy<PatchTasks>(() => new PatchTasks());

		private PatchTasks()
		{
			Utils.StatusChanged += UtilsOnStatusChanged;
			Utils.ProgressChanged += UtilsOnProgressChanged;
		}

		public static PatchTasks Instance => instance.Value;

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

		private static bool _saveDataBackupFromOldPatch = false;

		public event EventHandler<StatusChangedEventArgs> StatusChanged;
		public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public event PropertyChangedEventHandler PropertyChanged;

		private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") =>
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		private void UtilsOnStatusChanged(object sender, StatusChangedEventArgs e)
		{
			if (this.IsAnyRunning) StatusChanged?.Invoke(this, e);
		}

		private void UtilsOnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (this.IsAnyRunning) ProgressChanged?.Invoke(this, e);
		}

		public async Task FinishPatch(bool processSavedata)
		{
			try
			{
				this.IsAnyRunning = true;

				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				Java.IO.File backupSavedata = null;

				try
				{
					if (_saveDataBackupFromOldPatch && processSavedata)
					{
						backupSavedata = new Java.IO.File(Path.Combine(OkkeiFilesPathBackup, SavedataBackupFileName));

						if (backupSavedata.Exists())
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.restore_old_saves),
									MessageBox.Data.Empty));

							backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.BackupSavedata]));
							backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);

							await Utils.CopyFile(backupSavedata.Path, SavedataPath, SavedataFileName);
							token.ThrowIfCancellationRequested();
						}
					}

					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

					Java.IO.File installedObb = new Java.IO.File(FilePaths[Files.ObbToReplace]);

					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.compare_obb),
							MessageBox.Data.Empty));

					if (!Utils.CompareMD5(Files.ObbToReplace) || !installedObb.Exists())
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.download_obb),
								MessageBox.Data.Empty));

						await Utils.DownloadFile(ObbUrl, ObbPath, ObbFileName);
						token.ThrowIfCancellationRequested();

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.write_obb_md5),
								MessageBox.Data.Empty));

						Preferences.Set(Prefkey.downloaded_obb_md5.ToString(), Utils.CalculateMD5(installedObb.Path));
						Preferences.Set(Prefkey.apk_is_patched.ToString(), true);
					}

					installedObb.Dispose();

					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.patch_success),
							MessageBox.Data.Empty));
				}
				catch (System.OperationCanceledException)
				{
					backupSavedata?.Delete();

					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.aborted),
							MessageBox.Data.Empty));
				}
				finally
				{
					backupSavedata?.Dispose();
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

		public async Task PatchTask(Activity callerActivity, bool processSavedata)
		{
			try
			{
				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

				try
				{
					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.checking),
							MessageBox.Data.Empty));

					bool isPatched =
						Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

					if (isPatched)
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.error_patched),
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

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.no_free_space_patch),
									MessageBox.Code.OK)));

						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}


					// Backup save data
					if (processSavedata)
					{
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

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
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.compare_saves),
									MessageBox.Data.Empty));

							if (!Utils.CompareMD5(Files.OriginalSavedata))
							{
								StatusChanged?.Invoke(this,
									new StatusChangedEventArgs(
										Application.Context.Resources.GetText(Resource.String.backup_saves),
										MessageBox.Data.Empty));

								await Utils.CopyFile(originalSavedata.Path,
									backupSavedata.Parent, backupSavedata.Name);
								if (token.IsCancellationRequested) originalSavedata.Dispose();
								token.ThrowIfCancellationRequested();

								StatusChanged?.Invoke(this,
									new StatusChangedEventArgs(
										Application.Context.Resources.GetText(Resource.String.write_saves_md5),
										MessageBox.Data.Empty));

								Preferences.Set(Prefkey.savedata_md5.ToString(),
									Utils.CalculateMD5(originalSavedata.Path));
								originalSavedata.Dispose();
							}
						}
						else
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(null,
									new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
										callerActivity.Resources.GetText(Resource.String.saves_not_found_patch),
										MessageBox.Code.OK)));

						backupSavedata.Dispose();
					}

					if (!new Java.IO.File(FilePaths[Files.SignedApk]).Exists())
					{
						// Get installed CHAOS;CHILD APK
						string originalApkPath = Application.Context.PackageManager
							.GetPackageInfo(ChaosChildPackageName, 0)
							.ApplicationInfo
							.PublicSourceDir;
						Java.IO.File unpatchedApk = new Java.IO.File(FilePaths[Files.TempApk]);

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.copy_apk),
								MessageBox.Data.Empty));

						await Utils.CopyFile(originalApkPath, unpatchedApk.Parent,
							unpatchedApk.Name);
						token.ThrowIfCancellationRequested();


						// Backup APK
						Java.IO.File backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);

						if (unpatchedApk.Exists())
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.compare_apk),
									MessageBox.Data.Empty));

							if (!Utils.CompareMD5(Files.TempApk) || !backupApk.Exists())
							{
								StatusChanged?.Invoke(this,
									new StatusChangedEventArgs(
										Application.Context.Resources.GetText(Resource.String.backup_apk),
										MessageBox.Data.Empty));

								await Utils.CopyFile(originalApkPath, backupApk.Parent,
									backupApk.Name);
								token.ThrowIfCancellationRequested();

								StatusChanged?.Invoke(this,
									new StatusChangedEventArgs(
										Application.Context.Resources.GetText(Resource.String.write_apk_md5),
										MessageBox.Data.Empty));

								Preferences.Set(Prefkey.backup_apk_md5.ToString(), Utils.CalculateMD5(backupApk.Path));
							}
						}

						backupApk.Dispose();


						// Download scripts
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

						Java.IO.File scriptsZip = new Java.IO.File(FilePaths[Files.Scripts]);

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.compare_scripts),
								MessageBox.Data.Empty));

						if (!Utils.CompareMD5(Files.Scripts) || !scriptsZip.Exists())
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.download_scripts),
									MessageBox.Data.Empty));

							await Utils.DownloadFile(ScriptsUrl, scriptsZip.Parent,
								scriptsZip.Name);
							token.ThrowIfCancellationRequested();

							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.write_scripts_md5),
									MessageBox.Data.Empty));

							Preferences.Set(Prefkey.scripts_md5.ToString(), Utils.CalculateMD5(scriptsZip.Path));
						}


						// Extract scripts
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

						FastZip fastZip = new FastZip();
						string fileFilter = null;

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.extract_scripts),
								MessageBox.Data.Empty));

						fastZip.ExtractZip(scriptsZip.Path, Path.Combine(OkkeiFilesPath, "scripts"),
							fileFilter);

						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));


						// Replace scripts
						string[] filePaths = Directory.GetFiles(Path.Combine(OkkeiFilesPath, "scripts"));
						int scriptsCount = filePaths.Length;

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.replace_scripts),
								MessageBox.Data.Empty));

						ZipFile zipFile = new ZipFile(unpatchedApk.Path);

						zipFile.BeginUpdate();

						int i = 0;
						foreach (string scriptfile in filePaths)
						{
							zipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));
							i++;
							ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(i, scriptsCount));
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
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.sign_apk),
								MessageBox.Data.Empty));

						FileStream apkToSign = new FileStream(FilePaths[Files.TempApk], FileMode.Open);
						FileStream signedApkStream =
							new FileStream(FilePaths[Files.SignedApk], FileMode.OpenOrCreate);
						bool signWholeFile = false;

						SignPackage(apkToSign, testkey, signedApkStream, signWholeFile);

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.write_patched_apk_md5),
								MessageBox.Data.Empty));

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
					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

					Java.IO.File originalObb = new Java.IO.File(FilePaths[Files.ObbToBackup]);
					Java.IO.File backupObb = new Java.IO.File(FilePaths[Files.BackupObb]);

					if (originalObb.Exists())
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.compare_obb),
								MessageBox.Data.Empty));

						if (!Utils.CompareMD5(Files.ObbToBackup) || !backupObb.Exists())
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.backup_obb),
									MessageBox.Data.Empty));

							await Utils.CopyFile(originalObb.Path, backupObb.Parent,
								backupObb.Name);
							originalObb?.Dispose();
							if (token.IsCancellationRequested) backupObb?.Dispose();
							token.ThrowIfCancellationRequested();

							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.write_obb_md5),
									MessageBox.Data.Empty));

							Preferences.Set(Prefkey.backup_obb_md5.ToString(), Utils.CalculateMD5(backupObb.Path));
							backupObb?.Dispose();
						}
					}
					else
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.obb_not_found),
									MessageBox.Code.OK)));
						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}

					StatusChanged?.Invoke(null, new StatusChangedEventArgs("", MessageBox.Data.Empty));


					// Uninstall and install patched CHAOS;CHILD, then restore save data if exists and checked, after that download OBB
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