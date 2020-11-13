using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Widget;
using System.IO;
using System.Threading;
using Xamarin.Essentials;
using Java.IO;
using System.Net.Http;
using System.Net;
using Android.OS;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal static class Utils
	{
		private static readonly HttpClient Client = new HttpClient();

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
		/// Compares given file with a predefined corresponding file or corresponding checksum written in preferences and returns true if checksums are equal, false otherwise. See predefined values in <see cref="FileToCompareWith"/>.
		/// </summary>
		public static bool CompareMD5(Files file)
		{
			bool result = false;

			string firstMd5 = "";
			string secondMd5 = "";

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
			byte[] data = new byte[size];
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
				var packageInstaller = Android.App.Application.Context.PackageManager.PackageInstaller;
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

		public static async void OnInstallResult(Activity callerActivity)
		{
			bool isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

			TextView info = callerActivity.FindViewById<TextView>(Resource.Id.Status);

			MainThread.BeginInvokeOnMainThread(() =>
			{
				info.Text = callerActivity.Resources.GetText(Resource.String.wait_installer);
			});

			await Task.Delay(6000);

			ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);

			if (!IsAppInstalled(ChaosChildPackageName))
			{
				Button patch = callerActivity.FindViewById<Button>(Resource.Id.Patch);
				Button unpatch = callerActivity.FindViewById<Button>(Resource.Id.Unpatch);

				MainThread.BeginInvokeOnMainThread(() =>
				{
					MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
						callerActivity.Resources.GetText(Resource.String.install_error), MessageBox.Code.OK);
				});
				MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });
				MainThread.BeginInvokeOnMainThread(() =>
				{
					info.Text = callerActivity.Resources.GetText(Resource.String.aborted);
				});

				if (isPatched)
					MainThread.BeginInvokeOnMainThread(() =>
					{
						unpatch.Text = callerActivity.Resources.GetText(Resource.String.unpatch);
					});
				else
					MainThread.BeginInvokeOnMainThread(() =>
					{
						patch.Text = callerActivity.Resources.GetText(Resource.String.patch);
					});

				TokenSource = new CancellationTokenSource();
				PatchTasks.IsAnyRunning = false;
				UnpatchTasks.IsAnyRunning = false;
			}
		}

		public static void UninstallPackage(Activity callerActivity, string packageName)
		{
			var packageUri = Android.Net.Uri.Parse("package:" + packageName);
			Intent uninstallIntent = new Intent(Intent.ActionDelete, packageUri);
			callerActivity.StartActivityForResult(uninstallIntent, (int) RequestCodes.UninstallCode);
		}

		public static void OnUninstallResult(Activity callerActivity)
		{
			if (IsAppInstalled(ChaosChildPackageName))
			{
				MainThread.BeginInvokeOnMainThread(() =>
				{
					MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
						callerActivity.Resources.GetText(Resource.String.uninstall_error), MessageBox.Code.OK);
				});

				TokenSource = new CancellationTokenSource();
			}
			else
			{
				// Install APK
				bool isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

				TokenSource = new CancellationTokenSource();
				CancellationToken token = TokenSource.Token;

				Button patch = callerActivity.FindViewById<Button>(Resource.Id.Patch);
				Button unpatch = callerActivity.FindViewById<Button>(Resource.Id.Unpatch);
				TextView info = callerActivity.FindViewById<TextView>(Resource.Id.Status);
				ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);

				string apkMd5 = "";
				string apkFileMd5 = "";
				string path = "";

				if (isPatched)
				{
					if (Preferences.ContainsKey(Prefkey.backup_apk_md5.ToString()))
						apkMd5 = Preferences.Get(Prefkey.backup_apk_md5.ToString(), "");
					path = FilePaths[Files.BackupApk];
				}
				else
				{
					if (Preferences.ContainsKey(Prefkey.signed_apk_md5.ToString()))
						apkMd5 = Preferences.Get(Prefkey.signed_apk_md5.ToString(), "");
					path = FilePaths[Files.SignedApk];
				}

				try
				{
					if (System.IO.File.Exists(path))
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							info.Text = callerActivity.Resources.GetText(Resource.String.compare_apk);
						});

						apkFileMd5 = CalculateMD5(path);

						if (apkMd5 == apkFileMd5)
							InstallPackage(callerActivity, Android.Net.Uri.FromFile(new Java.IO.File(path)));
						else
						{
							System.IO.File.Delete(path);

							if (isPatched)
								MainThread.BeginInvokeOnMainThread(() =>
								{
									MessageBox.Show(callerActivity,
										callerActivity.Resources.GetText(Resource.String.error),
										callerActivity.Resources.GetText(
											Resource.String.not_trustworthy_apk_unpatch), MessageBox.Code.OK);
								});
							else
								MainThread.BeginInvokeOnMainThread(() =>
								{
									MessageBox.Show(callerActivity,
										callerActivity.Resources.GetText(Resource.String.error),
										callerActivity.Resources.GetText(Resource.String.not_trustworthy_apk_patch),
										MessageBox.Code.OK);
								});

							TokenSource.Cancel();
							token.ThrowIfCancellationRequested();
						}
					}
					else
					{
						if (isPatched)
							MainThread.BeginInvokeOnMainThread(() =>
							{
								MessageBox.Show(callerActivity,
									callerActivity.Resources.GetText(Resource.String.error),
									callerActivity.Resources.GetText(Resource.String.apk_not_found_unpatch),
									MessageBox.Code.OK);
							});
						else
							MainThread.BeginInvokeOnMainThread(() =>
							{
								MessageBox.Show(callerActivity,
									callerActivity.Resources.GetText(Resource.String.error),
									callerActivity.Resources.GetText(Resource.String.apk_not_found_patch),
									MessageBox.Code.OK);
							});

						TokenSource.Cancel();
						token.ThrowIfCancellationRequested();
					}
				}
				catch (System.OperationCanceledException)
				{
					MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });
					MainThread.BeginInvokeOnMainThread(() =>
					{
						info.Text = callerActivity.Resources.GetText(Resource.String.aborted);
					});

					if (isPatched)
						MainThread.BeginInvokeOnMainThread(() =>
						{
							unpatch.Text = callerActivity.Resources.GetText(Resource.String.unpatch);
						});
					else
						MainThread.BeginInvokeOnMainThread(() =>
						{
							patch.Text = callerActivity.Resources.GetText(Resource.String.patch);
						});

					TokenSource = new CancellationTokenSource();
					PatchTasks.IsAnyRunning = false;
					UnpatchTasks.IsAnyRunning = false;
				}
			}
		}

		public static Task CopyFile(Activity callerActivity, string inFilePath, string outFilePath, string outFileName)
		{
			CancellationToken token = TokenSource.Token;

			ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);

			int bufferLength = 0x14000;
			byte[] buffer = new byte[bufferLength];
			int length;

			MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = 0; });

			Directory.CreateDirectory(outFilePath);

			FileStream input = null;

			var inputFile = new Java.IO.File(inFilePath);

			InputStream inputBaseApkStream = new FileInputStream(inputFile);

			var output = new FileStream(Path.Combine(outFilePath, outFileName), FileMode.OpenOrCreate);
			var outFile = new Java.IO.File(Path.Combine(outFilePath, outFileName));

			if (!inFilePath.StartsWith("/data"))
			{
				input = new FileStream(inFilePath, FileMode.Open);
				int inputLength = (int) input.Length;

				MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = inputLength / bufferLength; });

				while ((length = input.Read(buffer)) > 0)
				{
					output.Write(buffer, 0, length);
					MainThread.BeginInvokeOnMainThread(() => { progressBar.IncrementProgressBy(1); });
					if (token.IsCancellationRequested) break;
				}
			}
			else
			{
				int fileSize = (int) inputFile.Length();
				MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = fileSize / bufferLength; });

				while ((length = inputBaseApkStream.Read(buffer)) > 0)
				{
					output.Write(buffer, 0, length);
					MainThread.BeginInvokeOnMainThread(() => { progressBar.IncrementProgressBy(1); });
					if (token.IsCancellationRequested) break;
				}
			}

			input?.Dispose();
			inputFile.Dispose();
			inputBaseApkStream?.Dispose();
			output.Dispose();
			if (token.IsCancellationRequested && outFile.Exists()) outFile.Delete();
			outFile.Dispose();

			return !token.IsCancellationRequested
				? Task.CompletedTask
				: Task.FromException(new System.OperationCanceledException());
		}

		public static async Task DownloadFile(Activity callerActivity, string URL, string outFilePath,
			string outFileName)
		{
			CancellationToken token = TokenSource.Token;

			ProgressBar progressBar = callerActivity.FindViewById<ProgressBar>(Resource.Id.progressBar);

			Directory.CreateDirectory(outFilePath);
			var output = new FileStream(Path.Combine(outFilePath, outFileName), FileMode.OpenOrCreate);

			int bufferLength = 0x14000;
			byte[] buffer = new byte[bufferLength];
			int length;

			var downloadedFile = new Java.IO.File(Path.Combine(outFilePath, outFileName));

			Stream download = null;

			try
			{
				HttpResponseMessage response =
					await Client.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead);

				if (response.StatusCode == HttpStatusCode.OK)
				{
					int contentLength = (int) response.Content.Headers.ContentLength;
					MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = contentLength; });

					download = await response.Content.ReadAsStreamAsync();
				}
				else
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
							Java.Lang.String.Format(
								callerActivity.Resources.GetText(Resource.String.http_file_access_error),
								response.StatusCode.ToString()), MessageBox.Code.OK);
					});

					TokenSource.Cancel();
				}

				if (!token.IsCancellationRequested)
				{
					while ((length = download.Read(buffer)) > 0)
					{
						output.Write(buffer, 0, length);

						int outputLength;
						if (output is not null)
						{
							outputLength = (int) output.Length;
							MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = outputLength; });
						}

						if (token.IsCancellationRequested) break;
					}
				}
			}
			catch (Exception ex) when (!(ex is System.OperationCanceledException))
			{
				MainThread.BeginInvokeOnMainThread(() =>
				{
					MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.error),
						callerActivity.Resources.GetText(Resource.String.http_file_download_error),
						MessageBox.Code.OK);
				});

				TokenSource.Cancel();
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
				$"-------------------------\nVersion Code: {AppInfo.BuildString}\nVersion Name: {AppInfo.VersionString}\n-------------------------\nDevice Info\n-------------------------\n{Utils.GetDeviceInfo()}\n-------------------------\nException Stack Trace\n-------------------------\n{(ex != null ? ex.Message : "None")}\n\n{(ex != null ? ex.StackTrace : "None")}";
		}

		public static string GetDeviceInfo()
		{
			string manufacturer = Build.Manufacturer;
			string model = Build.Model;
			string product = Build.Product;
			string incremental = Build.VERSION.Incremental;
			string release = Build.VERSION.Release;
			BuildVersionCodes sdkInt = Build.VERSION.SdkInt;
			return
				$"manufacturer:       {manufacturer}\nmodel:              {model}\nproduct:            {product}\nincremental:        {incremental}\nrelease:            {release}\nsdkInt:             {sdkInt}";
		}

		public static void WriteBugReport(Activity callerActivity, Exception ex)
		{
			string bugReport = Utils.GetBugReportText(ex);
			System.IO.File.WriteAllText(BugReportLogPath, bugReport);
			MainThread.BeginInvokeOnMainThread(() =>
			{
				MessageBox.Show(callerActivity, callerActivity.Resources.GetText(Resource.String.exception),
					callerActivity.Resources.GetText(Resource.String.exception_notice), MessageBox.Code.Exit);
			});
		}
	}
}