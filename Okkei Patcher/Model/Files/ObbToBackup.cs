using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Model.Files
{
	internal class ObbToBackup : VerifiableFile
	{
		public ObbToBackup()
		{
			Directory = ObbPath;
			FileName = ObbFileName;
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var md5 = string.Empty;
			var md5ToCompare = Preferences.Get(Prefkey.backup_obb_md5.ToString(), string.Empty);
			if (md5ToCompare == string.Empty) return false;
			if (Exists)
				md5 = await MD5Utils.ComputeMD5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}