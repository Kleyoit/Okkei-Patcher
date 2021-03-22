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
	internal class Utils
	{
		private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() => new HttpClient());

		public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public event EventHandler<MessageBox.Data> MessageGenerated;
		public event EventHandler ErrorOccurred;
		public event EventHandler InstallFailed;

		public Task<string> CalculateMD5(string filename, CancellationToken token)
		{
			using var md5 = MD5.Create();
			using var stream = System.IO.File.OpenRead(filename);

			const int bufferLength = 0x100000;
			var buffer = new byte[bufferLength];
			int length;

			ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100, false));

			var progressMax = (int) Math.Ceiling((double) stream.Length / bufferLength);
			var progress = 0;

			while ((length = stream.Read(buffer)) > 0 && !token.IsCancellationRequested)
			{
				++progress;
				if (progress != progressMax)
					md5.TransformBlock(buffer, 0, bufferLength, buffer, 0);
				else
					md5.TransformFinalBlock(buffer, 0, length);
				ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(progress, progressMax, false));
			}

			token.ThrowIfCancellationRequested();

			return Task.FromResult(BitConverter.ToString(md5.Hash).Replace("-", string.Empty).ToLowerInvariant());
		}

		/// <summary>
		///     Compares given file with a predefined corresponding file or corresponding checksum written in preferences and
		///     returns true if checksums are equal, false otherwise. See predefined values in <see cref="FileToCompareWith" />.
		/// </summary>
		public async Task<bool> CompareMD5(Files file, CancellationToken token)
		{
			var result = false;

			var firstMd5 = string.Empty;
			var secondMd5 = string.Empty;

			switch (file)
			{
				case Files.OriginalSavedata:
					if (System.IO.File.Exists(FileToCompareWith[file]))
						secondMd5 = await CalculateMD5(FileToCompareWith[file], token).ConfigureAwait(false);
					break;
				default:
					secondMd5 = Preferences.Get(FileToCompareWith[file], string.Empty);
					break;
			}

			if (System.IO.File.Exists(FilePaths[file]) && secondMd5 != string.Empty)
				firstMd5 = await CalculateMD5(FilePaths[file], token).ConfigureAwait(false);

			if (firstMd5 == secondMd5 && firstMd5 != string.Empty && secondMd5 != string.Empty) result = true;

			return result;
		}

		public byte[] ReadCert(Stream certStream, int size)
		{
			if (certStream == null) throw new ArgumentNullException(nameof(certStream));
			var data = new byte[size];
			size = certStream.Read(data, 0, size);
			certStream.Close();
			return data;
		}

		public bool IsAppInstalled(string packageName)
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

		private void AddApkToInstallSession(Android.Net.Uri apkUri, PackageInstaller.Session session)
		{
			var packageInSession = session.OpenWrite("package", 0, -1);
			FileStream input = null;
			if (apkUri.Path != null) input = new FileStream(apkUri.Path, FileMode.Open);

			try
			{
				if (input != null) input.CopyTo(packageInSession);
				else throw new Exception("InputStream is null");
			}
			finally
			{
				packageInSession.Close();
				input?.Close();
			}

			// That this is necessary could be a Xamarin bug
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		public void InstallPackage(Activity activity, Android.Net.Uri apkUri)
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
				session.Commit(statusReceiver);
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

		private void OnInstallFailed(object sender, EventArgs e)
		{
			InstallFailed?.Invoke(this, e);
			((PackageInstallObserver) sender).InstallFailed -= OnInstallFailed;
		}

		public void UninstallPackage(Activity activity, string packageName)
		{
			var packageUri = Android.Net.Uri.Parse("package:" + packageName);
			var uninstallIntent = new Intent(Intent.ActionDelete, packageUri);
			activity.StartActivityForResult(uninstallIntent, (int) RequestCodes.UninstallCode);
		}

		public Task CopyFile(string inFilePath, string outFilePath, string outFileName, CancellationToken token)
		{
			const int bufferLength = 0x14000;
			var buffer = new byte[bufferLength];
			int length;

			ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100, false));

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
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(progress, progressMax, false));
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
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(progress, progressMax, false));
				}

				inputFile.Dispose();
				javaInputStream.Dispose();
			}

			output.Dispose();

			var outFile = Path.Combine(outFilePath, outFileName);
			if (token.IsCancellationRequested && System.IO.File.Exists(outFile)) System.IO.File.Delete(outFile);

			token.ThrowIfCancellationRequested();

			return Task.CompletedTask;
		}

		public async Task DownloadFile(string URL, string outFilePath, string outFileName,
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
				var response = await Client.Value.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead)
					.ConfigureAwait(false);
				var contentLength = -1;

				if (response.StatusCode == HttpStatusCode.OK)
				{
					contentLength = (int?) response.Content.Headers.ContentLength ?? int.MaxValue;
					download = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
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
					ErrorOccurred?.Invoke(null, EventArgs.Empty);
				}

				while ((length = download.Read(buffer)) > 0 && !token.IsCancellationRequested)
				{
					output.Write(buffer, 0, length);
					ProgressChanged?.Invoke(null,
						new ProgressChangedEventArgs((int) output.Length, contentLength, false));
				}
			}
			finally
			{
				//await Task.Delay(1);    // Xamarin debugger bug workaround
				download?.Dispose();
				output.Dispose();
				var downloadedFile = Path.Combine(outFilePath, outFileName);
				if (token.IsCancellationRequested && System.IO.File.Exists(downloadedFile))
					System.IO.File.Delete(downloadedFile);
				token.ThrowIfCancellationRequested();
			}
		}

		public string GetBugReportText(Exception ex)
		{
			return
				$"-------------------------\nVersion Code: {AppInfo.BuildString}\nVersion Name: {AppInfo.VersionString}\n-------------------------\nDevice Info\n-------------------------\n{GetDeviceInfo()}\n-------------------------\nException Stack Trace\n-------------------------\n{(ex != null ? ex.Message : "None")}\n\n{(ex != null ? ex.StackTrace : "None")}";
		}

		public string GetDeviceInfo()
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

		public static bool IsBackupAvailable()
		{
			return System.IO.File.Exists(FilePaths[Files.BackupApk]) &&
			       System.IO.File.Exists(FilePaths[Files.BackupObb]);
		}

		public static void ClearOkkeiFiles()
		{
			if (Directory.Exists(OkkeiFilesPath)) RecursiveClearFiles(OkkeiFilesPath);
			if (System.IO.File.Exists(ManifestPath)) System.IO.File.Delete(ManifestPath);
			if (System.IO.File.Exists(ManifestBackupPath)) System.IO.File.Delete(ManifestBackupPath);
		}

		public static void RecursiveClearFiles(string path)
		{
			var files = Directory.GetFiles(path);
			if (files.Length > 0)
				foreach (var file in files)
					System.IO.File.Delete(file);
			var directories = Directory.GetDirectories(path);
			if (directories.Length > 0)
				foreach (var dir in directories)
				{
					RecursiveClearFiles(dir);
					Directory.Delete(dir);
				}
		}

		public static int GetPatchSizeInMB()
		{
			var scriptsSize = (int) Math.Round(GlobalManifest.Scripts.Size / (double) 0x100000);
			var obbSize = (int) Math.Round(GlobalManifest.Obb.Size / (double) 0x100000);
			return scriptsSize + obbSize;
		}

		public static double GetAppUpdateSizeInMB()
		{
			return Math.Round(GlobalManifest.OkkeiPatcher.Size / (double) 0x100000, 2);
		}
	}
}