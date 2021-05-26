namespace OkkeiPatcher.Model.DTO
{
	public readonly struct InstallMessageData
	{
		public MessageData Data { get; }
		public string FilePath { get; }

		public InstallMessageData(MessageData data, string filePath)
		{
			Data = data;
			FilePath = filePath;
		}
	}
}