using System.IO;
using Android.App;
using OkkeiPatcher.Model.Files;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Utils
{
	internal static class OkkeiUtils
	{
		public static bool IsBackupAvailable()
		{
			return Files.BackupApk.Exists && Files.BackupObb.Exists;
		}

		public static void ClearOkkeiFiles()
		{
			if (Directory.Exists(OkkeiFilesPath)) FileUtils.RecursiveClearFiles(OkkeiFilesPath);
			FileUtils.DeleteIfExists(ManifestPath);
			FileUtils.DeleteIfExists(ManifestBackupPath);
		}

		public static string GetText(int id)
		{
			return Application.Context.Resources.GetText(id);
		}
	}
}