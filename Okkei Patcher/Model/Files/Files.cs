using OkkeiPatcher.Model.Files.Impl;

namespace OkkeiPatcher.Model.Files
{
	internal static class Files
	{
		public static readonly VerifiableFile BackupApk = new BackupApk();
		public static readonly VerifiableFile BackupObb = new BackupObb();
		public static readonly VerifiableFile BackupSavedata = new BackupSavedata();
		public static readonly VerifiableFile ObbToBackup = new ObbToBackup();
		public static readonly VerifiableFile ObbToReplace = new ObbToReplace();
		public static readonly VerifiableFile OriginalSavedata = new OriginalSavedata();
		public static readonly VerifiableFile Scripts = new Scripts();
		public static readonly VerifiableFile SignedApk = new SignedApk();
		public static readonly VerifiableFile TempApk = new TempApk();
		public static readonly VerifiableFile TempSavedata = new TempSavedata();
	}
}