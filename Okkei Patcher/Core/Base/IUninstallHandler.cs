using System;
using System.Threading;
using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Core.Base
{
	internal interface IUninstallHandler
	{
		public event EventHandler<UninstallMessageData> UninstallMessageGenerated;

		public void OnUninstallResult(IProgress<ProgressInfo> progress, CancellationToken token);
	}
}