using System;
using Android.App;

namespace OkkeiPatcher
{
	public static class MessageBox
	{
		public static void Show(Activity activity, string title, string message, string buttonText, Action action)
		{
			Show(activity, new Data(title, message, buttonText, null, action, null));
		}

		public static void Show(Activity activity, string title, string message, string positiveButtonText,
			string negativeButtonText, Action positiveAction, Action negativeAction)
		{
			Show(activity,
				new Data(title, message, positiveButtonText, negativeButtonText, positiveAction, negativeAction));
		}

		public static void Show(Activity activity, Data data)
		{
			if (data.Equals(Data.Empty)) return;

			var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(activity);

			builder.SetTitle(data.Title);
			builder.SetMessage(data.Message);
			builder.SetCancelable(false);

			builder.SetPositiveButton(data.PositiveButtonText, (sender, e) => { data.PositiveAction?.Invoke(); });
			if (data.NegativeButtonText != null)
				builder.SetNegativeButton(data.NegativeButtonText, (sender, e) => { data.NegativeAction?.Invoke(); });

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