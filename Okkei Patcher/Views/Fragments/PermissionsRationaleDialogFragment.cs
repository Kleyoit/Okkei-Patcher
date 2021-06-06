using Android.App;
using Android.OS;
using OkkeiPatcher.Core;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class PermissionsRationaleDialogFragment : DialogFragment
	{
		private const string PermissionsStringArrayKey = "permissionsArray";
		private string[] _permissions;

		public static PermissionsRationaleDialogFragment NewInstance(string[] permissions)
		{
			var fragment = new PermissionsRationaleDialogFragment();
			var args = new Bundle();

			args.PutStringArray(PermissionsStringArrayKey, permissions);

			fragment.Arguments = args;
			return fragment;
		}

		public override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			var args = Arguments;
			if (args == null) return;

			_permissions = args.GetStringArray(PermissionsStringArrayKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			Cancelable = false;
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireContext())
				.SetTitle(Resource.String.error)
				.SetMessage(Resource.String.no_storage_permission_rationale)
				.SetPositiveButton(Resource.String.dialog_ok,
					(sender, e) =>
						RequireActivity()
							.RequestPermissions(_permissions, (int) RequestCodes.StoragePermissionRequestCode))
				.SetNegativeButton(Resource.String.dialog_exit, (sender, e) => System.Environment.Exit(0))
				.Create();
		}
	}
}