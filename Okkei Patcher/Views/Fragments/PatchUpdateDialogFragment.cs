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
		private const string PatchSizeIntKey = "patchSize";
		private int _patchSize;
		private MainViewModel _viewModel;

		public static PatchUpdateDialogFragment NewInstance(int patchSize)
		{
			var fragment = new PatchUpdateDialogFragment();
			var args = new Bundle();

			args.PutInt(PatchSizeIntKey, patchSize);

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

			_patchSize = args.GetInt(PatchSizeIntKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(Resource.String.update_header)
				.SetMessage(string.Format(RequireActivity().GetText(Resource.String.update_patch_available),
					_patchSize.ToString()))
				.SetCancelable(false)
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) =>
				{
					if (_viewModel.CheckForAppUpdates())
						MainThread.BeginInvokeOnMainThread(() =>
						{
							var updateSize = _viewModel.GetAppUpdateSize();
							var changelog = _viewModel.GetAppChangelog();
							AppUpdateDialogFragment.NewInstance(updateSize, changelog)
								.Show(RequireActivity().SupportFragmentManager, nameof(AppUpdateDialogFragment));
						});
				})
				.Create();
		}
	}
}