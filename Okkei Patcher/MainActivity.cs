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
		private static readonly Lazy<ConcurrentDictionary<int, View>> ViewCache =
			new Lazy<ConcurrentDictionary<int, View>>(() => new ConcurrentDictionary<int, View>());

		private CancellationTokenSource _cts = new CancellationTokenSource();

#nullable enable
		private T? FindCachedViewById<T>(int id) where T : View
		{
			if (!ViewCache.Value.TryGetValue(id, out var view))
			{
				view = FindViewById<T>(id);
				if (view != null) ViewCache.Value.TryAdd(id, view);
			}

			return (T?) view;
		}
#nullable disable

		private bool RequestInstallPackagesPermission()
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			{
				if (!PackageManager.CanRequestPackageInstalls())
				{
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

				return true;
			}

			return true;
		}

		private void ExecuteManifestTasks()
		{
			MessageBox.Show(this, Resources.GetText(Resource.String.attention),
				Resources.GetText(Resource.String.manifest_prompt), Resources.GetText(Resource.String.dialog_ok),
				async () => await Task.Run(async () =>
				{
					if (!await ManifestTasks.Instance.GetManifest(_cts.Token)) return;

					var appUpdateInstallFlag = false;
					if (ManifestTasks.Instance.CheckAppUpdate())
						MessageBox.Show(this, Resources.GetText(Resource.String.update_header),
							Java.Lang.String.Format(Resources.GetText(Resource.String.update_app_available),
								ManifestTasks.Instance.GetAppUpdateSizeInMB().ToString(CultureInfo.CurrentCulture),
								GlobalManifest.OkkeiPatcher.Changelog),
							Resources.GetText(Resource.String.dialog_update),
							Resources.GetText(Resource.String.dialog_cancel),
							async () =>
							{
								await ManifestTasks.Instance.InstallAppUpdate(this, _cts.Token);
								appUpdateInstallFlag = true;
							}, null);

					if (!appUpdateInstallFlag && (ManifestTasks.Instance.CheckScriptsUpdate() ||
					                              ManifestTasks.Instance.CheckObbUpdate()))
					{
						FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = true;
						MessageBox.Show(this, Resources.GetText(Resource.String.update_header),
							Java.Lang.String.Format(Resources.GetText(Resource.String.update_patch_available),
								ManifestTasks.Instance.GetPatchUpdateSizeInMB().ToString()),
							Resources.GetText(Resource.String.dialog_ok), null);
					}
				}));
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			switch (requestCode)
			{
				case (int) RequestCodes.UnknownAppSourceSettingsCode:
					if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
					{
						if (!PackageManager.CanRequestPackageInstalls())
							MessageBox.Show(this, Resources.GetText(Resource.String.error),
								Resources.GetText(Resource.String.no_install_permission),
								Resources.GetText(Resource.String.dialog_exit),
								() => { System.Environment.Exit(0); });
						else ExecuteManifestTasks();
					}

					break;
				case (int) RequestCodes.StoragePermissionSettingsCode:
					if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
					{
						if (CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
						{
							MessageBox.Show(this, Resources.GetText(Resource.String.error),
								Resources.GetText(Resource.String.no_storage_permission),
								Resources.GetText(Resource.String.dialog_exit),
								() => { System.Environment.Exit(0); });
						}
						else
						{
							Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
							Directory.CreateDirectory(OkkeiFilesPath);
							if (RequestInstallPackagesPermission()) ExecuteManifestTasks();
						}
					}

					break;
				case (int) RequestCodes.UninstallCode:
					Utils.OnUninstallResult(this, _cts.Token);
					break;
				case (int) RequestCodes.KitKatInstallCode:
					if (resultCode == Result.Ok)
					{
						var savedataCheckbox = FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox);
						Utils.OnInstallSuccess(savedataCheckbox.Checked, _cts.Token);
					}
					else
					{
						Utils.NotifyInstallFailed();
					}

					break;
			}
		}

		protected override void OnNewIntent(Intent intent)
		{
			if (ActionPackageInstalled.Equals(intent.Action))
			{
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
						var savedataCheckbox = FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox);
						Utils.OnInstallSuccess(savedataCheckbox.Checked, _cts.Token);
						break;
				}
			}
		}

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			Platform.Init(this, savedInstanceState);
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

			ManifestTasks.Instance.StatusChanged += OnStatusChanged;
			ManifestTasks.Instance.ProgressChanged += OnProgressChanged;
			ManifestTasks.Instance.MessageGenerated += OnMessageGenerated;
			ManifestTasks.Instance.ErrorOccurred += OnErrorOccurred_ManifestTasks;
			ManifestTasks.Instance.PropertyChanged += OnPropertyChanged_ManifestTasks;


			// Set apk_is_patched = false pref on first start
			if (!Preferences.ContainsKey(Prefkey.apk_is_patched.ToString()))
				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);


			// Set buttons state depending on patch status and backup existence
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
			{
				patchButton.Enabled = false;
				unpatchButton.Enabled = true;
				if (Build.VERSION.SdkInt >= BuildVersionCodes.M &&
				    CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted ||
				    Build.VERSION.SdkInt < BuildVersionCodes.M)
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
				}
				else
				{
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
						() => { System.Environment.Exit(0); });
				}
			}
			else
			{
				Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
				Directory.CreateDirectory(OkkeiFilesPath);
				if (RequestInstallPackagesPermission()) ExecuteManifestTasks();
			}
		}

		public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
			[GeneratedEnum] Permission[] grantResults)
		{
			Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

			base.OnRequestPermissionsResult(requestCode, permissions, grantResults);


			// Request read/write external storage permissions on first start
			if (requestCode == (int) RequestCodes.StoragePermissionRequestCode &&
			    Build.VERSION.SdkInt >= BuildVersionCodes.M)
			{
				if (grantResults[0] != Permission.Granted)
				{
					if (ShouldShowRequestPermissionRationale(permissions[0]))
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.error),
							Resources.GetText(Resource.String.no_storage_permission_rationale),
							Resources.GetText(Resource.String.dialog_ok),
							Resources.GetText(Resource.String.dialog_exit),
							() => { RequestPermissions(permissions, (int) RequestCodes.StoragePermissionRequestCode); },
							() => { System.Environment.Exit(0); });
					}
					else
					{
						Preferences.Set(Prefkey.extstorage_permission_denied.ToString(), true);

						MessageBox.Show(this, Resources.GetText(Resource.String.error),
							Resources.GetText(Resource.String.no_storage_permission),
							Resources.GetText(Resource.String.dialog_exit),
							() => { System.Environment.Exit(0); });
					}
				}
				else
				{
					Preferences.Remove(Prefkey.extstorage_permission_denied.ToString());
					Directory.CreateDirectory(OkkeiFilesPath);

					FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable();

					if (RequestInstallPackagesPermission()) ExecuteManifestTasks();
				}
			}
		}

		private void OnPropertyChanged_Patch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(PatchTasks.Instance.IsRunning))
			{
				var patchButton = FindCachedViewById<Button>(Resource.Id.patchButton);

				if (!PatchTasks.Instance.IsRunning)
				{
					PatchTasks.Instance.StatusChanged -= OnStatusChanged;
					PatchTasks.Instance.ProgressChanged -= OnProgressChanged;
					PatchTasks.Instance.MessageGenerated -= OnMessageGenerated;
					PatchTasks.Instance.PropertyChanged -= OnPropertyChanged_Patch;
					PatchTasks.Instance.ErrorOccurred -= OnErrorOccurred_Patch;

					_cts.Dispose();
					_cts = new CancellationTokenSource();

					MainThread.BeginInvokeOnMainThread(() =>
					{
						patchButton.Text = Resources.GetText(Resource.String.patch);
						FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = true;
					});

					if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
						MainThread.BeginInvokeOnMainThread(() =>
						{
							patchButton.Enabled = false;
							FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable();
						});
				}
				else
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = false;
						FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = false;
						patchButton.Text = Resources.GetText(Resource.String.abort);
					});
				}
			}
		}

		private void OnPropertyChanged_Unpatch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(UnpatchTasks.Instance.IsRunning))
			{
				var unpatchButton = FindCachedViewById<Button>(Resource.Id.unpatchButton);

				if (!UnpatchTasks.Instance.IsRunning)
				{
					UnpatchTasks.Instance.StatusChanged -= OnStatusChanged;
					UnpatchTasks.Instance.ProgressChanged -= OnProgressChanged;
					UnpatchTasks.Instance.MessageGenerated -= OnMessageGenerated;
					UnpatchTasks.Instance.PropertyChanged -= OnPropertyChanged_Unpatch;
					UnpatchTasks.Instance.ErrorOccurred -= OnErrorOccurred_Unpatch;

					_cts.Dispose();
					_cts = new CancellationTokenSource();

					MainThread.BeginInvokeOnMainThread(() =>
					{
						unpatchButton.Text = Resources.GetText(Resource.String.unpatch);
						FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = true;
					});

					if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
						MainThread.BeginInvokeOnMainThread(() =>
						{
							unpatchButton.Enabled = false;
							FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = true;
						});
					else
						MainThread.BeginInvokeOnMainThread(() =>
							FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable());
				}
				else
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = false;
						FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = false;
						unpatchButton.Text = Resources.GetText(Resource.String.abort);
					});
				}
			}
		}

		private void OnPropertyChanged_ManifestTasks(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(ManifestTasks.Instance.IsRunning))
			{
				var patchButton = FindCachedViewById<Button>(Resource.Id.patchButton);

				if (!ManifestTasks.Instance.IsRunning)
				{
					_cts.Dispose();
					_cts = new CancellationTokenSource();

					MainThread.BeginInvokeOnMainThread(() =>
					{
						patchButton.Enabled = !Preferences.Get(Prefkey.apk_is_patched.ToString(), false);
						patchButton.Text = Resources.GetText(Resource.String.patch);
						FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = Utils.IsBackupAvailable();
						FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = true;
					});
				}
				else
				{
					MainThread.BeginInvokeOnMainThread(() =>
					{
						patchButton.Enabled = true;
						FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = false;
						FindCachedViewById<Button>(Resource.Id.clearDataButton).Enabled = false;
						patchButton.Text = Resources.GetText(Resource.String.abort);
					});
				}
			}
		}

		private void OnStatusChanged(object sender, string e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				FindCachedViewById<TextView>(Resource.Id.statusText).Text = e;
			});
		}

		private void OnMessageGenerated(object sender, MessageBox.Data e)
		{
			MainThread.BeginInvokeOnMainThread(() => { MessageBox.Show(this, e); });
		}

		private void OnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			var progress = e.Progress;
			var max = e.Max;
			var progressBar = FindCachedViewById<ProgressBar>(Resource.Id.progressBar);
			if (progressBar.Max != max) MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = max; });
			MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = progress; });
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
			if ((!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning) && !_cts.IsCancellationRequested)
			{
				if ((!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning) &&
				    (!ManifestTasks.IsInstantiated || !ManifestTasks.Instance.IsRunning))
					MessageBox.Show(this, Resources.GetText(Resource.String.warning),
						Resources.GetText(Resource.String.long_process_warning),
						Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
						() =>
						{
							MessageBox.Show(this, Resources.GetText(Resource.String.warning),
								Java.Lang.String.Format(Resources.GetText(Resource.String.download_size_warning),
									ManifestTasks.Instance.GetPatchSizeInMB()),
								Resources.GetText(Resource.String.dialog_ok),
								Resources.GetText(Resource.String.dialog_cancel),
								() =>
								{
									PatchTasks.Instance.StatusChanged += OnStatusChanged;
									PatchTasks.Instance.ProgressChanged += OnProgressChanged;
									PatchTasks.Instance.MessageGenerated += OnMessageGenerated;
									PatchTasks.Instance.PropertyChanged += OnPropertyChanged_Patch;
									PatchTasks.Instance.ErrorOccurred += OnErrorOccurred_Patch;

									var savedataCheckbox = FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox);
									Task.Run(() =>
										PatchTasks.Instance.PatchTask(this, savedataCheckbox.Checked, _cts.Token));
								}, null);
						}, null);
				else
					MessageBox.Show(this, Resources.GetText(Resource.String.warning),
						Resources.GetText(Resource.String.abort_warning),
						Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
						() => _cts.Cancel(), null);
			}
		}

		private void OnErrorOccurred_Patch(object sender, EventArgs e)
		{
			if ((!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning) && !_cts.IsCancellationRequested &&
			    PatchTasks.IsInstantiated && PatchTasks.Instance.IsRunning)
				_cts.Cancel();
		}

		private void Unpatch_Click(object sender, EventArgs e)
		{
			if ((!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning) && !_cts.IsCancellationRequested)
			{
				if (!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning)
					MessageBox.Show(this, Resources.GetText(Resource.String.warning),
						Resources.GetText(Resource.String.long_process_warning),
						Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
						() =>
						{
							UnpatchTasks.Instance.StatusChanged += OnStatusChanged;
							UnpatchTasks.Instance.ProgressChanged += OnProgressChanged;
							UnpatchTasks.Instance.MessageGenerated += OnMessageGenerated;
							UnpatchTasks.Instance.PropertyChanged += OnPropertyChanged_Unpatch;
							UnpatchTasks.Instance.ErrorOccurred += OnErrorOccurred_Unpatch;

							var savedataCheckbox = FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox);
							Task.Run(
								() => UnpatchTasks.Instance.UnpatchTask(this, savedataCheckbox.Checked, _cts.Token));
						}, null);
				else
					MessageBox.Show(this, Resources.GetText(Resource.String.warning),
						Resources.GetText(Resource.String.abort_warning),
						Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
						() => _cts.Cancel(), null);
			}
		}

		private void OnErrorOccurred_Unpatch(object sender, EventArgs e)
		{
			if ((!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning) && !_cts.IsCancellationRequested &&
			    UnpatchTasks.IsInstantiated && UnpatchTasks.Instance.IsRunning)
				_cts.Cancel();
		}

		private void ClearData_Click(object sender, EventArgs e)
		{
			if ((!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning) &&
			    (!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning))
				MessageBox.Show(this, Resources.GetText(Resource.String.warning),
					Resources.GetText(Resource.String.clear_data_warning),
					Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
					() =>
					{
						Preferences.Clear();
						Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
						Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);

						Utils.ClearOkkeiFolder();

						FindCachedViewById<CheckBox>(Resource.Id.savedataCheckbox).Checked = true;
						FindCachedViewById<Button>(Resource.Id.patchButton).Enabled = true;
						FindCachedViewById<Button>(Resource.Id.unpatchButton).Enabled = false;
						FindCachedViewById<TextView>(Resource.Id.statusText).Text =
							Resources.GetText(Resource.String.data_cleared);
					}, null);
		}

		private void OnErrorOccurred_ManifestTasks(object sender, EventArgs e)
		{
			if ((!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning) &&
			    (!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning) && !_cts.IsCancellationRequested &&
			    ManifestTasks.IsInstantiated && ManifestTasks.Instance.IsRunning)
				_cts.Cancel();
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