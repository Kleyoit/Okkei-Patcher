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
			Utils.MessageGenerated += UtilsOnMessageGenerated;
			Utils.TokenErrorOccurred += UtilsOnTokenErrorOccurred;
			Utils.TaskErrorOccurred += UtilsOnTaskErrorOccurred;
		}

		public static bool IsInstantiated => instance.IsValueCreated;

		public static PatchTasks Instance => instance.Value;

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

		public event EventHandler<string> StatusChanged;
		public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public event EventHandler<MessageBox.Data> MessageGenerated;
		public event EventHandler ErrorOccurred;

		private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private void UtilsOnStatusChanged(object sender, string e)
		{
			if (IsRunning) StatusChanged?.Invoke(this, e);
		}

		private void UtilsOnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (IsRunning) ProgressChanged?.Invoke(this, e);
		}

		private void UtilsOnMessageGenerated(object sender, MessageBox.Data e)
		{
			if (IsRunning) MessageGenerated?.Invoke(this, e);
		}

		private void UtilsOnTokenErrorOccurred(object sender, EventArgs e)
		{
			if (IsRunning) ErrorOccurred?.Invoke(this, e);
		}

		private void UtilsOnTaskErrorOccurred(object sender, EventArgs e)
		{
			if (IsRunning) IsRunning = false;
		}

		public async Task FinishPatch(bool processSavedata, CancellationToken token)
		{
			try
			{
				IsRunning = true;

				Java.IO.File backupSavedata = null;
				var installedObb = new Java.IO.File(FilePaths[Files.ObbToReplace]);

				try
				{
					if (_saveDataBackupFromOldPatch && processSavedata)
					{
						backupSavedata = new Java.IO.File(Path.Combine(OkkeiFilesPathBackup, SavedataBackupFileName));

						if (backupSavedata.Exists())
						{
							StatusChanged?.Invoke(this,
								Application.Context.Resources.GetText(Resource.String.restore_old_saves));

							backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.BackupSavedata]));
							backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);

							await Utils.CopyFile(backupSavedata.Path, SavedataPath,
								SavedataFileName, token);
						}
					}

					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

					StatusChanged?.Invoke(this, Application.Context.Resources.GetText(Resource.String.compare_obb));

					if (!installedObb.Exists() || !Utils.CompareMD5(Files.ObbToReplace, token).Result)
					{
						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.download_obb));

						await Utils.DownloadFile(ObbUrl, ObbPath, ObbFileName, token);

						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.write_obb_md5));

						Preferences.Set(Prefkey.downloaded_obb_md5.ToString(),
							Utils.CalculateMD5(installedObb.Path, token).Result);
						Preferences.Set(Prefkey.apk_is_patched.ToString(), true);
					}

					StatusChanged?.Invoke(this, Application.Context.Resources.GetText(Resource.String.patch_success));
				}
				catch (OperationCanceledException)
				{
					backupSavedata?.Delete();

					StatusChanged?.Invoke(this, Application.Context.Resources.GetText(Resource.String.aborted));
				}
				finally
				{
					backupSavedata?.Dispose();
					installedObb.Dispose();
					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}

		public async Task PatchTask(Activity activity, bool processSavedata, CancellationToken token)
		{
			try
			{
				IsRunning = true;
				_saveDataBackupFromOldPatch = false;

				ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

				var originalSavedata = new Java.IO.File(FilePaths[Files.OriginalSavedata]);
				var backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
				var unpatchedApk = new Java.IO.File(FilePaths[Files.TempApk]);
				var backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);
				var scriptsZip = new Java.IO.File(FilePaths[Files.Scripts]);
				var originalObb = new Java.IO.File(FilePaths[Files.ObbToBackup]);
				var backupObb = new Java.IO.File(FilePaths[Files.BackupObb]);

				try
				{
					StatusChanged?.Invoke(this, Application.Context.Resources.GetText(Resource.String.checking));

					var isPatched =
						Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

					if (isPatched)
					{
						MessageGenerated?.Invoke(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.error_patched),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					if (!Utils.IsAppInstalled(ChaosChildPackageName))
					{
						MessageGenerated?.Invoke(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.cc_not_found),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						MessageGenerated?.Invoke(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.no_free_space_patch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}


					// Backup save data
					if (processSavedata)
					{
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

						if (backupSavedata.Exists())
							if (Utils.CompareMD5(Files.BackupSavedata, token).Result)
							{
								_saveDataBackupFromOldPatch = true;
								backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.SAVEDATA_BACKUP]));
								backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
							}

						if (originalSavedata.Exists())
						{
							StatusChanged?.Invoke(this,
								Application.Context.Resources.GetText(Resource.String.compare_saves));

							if (!Utils.CompareMD5(Files.OriginalSavedata, token).Result)
							{
								StatusChanged?.Invoke(this,
									Application.Context.Resources.GetText(Resource.String.backup_saves));

								await Utils.CopyFile(originalSavedata.Path,
									backupSavedata.Parent, backupSavedata.Name, token);

								StatusChanged?.Invoke(this,
									Application.Context.Resources.GetText(Resource.String.write_saves_md5));

								Preferences.Set(Prefkey.savedata_md5.ToString(),
									Utils.CalculateMD5(originalSavedata.Path, token).Result);
							}
						}
						else
						{
							MessageGenerated?.Invoke(this,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
									activity.Resources.GetText(Resource.String.saves_not_found_patch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
									null, null));
						}
					}

					if (!File.Exists(FilePaths[Files.SignedApk]))
					{
						// Get installed CHAOS;CHILD APK
						var originalApkPath = Application.Context.PackageManager
							.GetPackageInfo(ChaosChildPackageName, 0)
							.ApplicationInfo
							.PublicSourceDir;

						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.copy_apk));

						await Utils.CopyFile(originalApkPath, unpatchedApk.Parent,
							unpatchedApk.Name, token);


						// Backup APK
						if (unpatchedApk.Exists())
						{
							StatusChanged?.Invoke(this,
								Application.Context.Resources.GetText(Resource.String.compare_apk));

							if (!backupApk.Exists() || !Utils.CompareMD5(Files.TempApk, token).Result)
							{
								StatusChanged?.Invoke(this,
									Application.Context.Resources.GetText(Resource.String.backup_apk));

								await Utils.CopyFile(originalApkPath, backupApk.Parent,
									backupApk.Name, token);

								StatusChanged?.Invoke(this,
									Application.Context.Resources.GetText(Resource.String.write_apk_md5));

								Preferences.Set(Prefkey.backup_apk_md5.ToString(),
									Utils.CalculateMD5(backupApk.Path, token).Result);
							}
						}


						// Download scripts
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.compare_scripts));

						if (!scriptsZip.Exists() || !Utils.CompareMD5(Files.Scripts, token).Result)
						{
							StatusChanged?.Invoke(this,
								Application.Context.Resources.GetText(Resource.String.download_scripts));

							await Utils.DownloadFile(ScriptsUrl, scriptsZip.Parent,
								scriptsZip.Name, token);

							StatusChanged?.Invoke(this,
								Application.Context.Resources.GetText(Resource.String.write_scripts_md5));

							Preferences.Set(Prefkey.scripts_md5.ToString(),
								Utils.CalculateMD5(scriptsZip.Path, token).Result);
						}


						// Extract scripts
						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

						var fastZip = new FastZip();
						string fileFilter = null;

						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.extract_scripts));

						fastZip.ExtractZip(scriptsZip.Path, Path.Combine(OkkeiFilesPath, "scripts"),
							fileFilter);

						ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));


						// Replace scripts
						var filePaths = Directory.GetFiles(Path.Combine(OkkeiFilesPath, "scripts"));
						var scriptsCount = filePaths.Length;

						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.replace_scripts));

						var zipFile = new ZipFile(unpatchedApk.Path);

						zipFile.BeginUpdate();

						var progress = 0;
						foreach (var scriptfile in filePaths)
						{
							zipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));
							++progress;
							ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(progress, scriptsCount));
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

						if (token.IsCancellationRequested)
						{
							if (unpatchedApk.Exists()) unpatchedApk.Delete();
							throw new OperationCanceledException("The operation was canceled.", token);
						}


						// Sign APK
						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.sign_apk));

						var apkToSign = new FileStream(FilePaths[Files.TempApk], FileMode.Open);
						var signedApkStream =
							new FileStream(FilePaths[Files.SignedApk], FileMode.OpenOrCreate);
						var signWholeFile = false;

						SignPackage(apkToSign, Testkey, signedApkStream, signWholeFile);

						apkToSign.Dispose();
						signedApkStream.Dispose();

						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.write_patched_apk_md5));

						Preferences.Set(Prefkey.signed_apk_md5.ToString(),
							Utils.CalculateMD5(FilePaths[Files.SignedApk], token).Result);

						if (unpatchedApk.Exists()) unpatchedApk.Delete();

						if (token.IsCancellationRequested)
						{
							if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);
							throw new OperationCanceledException("The operation was canceled.", token);
						}
					}


					// Backup OBB
					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));

					if (originalObb.Exists())
					{
						StatusChanged?.Invoke(this,
							Application.Context.Resources.GetText(Resource.String.compare_obb));

						if (!backupObb.Exists() || !Utils.CompareMD5(Files.ObbToBackup, token).Result)
						{
							StatusChanged?.Invoke(this,
								Application.Context.Resources.GetText(Resource.String.backup_obb));

							await Utils.CopyFile(originalObb.Path, backupObb.Parent,
								backupObb.Name, token);

							StatusChanged?.Invoke(this,
								Application.Context.Resources.GetText(Resource.String.write_obb_md5));

							Preferences.Set(Prefkey.backup_obb_md5.ToString(),
								Utils.CalculateMD5(backupObb.Path, token).Result);
						}
					}
					else
					{
						MessageGenerated?.Invoke(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.obb_not_found_patch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
						ErrorOccurred?.Invoke(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					StatusChanged?.Invoke(null, string.Empty);


					// Uninstall and install patched CHAOS;CHILD, then restore save data if exists and checked, after that download OBB
					Utils.UninstallPackage(activity, ChaosChildPackageName);

					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
				}
				catch (OperationCanceledException)
				{
					StatusChanged?.Invoke(this,
						Application.Context.Resources.GetText(Resource.String.aborted));

					ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
				finally
				{
					originalSavedata.Dispose();
					backupSavedata.Dispose();
					unpatchedApk.Dispose();
					backupApk.Dispose();
					scriptsZip.Dispose();
					originalObb.Dispose();
					backupObb.Dispose();
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}
	}
}