﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;

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

		public const long TwoGb = (long) 1024 * 1024 * 1024 * 2;
		public static X509Certificate2 Testkey;

		public static readonly string ActionPackageInstalled =
			"solru.okkeipatcher.PACKAGE_INSTALLED_ACTION";

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

		public static readonly string TempApkFileName = "base.apk";
		public static readonly string BackupApkFileName = "backup.apk";
		public static readonly string SignedApkFileName = "signed.apk";
		public static readonly string ScriptsFileName = "scripts.zip";
		public static readonly string SavedataFileName = "SAVEDATA.DAT";
		public static readonly string SavedataBackupFileName = "SAVEDATA_BACKUP.DAT";
		public static readonly string ChaosChildPackageName = "com.mages.chaoschild_jp";
		public static readonly string ObbFileName = "main.87.com.mages.chaoschild_jp.obb";
		public static readonly string ManifestFileName = "Manifest.json";
		public static readonly string ManifestBackupFileName = "ManifestBackup.json";
		public static readonly string AppUpdateFileName = "OkkeiPatcher.apk";
		public static readonly string ManifestPath = Path.Combine(PrivateStorage, ManifestFileName);
		public static readonly string ManifestBackupPath = Path.Combine(PrivateStorage, ManifestBackupFileName);
		public static readonly string AppUpdatePath = Path.Combine(OkkeiFilesPath, AppUpdateFileName);

		public static readonly string ManifestUrl =
			"https://raw.githubusercontent.com/ForrrmerBlack/okkei-patcher/master/Manifest.json";

		public static readonly string BugReportLogPath = Path.Combine(OkkeiFilesPath, "bugreport.log");
		public static readonly string CertFileName = "testkey.p12";
		public static readonly string CertPassword = "password";
		public static OkkeiManifest GlobalManifest;

		public static readonly Dictionary<Files, string> FilePaths = new Dictionary<Files, string>
		{
			{Files.OriginalSavedata, Path.Combine(SavedataPath, SavedataFileName)},
			{Files.SAVEDATA_BACKUP, Path.Combine(OkkeiFilesPathBackup, SavedataBackupFileName)},
			{Files.BackupSavedata, Path.Combine(OkkeiFilesPathBackup, SavedataFileName)},
			{Files.TempApk, Path.Combine(OkkeiFilesPath, TempApkFileName)},
			{Files.BackupApk, Path.Combine(OkkeiFilesPathBackup, BackupApkFileName)},
			{Files.SignedApk, Path.Combine(OkkeiFilesPath, SignedApkFileName)},
			{Files.Scripts, Path.Combine(OkkeiFilesPath, ScriptsFileName)},
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
			{Files.Scripts, Prefkey.scripts_md5.ToString()},
			{Files.ObbToBackup, Prefkey.backup_obb_md5.ToString()},
			{Files.ObbToReplace, Prefkey.downloaded_obb_md5.ToString()},
			{Files.BackupObb, Prefkey.backup_obb_md5.ToString()}
		};
	}
}