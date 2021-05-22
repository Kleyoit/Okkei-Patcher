using System;
using System.Threading;
using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Patcher
{
	internal interface IInstallHandler
	{
		public void OnInstallSuccess(IProgress<ProgressInfo> progress, CancellationToken token);

		public void NotifyInstallFailed();
	}
}