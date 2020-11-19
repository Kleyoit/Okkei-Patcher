﻿using System;
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
using static Patcher.GlobalData;

namespace Patcher
{
	public static class Utils
	{
		private static readonly HttpClient Client = new HttpClient();

		public static event EventHandler<StatusChangedEventArgs> StatusChanged;
		public static event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public static event EventHandler ErrorCanceled;

		public static string CalculateMD5(string filename)
		{
			using (var md5 = MD5.Create())
			{
				using (var stream = System.IO.File.OpenRead(filename))
				{
					var hash = md5.ComputeHash(stream);
					return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
				}
			}
		}

		/// <summary>
		///     Compares given file with a predefined corresponding file or corresponding checksum written in preferences and
		///     returns true if checksums are equal, false otherwise. See predefined values in <see cref="FileToCompareWith" />.
		/// </summary>
		public static bool CompareMD5(Files file)
		{
			var result = false;

			var firstMd5 = "";
			var secondMd5 = "";

			var firstFile = new Java.IO.File(FilePaths[file]);

			switch (file)
			{
				case Files.OriginalSavedata:
					var secondFile = new Java.IO.File(FileToCompareWith[file]);
					if (secondFile.Exists()) secondMd5 = CalculateMD5(secondFile.Path);
					break;
				default:
					secondMd5 = Preferences.Get(FileToCompareWith[file], "");
					break;
			}

			if (firstFile.Exists() && secondMd5 != "") firstMd5 = CalculateMD5(firstFile.Path);
			if (firstMd5 == secondMd5 && firstMd5 != "" && secondMd5 != "") result = true;

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
			var input = new FileStream(apkUri.Path, FileMode.Open);

			try
			{
				if (input != null && packageInSession != null) input.CopyTo(packageInSession);
				else throw new Exception("InputStream or session is null");
			}
			finally
			{
				packageInSession?.Close();
				input.Close();
			}

			// That this is necessary could be a Xamarin bug
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		public static void InstallPackage(Activity callerActivity, Android.Net.Uri apkUri)
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
			{
				var packageInstaller = Application.Context.PackageManager.PackageInstaller;
				var sessionParams = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
				var sessionId = packageInstaller.CreateSession(sessionParams);
				var session = packageInstaller.OpenSession(sessionId);

				AddApkToInstallSession(apkUri, session);

				// Create an install status receiver
				var intent = new Intent(callerActivity, callerActivity.Class);
				intent.SetAction(PACKAGE_INSTALLED_ACTION);
				var pendingIntent =
					PendingIntent.GetActivity(callerActivity, 0, intent, PendingIntentFlags.UpdateCurrent);
				var statusReceiver = pendingIntent.IntentSender;

				// Commit the session (this will start the installation workflow)
				session.Commit(statusReceiver);
			}
		}

		public static async void OnInstallResult()
		{
			StatusChanged?.Invoke(null,
				new StatusChangedEventArgs(
					Application.Context.Resources.GetText(Resource.String.wait_installer),
					MessageBox.Data.Empty));

			await Task.Delay(6000);

			if (!IsAppInstalled(ChaosChildPackageName))
			{
				ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100));

