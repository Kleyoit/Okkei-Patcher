using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using ICSharpCode.SharpZipLib.Zip;
using Xamarin.Essentials;
using static SignApk.SignApk;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class PatchTasks : BaseTasks
	{
		private static readonly Lazy<PatchTasks> instance = new Lazy<PatchTasks>(() => new PatchTasks());

		private bool _saveDataBackupFromOldPatch;

		private PatchTasks()
		{
		}

		public static bool IsInstantiated => instance.IsValueCreated;

		public static PatchTasks Instance => instance.Value;

		public async Task FinishPatch(bool processSavedata, CancellationToken token)
		{
			try
			{
				IsRunning = true;

				Java.IO.File backupSavedata = null;
				var installedObb = new Java.IO.File(FilePaths[Files.ObbToReplace]);

				try
				{
					if (_saveDataBackupFromOldPatch && processSavedata && !ManifestTasks.Instance.CheckPatchUpdate())
					{
						backupSavedata = new Java.IO.File(Path.Combine(OkkeiFilesPathBackup, SavedataBackupFileName));

						if (backupSavedata.Exists())
						{
							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.restore_old_saves));

							backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.BackupSavedata]));
							backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);

							await Utils.CopyFile(backupSavedata.Path, SavedataPath,
								SavedataFileName, token);
						}
					}

					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));

					if (ManifestTasks.Instance.CheckObbUpdate() && installedObb.Exists()) installedObb.Delete();

					if (!ManifestTasks.Instance.CheckPatchUpdate())
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_obb));

					if ((ManifestTasks.Instance.CheckObbUpdate() || !ManifestTasks.Instance.CheckScriptsUpdate()) &&
					    (!installedObb.Exists() || !await Utils.CompareMD5(Files.ObbToReplace, token)))
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.download_obb));

						try
						{
							await Utils.DownloadFile(GlobalManifest.Obb.URL, ObbPath, ObbFileName, token);
						}
						catch (Exception ex) when (!(ex is OperationCanceledException))
						{
							OnMessageGenerated(this,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.http_file_download_error),
									Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
									null, null));
							OnErrorOccurred(this, EventArgs.Empty);
						}

						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_obb_md5));

						var obbHash = await Utils.CalculateMD5(installedObb.Path, token);
						if (obbHash != GlobalManifest.Obb.MD5)
							OnMessageGenerated(this,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.hash_obb_mismatch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
									() =>
									{
										OnErrorOccurred(this, EventArgs.Empty);
										throw new OperationCanceledException("The operation was canceled.", token);
									}, null));

						Preferences.Set(Prefkey.downloaded_obb_md5.ToString(), obbHash);
						Preferences.Set(Prefkey.obb_version.ToString(), GlobalManifest.Obb.Version);
					}

					Preferences.Set(Prefkey.apk_is_patched.ToString(), true);

					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.patch_success));
				}
				catch (OperationCanceledException)
				{
					backupSavedata?.Delete();

					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
				}
				finally
				{
					backupSavedata?.Dispose();
					installedObb.Dispose();
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
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

				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));

				var originalSavedata = new Java.IO.File(FilePaths[Files.OriginalSavedata]);
				var backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
				var unpatchedApk = new Java.IO.File(FilePaths[Files.TempApk]);
				var backupApk = new Java.IO.File(FilePaths[Files.BackupApk]);
				var scriptsZip = new Java.IO.File(FilePaths[Files.Scripts]);
				var originalObb = new Java.IO.File(FilePaths[Files.ObbToBackup]);
				var backupObb = new Java.IO.File(FilePaths[Files.BackupObb]);

				try
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.checking));

					var isPatched =
						Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

					if (isPatched && !ManifestTasks.Instance.CheckPatchUpdate())
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.error_patched),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						OnErrorOccurred(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					if (!Utils.IsAppInstalled(ChaosChildPackageName))
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.cc_not_found),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						OnErrorOccurred(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					if (Android.OS.Environment.ExternalStorageDirectory.UsableSpace < TwoGb)
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.no_free_space_patch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
						OnErrorOccurred(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}


					// Backup save data
					if (processSavedata && !ManifestTasks.Instance.CheckPatchUpdate())
					{
						OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));

						if (backupSavedata.Exists())
							if (await Utils.CompareMD5(Files.BackupSavedata, token))
							{
								_saveDataBackupFromOldPatch = true;
								backupSavedata.RenameTo(new Java.IO.File(FilePaths[Files.SAVEDATA_BACKUP]));
								backupSavedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
							}

						if (originalSavedata.Exists())
						{
							OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_saves));

							if (!await Utils.CompareMD5(Files.OriginalSavedata, token))
							{
								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.backup_saves));

								await Utils.CopyFile(originalSavedata.Path,
									backupSavedata.Parent, backupSavedata.Name, token);

								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.write_saves_md5));

								Preferences.Set(Prefkey.savedata_md5.ToString(),
									await Utils.CalculateMD5(originalSavedata.Path, token));
							}
						}
						else
						{
							OnMessageGenerated(this,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
									activity.Resources.GetText(Resource.String.saves_not_found_patch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
									null, null));
						}
					}

					if ((!ManifestTasks.Instance.CheckObbUpdate() || ManifestTasks.Instance.CheckScriptsUpdate()) &&
						(!File.Exists(FilePaths[Files.SignedApk]) || !backupApk.Exists()))
					{
						// Get installed CHAOS;CHILD APK
						var originalApkPath = Application.Context.PackageManager
							.GetPackageInfo(ChaosChildPackageName, 0)
							.ApplicationInfo
							.PublicSourceDir;

						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.copy_apk));

						await Utils.CopyFile(originalApkPath, unpatchedApk.Parent,
							unpatchedApk.Name, token);


						// Backup APK
						if (unpatchedApk.Exists() && !ManifestTasks.Instance.CheckPatchUpdate())
						{
							OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));

							if (!backupApk.Exists() || !await Utils.CompareMD5(Files.TempApk, token))
							{
								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.backup_apk));

								await Utils.CopyFile(originalApkPath, backupApk.Parent,
									backupApk.Name, token);

								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.write_apk_md5));

								Preferences.Set(Prefkey.backup_apk_md5.ToString(),
									await Utils.CalculateMD5(backupApk.Path, token));
							}
						}

						if (ManifestTasks.Instance.CheckScriptsUpdate() || !ManifestTasks.Instance.CheckObbUpdate())
						{
							// Download scripts
							OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.compare_scripts));

							if (!scriptsZip.Exists() || !await Utils.CompareMD5(Files.Scripts, token))
							{
								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.download_scripts));

								try
								{
									await Utils.DownloadFile(GlobalManifest.Scripts.URL, scriptsZip.Parent,
										scriptsZip.Name, token);
								}
								catch (Exception ex) when (!(ex is OperationCanceledException))
								{
									OnMessageGenerated(this,
										new MessageBox.Data(
											Application.Context.Resources.GetText(Resource.String.error),
											Application.Context.Resources.GetText(Resource.String
												.http_file_download_error),
											Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
											null, null));
									OnErrorOccurred(this, EventArgs.Empty);
								}

								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.write_scripts_md5));

								var scriptsHash = await Utils.CalculateMD5(scriptsZip.Path, token);
								if (scriptsHash != GlobalManifest.Scripts.MD5)
									OnMessageGenerated(this,
										new MessageBox.Data(
											Application.Context.Resources.GetText(Resource.String.error),
											Application.Context.Resources.GetText(Resource.String
												.hash_scripts_mismatch),
											Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
											() =>
											{
												OnErrorOccurred(this, EventArgs.Empty);
												throw new OperationCanceledException("The operation was canceled.",
													token);
											}, null));

								Preferences.Set(Prefkey.scripts_md5.ToString(), scriptsHash);
								Preferences.Set(Prefkey.scripts_version.ToString(), GlobalManifest.Scripts.Version);
							}


							// Extract scripts
							OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));

							var fastZip = new FastZip();
							string fileFilter = null;

							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.extract_scripts));

							fastZip.ExtractZip(scriptsZip.Path, Path.Combine(OkkeiFilesPath, "scripts"),
								fileFilter);

							OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));


							// Replace scripts
							var filePaths = Directory.GetFiles(Path.Combine(OkkeiFilesPath, "scripts"));
							var scriptsCount = filePaths.Length;

							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.replace_scripts));

							var zipFile = new ZipFile(unpatchedApk.Path);

							zipFile.BeginUpdate();

							var progress = 0;
							foreach (var scriptfile in filePaths)
							{
								zipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));
								++progress;
								OnProgressChanged(this, new ProgressChangedEventArgs(progress, scriptsCount));
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
							OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.sign_apk));

							var apkToSign = new FileStream(FilePaths[Files.TempApk], FileMode.Open);
							var signedApkStream =
								new FileStream(FilePaths[Files.SignedApk], FileMode.OpenOrCreate);
							var signWholeFile = false;

							SignPackage(apkToSign, Testkey, signedApkStream, signWholeFile);

							apkToSign.Dispose();
							signedApkStream.Dispose();

							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.write_patched_apk_md5));

							Preferences.Set(Prefkey.signed_apk_md5.ToString(),
								await Utils.CalculateMD5(FilePaths[Files.SignedApk], token));

							if (unpatchedApk.Exists()) unpatchedApk.Delete();

							if (token.IsCancellationRequested)
							{
								if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);
								throw new OperationCanceledException("The operation was canceled.", token);
							}
						}
					}

					if (!ManifestTasks.Instance.CheckPatchUpdate())
					{
						// Backup OBB
						OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));

						if (originalObb.Exists())
						{
							OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_obb));

							if (!backupObb.Exists() || !await Utils.CompareMD5(Files.ObbToBackup, token))
							{
								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.backup_obb));

								await Utils.CopyFile(originalObb.Path, backupObb.Parent,
									backupObb.Name, token);

								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.write_obb_md5));

								Preferences.Set(Prefkey.backup_obb_md5.ToString(),
									await Utils.CalculateMD5(backupObb.Path, token));
							}
						}
						else
						{
							OnMessageGenerated(this,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.obb_not_found_patch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null,
									null));
							OnErrorOccurred(this, EventArgs.Empty);
							throw new OperationCanceledException("The operation was canceled.", token);
						}
					}

					OnStatusChanged(null, string.Empty);

					if (!ManifestTasks.Instance.CheckPatchUpdate())
						// Uninstall and install patched CHAOS;CHILD, then restore save data if exists and checked, after that download OBB
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
								Application.Context.Resources.GetText(Resource.String.uninstall_prompt_patch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								() => Utils.UninstallPackage(activity, ChaosChildPackageName), null));
					else if (ManifestTasks.Instance.CheckScriptsUpdate())
						Utils.OnUninstallResult(activity, token);
					else if (ManifestTasks.Instance.CheckObbUpdate())
						Utils.OnInstallSuccess(false, token);

					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
				}
				catch (OperationCanceledException)
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));

					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
				finally
				{
					originalSavedata.Dispose();
					backupSavedata.Dispose();
					unpatchedApk.Dispose();
					backupApk.Dispose();
					if (scriptsZip.Exists()) scriptsZip.Delete();
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