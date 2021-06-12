using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;

namespace OkkeiPatcher.Model.Files.Base
{
	internal abstract class VerifiableFile
	{
		public string Directory { get; protected set; }
		public string FileName { get; protected set; }
		public string FullPath => Path.Combine(Directory, FileName);
		public bool Exists => File.Exists(FullPath);

		public abstract Task<bool> VerifyAsync(IProgress<ProgressInfo> progress, CancellationToken token);

		public void DeleteIfExists()
		{
			if (Exists) File.Delete(FullPath);
		}

		public void MoveTo(VerifiableFile destinationFile)
		{
			File.Move(FullPath, destinationFile.FullPath);
		}

		public Task CopyToAsync(VerifiableFile destinationFile, IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			return IOUtils.CopyFileAsync(this, destinationFile, progress, token);
		}

		public override string ToString()
		{
			return FullPath;
		}
	}
}