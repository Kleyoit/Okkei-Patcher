using System;
using System.IO;

namespace OkkeiPatcher.Extensions
{
	public static class StreamExtensions
	{
		public static void Copy(this Stream input, Stream output, IProgress<float> progress)
		{
			var buf = new byte[0x14000];
			int length;
			var transferredBytes = 0;
			while ((length = input.Read(buf)) > 0)
			{
				transferredBytes += length;
				output.Write(buf, 0, length);
				progress.Report((float) transferredBytes / input.Length);
			}
		}
	}
}