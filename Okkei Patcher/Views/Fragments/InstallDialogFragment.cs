using Android.App;
using Android.Net;
using Android.OS;
using AndroidX.Lifecycle;
using Java.IO;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Patcher;
using OkkeiPatcher.ViewModels;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class InstallDialogFragment : DialogFragment
	{
		private const string TitleStringKey = "titleText";
		private const string MessageStringKey = "messageText";
		private const string PositiveButtonStringKey = "positiveButtonText";
		private const string FilePathStringKey = "filePath";
		private string _title, _message, _positiveButtonText, _filePath;
		private MainViewModel _viewModel;

		public static InstallDialogFragment NewInstance(InstallMessageData messageData)
		{
			var fragment = new InstallDialogFragment();
			var args = new Bundle();

			args.PutString(TitleStringKey, messageData.Data.Title);
			args.PutString(MessageStringKey, messageData.Data.Message);
			args.PutString(PositiveButtonStringKey, messageData.Data.PositiveButtonText);
			args.PutString(FilePathStringKey, messageData.FilePath);

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

			_title = args.GetString(TitleStringKey);
			_message = args.GetString(MessageStringKey);
			_positiveButtonText = args.GetString(PositiveButtonStringKey);
			_filePath = args.GetString(FilePathStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			var installer = new PackageInstaller(_viewModel.ProgressProvider);
			installer.InstallFailed += _viewModel.PackageInstallerOnInstallFailed;

			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(_title)
				.SetMessage(_message)
				.SetCancelable(false)
				.SetPositiveButton(_positiveButtonText,
					(sender, e) => installer.InstallPackage(RequireActivity(), Uri.FromFile(new File(_filePath))))
				.Create();
		}
	}
}