using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using static OkkeiPatcher.Model.GlobalData;

namespace OkkeiPatcher.Patcher
{
	internal class ToolsBase : INotifyPropertyChanged
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

		protected void WriteBugReport(Exception ex)
		{
			IsRunning = true;
			var bugReport = DebugUtils.GetBugReportText(ex);
			System.IO.File.WriteAllText(BugReportLogPath, bugReport);
			DisplayMessage(Resource.String.exception, Resource.String.exception_notice, Resource.String.dialog_exit,
				() => Environment.Exit(0));
		}
	}
}