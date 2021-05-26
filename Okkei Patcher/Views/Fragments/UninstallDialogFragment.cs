using Android.App;
using Android.OS;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class UninstallDialogFragment : DialogFragment
	{
		private const string TitleStringKey = "titleText";
		private const string MessageStringKey = "messageText";
		private const string PositiveButtonStringKey = "positiveButtonText";
		private const string PackageNameStringKey = "packageName";
		private string _title, _message, _positiveButtonText, _packageName;

		public static UninstallDialogFragment NewInstance(UninstallMessageData messageData)
		{
			var fragment = new UninstallDialogFragment();
			var args = new Bundle();

			args.PutString(TitleStringKey, messageData.Data.Title);
			args.PutString(MessageStringKey, messageData.Data.Message);
			args.PutString(PositiveButtonStringKey, messageData.Data.PositiveButtonText);
			args.PutString(PackageNameStringKey, messageData.PackageName);

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
			_packageName = args.GetString(PackageNameStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(_title)
				.SetMessage(_message)
				.SetCancelable(false)
				.SetPositiveButton(_positiveButtonText,
					(sender, e) => PackageManagerUtils.UninstallPackage(RequireActivity(), _packageName))
				.Create();
		}
	}
}