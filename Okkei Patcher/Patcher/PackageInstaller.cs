using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using OkkeiPatcher.Extensions;
using OkkeiPatcher.Model;
using OkkeiPatcher.Model.DTO;
using Xamarin.Essentials;

namespace OkkeiPatcher.Patcher
{
	internal class PackageInstaller
	{
		public PackageInstaller(IProgress<ProgressInfo> progress)
		{
			Progress = progress;
		}

		private IProgress<ProgressInfo> Progress { get; }
		public event EventHandler InstallFailed;

		private static void AddApkToInstallSession(Android.Net.Uri apkUri,
			Android.Content.PM.PackageInstaller.Session session)
		{
			var packageInSession = session.OpenWrite("package", 0, -1);
			FileStream input = null;
			if (apkUri.Path != null) input = new FileStream(apkUri.Path, FileMode.Open);

			try
			{
				var progress = new Progress<float>();
				progress.ProgressChanged += (sender, f) => session.SetStagingProgress(f);
				if (input != null) input.Copy(packageInSession, progress);
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
				var sessionParams =
					new Android.Content.PM.PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
				var sessionId = packageInstaller.CreateSession(sessionParams);
				var session = packageInstaller.OpenSession(sessionId);

				AddApkToInstallSession(apkUri, session);

				var intent = new Intent(activity, activity.Class);
				intent.SetAction(GlobalData.ActionPackageInstalled);

				var pendingIntent = PendingIntent.GetActivity(activity,
					(int) GlobalData.RequestCodes.PendingIntentInstallCode,
					intent, PendingIntentFlags.UpdateCurrent);

				var observer = new PackageInstallObserver(packageInstaller);
				observer.ProgressChanged += OnProgressChanged;
				observer.InstallFailed += OnInstallFailed;
				packageInstaller.RegisterSessionCallback(observer);

				var statusReceiver = pendingIntent?.IntentSender;

				session.Commit(statusReceiver);
			}
			else
			{
#pragma warning disable CS0618 // Type or member is obsolete
				var intent = new Intent(Intent.ActionInstallPackage);
#pragma warning restore CS0618 // Type or member is obsolete
				intent.SetData(apkUri);
				intent.SetFlags(ActivityFlags.GrantReadUriPermission);
				intent.PutExtra(Intent.ExtraNotUnknownSource, false);
				intent.PutExtra(Intent.ExtraReturnResult, true);
				intent.PutExtra(Intent.ExtraInstallerPackageName, AppInfo.PackageName);
				activity.StartActivityForResult(intent, (int) GlobalData.RequestCodes.KitKatInstallCode);
			}
		}

		private void OnProgressChanged(object sender, float e)
		{
			Progress.Report((int) (e * 100), 100);
		}

		private void OnInstallFailed(object sender, EventArgs e)
		{
			Progress.Reset();
			((PackageInstallObserver) sender).InstallFailed -= OnInstallFailed;
			InstallFailed?.Invoke(this, e);
		}
	}
}