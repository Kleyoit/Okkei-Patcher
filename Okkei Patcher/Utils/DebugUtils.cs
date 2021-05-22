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
			var manufacturer = Build.Manufacturer;
			var model = Build.Model;
			var product = Build.Product;
			var incremental = Build.VERSION.Incremental;
			var release = Build.VERSION.Release;
			var sdkInt = Build.VERSION.SdkInt;
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