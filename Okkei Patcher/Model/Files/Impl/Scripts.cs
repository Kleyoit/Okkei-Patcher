using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Files.Base;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;

namespace OkkeiPatcher.Model.Files.Impl
{
	internal class Scripts : VerifiableFile
	{
		public Scripts()
		{
			Directory = OkkeiPaths.Root;
			FileName = "scripts.zip";
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var md5 = string.Empty;
			string md5ToCompare = Preferences.Get(FilePrefkey.scripts_md5.ToString(), string.Empty);
			if (md5ToCompare == string.Empty) return false;
			if (Exists)
				md5 = await Md5Utils.ComputeMd5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}