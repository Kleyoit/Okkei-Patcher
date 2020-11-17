using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Android.App;
using Android.OS;

namespace OkkeiPatcher
{
	internal static class GlobalData
	{
		public enum Files
		{
			OriginalSavedata,
			SAVEDATA_BACKUP,
			BackupSavedata,
			TempApk,
			BackupApk,
			SignedApk,
			Scripts,
			ObbToBackup,
			ObbToReplace,
			BackupObb
		}

		public enum Prefkey
		{
			savedata_md5,
			scripts_md5,
			downloaded_obb_md5,
			signed_apk_md5,
			backup_obb_md5,
			backup_apk_md5,
			backup_restore_savedata,
			apk_is_patched
		}

		public enum RequestCodes
		{
			UnknownAppSourceCode,
			UninstallCode,
			InstallCode
		}

		public const long TwoGb = (long) 1024 * 1024 * 1024 * 2;
		public static readonly X509Certificate2 Testkey;

		public static readonly string PACKAGE_INSTALLED_ACTION =
			"com.example.android.apis.content.SESSION_API_PACKAGE_INSTALLED";

		public static readonly string OkkeiFilesPath =
			Path.Combine(Environment.ExternalStorageDirectory.Path, "OkkeiPatcher");

		public static readonly string OkkeiFilesPathBackup = Path.Combine(OkkeiFilesPath, "backup");

		public static readonly string ObbPath = Path.Combine(Environment.ExternalStorageDirectory.Path,
			"Android/obb/com.mages.chaoschild_jp");

		public static readonly string SavedataPath = Path.Combine(Environment.ExternalStorageDirectory.Path,
			"Android/data/com.mages.chaoschild_jp/files");

		public static readonly string SavedataFileName = "SAVEDATA.DAT";
		public static readonly string SavedataBackupFileName = "SAVEDATA_BACKUP.DAT";
		public static readonly string ChaosChildPackageName = "com.mages.chaoschild_jp";
		public static readonly string ObbFileName = "main.87.com.mages.chaoschild_jp.obb";

		public static readonly string ScriptsUrl =
			"https://github.com/ForrrmerBlack/okkei-patcher/releases/download/1.1.0/scripts.zip";

		public static readonly string ObbUrl =
			"https://github.com/ForrrmerBlack/okkei-patcher/releases/download/1.1.0/main.87.com.mages.chaoschild_jp.obb";

		public static readonly string BugReportLogPath = Path.Combine(OkkeiFilesPath, "bugreport.log");
		public static readonly string CertFileName = "testkey.p12";
		public static readonly string CertPassword = "password";

		public static readonly Dictionary<Files, string> FilePaths = new Dictionary<Files, string>
		{
			{Files.OriginalSavedata, Path.Combine(SavedataPath, SavedataFileName)},
			{Files.SAVEDATA_BACKUP, Path.Combine(OkkeiFilesPathBackup, SavedataBackupFileName)},
			{Files.BackupSavedata, Path.Combine(OkkeiFilesPathBackup, SavedataFileName)},
			{Files.TempApk, Path.Combine(OkkeiFilesPath, "base.apk")},
			{Files.BackupApk, Path.Combine(OkkeiFilesPathBackup, "backup.apk")},
			{Files.SignedApk, Path.Combine(OkkeiFilesPath, "signed.apk")},
			{Files.Scripts, Path.Combine(OkkeiFilesPath, "scripts.zip")},
			{Files.ObbToBackup, Path.Combine(ObbPath, ObbFileName)},
			{Files.ObbToReplace, Path.Combine(ObbPath, ObbFileName)},
			{Files.BackupObb, Path.Combine(OkkeiFilesPathBackup, ObbFileName)}
		};

		public static readonly Dictionary<Files, string> FileToCompareWith = new Dictionary<Files, string>
		{
			{Files.OriginalSavedata, Path.Combine(OkkeiFilesPathBackup, SavedataFileName)},
			{Files.SAVEDATA_BACKUP, Prefkey.savedata_md5.ToString()},
			{Files.BackupSavedata, Prefkey.savedata_md5.ToString()},
			{Files.TempApk, Prefkey.backup_apk_md5.ToString()},
			{Files.BackupApk, Prefkey.backup_apk_md5.ToString()},
			{Files.SignedApk, Prefkey.signed_apk_md5.ToString()},
			{Files.Scripts, Prefkey.signed_apk_md5.ToString()},
			{Files.ObbToBackup, Prefkey.backup_obb_md5.ToString()},
			{Files.ObbToReplace, Prefkey.downloaded_obb_md5.ToString()},
			{Files.BackupObb, Prefkey.backup_obb_md5.ToString()}
		};

		static GlobalData()
		{
			// Read testcert for signing APK
			var assets = Application.Context.Assets;
			var testkeyFile = assets.Open(CertFileName);
			var testkeySize = 2797;

			var testkeyTemp = new X509Certificate2(Utils.ReadCert(testkeyFile, testkeySize), CertPassword);
			Testkey = testkeyTemp;

			testkeyFile?.Close();
			testkeyFile?.Dispose();
		}
	}
}