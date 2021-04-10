﻿namespace OkkeiPatcher
{
	public readonly struct ProgressChangedEventArgs
	{
		public ProgressChangedEventArgs(int progress, int max, bool isIndeterminate)
		{
			Max = max;
			Progress = progress;
			IsIndeterminate = isIndeterminate;
		}

		public int Max { get; }
		public int Progress { get; }
		public bool IsIndeterminate { get; }
	}
}