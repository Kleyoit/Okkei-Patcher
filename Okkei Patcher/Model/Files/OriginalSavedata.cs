using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;

namespace OkkeiPatcher.Model.Files
{
	internal class OriginalSavedata : VerifiableFile
	{
		public OriginalSavedata()
		{
			Directory = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath,
				"Android/data/com.mages.chaoschild_jp/files");
			FileName = "SAVEDATA.DAT";
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var backupSavedata = new BackupSavedata();
			var md5 = string.Empty;
			var md5ToCompare = string.Empty;
			if (backupSavedata.Exists)
				md5ToCompare = await Md5Utils.ComputeMd5Async(backupSavedata.FullPath, progress, token)
					.ConfigureAwait(false);
			if (md5ToCompare == string.Empty) return false;
			if (Exists)
				md5 = await Md5Utils.ComputeMd5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}