				StatusChanged?.Invoke(null,
					new StatusChangedEventArgs(
						Application.Context.Resources.GetText(Resource.String.aborted),
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.install_error),
							MessageBox.Code.OK)));

				if (PatchTasks.Instance.IsRunning) PatchTasks.Instance.IsRunning = false;
				if (UnpatchTasks.Instance.IsRunning) UnpatchTasks.Instance.IsRunning = false;
			}
		}

		public static void UninstallPackage(Activity callerActivity, string packageName)
		{
			var packageUri = Android.Net.Uri.Parse("package:" + packageName);
			var uninstallIntent = new Intent(Intent.ActionDelete, packageUri);
			callerActivity.StartActivityForResult(uninstallIntent, (int) RequestCodes.UninstallCode);
		}

		public static void OnUninstallResult(Activity callerActivity, CancellationToken token)
		{
			if (IsAppInstalled(ChaosChildPackageName))
			{
				StatusChanged?.Invoke(null,
					new StatusChangedEventArgs(Application.Context.Resources.GetText(Resource.String.aborted),
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.uninstall_error),
							MessageBox.Code.OK)));

				if (PatchTasks.Instance.IsRunning) PatchTasks.Instance.IsRunning = false;
				if (UnpatchTasks.Instance.IsRunning) UnpatchTasks.Instance.IsRunning = false;
			}
			else
			{
				// Install APK
				var apkMd5 = "";
				var path = "";

				if (PatchTasks.Instance.IsRunning)
				{
					if (Preferences.ContainsKey(Prefkey.signed_apk_md5.ToString()))
						apkMd5 = Preferences.Get(Prefkey.signed_apk_md5.ToString(), "");
					path = FilePaths[Files.SignedApk];
				}
				else if (UnpatchTasks.Instance.IsRunning)
				{
					if (Preferences.ContainsKey(Prefkey.backup_apk_md5.ToString()))
						apkMd5 = Preferences.Get(Prefkey.backup_apk_md5.ToString(), "");
					path = FilePaths[Files.BackupApk];
				}

				try
				{
					if (System.IO.File.Exists(path))
					{
						StatusChanged?.Invoke(null,
							new StatusChangedEventArgs(
								Application.Context.Resources.GetText(Resource.String.compare_apk),
								MessageBox.Data.Empty));

						var apkFileMd5 = CalculateMD5(path);

						if (apkMd5 == apkFileMd5)
						{
							InstallPackage(callerActivity, Android.Net.Uri.FromFile(new Java.IO.File(path)));
						}
						else
						{
							System.IO.File.Delete(path);

							if (PatchTasks.Instance.IsRunning)
								StatusChanged?.Invoke(null,
									new StatusChangedEventArgs(null,
										new MessageBox.Data(
											Application.Context.Resources.GetText(Resource.String.error),
											Application.Context.Resources.GetText(Resource.String
												.not_trustworthy_apk_patch),
											MessageBox.Code.OK)));

							else if (UnpatchTasks.Instance.IsRunning)
								StatusChanged?.Invoke(null,
									new StatusChangedEventArgs(null,
										new MessageBox.Data(
											Application.Context.Resources.GetText(Resource.String.error),
											Application.Context.Resources.GetText(Resource.String
												.not_trustworthy_apk_unpatch),
											MessageBox.Code.OK)));

							ErrorCanceled?.Invoke(null, EventArgs.Empty);
							token.ThrowIfCancellationRequested();
						}
					}
					else
					{
						if (PatchTasks.Instance.IsRunning)
							StatusChanged?.Invoke(null,
								new StatusChangedEventArgs(null,
									new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
										Application.Context.Resources.GetText(Resource.String.apk_not_found_patch),
										MessageBox.Code.OK)));

						else if (UnpatchTasks.Instance.IsRunning)
							StatusChanged?.Invoke(null,
								new StatusChangedEventArgs(null,
									new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
										Application.Context.Resources.GetText(Resource.String.apk_not_found_unpatch),
										MessageBox.Code.OK)));

						ErrorCanceled?.Invoke(null, EventArgs.Empty);
						token.ThrowIfCancellationRequested();
					}
				}
				catch (System.OperationCanceledException)
				{
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100));
					StatusChanged?.Invoke(null,
						new StatusChangedEventArgs(
							Application.Context.Resources.GetText(Resource.String.aborted),
							MessageBox.Data.Empty));

					if (PatchTasks.Instance.IsRunning) PatchTasks.Instance.IsRunning = false;
					if (UnpatchTasks.Instance.IsRunning) UnpatchTasks.Instance.IsRunning = false;
				}
			}
		}

		public static Task CopyFile(string inFilePath, string outFilePath, string outFileName, CancellationToken token)
		{
			var bufferLength = 0x14000;
			var buffer = new byte[bufferLength];
			int length;

			ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100));

			Directory.CreateDirectory(outFilePath);

			FileStream input = null;

			var inputFile = new Java.IO.File(inFilePath);

			InputStream inputBaseApkStream = new FileInputStream(inputFile);

			var output = new FileStream(Path.Combine(outFilePath, outFileName), FileMode.OpenOrCreate);
			var outFile = new Java.IO.File(Path.Combine(outFilePath, outFileName));

			if (!inFilePath.StartsWith("/data"))
			{
				input = new FileStream(inFilePath, FileMode.Open);
				var progressMax = (int) input.Length / bufferLength;

				var i = 0;
				while ((length = input.Read(buffer)) > 0)
				{
					output.Write(buffer, 0, length);
					i++;
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(i, progressMax));
					if (token.IsCancellationRequested) break;
				}
			}
			else
			{
				var fileSize = (int) inputFile.Length();
				var progressMax = fileSize / bufferLength;

				var i = 0;
				while ((length = inputBaseApkStream.Read(buffer)) > 0)
				{
					output.Write(buffer, 0, length);
					i++;
					ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(i, progressMax));
					if (token.IsCancellationRequested) break;
				}
			}

			input?.Dispose();
			inputFile.Dispose();
			inputBaseApkStream.Dispose();
			output.Dispose();
			if (token.IsCancellationRequested && outFile.Exists()) outFile.Delete();
			outFile.Dispose();

			return !token.IsCancellationRequested
				? Task.CompletedTask
				: Task.FromException(new System.OperationCanceledException(token));
		}

		public static async Task DownloadFile(string URL, string outFilePath, string outFileName,
			CancellationToken token)
		{
			Directory.CreateDirectory(outFilePath);
			var output = new FileStream(Path.Combine(outFilePath, outFileName), FileMode.OpenOrCreate);

			var bufferLength = 0x14000;
			var buffer = new byte[bufferLength];
			int length;

			var downloadedFile = new Java.IO.File(Path.Combine(outFilePath, outFileName));

			Stream download = null;

			try
			{
				var response =
					await Client.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead);
				var contentLength = -1;

				if (response.StatusCode == HttpStatusCode.OK)
				{
					contentLength = (int) response.Content.Headers.ContentLength;
					download = await response.Content.ReadAsStreamAsync();
				}
				else
				{
					StatusChanged?.Invoke(null,
						new StatusChangedEventArgs(null,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Java.Lang.String.Format(
									Application.Context.Resources.GetText(Resource.String.http_file_access_error),
									response.StatusCode.ToString()), MessageBox.Code.OK)));

					ErrorCanceled?.Invoke(null, EventArgs.Empty);
				}

				if (!token.IsCancellationRequested)
					while ((length = download.Read(buffer)) > 0)
					{
						output.Write(buffer, 0, length);
						ProgressChanged?.Invoke(null, new ProgressChangedEventArgs((int) output.Length, contentLength));
						if (token.IsCancellationRequested) break;
					}
			}
			catch (Exception ex) when (!(ex is System.OperationCanceledException))
			{
				StatusChanged?.Invoke(null,
					new StatusChangedEventArgs(null,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.http_file_download_error),
							MessageBox.Code.OK)));

				ErrorCanceled?.Invoke(null, EventArgs.Empty);
			}
			finally
			{
				//await Task.Delay(1);    // Xamarin debugger bug workaround
				download?.Dispose();
				output.Dispose();
				if (token.IsCancellationRequested && downloadedFile.Exists()) downloadedFile.Delete();
				downloadedFile.Dispose();
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
			StatusChanged?.Invoke(null,
				new StatusChangedEventArgs(null,
					new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.exception),
						Application.Context.Resources.GetText(Resource.String.exception_notice),
						MessageBox.Code.Exit)));
		}
	}
}