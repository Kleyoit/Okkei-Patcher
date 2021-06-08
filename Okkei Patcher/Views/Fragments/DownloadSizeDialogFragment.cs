using Android.App;
using Android.OS;
using AndroidX.Lifecycle;
using OkkeiPatcher.ViewModels;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class DownloadSizeDialogFragment : DialogFragment
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
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(Resource.String.warning)
				.SetMessage(string.Format(GetText(Resource.String.download_size_warning),
					_viewModel.GetPatchSize().ToString()))
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) => _viewModel.StartPatch())
				.SetNegativeButton(Resource.String.dialog_cancel, (sender, e) => { })
				.Create();
		}
	}
}