using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using OkkeiPatcher.Core;
using Xamarin.Essentials;

namespace OkkeiPatcher.Utils
{
	internal static class PackageManagerUtils
	{
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

		public static void UninstallPackage(Activity activity, string packageName)
		{
			Uri packageUri = Uri.Parse("package:" + packageName);
			var uninstallIntent = new Intent(Intent.ActionDelete, packageUri);
			activity.StartActivityForResult(uninstallIntent, (int) RequestCode.UninstallCode);
		}

		public static string GetPackagePublicSourceDir(string packageName)
		{
			return Application.Context.PackageManager
				?.GetPackageInfo(packageName, 0)
				?.ApplicationInfo
				?.PublicSourceDir;
		}

		public static int GetVersionCode()
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
				return (int) Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0)
					.LongVersionCode;
#pragma warning disable CS0618 // Type or member is obsolete
			return Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0)
				.VersionCode;
#pragma warning restore CS0618 // Type or member is obsolete
		}
	}
}