using System;
using Android.App;
using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Utils
{
	public static class MessageBox
	{
		public static void Show(Activity activity, MessageData data)
		{
			Show(activity, data.Title, data.Message, data.PositiveButtonText, data.NegativeButtonText,
				data.PositiveAction, data.NegativeAction);
		}

		public static void Show(Activity activity, int titleId, int messageId, int positiveButtonTextId,
			int negativeButtonTextId,
			Action positiveAction,
			Action negativeAction)
		{
			var title = OkkeiUtils.GetText(titleId);
			var message = OkkeiUtils.GetText(messageId);
			var positiveButtonText = OkkeiUtils.GetText(positiveButtonTextId);
			var negativeButtonText = OkkeiUtils.GetText(negativeButtonTextId);
			Show(activity, title, message, positiveButtonText, negativeButtonText, positiveAction, negativeAction);
		}

		public static void Show(Activity activity, int titleId, int messageId, int buttonTextId, Action action)
		{
			var title = OkkeiUtils.GetText(titleId);
			var message = OkkeiUtils.GetText(messageId);
			var buttonText = OkkeiUtils.GetText(buttonTextId);
			Show(activity, title, message, buttonText, null, action, null);
		}

		public static void Show(Activity activity, string title, string message, string buttonText, Action action)
		{
			Show(activity, title, message, buttonText, null, action, null);
		}

		public static void Show(Activity activity, string title, string message, string positiveButtonText,
			string negativeButtonText, Action positiveAction, Action negativeAction)
		{
			if (title == null && message == null && positiveButtonText == null && negativeButtonText == null &&
			    positiveAction == null && negativeAction == null) return;

			var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(activity);

			builder.SetTitle(title);
			builder.SetMessage(message);
			builder.SetCancelable(false);

			builder.SetPositiveButton(positiveButtonText, (sender, e) => positiveAction?.Invoke());
			if (negativeButtonText != null)
				builder.SetNegativeButton(negativeButtonText, (sender, e) => negativeAction?.Invoke());

			builder.Create()?.Show();
		}
	}
}