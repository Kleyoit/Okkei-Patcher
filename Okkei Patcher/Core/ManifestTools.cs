using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.Exceptions;
using OkkeiPatcher.Model.Manifest;
using OkkeiPatcher.Utils;
using OkkeiPatcher.Utils.Extensions;
using PropertyChanged;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.OkkeiFilesPaths;

namespace OkkeiPatcher.Core
{
	internal class ManifestTools : ToolsBase, IInstallHandler
	{
		private const string ManifestUrl =
			"https://raw.githubusercontent.com/ForrrmerBlack/okkei-patcher/master/Manifest.json";

		private const string ManifestFileName = "Manifest.json";
		private const string ManifestBackupFileName = "ManifestBackup.json";
		private const string AppUpdateFileName = "OkkeiPatcher.apk";
		public static readonly string ManifestPath = Path.Combine(PrivateStorage, ManifestFileName);
		public static readonly string ManifestBackupPath = Path.Combine(PrivateStorage, ManifestBackupFileName);
		private static readonly string AppUpdatePath = Path.Combine(OkkeiFilesPath, AppUpdateFileName);

		[DoNotNotify]
		public OkkeiManifest Manifest { get; private set; }

		[DoNotNotify]
		public bool ManifestLoaded { get; private set; }

		public bool IsAppUpdateAvailable
		{
			get
			{
				try
				{
					return Manifest.OkkeiPatcher.Version > PackageManagerUtils.GetVersionCode();
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

		public event EventHandler<InstallMessageData> InstallMessageGenerated;

		public void NotifyInstallFailed()
		{
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.install_error, Resource.String.dialog_ok);
			IsRunning = false;
		}

		public void OnInstallSuccess(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (!IsRunning) return;
			//if (System.IO.File.Exists(AppUpdatePath)) System.IO.File.Delete(AppUpdatePath);
			progress.Reset();
			ClearStatus();
			IsRunning = false;
		}

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

		public async Task<bool> RetrieveManifestAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			return await InternalRetrieveManifestAsync(progress, token).OnException(WriteBugReport);
		}

		private async Task<bool> InternalRetrieveManifestAsync(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			IsRunning = true;
			UpdateStatus(Resource.String.manifest_download);

			try
			{
				if (!await DownloadManifestAsync(progress, token))
				{
					SetStatusToAborted();
					DisplayFatalErrorMessage(Resource.String.error, Resource.String.manifest_corrupted,
						Resource.String.dialog_exit);
					return false;
				}

				UpdateStatus(Resource.String.manifest_download_completed);
				ManifestLoaded = true;
				return true;
			}
			catch (HttpStatusCodeException ex)
			{
				SetStatusToAborted();
				DisplayMessage(Resource.String.error, Resource.String.http_file_access_error, Resource.String.dialog_ok,
					ex.StatusCode.ToString());
				return false;
			}
			catch
			{
				File.Delete(ManifestPath);

				if (!RestoreManifestBackup())
				{
					SetStatusToAborted();
					DisplayFatalErrorMessage(Resource.String.error, Resource.String.manifest_download_failed,
						Resource.String.dialog_exit);
					return false;
				}

				UpdateStatus(Resource.String.manifest_backup_used);
				return true;
			}
			finally
			{
				progress.Reset();
				IsRunning = false;
			}
		}

		private async Task<bool> DownloadManifestAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			if (File.Exists(ManifestPath))
			{
				await IOUtils.CopyFileAsync(ManifestPath, PrivateStorage, ManifestBackupFileName, progress, token)
					.ConfigureAwait(false);
				File.Delete(ManifestPath);
			}

			await IOUtils.DownloadFileAsync(ManifestUrl, PrivateStorage, ManifestFileName, progress, token)
				.ConfigureAwait(false);

			progress.MakeIndeterminate();

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

		public void UpdateApp(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalUpdateAppAsync(progress, token).OnException(WriteBugReport));
		}

		private async Task InternalUpdateAppAsync(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			IsRunning = true;
			UpdateStatus(Resource.String.update_app_download);

			try
			{
				await DownloadAppUpdateAsync(progress, token);

				UpdateStatus(Resource.String.compare_apk);
				var updateHash = await MD5Utils.ComputeMD5Async(AppUpdatePath, progress, token).ConfigureAwait(false);

				if (updateHash != Manifest.OkkeiPatcher.MD5)
				{
					SetStatusToAborted();
					DisplayErrorMessage(Resource.String.error, Resource.String.update_app_corrupted,
						Resource.String.dialog_ok);
					progress.Reset();
					return;
				}

				UpdateStatus(Resource.String.installing);
				progress.MakeIndeterminate();
				InstallAppUpdate();
			}
			catch (OperationCanceledException)
			{
				SetStatusToAborted();
				DisplayErrorMessage(Resource.String.error, Resource.String.update_app_aborted,
					Resource.String.dialog_ok);
				progress.Reset();
			}
			catch (HttpStatusCodeException ex)
			{
				SetStatusToAborted();
				DisplayMessage(Resource.String.error, Resource.String.http_file_access_error, Resource.String.dialog_ok,
					ex.StatusCode.ToString());
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
			{
				SetStatusToAborted();
				DisplayErrorMessage(Resource.String.error, Resource.String.http_file_download_error,
					Resource.String.dialog_ok);
				progress.Reset();
			}
			finally
			{
				IsRunning = false;
			}
		}

		private async Task DownloadAppUpdateAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			await IOUtils.DownloadFileAsync(Manifest.OkkeiPatcher.URL, OkkeiFilesPath, AppUpdateFileName, progress,
					token)
				.ConfigureAwait(false);
		}

		private void InstallAppUpdate()
		{
			DisplayInstallMessage(Resource.String.attention, Resource.String.update_app_attention,
				Resource.String.dialog_ok, AppUpdatePath);
		}

		private void DisplayInstallMessage(int titleId, int messageId, int buttonTextId, string filePath)
		{
			var data = MessageDataUtils.CreateInstallMessageData(titleId, messageId, buttonTextId, filePath);
			InstallMessageGenerated?.Invoke(this, data);
		}
	}
}