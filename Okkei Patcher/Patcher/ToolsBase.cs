using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using OkkeiPatcher.Extensions;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher.Patcher
{
	internal abstract class ToolsBase : INotifyPropertyChanged
	{
		private bool _isRunningField;
		protected ProcessState ProcessState;

		public bool IsRunning
		{
			get => _isRunningField;
			protected set
			{
				if (value == _isRunningField) return;
				_isRunningField = value;
				NotifyPropertyChanged();
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
		public event EventHandler<string> StatusChanged;
		public event EventHandler<MessageData> MessageGenerated;
		public event EventHandler ErrorOccurred;

		protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		protected virtual void OnStatusChanged(object sender, string e)
		{
			StatusChanged?.Invoke(sender, e);
		}

		protected virtual void OnMessageGenerated(object sender, MessageData e)
		{
			MessageGenerated?.Invoke(sender, e);
		}

		protected virtual void OnErrorOccurred(object sender, EventArgs e)
		{
			ErrorOccurred?.Invoke(sender, e);
		}

		protected virtual void DisplayMessage(int titleId, int messageId, int positiveButtonTextId,
			int negativeButtonTextId,
			Action positiveAction,
			Action negativeAction)
		{
			var title = OkkeiUtils.GetText(titleId);
			var message = OkkeiUtils.GetText(messageId);
			var positiveButtonText = OkkeiUtils.GetText(positiveButtonTextId);
			var negativeButtonText = OkkeiUtils.GetText(negativeButtonTextId);
			OnMessageGenerated(this,
				new MessageData(title, message, positiveButtonText, negativeButtonText, positiveAction,
					negativeAction));
		}

		protected virtual void DisplayMessage(int titleId, int messageId, int buttonTextId, Action action)
		{
			var title = OkkeiUtils.GetText(titleId);
			var message = OkkeiUtils.GetText(messageId);
			var buttonText = OkkeiUtils.GetText(buttonTextId);
			OnMessageGenerated(this, new MessageData(title, message, buttonText, null, action, null));
		}

		protected virtual void DisplayMessage(string title, string message, string buttonText, Action action)
		{
			OnMessageGenerated(this, new MessageData(title, message, buttonText, null, action, null));
		}

		protected virtual void DisplayMessage(string title, string message, string positiveButtonText,
			string negativeButtonText,
			Action positiveAction,
			Action negativeAction)
		{
			OnMessageGenerated(this,
				new MessageData(title, message, positiveButtonText, negativeButtonText, positiveAction,
					negativeAction));
		}

		protected virtual void UpdateStatus(int id)
		{
			OnStatusChanged(this, OkkeiUtils.GetText(id));
		}

		protected virtual void UpdateStatus(string status)
		{
			OnStatusChanged(this, status);
		}

		protected virtual void ClearStatus()
		{
			OnStatusChanged(this, string.Empty);
		}

		protected virtual void SetStatusToAborted()
		{
			OnStatusChanged(this, OkkeiUtils.GetText(Resource.String.aborted));
		}

		protected virtual void NotifyAboutError()
		{
			OnErrorOccurred(this, EventArgs.Empty);
		}

		protected virtual void PackageInstallerOnInstallFailed(object sender, EventArgs e)
		{
			if (!(sender is PackageInstaller installer)) return;
			installer.InstallFailed -= PackageInstallerOnInstallFailed;
			NotifyInstallFailed();
		}

		protected void WriteBugReport(Exception ex)
		{
			IsRunning = true;
			var bugReport = DebugUtils.GetBugReportText(ex);
			System.IO.File.WriteAllText(BugReportLogPath, bugReport);
			DisplayMessage(Resource.String.exception, Resource.String.exception_notice, Resource.String.dialog_exit,
				() => Environment.Exit(0));
		}

		protected bool CheckUninstallSuccess(IProgress<ProgressInfo> progress)
		{
			if (!PackageManagerUtils.IsAppInstalled(ChaosChildPackageName) || ProcessState.ScriptsUpdate) return true;

			progress.Reset();
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.uninstall_error, Resource.String.dialog_ok, null);
			IsRunning = false;
			return false;
		}

		public void OnUninstallResult(Activity activity, IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalOnUninstallResult(activity, progress, token).OnException(WriteBugReport));
		}

		public void OnInstallSuccess(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalOnInstallSuccess(progress, token).OnException(WriteBugReport));
		}

		public void NotifyInstallFailed()
		{
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.install_error, Resource.String.dialog_ok, null);
			IsRunning = false;
		}

		protected abstract Task InternalOnUninstallResult(Activity activity, IProgress<ProgressInfo> progress,
			CancellationToken token);

		protected abstract Task InternalOnInstallSuccess(IProgress<ProgressInfo> progress, CancellationToken token);
	}
}