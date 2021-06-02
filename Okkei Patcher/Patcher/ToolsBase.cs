using System;
using System.ComponentModel;
using System.IO;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Patcher
{
	internal class ToolsBase : INotifyPropertyChanged
	{
		protected const string ChaosChildPackageName = "com.mages.chaoschild_jp";
		private static readonly string BugReportLogPath = Path.Combine(OkkeiFilesPath, "bugreport.log");
		protected ProcessState ProcessState;

		public bool IsRunning { get; protected set; }

		public event PropertyChangedEventHandler PropertyChanged;
		public event EventHandler<int> StatusChanged;
		public event EventHandler<MessageData> MessageGenerated;
		public event EventHandler<MessageData> FatalErrorOccurred;
		public event EventHandler ErrorOccurred;

		protected virtual void OnStatusChanged(object sender, int e)
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
			int negativeButtonTextId)
		{
			OnMessageGenerated(this, new MessageData(titleId, messageId, positiveButtonTextId, negativeButtonTextId));
		}

		protected virtual void DisplayMessage(int titleId, int messageId, int buttonTextId)
		{
			OnMessageGenerated(this, new MessageData(titleId, messageId, buttonTextId));
		}

		protected virtual void DisplayMessage(int titleId, int messageId, int buttonTextId,
			string error)
		{
			OnMessageGenerated(this, new MessageData(titleId, messageId, buttonTextId, 0, error));
		}

		protected virtual void DisplayErrorMessage(int titleId, int messageId, int buttonTextId,
			string error)
		{
			OnMessageGenerated(this, new MessageData(titleId, messageId, buttonTextId, 0, error));
			NotifyAboutError();
		}

		protected virtual void DisplayErrorMessage(int titleId, int messageId, int buttonTextId)
		{
			DisplayMessage(titleId, messageId, buttonTextId);
			NotifyAboutError();
		}

		protected virtual void DisplayFatalErrorMessage(int titleId, int messageId, int buttonTextId)
		{
			var data = new MessageData(titleId, messageId, buttonTextId);
			FatalErrorOccurred?.Invoke(this, data);
			NotifyAboutError();
		}

		protected virtual void UpdateStatus(int id)
		{
			OnStatusChanged(this, id);
		}

		protected virtual void ClearStatus()
		{
			OnStatusChanged(this, Resource.String.empty);
		}

		protected virtual void SetStatusToAborted()
		{
			OnStatusChanged(this, Resource.String.aborted);
		}

		protected virtual void NotifyAboutError()
		{
			OnErrorOccurred(this, EventArgs.Empty);
		}

		protected void WriteBugReport(Exception ex)
		{
			IsRunning = true;
			var bugReport = DebugUtils.GetBugReportText(ex);
			File.WriteAllText(BugReportLogPath, bugReport);
			DisplayFatalErrorMessage(Resource.String.exception, Resource.String.exception_notice,
				Resource.String.dialog_exit);
		}
	}
}