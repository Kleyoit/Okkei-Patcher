using System;

namespace OkkeiPatcher
{
	internal class ProgressChangedEventArgs : EventArgs
	{
		public ProgressChangedEventArgs(int progress, int max)
		{
			Max = max;
			Progress = progress;
		}

		public int Max { get; }
		public int Progress { get; }
	}
}