using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Extensions;
using OkkeiPatcher.Model.DTO;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Utils
{
	internal static class MD5Utils
	{
		public static Task<string> CalculateMD5(string filename, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			using var md5 = MD5.Create();
			using var stream = File.OpenRead(filename);

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

		/// <summary>
		///     Compares given file with a predefined corresponding file or corresponding checksum written in preferences and
		///     returns true if checksums are equal, false otherwise. See predefined values in
		///     <see cref="GlobalData.FileToCompareWith" />.
		/// </summary>
		public static async Task<bool> CompareMD5(Files file, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			var result = false;

			var firstMd5 = string.Empty;
			var secondMd5 = string.Empty;

			switch (file)
			{
				case Files.OriginalSavedata:
					if (File.Exists(FileToCompareWith[file]))
						secondMd5 = await CalculateMD5(FileToCompareWith[file], progress, token)
							.ConfigureAwait(false);
					break;
				default:
					secondMd5 = Preferences.Get(FileToCompareWith[file], string.Empty);
					break;
			}

			if (File.Exists(FilePaths[file]) && secondMd5 != string.Empty)
				firstMd5 = await CalculateMD5(FilePaths[file], progress, token).ConfigureAwait(false);

			if (firstMd5 == secondMd5 && firstMd5 != string.Empty && secondMd5 != string.Empty) result = true;

			return result;
		}
	}
}