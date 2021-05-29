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
		private const string TitleIdIntKey = "titleId";
		private const string MessageIdIntKey = "messageId";
		private const string FilePathStringKey = "filePath";
		private int _titleId, _messageId;
		private string _filePath;
		private MainViewModel _viewModel;

		public static InstallDialogFragment NewInstance(InstallMessageData messageData)
		{
			var fragment = new InstallDialogFragment();
			var args = new Bundle();

			args.PutInt(TitleIdIntKey, messageData.Data.TitleId);
			args.PutInt(MessageIdIntKey, messageData.Data.MessageId);
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

			_titleId = args.GetInt(TitleIdIntKey);
			_messageId = args.GetInt(MessageIdIntKey);
			_filePath = args.GetString(FilePathStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			Cancelable = false;

			var installer = new PackageInstaller(_viewModel.ProgressProvider);
			installer.InstallFailed += _viewModel.PackageInstallerOnInstallFailed;

			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(_titleId)
				.SetMessage(_messageId)
				.SetPositiveButton(Resource.String.dialog_ok,
					(sender, e) => installer.InstallPackage(RequireActivity(), Uri.FromFile(new File(_filePath))))
				.Create();
		}
	}
}