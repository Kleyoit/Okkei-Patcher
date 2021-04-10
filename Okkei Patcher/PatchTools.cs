using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using ICSharpCode.SharpZipLib.Zip;
using Xamarin.Essentials;
using static SignApk.SignApk;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class PatchTools : ToolsBase
	{
		private bool _saveDataBackupFromOldPatch;

		public PatchTools(Utils utils) : base(utils)
		{
		}

		protected override async Task Finish(CancellationToken token)
		{
			if (!IsRunning) return;

			if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);

			try
			{
				if (_saveDataBackupFromOldPatch && ProcessState.ProcessSavedata && !ProcessState.PatchUpdate &&
				    File.Exists(FilePaths[Files.SAVEDATA_BACKUP]))
				{
					OnStatusChanged(this,
						Application.Context.Resources.GetText(Resource.String.restore_old_saves));

					if (File.Exists(FilePaths[Files.BackupSavedata])) File.Delete(FilePaths[Files.BackupSavedata]);
					File.Move(FilePaths[Files.SAVEDATA_BACKUP], FilePaths[Files.BackupSavedata]);

					await UtilsInstance.CopyFile(FilePaths[Files.BackupSavedata], SavedataPath,
						SavedataFileName, token).ConfigureAwait(false);
				}

				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));

				if (ProcessState.ObbUpdate && File.Exists(FilePaths[Files.ObbToReplace]))
					File.Delete(FilePaths[Files.ObbToReplace]);

				if (!ProcessState.PatchUpdate)
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_obb));

				if ((ProcessState.ObbUpdate || !ProcessState.ScriptsUpdate) &&
				    (!File.Exists(FilePaths[Files.ObbToReplace]) ||
				     !await UtilsInstance.CompareMD5(Files.ObbToReplace, token).ConfigureAwait(false)))
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.download_obb));

					try
					{
						await UtilsInstance.DownloadFile(GlobalManifest.Obb.URL, ObbPath, ObbFileName, token)
							.ConfigureAwait(false);
					}
					catch (Exception ex) when (!(ex is OperationCanceledException))
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.http_file_download_error),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						OnErrorOccurred(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.write_obb_md5));

					var obbHash = await UtilsInstance.CalculateMD5(FilePaths[Files.ObbToReplace], token)
						.ConfigureAwait(false);
					if (obbHash != GlobalManifest.Obb.MD5)
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.hash_obb_mismatch),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						OnErrorOccurred(this, EventArgs.Empty);
						throw new OperationCanceledException("The operation was canceled.", token);
					}

					Preferences.Set(Prefkey.downloaded_obb_md5.ToString(), obbHash);
					Preferences.Set(Prefkey.obb_version.ToString(), GlobalManifest.Obb.Version);
				}

				Preferences.Set(Prefkey.apk_is_patched.ToString(), true);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.patch_success));
			}
			catch (OperationCanceledException)
			{
				if (_saveDataBackupFromOldPatch && ProcessState.ProcessSavedata && !ProcessState.PatchUpdate &&
				    File.Exists(FilePaths[Files.BackupSavedata]))
					File.Delete(FilePaths[Files.BackupSavedata]);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
			}
			finally
			{
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
				IsRunning = false;
			}
		}

		public void Start(Activity activity, ProcessState processState, CancellationToken token)
		{
			Task.Run(() => StartPrivate(activity, processState, token).OnException(WriteBugReport));
		}

		private async Task StartPrivate(Activity activity, ProcessState processState, CancellationToken token)
		{
			IsRunning = true;
			_saveDataBackupFromOldPatch = false;

			ProcessState = processState;

			OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));

			// Read testcert for signing APK
			var assets = Application.Context.Assets;
			var testkeyFile = assets?.Open(CertFileName);
			var testkeySize = 2797;

			var testkeyTemp =
				new X509Certificate2(UtilsInstance.ReadCert(testkeyFile, testkeySize), CertPassword);
			Testkey = testkeyTemp;

			testkeyFile?.Close();
			testkeyFile?.Dispose();

			try
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.checking));

				var isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

				if (isPatched && !ProcessState.PatchUpdate)
				{
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.error_patched),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
					OnErrorOccurred(this, EventArgs.Empty);
					throw new OperationCanceledException("The operation was canceled.", token);
				}

				if (!UtilsInstance.IsAppInstalled(ChaosChildPackageName))
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
				if (ProcessState.ProcessSavedata && !ProcessState.PatchUpdate)
				{
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));

					if (File.Exists(FilePaths[Files.BackupSavedata]) &&
					    await UtilsInstance.CompareMD5(Files.BackupSavedata, token).ConfigureAwait(false))
					{
						_saveDataBackupFromOldPatch = true;
						if (File.Exists(FilePaths[Files.SAVEDATA_BACKUP]))
							File.Delete(FilePaths[Files.SAVEDATA_BACKUP]);
						File.Move(FilePaths[Files.BackupSavedata], FilePaths[Files.SAVEDATA_BACKUP]);
					}

					if (File.Exists(FilePaths[Files.OriginalSavedata]))
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_saves));

						if (!await UtilsInstance.CompareMD5(Files.OriginalSavedata, token).ConfigureAwait(false))
						{
							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.backup_saves));

							await UtilsInstance.CopyFile(FilePaths[Files.OriginalSavedata], OkkeiFilesPathBackup,
								SavedataFileName,
								token).ConfigureAwait(false);

							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.write_saves_md5));

							Preferences.Set(Prefkey.savedata_md5.ToString(),
								await UtilsInstance.CalculateMD5(FilePaths[Files.OriginalSavedata], token)
									.ConfigureAwait(false));
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

				if ((!ProcessState.ObbUpdate || ProcessState.ScriptsUpdate) &&
				    (!File.Exists(FilePaths[Files.SignedApk]) || !File.Exists(FilePaths[Files.BackupApk])))
				{
					// Get installed CHAOS;CHILD APK
					var originalApkPath = Application.Context.PackageManager
						.GetPackageInfo(ChaosChildPackageName, 0)
						.ApplicationInfo
						.PublicSourceDir;

					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.copy_apk));

					await UtilsInstance.CopyFile(originalApkPath, OkkeiFilesPath, TempApkFileName, token)
						.ConfigureAwait(false);


					// Backup APK
					if (File.Exists(FilePaths[Files.TempApk]) && !ProcessState.PatchUpdate)
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));

						if (!File.Exists(FilePaths[Files.BackupApk]) ||
						    !await UtilsInstance.CompareMD5(Files.TempApk, token).ConfigureAwait(false))
						{
							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.backup_apk));

							await UtilsInstance.CopyFile(originalApkPath, OkkeiFilesPathBackup, BackupApkFileName,
									token)
								.ConfigureAwait(false);

							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.write_apk_md5));

							Preferences.Set(Prefkey.backup_apk_md5.ToString(),
								await UtilsInstance.CalculateMD5(FilePaths[Files.BackupApk], token)
									.ConfigureAwait(false));
						}
					}

					if (ProcessState.ScriptsUpdate || !ProcessState.ObbUpdate)
					{
						// Download scripts
						OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
						OnStatusChanged(this,
							Application.Context.Resources.GetText(Resource.String.compare_scripts));

						if (!File.Exists(FilePaths[Files.Scripts]) ||
						    !await UtilsInstance.CompareMD5(Files.Scripts, token).ConfigureAwait(false))
						{
							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.download_scripts));

							try
							{
								await UtilsInstance.DownloadFile(GlobalManifest.Scripts.URL, OkkeiFilesPath,
									ScriptsFileName, token).ConfigureAwait(false);
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
								throw new OperationCanceledException("The operation was canceled.", token);
							}

							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.write_scripts_md5));

							var scriptsHash =
								await UtilsInstance.CalculateMD5(FilePaths[Files.Scripts], token)
									.ConfigureAwait(false);
							if (scriptsHash != GlobalManifest.Scripts.MD5)
							{
								OnStatusChanged(this,
									Application.Context.Resources.GetText(Resource.String.aborted));
								OnMessageGenerated(this,
									new MessageBox.Data(
										Application.Context.Resources.GetText(Resource.String.error),
										Application.Context.Resources.GetText(Resource.String
											.hash_scripts_mismatch),
										Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
										null, null));
								OnErrorOccurred(this, EventArgs.Empty);
								throw new OperationCanceledException("The operation was canceled.", token);
							}

							Preferences.Set(Prefkey.scripts_md5.ToString(), scriptsHash);
							Preferences.Set(Prefkey.scripts_version.ToString(), GlobalManifest.Scripts.Version);
						}


						// Extract scripts
						OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));

						var fastZip = new FastZip();
						string fileFilter = null;

						OnStatusChanged(this,
							Application.Context.Resources.GetText(Resource.String.extract_scripts));

						fastZip.ExtractZip(FilePaths[Files.Scripts], Path.Combine(OkkeiFilesPath, "scripts"),
							fileFilter);

						OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));


						// Replace scripts
						var filePaths = Directory.GetFiles(Path.Combine(OkkeiFilesPath, "scripts"));
						var scriptsCount = filePaths.Length;

						OnStatusChanged(this,
							Application.Context.Resources.GetText(Resource.String.replace_scripts));

						var zipFile = new ZipFile(FilePaths[Files.TempApk]);

						zipFile.BeginUpdate();

						var progress = 0;
						foreach (var scriptfile in filePaths)
						{
							zipFile.Add(scriptfile, "assets/script/" + Path.GetFileName(scriptfile));
							++progress;
							OnProgressChanged(this, new ProgressChangedEventArgs(progress, scriptsCount, false));
						}

						OnProgressChanged(this, new ProgressChangedEventArgs(progress, scriptsCount, true));

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
							if (File.Exists(FilePaths[Files.TempApk])) File.Delete(FilePaths[Files.TempApk]);
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
							await UtilsInstance.CalculateMD5(FilePaths[Files.SignedApk], token)
								.ConfigureAwait(false));

						if (File.Exists(FilePaths[Files.TempApk])) File.Delete(FilePaths[Files.TempApk]);

						if (token.IsCancellationRequested)
						{
							if (File.Exists(FilePaths[Files.SignedApk])) File.Delete(FilePaths[Files.SignedApk]);
							throw new OperationCanceledException("The operation was canceled.", token);
						}
					}
				}

				if (!ProcessState.PatchUpdate)
				{
					// Backup OBB
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));

					if (File.Exists(FilePaths[Files.ObbToBackup]))
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_obb));

						if (!File.Exists(FilePaths[Files.BackupObb]) ||
						    !await UtilsInstance.CompareMD5(Files.ObbToBackup, token).ConfigureAwait(false))
						{
							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.backup_obb));

							await UtilsInstance.CopyFile(FilePaths[Files.ObbToBackup], OkkeiFilesPathBackup,
									ObbFileName, token)
								.ConfigureAwait(false);

							OnStatusChanged(this,
								Application.Context.Resources.GetText(Resource.String.write_obb_md5));

							Preferences.Set(Prefkey.backup_obb_md5.ToString(),
								await UtilsInstance.CalculateMD5(FilePaths[Files.BackupObb], token)
									.ConfigureAwait(false));
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

				OnStatusChanged(this, string.Empty);
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, true));

				if (!ProcessState.PatchUpdate)
				{
					// Uninstall and install patched CHAOS;CHILD, then restore save data if exists and checked, after that download OBB
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
							Application.Context.Resources.GetText(Resource.String.uninstall_prompt_patch),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							() => UtilsInstance.UninstallPackage(activity, ChaosChildPackageName), null));
					return;
				}

				if (ProcessState.ScriptsUpdate)
				{
					await OnUninstallResult(activity, token).ConfigureAwait(false);
					return;
				}

				if (ProcessState.ObbUpdate) OnInstallSuccess(token);
			}
			catch (OperationCanceledException)
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));

				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
				IsRunning = false;
			}
			finally
			{
				if (File.Exists(FilePaths[Files.Scripts])) File.Delete(FilePaths[Files.Scripts]);
			}
		}

		public override async Task OnUninstallResult(Activity activity, CancellationToken token)
		{
			if (!CheckUninstallSuccess()) return;

			// Install APK
			var apkMd5 = string.Empty;

			if (!IsRunning) return;

			if (Preferences.ContainsKey(Prefkey.signed_apk_md5.ToString()))
				apkMd5 = Preferences.Get(Prefkey.signed_apk_md5.ToString(), string.Empty);
			var path = FilePaths[Files.SignedApk];
			var message = Application.Context.Resources.GetText(Resource.String.install_prompt_patch);

			try
			{
				if (File.Exists(path))
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));

					var apkFileMd5 = await UtilsInstance.CalculateMD5(path, token).ConfigureAwait(false);

					if (apkMd5 == apkFileMd5)
					{
						OnStatusChanged(this,
							Application.Context.Resources.GetText(Resource.String.installing));

						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
								message, Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								() => MainThread.BeginInvokeOnMainThread(() =>
									UtilsInstance.InstallPackage(activity,
										Android.Net.Uri.FromFile(new Java.IO.File(path)))),
								null));
						return;
					}

					File.Delete(path);

					OnMessageGenerated(this, new MessageBox.Data(
						Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.not_trustworthy_apk_patch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok),
						null,
						null, null));

					OnErrorOccurred(this, EventArgs.Empty);
					throw new OperationCanceledException("The operation was canceled.", token);
				}

				OnMessageGenerated(this,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
						Application.Context.Resources.GetText(Resource.String.apk_not_found_patch),
						Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
						null, null));

				OnErrorOccurred(this, EventArgs.Empty);
				throw new OperationCanceledException("The operation was canceled.", token);
			}
			catch (OperationCanceledException)
			{
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
				IsRunning = false;
			}
		}
	}
}