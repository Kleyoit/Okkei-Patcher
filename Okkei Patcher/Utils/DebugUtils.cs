using System;
using Android.OS;
using Xamarin.Essentials;

namespace OkkeiPatcher.Utils
{
	internal static class DebugUtils
	{
		public static string GetBugReportText(Exception ex)
		{
			return
				"-------------------------\n" +
				$"Version Code: {AppInfo.BuildString}\n" +
				$"Version Name: {AppInfo.VersionString}\n" +
				"-------------------------\nDevice Info\n-------------------------\n" +
				$"{GetDeviceInfo()}\n" +
				"-------------------------\nException Stack Trace\n-------------------------\n" +
				$"{(ex != null ? ex.GetType().FullName : "None")}\n" +
				$"{(ex != null ? ex.Message : "None")}\n\n" +
				$"{(ex != null ? ex.StackTrace : "None")}";
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
				$"manufacturer:       {manufacturer}\n" +
				$"model:              {model}\n" +
				$"product:            {product}\n" +
				$"incremental:        {incremental}\n" +
				$"release:            {release}\n" +
				$"sdkInt:             {sdkInt}";
		}
	}
}