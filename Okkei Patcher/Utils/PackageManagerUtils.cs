using Android.App;
using Android.Content;
using Android.Content.PM;
using static OkkeiPatcher.Model.GlobalData;

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
			var packageUri = Android.Net.Uri.Parse("package:" + packageName);
			var uninstallIntent = new Intent(Intent.ActionDelete, packageUri);
			activity.StartActivityForResult(uninstallIntent, (int) RequestCodes.UninstallCode);
		}
	}
}