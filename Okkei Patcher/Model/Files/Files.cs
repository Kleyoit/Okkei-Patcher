namespace OkkeiPatcher.Model.Files
{
	internal static class Files
	{
		public static VerifiableFile BackupApk { get; } = new BackupApk();
		public static VerifiableFile BackupObb { get; } = new BackupObb();
		public static VerifiableFile BackupSavedata { get; } = new BackupSavedata();
		public static VerifiableFile ObbToBackup { get; } = new ObbToBackup();
		public static VerifiableFile ObbToReplace { get; } = new ObbToReplace();
		public static VerifiableFile OriginalSavedata { get; } = new OriginalSavedata();
		public static VerifiableFile Scripts { get; } = new Scripts();
		public static VerifiableFile SignedApk { get; } = new SignedApk();
		public static VerifiableFile TempApk { get; } = new TempApk();
		public static VerifiableFile TempSavedata { get; } = new TempSavedata();
	}
}