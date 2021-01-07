using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.IO;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal static class Utils
	{
		private static readonly HttpClient Client = new HttpClient();

		public static event EventHandler<string> StatusChanged;
		public static event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public static event EventHandler<MessageBox.Data> MessageGenerated;
		public static event EventHandler TokenErrorOccurred;
		public static event EventHandler TaskErrorOccurred;

		public static Task<string> CalculateMD5(string filename, CancellationToken token)
		{
			using var md5 = MD5.Create();
			using var stream = System.IO.File.OpenRead(filename);

			const int bufferLength = 0x100000;
			var buffer = new byte[bufferLength];
			int length;

			ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100));

			var progressMax = (int) Math.Ceiling((double) stream.Length / bufferLength);
			var progress = 0;

			while ((length = stream.Read(buffer)) > 0 && !token.IsCancellationRequested)
			{
				++progress;
				if (progress != progressMax)
					md5.TransformBlock(buffer, 0, bufferLength, buffer, 0);
				else
					md5.TransformFinalBlock(buffer, 0, length);
				ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(progress, progressMax));
			}

			token.ThrowIfCancellationRequested();

			return Task.FromResult(BitConverter.ToString(md5.Hash).Replace("-", string.Empty).ToLowerInvariant());
		}

		/// <summary>
		///     Compares given file with a predefined corresponding file or corresponding checksum written in preferences and
		///     returns true if checksums are equal, false otherwise. See predefined values in <see cref="FileToCompareWith" />.
		/// </summary>
		public static async Task<bool> CompareMD5(Files file, CancellationToken token)
		{
			var result = false;

			var firstFile = new Java.IO.File(FilePaths[file]);
			Java.IO.File secondFile = null;

			try
			{
				var firstMd5 = string.Empty;
				var secondMd5 = string.Empty;

				switch (file)
				{
					case Files.OriginalSavedata:
						secondFile = new Java.IO.File(FileToCompareWith[file]);
						if (secondFile.Exists()) secondMd5 = await CalculateMD5(secondFile.Path, token);
						break;
					default:
						secondMd5 = Preferences.Get(FileToCompareWith[file], string.Empty);
						break;
				}

				if (firstFile.Exists() && secondMd5 != string.Empty)
					firstMd5 = await CalculateMD5(firstFile.Path, token);

				if (firstMd5 == secondMd5 && firstMd5 != string.Empty && secondMd5 != string.Empty) result = true;
			}
			finally
			{
				firstFile.Dispose();
				secondFile?.Dispose();
			}

			return result;
		}

		public static byte[] ReadCert(Stream certStream, int size)
		{
			var data = new byte[size];
			size = certStream.Read(data, 0, size);
			certStream.Close();
			return data;
		}

		public static bool IsAppInstalled(string packageName)
		{
			try
			{
				Application.Context.PackageManager.GetPackageInfo(packageName, PackageInfoFlags.Activities);
				return true;
			}
			catch (PackageManager.NameNotFoundException)
			{
				return false;
			}
		}

		private static void AddApkToInstallSession(Android.Net.Uri apkUri, PackageInstaller.Session session)
		{
			var packageInSession = session.OpenWrite("package", 0, -1);
			FileStream input = null;
			if (apkUri.Path != null) input = new FileStream(apkUri.Path, FileMode.Open);

			try
			{
				if (input != null && packageInSession != null) input.CopyTo(packageInSession);
				else throw new Exception("InputStream and/or session is null");
			}
			finally
			{
				packageInSession?.Close();
				input?.Close();
			}

			// That this is necessary could be a Xamarin bug
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		public static void InstallPackage(Activity activity, Android.Net.Uri apkUri)
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
			{
				var packageInstaller = Application.Context.PackageManager.PackageInstaller;
				var sessionParams = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
				var sessionId = packageInstaller.CreateSession(sessionParams);
				var session = packageInstaller.OpenSession(sessionId);

				AddApkToInstallSession(apkUri, session);

				// Create an install status receiver
				var intent = new Intent(activity, activity.Class);
				intent.SetAction(ActionPackageInstalled);

				var pendingIntent =
					PendingIntent.GetActivity(activity, (int) RequestCodes.PendingIntentInstallCode, intent,
						PendingIntentFlags.UpdateCurrent);

				var observer = new PackageInstallObserver(packageInstaller);
				observer.InstallFailed += OnInstallFailed;
				packageInstaller.RegisterSessionCallback(observer);

				var statusReceiver = pendingIntent?.IntentSender;

				// Commit the session (this will start the installation workflow)
				session?.Commit(statusReceiver);
			}
			else
			{
				var intent = new Intent(Intent.ActionInstallPackage);
				intent.SetData(apkUri);
				intent.SetFlags(ActivityFlags.GrantReadUriPermission);
				intent.PutExtra(Intent.ExtraNotUnknownSource, false);
				intent.PutExtra(Intent.ExtraReturnResult, true);
				intent.PutExtra(Intent.ExtraInstallerPackageName, AppInfo.PackageName);
				activity.StartActivityForResult(intent, (int) RequestCodes.KitKatInstallCode);
			}
		}

		private static void OnInstallFailed(object sender, EventArgs e)
		{
			NotifyInstallFailed();
			((PackageInstallObserver) sender).InstallFailed -= OnInstallFailed;
		}

		public static void NotifyInstallFailed()
		{
			ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100));
			StatusChanged?.Invoke(null, Application.Context.Resources.GetText(Resource.String.aborted));
			MessageGenerated?.Invoke(null, new MessageBox.Data(
				Application.Context.Resources.GetText(Resource.String.error),
				Application.Context.Resources.GetText(Resource.String.install_error),
				Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
				null, null));
			TaskErrorOccurred?.Invoke(null, EventArgs.Empty);
		}

		public static void OnInstallSuccess(bool processSavedata, CancellationToken token)
		{
			if (PatchTasks.Instance.IsRunning)
			{
				if (System.IO.File.Exists(FilePaths[Files.SignedApk]))
					System.IO.File.Delete(FilePaths[Files.SignedApk]);
				Task.Run(() => PatchTasks.Instance.FinishPatch(processSavedata, token));
			}
			else if (UnpatchTasks.Instance.IsRunning)
			{
				Task.Run(() => UnpatchTasks.Instance.RestoreFiles(processSavedata, token));
			}
			else if (ManifestTasks.Instance.IsRunning)
			{
				//if (System.IO.File.Exists(AppUpdatePath)) System.IO.File.Delete(AppUpdatePath);
				TaskErrorOccurred?.Invoke(null, EventArgs.Empty);
			}
		}

		public static void UninstallPackage(Activity activity, string packageName)
		{
			var packageUri = Android.Net.Uri.Parse("package:" + packageName);
			var uninstallIntent = new Intent(Intent.ActionDelete, packageUri);
			activity.StartActivityForResult(uninstallIntent, (int) RequestCodes.UninstallCode);
		}

		public static async void OnUninstallResult(Activity activity, CancellationToken token)
		{
			if (IsAppInstalled(ChaosChildPackageName) && !ManifestTasks.Instance.CheckScriptsUpdate())
			{
				StatusChanged?.Invoke(null, Application.Context.Resources.GetText(Resource.String.aborted));
				MessageGenerated?.Invoke(null, new MessageBox.Data(
					Application.Context.Resources.GetText(Resource.String.error),
					Application.Context.Resources.GetText(Resource.String.uninstall_error),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
					null, null));
				TaskErrorOccurred?.Invoke(null, EventArgs.Empty);
			}
			else
			{
				// Install APK
				var apkMd5 = string.Empty;
				var path = string.Empty;
				var message = string.Empty;

				if (PatchTasks.Instance.IsRunning)
				{
					if (Preferences.ContainsKey(Prefkey.signed_apk_md5.ToString()))
						apkMd5 = Preferences.Get(Prefkey.signed_apk_md5.ToString(), string.Empty);
					path = FilePaths[Files.SignedApk];
					message = Application.Context.Resources.GetText(Resource.String.install_prompt_patch);
				}
				else if (UnpatchTasks.Instance.IsRunning)
				{
					if (Preferences.ContainsKey(Prefkey.backup_apk_md5.ToString()))
						apkMd5 = Preferences.Get(Prefkey.backup_apk_md5.ToString(), string.Empty);
					path = FilePaths[Files.BackupApk];
					message = Application.Context.Resources.GetText(Resource.String.install_prompt_unpatch);
				}

				try
				{
					if (System.IO.File.Exists(path))
					{
						StatusChanged?.Invoke(null, Application.Context.Resources.GetText(Resource.String.compare_apk));

						var apkFileMd5 = await CalculateMD5(path, token);

						if (apkMd5 == apkFileMd5)
						{
							StatusChanged?.Invoke(null,
								Application.Context.Resources.GetText(Resource.String.installing));

							MessageGenerated?.Invoke(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.warning),
									message, Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
									() => MainThread.BeginInvokeOnMainThread(() =>
										InstallPackage(activity, Android.Net.Uri.FromFile(new Java.IO.File(path)))),
									null));
						}
						else
						{
							System.IO.File.Delete(path);

							if (PatchTasks.Instance.IsRunning)
								MessageGenerated?.Invoke(null, new MessageBox.Data(
									Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String
										.not_trustworthy_apk_patch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok),
									null,
									null, null));

							else if (UnpatchTasks.Instance.IsRunning)
								MessageGenerated?.Invoke(null, new MessageBox.Data(
									Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String
										.not_trustworthy_apk_unpatch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok),
									null,
									null, null));

							TokenErrorOccurred?.Invoke(null, EventArgs.Empty);
							throw new System.OperationCanceledException("The operation was canceled.", token);
						}
					}
					else
					{
						if (PatchTasks.Instance.IsRunning)
							MessageGenerated?.Invoke(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.apk_not_found_patch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
									null, null));

						else if (UnpatchTasks.Instance.IsRunning)
							MessageGenerated?.Invoke(null,
								new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
									Application.Context.Resources.GetText(Resource.String.apk_not_found_unpatch),
									Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
									null, null));

						TokenErrorOccurred?.Invoke(null, EventArgs.Empty);
						throw new System.OperationCanceledException("The operation was canceled.", token);
					}
				}
				catch (System.OperationCanceledException)
				{
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100));
					StatusChanged?.Invoke(null, Application.Context.Resources.GetText(Resource.String.aborted));
					TaskErrorOccurred?.Invoke(null, EventArgs.Empty);
				}
			}
		}

		public static Task CopyFile(string inFilePath, string outFilePath, string outFileName, CancellationToken token)
		{
			const int bufferLength = 0x14000;
			var buffer = new byte[bufferLength];
			int length;

			ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100));

			Directory.CreateDirectory(outFilePath);
			var outPath = Path.Combine(outFilePath, outFileName);
			if (System.IO.File.Exists(outPath)) System.IO.File.Delete(outPath);

			var output = new FileStream(outPath, FileMode.OpenOrCreate);

			int progressMax;
			var progress = 0;

			if (inFilePath.StartsWith(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath))
			{
				var inputStream = new FileStream(inFilePath, FileMode.Open);
				progressMax = (int) inputStream.Length / bufferLength;

				while ((length = inputStream.Read(buffer)) > 0 && !token.IsCancellationRequested)
				{
					output.Write(buffer, 0, length);
					++progress;
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(progress, progressMax));
				}

				inputStream.Dispose();
			}
			else
			{
				var inputFile = new Java.IO.File(inFilePath);
				InputStream javaInputStream = new FileInputStream(inputFile);
				progressMax = (int) inputFile.Length() / bufferLength;

				while ((length = javaInputStream.Read(buffer)) > 0 && !token.IsCancellationRequested)
				{
					output.Write(buffer, 0, length);
					++progress;
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(progress, progressMax));
				}

				inputFile.Dispose();
				javaInputStream.Dispose();
			}

			output.Dispose();

			var outFile = new Java.IO.File(Path.Combine(outFilePath, outFileName));
			if (token.IsCancellationRequested && outFile.Exists()) outFile.Delete();
			outFile.Dispose();

			token.ThrowIfCancellationRequested();

			return Task.CompletedTask;
		}

		public static async Task DownloadFile(string URL, string outFilePath, string outFileName,
			CancellationToken token)
		{
			Directory.CreateDirectory(outFilePath);
			var outPath = Path.Combine(outFilePath, outFileName);
			if (System.IO.File.Exists(outPath)) System.IO.File.Delete(outPath);
			var output = new FileStream(outPath, FileMode.OpenOrCreate);

			const int bufferLength = 0x14000;
			var buffer = new byte[bufferLength];
			int length;

			Stream download = null;

			try
			{
				var response = await Client.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead);
				var contentLength = -1;

				if (response.StatusCode == HttpStatusCode.OK)
				{
					contentLength = (int?) response.Content.Headers.ContentLength ?? int.MaxValue;
					download = await response.Content.ReadAsStreamAsync();
				}
				else
				{
					MessageGenerated?.Invoke(null,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Java.Lang.String.Format(
								Application.Context.Resources.GetText(Resource.String.http_file_access_error),
								response.StatusCode.ToString()),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
					TokenErrorOccurred?.Invoke(null, EventArgs.Empty);
				}

				while ((length = download.Read(buffer)) > 0 && !token.IsCancellationRequested)
				{
					output.Write(buffer, 0, length);
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs((int) output.Length, contentLength));
				}
			}
			finally
			{
				//await Task.Delay(1);    // Xamarin debugger bug workaround
				download?.Dispose();
				output.Dispose();
				var downloadedFile = new Java.IO.File(Path.Combine(outFilePath, outFileName));
				if (token.IsCancellationRequested && downloadedFile.Exists()) downloadedFile.Delete();
				downloadedFile.Dispose();
				token.ThrowIfCancellationRequested();
			}
		}

		public static string GetBugReportText(Exception ex)
		{
			return
				$"-------------------------\nVersion Code: {AppInfo.BuildString}\nVersion Name: {AppInfo.VersionString}\n-------------------------\nDevice Info\n-------------------------\n{GetDeviceInfo()}\n-------------------------\nException Stack Trace\n-------------------------\n{(ex != null ? ex.Message : "None")}\n\n{(ex != null ? ex.StackTrace : "None")}";
		}

		public static string GetDeviceInfo()
		{
			var manufacturer = Build.Manufacturer;
			var model = Build.Model;
			var product = Build.Product;
			var incremental = Build.VERSION.Incremental;
			var release = Build.VERSION.Release;
			var sdkInt = Build.VERSION.SdkInt;
			return
				$"manufacturer:       {manufacturer}\nmodel:              {model}\nproduct:            {product}\nincremental:        {incremental}\nrelease:            {release}\nsdkInt:             {sdkInt}";
		}

		public static void WriteBugReport(Exception ex)
		{
			var bugReport = GetBugReportText(ex);
			System.IO.File.WriteAllText(BugReportLogPath, bugReport);
			MessageGenerated?.Invoke(null,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.exception),
					Application.Context.Resources.GetText(Resource.String.exception_notice),
					Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
					() => { System.Environment.Exit(0); }, null));
		}

		public static bool IsBackupAvailable()
		{
			return System.IO.File.Exists(FilePaths[Files.BackupApk]) &&
			       System.IO.File.Exists(FilePaths[Files.BackupObb]);
		}

		public static void ClearOkkeiFiles()
		{
			if (Directory.Exists(OkkeiFilesPath))
			{
				var directories = Directory.GetDirectories(OkkeiFilesPath);
				if (directories.Length > 0)
					foreach (var dir in directories)
					{
						var files = Directory.GetFiles(dir);
						if (files.Length > 0)
							foreach (var file in files)
								System.IO.File.Delete(file);
						Directory.Delete(dir);
					}

				var okkeiFiles = Directory.GetFiles(OkkeiFilesPath);
				if (okkeiFiles.Length > 0)
					foreach (var file in okkeiFiles)
						System.IO.File.Delete(file);
			}

			if (System.IO.File.Exists(ManifestPath)) System.IO.File.Delete(ManifestPath);
			if (System.IO.File.Exists(ManifestBackupPath)) System.IO.File.Delete(ManifestBackupPath);
		}
	}
}