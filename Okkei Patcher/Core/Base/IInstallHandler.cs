using System;
using System.Threading;
using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Core.Base
{
	internal interface IInstallHandler
	{
		public event EventHandler<InstallMessageData> InstallMessageGenerated;

		public void OnInstallSuccess(IProgress<ProgressInfo> progress, CancellationToken token);

		public void NotifyInstallFailed();
	}
}