using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class ManifestTools : ToolsBase
	{
		public ManifestTools(Utils utils) : base(utils)
		{
		}

		public OkkeiManifest Manifest { get; private set; }

		public bool IsAppUpdateAvailable
		{
			get
			{
				int appVersion;
				if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
					appVersion = (int) Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0)
						.LongVersionCode;
				else
#pragma warning disable CS0618 // Type or member is obsolete
					appVersion = Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0).VersionCode;
#pragma warning restore CS0618 // Type or member is obsolete

				return Manifest.OkkeiPatcher.Version > appVersion;
			}
		}

		public bool IsScriptsUpdateAvailable
		{
			get
			{
				if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false)) return false;

				if (!Preferences.ContainsKey(Prefkey.scripts_version.ToString()))
					Preferences.Set(Prefkey.scripts_version.ToString(), 1);
				var scriptsVersion = Preferences.Get(Prefkey.scripts_version.ToString(), 1);
				return Manifest.Scripts.Version > scriptsVersion;
			}
		}

		public bool IsObbUpdateAvailable
		{
			get
			{
				if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false)) return false;

				if (!Preferences.ContainsKey(Prefkey.obb_version.ToString()))
					Preferences.Set(Prefkey.obb_version.ToString(), 1);
				var obbVersion = Preferences.Get(Prefkey.obb_version.ToString(), 1);
				return Manifest.Obb.Version > obbVersion;
			}
		}

		public bool IsPatchUpdateAvailable => IsScriptsUpdateAvailable || IsObbUpdateAvailable;

		public int PatchSizeInMB
		{
			get
			{
				if (!IsPatchUpdateAvailable)
				{
					var scriptsSize = (int) Math.Round(Manifest.Scripts.Size / (double) 0x100000);
					var obbSize = (int) Math.Round(Manifest.Obb.Size / (double) 0x100000);
					return scriptsSize + obbSize;
				}

				var scriptsUpdateSize = IsScriptsUpdateAvailable
					? (int) Math.Round(Manifest.Scripts.Size / (double) 0x100000)
					: 0;
				var obbUpdateSize = IsObbUpdateAvailable
					? (int) Math.Round(Manifest.Obb.Size / (double) 0x100000)
					: 0;
				return scriptsUpdateSize + obbUpdateSize;
			}
		}

		public double AppUpdateSizeInMB => Math.Round(Manifest.OkkeiPatcher.Size / (double) 0x100000, 2);

		public static bool VerifyManifest(OkkeiManifest manifest)
		{
			return
				manifest != null &&
				manifest.Version != 0 &&
				manifest.OkkeiPatcher != null &&
				manifest.OkkeiPatcher.Version != 0 &&
				manifest.OkkeiPatcher.Changelog != null &&
				manifest.OkkeiPatcher.URL != null &&
				manifest.OkkeiPatcher.MD5 != null &&
				manifest.OkkeiPatcher.Size != 0 &&
				manifest.Scripts != null &&
				manifest.Scripts.Version != 0 &&
				manifest.Scripts.URL != null &&
				manifest.Scripts.MD5 != null &&
				manifest.Scripts.Size != 0 &&
				manifest.Obb != null &&
				manifest.Obb.Version != 0 &&
				manifest.Obb.URL != null &&
				manifest.Obb.MD5 != null &&
				manifest.Obb.Size != 0;
		}

		public async Task<bool> RetrieveManifest(CancellationToken token)
		{
			return await RetrieveManifestPrivate(token).OnException(WriteBugReport);
		}

		private async Task<bool> RetrieveManifestPrivate(CancellationToken token)
		{
			IsRunning = true;
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.manifest_download));

			try
			{
				if (!await DownloadManifest(token))
				{
					SetStatusToAborted();
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.manifest_corrupted),
							Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
							() => System.Environment.Exit(0), null));
					NotifyAboutError();
					IsRunning = false;
					return false;
				}

				OnStatusChanged(this,
					Application.Context.Resources.GetText(Resource.String.manifest_download_completed));
				IsRunning = false;
				return true;
			}
			catch (Exception)
			{
				File.Delete(ManifestPath);

				if (!RestoreManifestBackup())
				{
					SetStatusToAborted();
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.manifest_download_aborted),
							Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
							() => System.Environment.Exit(0), null));
					NotifyAboutError();
					IsRunning = false;
					return false;
				}

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.manifest_backup_used));
				IsRunning = false;
				return true;
			}
			finally
			{
				ResetProgress();
			}
		}

		private async Task<bool> DownloadManifest(CancellationToken token)
		{
			if (File.Exists(ManifestPath))
			{
				await UtilsInstance.CopyFile(ManifestPath, PrivateStorage, ManifestBackupFileName, token)
					.ConfigureAwait(false);
				File.Delete(ManifestPath);
			}

			await UtilsInstance.DownloadFile(ManifestUrl, PrivateStorage, ManifestFileName, token)
				.ConfigureAwait(false);

			SetIndeterminateProgress();

			OkkeiManifest manifest;
			try
			{
				var json = File.ReadAllText(ManifestPath);
				manifest = JsonSerializer.Deserialize<OkkeiManifest>(json);
			}
			catch
			{
				manifest = null;
			}

			if (VerifyManifest(manifest))
			{
				Manifest = manifest;
				return true;
			}

			return false;
		}

		private bool RestoreManifestBackup()
		{
			OkkeiManifest manifest = null;
			if (File.Exists(ManifestBackupPath))
			{
				try
				{
					var json = File.ReadAllText(ManifestBackupPath);
					manifest = JsonSerializer.Deserialize<OkkeiManifest>(json);
				}
				catch
				{
					manifest = null;
				}

				if (!VerifyManifest(manifest))
				{
					File.Delete(ManifestBackupPath);
					return false;
				}
			}

			File.Copy(ManifestBackupPath, ManifestPath);
			File.Delete(ManifestBackupPath);
			Manifest = manifest;
			return true;
		}

		public void UpdateApp(Activity activity, CancellationToken token)
		{
			Task.Run(() => UpdateAppPrivate(activity, token).OnException(WriteBugReport));
		}

		private async Task UpdateAppPrivate(Activity activity, CancellationToken token)
		{
			IsRunning = true;
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.update_app_download));

			try
			{
				await DownloadAppUpdate(token);

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));
				var updateHash = await UtilsInstance.CalculateMD5(AppUpdatePath, token).ConfigureAwait(false);

				if (updateHash != Manifest.OkkeiPatcher.MD5)
				{
					SetStatusToAborted();
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.update_app_corrupted),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));
					NotifyAboutError();
					IsRunning = false;
					ResetProgress();
					return;
				}

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.installing));
				SetIndeterminateProgress();
				InstallAppUpdate(activity);
			}
			catch (Exception ex)
			{
				SetStatusToAborted();

				if (ex is System.OperationCanceledException)
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.update_app_aborted),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));

				if (ex is HttpRequestException)
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.http_file_download_error),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null, null, null));

				NotifyAboutError();
				IsRunning = false;
				ResetProgress();
			}
		}

		private async Task DownloadAppUpdate(CancellationToken token)
		{
			try
			{
				await UtilsInstance.DownloadFile(Manifest.OkkeiPatcher.URL, OkkeiFilesPath, AppUpdateFileName, token)
					.ConfigureAwait(false);
			}
			catch (Exception ex) when (!(ex is System.OperationCanceledException))
			{
				throw new HttpRequestException("Download failed.");
			}
		}

		private void InstallAppUpdate(Activity activity)
		{
			OnMessageGenerated(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
					Application.Context.Resources.GetText(Resource.String.update_app_attention),
					Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
					() => MainThread.BeginInvokeOnMainThread(() =>
						UtilsInstance.InstallPackage(activity,
							Android.Net.Uri.FromFile(new Java.IO.File(AppUpdatePath)))),
					null));
		}

		protected override Task OnInstallSuccessProtected(CancellationToken token)
		{
			if (!IsRunning) return Task.CompletedTask;
			//if (System.IO.File.Exists(AppUpdatePath)) System.IO.File.Delete(AppUpdatePath);
			ResetProgress();
			ClearStatus();
			IsRunning = false;
			return Task.CompletedTask;
		}

		protected override Task OnUninstallResultProtected(Activity activity, CancellationToken token)
		{
			throw new NotImplementedException("This should not be called.");
		}
	}
}