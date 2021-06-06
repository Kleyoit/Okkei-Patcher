using System;
using System.IO;

namespace OkkeiPatcher.Model
{
	internal static class OkkeiFilesPaths
	{
		public static readonly string OkkeiFilesPath =
			Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, "OkkeiPatcher");

		public static readonly string OkkeiFilesBackupPath = Path.Combine(OkkeiFilesPath, "backup");
		public static readonly string PrivateStorage = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
	}
}