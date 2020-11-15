using System;
using System.ComponentModel;
using System.IO;
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

namespace OkkeiPatcher
{
	[Activity(Label = "@string/app_name", Theme = "@style/Theme.MaterialComponents", MainLauncher = true,
		LaunchMode = LaunchMode.SingleTop)]
	public class MainActivity : AppCompatActivity
	{
		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			if (requestCode == (int) RequestCodes.UnknownAppSourceCode && Build.VERSION.SdkInt >= BuildVersionCodes.O)
			{
				if (!this.PackageManager.CanRequestPackageInstalls())
					MainThread.BeginInvokeOnMainThread(() =>
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.error),
							Resources.GetText(Resource.String.no_install_permission), MessageBox.Code.Exit);
					});
			}

			if (requestCode == (int) RequestCodes.UninstallCode)
				Utils.OnUninstallResult(this);

			if (requestCode == (int) RequestCodes.InstallCode)
				Utils.OnInstallResult();
		}

		protected override void OnNewIntent(Intent intent)
		{
			Bundle extras = intent.Extras;
			if (PACKAGE_INSTALLED_ACTION.Equals(intent.Action))
			{
				TextView info = FindViewById<TextView>(Resource.Id.Status);
				CheckBox checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);

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

							var signedApk = new Java.IO.File(FilePaths[Files.SignedApk]);
							if (signedApk.Exists()) signedApk.Delete();
							signedApk.Dispose();

							Task.Run(() => PatchTasks.Instance.FinishPatch(checkBoxSavedata.Checked));
						}
						else if (UnpatchTasks.Instance.IsRunning)
							Task.Run(() => UnpatchTasks.Instance.RestoreFiles(checkBoxSavedata.Checked));

						break;
				}
			}
		}

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			Xamarin.Essentials.Platform.Init(this, savedInstanceState);
			SetContentView(Resource.Layout.activity_main);


			// Set portrait orientation
			this.RequestedOrientation = ScreenOrientation.Portrait;


			// Don't turn screen off
			this.Window?.AddFlags(WindowManagerFlags.KeepScreenOn);


			// Subscribe to events
			FloatingActionButton fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
			fab.Click += FabOnClick;

			Button patch = FindViewById<Button>(Resource.Id.Patch);
			patch.Click += Patch_Click;

			Button unpatch = FindViewById<Button>(Resource.Id.Unpatch);
			unpatch.Click += Unpatch_Click;

			Button clear = FindViewById<Button>(Resource.Id.Clear);
			clear.Click += Clear_Click;

			CheckBox checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
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
			else if (!Directory.Exists(OkkeiFilesPath)) Directory.CreateDirectory(OkkeiFilesPath);


			// Request permission to install packages on first start
			if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
			{
				if (!this.PackageManager.CanRequestPackageInstalls())
					MainThread.BeginInvokeOnMainThread(() =>
					{
						MessageBox.Show(this, Resources.GetText(Resource.String.attention),
							Resources.GetText(Resource.String.unknown_sources_notice),
							MessageBox.Code.UnknownAppSourceNotice);
					});
			}
		}

		private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "IsRunning")
			{
				Button button;
				string buttonText;

				if (sender is PatchTasks)
				{
					button = FindViewById<Button>(Resource.Id.Patch);

					buttonText = PatchTasks.Instance.IsRunning
						? Resources.GetText(Resource.String.abort)
						: Resources.GetText(Resource.String.patch);
				}
				else if (sender is UnpatchTasks)
				{
					button = FindViewById<Button>(Resource.Id.Unpatch);

					buttonText = UnpatchTasks.Instance.IsRunning
						? Resources.GetText(Resource.String.abort)
						: Resources.GetText(Resource.String.unpatch);
				}
				else return;

				MainThread.BeginInvokeOnMainThread(() => { button.Text = buttonText; });
			}
		}

		private void OnStatusChanged(object sender, StatusChangedEventArgs e)
		{
			string info = e.Info;
			MessageBox.Data data = e.MessageData;
			if (info != null)
				MainThread.BeginInvokeOnMainThread(() => { FindViewById<TextView>(Resource.Id.Status).Text = info; });
			if (!data.Equals(MessageBox.Data.Empty))
				MainThread.BeginInvokeOnMainThread(() => { MessageBox.Show(this, data); });
		}

		private void OnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			int progress = e.Progress;
			int max = e.Max;
			ProgressBar progressBar = FindViewById<ProgressBar>(Resource.Id.progressBar);
			if (progressBar.Max != max) MainThread.BeginInvokeOnMainThread(() => { progressBar.Max = max; });
			MainThread.BeginInvokeOnMainThread(() => { progressBar.Progress = progress; });
		}

		private void CheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
		{
			bool isChecked = e.IsChecked;
			if (sender == FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata))
				Preferences.Set(Prefkey.backup_restore_savedata.ToString(), isChecked);
		}

		private void Clear_Click(object sender, EventArgs e)
		{
			TextView info = FindViewById<TextView>(Resource.Id.Status);

			if (!PatchTasks.Instance.IsRunning && !UnpatchTasks.Instance.IsRunning)
			{
				Java.IO.File apk = new Java.IO.File(FilePaths[Files.BackupApk]);
				if (apk.Exists()) apk.Delete();

				Java.IO.File obb = new Java.IO.File(FilePaths[Files.BackupObb]);
				if (obb.Exists()) obb.Delete();

				Java.IO.File savedata = new Java.IO.File(FilePaths[Files.BackupSavedata]);
				if (savedata.Exists()) savedata.Delete();

				info.Text = Resources.GetText(Resource.String.backup_cleared);

				apk.Dispose();
				obb.Dispose();
				savedata.Dispose();
			}
		}

		private void Unpatch_Click(object sender, EventArgs e)
		{
			if (!PatchTasks.Instance.IsRunning && !TokenSource.IsCancellationRequested)
			{
				if (!UnpatchTasks.Instance.IsRunning)
				{
					UnpatchTasks.Instance.StatusChanged += OnStatusChanged;
					UnpatchTasks.Instance.ProgressChanged += OnProgressChanged;
					UnpatchTasks.Instance.PropertyChanged += OnPropertyChanged;

					CheckBox checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
					Task.Run(() => UnpatchTasks.Instance.UnpatchTask(this, checkBoxSavedata.Checked));
				}
				else TokenSource.Cancel();
			}
		}

		private void Patch_Click(object sender, EventArgs e)
		{
			if (!UnpatchTasks.Instance.IsRunning && !TokenSource.IsCancellationRequested)
			{
				if (!PatchTasks.Instance.IsRunning)
				{
					PatchTasks.Instance.StatusChanged += OnStatusChanged;
					PatchTasks.Instance.ProgressChanged += OnProgressChanged;
					PatchTasks.Instance.PropertyChanged += OnPropertyChanged;

					CheckBox checkBoxSavedata = FindViewById<CheckBox>(Resource.Id.CheckBoxSavedata);
					Task.Run(() => PatchTasks.Instance.PatchTask(this, checkBoxSavedata.Checked));
				}
				else TokenSource.Cancel();
			}
		}

		private void FabOnClick(object sender, EventArgs eventArgs)
		{
			View view = (View) sender;
			Snackbar.Make(view,
					Java.Lang.String.Format(Resources.GetString(Resource.String.fab_version), AppInfo.VersionString),
					Snackbar.LengthLong)
				.SetAction("Action", (Android.Views.View.IOnClickListener) null).Show();
		}

		public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
			[GeneratedEnum] Android.Content.PM.Permission[] grantResults)
		{
			Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

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
				else if (!Directory.Exists(OkkeiFilesPath)) Directory.CreateDirectory(OkkeiFilesPath);
			}
		}
	}
}