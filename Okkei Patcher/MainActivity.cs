using System;
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
using System.Security.Cryptography.X509Certificates;
using Android.Content.Res;
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
					MessageBox.Show(this, Resources.GetText(Resource.String.error),
						Resources.GetText(Resource.String.no_install_permission), MessageBox.Code.Exit);
			}

			if (requestCode == (int) RequestCodes.UninstallCode)
				Utils.OnUninstallResult(this);

			if (requestCode == (int) RequestCodes.InstallCode)
				Utils.OnInstallResult(this);
		}

		protected override void OnNewIntent(Intent intent)
		{
			Bundle extras = intent.Extras;
			if (PACKAGE_INSTALLED_ACTION.Equals(intent.Action))
			{
				TextView info = FindViewById<TextView>(Resource.Id.Status);

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

						bool isPatched = Preferences.Get(Prefkey.apk_is_patched.ToString(), false);

						if (!isPatched)
						{
							MainThread.BeginInvokeOnMainThread(() =>
							{
								info.Text = Resources.GetText(Resource.String.patch_success);
							});

							var signedApk = new Java.IO.File(FilePaths[Files.SignedApk]);
							if (signedApk.Exists()) signedApk.Delete();
							signedApk.Dispose();

							Task.Run(() => PatchTasks.FinishPatch(this));
						}
						else
							Task.Run(() => UnpatchTasks.RestoreFiles(this));

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


			// Read testcert for signing APK
			AssetManager assets = this.Assets;
			Stream testkeyFile = assets.Open(CertFileName);
			int testkeySize = 2797;

			X509Certificate2 testkeyTemp = new X509Certificate2(Utils.ReadCert(testkeyFile, testkeySize), CertPassword);
			testkey = testkeyTemp;

			testkeyFile?.Close();
			testkeyFile?.Dispose();


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
					MessageBox.Show(this, Resources.GetText(Resource.String.attention),
						Resources.GetText(Resource.String.unknown_sources_notice),
						MessageBox.Code.UnknownAppSourceNotice);
			}
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

			if (!PatchTasks.IsAnyRunning && !UnpatchTasks.IsAnyRunning)
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
			if (!PatchTasks.IsAnyRunning)
			{
				if (!TokenSource.IsCancellationRequested) UnpatchTasks.IsAnyRunning = !UnpatchTasks.IsAnyRunning;

				Button unpatch = FindViewById<Button>(Resource.Id.Unpatch);
				ProgressBar progressBar = FindViewById<ProgressBar>(Resource.Id.progressBar);

				if (UnpatchTasks.IsAnyRunning && !TokenSource.IsCancellationRequested)
				{
					unpatch.Text = Resources.GetText(Resource.String.abort);

					progressBar.Progress = 0;
					Task.Run(() => UnpatchTasks.UnpatchTask(this));
				}
				else TokenSource.Cancel();
			}
		}

		private void Patch_Click(object sender, EventArgs e)
		{
			if (!UnpatchTasks.IsAnyRunning)
			{
				if (!TokenSource.IsCancellationRequested) PatchTasks.IsAnyRunning = !PatchTasks.IsAnyRunning;

				Button patch = FindViewById<Button>(Resource.Id.Patch);
				ProgressBar progressBar = FindViewById<ProgressBar>(Resource.Id.progressBar);

				if (PatchTasks.IsAnyRunning && !TokenSource.IsCancellationRequested)
				{
					patch.Text = Resources.GetText(Resource.String.abort);

					progressBar.Progress = 0;
					Task.Run(() => PatchTasks.PatchTask(this));
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