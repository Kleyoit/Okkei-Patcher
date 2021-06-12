using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Files.Base;
using OkkeiPatcher.Utils.Extensions;

namespace OkkeiPatcher.Utils
{
	internal static class Md5Utils
	{
		public static Task<string> ComputeMd5Async(VerifiableFile file, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			return ComputeMd5Async(file.FullPath, progress, token);
		}

		public static Task<string> ComputeMd5Async(string filename, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			using var md5 = MD5.Create();
			using FileStream stream = File.OpenRead(filename);

			const int bufferLength = 0x100000;
			var buffer = new byte[bufferLength];
			int length;

			progress.Reset();

			var progressMax = (int) Math.Ceiling((double) stream.Length / bufferLength);
			var currentProgress = 0;

			while ((length = stream.Read(buffer)) > 0 && !token.IsCancellationRequested)
			{
				++currentProgress;
				if (currentProgress != progressMax)
					md5.TransformBlock(buffer, 0, bufferLength, buffer, 0);
				else
					md5.TransformFinalBlock(buffer, 0, length);
				progress.Report(currentProgress, progressMax);
			}

			token.ThrowIfCancellationRequested();

			return Task.FromResult(BitConverter.ToString(md5.Hash).Replace("-", string.Empty).ToLowerInvariant());
		}
	}
}