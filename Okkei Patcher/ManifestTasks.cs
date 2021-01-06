using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Newtonsoft.Json;
using Xamarin.Essentials;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal class ManifestTasks : BaseTasks
	{
		private static readonly Lazy<ManifestTasks> instance = new Lazy<ManifestTasks>(() => new ManifestTasks());

		private bool? _scriptsUpdateAvailable, _obbUpdateAvailable;

		private ManifestTasks()
		{
			PatchTasks.Instance.PropertyChanged += PatchTasksOnPropertyChanged;
		}

		public static bool IsInstantiated => instance.IsValueCreated;

		public static ManifestTasks Instance => instance.Value;

		private void PatchTasksOnPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(PatchTasks.Instance.IsRunning) && !PatchTasks.Instance.IsRunning)
			{
				_scriptsUpdateAvailable = false;
				_obbUpdateAvailable = false;
			}
		}

		public bool VerifyManifest(OkkeiManifest manifest)
		{
			return
				manifest.Version != 0 &&
				manifest.OkkeiPatcher.Version != 0 &&
				manifest.OkkeiPatcher.Changelog != null &&
				manifest.OkkeiPatcher.URL != null &&
				manifest.OkkeiPatcher.MD5 != null &&
				manifest.OkkeiPatcher.Size != 0 &&
				manifest.Scripts.Version != 0 &&
				manifest.Scripts.URL != null &&
				manifest.Scripts.MD5 != null &&
				manifest.Scripts.Size != 0;
		}

		public async Task<bool> GetManifest(CancellationToken token)
		{
			try
			{
				IsRunning = true;
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.manifest_download));

				try
				{
					await Utils.DownloadFile(ManifestUrl, PrivateStorage, ManifestFileName, token);

					var json = File.ReadAllText(ManifestPath);
					var manifest = JsonConvert.DeserializeObject<OkkeiManifest>(json);

					if (!VerifyManifest(manifest))
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.manifest_corrupted),
								Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
								() => System.Environment.Exit(0), null));
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
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.manifest_download_aborted),
							Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
							() => System.Environment.Exit(0), null));
					OnErrorOccurred(this, EventArgs.Empty);
					return false;
				}
				finally
				{
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
				return false;
			}
			return false;
		}

		public async Task InstallAppUpdate(Activity activity, CancellationToken token)
		{
			try
			{
				IsRunning = true;
				OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.update_app_download));

				try
				{
					try
					{
						await Utils.DownloadFile(GlobalManifest.OkkeiPatcher.URL, OkkeiFilesPath, AppUpdateFileName,
							token);
					}
					catch (Exception ex) when (!(ex is System.OperationCanceledException))
					{
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.http_file_download_error),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
						OnErrorOccurred(this, EventArgs.Empty);
					}

					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.compare_apk));
					var updateHash = await Utils.CalculateMD5(AppUpdatePath, token);

					if (updateHash != GlobalManifest.OkkeiPatcher.MD5)
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
								Application.Context.Resources.GetText(Resource.String.update_app_corrupted),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								null, null));
					}
					else
					{
						OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.installing));
						OnMessageGenerated(this,
							new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.attention),
								Application.Context.Resources.GetText(Resource.String.update_app_attention),
								Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
								() => MainThread.BeginInvokeOnMainThread(() =>
									Utils.InstallPackage(activity,
										Android.Net.Uri.FromFile(new Java.IO.File(AppUpdatePath)))),
								null));
					}
				}
				catch (System.OperationCanceledException)
				{
					OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
					OnMessageGenerated(this,
						new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.error),
							Application.Context.Resources.GetText(Resource.String.manifest_download_aborted),
							Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
							() => System.Environment.Exit(0), null));
				}
				finally
				{
					OnProgressChanged(this, new ProgressChangedEventArgs(0, 100));
					IsRunning = false;
				}
			}
			catch (Exception ex)
			{
				Utils.WriteBugReport(ex);
			}
		}

		public bool CheckAppUpdate()
		{
			int appVersion;
			if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
				appVersion = (int) Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0)
					.LongVersionCode;
			else
				appVersion = Application.Context.PackageManager.GetPackageInfo(AppInfo.PackageName, 0).VersionCode;
			if (GlobalManifest.Obb.Version > appVersion) return true;
			return false;
		}

		public bool CheckScriptsUpdate()
		{
			if (_scriptsUpdateAvailable != null) return _scriptsUpdateAvailable.Value;
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
			{
				if (!Preferences.ContainsKey(Prefkey.scripts_version.ToString()))
					Preferences.Set(Prefkey.scripts_version.ToString(), 1);
			}
			else
			{
				_scriptsUpdateAvailable = false;
				return _scriptsUpdateAvailable.Value;
			}

			var scriptsVersion = Preferences.Get(Prefkey.scripts_version.ToString(), 1);
			_scriptsUpdateAvailable = GlobalManifest.Scripts.Version > scriptsVersion;
			return _scriptsUpdateAvailable.Value;
		}

		public bool CheckObbUpdate()
		{
			if (_obbUpdateAvailable != null) return _obbUpdateAvailable.Value;
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
			{
				if (!Preferences.ContainsKey(Prefkey.obb_version.ToString()))
					Preferences.Set(Prefkey.obb_version.ToString(), 1);
			}
			else
			{
				_obbUpdateAvailable = false;
				return _obbUpdateAvailable.Value;
			}

			var obbVersion = Preferences.Get(Prefkey.obb_version.ToString(), 1);
			_obbUpdateAvailable = GlobalManifest.Obb.Version > obbVersion;
			return _obbUpdateAvailable.Value;
		}

		public int GetPatchSizeInMB()
		{
			var scriptsSize = CheckScriptsUpdate() ? GlobalManifest.Scripts.Size / 1024 : 0;
			var obbSize = CheckObbUpdate() ? GlobalManifest.Obb.Size / 1024 : 0;
			return (int) (scriptsSize + obbSize);
		}

		public double GetAppUpdateSizeInMB()
		{
			return Math.Round(GlobalManifest.OkkeiPatcher.Size / 1024d, 2);
		}
	}
}