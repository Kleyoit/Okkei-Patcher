using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.DTO.Base;
using OkkeiPatcher.Model.Exceptions;
using OkkeiPatcher.Model.Manifest.Base;
using OkkeiPatcher.Utils;
using OkkeiPatcher.Utils.Extensions;
using PropertyChanged;
using static OkkeiPatcher.Model.OkkeiFilesPaths;

namespace OkkeiPatcher.Core.Base
{
	internal abstract class ManifestTools : ToolsBase, IInstallHandler
	{
		private const string ManifestFileName = "Manifest.json";
		private const string ManifestBackupFileName = "ManifestBackup.json";
		private const string AppUpdateFileName = "OkkeiPatcher.apk";
		private static readonly string AppUpdatePath = Path.Combine(OkkeiFilesPath, AppUpdateFileName);
		private static readonly string ManifestPath = Path.Combine(PrivateStorage, ManifestFileName);
		private static readonly string ManifestBackupPath = Path.Combine(PrivateStorage, ManifestBackupFileName);

		[DoNotNotify] public OkkeiManifest Manifest { get; private set; }
		[DoNotNotify] public bool ManifestLoaded { get; private set; }
		[DoNotNotify] protected string ManifestUrl { private get; set; }
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

		protected abstract bool VerifyManifest(OkkeiManifest manifest);

		public abstract Task<bool> RetrieveManifestAsync(IProgress<ProgressInfo> progress, CancellationToken token);

		protected async Task<bool> InternalRetrieveManifestAsync<T>(IProgress<ProgressInfo> progress,
			CancellationToken token) where T : OkkeiManifest
		{
			IsRunning = true;
			UpdateStatus(Resource.String.manifest_download);

			try
			{
				if (!await DownloadManifestAsync<T>(progress, token))
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

				if (!RestoreManifestBackup<T>())
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

		private async Task<bool> DownloadManifestAsync<T>(IProgress<ProgressInfo> progress, CancellationToken token)
			where T : OkkeiManifest
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

			T manifest;
			try
			{
				var json = File.ReadAllText(ManifestPath);
				manifest = JsonSerializer.Deserialize<T>(json);
			}
			catch
			{
				manifest = null;
			}

			if (!VerifyManifest(manifest)) return false;
			Manifest = manifest;
			return true;
		}

		private bool RestoreManifestBackup<T>() where T : OkkeiManifest
		{
			T manifest;

			if (!File.Exists(ManifestBackupPath)) return false;

			try
			{
				var json = File.ReadAllText(ManifestBackupPath);
				manifest = JsonSerializer.Deserialize<T>(json);
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
				var updateHash = await Md5Utils.ComputeMd5Async(AppUpdatePath, progress, token).ConfigureAwait(false);

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
			var data = MessageDataUtils.CreateInstallMessageData(titleId, messageId, buttonTextId, filePath);
			InstallMessageGenerated?.Invoke(this, data);
		}
	}
}