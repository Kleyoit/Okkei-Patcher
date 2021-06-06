using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;

namespace OkkeiPatcher.Model.Files
{
	internal class ObbToReplace : VerifiableFile
	{
		public ObbToReplace()
		{
			Directory = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath,
				"Android/obb/com.mages.chaoschild_jp");
			FileName = "main.87.com.mages.chaoschild_jp.obb";
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var md5 = string.Empty;
			var md5ToCompare = Preferences.Get(Prefkey.downloaded_obb_md5.ToString(), string.Empty);
			if (md5ToCompare == string.Empty) return false;
			if (Exists)
				md5 = await MD5Utils.ComputeMD5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}