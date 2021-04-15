using System;
using System.Collections.Concurrent;
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
	[Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true,
		LaunchMode = LaunchMode.SingleTop)]
	public class MainActivity : AppCompatActivity
	{
		private static readonly ConcurrentDictionary<int, View> ViewCache = new ConcurrentDictionary<int, View>();

		private static readonly Lazy<Utils> UtilsInstance = new Lazy<Utils>(() => new Utils());

		private static readonly Lazy<PatchTools> PatchTools =
			new Lazy<PatchTools>(() => new PatchTools(UtilsInstance.Value));

		private static readonly Lazy<UnpatchTools> UnpatchTools =
			new Lazy<UnpatchTools>(() => new UnpatchTools(UtilsInstance.Value));

		private static readonly Lazy<ManifestTools> ManifestTools =
			new Lazy<ManifestTools>(() => new ManifestTools(UtilsInstance.Value));

		private bool _backPressed;

		private CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();
		private ToolsBase _currentToolsObject;
		private int _lastBackPressedTimestamp;
		private bool _patchToolsEventsSubscribed;
		private bool _unpatchToolsEventsSubscribed;

#nullable enable
		private T? FindCachedViewById<T>(int id) where T : View
		{
			if (!ViewCache.TryGetValue(id, out var view))
			{
				view = FindViewById<T>(id);
				if (view != null) ViewCache.TryAdd(id, view);
			}

			return (T?) view;
		}
#nullable disable

		private ProcessState CreateProcessState()
		{
			var processSavedata = FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox).Checked;
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

		private void ExecuteManifestTasks()
		{
			MessageBox.Show(this, Resources.GetText(Resource.String.attention),
				Resources.GetText(Resource.String.manifest_prompt), Resources.GetText(Resource.String.dialog_ok),
				() => Task.Run(async () =>
				{
					_currentToolsObject = ManifestTools.Value;
					ManifestTools.Value.StatusChanged += OnStatusChanged;
					ManifestTools.Value.ProgressChanged += OnProgressChanged;
					ManifestTools.Value.MessageGenerated += OnMessageGenerated;
					ManifestTools.Value.ErrorOccurred += OnErrorOccurred_ManifestTasks;
					ManifestTools.Value.PropertyChanged += OnPropertyChanged_ManifestTasks;

					if (!await ManifestTools.Value.RetrieveManifest(_cancelTokenSource.Token)) return;

					if (ManifestTools.Value.IsPatchUpdateAvailable)
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = true;
							MessageBox.Show(this, Resources.GetText(Resource.String.update_header),
								Java.Lang.String.Format(Resources.GetText(Resource.String.update_patch_available),
									ManifestTools.Value.PatchSizeInMB.ToString()),
								Resources.GetText(Resource.String.dialog_ok), UpdateApp);
						});
						return;
					}

					UpdateApp();
				}));
		}

		private void UpdateApp()
		{
			if (!ManifestTools.Value.IsAppUpdateAvailable) return;
			MainThread.BeginInvokeOnMainThread(() =>
				MessageBox.Show(this, Resources.GetText(Resource.String.update_header),
					Java.Lang.String.Format(Resources.GetText(Resource.String.update_app_available),
						AppInfo.VersionString,
						ManifestTools.Value.AppUpdateSizeInMB.ToString(CultureInfo.CurrentCulture),
						ManifestTools.Value.Manifest.OkkeiPatcher.Changelog),
					Resources.GetText(Resource.String.dialog_update),
					Resources.GetText(Resource.String.dialog_cancel),
					() => ManifestTools.Value.UpdateApp(this, _cancelTokenSource.Token), null));
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

					ExecuteManifestTasks();
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
					if (RequestInstallPackagesPermission()) ExecuteManifestTasks();
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


			// Set portrait orientation
			RequestedOrientation = ScreenOrientation.Portrait;


			// Don't turn screen off
			Window?.AddFlags(WindowManagerFlags.KeepScreenOn);


			// Subscribe to events
			var fab = FindCachedViewById<FloatingActionButton>(Resource.Id.fab);
			fab.Click += FabOnClick;

			var patchButton = FindCachedViewById<Button>(Resource.Id.patchButton);
			patchButton.Click += Patch_Click;

			var unpatchButton = FindCachedViewById<Button>(Resource.Id.unpatchButton);
			unpatchButton.Click += Unpatch_Click;

			var clearDataButton = FindCachedViewById<Button>(Resource.Id.clearDataButton);
			clearDataButton.Click += ClearData_Click;

			var savedataCheckbox = FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox);
			savedataCheckbox.CheckedChange += CheckBox_CheckedChange;


			// Set apk_is_patched = false pref on first start
			if (!Preferences.ContainsKey(Prefkey.apk_is_patched.ToString()))
				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);


			// Set buttons state depending on patch status and backup existence
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
			{
				patchButton.Enabled = false;
				unpatchButton.Enabled = true;
				if (Build.VERSION.SdkInt < BuildVersionCodes.M ||
				    CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted)
					unpatchButton.Enabled = Utils.IsBackupAvailable();
			}
			else
			{
				patchButton.Enabled = true;
				unpatchButton.Enabled = false;
			}


			// Restore previous state of checkbox or set pref on first start
			if (!Preferences.ContainsKey(Prefkey.backup_restore_savedata.ToString()))
				Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);
			else
				savedataCheckbox.Checked = Preferences.Get(Prefkey.backup_restore_savedata.ToString(), true);


			// Request read/write external storage permissions
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
			if (RequestInstallPackagesPermission()) ExecuteManifestTasks();
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

			FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable();

			if (RequestInstallPackagesPermission()) ExecuteManifestTasks();
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

		private void OnPropertyChanged_Patch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(PatchTools.Value.IsRunning)) return;
			MainThread.BeginInvokeOnMainThread(() =>
			{
				var patchButton = FindCachedViewById<Button>(Resource.Id.patchButton);

				if (!PatchTools.Value.IsRunning)
				{
					_cancelTokenSource.Dispose();
					_cancelTokenSource = new CancellationTokenSource();

					patchButton.Text = Resources.GetText(Resource.String.patch);
					FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = true;
					if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
					{
						patchButton.Enabled = false;
						FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable();
					}

					return;
				}

				FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = false;
				FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = false;
				patchButton.Text = Resources.GetText(Resource.String.abort);
			});
		}

		private void OnPropertyChanged_Unpatch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(UnpatchTools.Value.IsRunning)) return;
			MainThread.BeginInvokeOnMainThread(() =>
			{
				var unpatchButton = FindCachedViewById<Button>(Resource.Id.unpatchButton);

				if (!UnpatchTools.Value.IsRunning)
				{
					_cancelTokenSource.Dispose();
					_cancelTokenSource = new CancellationTokenSource();

					unpatchButton.Text = Resources.GetText(Resource.String.unpatch);
					FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = true;
					if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
					{
						unpatchButton.Enabled = false;
						FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = true;
						return;
					}

					FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable();
					return;
				}

				FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = false;
				FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = false;
				unpatchButton.Text = Resources.GetText(Resource.String.abort);
			});
		}

		private void OnPropertyChanged_ManifestTasks(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(ManifestTools.Value.IsRunning)) return;
			MainThread.BeginInvokeOnMainThread(() =>
			{
				var patchButton = FindCachedViewById<Button>(Resource.Id.patchButton);

				if (!ManifestTools.Value.IsRunning)
				{
					_cancelTokenSource.Dispose();
					_cancelTokenSource = new CancellationTokenSource();

					patchButton.Enabled = !Preferences.Get(Prefkey.apk_is_patched.ToString(), false) ||
					                      ManifestTools.Value.IsPatchUpdateAvailable;
					patchButton.Text = Resources.GetText(Resource.String.patch);
					FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable();
					FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = true;
					return;
				}

				patchButton.Enabled = true;
				FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = false;
				FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = false;
				patchButton.Text = Resources.GetText(Resource.String.abort);
			});
		}

		private void OnStatusChanged(object sender, string e)
		{
			MainThread.BeginInvokeOnMainThread(() => FindCachedViewById<TextView>(Resource.Id.statusText).Text = e);
		}

		private void OnMessageGenerated(object sender, MessageBox.Data e)
		{
			MainThread.BeginInvokeOnMainThread(() => MessageBox.Show(this, e));
		}

		private void OnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				var progress = e.Progress;
				var max = e.Max;
				var isIndeterminate = e.IsIndeterminate;
				var progressBar = FindCachedViewById<ProgressBar>(Resource.Id.progressBar);
				if (progressBar == null) return;
				if (progressBar.Indeterminate != isIndeterminate) progressBar.Indeterminate = isIndeterminate;
				if (progressBar.Max != max) progressBar.Max = max;
				progressBar.Progress = progress;
			});
		}

		private void CheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
		{
			var isChecked = e.IsChecked;
			var prefkey = Prefkey.backup_restore_savedata.ToString();
			if (sender == FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox) &&
			    Preferences.Get(prefkey, true) != isChecked)
				Preferences.Set(prefkey, isChecked);
		}

		private void Patch_Click(object sender, EventArgs e)
		{
			if (UnpatchTools.IsValueCreated && UnpatchTools.Value.IsRunning ||
			    _cancelTokenSource.IsCancellationRequested)
				return;
			if ((!PatchTools.IsValueCreated || !PatchTools.Value.IsRunning) &&
			    (!ManifestTools.IsValueCreated || !ManifestTools.Value.IsRunning))
			{
				MessageBox.Show(this, Resources.GetText(Resource.String.warning),
					Resources.GetText(Resource.String.long_process_warning),
					Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
					() =>
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.warning),
							Java.Lang.String.Format(Resources.GetText(Resource.String.download_size_warning),
								ManifestTools.Value.PatchSizeInMB),
							Resources.GetText(Resource.String.dialog_ok),
							Resources.GetText(Resource.String.dialog_cancel),
							() =>
							{
								_currentToolsObject = PatchTools.Value;
								if (!_patchToolsEventsSubscribed)
								{
									PatchTools.Value.StatusChanged += OnStatusChanged;
									PatchTools.Value.ProgressChanged += OnProgressChanged;
									PatchTools.Value.MessageGenerated += OnMessageGenerated;
									PatchTools.Value.PropertyChanged += OnPropertyChanged_Patch;
									PatchTools.Value.ErrorOccurred += OnErrorOccurred_Patch;
									_patchToolsEventsSubscribed = true;
								}

								PatchTools.Value.Patch(this, CreateProcessState(), ManifestTools.Value.Manifest,
									_cancelTokenSource.Token);
							}, null);
					}, null);
				return;
			}

			MessageBox.Show(this, Resources.GetText(Resource.String.warning),
				Resources.GetText(Resource.String.abort_warning),
				Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
				() =>
				{
					if (PatchTools.IsValueCreated && PatchTools.Value.IsRunning ||
					    ManifestTools.IsValueCreated && ManifestTools.Value.IsRunning)
						_cancelTokenSource.Cancel();
				}, null);
		}

		private void OnErrorOccurred_Patch(object sender, EventArgs e)
		{
			if ((!UnpatchTools.IsValueCreated || !UnpatchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    PatchTools.IsValueCreated && PatchTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void Unpatch_Click(object sender, EventArgs e)
		{
			if (PatchTools.IsValueCreated && PatchTools.Value.IsRunning || _cancelTokenSource.IsCancellationRequested)
				return;
			if (!UnpatchTools.IsValueCreated || !UnpatchTools.Value.IsRunning)
			{
				MessageBox.Show(this, Resources.GetText(Resource.String.warning),
					Resources.GetText(Resource.String.long_process_warning),
					Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
					() =>
					{
						_currentToolsObject = UnpatchTools.Value;
						if (!_unpatchToolsEventsSubscribed)
						{
							UnpatchTools.Value.StatusChanged += OnStatusChanged;
							UnpatchTools.Value.ProgressChanged += OnProgressChanged;
							UnpatchTools.Value.MessageGenerated += OnMessageGenerated;
							UnpatchTools.Value.PropertyChanged += OnPropertyChanged_Unpatch;
							UnpatchTools.Value.ErrorOccurred += OnErrorOccurred_Unpatch;
							_unpatchToolsEventsSubscribed = true;
						}

						UnpatchTools.Value.Unpatch(this, CreateProcessState(), _cancelTokenSource.Token);
					}, null);
				return;
			}

			MessageBox.Show(this, Resources.GetText(Resource.String.warning),
				Resources.GetText(Resource.String.abort_warning),
				Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
				() =>
				{
					if (UnpatchTools.IsValueCreated && UnpatchTools.Value.IsRunning)
						_cancelTokenSource.Cancel();
				}, null);
		}

		private void OnErrorOccurred_Unpatch(object sender, EventArgs e)
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
				Resources.GetText(Resource.String.clear_data_warning),
				Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
				() =>
				{
					Preferences.Clear();
					Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
					Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);

					Utils.ClearOkkeiFiles();

					FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox).Checked = true;
					FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = true;
					FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = false;
					FindCachedViewById<TextView>(Resource.Id.statusText).Text =
						Resources.GetText(Resource.String.data_cleared);
				}, null);
		}

		private void OnErrorOccurred_ManifestTasks(object sender, EventArgs e)
		{
			if ((!PatchTools.IsValueCreated || !PatchTools.Value.IsRunning) &&
			    (!UnpatchTools.IsValueCreated || !UnpatchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    ManifestTools.IsValueCreated && ManifestTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void FabOnClick(object sender, EventArgs eventArgs)
		{
			var view = (View) sender;
			Snackbar.Make(view,
					Java.Lang.String.Format(Resources.GetString(Resource.String.fab_version), AppInfo.VersionString),
					BaseTransientBottomBar.LengthLong)
				.SetAction("Action", (View.IOnClickListener) null).Show();
		}
	}
}