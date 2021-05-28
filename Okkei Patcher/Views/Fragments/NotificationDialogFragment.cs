using Android.App;
using Android.OS;
using OkkeiPatcher.Model.DTO;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class NotificationDialogFragment : DialogFragment
	{
		private const string TitleStringKey = "titleText";
		private const string MessageStringKey = "messageText";
		private const string PositiveButtonStringKey = "positiveButtonText";
		private const string NegativeButtonStringKey = "negaitiveButtonText";
		private string _title, _message, _positiveButtonText, _negativeButtonText;

		public static NotificationDialogFragment NewInstance(MessageData messageData)
		{
			var fragment = new NotificationDialogFragment();
			var args = new Bundle();

			args.PutString(TitleStringKey, messageData.Title);
			args.PutString(MessageStringKey, messageData.Message);
			args.PutString(PositiveButtonStringKey, messageData.PositiveButtonText);
			if (messageData.NegativeButtonText != null)
				args.PutString(NegativeButtonStringKey, messageData.NegativeButtonText);

			fragment.Arguments = args;
			return fragment;
		}

		public override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			var args = Arguments;
			if (args == null) return;

			_title = args.GetString(TitleStringKey);
			_message = args.GetString(MessageStringKey);
			_positiveButtonText = args.GetString(PositiveButtonStringKey);
			_negativeButtonText = args.GetString(NegativeButtonStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(_title)
				.SetMessage(_message)
				.SetCancelable(false)
				.SetPositiveButton(_positiveButtonText, (sender, e) => { });
			if (_negativeButtonText != null)
				builder.SetNegativeButton(_negativeButtonText, (sender, e) => { });
			return builder.Create();
		}
	}
}