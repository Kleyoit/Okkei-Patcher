using System;
using System.ComponentModel;
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
		private bool? _scriptsUpdateAvailable, _obbUpdateAvailable;

		public bool IsAppUpdateAvailable
		{
			get
			{
				int appVersion;
				if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
					appVersion = (int) Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0)
						.LongVersionCode;
				else
					appVersion = Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0).VersionCode;
				IsRunning = false;
				return GlobalManifest.OkkeiPatcher.Version > appVersion;
			}
		}

		public bool IsScriptsUpdateAvailable
		{
			get
			{
				if (_scriptsUpdateAvailable != null) return _scriptsUpdateAvailable.Value;
				if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
				{
					if (!Preferences.ContainsKey(Prefkey.scripts_version.ToString()))
						Preferences.Set(Prefkey.scripts_version.ToString(), 1);
					var scriptsVersion = Preferences.Get(Prefkey.scripts_version.ToString(), 1);
					_scriptsUpdateAvailable = GlobalManifest.Scripts.Version > scriptsVersion;
					return _scriptsUpdateAvailable.Value;
				}

				_scriptsUpdateAvailable = false;
				return _scriptsUpdateAvailable.Value;
			}
		}

		public bool IsObbUpdateAvailable
		{
			get
			{
				if (_obbUpdateAvailable != null) return _obbUpdateAvailable.Value;
				if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
				{
					if (!Preferences.ContainsKey(Prefkey.obb_version.ToString()))
						Preferences.Set(Prefkey.obb_version.ToString(), 1);
					var obbVersion = Preferences.Get(Prefkey.obb_version.ToString(), 1);
					_obbUpdateAvailable = GlobalManifest.Obb.Version > obbVersion;
					return _obbUpdateAvailable.Value;
				}

				_obbUpdateAvailable = false;
				return _obbUpdateAvailable.Value;
			}
		}

		public bool IsPatchUpdateAvailable => IsScriptsUpdateAvailable || IsObbUpdateAvailable;

		public int PatchUpdateSizeInMB
		{
			get
			{
				var scriptsSize = IsScriptsUpdateAvailable
					? (int) Math.Round(GlobalManifest.Scripts.Size / (double) 0x100000)
					: 0;
				var obbSize = IsObbUpdateAvailable
					? (int) Math.Round(GlobalManifest.Obb.Size / (double) 0x100000)
					: 0;
				return scriptsSize + obbSize;
			}
		}

		public void PatchTasksOnPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (!(sender is PatchTools patchTools)) return;
			if (e.PropertyName != nameof(patchTools.IsRunning) || patchTools.IsRunning)
				return;
			_scriptsUpdateAvailable = false;
			_obbUpdateAvailable = false;
		}

		public bool VerifyManifest(OkkeiManifest manifest)
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

		public async Task<bool> GetManifest(CancellationToken token)
		{
			IsRunning = true;
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.manifest_download));

			try
			{
				if (File.Exists(ManifestPath))
				{
					await UtilsInstance.Value.CopyFile(ManifestPath, PrivateStorage, ManifestBackupFileName, token)
						.ConfigureAwait(false);
					File.Delete(ManifestPath);
				}

				await UtilsInstance.Value.DownloadFile(ManifestUrl, PrivateStorage, ManifestFileName, token)
					.ConfigureAwait(false);

				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, true));

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

				if (!VerifyManifest(manifest))
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.manifest_corrupted),
							Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
							() => System.Environment.Exit(0), null));
					OnErrorOccurred(this, EventArgs.Empty);
				}
				else
				{
					OnStatusChanged(this,
						Application.Context.Resources.GetText(Resource.String.manifest_download_completed));
					GlobalManifest = manifest;
					return true;
				}
			}
			catch (Exception)
			{
				OkkeiManifest manifest = null;
				File.Delete(ManifestPath);
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

					if (!VerifyManifest(manifest)) File.Delete(ManifestBackupPath);
				}

				if (!File.Exists(ManifestBackupPath))
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.manifest_download_aborted),
							Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
							() => System.Environment.Exit(0), null));
					OnErrorOccurred(this, EventArgs.Empty);
					IsRunning = false;
					return false;
				}

				File.Copy(ManifestBackupPath, ManifestPath);
				File.Delete(ManifestBackupPath);
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.manifest_backup_used));
				GlobalManifest = manifest;
				return true;
			}
			finally
			{
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
			}

			IsRunning = false;
			return false;
		}

		public async Task InstallAppUpdate(Activity activity, CancellationToken token)
		{
			IsRunning = true;
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.update_app_download));

			try
			{
				try
				{
					await UtilsInstance.Value.DownloadFile(GlobalManifest.OkkeiPatcher.URL, OkkeiFilesPath,
						AppUpdateFileName,
						token).ConfigureAwait(false);
				}
				catch (Exception ex) when (!(ex is System.OperationCanceledException))
				{
					throw new HttpRequestException("Download failed.");
				}

				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));
				var updateHash = await UtilsInstance.Value.CalculateMD5(AppUpdatePath, token).ConfigureAwait(false);

				if (updateHash != GlobalManifest.OkkeiPatcher.MD5)
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.update_app_corrupted),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
					OnErrorOccurred(this, EventArgs.Empty);
					IsRunning = false;
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
				}
				else
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.installing));
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, true));
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
							Application.Context.Resources.GetText(Resource.String.update_app_attention),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							() => MainThread.BeginInvokeOnMainThread(() =>
								UtilsInstance.Value.InstallPackage(activity,
									Android.Net.Uri.FromFile(new Java.IO.File(AppUpdatePath)))),
							null));
				}
			}
			catch (Exception ex)
			{
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
				if (ex is System.OperationCanceledException)
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.update_app_aborted),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
				if (ex is HttpRequestException && ex.Message == "Download failed.")
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.http_file_download_error),
							Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
							null, null));
				OnErrorOccurred(this, EventArgs.Empty);
				IsRunning = false;
				OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
			}
		}

		protected override Task Finish(bool processSavedata, bool scriptsUpdate, bool obbUpdate, CancellationToken token)
		{
			if (!IsRunning) return Task.CompletedTask;
			//if (System.IO.File.Exists(AppUpdatePath)) System.IO.File.Delete(AppUpdatePath);
			OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
			OnStatusChanged(this, string.Empty);
			IsRunning = false;
			return Task.CompletedTask;
		}

		public override Task OnUninstallResult(Activity activity, bool scriptsUpdate, CancellationToken token)
		{
			throw new NotImplementedException("This should not be called.");
		}
	}
}