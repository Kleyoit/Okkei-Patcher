using Android.App;
using Android.OS;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class PatchWarningDialogFragment : DialogFragment
	{
		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(Resource.String.warning)
				.SetMessage(Resource.String.long_process_warning)
				.SetPositiveButton(Resource.String.dialog_ok,
					(sender, e) => new DownloadSizeDialogFragment().Show(RequireActivity().SupportFragmentManager,
						nameof(DownloadSizeDialogFragment)))
				.SetNegativeButton(Resource.String.dialog_cancel, (sender, e) => { })
				.Create();
		}
	}
}