using Android.App;
using Android.OS;
using AndroidX.Lifecycle;
using OkkeiPatcher.Patcher;
using OkkeiPatcher.ViewModels;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class ClearDataDialogFragment : DialogFragment
	{
		private MainViewModel _viewModel;

		public override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			_viewModel =
				new ViewModelProvider(RequireActivity()).Get(Java.Lang.Class.FromType(typeof(MainViewModel))) as
					MainViewModel;
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			var installer = new PackageInstaller(_viewModel.ProgressProvider);
			installer.InstallFailed += _viewModel.PackageInstallerOnInstallFailed;

			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(Resource.String.warning)
				.SetMessage(Resource.String.clear_data_warning)
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) => _viewModel.ClearData())
				.SetNegativeButton(Resource.String.dialog_cancel, (sender, args) => { })
				.Create();
		}
	}
}