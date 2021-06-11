using Android.App;
using Android.Content;
using Android.OS;
using OkkeiPatcher.Core;
using Xamarin.Essentials;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class StoragePermissionsSettingsDialogFragment : DialogFragment
	{
		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			Cancelable = false;
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(Resource.String.error)
				.SetMessage(Resource.String.no_storage_permission_settings)
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) =>
				{
					var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings,
						Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
					RequireActivity().StartActivityForResult(intent, (int) RequestCode.StoragePermissionSettingsCode);
				})
				.SetNegativeButton(Resource.String.dialog_exit, (sender, e) => System.Environment.Exit(0))
				.Create();
		}
	}
}