namespace OkkeiPatcher.Model.DTO
{
	internal readonly struct MessageData
	{
		public int TitleId { get; }
		public int MessageId { get; }
		public int PositiveButtonTextId { get; }
		public int NegativeButtonTextId { get; }
		public string Error { get; }
		
		public MessageData(int titleId, int messageId, int positiveButtonTextId, int negativeButtonTextId = 0, string error = null)
		{
			TitleId = titleId;
			MessageId = messageId;
			PositiveButtonTextId = positiveButtonTextId;
			NegativeButtonTextId = negativeButtonTextId;
			Error = error;
		}
	}
}