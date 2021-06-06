using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.OkkeiFilesPaths;

namespace OkkeiPatcher.Model.Files
{
	internal class BackupSavedata : VerifiableFile
	{
		public BackupSavedata()
		{
			Directory = OkkeiFilesBackupPath;
			FileName = "SAVEDATA.DAT";
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var md5 = string.Empty;
			var md5ToCompare = Preferences.Get(Prefkey.savedata_md5.ToString(), string.Empty);
			if (md5ToCompare == string.Empty) return false;
			if (Exists)
				md5 = await MD5Utils.ComputeMD5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}