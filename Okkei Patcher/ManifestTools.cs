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
				try
				{
					int appVersion;
					if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
						appVersion = (int) Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0)
							.LongVersionCode;
					else
#pragma warning disable CS0618 // Type or member is obsolete
						appVersion = Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0)
							.VersionCode;
#pragma warning restore CS0618 // Type or member is obsolete

					return Manifest.OkkeiPatcher.Version > appVersion;
				}
				catch (Exception ex)
				{
					WriteBugReport(ex);
					return false;
				}
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
			return await InternalRetrieveManifest(token).OnException(WriteBugReport);
		}

		private async Task<bool> InternalRetrieveManifest(CancellationToken token)
		{
			IsRunning = true;
			UpdateStatus(Resource.String.manifest_download);

			try
			{
				if (!await DownloadManifest(token))
				{
					SetStatusToAborted();
					DisplayMessage(Resource.String.error, Resource.String.manifest_corrupted,
						Resource.String.dialog_exit, () => System.Environment.Exit(0));
					NotifyAboutError();
					return false;
				}

				UpdateStatus(Resource.String.manifest_download_completed);
				return true;
			}
			catch
			{
				File.Delete(ManifestPath);

				if (!RestoreManifestBackup())
				{
					SetStatusToAborted();
					DisplayMessage(Resource.String.error, Resource.String.manifest_download_failed,
						Resource.String.dialog_exit, () => System.Environment.Exit(0));
					NotifyAboutError();
					return false;
				}

				UpdateStatus(Resource.String.manifest_backup_used);
				return true;
			}
			finally
			{
				ResetProgress();
				IsRunning = false;
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

			if (!VerifyManifest(manifest)) return false;
			Manifest = manifest;
			return true;
		}

		private bool RestoreManifestBackup()
		{
			OkkeiManifest manifest;

			if (!File.Exists(ManifestBackupPath)) return false;

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

			File.Copy(ManifestBackupPath, ManifestPath);
			File.Delete(ManifestBackupPath);
			Manifest = manifest;
			return true;
		}

		public void UpdateApp(Activity activity, CancellationToken token)
		{
			Task.Run(() => InternalUpdateApp(activity, token).OnException(WriteBugReport));
		}

		private async Task InternalUpdateApp(Activity activity, CancellationToken token)
		{
			IsRunning = true;
			UpdateStatus(Resource.String.update_app_download);

			try
			{
				await DownloadAppUpdate(token);

				UpdateStatus(Resource.String.compare_apk);
				var updateHash = await UtilsInstance.CalculateMD5(AppUpdatePath, token).ConfigureAwait(false);

				if (updateHash != Manifest.OkkeiPatcher.MD5)
				{
					SetStatusToAborted();
					DisplayMessage(Resource.String.error, Resource.String.update_app_corrupted,
						Resource.String.dialog_ok, null);
					NotifyAboutError();
					ResetProgress();
					return;
				}

				UpdateStatus(Resource.String.installing);
				SetIndeterminateProgress();
				InstallAppUpdate(activity);
			}
			catch (System.OperationCanceledException)
			{
				SetStatusToAborted();

				DisplayMessage(Resource.String.error, Resource.String.update_app_aborted, Resource.String.dialog_ok,
					null);

				NotifyAboutError();
				ResetProgress();
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
			{
				SetStatusToAborted();

				DisplayMessage(Resource.String.error, Resource.String.http_file_download_error,
					Resource.String.dialog_ok, null);

				NotifyAboutError();
				ResetProgress();
			}
			finally
			{
				IsRunning = false;
			}
		}

		private async Task DownloadAppUpdate(CancellationToken token)
		{
			await UtilsInstance.DownloadFile(Manifest.OkkeiPatcher.URL, OkkeiFilesPath, AppUpdateFileName, token)
				.ConfigureAwait(false);
		}

		private void InstallAppUpdate(Activity activity)
		{
			DisplayMessage(Resource.String.attention, Resource.String.update_app_attention, Resource.String.dialog_ok,
				() => MainThread.BeginInvokeOnMainThread(() =>
					UtilsInstance.InstallPackage(activity, Android.Net.Uri.FromFile(new Java.IO.File(AppUpdatePath)))));
		}

		protected override Task InternalOnInstallSuccess(CancellationToken token)
		{
			if (!IsRunning) return Task.CompletedTask;
			//if (System.IO.File.Exists(AppUpdatePath)) System.IO.File.Delete(AppUpdatePath);
			ResetProgress();
			ClearStatus();
			IsRunning = false;
			return Task.CompletedTask;
		}

		protected override Task InternalOnUninstallResult(Activity activity, CancellationToken token)
		{
			throw new NotImplementedException("This should not be called.");
		}
	}
}