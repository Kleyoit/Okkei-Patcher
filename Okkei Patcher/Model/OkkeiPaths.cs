using System;
using System.IO;

namespace OkkeiPatcher.Model
{
	internal static class OkkeiPaths
	{
		public static readonly string Root =
			Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, "OkkeiPatcher");

		public static readonly string Backup = Path.Combine(Root, "backup");
		public static readonly string Private = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
	}
}