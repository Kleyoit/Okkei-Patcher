namespace OkkeiPatcher.Model.Files
{
	internal static class FilesProvider
	{
		static FilesProvider()
		{
			BackupApk = new BackupApk();
			BackupObb = new BackupObb();
			BackupSavedata = new BackupSavedata();
			ObbToBackup = new ObbToBackup();
			ObbToReplace = new ObbToReplace();
			OriginalSavedata = new OriginalSavedata();
			Scripts = new Scripts();
			SignedApk = new SignedApk();
			TempApk = new TempApk();
			TempSavedata = new TempSavedata();
		}

		public static BackupApk BackupApk { get; }
		public static BackupObb BackupObb { get; }
		public static BackupSavedata BackupSavedata { get; }
		public static ObbToBackup ObbToBackup { get; }
		public static ObbToReplace ObbToReplace { get; }
		public static OriginalSavedata OriginalSavedata { get; }
		public static Scripts Scripts { get; }
		public static SignedApk SignedApk { get; }
		public static TempApk TempApk { get; }
		public static TempSavedata TempSavedata { get; }
	}
}