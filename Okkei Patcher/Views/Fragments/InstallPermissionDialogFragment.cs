using Android.App;
using Android.Content;
using Android.OS;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class InstallPermissionDialogFragment : DialogFragment
	{
		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(Resource.String.attention)
				.SetMessage(Resource.String.unknown_sources_notice)
				.SetCancelable(false)
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) =>
				{
					var intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
						Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
					RequireActivity().StartActivityForResult(intent, (int) RequestCodes.UnknownAppSourceSettingsCode);
				})
				.Create();
		}
	}
}