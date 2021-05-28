using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using AndroidX.Lifecycle;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Patcher;
using OkkeiPatcher.Utils;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.ViewModels
{
	internal class MainViewModel : ViewModel, INotifyPropertyChanged
	{
		private readonly Lazy<PatchTools> _patchTools = new Lazy<PatchTools>(() => new PatchTools());
		private readonly Lazy<UnpatchTools> _unpatchTools = new Lazy<UnpatchTools>(() => new UnpatchTools());
		private readonly Lazy<ManifestTools> _manifestTools = new Lazy<ManifestTools>(() => new ManifestTools());
		private readonly Progress<ProgressInfo> _progress;
		private CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();
		private IInstallHandler _installHandler;
		private bool _patchToolsEventsSubscribed;
		private IUninstallHandler _uninstallHandler;
		private bool _unpatchToolsEventsSubscribed;

		public MainViewModel()
		{
			_progress = new Progress<ProgressInfo>(OnProgressChangedFromModel);

			SetApkIsPatchedPreferenceIfNotSet();
			SetButtonsState();
			SetCheckBoxState();
		}

		public bool PatchEnabled { get; set; }
		public bool UnpatchEnabled { get; set; }
		public string PatchText { get; set; }
		public string UnpatchText { get; set; }
		public bool ClearDataEnabled { get; set; }
		public bool ProcessSavedataEnabled { get; set; }
		public bool ProgressIndeterminate { get; set; }
		public int Progress { get; set; }
		public int ProgressMax { get; set; }
		public string Status { get; set; }

		public bool CanPatch => (!_unpatchTools.IsValueCreated || !_unpatchTools.Value.IsRunning) &&
		                        !_cancelTokenSource.IsCancellationRequested;

		public bool Patching => _patchTools.IsValueCreated && _patchTools.Value.IsRunning ||
		                        _manifestTools.IsValueCreated && _manifestTools.Value.IsRunning;

		public bool CanUnpatch => (!_patchTools.IsValueCreated || !_patchTools.Value.IsRunning) &&
		                          !_cancelTokenSource.IsCancellationRequested;

		public bool Unpatching => _unpatchTools.IsValueCreated && _unpatchTools.Value.IsRunning;

		public IProgress<ProgressInfo> ProgressProvider => _progress;
		public event PropertyChangedEventHandler PropertyChanged;
		public event EventHandler<MessageData> MessageGenerated;
		public event EventHandler<MessageData> FatalErrorOccurred;
		public event EventHandler<InstallMessageData> InstallMessageGenerated;
		public event EventHandler<UninstallMessageData> UninstallMessageGenerated;

		private void OnProcessSavedataEnabledChanged()
		{
			Preferences.Set(Prefkey.backup_restore_savedata.ToString(), ProcessSavedataEnabled);
		}

		private static void SetApkIsPatchedPreferenceIfNotSet()
		{
			if (!Preferences.ContainsKey(Prefkey.apk_is_patched.ToString()))
				Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
		}

		private void SetButtonsState()
		{
			if (Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
			{
				PatchEnabled = false;
				UnpatchEnabled = true;
				return;
			}

			PatchEnabled = true;
			UnpatchEnabled = false;
		}

		private void SetCheckBoxState()
		{
			if (!Preferences.ContainsKey(Prefkey.backup_restore_savedata.ToString()))
			{
				Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);
				return;
			}

			ProcessSavedataEnabled = Preferences.Get(Prefkey.backup_restore_savedata.ToString(), true);
		}

		private ProcessState CreateProcessState()
		{
			var processSavedata = ProcessSavedataEnabled;
			var scriptsUpdate = _manifestTools.Value.IsScriptsUpdateAvailable;
			var obbUpdate = _manifestTools.Value.IsObbUpdateAvailable;
			return new ProcessState(processSavedata, scriptsUpdate, obbUpdate);
		}

		public async Task<bool> RetrieveManifest()
		{
			_installHandler = _manifestTools.Value;

			_manifestTools.Value.InstallMessageGenerated += OnInstallMessageFromModel;
			_manifestTools.Value.StatusChanged += OnStatusChangedFromModel;
			_manifestTools.Value.MessageGenerated += OnMessageGeneratedFromModel;
			_manifestTools.Value.FatalErrorOccurred += OnFatalErrorOccurredFromModel;
			_manifestTools.Value.ErrorOccurred += OnErrorOccurred_ManifestTools;
			_manifestTools.Value.PropertyChanged += OnPropertyChanged_ManifestTools;

			return await _manifestTools.Value.RetrieveManifestAsync(_progress, _cancelTokenSource.Token);
		}

		public bool CheckForPatchUpdates()
		{
			var isPatchUpdateAvailable = _manifestTools.Value.IsPatchUpdateAvailable;
			if (isPatchUpdateAvailable) PatchEnabled = true;
			return isPatchUpdateAvailable;
		}

		public bool CheckForAppUpdates()
		{
			return _manifestTools.Value.IsAppUpdateAvailable;
		}

		public int GetPatchSize()
		{
			return _manifestTools.Value.PatchSizeInMB;
		}

		public double GetAppUpdateSize()
		{
			return _manifestTools.Value.AppUpdateSizeInMB;
		}

		public string GetAppChangelog()
		{
			return _manifestTools.Value.Manifest.OkkeiPatcher.Changelog;
		}

		public void UpdateApp()
		{
			_manifestTools.Value.UpdateApp(_progress, _cancelTokenSource.Token);
		}

		public void OnUninstallResult()
		{
			_uninstallHandler?.OnUninstallResult(_progress, _cancelTokenSource.Token);
		}

		public void OnInstallSuccess()
		{
			_installHandler?.OnInstallSuccess(_progress, _cancelTokenSource.Token);
		}

		public void OnInstallFail()
		{
			_installHandler?.NotifyInstallFailed();
		}

		private void OnPropertyChanged_PatchTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(_patchTools.Value.IsRunning)) return;

			if (!_patchTools.Value.IsRunning)
			{
				_cancelTokenSource.Dispose();
				_cancelTokenSource = new CancellationTokenSource();

				PatchText = OkkeiUtils.GetText(Resource.String.patch);
				ClearDataEnabled = true;
				if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false)) return;
				PatchEnabled = false;
				UnpatchEnabled = OkkeiUtils.IsBackupAvailable();

				return;
			}

			UnpatchEnabled = false;
			ClearDataEnabled = false;
			PatchText = OkkeiUtils.GetText(Resource.String.abort);
		}

		private void OnPropertyChanged_UnpatchTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(_unpatchTools.Value.IsRunning)) return;

			if (!_unpatchTools.Value.IsRunning)
			{
				_cancelTokenSource.Dispose();
				_cancelTokenSource = new CancellationTokenSource();

				UnpatchText = OkkeiUtils.GetText(Resource.String.unpatch);
				ClearDataEnabled = true;
				if (!Preferences.Get(Prefkey.apk_is_patched.ToString(), false))
				{
					UnpatchEnabled = false;
					PatchEnabled = true;
					return;
				}

				UnpatchEnabled = OkkeiUtils.IsBackupAvailable();
				return;
			}

			PatchEnabled = false;
			ClearDataEnabled = false;
			UnpatchText = OkkeiUtils.GetText(Resource.String.abort);
		}

		private void OnPropertyChanged_ManifestTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(_manifestTools.Value.IsRunning)) return;

			if (!_manifestTools.Value.IsRunning)
			{
				_cancelTokenSource.Dispose();
				_cancelTokenSource = new CancellationTokenSource();

				PatchEnabled = !Preferences.Get(Prefkey.apk_is_patched.ToString(), false) ||
				               _manifestTools.Value.IsPatchUpdateAvailable;
				PatchText = OkkeiUtils.GetText(Resource.String.patch);
				UnpatchEnabled = OkkeiUtils.IsBackupAvailable();
				ClearDataEnabled = true;
				return;
			}

			PatchEnabled = true;
			UnpatchEnabled = false;
			ClearDataEnabled = false;
			PatchText = OkkeiUtils.GetText(Resource.String.abort);
		}

		public void PackageInstallerOnInstallFailed(object sender, EventArgs e)
		{
			if (!(sender is PackageInstaller installer)) return;
			installer.InstallFailed -= PackageInstallerOnInstallFailed;
			_installHandler?.NotifyInstallFailed();
		}

		private void OnStatusChangedFromModel(object sender, string e)
		{
			Status = e;
		}

		private void OnMessageGeneratedFromModel(object sender, MessageData e)
		{
			MessageGenerated?.Invoke(this, e);
		}

		private void OnProgressChangedFromModel(ProgressInfo e)
		{
			ProgressIndeterminate = e.IsIndeterminate;
			ProgressMax = e.Max;
			if (!ProgressIndeterminate) Progress = e.Progress;
		}

		private void OnFatalErrorOccurredFromModel(object sender, MessageData e)
		{
			FatalErrorOccurred?.Invoke(this, e);
		}

		private void OnUninstallMessageFromModel(object sender, UninstallMessageData e)
		{
			UninstallMessageGenerated?.Invoke(this, e);
		}

		private void OnInstallMessageFromModel(object sender, InstallMessageData e)
		{
			InstallMessageGenerated?.Invoke(this, e);
		}

		public void AbortCurrentProcess()
		{
			if (Patching || Unpatching) _cancelTokenSource.Cancel();
		}

		public void StartPatch()
		{
			_installHandler = _patchTools.Value;
			_uninstallHandler = _patchTools.Value;

			if (!_patchToolsEventsSubscribed)
			{
				_patchTools.Value.InstallMessageGenerated += OnInstallMessageFromModel;
				_patchTools.Value.UninstallMessageGenerated += OnUninstallMessageFromModel;
				_patchTools.Value.StatusChanged += OnStatusChangedFromModel;
				_patchTools.Value.MessageGenerated += OnMessageGeneratedFromModel;
				_patchTools.Value.PropertyChanged += OnPropertyChanged_PatchTools;
				_patchTools.Value.ErrorOccurred += OnErrorOccurred_PatchTools;
				_patchToolsEventsSubscribed = true;
			}

			_patchTools.Value.Patch(CreateProcessState(), _manifestTools.Value.Manifest, _progress,
				_cancelTokenSource.Token);
		}

		private void OnErrorOccurred_PatchTools(object sender, EventArgs e)
		{
			if ((!_unpatchTools.IsValueCreated || !_unpatchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    _patchTools.IsValueCreated && _patchTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		private void OnErrorOccurred_ManifestTools(object sender, EventArgs e)
		{
			if ((!_patchTools.IsValueCreated || !_patchTools.Value.IsRunning) &&
			    (!_unpatchTools.IsValueCreated || !_unpatchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    _manifestTools.IsValueCreated && _manifestTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		public void StartUnpatch()
		{
			_installHandler = _unpatchTools.Value;
			_uninstallHandler = _unpatchTools.Value;

			if (!_unpatchToolsEventsSubscribed)
			{
				_unpatchTools.Value.InstallMessageGenerated += OnInstallMessageFromModel;
				_unpatchTools.Value.UninstallMessageGenerated += OnUninstallMessageFromModel;
				_unpatchTools.Value.StatusChanged += OnStatusChangedFromModel;
				_unpatchTools.Value.MessageGenerated += OnMessageGeneratedFromModel;
				_unpatchTools.Value.PropertyChanged += OnPropertyChanged_UnpatchTools;
				_unpatchTools.Value.ErrorOccurred += OnErrorOccurred_UnpatchTools;
				_unpatchToolsEventsSubscribed = true;
			}

			_unpatchTools.Value.Unpatch(CreateProcessState(), _progress, _cancelTokenSource.Token);
		}

		private void OnErrorOccurred_UnpatchTools(object sender, EventArgs e)
		{
			if ((!_patchTools.IsValueCreated || !_patchTools.Value.IsRunning) &&
			    !_cancelTokenSource.IsCancellationRequested &&
			    _unpatchTools.IsValueCreated && _unpatchTools.Value.IsRunning)
				_cancelTokenSource.Cancel();
		}

		public void ClearData()
		{
			Preferences.Clear();
			Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
			Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);

			OkkeiUtils.ClearOkkeiFiles();

			ProcessSavedataEnabled = true;
			PatchEnabled = true;
			UnpatchEnabled = false;
			Status = OkkeiUtils.GetText(Resource.String.data_cleared);
		}

		protected override void OnCleared()
		{
			base.OnCleared();
			PropertyChanged = null;
			MessageGenerated = null;
			FatalErrorOccurred = null;
			InstallMessageGenerated = null;
			UninstallMessageGenerated = null;
		}
	}
}