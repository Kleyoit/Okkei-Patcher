namespace OkkeiPatcher.Model.DTO
{
	public readonly struct MessageData
	{
		public string Title { get; }
		public string Message { get; }
		public string PositiveButtonText { get; }
		public string NegativeButtonText { get; }

		public static readonly MessageData Empty = new MessageData(null, null, null, null);

		public MessageData(string title, string message, string positiveButtonText, string negativeButtonText)
		{
			Title = title;
			Message = message;
			PositiveButtonText = positiveButtonText;
			NegativeButtonText = negativeButtonText;
		}
	}
}