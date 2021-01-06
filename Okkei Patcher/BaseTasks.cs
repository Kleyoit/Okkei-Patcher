using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OkkeiPatcher
{
	internal class BaseTasks : INotifyPropertyChanged
	{
		protected bool _isRunning;

		protected BaseTasks()
		{
			Utils.StatusChanged += UtilsOnStatusChanged;
			Utils.ProgressChanged += UtilsOnProgressChanged;
			Utils.MessageGenerated += UtilsOnMessageGenerated;
			Utils.TokenErrorOccurred += UtilsOnTokenErrorOccurred;
			Utils.TaskErrorOccurred += UtilsOnTaskErrorOccurred;
		}

		public bool IsRunning
		{
			get => _isRunning;
			protected set
			{
				if (value != _isRunning)
				{
					_isRunning = value;
					NotifyPropertyChanged();
				}
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

		private void UtilsOnStatusChanged(object sender, string e)
		{
			if (IsRunning) StatusChanged?.Invoke(this, e);
		}

		private void UtilsOnProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (IsRunning) ProgressChanged?.Invoke(this, e);
		}

		private void UtilsOnMessageGenerated(object sender, MessageBox.Data e)
		{
			if (IsRunning) MessageGenerated?.Invoke(this, e);
		}

		private void UtilsOnTokenErrorOccurred(object sender, EventArgs e)
		{
			if (IsRunning) ErrorOccurred?.Invoke(this, e);
		}

		private void UtilsOnTaskErrorOccurred(object sender, EventArgs e)
		{
			if (IsRunning) IsRunning = false;
		}
	}
}