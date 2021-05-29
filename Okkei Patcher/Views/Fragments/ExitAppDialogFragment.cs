using Android.App;
using Android.OS;
using AndroidX.Lifecycle;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.ViewModels;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class ExitAppDialogFragment : DialogFragment
	{
		private const string TitleIdIntKey = "titleId";
		private const string MessageIdIntKey = "messageId";
		private int _titleId, _messageId;
		private MainViewModel _viewModel;

		public static ExitAppDialogFragment NewInstance(MessageData messageData)
		{
			return NewInstance(messageData.TitleId, messageData.MessageId);
		}

		public static ExitAppDialogFragment NewInstance(int titleId, int messageId)
		{
			var fragment = new ExitAppDialogFragment();
			var args = new Bundle();

			args.PutInt(TitleIdIntKey, titleId);
			args.PutInt(MessageIdIntKey, messageId);

			fragment.Arguments = args;
			return fragment;
		}

		public override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			_viewModel =
				new ViewModelProvider(RequireActivity()).Get(Java.Lang.Class.FromType(typeof(MainViewModel))) as
					MainViewModel;

			var args = Arguments;
			if (args == null) return;

			_titleId = args.GetInt(TitleIdIntKey);
			_messageId = args.GetInt(MessageIdIntKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			_viewModel.Exiting = true;
			Cancelable = false;
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(_titleId)
				.SetMessage(_messageId)
				.SetPositiveButton(Resource.String.dialog_exit, (sender, e) => System.Environment.Exit(0))
				.Create();
		}
	}
}