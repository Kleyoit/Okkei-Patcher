using System;
using System.ComponentModel;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Lifecycle;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.Snackbar;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using OkkeiPatcher.ViewModels;
using OkkeiPatcher.Views.Fragments;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Views.Activities
{
	[Activity(Label = "@string/app_name", Theme = "@style/AppTheme", //ScreenOrientation = ScreenOrientation.Portrait,
		MainLauncher = true, LaunchMode = LaunchMode.SingleTop)]
	public class MainActivity : AppCompatActivity
	{
		private bool _backPressed;
		private int _lastBackPressedTimestamp;
		private MainViewModel _viewModel;

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			SetContentView(Resource.Layout.activity_main);

			//Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

			SubscribeToViewsEvents();

			SetUnpatchButtonState();
			RequestReadWriteStoragePermissions();

			_viewModel =
				new ViewModelProvider(this).Get(Java.Lang.Class.FromType(typeof(MainViewModel))) as MainViewModel;

			SubscribeToViewModel();
			SetStateFromViewModel();
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			switch (requestCode)
			{
				case (int) RequestCodes.UnknownAppSourceSettingsCode:
					OnRequestInstallPermissionResult();
					break;
				case (int) RequestCodes.StoragePermissionSettingsCode:
					OnRequestStoragePermissionResult();
					break;
				case (int) RequestCodes.UninstallCode:
					_viewModel.OnUninstallResult();
					break;
				case (int) RequestCodes.KitKatInstallCode:
					if (resultCode == Result.Ok)
					{
						_viewModel.OnInstallSuccess();
						break;
					}

					_viewModel.OnInstallFail();
					break;
			}
		}

		protected override void OnNewIntent(Intent intent)
		{
			if (!ActionPackageInstalled.Equals(intent.Action)) return;
			var extras = intent.Extras;
			var status = extras?.GetInt(PackageInstaller.ExtraStatus);
			//var message = extras?.GetString(PackageInstaller.ExtraStatusMessage);

			switch (status)
			{
				case (int) PackageInstallStatus.PendingUserAction:
					// Ask user to confirm the installation
					var confirmIntent = (Intent) extras.Get(Intent.ExtraIntent);
					StartActivity(confirmIntent);
					break;
				case (int) PackageInstallStatus.Success:
					_viewModel.OnInstallSuccess();
					break;
			}
		}

		public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
			[GeneratedEnum] Permission[] grantResults)
		{
			base.OnRequestPermissionsResult(requestCode, permissions, grantResults);


			// Request read/write external storage permissions on first start
			if (requestCode != (int) RequestCodes.StoragePermissionRequestCode ||
			    Build.VERSION.SdkInt < BuildVersionCodes.M) return;
			if (grantResults[0] != Permission.Granted)
			{
				if (ShouldShowRequestPermissionRationale(permissions[0]))
				{
					PermissionsRationaleDialogFragment.NewInstance(permissions).Show(SupportFragmentManager,
						nameof(PermissionsRationaleDialogFragment));
					return;
				}

				Preferences.Set(Prefkey.extstorage_permission_denied.ToString(), true);

				ExitAppDialogFragment.NewInstance(Resource.String.error, Resource.String.no_storage_permission)
					.Show(SupportFragmentManager, nameof(ExitAppDialogFragment));

				return;
			}

			Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
			Directory.CreateDirectory(OkkeiFilesPath);

			_viewModel.UnpatchEnabled = OkkeiUtils.IsBackupAvailable();

			if (RequestInstallPackagesPermission())
				new ManifestPromptDialogFragment().Show(SupportFragmentManager, nameof(ManifestPromptDialogFragment));
		}

		public override void OnBackPressed()
		{
			var lastBackPressedTimestampTemp = _lastBackPressedTimestamp;
			_lastBackPressedTimestamp = System.Environment.TickCount;
			var sinceLastBackPressed = _lastBackPressedTimestamp - lastBackPressedTimestampTemp;

			if (_backPressed && sinceLastBackPressed <= 2000)
			{
				_backPressed = false;
				base.OnBackPressed();
				return;
			}

			_backPressed = true;
			Toast.MakeText(this, Resources.GetText(Resource.String.back_button_pressed), ToastLength.Short).Show();
		}

		protected override void OnDestroy()
		{
			UnsubscribeViewsEvents();
			UnsubscribeFromViewModel();
			base.OnDestroy();
		}

		private void OnRequestInstallPermissionResult()
		{
			if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
			if (!PackageManager.CanRequestPackageInstalls())
			{
				ExitAppDialogFragment.NewInstance(Resource.String.error, Resource.String.no_install_permission)
					.Show(SupportFragmentManager, nameof(ExitAppDialogFragment));
				return;
			}

			new ManifestPromptDialogFragment().Show(SupportFragmentManager,
				nameof(ManifestPromptDialogFragment));
		}

		private void OnRequestStoragePermissionResult()
		{
			if (Build.VERSION.SdkInt < BuildVersionCodes.M) return;
			if (CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
			{
				ExitAppDialogFragment.NewInstance(Resource.String.error, Resource.String.no_storage_permission)
					.Show(SupportFragmentManager, nameof(ExitAppDialogFragment));
				return;
			}

			Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
			Directory.CreateDirectory(OkkeiFilesPath);
			if (RequestInstallPackagesPermission())
				new ManifestPromptDialogFragment().Show(SupportFragmentManager,
					nameof(ManifestPromptDialogFragment));
		}

		private void SubscribeToViewsEvents()
		{
			FindViewById<FloatingActionButton>(Resource.Id.infoButton).Click += InfoButton_Click;
			FindViewById<Button>(Resource.Id.patchButton).Click += Patch_Click;
			FindViewById<Button>(Resource.Id.unpatchButton).Click += Unpatch_Click;
			FindViewById<Button>(Resource.Id.clearDataButton).Click += ClearData_Click;
			FindViewById<CheckBox>(Resource.Id.savedataCheckBox).Click += SavedataCheckBox_Click;
		}

		private void UnsubscribeViewsEvents()
		{
			FindViewById<FloatingActionButton>(Resource.Id.infoButton).Click -= InfoButton_Click;
			FindViewById<Button>(Resource.Id.patchButton).Click -= Patch_Click;
			FindViewById<Button>(Resource.Id.unpatchButton).Click -= Unpatch_Click;
			FindViewById<Button>(Resource.Id.clearDataButton).Click -= ClearData_Click;
			FindViewById<CheckBox>(Resource.Id.savedataCheckBox).Click -= SavedataCheckBox_Click;
		}

		private void SubscribeToViewModel()
		{
			_viewModel.MessageGenerated += OnMessage;
			_viewModel.InstallMessageGenerated += OnInstallMessage;
			_viewModel.UninstallMessageGenerated += OnUninstallMessage;
			_viewModel.FatalErrorOccurred += ViewModelOnFatalErrorOccurred;
			_viewModel.PropertyChanged += ViewModelOnPropertyChanged;
		}

		private void UnsubscribeFromViewModel()
		{
			_viewModel.MessageGenerated -= OnMessage;
			_viewModel.InstallMessageGenerated -= OnInstallMessage;
			_viewModel.UninstallMessageGenerated -= OnUninstallMessage;
			_viewModel.FatalErrorOccurred -= ViewModelOnFatalErrorOccurred;
			_viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
		}

		private void ViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(_viewModel.PatchEnabled):
					UpdatePatchEnabled();
					break;
				case nameof(_viewModel.UnpatchEnabled):
					UpdateUnpatchEnabled();
					break;
				case nameof(_viewModel.PatchText):
					UpdatePatchText();
					break;
				case nameof(_viewModel.UnpatchText):
					UpdateUnpatchText();
					break;
				case nameof(_viewModel.ClearDataEnabled):
					UpdateClearDataEnabled();
					break;
				case nameof(_viewModel.ProcessSavedataEnabled):
					UpdateProcessSavedataEnabled();
					break;
				case nameof(_viewModel.ProgressIndeterminate):
					UpdateProgressIndeterminate();
					break;
				case nameof(_viewModel.Progress):
					UpdateProgress();
					break;
				case nameof(_viewModel.ProgressMax):
					UpdateProgressMax();
					break;
				case nameof(_viewModel.Status):
					UpdateStatus();
					break;
			}
		}

		private void SetStateFromViewModel()
		{
			UpdatePatchEnabled();
			UpdateUnpatchEnabled();
			UpdatePatchText();
			UpdateUnpatchText();
			UpdateClearDataEnabled();
			UpdateProcessSavedataEnabled();
			UpdateProgressIndeterminate();
			UpdateProgress();
			UpdateProgressMax();
			UpdateStatus();
		}

		private void UpdatePatchEnabled()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<Button>(Resource.Id.patchButton).Enabled = _viewModel.PatchEnabled);
		}

		private void UpdateUnpatchEnabled()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<Button>(Resource.Id.unpatchButton).Enabled = _viewModel.UnpatchEnabled);
		}

		private void UpdatePatchText()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<Button>(Resource.Id.patchButton).Text = _viewModel.PatchText);
		}

		private void UpdateUnpatchText()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<Button>(Resource.Id.unpatchButton).Text = _viewModel.UnpatchText);
		}

		private void UpdateClearDataEnabled()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<Button>(Resource.Id.clearDataButton).Enabled = _viewModel.ClearDataEnabled);
		}

		private void UpdateProcessSavedataEnabled()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<CheckBox>(Resource.Id.savedataCheckBox).Checked = _viewModel.ProcessSavedataEnabled);
		}

		private void UpdateProgressIndeterminate()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<ProgressBar>(Resource.Id.progressBar).Indeterminate = _viewModel.ProgressIndeterminate);
		}

		private void UpdateProgress()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<ProgressBar>(Resource.Id.progressBar).Progress = _viewModel.Progress);
		}

		private void UpdateProgressMax()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<ProgressBar>(Resource.Id.progressBar).Max = _viewModel.ProgressMax);
		}

		private void UpdateStatus()
		{
			MainThread.BeginInvokeOnMainThread(() =>
				FindViewById<TextView>(Resource.Id.statusText).Text = _viewModel.Status);
		}

		private void OnMessage(object sender, MessageData e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
				NotificationDialogFragment.NewInstance(e)
					.Show(SupportFragmentManager, nameof(NotificationDialogFragment)));
		}

		private void OnInstallMessage(object sender, InstallMessageData e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
				InstallDialogFragment.NewInstance(e).Show(SupportFragmentManager, nameof(InstallDialogFragment)));
		}

		private void OnUninstallMessage(object sender, UninstallMessageData e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
				UninstallDialogFragment.NewInstance(e).Show(SupportFragmentManager, nameof(UninstallDialogFragment)));
		}

		private void ViewModelOnFatalErrorOccurred(object sender, MessageData e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
				ExitAppDialogFragment.NewInstance(e).Show(SupportFragmentManager, nameof(ExitAppDialogFragment)));
		}

		private void SetUnpatchButtonState()
		{
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false) &&
			    (Build.VERSION.SdkInt < BuildVersionCodes.M ||
			     CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted))
				_viewModel.UnpatchEnabled = OkkeiUtils.IsBackupAvailable();
		}

		private bool RequestInstallPackagesPermission()
		{
			if (Build.VERSION.SdkInt < BuildVersionCodes.O || PackageManager.CanRequestPackageInstalls())
				return true;

			new InstallPermissionDialogFragment().Show(SupportFragmentManager, nameof(InstallPermissionDialogFragment));
			return false;
		}

		private void RequestReadWriteStoragePermissions()
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.M &&
			    CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
			{
				if (!Preferences.Get(Prefkey.extstorage_permission_denied.ToString(), false))
				{
					string[] extStoragePermissions =
						{Manifest.Permission.WriteExternalStorage, Manifest.Permission.ReadExternalStorage};
					RequestPermissions(extStoragePermissions, (int) RequestCodes.StoragePermissionRequestCode);
					return;
				}

				new StoragePermissionsSettingsDialogFragment().Show(SupportFragmentManager,
					nameof(StoragePermissionsSettingsDialogFragment));
				return;
			}

			Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
			Directory.CreateDirectory(OkkeiFilesPath);
			if (RequestInstallPackagesPermission())
				new ManifestPromptDialogFragment().Show(SupportFragmentManager, nameof(ManifestPromptDialogFragment));
		}

		private void SavedataCheckBox_Click(object sender, EventArgs e)
		{
			_viewModel.ProcessSavedataEnabled = ((CheckBox) sender).Checked;
		}

		private void Patch_Click(object sender, EventArgs e)
		{
			if (!_viewModel.CanPatch) return;
			if (!_viewModel.Patching)
			{
				new PatchWarningDialogFragment().Show(SupportFragmentManager, nameof(PatchWarningDialogFragment));
				return;
			}

			new AbortDialogFragment().Show(SupportFragmentManager, nameof(AbortDialogFragment));
		}

		private void Unpatch_Click(object sender, EventArgs e)
		{
			if (!_viewModel.CanUnpatch) return;
			if (!_viewModel.Unpatching)
			{
				new UnpatchWarningDialogFragment().Show(SupportFragmentManager, nameof(UnpatchWarningDialogFragment));
				return;
			}

			new AbortDialogFragment().Show(SupportFragmentManager, nameof(AbortDialogFragment));
		}

		private void ClearData_Click(object sender, EventArgs e)
		{
			if (_viewModel.Patching || _viewModel.Unpatching) return;
			new ClearDataDialogFragment().Show(SupportFragmentManager, nameof(ClearDataDialogFragment));
		}

		private void InfoButton_Click(object sender, EventArgs eventArgs)
		{
			var view = (View) sender;
			Snackbar.Make(view,
					string.Format(Resources.GetText(Resource.String.fab_version), AppInfo.VersionString),
					BaseTransientBottomBar.LengthLong)
				.SetAction("Action", (View.IOnClickListener) null).Show();
		}
	}
}