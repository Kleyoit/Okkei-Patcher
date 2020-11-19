using System;

namespace Patcher
{
	public class ProgressChangedEventArgs : EventArgs
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