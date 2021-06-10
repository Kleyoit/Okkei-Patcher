using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Utils
{
	internal static class MessageDataUtils
	{
		public static InstallMessageData CreateInstallMessageData(int titleId, int messageId, int buttonTextId,
			string filePath)
		{
			var data = new MessageData(titleId, messageId, buttonTextId);
			return new InstallMessageData(data, filePath);
		}

		public static UninstallMessageData CreateUninstallMessageData(int titleId, int messageId, int buttonTextId,
			string packageName)
		{
			var data = new MessageData(titleId, messageId, buttonTextId);
			return new UninstallMessageData(data, packageName);
		}
	}
}