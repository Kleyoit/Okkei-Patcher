using System;

namespace OkkeiPatcher
{
	class ProgressChangedEventArgs : EventArgs
	{
		public int Max { get; }
		public int Progress { get; }

		public ProgressChangedEventArgs(int progress, int max)
		{
			Max = max;
			Progress = progress;
		}
	}
}