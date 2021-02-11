using Android.App;
using Android.Content;
using Android.OS;

namespace Xamarin.Essentials
{
	public static partial class Platform
    {
        public static Context AppContext => Application.Context;

        static int? sdkInt;

        internal static int SdkInt
            => sdkInt ??= (int)Build.VERSION.SdkInt;

        internal static bool HasApiLevel(BuildVersionCodes versionCode) =>
            SdkInt >= (int)versionCode;
    }
}