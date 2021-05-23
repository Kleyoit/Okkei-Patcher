using System;
using System.Threading;
using Android.App;
using OkkeiPatcher.Model.DTO;

namespace OkkeiPatcher.Patcher
{
	internal interface IUninstallHandler
	{
		public void OnUninstallResult(Activity activity, IProgress<ProgressInfo> progress, CancellationToken token);
	}
}