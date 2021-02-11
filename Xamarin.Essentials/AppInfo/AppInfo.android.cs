using System.Globalization;
using Android.Content.PM;
using Android.Content.Res;

namespace Xamarin.Essentials
{
    public static partial class AppInfo
    {
        static string PlatformGetPackageName() => Platform.AppContext.PackageName;

        static string PlatformGetName()
        {
            var applicationInfo = Platform.AppContext.ApplicationInfo;
            var packageManager = Platform.AppContext.PackageManager;
            return applicationInfo.LoadLabel(packageManager);
        }

        static string PlatformGetVersionString()
        {
            var pm = Platform.AppContext.PackageManager;
            var packageName = Platform.AppContext.PackageName;
            using (var info = pm.GetPackageInfo(packageName, PackageInfoFlags.MetaData))
            {
                return info.VersionName;
            }
        }

        static string PlatformGetBuild()
        {
            var pm = Platform.AppContext.PackageManager;
            var packageName = Platform.AppContext.PackageName;
            using (var info = pm.GetPackageInfo(packageName, PackageInfoFlags.MetaData))
            {
#if __ANDROID_28__
                return info.LongVersionCode.ToString(CultureInfo.InvariantCulture);
#else
#pragma warning disable CS0618 // Type or member is obsolete
                return info.VersionCode.ToString(CultureInfo.InvariantCulture);
#pragma warning restore CS0618 // Type or member is obsolete
#endif
            }
        }

        static AppTheme PlatformRequestedTheme()
        {
            return (Platform.AppContext.Resources.Configuration.UiMode & UiMode.NightMask) switch
            {
                UiMode.NightYes => AppTheme.Dark,
                UiMode.NightNo => AppTheme.Light,
                _ => AppTheme.Unspecified
            };
        }
    }
}
