using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.OkkeiFilesPaths;

namespace OkkeiPatcher.Model.Files
{
	internal class TempApk : VerifiableFile
	{
		public TempApk()
		{
			Directory = OkkeiFilesPath;
			FileName = "base.apk";
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var md5 = string.Empty;
			var md5ToCompare = Preferences.Get(Prefkey.backup_apk_md5.ToString(), string.Empty);
			if (md5ToCompare == string.Empty) return false;
			if (Exists)
				md5 = await Md5Utils.ComputeMd5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}