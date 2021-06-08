using Android.App;
using Android.OS;
using OkkeiPatcher.Model.DTO;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class NotificationDialogFragment : DialogFragment
	{
		private const string TitleIdIntKey = "titleId";
		private const string MessageIdIntKey = "messageId";
		private const string PositiveButtonIdIntKey = "positiveButtonId";
		private const string NegativeButtonIdIntKey = "negaitiveButtonId";
		private const string ErrorStringKey = "errorString";
		private int _titleId, _messageId, _positiveButtonTextId, _negativeButtonTextId;
		private string _error;

		public static NotificationDialogFragment NewInstance(MessageData messageData)
		{
			var fragment = new NotificationDialogFragment();
			var args = new Bundle();

			args.PutInt(TitleIdIntKey, messageData.TitleId);
			args.PutInt(MessageIdIntKey, messageData.MessageId);
			args.PutInt(PositiveButtonIdIntKey, messageData.PositiveButtonTextId);
			if (messageData.NegativeButtonTextId != 0)
				args.PutInt(NegativeButtonIdIntKey, messageData.NegativeButtonTextId);
			if (messageData.Error != null)
				args.PutString(ErrorStringKey, messageData.Error);

			fragment.Arguments = args;
			return fragment;
		}

		public override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			var args = Arguments;
			if (args == null) return;

			_titleId = args.GetInt(TitleIdIntKey);
			_messageId = args.GetInt(MessageIdIntKey);
			_positiveButtonTextId = args.GetInt(PositiveButtonIdIntKey);
			_negativeButtonTextId = args.GetInt(NegativeButtonIdIntKey);
			_error = args.GetString(ErrorStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(_titleId);
			if (_error != null)
				builder.SetMessage(string.Format(GetText(_messageId), _error));
			else builder.SetMessage(_messageId);
			builder.SetPositiveButton(_positiveButtonTextId, (sender, e) => { });
			if (_negativeButtonTextId != 0)
				builder.SetNegativeButton(_negativeButtonTextId, (sender, e) => { });
			return builder.Create();
		}
	}
}