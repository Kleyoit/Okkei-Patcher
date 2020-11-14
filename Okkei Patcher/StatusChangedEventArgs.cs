using System;

namespace OkkeiPatcher
{
	internal class StatusChangedEventArgs : EventArgs
	{
		public string Info { get; }
		public MessageBox.Data MessageData { get; }

		public StatusChangedEventArgs(string info, MessageBox.Data data)
		{
			Info = info;
			MessageData = data;
		}


	}
}