namespace OkkeiPatcher.Model.DTO
{
	internal readonly struct UninstallMessageData
	{
		public MessageData Data { get; }
		public string PackageName { get; }

		public UninstallMessageData(MessageData data, string packageName)
		{
			Data = data;
			PackageName = packageName;
		}
	}
}