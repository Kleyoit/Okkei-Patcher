using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Java.IO;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Exceptions;
using OkkeiPatcher.Model.Files;
using OkkeiPatcher.Utils.Extensions;
using File = System.IO.File;

namespace OkkeiPatcher.Utils
{
	internal static class IOUtils
	{
		private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(() => new HttpClient());

		public static Task CopyFileAsync(VerifiableFile inFile, VerifiableFile outFile,
			IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			return CopyFileAsync(inFile.FullPath, outFile.Directory, outFile.FileName, progress, token);
		}

		public static Task CopyFileAsync(string inFilePath, VerifiableFile outFile, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			return CopyFileAsync(inFilePath, outFile.Directory, outFile.FileName, progress, token);
		}

		public static Task CopyFileAsync(VerifiableFile inFile, string outFilePath, string outFileName,
			IProgress<ProgressInfo> progress, CancellationToken token)
		{
			return CopyFileAsync(inFile.FullPath, outFilePath, outFileName, progress, token);
		}

		public static Task CopyFileAsync(string inFilePath, string outFilePath, string outFileName,
			IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (inFilePath == null) throw new ArgumentNullException(nameof(inFilePath));
			if (outFilePath == null) throw new ArgumentNullException(nameof(outFilePath));
			if (outFileName == null) throw new ArgumentNullException(nameof(outFileName));

			const int bufferLength = 0x14000;
			var buffer = new byte[bufferLength];
			int length;

			progress.Reset();

			Directory.CreateDirectory(outFilePath);
			string outPath = Path.Combine(outFilePath, outFileName);
			if (File.Exists(outPath)) File.Delete(outPath);

			var output = new FileStream(outPath, FileMode.OpenOrCreate);

			int progressMax;
			var currentProgress = 0;

			if (inFilePath.StartsWith(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath))
			{
				var inputStream = new FileStream(inFilePath, FileMode.Open);
				progressMax = (int) inputStream.Length / bufferLength;

				while ((length = inputStream.Read(buffer)) > 0 && !token.IsCancellationRequested)
				{
					output.Write(buffer, 0, length);
					++currentProgress;
					progress.Report(currentProgress, progressMax);
				}

				inputStream.Dispose();
			}
			else
			{
				var inputFile = new Java.IO.File(inFilePath);
				InputStream javaInputStream = new FileInputStream(inputFile);
				progressMax = (int) inputFile.Length() / bufferLength;

				while ((length = javaInputStream.Read(buffer)) > 0 && !token.IsCancellationRequested)
				{
					output.Write(buffer, 0, length);
					++currentProgress;
					progress.Report(currentProgress, progressMax);
				}

				inputFile.Dispose();
				javaInputStream.Dispose();
			}

			output.Dispose();

			string outFile = Path.Combine(outFilePath, outFileName);
			if (token.IsCancellationRequested && File.Exists(outFile)) File.Delete(outFile);

			token.ThrowIfCancellationRequested();

			return Task.CompletedTask;
		}

		/// <exception cref="System.IO.IOException"></exception>
		/// <exception cref="HttpRequestException"></exception>
		/// <exception cref="HttpStatusCodeException"></exception>
		/// <exception cref="ArgumentNullException"></exception>
		public static async Task DownloadFileAsync(string url, VerifiableFile outFile, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			await DownloadFileAsync(url, outFile.Directory, outFile.FileName, progress, token);
		}

		/// <exception cref="System.IO.IOException"></exception>
		/// <exception cref="HttpRequestException"></exception>
		/// <exception cref="HttpStatusCodeException"></exception>
		/// <exception cref="ArgumentNullException"></exception>
		public static async Task DownloadFileAsync(string url, string outFilePath, string outFileName,
			IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			if (url == null) throw new ArgumentNullException(nameof(url));
			if (outFilePath == null) throw new ArgumentNullException(nameof(outFilePath));
			if (outFileName == null) throw new ArgumentNullException(nameof(outFileName));

			Directory.CreateDirectory(outFilePath);
			string outPath = Path.Combine(outFilePath, outFileName);
			if (File.Exists(outPath)) File.Delete(outPath);
			var output = new FileStream(outPath, FileMode.OpenOrCreate);

			const int bufferLength = 0x14000;
			var buffer = new byte[bufferLength];

			Stream download = null;

			try
			{
				int length;
				HttpResponseMessage response = await Client.Value
					.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
					.ConfigureAwait(false);

				if (response.StatusCode != HttpStatusCode.OK)
					throw new HttpStatusCodeException(response.StatusCode);

				int contentLength = (int?) response.Content.Headers.ContentLength ?? int.MaxValue;
				download = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

				while ((length = download.Read(buffer)) > 0 && !token.IsCancellationRequested)
				{
					output.Write(buffer, 0, length);
					progress.Report((int) output.Length, contentLength);
				}
			}
			finally
			{
				//await Task.Delay(1);    // Xamarin debugger bug workaround
				download?.Dispose();
				output.Dispose();
				string downloadedFile = Path.Combine(outFilePath, outFileName);
				if (token.IsCancellationRequested && File.Exists(downloadedFile))
					File.Delete(downloadedFile);
				token.ThrowIfCancellationRequested();
			}
		}
	}
}