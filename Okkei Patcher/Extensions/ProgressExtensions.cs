using System;
using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Extensions
{
	public static class ProgressExtensions
	{
		public static void Report(this IProgress<ProgressInfo> progress, int currentProgress, int max)
		{
			progress.Report(new ProgressInfo(currentProgress, max, false));
		}

		public static void Reset(this IProgress<ProgressInfo> progress)
		{
			progress.Report(new ProgressInfo(0, 100, false));
		}

		public static void MakeIndeterminate(this IProgress<ProgressInfo> progress)
		{
			progress.Report(new ProgressInfo(0, 100, true));
		}
	}
}