using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AndroidX.Lifecycle;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Patcher;
using OkkeiPatcher.Utils;
using PropertyChanged;
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
		private bool _patchToolsEventsSubscribed;
		private bool _unpatchToolsEventsSubscribed;
		private IInstallHandler _installHandler;
		private IUninstallHandler _uninstallHandler;

		public MainViewModel()
		{
			_progress = new Progress<ProgressInfo>(OnProgressChangedFromModel);

			SetApkIsPatchedPreferenceIfNotSet();
			SetCheckBoxStatePreferenceIfNotSet();
			Init();
		}

		public bool PatchEnabled { get; private set; }
		public bool UnpatchEnabled { get; private set; }
		public int PatchText { get; private set; }
		public int UnpatchText { get; private set; }
		public bool ClearDataEnabled { get; private set; }
		public bool ProcessSavedataEnabled { get; set; }
		public bool ProgressIndeterminate { get; private set; }
		public int Progress { get; private set; }
		public int ProgressMax { get; private set; }
		public int Status { get; private set; }
		public bool ManifestLoaded => _manifestTools.Value.ManifestLoaded;
		public bool IsPatched => Preferences.Get(Prefkey.apk_is_patched.ToString(), false);
		public bool Patching => _patchTools.IsValueCreated && _patchTools.Value.IsRunning;
		public bool Unpatching => _unpatchTools.IsValueCreated && _unpatchTools.Value.IsRunning;
		public bool ManifestToolsRunning => _manifestTools.IsValueCreated && _manifestTools.Value.IsRunning;
		public bool CanPatch => !Unpatching && !_cancelTokenSource.IsCancellationRequested;
		public bool CanUnpatch => !Patching && !_cancelTokenSource.IsCancellationRequested;
		public IProgress<ProgressInfo> ProgressProvider => _progress;

		[DoNotNotify]
		public bool Exiting { get; set; }

		public event PropertyChangedEventHandler PropertyChanged;
		public event EventHandler<MessageData> MessageGenerated;
		public event EventHandler<MessageData> FatalErrorOccurred;
		public event EventHandler<InstallMessageData> InstallMessageGenerated;
		public event EventHandler<UninstallMessageData> UninstallMessageGenerated;

		private void OnProcessSavedataEnabledChanged()
		{
			Preferences.Set(Prefkey.backup_restore_savedata.ToString(), ProcessSavedataEnabled);
		}

		private void Init()
		{
			PatchText = Resource.String.patch;
			UnpatchText = Resource.String.unpatch;
			ClearDataEnabled = true;
			ProcessSavedataEnabled = Preferences.Get(Prefkey.backup_restore_savedata.ToString(), true);
			Status = Resource.String.empty;
			PatchEnabled = !IsPatched;
			UnpatchEnabled = IsPatched;
		}

		private static void SetApkIsPatchedPreferenceIfNotSet()
		{
			if (Preferences.ContainsKey(Prefkey.apk_is_patched.ToString())) return;
			Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
		}

		private static void SetCheckBoxStatePreferenceIfNotSet()
		{
			if (Preferences.ContainsKey(Prefkey.backup_restore_savedata.ToString())) return;
			Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);
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
			_manifestTools.Value.ErrorOccurred += OnErrorOccurredFromManifestTools;
			_manifestTools.Value.PropertyChanged += OnPropertyChangedFromManifestTools;

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

		private void OnPropertyChangedFromPatchTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(_patchTools.Value.IsRunning)) return;

			if (!_patchTools.Value.IsRunning)
			{
				_cancelTokenSource.Dispose();
				_cancelTokenSource = new CancellationTokenSource();

				PatchText = Resource.String.patch;
				ClearDataEnabled = true;
				PatchEnabled = !IsPatched;
				UnpatchEnabled = IsPatched;
				return;
			}

			UnpatchEnabled = false;
			ClearDataEnabled = false;
			PatchText = Resource.String.abort;
		}

		private void OnPropertyChangedFromUnpatchTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(_unpatchTools.Value.IsRunning)) return;

			if (!_unpatchTools.Value.IsRunning)
			{
				_cancelTokenSource.Dispose();
				_cancelTokenSource = new CancellationTokenSource();

				UnpatchText = Resource.String.unpatch;
				ClearDataEnabled = true;
				PatchEnabled = !IsPatched;
				UnpatchEnabled = IsPatched;
				return;
			}

			PatchEnabled = false;
			ClearDataEnabled = false;
			UnpatchText = Resource.String.abort;
		}

		private void OnPropertyChangedFromManifestTools(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(_manifestTools.Value.IsRunning)) return;

			if (!_manifestTools.Value.IsRunning)
			{
				_cancelTokenSource.Dispose();
				_cancelTokenSource = new CancellationTokenSource();

				PatchText = Resource.String.patch;
				PatchEnabled = !IsPatched || _manifestTools.Value.IsPatchUpdateAvailable;
				UnpatchEnabled = IsPatched;
				ClearDataEnabled = true;
				return;
			}

			PatchEnabled = true;
			UnpatchEnabled = false;
			ClearDataEnabled = false;
			PatchText = Resource.String.abort;
		}

		public void PackageInstallerOnInstallFailed(object sender, EventArgs e)
		{
			if (!(sender is PackageInstaller installer)) return;
			installer.InstallFailed -= PackageInstallerOnInstallFailed;
			_installHandler?.NotifyInstallFailed();
		}

		private void OnStatusChangedFromModel(object sender, int e)
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
			if (Patching || Unpatching || ManifestToolsRunning) _cancelTokenSource.Cancel();
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
				_patchTools.Value.PropertyChanged += OnPropertyChangedFromPatchTools;
				_patchTools.Value.ErrorOccurred += OnErrorOccurredFromPatchTools;
				_patchToolsEventsSubscribed = true;
			}

			_patchTools.Value.Patch(CreateProcessState(), _manifestTools.Value.Manifest, _progress,
				_cancelTokenSource.Token);
		}

		private void OnErrorOccurredFromPatchTools(object sender, EventArgs e)
		{
			if (CanPatch && Patching) _cancelTokenSource.Cancel();
		}

		private void OnErrorOccurredFromManifestTools(object sender, EventArgs e)
		{
			if (!Patching && !Unpatching && ManifestToolsRunning && !_cancelTokenSource.IsCancellationRequested)
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
				_unpatchTools.Value.PropertyChanged += OnPropertyChangedFromUnpatchTools;
				_unpatchTools.Value.ErrorOccurred += OnErrorOccurredFromUnpatchTools;
				_unpatchToolsEventsSubscribed = true;
			}

			_unpatchTools.Value.Unpatch(CreateProcessState(), _progress, _cancelTokenSource.Token);
		}

		private void OnErrorOccurredFromUnpatchTools(object sender, EventArgs e)
		{
			if (CanUnpatch && Unpatching) _cancelTokenSource.Cancel();
		}

		public void ClearData()
		{
			Preferences.Clear();
			Preferences.Set(Prefkey.apk_is_patched.ToString(), false);
			Preferences.Set(Prefkey.backup_restore_savedata.ToString(), true);

			ClearOkkeiFiles();

			ProcessSavedataEnabled = true;
			PatchEnabled = true;
			UnpatchEnabled = false;
			Status = Resource.String.data_cleared;
		}

		private static void ClearOkkeiFiles()
		{
			if (Directory.Exists(OkkeiFilesPath)) FileUtils.RecursiveClearFiles(OkkeiFilesPath);
			FileUtils.DeleteIfExists(ManifestTools.ManifestPath);
			FileUtils.DeleteIfExists(ManifestTools.ManifestBackupPath);
		}

		protected override void OnCleared()
		{
			PropertyChanged = null;
			MessageGenerated = null;
			FatalErrorOccurred = null;
			InstallMessageGenerated = null;
			UninstallMessageGenerated = null;

			_manifestTools.Value.InstallMessageGenerated -= OnInstallMessageFromModel;
			_manifestTools.Value.StatusChanged -= OnStatusChangedFromModel;
			_manifestTools.Value.MessageGenerated -= OnMessageGeneratedFromModel;
			_manifestTools.Value.FatalErrorOccurred -= OnFatalErrorOccurredFromModel;
			_manifestTools.Value.ErrorOccurred -= OnErrorOccurredFromManifestTools;
			_manifestTools.Value.PropertyChanged -= OnPropertyChangedFromManifestTools;

			_patchTools.Value.InstallMessageGenerated -= OnInstallMessageFromModel;
			_patchTools.Value.UninstallMessageGenerated -= OnUninstallMessageFromModel;
			_patchTools.Value.StatusChanged -= OnStatusChangedFromModel;
			_patchTools.Value.MessageGenerated -= OnMessageGeneratedFromModel;
			_patchTools.Value.PropertyChanged -= OnPropertyChangedFromPatchTools;
			_patchTools.Value.ErrorOccurred -= OnErrorOccurredFromPatchTools;
			_patchToolsEventsSubscribed = false;

			_unpatchTools.Value.InstallMessageGenerated -= OnInstallMessageFromModel;
			_unpatchTools.Value.UninstallMessageGenerated -= OnUninstallMessageFromModel;
			_unpatchTools.Value.StatusChanged -= OnStatusChangedFromModel;
			_unpatchTools.Value.MessageGenerated -= OnMessageGeneratedFromModel;
			_unpatchTools.Value.PropertyChanged -= OnPropertyChangedFromUnpatchTools;
			_unpatchTools.Value.ErrorOccurred -= OnErrorOccurredFromUnpatchTools;
			_unpatchToolsEventsSubscribed = false;

			base.OnCleared();
		}
	}
}