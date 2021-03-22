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
		protected readonly Lazy<Utils> UtilsInstance = new Lazy<Utils>(() => new Utils());
		protected bool IsRunningField;

		protected ToolsBase()
		{
			UtilsInstance.Value.ProgressChanged += UtilsOnProgressChanged;
			UtilsInstance.Value.MessageGenerated += UtilsOnMessageGenerated;
			UtilsInstance.Value.ErrorOccurred += UtilsOnErrorOccurred;
			UtilsInstance.Value.InstallFailed += UtilsOnInstallFailed;
		}

		public bool IsRunning
		{
			get => IsRunningField;
			protected set
			{
				if (value == IsRunningField) return;
				IsRunningField = value;
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

		public void WriteBugReport(Exception ex)
		{
			IsRunning = true;
			var bugReport = UtilsInstance.Value.GetBugReportText(ex);
			System.IO.File.WriteAllText(BugReportLogPath, bugReport);
			MessageGenerated?.Invoke(this,
				new MessageBox.Data(Application.Context.Resources.GetText(Resource.String.exception),
					Application.Context.Resources.GetText(Resource.String.exception_notice),
					Application.Context.Resources.GetText(Resource.String.dialog_exit), null,
					() => Environment.Exit(0), null));
		}

		protected bool CheckUninstallSuccess(bool scriptsUpdate)
		{
			if (!UtilsInstance.Value.IsAppInstalled(ChaosChildPackageName) || scriptsUpdate) return true;

			OnProgressChanged(this, new ProgressChangedEventArgs(0, 100, false));
			OnStatusChanged(this, Application.Context.Resources.GetText(Resource.String.aborted));
			OnMessageGenerated(this, new MessageBox.Data(
				Application.Context.Resources.GetText(Resource.String.error),
				Application.Context.Resources.GetText(Resource.String.uninstall_error),
				Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
				null, null));
			IsRunning = false;
			return false;
		}

		public void OnInstallSuccess(bool processSavedata, bool scriptsUpdate, bool obbUpdate, CancellationToken token)
		{
			Task.Run(() => Finish(processSavedata, scriptsUpdate, obbUpdate, token).OnException(WriteBugReport));
		}

		public void NotifyInstallFailed()
		{
			ProgressChanged?.Invoke(null, new ProgressChangedEventArgs(0, 100, false));
			StatusChanged?.Invoke(null, Application.Context.Resources.GetText(Resource.String.aborted));
			MessageGenerated?.Invoke(null, new MessageBox.Data(
				Application.Context.Resources.GetText(Resource.String.error),
				Application.Context.Resources.GetText(Resource.String.install_error),
				Application.Context.Resources.GetText(Resource.String.dialog_ok), null,
				null, null));
			IsRunning = false;
		}

		public abstract Task OnUninstallResult(Activity activity, bool scriptsUpdate, CancellationToken token);

		public abstract Task Finish(bool processSavedata, bool scriptsUpdate, bool obbUpdate, CancellationToken token);
	}
}