﻿using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Files.Base;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;

namespace OkkeiPatcher.Model.Files.Impl
{
	internal class BackupObb : VerifiableFile
	{
		public BackupObb()
		{
			Directory = OkkeiPaths.Backup;
			FileName = "main.87.com.mages.chaoschild_jp.obb";
		}

		public override async Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			var md5 = string.Empty;
			string md5ToCompare = Preferences.Get(FilePrefkey.backup_obb_md5.ToString(), string.Empty);
			if (md5ToCompare == string.Empty) return false;
			if (Exists)
				md5 = await Md5Utils.ComputeMd5Async(FullPath, progress, token).ConfigureAwait(false);
			return md5 == md5ToCompare;
		}
	}
}