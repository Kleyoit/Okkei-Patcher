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

		private bool _isRunning;

		private bool _saveDataBackupFromOldPatch;

		private PatchTasks()
		{
			Utils.StatusChanged += UtilsOnStatusChanged;
			Utils.ProgressChanged += UtilsOnProgressChanged;
			Utils.ErrorCanceled += UtilsOnErrorCanceled;
		}

		public static PatchTasks Instance => instance.Value;

		public bool IsRunning
		{
			get => _isRunning;
			set
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
		public event EventHandler ErrorCanceled;

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

		private void UtilsOnErrorCanceled(object sender, EventArgs e)
		{
			if (IsRunning) ErrorCanceled?.Invoke(this, e);
		}

		public async Task FinishPatch(bool processSavedata, CancellationToken token)
		{
			try
			{
				IsRunning = true;

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

							await Utils.CopyFile(backupSavedata.Path, SavedataPath,
								SavedataFileName, token);
							token.ThrowIfCancellationRequested();
						}
					}

					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

					var installedObb = new Java.IO.File(FilePaths[Files.ObbToReplace]);

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

						await Utils.DownloadFile(ObbUrl, ObbPath, ObbFileName, token);
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
				catch (OperationCanceledException)
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
					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}

		public async Task PatchTask(Activity callerActivity, bool processSavedata, CancellationToken token)
		{
			try
			{
				IsRunning = true;
				_saveDataBackupFromOldPatch = false;

				ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

				try
				{
					StatusChanged?.Invoke(this,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.checking),
							MessageBox.Data.Empty));

					var isPatched =
						Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

					if (isPatched)
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.error_patched),
									MessageBox.Code.OK)));

						ErrorCanceled?.Invoke(this, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}

					if (!Utils.IsAppInstalled(ChaosChildPackageName))
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.cc_not_found),
									MessageBox.Code.OK)));

						ErrorCanceled?.Invoke(this, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.no_free_space_patch),
									MessageBox.Code.OK)));

						ErrorCanceled?.Invoke(this, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}


					// Backup save data
					if (processSavedata)
					{
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

						var originalSavedata = new Java.IO.File(FilePaths[Files.OriginalSavedata]);
						var backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);

						if (backupSavedata.Exists())
							if (Utils.CompareMD5(Files.BackupSavedata))
							{
								_saveDataBackupFromOldPatch = true;
								backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.SAVEDATA_BACKUP]));
								backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
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
									backupSavedata.Parent, backupSavedata.Name, token);
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
						{
							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(null,
									new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
										callerActivity.Resources.GetText(Resource.String.saves_not_found_patch),
										MessageBox.Code.OK)));
						}

						backupSavedata.Dispose();
					}

					if (!new Java.IO.File(FilePaths[Files.SignedApk]).Exists())
					{
						// Get installed CHAOS;CHILD APK
						var originalApkPath = Application.Context.PackageManager
							.GetPackageInfo(ChaosChildPackageName, 0)
							.ApplicationInfo
							.PublicSourceDir;
						var unpatchedApk = new Java.IO.File(FilePaths[Files.TempApk]);

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.copy_apk),
								MessageBox.Data.Empty));

						await Utils.CopyFile(originalApkPath, unpatchedApk.Parent,
							unpatchedApk.Name, token);
						token.ThrowIfCancellationRequested();


						// Backup APK
						var backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);

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
									backupApk.Name, token);
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

						var scriptsZip = new Java.IO.File(FilePaths[Files.Scripts]);

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
								scriptsZip.Name, token);
							token.ThrowIfCancellationRequested();

							StatusChanged?.Invoke(this,
								new StatusChangedEventArgs(
									Application.Context.Resources.GetText(Resource.String.write_scripts_md5),
									MessageBox.Data.Empty));

							Preferences.Set(Prefkey.scripts_md5.ToString(), Utils.CalculateMD5(scriptsZip.Path));
						}


						// Extract scripts
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

						var fastZip = new FastZip();
						string fileFilter = null;

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.extract_scripts),
								MessageBox.Data.Empty));

						fastZip.ExtractZip(scriptsZip.Path, Path.Combine(OkkeiFilesPath, "scripts"),
							fileFilter);

						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));


						// Replace scripts
						var filePaths = Directory.GetFiles(Path.Combine(OkkeiFilesPath, "scripts"));
						var scriptsCount = filePaths.Length;

						StatusChanged?.Invoke(this,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.replace_scripts),
								MessageBox.Data.Empty));

						var zipFile = new ZipFile(unpatchedApk.Path);

						zipFile.BeginUpdate();

						var i = 0;
						foreach (var scriptfile in filePaths)
						{
							zipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));
							i++;
							ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(i, scriptsCount));
						}


						// Remove APK signature
						foreach (ZipEntry ze in zipFile)
							if (ze.Name.StartsWith("META-INF/"))
								zipFile.Delete(ze);


						// Update APK
						zipFile.CommitUpdate();
						zipFile.Close();


						// Delete temp files
						foreach (var file in filePaths) File.Delete(file);
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

						var apkToSign = new FileStream(FilePaths[Files.TempApk], FileMode.Open);
						var signedApkStream =
							new FileStream(FilePaths[Files.SignedApk], FileMode.OpenOrCreate);
						var signWholeFile = false;

						SignPackage(apkToSign, Testkey, signedApkStream, signWholeFile);

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

					var originalObb = new Java.IO.File(FilePaths[Files.ObbToBackup]);
					var backupObb = new Java.IO.File(FilePaths[Files.BackupObb]);

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
								backupObb.Name, token);
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
									Application.Context.Resources.GetText(Resource.String.obb_not_found_patch),
									MessageBox.Code.OK)));
						ErrorCanceled?.Invoke(this, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}

					StatusChanged?.Invoke(null, new StatusChangedEventArgs("", MessageBox.Data.Empty));


					// Uninstall and install patched CHAOS;CHILD, then restore save data if exists and checked, after that download OBB
					Utils.UninstallPackage(callerActivity, ChaosChildPackageName);

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
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}
	}
}