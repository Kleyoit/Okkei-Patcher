﻿using System;
using System.Collections.Concurrent;
using System.ComponentModel;
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

		private void RequestInstallPackagesPermission()
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.O && !PackageManager.CanRequestPackageInstalls())
				MessageBox.Show(this, Resources.GetText(Resource.String.attention),
					Resources.GetText(Resource.String.unknown_sources_notice),
					Resources.GetText(Resource.String.dialog_ok),
					() =>
					{
						var intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
							Android.Net.Uri.Parse("package:" + AppInfo.PackageName));
						StartActivityForResult(intent, (int) RequestCodes.UnknownAppSourceSettingsCode);
					});
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			switch (requestCode)
			{
				case (int) RequestCodes.UnknownAppSourceSettingsCode:
					if (Build.VERSION.SdkInt >= BuildVersionCodes.O && !PackageManager.CanRequestPackageInstalls())
						MessageBox.Show(this, Resources.GetText(Resource.String.error),
							Resources.GetText(Resource.String.no_install_permission),
							Resources.GetText(Resource.String.dialog_exit),
							() => { System.Environment.Exit(0); });
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
							RequestInstallPackagesPermission();
						}
					}

					break;
				case (int) RequestCodes.UninstallCode:
					Utils.OnUninstallResult(this, _cts.Token);
					break;
				case (int) RequestCodes.KitKatInstallCode:
					if (resultCode == Result.Ok)
					{
						var checkBoxSavedata = FindCachedViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
						Utils.OnInstallSuccess(checkBoxSavedata.Checked, _cts.Token);
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
						var checkBoxSavedata = FindCachedViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
						Utils.OnInstallSuccess(checkBoxSavedata.Checked, _cts.Token);
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

			var patch = FindCachedViewById<Button>(Resource.Id.Patch);
			patch.Click += Patch_Click;

			var unpatch = FindCachedViewById<Button>(Resource.Id.Unpatch);
			unpatch.Click += Unpatch_Click;

			var clear = FindCachedViewById<Button>(Resource.Id.Clear);
			clear.Click += Clear_Click;

			var checkBoxSavedata = FindCachedViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
			checkBoxSavedata.CheckedChange += CheckBox_CheckedChange;


			// Set apk_is_patched = false pref on first start
			if (!Preferences.ContainsKey(Prefkey.apk_is_patched.ToString()))
				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);


			// Set buttons state depending on patch status
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
			{
				patch.Enabled = false;
				unpatch.Enabled = true;
			}
			else
			{
				patch.Enabled = true;
				unpatch.Enabled = false;
			}


			// Set "Clear backup" button state depending on backup existence
			clear.Enabled = false;
			if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
			{
				if (CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted)
					clear.Enabled = Utils.IsBackupAvailable();
			}
			else clear.Enabled = Utils.IsBackupAvailable();


			// Restore previous state of checkbox or set pref on first start
			if (!Preferences.ContainsKey(Prefkey.backup_restore_savedata.ToString()))
				Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);
			else
				checkBoxSavedata.Checked = Preferences.Get(Prefkey.backup_restore_savedata.ToString(), true);


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
				RequestInstallPackagesPermission();
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

					FindCachedViewById<Button>(Resource.Id.Clear).Enabled = Utils.IsBackupAvailable();

					RequestInstallPackagesPermission();
				}
			}
		}

		private void OnPropertyChanged_Patch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(PatchTasks.Instance.IsRunning))
			{
				var button = FindCachedViewById<Button>(Resource.Id.Patch);

				if (!PatchTasks.Instance.IsRunning)
				{
					PatchTasks.Instance.StatusChanged -= OnStatusChanged;
					PatchTasks.Instance.ProgressChanged -= OnProgressChanged;
					PatchTasks.Instance.MessageGenerated -= OnMessageGenerated;
					PatchTasks.Instance.PropertyChanged -= OnPropertyChanged_Patch;
					PatchTasks.Instance.ErrorOccurred -= Patch_Click;

					_cts.Dispose();
					_cts = new CancellationTokenSource();

					MainThread.BeginInvokeOnMainThread(() => { button.Text = Resources.GetText(Resource.String.patch); });

					if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							button.Enabled = false;
							FindCachedViewById<Button>(Resource.Id.Unpatch).Enabled = true;
						});
					}

					MainThread.BeginInvokeOnMainThread(() =>
						FindCachedViewById<Button>(Resource.Id.Clear).Enabled = Utils.IsBackupAvailable());
				}
				else
				{
					MainThread.BeginInvokeOnMainThread(() => { button.Text = Resources.GetText(Resource.String.abort); });
				}
			}
		}

		private void OnPropertyChanged_Unpatch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(UnpatchTasks.Instance.IsRunning))
			{
				var button = FindCachedViewById<Button>(Resource.Id.Unpatch);

				if (!UnpatchTasks.Instance.IsRunning)
				{
					UnpatchTasks.Instance.StatusChanged -= OnStatusChanged;
					UnpatchTasks.Instance.ProgressChanged -= OnProgressChanged;
					UnpatchTasks.Instance.MessageGenerated -= OnMessageGenerated;
					UnpatchTasks.Instance.PropertyChanged -= OnPropertyChanged_Unpatch;
					UnpatchTasks.Instance.ErrorOccurred -= Unpatch_Click;

					_cts.Dispose();
					_cts = new CancellationTokenSource();

					MainThread.BeginInvokeOnMainThread(() => { button.Text = Resources.GetText(Resource.String.unpatch); });

					if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
					{
						MainThread.BeginInvokeOnMainThread(() =>
						{
							button.Enabled = false;
							FindCachedViewById<Button>(Resource.Id.Patch).Enabled = true;
						});
					}

					MainThread.BeginInvokeOnMainThread(() =>
						FindCachedViewById<Button>(Resource.Id.Clear).Enabled = Utils.IsBackupAvailable());
				}
				else
				{
					MainThread.BeginInvokeOnMainThread(() => { button.Text = Resources.GetText(Resource.String.abort); });
				}
			}
		}

		private void OnStatusChanged(object sender, string e)
		{
			MainThread.BeginInvokeOnMainThread(() => { FindCachedViewById<TextView>(Resource.Id.Status).Text = e; });
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
			if (sender == FindCachedViewById<CheckBox>(Resource.Id.CheckBoxSavedata) &&
			    Preferences.Get(prefkey, true) != isChecked)
				Preferences.Set(prefkey, isChecked);
		}

		private void Patch_Click(object sender, EventArgs e)
		{
			if ((!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning) && !_cts.IsCancellationRequested)
			{
				if (!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning)
				{
					MessageBox.Show(this, Resources.GetText(Resource.String.warning),
						Resources.GetText(Resource.String.long_process_warning),
						Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
						() =>
						{
							PatchTasks.Instance.StatusChanged += OnStatusChanged;
							PatchTasks.Instance.ProgressChanged += OnProgressChanged;
							PatchTasks.Instance.MessageGenerated += OnMessageGenerated;
							PatchTasks.Instance.PropertyChanged += OnPropertyChanged_Patch;
							PatchTasks.Instance.ErrorOccurred += Patch_Click;

							var checkBoxSavedata = FindCachedViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
							Task.Run(() => PatchTasks.Instance.PatchTask(this, checkBoxSavedata.Checked, _cts.Token));
						}, null);
				}
				else
				{
					_cts.Cancel();
				}
			}
		}

		private void Unpatch_Click(object sender, EventArgs e)
		{
			if ((!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning) && !_cts.IsCancellationRequested)
			{
				if (!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning)
				{
					MessageBox.Show(this, Resources.GetText(Resource.String.warning),
						Resources.GetText(Resource.String.long_process_warning),
						Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
						() =>
						{
							UnpatchTasks.Instance.StatusChanged += OnStatusChanged;
							UnpatchTasks.Instance.ProgressChanged += OnProgressChanged;
							UnpatchTasks.Instance.MessageGenerated += OnMessageGenerated;
							UnpatchTasks.Instance.PropertyChanged += OnPropertyChanged_Unpatch;
							UnpatchTasks.Instance.ErrorOccurred += Unpatch_Click;

							var checkBoxSavedata = FindCachedViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
							Task.Run(
								() => UnpatchTasks.Instance.UnpatchTask(this, checkBoxSavedata.Checked, _cts.Token));
						}, null);
				}
				else
				{
					_cts.Cancel();
				}
			}
		}

		private void Clear_Click(object sender, EventArgs e)
		{
			if ((!PatchTasks.IsInstantiated || !PatchTasks.Instance.IsRunning) &&
			    (!UnpatchTasks.IsInstantiated || !UnpatchTasks.Instance.IsRunning))
			{
				MessageBox.Show(this, Resources.GetText(Resource.String.warning),
					Resources.GetText(Resource.String.clear_backup_warning),
					Resources.GetText(Resource.String.dialog_ok), Resources.GetText(Resource.String.dialog_cancel),
					() =>
					{
						if (File.Exists(FilePaths[Files.BackupApk])) File.Delete(FilePaths[Files.BackupApk]);
						if (File.Exists(FilePaths[Files.BackupObb])) File.Delete(FilePaths[Files.BackupObb]);
						if (File.Exists(FilePaths[Files.BackupSavedata])) File.Delete(FilePaths[Files.BackupSavedata]);
						if (File.Exists(FilePaths[Files.SAVEDATA_BACKUP]))
							File.Delete(FilePaths[Files.SAVEDATA_BACKUP]);

						FindCachedViewById<TextView>(Resource.Id.Status).Text =
							Resources.GetText(Resource.String.backup_cleared);
						FindCachedViewById<Button>(Resource.Id.Patch).Enabled = false;
					}, null);
			}
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