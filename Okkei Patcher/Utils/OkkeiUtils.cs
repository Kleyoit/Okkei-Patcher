using System.IO;
using Android.App;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Utils
{
	internal static class OkkeiUtils
	{
		public static bool IsBackupAvailable()
		{
			return File.Exists(FilePaths[Files.BackupApk]) && File.Exists(FilePaths[Files.BackupObb]);
		}

		public static void ClearOkkeiFiles()
		{
			if (Directory.Exists(OkkeiFilesPath)) FileUtils.RecursiveClearFiles(OkkeiFilesPath);
			if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
			if (File.Exists(ManifestBackupPath)) File.Delete(ManifestBackupPath);
		}

		public static string GetText(int id)
		{
			return Application.Context.Resources.GetText(id);
		}
	}
}