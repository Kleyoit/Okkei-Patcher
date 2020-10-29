using Android.App;
using Android.Content;
using Xamarin.Essentials;

namespace OkkeiPatcher
{
	public static class MessageBox
	{
		private static Activity _getMessageBoxActivity;

		public enum Code
		{
			OK,
			UnknownAppSourceNotice,
			Exit
		}

		public static void Show(Activity callerActivity, string title, string message, MessageBox.Code id)
		{
			MessageBox._getMessageBoxActivity = callerActivity;

			Android.App.AlertDialog.Builder builder;
			builder = new Android.App.AlertDialog.Builder(callerActivity);
			builder.SetTitle(title);
			builder.SetMessage(message);
			builder.SetCancelable(false);
			switch (id)
			{
				case MessageBox.Code.OK:
					builder.SetPositiveButton("OK", delegate { });
					break;
				case MessageBox.Code.UnknownAppSourceNotice:
					builder.SetPositiveButton("OK", MessageBoxOkUnknownAppSourceNoticeAction);
					break;
				case MessageBox.Code.Exit:
					builder.SetPositiveButton("EXIT", MessageBoxExitAction);
					break;
			}
			Dialog dialog = builder.Create();
			dialog.Show();
		}

		private static void MessageBoxOkUnknownAppSourceNoticeAction(object sender, DialogClickEventArgs e)
		{
			Intent intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
			_getMessageBoxActivity.StartActivityForResult(intent, (int)GlobalData.RequestCodes.UnknownAppSourceCode);
		}

		private static void MessageBoxExitAction(object sender, DialogClickEventArgs e)
		{
			System.Environment.Exit(0);
		}
	}
}