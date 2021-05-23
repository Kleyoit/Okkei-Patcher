using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Model.Files
{
	internal class OriginalSavedata : VerifiableFile
	{
		public OriginalSavedata()
		{
			Directory = SavedataPath;
			FileName = SavedataFileName;
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var backupSavedata = new BackupSavedata();
			var md5 = string.Empty;
			var md5ToCompare = string.Empty;
			if (File.Exists(backupSavedata.FullPath))
				md5ToCompare = await MD5Utils.ComputeMD5Async(backupSavedata.FullPath, progress, token)
					.ConfigureAwait(false);
			if (md5ToCompare == string.Empty) return false;
			if (File.Exists(FullPath))
				md5 = await MD5Utils.ComputeMD5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}