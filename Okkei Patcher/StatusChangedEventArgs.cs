using System;

namespace OkkeiPatcher
{
	internal class StatusChangedEventArgs : EventArgs
	{
		public StatusChangedEventArgs(string info, MessageBox.Data data)
		{
			Info = info;
			MessageData = data;
		}

		public string Info { get; }
		public MessageBox.Data MessageData { get; }
	}
}