using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Utils
{
	public static class MessageDataUtils
	{
		public static InstallMessageData CreateInstallMessageData(int titleId, int messageId, int positiveButtonTextId,
			string filePath)
		{
			var data = CreateMessageData(titleId, messageId, positiveButtonTextId);
			return new InstallMessageData(data, filePath);
		}

		public static UninstallMessageData CreateUninstallMessageData(int titleId, int messageId,
			int positiveButtonTextId,
			string packageName)
		{
			var data = CreateMessageData(titleId, messageId, positiveButtonTextId);
			return new UninstallMessageData(data, packageName);
		}

		public static MessageData CreateMessageData(int titleId, int messageId, int positiveButtonTextId,
			int negativeButtonTextId)
		{
			var title = OkkeiUtils.GetText(titleId);
			var message = OkkeiUtils.GetText(messageId);
			var positiveButtonText = OkkeiUtils.GetText(positiveButtonTextId);
			var negativeButtonText = OkkeiUtils.GetText(negativeButtonTextId);
			return new MessageData(title, message, positiveButtonText, negativeButtonText);
		}

		public static MessageData CreateMessageData(int titleId, int messageId, int positiveButtonTextId)
		{
			var title = OkkeiUtils.GetText(titleId);
			var message = OkkeiUtils.GetText(messageId);
			var positiveButtonText = OkkeiUtils.GetText(positiveButtonTextId);
			return new MessageData(title, message, positiveButtonText, null);
		}
	}
}