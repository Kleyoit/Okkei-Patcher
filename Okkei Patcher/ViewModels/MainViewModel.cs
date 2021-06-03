using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AndroidX.Lifecycle;
using OkkeiPatcher.Core;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using PropertyChanged;
using Xamarin.Essentials;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.ViewModels
{
	internal class MainViewModel : ViewModel, INotifyPropertyChanged
	{
		private readonly Lazy<Patcher> _patcher = new Lazy<Patcher>(() => new Patcher());
		private readonly Lazy<Unpatcher> _unpatcher = new Lazy<Unpatcher>(() => new Unpatcher());
		private readonly Lazy<ManifestTools> _manifestTools = new Lazy<ManifestTools>(() => new ManifestTools());
		private readonly Progress<ProgressInfo> _progress;
		private CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();
		private bool _patcherEventsSubscribed;
		private bool _unpatcherEventsSubscribed;
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
		public bool Patching => _patcher.IsValueCreated && _patcher.Value.IsRunning;
		public bool Unpatching => _unpatcher.IsValueCreated && _unpatcher.Value.IsRunning;
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
			if (e.PropertyName != nameof(_patcher.Value.IsRunning)) return;

			if (!_patcher.Value.IsRunning)
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
			if (e.PropertyName != nameof(_unpatcher.Value.IsRunning)) return;

			if (!_unpatcher.Value.IsRunning)
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
			_installHandler = _patcher.Value;
			_uninstallHandler = _patcher.Value;

			if (!_patcherEventsSubscribed)
			{
				_patcher.Value.InstallMessageGenerated += OnInstallMessageFromModel;
				_patcher.Value.UninstallMessageGenerated += OnUninstallMessageFromModel;
				_patcher.Value.StatusChanged += OnStatusChangedFromModel;
				_patcher.Value.MessageGenerated += OnMessageGeneratedFromModel;
				_patcher.Value.PropertyChanged += OnPropertyChangedFromPatchTools;
				_patcher.Value.ErrorOccurred += OnErrorOccurredFromPatchTools;
				_patcherEventsSubscribed = true;
			}

			_patcher.Value.Patch(CreateProcessState(), _manifestTools.Value.Manifest, _progress,
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
			_installHandler = _unpatcher.Value;
			_uninstallHandler = _unpatcher.Value;

			if (!_unpatcherEventsSubscribed)
			{
				_unpatcher.Value.InstallMessageGenerated += OnInstallMessageFromModel;
				_unpatcher.Value.UninstallMessageGenerated += OnUninstallMessageFromModel;
				_unpatcher.Value.StatusChanged += OnStatusChangedFromModel;
				_unpatcher.Value.MessageGenerated += OnMessageGeneratedFromModel;
				_unpatcher.Value.PropertyChanged += OnPropertyChangedFromUnpatchTools;
				_unpatcher.Value.ErrorOccurred += OnErrorOccurredFromUnpatchTools;
				_unpatcherEventsSubscribed = true;
			}

			_unpatcher.Value.Unpatch(CreateProcessState(), _progress, _cancelTokenSource.Token);
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

			_patcher.Value.InstallMessageGenerated -= OnInstallMessageFromModel;
			_patcher.Value.UninstallMessageGenerated -= OnUninstallMessageFromModel;
			_patcher.Value.StatusChanged -= OnStatusChangedFromModel;
			_patcher.Value.MessageGenerated -= OnMessageGeneratedFromModel;
			_patcher.Value.PropertyChanged -= OnPropertyChangedFromPatchTools;
			_patcher.Value.ErrorOccurred -= OnErrorOccurredFromPatchTools;
			_patcherEventsSubscribed = false;

			_unpatcher.Value.InstallMessageGenerated -= OnInstallMessageFromModel;
			_unpatcher.Value.UninstallMessageGenerated -= OnUninstallMessageFromModel;
			_unpatcher.Value.StatusChanged -= OnStatusChangedFromModel;
			_unpatcher.Value.MessageGenerated -= OnMessageGeneratedFromModel;
			_unpatcher.Value.PropertyChanged -= OnPropertyChangedFromUnpatchTools;
			_unpatcher.Value.ErrorOccurred -= OnErrorOccurredFromUnpatchTools;
			_unpatcherEventsSubscribed = false;

			base.OnCleared();
		}
	}
}