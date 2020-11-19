using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using Xamarin.Essentials;
using Android.Content;
using static OkkeiPatcher.GlobalData;
using File = Java.IO.File;
using String = Java.Lang.String;

namespace OkkeiPatcher
{
	[Activity(Label = "@string/app_name", Theme = "@style/Theme.MaterialComponents", MainLauncher = true,
		LaunchMode = LaunchMode.SingleTop)]
	public class MainActivity : AppCompatActivity
	{
		private CancellationTokenSource _tokenSource = new CancellationTokenSource();

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			if (requestCode == (int) RequestCodes.UnknownAppSourceCode && Build.VERSION.SdkInt >= BuildVersionCodes.O)
				if (!PackageManager.CanRequestPackageInstalls())
					MainThread.BeginInvokeOnMainThread(() =>
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.error),
							Resources.GetText(Resource.String.no_install_permission), MessageBox.Code.Exit);
					});

			if (requestCode == (int) RequestCodes.UninstallCode)
				Utils.OnUninstallResult(this, _tokenSource.Token);

			if (requestCode == (int) RequestCodes.InstallCode)
				Utils.OnInstallResult();
		}

		protected override void OnNewIntent(Intent intent)
		{
			var extras = intent.Extras;
			if (PACKAGE_INSTALLED_ACTION.Equals(intent.Action))
			{
				var info = FindViewById<TextView>(Resource.Id.Status);
				var checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);

				var status = extras?.GetInt(PackageInstaller.ExtraStatus);
				//var message = extras.GetString(PackageInstaller.ExtraStatusMessage);

				switch (status)
				{
					case (int) PackageInstallStatus.PendingUserAction:
						// Ask user to confirm the installation
						var confirmIntent = (Intent) extras.Get(Intent.ExtraIntent);
						StartActivityForResult(confirmIntent, (int) RequestCodes.InstallCode);
						break;
					case (int) PackageInstallStatus.Success:
						if (PatchTasks.Instance.IsRunning)
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = Resources.GetText(Resource.String.patch_success);
							});

							var signedApk = new File(FilePaths[Files.SignedApk]);
							if (signedApk.Exists()) signedApk.Delete();
							signedApk.Dispose();

							Task.Run(
								() => PatchTasks.Instance.FinishPatch(checkBoxSavedata.Checked, _tokenSource.Token));
						}
						else if (UnpatchTasks.Instance.IsRunning)
						{
							Task.Run(() =>
								UnpatchTasks.Instance.RestoreFiles(checkBoxSavedata.Checked, _tokenSource.Token));
						}

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
			var fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
			fab.Click += FabOnClick;

			var patch = FindViewById<Button>(Resource.Id.Patch);
			patch.Click += Patch_Click;

			var unpatch = FindViewById<Button>(Resource.Id.Unpatch);
			unpatch.Click += Unpatch_Click;

			var clear = FindViewById<Button>(Resource.Id.Clear);
			clear.Click += Clear_Click;

			var checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
			checkBoxSavedata.CheckedChange += CheckBox_CheckedChange;


			// Set apk_is_patched = false pref on first start
			if (!Preferences.ContainsKey(Prefkey.apk_is_patched.ToString()))
				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);


			// Restore previous state of checkbox or set pref on first start
			if (!Preferences.ContainsKey(Prefkey.backup_restore_savedata.ToString()))
				Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);
			else
				checkBoxSavedata.Checked = Preferences.Get(Prefkey.backup_restore_savedata.ToString(), true);


			// Request read/write external storage permissions on first start
			if (CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
			{
				string[] extStoragePermissions =
					{Manifest.Permission.WriteExternalStorage, Manifest.Permission.ReadExternalStorage};
				RequestPermissions(extStoragePermissions, 0);
			}

			// Create OkkeiPatcher directory if doesn't exist
			else if (!Directory.Exists(OkkeiFilesPath))
			{
				Directory.CreateDirectory(OkkeiFilesPath);
			}


			// Request permission to install packages on first start
			if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
				if (!PackageManager.CanRequestPackageInstalls())
					MainThread.BeginInvokeOnMainThread(() =>
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.attention),
							Resources.GetText(Resource.String.unknown_sources_notice),
							MessageBox.Code.UnknownAppSourceNotice);
					});
		}

		private void OnPropertyChanged_Patch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(PatchTasks.Instance.IsRunning))
			{
				var button = FindViewById<Button>(Resource.Id.Patch);
				string buttonText;

				if (!PatchTasks.Instance.IsRunning)
				{
					buttonText = Resources.GetText(Resource.String.patch);

					PatchTasks.Instance.StatusChanged -= OnStatusChanged;
					PatchTasks.Instance.ProgressChanged -= OnProgressChanged;
					PatchTasks.Instance.PropertyChanged -= OnPropertyChanged_Patch;
					PatchTasks.Instance.ErrorCanceled -= Patch_Click;

					_tokenSource = new CancellationTokenSource();
				}
				else
				{
					buttonText = Resources.GetText(Resource.String.abort);
				}

				MainThread.BeginInvokeOnMainThread(() => { button.Text = buttonText; });
			}
		}

		private void OnPropertyChanged_Unpatch(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(UnpatchTasks.Instance.IsRunning))
			{
				var button = FindViewById<Button>(Resource.Id.Unpatch);
				string buttonText;

				if (!UnpatchTasks.Instance.IsRunning)
				{
					buttonText = Resources.GetText(Resource.String.unpatch);

					UnpatchTasks.Instance.StatusChanged -= OnStatusChanged;
					UnpatchTasks.Instance.ProgressChanged -= OnProgressChanged;
					UnpatchTasks.Instance.PropertyChanged -= OnPropertyChanged_Unpatch;
					UnpatchTasks.Instance.ErrorCanceled -= Unpatch_Click;

					_tokenSource = new CancellationTokenSource();
				}
				else
				{
					buttonText = Resources.GetText(Resource.String.abort);
				}

				MainThread.BeginInvokeOnMainThread(() => { button.Text = buttonText; });
			}
		}

		private void OnStatusChanged(object sender, StatusChangedEventArgs e)
		{
			var info = e.Info;
			var data = e.MessageData;
			if (info != null)
				MainThread.BeginInvokeOnMainThread(() => { FindViewById<TextView>(Resource.Id.Status).Text = info; });
			if (!data.Equals(MessageBox.Data.Empty))
				MainThread.BeginInvokeOnMainThread(() => { MessageBox.Show(this, data); });
		}

		private void OnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			var progress = e.Progress;
			var max = e.Max;
			var progressBar = FindViewById<ProgressBar>(Resource.Id.progressBar);
			if (progressBar.Max != max) MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = max; });
			MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = progress; });
		}

		private void CheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
		{
			var isChecked = e.IsChecked;
			if (sender == FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata))
				Preferences.Set(Prefkey.backup_restore_savedata.ToString(), isChecked);
		}

		private void Patch_Click(object sender, EventArgs e)
		{
			if (!UnpatchTasks.Instance.IsRunning && !_tokenSource.IsCancellationRequested)
			{
				if (!PatchTasks.Instance.IsRunning)
				{
					PatchTasks.Instance.StatusChanged += OnStatusChanged;
					PatchTasks.Instance.ProgressChanged += OnProgressChanged;
					PatchTasks.Instance.PropertyChanged += OnPropertyChanged_Patch;
					PatchTasks.Instance.ErrorCanceled += Patch_Click;

					var checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
					Task.Run(() => PatchTasks.Instance.PatchTask(this, checkBoxSavedata.Checked,
						_tokenSource.Token));
				}
				else
				{
					_tokenSource.Cancel();
				}
			}
		}

		private void Unpatch_Click(object sender, EventArgs e)
		{
			if (!PatchTasks.Instance.IsRunning && !_tokenSource.IsCancellationRequested)
			{
				if (!UnpatchTasks.Instance.IsRunning)
				{
					UnpatchTasks.Instance.StatusChanged += OnStatusChanged;
					UnpatchTasks.Instance.ProgressChanged += OnProgressChanged;
					UnpatchTasks.Instance.PropertyChanged += OnPropertyChanged_Unpatch;
					UnpatchTasks.Instance.ErrorCanceled += Unpatch_Click;

					var checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
					Task.Run(() => UnpatchTasks.Instance.UnpatchTask(this, checkBoxSavedata.Checked,
						_tokenSource.Token));
				}
				else
				{
					_tokenSource.Cancel();
				}
			}
		}

		private void Clear_Click(object sender, EventArgs e)
		{
			if (!PatchTasks.Instance.IsRunning && !UnpatchTasks.Instance.IsRunning)
			{
				var info = FindViewById<TextView>(Resource.Id.Status);

				var apk = new File(FilePaths[Files.BackupApk]);
				if (apk.Exists()) apk.Delete();

				var obb = new File(FilePaths[Files.BackupObb]);
				if (obb.Exists()) obb.Delete();

				var savedata = new File(FilePaths[Files.BackupSavedata]);
				if (savedata.Exists()) savedata.Delete();

				info.Text = Resources.GetText(Resource.String.backup_cleared);

				apk.Dispose();
				obb.Dispose();
				savedata.Dispose();
			}
		}

		private void FabOnClick(object sender, EventArgs eventArgs)
		{
			var view = (View) sender;
			Snackbar.Make(view,
					String.Format(Resources.GetString(Resource.String.fab_version), AppInfo.VersionString),
					Snackbar.LengthLong)
				.SetAction("Action", (View.IOnClickListener) null).Show();
		}

		public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
			[GeneratedEnum] Permission[] grantResults)
		{
			Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

			base.OnRequestPermissionsResult(requestCode, permissions, grantResults);


			// Request read/write external storage permissions on first start
			if (requestCode == 0)
			{
				if (CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted)
				{
					string[] extStoragePermissions =
						{Manifest.Permission.WriteExternalStorage, Manifest.Permission.ReadExternalStorage};
					RequestPermissions(extStoragePermissions, 0);
				}

				// Create OkkeiPatcher directory if doesn't exist
				else if (!Directory.Exists(OkkeiFilesPath))
				{
					Directory.CreateDirectory(OkkeiFilesPath);
				}
			}
		}
	}
}