using System.IO;

namespace OkkeiPatcher.Utils
{
	internal static class FileUtils
	{
		private const long TwoGb = (long) 1024 * 1024 * 1024 * 2;

		public static void RecursiveClearFiles(string path)
		{
			string[] files = Directory.GetFiles(path);
			if (files.Length > 0)
				foreach (string file in files)
					File.Delete(file);
			string[] directories = Directory.GetDirectories(path);
			if (directories.Length == 0) return;
			foreach (string dir in directories)
			{
				RecursiveClearFiles(dir);
				Directory.Delete(dir);
			}
		}

		public static void DeleteFolder(string folderPath)
		{
			RecursiveClearFiles(folderPath);
			Directory.Delete(folderPath);
		}

		public static void DeleteIfExists(string filePath)
		{
			if (File.Exists(filePath)) File.Delete(filePath);
		}

		public static bool IsEnoughSpace()
		{
			return Android.OS.Environment.ExternalStorageDirectory.UsableSpace >= TwoGb;
		}
	}
}