using Android.App;
using Android.Content;
using Xamarin.Essentials;

namespace OkkeiPatcher
{
	public static class MessageBox
	{
		private static Activity _getMessageBoxActivity;

		public struct Data
		{
			public string Title { get; }
			public string Message { get; }
			public Code Id { get; }

			public static Data Empty => new Data(string.Empty, string.Empty, Code.OK);

			public Data(string title, string message, Code id)
			{
				Title = title;
				Message = message;
				Id = id;
			}
		}

		public enum Code
		{
			OK,
			UnknownAppSourceNotice,
			Exit
		}

		public static void Show(Activity callerActivity, string title, string message, MessageBox.Code id) =>
			Show(callerActivity, new Data(title, message, id));

		public static void Show(Activity callerActivity, Data data)
		{
			MessageBox._getMessageBoxActivity = callerActivity;

			var builder = new AlertDialog.Builder(Application.Context);
			builder.SetTitle(data.Title);
			builder.SetMessage(data.Message);
			builder.SetCancelable(false);
			switch (data.Id)
			{
				case MessageBox.Code.OK:
					builder.SetPositiveButton(Application.Context.Resources.GetText(Resource.String.dialog_ok),
						delegate { });
					break;
				case MessageBox.Code.UnknownAppSourceNotice:
					builder.SetPositiveButton(Application.Context.Resources.GetText(Resource.String.dialog_ok),
						MessageBoxOkUnknownAppSourceNoticeAction);
					break;
				case MessageBox.Code.Exit:
					builder.SetPositiveButton(Application.Context.Resources.GetText(Resource.String.dialog_exit),
						MessageBoxExitAction);
					break;
			}

			builder.Create()?.Show();
		}

		private static void MessageBoxOkUnknownAppSourceNoticeAction(object sender, DialogClickEventArgs e)
		{
			Intent intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
				Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
			_getMessageBoxActivity.StartActivityForResult(intent, (int) GlobalData.RequestCodes.UnknownAppSourceCode);
		}

		private static void MessageBoxExitAction(object sender, DialogClickEventArgs e) =>
			System.Environment.Exit(0);
	}
}