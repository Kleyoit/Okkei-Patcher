using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.Snackbar;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	[Activity(Label = "@string/app_name", Theme = "@style/AppTheme", ScreenOrientation = ScreenOrientation.Portrait,
		MainLauncher = true, LaunchMode = LaunchMode.SingleTop)]
	public class MainActivity : AppCompatActivity
	{
		private static readonly Lazy<Utils> UtilsInstance = new Lazy<Utils>(() => new Utils());

		private static readonly Lazy<PatchTools> PatchTools =
			new Lazy<PatchTools>(() => new PatchTools(UtilsInstance.Value));

		private static readonly Lazy<UnpatchTools> UnpatchTools =
			new Lazy<UnpatchTools>(() => new UnpatchTools(UtilsInstance.Value));

		private static readonly Lazy<ManifestTools> ManifestTools =
			new Lazy<ManifestTools>(() => new ManifestTools(UtilsInstance.Value));

		private FloatingActionButton _infoButton;
		private Button _patchButton;
		private Button _unpatchButton;
		private Button _clearDataButton;
		private CheckBox _savedataCheckBox;
		private TextView _statusText;
		private ProgressBar _progressBar;

		private bool _backPressed;
		private CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();
		private ToolsBase _currentToolsObject;
		private int _lastBackPressedTimestamp;
		private bool _patchToolsEventsSubscribed;
		private bool _unpatchToolsEventsSubscribed;

		private void InitViewFields()
		{
			_infoButton = FindViewById<FloatingActionButton>(Resource.Id.infoButton);
			_patchButton = FindViewById<Button>(Resource.Id.patchButton);
			_unpatchButton = FindViewById<Button>(Resource.Id.unpatchButton);
			_clearDataButton = FindViewById<Button>(Resource.Id.clearDataButton);
			_savedataCheckBox = FindViewById<CheckBox>(Resource.Id.savedataCheckBox);
			_statusText = FindViewById<TextView>(Resource.Id.statusText);
			_progressBar = FindViewById<ProgressBar>(Resource.Id.progressBar);
		}

		private void SubscribeToViewsEvents()
		{
			_infoButton.Click += InfoButton_Click;
			_patchButton.Click += Patch_Click;
			_unpatchButton.Click += Unpatch_Click;
			_clearDataButton.Click += ClearData_Click;
			_savedataCheckBox.CheckedChange += CheckBox_CheckedChange;
		}

		private ProcessState CreateProcessState()
		{
			var processSavedata = _savedataCheckBox.Checked;
			var scriptsUpdate = ManifestTools.Value.IsScriptsUpdateAvailable;
			var obbUpdate = ManifestTools.Value.IsObbUpdateAvailable;
			return new ProcessState(processSavedata, scriptsUpdate, obbUpdate);
		}

		private bool RequestInstallPackagesPermission()
		{
			if (Build.VERSION.SdkInt < BuildVersionCodes.O || PackageManager.CanRequestPackageInstalls())
				return true;

			MessageBox.Show(this, Resources.GetText(Resource.String.attention),
				Resources.GetText(Resource.String.unknown_sources_notice),
				Resources.GetText(Resource.String.dialog_ok),
				() =>
				{
					var intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
						Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
					StartActivityForResult(intent, (int) RequestCodes.UnknownAppSourceSettingsCode);
				});
			return false;
		}

		private void ShowManifestPrompt()
		{
			MessageBox.Show(this, Resources.GetText(Resource.String.attention),
				Resources.GetText(Resource.String.manifest_prompt), Resources.GetText(Resource.String.dialog_ok),
				() => Task.Run(RetrieveManifest));
		}

		private async Task RetrieveManifest()
		{
			_currentToolsObject = ManifestTools.Value;

			ManifestTools.Value.StatusChanged += OnStatusChanged;
			ManifestTools.Value.ProgressChanged += OnProgressChanged;
			ManifestTools.Value.MessageGenerated += OnMessageGenerated;
			ManifestTools.Value.ErrorOccurred += OnErrorOccurred_ManifestTools;
			ManifestTools.Value.PropertyChanged += OnPropertyChanged_ManifestTools;

			if (!await ManifestTools.Value.RetrieveManifest(_cancelTokenSource.Token)) return;

			CheckForUpdates();
		}

		private void CheckForUpdates()
		{
			if (ManifestTools.Value.IsPatchUpdateAvailable)
			{
				ShowPatchUpdatePrompt();
				return;
			}

			CheckForAppUpdates();
		}

		private void ShowPatchUpdatePrompt()
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				_patchButton.Enabled = true;
				MessageBox.Show(this, Resources.GetText(Resource.String.update_header),
					Java.Lang.String.Format(Resources.GetText(Resource.String.update_patch_available),
						ManifestTools.Value.PatchSizeInMB.ToString()),
					Resources.GetText(Resource.String.dialog_ok), CheckForAppUpdates);
			});
		}

		private void CheckForAppUpdates()
		{
			if (!ManifestTools.Value.IsAppUpdateAvailable) return;
			MainThread.BeginInvokeOnMainThread(ShowAppUpdatePrompt);
		}

		private void ShowAppUpdatePrompt()
		{
			MessageBox.Show(this, Resources.GetText(Resource.String.update_header),
				Java.Lang.String.Format(Resources.GetText(Resource.String.update_app_available), AppInfo.VersionString,
					ManifestTools.Value.AppUpdateSizeInMB.ToString(CultureInfo.CurrentCulture),
					ManifestTools.Value.Manifest.OkkeiPatcher.Changelog),
				Resources.GetText(Resource.String.dialog_update), Resources.GetText(Resource.String.dialog_cancel),
				() => ManifestTools.Value.UpdateApp(this, _cancelTokenSource.Token), null);
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			switch (requestCode)
			{
				case (int) RequestCodes.UnknownAppSourceSettingsCode:
					if (Build.VERSION.SdkInt < BuildVersionCodes.O) break;
					if (!PackageManager.CanRequestPackageInstalls())
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.error),
							Resources.GetText(Resource.String.no_install_permission),
							Resources.GetText(Resource.String.dialog_exit),
							() => System.Environment.Exit(0));
						break;
					}

					ShowManifestPrompt();
					break;
				case (int) RequestCodes.StoragePermissionSettingsCode:
					if (Build.VERSION.SdkInt < BuildVersionCodes.M) break;
					if (CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.error),
							Resources.GetText(Resource.String.no_storage_permission),
							Resources.GetText(Resource.String.dialog_exit),
							() => System.Environment.Exit(0));
						break;
					}

					Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
					Directory.CreateDirectory(OkkeiFilesPath);
					if (RequestInstallPackagesPermission()) ShowManifestPrompt();
					break;
				case (int) RequestCodes.UninstallCode:
					_currentToolsObject.OnUninstallResult(this, _cancelTokenSource.Token);
					break;
				case (int) RequestCodes.KitKatInstallCode:
					if (resultCode == Result.Ok)
					{
						_currentToolsObject.OnInstallSuccess(_cancelTokenSource.Token);
						break;
					}

					_currentToolsObject.NotifyInstallFailed();
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
					_currentToolsObject.OnInstallSuccess(_cancelTokenSource.Token);
					break;
			}
		}

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			SetContentView(Resource.Layout.activity_main);

			// Don't turn screen off
			Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

			InitViewFields();
			SubscribeToViewsEvents();

			SetApkIsPatchedPreferenceIfNotSet();
			SetButtonsState();
			SetCheckBoxState();
			RequestReadWriteStoragePermissions();
		}

		private static void SetApkIsPatchedPreferenceIfNotSet()
		{
			if (!Preferences.ContainsKey(Prefkey.apk_is_patched.ToString()))
				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
		}

		private void SetButtonsState()
		{
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
			{
				_patchButton.Enabled = false;
				_unpatchButton.Enabled = true;
				if (Build.VERSION.SdkInt < BuildVersionCodes.M ||
				    CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted)
					_unpatchButton.Enabled = Utils.IsBackupAvailable();
				return;
			}

			_patchButton.Enabled = true;
			_unpatchButton.Enabled = false;
		}

		private void SetCheckBoxState()
		{
			if (!Preferences.ContainsKey(Prefkey.backup_restore_savedata.ToString()))
			{
				Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);
				return;
			}

			_savedataCheckBox.Checked = Preferences.Get(Prefkey.backup_restore_savedata.ToString(), true);
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

				MessageBox.Show(this, Resources.GetText(Resource.String.error),
					Resources.GetText(Resource.String.no_storage_permission_settings),
					Resources.GetText(Resource.String.dialog_ok),
					Resources.GetText(Resource.String.dialog_exit),
					() =>
					{
						var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings,
							Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
						StartActivityForResult(intent, (int) RequestCodes.StoragePermissionSettingsCode);
					},
					() => System.Environment.Exit(0));
				return;
			}

			Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
			Directory.CreateDirectory(OkkeiFilesPath);
			if (RequestInstallPackagesPermission()) ShowManifestPrompt();
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
					MessageBox.Show(this, Resources.GetText(Resource.String.error),
						Resources.GetText(Resource.String.no_storage_permission_rationale),
						Resources.GetText(Resource.String.dialog_ok),
						Resources.GetText(Resource.String.dialog_exit),
						() => RequestPermissions(permissions, (int) RequestCodes.StoragePermissionRequestCode),
						() => System.Environment.Exit(0));
					return;
				}

				Preferences.Set(Prefkey.extstorage_permission_denied.ToString(), true);

				MessageBox.Show(this, Resources.GetText(Resource.String.error),
					Resources.GetText(Resource.String.no_storage_permission),
					Resources.GetText(Resource.String.dialog_exit),
					() => System.Environment.Exit(0));

				return;
			}

			Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
			Directory.CreateDirectory(OkkeiFilesPath);

			_unpatchButton.Enabled = Utils.IsBackupAvailable();

			if (RequestInstallPackagesPermission()) ShowManifestPrompt();
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

		private void OnPropertyChanged_PatchTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(PatchTools.Value.IsRunning)) return;
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (!PatchTools.Value.IsRunning)
				{
					_cancelTokenSource.Dispose();
					_cancelTokenSource = new CancellationTokenSource();

					_patchButton.Text = Resources.GetText(Resource.String.patch);
					_clearDataButton.Enabled = true;
					if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false)) return;
					_patchButton.Enabled = false;
					_unpatchButton.Enabled = Utils.IsBackupAvailable();

					return;
				}

				_unpatchButton.Enabled = false;
				_clearDataButton.Enabled = false;
				_patchButton.Text = Resources.GetText(Resource.String.abort);
			});
		}

		private void OnPropertyChanged_UnpatchTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(UnpatchTools.Value.IsRunning)) return;
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (!UnpatchTools.Value.IsRunning)
				{
					_cancelTokenSource.Dispose();
					_cancelTokenSource = new CancellationTokenSource();

					_unpatchButton.Text = Resources.GetText(Resource.String.unpatch);
					_clearDataButton.Enabled = true;
					if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
					{
						_unpatchButton.Enabled = false;
						_patchButton.Enabled = true;
						return;
					}

					_unpatchButton.Enabled = Utils.IsBackupAvailable();
					return;
				}

				_patchButton.Enabled = false;
				_clearDataButton.Enabled = false;
				_unpatchButton.Text = Resources.GetText(Resource.String.abort);
			});
		}

		private void OnPropertyChanged_ManifestTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(ManifestTools.Value.IsRunning)) return;
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (!ManifestTools.Value.IsRunning)
				{
					_cancelTokenSource.Dispose();
					_cancelTokenSource = new CancellationTokenSource();

					_patchButton.Enabled = !Preferences.Get(Prefkey.apk_is_patched.ToString(), false) ||
					                       ManifestTools.Value.IsPatchUpdateAvailable;
					_patchButton.Text = Resources.GetText(Resource.String.patch);
					_unpatchButton.Enabled = Utils.IsBackupAvailable();
					_clearDataButton.Enabled = true;
					return;
				}

				_patchButton.Enabled = true;
				_unpatchButton.Enabled = false;
				_clearDataButton.Enabled = false;
				_patchButton.Text = Resources.GetText(Resource.String.abort);
			});
		}

		private void OnStatusChanged(object sender, string e)
		{
			MainThread.BeginInvokeOnMainThread(() => _statusText.Text = e);
		}

		private void OnMessageGenerated(object sender, MessageBox.Data e)
		{
			MainThread.BeginInvokeOnMainThread(() => MessageBox.Show(this, e));
		}

		private void OnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (_progressBar.Indeterminate != e.IsIndeterminate) _progressBar.Indeterminate = e.IsIndeterminate;
				if (_progressBar.Max != e.Max) _progressBar.Max = e.Max;
				_progressBar.Progress = e.Progress;
			});
		}

		private void CheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
		{
			var prefkey = Prefkey.backup_restore_savedata.ToString();
			if (sender == _savedataCheckBox && Preferences.Get(prefkey, true) != e.IsChecked)
				Preferences.Set(prefkey, e.IsChecked);
		}

		private void Patch_Click(object sender, EventArgs e)
		{
			if (UnpatchTools.IsValueCreated && UnpatchTools.Value.IsRunning ||
			    _cancelTokenSource.IsCancellationRequested) return;
			if ((!PatchTools.IsValueCreated || !PatchTools.Value.IsRunning) &&
			    (!ManifestTools.IsValueCreated || !ManifestTools.Value.IsRunning))
			{
				MessageBox.Show(this, Resources.GetText(Resource.String.warning),
					Resources.GetText(Resource.String.long_process_warning),
					Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
					ShowDownloadSizeWarning, null);
				return;
			}

			MessageBox.Show(this, Resources.GetText(Resource.String.warning),
				Resources.GetText(Resource.String.abort_warning), Resources.GetText(Resource.String.dialog_ok),
				Resources.GetText(Resource.String.dialog_cancel), AbortPatch, null);
		}

		private void ShowDownloadSizeWarning()
		{
			MessageBox.Show(this, Resources.GetText(Resource.String.warning),
				Java.Lang.String.Format(Resources.GetText(Resource.String.download_size_warning),
					ManifestTools.Value.PatchSizeInMB), Resources.GetText(Resource.String.dialog_ok),
				Resources.GetText(Resource.String.dialog_cancel), StartPatch, null);
		}

		private void StartPatch()
		{
			_currentToolsObject = PatchTools.Value;

			if (!_patchToolsEventsSubscribed)
			{
				PatchTools.Value.StatusChanged += OnStatusChanged;
				PatchTools.Value.ProgressChanged += OnProgressChanged;
				PatchTools.Value.MessageGenerated += OnMessageGenerated;
				PatchTools.Value.PropertyChanged += OnPropertyChanged_PatchTools;
				PatchTools.Value.ErrorOccurred += OnErrorOccurred_PatchTools;
				_patchToolsEventsSubscribed = true;
			}

			PatchTools.Value.Patch(this, CreateProcessState(), ManifestTools.Value.Manifest, _cancelTokenSource.Token);
		}

		private void AbortPatch()
		{
			if (PatchTools.IsValueCreated && PatchTools.Value.IsRunning ||
			    ManifestTools.IsValueCreated && ManifestTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void OnErrorOccurred_PatchTools(object sender, EventArgs e)
		{
			if ((!UnpatchTools.IsValueCreated || !UnpatchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    PatchTools.IsValueCreated && PatchTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void Unpatch_Click(object sender, EventArgs e)
		{
			if (PatchTools.IsValueCreated && PatchTools.Value.IsRunning ||
			    _cancelTokenSource.IsCancellationRequested) return;
			if (!UnpatchTools.IsValueCreated || !UnpatchTools.Value.IsRunning)
			{
				MessageBox.Show(this, Resources.GetText(Resource.String.warning),
					Resources.GetText(Resource.String.long_process_warning),
					Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
					StartUnpatch, null);
				return;
			}

			MessageBox.Show(this, Resources.GetText(Resource.String.warning),
				Resources.GetText(Resource.String.abort_warning), Resources.GetText(Resource.String.dialog_ok),
				Resources.GetText(Resource.String.dialog_cancel), AbortUnpatch, null);
		}

		private void StartUnpatch()
		{
			_currentToolsObject = UnpatchTools.Value;

			if (!_unpatchToolsEventsSubscribed)
			{
				UnpatchTools.Value.StatusChanged += OnStatusChanged;
				UnpatchTools.Value.ProgressChanged += OnProgressChanged;
				UnpatchTools.Value.MessageGenerated += OnMessageGenerated;
				UnpatchTools.Value.PropertyChanged += OnPropertyChanged_UnpatchTools;
				UnpatchTools.Value.ErrorOccurred += OnErrorOccurred_UnpatchTools;
				_unpatchToolsEventsSubscribed = true;
			}

			UnpatchTools.Value.Unpatch(this, CreateProcessState(), _cancelTokenSource.Token);
		}

		private void AbortUnpatch()
		{
			if (UnpatchTools.IsValueCreated && UnpatchTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void OnErrorOccurred_UnpatchTools(object sender, EventArgs e)
		{
			if ((!PatchTools.IsValueCreated || !PatchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    UnpatchTools.IsValueCreated && UnpatchTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void ClearData_Click(object sender, EventArgs e)
		{
			if (PatchTools.IsValueCreated && PatchTools.Value.IsRunning ||
			    UnpatchTools.IsValueCreated && UnpatchTools.Value.IsRunning)
				return;
			MessageBox.Show(this, Resources.GetText(Resource.String.warning),
				Resources.GetText(Resource.String.clear_data_warning), Resources.GetText(Resource.String.dialog_ok),
				Resources.GetText(Resource.String.dialog_cancel), ClearData, null);
		}

		private void ClearData()
		{
			Preferences.Clear();
			Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
			Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);

			Utils.ClearOkkeiFiles();

			_savedataCheckBox.Checked = true;
			_patchButton.Enabled = true;
			_unpatchButton.Enabled = false;
			_statusText.Text = Resources.GetText(Resource.String.data_cleared);
		}

		private void OnErrorOccurred_ManifestTools(object sender, EventArgs e)
		{
			if ((!PatchTools.IsValueCreated || !PatchTools.Value.IsRunning) &&
			    (!UnpatchTools.IsValueCreated || !UnpatchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    ManifestTools.IsValueCreated && ManifestTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void InfoButton_Click(object sender, EventArgs eventArgs)
		{
			var view = (View) sender;
			Snackbar.Make(view,
					Java.Lang.String.Format(Resources.GetString(Resource.String.fab_version), AppInfo.VersionString),
					BaseTransientBottomBar.LengthLong)
				.SetAction("Action", (View.IOnClickListener) null).Show();
		}
	}
}