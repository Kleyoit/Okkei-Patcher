using Android.App;
using Android.OS;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class UninstallDialogFragment : DialogFragment
	{
		private const string TitleIdIntKey = "titleText";
		private const string MessageIdIntKey = "messageText";
		private const string PackageNameStringKey = "packageName";
		private int _titleId, _messageId;
		private string _packageName;

		internal static UninstallDialogFragment NewInstance(UninstallMessageData messageData)
		{
			var fragment = new UninstallDialogFragment();
			var args = new Bundle();

			args.PutInt(TitleIdIntKey, messageData.Data.TitleId);
			args.PutInt(MessageIdIntKey, messageData.Data.MessageId);
			args.PutString(PackageNameStringKey, messageData.PackageName);

			fragment.Arguments = args;
			return fragment;
		}

		public override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			Bundle args = Arguments;
			if (args == null) return;

			_titleId = args.GetInt(TitleIdIntKey);
			_messageId = args.GetInt(MessageIdIntKey);
			_packageName = args.GetString(PackageNameStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			Cancelable = false;
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(_titleId)
				.SetMessage(_messageId)
				.SetPositiveButton(Resource.String.dialog_ok,
					(sender, e) => PackageManagerUtils.UninstallPackage(RequireActivity(), _packageName))
				.Create();
		}
	}
}