using System.Globalization;
using Android.App;
using Android.OS;
using AndroidX.Lifecycle;
using OkkeiPatcher.ViewModels;
using Xamarin.Essentials;
using DialogFragment = AndroidX.Fragment.App.DialogFragment;

namespace OkkeiPatcher.Views.Fragments
{
	public class AppUpdateDialogFragment : DialogFragment
	{
		private const string UpdateSizeDoubleKey = "updateSize";
		private const string ChangelogStringKey = "changelog";
		private string _changelog;
		private double _updateSize;
		private MainViewModel _viewModel;

		public static AppUpdateDialogFragment NewInstance(double updateSize, string changelog)
		{
			var fragment = new AppUpdateDialogFragment();
			var args = new Bundle();

			args.PutDouble(UpdateSizeDoubleKey, updateSize);
			args.PutString(ChangelogStringKey, changelog);

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

			_updateSize = args.GetDouble(UpdateSizeDoubleKey);
			_changelog = args.GetString(ChangelogStringKey);
		}

		public override Dialog OnCreateDialog(Bundle savedInstanceState)
		{
			return new AndroidX.AppCompat.App.AlertDialog.Builder(RequireActivity())
				.SetTitle(Resource.String.update_header)
				.SetMessage(string.Format(RequireActivity().GetText(Resource.String.update_app_available),
					AppInfo.VersionString,
					_updateSize.ToString(CultureInfo.CurrentCulture), _changelog))
				.SetPositiveButton(Resource.String.dialog_update, (sender, e) => _viewModel.UpdateApp())
				.SetNegativeButton(Resource.String.dialog_cancel, (sender, e) => { })
				.Create();
		}
	}
}