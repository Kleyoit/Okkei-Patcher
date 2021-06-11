using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.DTO.Base;
using OkkeiPatcher.Model.Exceptions;
using OkkeiPatcher.Model.Manifest;
using OkkeiPatcher.Utils;
using OkkeiPatcher.Utils.Extensions;
using PropertyChanged;

namespace OkkeiPatcher.Core.Base
{
	internal abstract class ManifestTools : ToolsBase, IInstallHandler
	{
		private const string ManifestUrl =
			"https://raw.githubusercontent.com/ForrrmerBlack/okkei-patcher/master/Manifest.json";

		private const string ManifestFileName = "Manifest.json";
		private const string ManifestBackupFileName = "ManifestBackup.json";
		private const string AppUpdateFileName = "OkkeiPatcher.apk";
		private static readonly string AppUpdatePath = Path.Combine(OkkeiPaths.Root, AppUpdateFileName);
		private static readonly string ManifestPath = Path.Combine(OkkeiPaths.Private, ManifestFileName);
		private static readonly string ManifestBackupPath = Path.Combine(OkkeiPaths.Private, ManifestBackupFileName);

		[DoNotNotify] public OkkeiManifest Manifest { get; private set; }
		[DoNotNotify] public bool ManifestLoaded { get; private set; }
		public abstract IPatchUpdates PatchUpdates { get; }
		public abstract int PatchSizeInMb { get; }
		public double AppUpdateSizeInMb => Math.Round(Manifest.OkkeiPatcher.Size / (double) 0x100000, 2);

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

		private static bool VerifyManifest(OkkeiManifest manifest)
		{
			return
				manifest != null &&
				manifest.Version > 0 &&
				manifest.OkkeiPatcher != null &&
				manifest.OkkeiPatcher.Version > 0 &&
				!string.IsNullOrEmpty(manifest.OkkeiPatcher.Changelog) &&
				!string.IsNullOrEmpty(manifest.OkkeiPatcher.URL) &&
				!string.IsNullOrEmpty(manifest.OkkeiPatcher.MD5) &&
				manifest.OkkeiPatcher.Size > 0 &&
				manifest.Patches != null &&
				manifest.Patches.Count > 0 &&
				manifest.Patches.Values.All(files =>
					files != null &&
					files.Count > 0 &&
					files.Values.All(file =>
						file != null &&
						file.Version > 0 &&
						!string.IsNullOrEmpty(file.URL) &&
						!string.IsNullOrEmpty(file.MD5) &&
						file.Size > 0));
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
				await IOUtils.CopyFileAsync(ManifestPath, OkkeiPaths.Private, ManifestBackupFileName, progress, token)
					.ConfigureAwait(false);
				File.Delete(ManifestPath);
			}

			await IOUtils.DownloadFileAsync(ManifestUrl, OkkeiPaths.Private, ManifestFileName, progress, token)
				.ConfigureAwait(false);

			progress.MakeIndeterminate();

			OkkeiManifest manifest;
			try
			{
				string json = File.ReadAllText(ManifestPath);
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
				string json = File.ReadAllText(ManifestBackupPath);
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

		private async Task InternalUpdateAppAsync(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			IsRunning = true;
			UpdateStatus(Resource.String.update_app_download);

			try
			{
				await DownloadAppUpdateAsync(progress, token);

				UpdateStatus(Resource.String.compare_apk);
				string updateHash =
					await Md5Utils.ComputeMd5Async(AppUpdatePath, progress, token).ConfigureAwait(false);

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
			await IOUtils.DownloadFileAsync(Manifest.OkkeiPatcher.URL, OkkeiPaths.Root, AppUpdateFileName, progress,
					token)
				.ConfigureAwait(false);
		}

		public static void DeleteManifest()
		{
			FileUtils.DeleteIfExists(ManifestPath);
			FileUtils.DeleteIfExists(ManifestBackupPath);
		}

		private void InstallAppUpdate()
		{
			DisplayInstallMessage(Resource.String.attention, Resource.String.update_app_attention,
				Resource.String.dialog_ok, AppUpdatePath);
		}

		private void DisplayInstallMessage(int titleId, int messageId, int buttonTextId, string filePath)
		{
			InstallMessageData data =
				MessageDataUtils.CreateInstallMessageData(titleId, messageId, buttonTextId, filePath);
			InstallMessageGenerated?.Invoke(this, data);
		}
	}
}