using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using static OkkeiPatcher.GlobalData;

namespace OkkeiPatcher
{
	internal abstract class ToolsBase : INotifyPropertyChanged
	{
		protected readonly Utils UtilsInstance;
		private bool _isRunningField;
		protected ProcessState ProcessState;

		protected ToolsBase(Utils utils)
		{
			UtilsInstance = utils;
			UtilsInstance.ProgressChanged += UtilsOnProgressChanged;
			UtilsInstance.MessageGenerated += UtilsOnMessageGenerated;
			UtilsInstance.ErrorOccurred += UtilsOnErrorOccurred;
			UtilsInstance.InstallFailed += UtilsOnInstallFailed;
		}

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
		public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
		public event EventHandler<MessageBox.Data> MessageGenerated;
		public event EventHandler ErrorOccurred;

		protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		protected virtual void OnStatusChanged(object sender, string e)
		{
			StatusChanged?.Invoke(sender, e);
		}

		protected virtual void OnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			ProgressChanged?.Invoke(sender, e);
		}

		protected virtual void OnMessageGenerated(object sender, MessageBox.Data e)
		{
			MessageGenerated?.Invoke(sender, e);
		}

		protected virtual void OnErrorOccurred(object sender, EventArgs e)
		{
			ErrorOccurred?.Invoke(sender, e);
		}

		private void UtilsOnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (IsRunning) ProgressChanged?.Invoke(this, e);
		}

		private void UtilsOnMessageGenerated(object sender, MessageBox.Data e)
		{
			if (IsRunning) MessageGenerated?.Invoke(this, e);
		}

		private void UtilsOnErrorOccurred(object sender, EventArgs e)
		{
			if (IsRunning) ErrorOccurred?.Invoke(this, e);
		}

		private void UtilsOnInstallFailed(object sender, EventArgs e)
		{
			NotifyInstallFailed();
		}

		protected virtual void ResetProgress()
		{
			OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
		}

		protected virtual void SetIndeterminateProgress()
		{
			OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, true));
		}

		protected virtual void UpdateProgress(int progress, int max, bool isIndeterminate)
		{
			OnProgressChanged(this, new ProgressChangedEventArgs(progress, max, isIndeterminate));
		}

		protected virtual void DisplayMessage(int titleId, int messageId, int positiveButtonTextId,
			int negativeButtonTextId,
			Action positiveAction,
			Action negativeAction)
		{
			var title = Utils.GetText(titleId);
			var message = Utils.GetText(messageId);
			var positiveButtonText = Utils.GetText(positiveButtonTextId);
			var negativeButtonText = Utils.GetText(negativeButtonTextId);
			OnMessageGenerated(this,
				new MessageBox.Data(title, message, positiveButtonText, negativeButtonText, positiveAction,
					negativeAction));
		}

		protected virtual void DisplayMessage(int titleId, int messageId, int buttonTextId, Action action)
		{
			var title = Utils.GetText(titleId);
			var message = Utils.GetText(messageId);
			var buttonText = Utils.GetText(buttonTextId);
			OnMessageGenerated(this, new MessageBox.Data(title, message, buttonText, null, action, null));
		}

		protected virtual void DisplayMessage(string title, string message, string buttonText, Action action)
		{
			OnMessageGenerated(this, new MessageBox.Data(title, message, buttonText, null, action, null));
		}

		protected virtual void DisplayMessage(string title, string message, string positiveButtonText,
			string negativeButtonText,
			Action positiveAction,
			Action negativeAction)
		{
			OnMessageGenerated(this,
				new MessageBox.Data(title, message, positiveButtonText, negativeButtonText, positiveAction,
					negativeAction));
		}

		protected virtual void UpdateStatus(int id)
		{
			OnStatusChanged(this, Utils.GetText(id));
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
			OnStatusChanged(this, Utils.GetText(Resource.String.aborted));
		}

		protected virtual void NotifyAboutError()
		{
			OnErrorOccurred(this, EventArgs.Empty);
		}

		protected void WriteBugReport(Exception ex)
		{
			IsRunning = true;
			var bugReport = Utils.GetBugReportText(ex);
			System.IO.File.WriteAllText(BugReportLogPath, bugReport);
			DisplayMessage(Resource.String.exception, Resource.String.exception_notice, Resource.String.dialog_exit,
				() => Environment.Exit(0));
		}

		protected bool CheckUninstallSuccess()
		{
			if (!Utils.IsAppInstalled(ChaosChildPackageName) || ProcessState.ScriptsUpdate) return true;

			ResetProgress();
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.uninstall_error, Resource.String.dialog_ok, null);
			IsRunning = false;
			return false;
		}

		public void OnUninstallResult(Activity activity, CancellationToken token)
		{
			Task.Run(() => OnUninstallResultProtected(activity, token).OnException(WriteBugReport));
		}

		public void OnInstallSuccess(CancellationToken token)
		{
			Task.Run(() => OnInstallSuccessProtected(token).OnException(WriteBugReport));
		}

		public void NotifyInstallFailed()
		{
			ResetProgress();
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.install_error, Resource.String.dialog_ok, null);
			IsRunning = false;
		}

		protected abstract Task OnUninstallResultProtected(Activity activity, CancellationToken token);

		protected abstract Task OnInstallSuccessProtected(CancellationToken token);
	}
}