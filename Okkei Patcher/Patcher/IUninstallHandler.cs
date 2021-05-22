using System;
using Android.App;
using System.Threading;
using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Patcher
{
	internal interface IUninstallHandler
	{
		public void OnUninstallResult(Activity activity, IProgress<ProgressInfo> progress, CancellationToken token);
	}
}