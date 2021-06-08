using Android.App;
using Android.OS;
using AndroidX.Lifecycle;
using OkkeiPatcher.ViewModels;
using Xamarin.Essentials;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class PatchUpdateDialogFragment : DialogFragment
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
				.SetTitle(Resource.String.update_header)
				.SetMessage(string.Format(GetText(Resource.String.update_patch_available),
					_viewModel.GetPatchSize().ToString()))
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) =>
				{
					if (_viewModel.IsAppUpdateAvailable())
						MainThread.BeginInvokeOnMainThread(() =>
							new AppUpdateDialogFragment().Show(RequireActivity().SupportFragmentManager,
								nameof(AppUpdateDialogFragment)));
				})
				.Create();
		}
	}
}