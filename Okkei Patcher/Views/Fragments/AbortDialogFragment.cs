using Android.App;
using Android.OS;
using AndroidX.Lifecycle;
using OkkeiPatcher.ViewModels;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class AbortDialogFragment : DialogFragment
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
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(Resource.String.warning)
				.SetMessage(Resource.String.abort_warning)
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) => _viewModel.AbortCurrentProcess())
				.SetNegativeButton(Resource.String.dialog_cancel, (sender, e) => { })
				.Create();
		}
	}
}