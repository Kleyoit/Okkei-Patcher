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

		public static readonly string OkkeiFilesPath =
			Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, "OkkeiPatcher");

		public static readonly string OkkeiFilesBackupPath = Path.Combine(OkkeiFilesPath, "backup");
		public static readonly string PrivateStorage = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
	}
}