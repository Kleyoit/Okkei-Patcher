using Android.App;
using Android.Content;
using Android.OS;
using OkkeiPatcher.Core;
using Xamarin.Essentials;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class InstallPermissionDialogFragment : DialogFragment
	{
		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			Cancelable = false;
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(Resource.String.attention)
				.SetMessage(Resource.String.unknown_sources_notice)
				.SetPositiveButton(Resource.String.dialog_ok, (sender, e) =>
				{
					var intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
						Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
					RequireActivity().StartActivityForResult(intent, (int) RequestCode.UnknownAppSourceSettingsCode);
				})
				.Create();
		}
	}
}