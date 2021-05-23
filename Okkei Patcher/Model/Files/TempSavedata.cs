using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Model.Files
{
	internal class TempSavedata : VerifiableFile
	{
		public TempSavedata()
		{
			Directory = OkkeiFilesPathBackup;
			FileName = TempSavedataFileName;
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var md5 = string.Empty;
			var md5ToCompare = Preferences.Get(Prefkey.savedata_md5.ToString(), string.Empty);
			if (md5ToCompare == string.Empty) return false;
			if (File.Exists(FullPath))
				md5 = await MD5Utils.ComputeMD5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}