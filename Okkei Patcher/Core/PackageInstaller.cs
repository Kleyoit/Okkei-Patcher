using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils.Extensions;
using Xamarin.Essentials;
using Uri = Android.Net.Uri;

namespace OkkeiPatcher.Core
{
	internal class PackageInstaller
	{
		public const string ActionPackageInstalled = "solru.okkeipatcher.PACKAGE_INSTALLED_ACTION";

		public PackageInstaller(IProgress<ProgressInfo> progress)
		{
			Progress = progress;
		}

		private IProgress<ProgressInfo> Progress { get; }
		public event EventHandler InstallFailed;

		private static void AddApkToInstallSession(Uri apkUri, Android.Content.PM.PackageInstaller.Session session)
		{
			Stream packageInSession = session.OpenWrite("package", 0, -1);
			FileStream input = null;
			if (apkUri.Path != null) input = new FileStream(apkUri.Path, FileMode.Open);

			try
			{
				var progress = new Progress<float>(session.SetStagingProgress);
				if (input != null) input.Copy(packageInSession, progress);
				else throw new NullReferenceException("APK InputStream was null");
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

		public void InstallPackage(Activity activity, Uri apkUri)
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
			{
				Android.Content.PM.PackageInstaller packageInstaller =
					Application.Context.PackageManager?.PackageInstaller;
				if (packageInstaller == null) throw new NullReferenceException("PackageInstaller was null");
				var sessionParams =
					new Android.Content.PM.PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
				int sessionId = packageInstaller.CreateSession(sessionParams);
				Android.Content.PM.PackageInstaller.Session session = packageInstaller.OpenSession(sessionId);

				var observer = new PackageInstallObserver(packageInstaller);
				observer.ProgressChanged += OnProgressChanged;
				observer.InstallFailed += OnInstallFailed;
				packageInstaller.RegisterSessionCallback(observer);

				AddApkToInstallSession(apkUri, session);

				var intent = new Intent(activity, activity.Class);
				intent.SetAction(ActionPackageInstalled);

				PendingIntent pendingIntent = PendingIntent.GetActivity(activity,
					(int) RequestCode.PendingIntentInstallCode,
					intent, PendingIntentFlags.UpdateCurrent);

				IntentSender statusReceiver = pendingIntent?.IntentSender;

				if (statusReceiver == null)
					throw new NullReferenceException("PackageInstaller status receiver was null");
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
				activity.StartActivityForResult(intent, (int) RequestCode.KitKatInstallCode);
			}
		}

		private void OnProgressChanged(object sender, float e)
		{
			Progress.Report((int) (e * 100), 100);
		}

		private void OnInstallFailed(object sender, EventArgs e)
		{
			Progress.Reset();
			if (!(sender is PackageInstallObserver observer)) return;
			observer.InstallFailed -= OnInstallFailed;
			InstallFailed?.Invoke(this, e);
			InstallFailed = null;
		}
	}
}