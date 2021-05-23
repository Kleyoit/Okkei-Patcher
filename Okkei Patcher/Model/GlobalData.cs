using System;
using System.IO;

namespace OkkeiPatcher.Model
{
	internal static class GlobalData
	{
		public enum Prefkey
		{
			savedata_md5,
			scripts_md5,
			downloaded_obb_md5,
			signed_apk_md5,
			backup_obb_md5,
			backup_apk_md5,
			backup_restore_savedata,
			apk_is_patched,
			extstorage_permission_denied,
			scripts_version,
			obb_version
		}

		public enum RequestCodes
		{
			UnknownAppSourceSettingsCode,
			StoragePermissionSettingsCode,
			StoragePermissionRequestCode,
			UninstallCode,
			PendingIntentInstallCode,
			KitKatInstallCode
		}

		public const string ManifestUrl =
			"https://raw.githubusercontent.com/ForrrmerBlack/okkei-patcher/master/Manifest.json";

		public const long TwoGb = (long) 1024 * 1024 * 1024 * 2;
		public const string ActionPackageInstalled = "solru.okkeipatcher.PACKAGE_INSTALLED_ACTION";
		public const string TempApkFileName = "base.apk";
		public const string BackupApkFileName = "backup.apk";
		public const string SignedApkFileName = "signed.apk";
		public const string ScriptsFileName = "scripts.zip";
		public const string SavedataFileName = "SAVEDATA.DAT";
		public const string TempSavedataFileName = "SAVEDATA_BACKUP.DAT";
		public const string ChaosChildPackageName = "com.mages.chaoschild_jp";
		public const string ObbFileName = "main.87.com.mages.chaoschild_jp.obb";
		public const string ManifestFileName = "Manifest.json";
		public const string ManifestBackupFileName = "ManifestBackup.json";
		public const string AppUpdateFileName = "OkkeiPatcher.apk";
		public const string CertFileName = "testkey.p12";
		public const string CertPassword = "password";

		public static readonly string OkkeiFilesPath =
			Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, "OkkeiPatcher");

		public static readonly string OkkeiFilesPathBackup = Path.Combine(OkkeiFilesPath, "backup");

		public static readonly string ObbPath = Path.Combine(
			Android.OS.Environment.ExternalStorageDirectory.AbsolutePath,
			"Android/obb/com.mages.chaoschild_jp");

		public static readonly string SavedataPath = Path.Combine(
			Android.OS.Environment.ExternalStorageDirectory.AbsolutePath,
			"Android/data/com.mages.chaoschild_jp/files");

		public static readonly string PrivateStorage = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		public static readonly string ManifestPath = Path.Combine(PrivateStorage, ManifestFileName);
		public static readonly string ManifestBackupPath = Path.Combine(PrivateStorage, ManifestBackupFileName);
		public static readonly string AppUpdatePath = Path.Combine(OkkeiFilesPath, AppUpdateFileName);
		public static readonly string BugReportLogPath = Path.Combine(OkkeiFilesPath, "bugreport.log");
	}
}