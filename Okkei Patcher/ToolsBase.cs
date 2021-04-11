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
		protected ProcessState ProcessState;
		private bool _isRunningField;

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

		protected void ResetProgress()
		{
			OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
		}

		protected void SetIndeterminateProgress()
		{
			OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, true));
		}

		protected void ClearStatus()
		{
			OnStatusChanged(this, string.Empty);
		}

		protected void SetStatusToAborted()
		{
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
		}

		protected void NotifyAboutError()
		{
			OnErrorOccurred(this, EventArgs.Empty);
		}

		protected void WriteBugReport(Exception ex)
		{
			IsRunning = true;
			var bugReport = Utils.GetBugReportText(ex);
			System.IO.File.WriteAllText(BugReportLogPath, bugReport);
			MessageGenerated?.Invoke(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.exception),
					Application.Context.Resources.GetText(Resource.String.exception_notice),
					Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
					() => Environment.Exit(0), null));
		}

		protected bool CheckUninstallSuccess()
		{
			if (!Utils.IsAppInstalled(ChaosChildPackageName) || ProcessState.ScriptsUpdate) return true;

			ResetProgress();
			SetStatusToAborted();
			OnMessageGenerated(this, new MessageBox.Data(
				Application.Context.Resources.GetText(Resource.String.error),
				Application.Context.Resources.GetText(Resource.String.uninstall_error),
				Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
				null, null));
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
			ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(0, 100, false));
			StatusChanged?.Invoke(this, Application.Context.Resources.GetText(Resource.String.aborted));
			MessageGenerated?.Invoke(this, new MessageBox.Data(
				Application.Context.Resources.GetText(Resource.String.error),
				Application.Context.Resources.GetText(Resource.String.install_error),
				Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
				null, null));
			IsRunning = false;
		}

		protected abstract Task OnUninstallResultProtected(Activity activity, CancellationToken token);

		protected abstract Task OnInstallSuccessProtected(CancellationToken token);
	}
}