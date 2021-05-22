namespace OkkeiPatcher.Model.DTO
{
	public readonly struct ProgressInfo
	{
		public ProgressInfo(int progress, int max, bool isIndeterminate)
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