using System;
using Android.App;

namespace OkkeiPatcher
{
	public static class MessageBox
	{
		public static void Show(Activity activity, Data data)
		{
			Show(activity, data.Title, data.Message, data.PositiveButtonText, data.NegativeButtonText,
				data.PositiveAction, data.NegativeAction);
		}

		public static void Show(Activity activity, int titleId, int messageId, int positiveButtonTextId,
			int negativeButtonTextId,
			Action positiveAction,
			Action negativeAction)
		{
			var title = Utils.GetText(titleId);
			var message = Utils.GetText(messageId);
			var positiveButtonText = Utils.GetText(positiveButtonTextId);
			var negativeButtonText = Utils.GetText(negativeButtonTextId);
			Show(activity, title, message, positiveButtonText, negativeButtonText, positiveAction, negativeAction);
		}

		public static void Show(Activity activity, int titleId, int messageId, int buttonTextId, Action action)
		{
			var title = Utils.GetText(titleId);
			var message = Utils.GetText(messageId);
			var buttonText = Utils.GetText(buttonTextId);
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

		public readonly struct Data
		{
			public string Title { get; }
			public string Message { get; }
			public string PositiveButtonText { get; }
			public string NegativeButtonText { get; }
			public Action PositiveAction { get; }
			public Action NegativeAction { get; }

			public static readonly Data Empty = new Data(null, null, null, null, null, null);

			public Data(string title, string message, string positiveButtonText, string negativeButtonText,
				Action positiveAction,
				Action negativeAction)
			{
				Title = title;
				Message = message;
				PositiveButtonText = positiveButtonText;
				NegativeButtonText = negativeButtonText;
				PositiveAction = positiveAction;
				NegativeAction = negativeAction;
			}
		}
	}
}