using System.Threading.Tasks;
using Android.App;
using Android.OS;
using AndroidX.Fragment.App;
using AndroidX.Lifecycle;
using OkkeiPatcher.ViewModels;
using Xamarin.Essentials;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class ManifestPromptDialogFragment : DialogFragment
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
			FragmentActivity context = RequireActivity();
			Cancelable = false;
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(Resource.String.attention)
				.SetMessage(Resource.String.manifest_prompt)
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) => Task.Run(async () =>
				{
					bool manifestRetrieved = await _viewModel.RetrieveManifestAsync();
					if (!manifestRetrieved) return;
					if (_viewModel.IsPatchUpdateAvailable())
					{
						MainThread.BeginInvokeOnMainThread(() =>
							new PatchUpdateDialogFragment().Show(context.SupportFragmentManager,
								nameof(PatchUpdateDialogFragment)));
						return;
					}

					if (_viewModel.IsAppUpdateAvailable())
						MainThread.BeginInvokeOnMainThread(() =>
							new AppUpdateDialogFragment().Show(context.SupportFragmentManager,
								nameof(AppUpdateDialogFragment)));
				}))
				.Create();
		}
	}
}