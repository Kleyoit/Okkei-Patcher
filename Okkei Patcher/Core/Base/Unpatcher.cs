using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Utils;
using OkkeiPatcher.Utils.Extensions;

namespace OkkeiPatcher.Core.Base
{
	internal abstract class Unpatcher : ToolsBase, IInstallHandler, IUninstallHandler
	{
		public event EventHandler<InstallMessageData> InstallMessageGenerated;
		public event EventHandler<UninstallMessageData> UninstallMessageGenerated;

		public void NotifyInstallFailed()
		{
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.install_error, Resource.String.dialog_ok);
			IsRunning = false;
		}

		public void OnInstallSuccess(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalOnInstallSuccessAsync(progress, token).OnException(WriteBugReport));
		}

		public void OnUninstallResult(IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalOnUninstallResultAsync(progress, token).OnException(WriteBugReport));
		}

		protected abstract Task InternalOnInstallSuccessAsync(IProgress<ProgressInfo> progress,
			CancellationToken token);

		public void Unpatch(ProcessState processState, IProgress<ProgressInfo> progress, CancellationToken token)
		{
			Task.Run(() => InternalUnpatchAsync(processState, progress, token).OnException(WriteBugReport));
		}

		protected abstract Task InternalUnpatchAsync(ProcessState processState, IProgress<ProgressInfo> progress,
			CancellationToken token);

		protected void OnUninstallFail(IProgress<ProgressInfo> progress)
		{
			progress.Reset();
			SetStatusToAborted();
			DisplayMessage(Resource.String.error, Resource.String.uninstall_error, Resource.String.dialog_ok);
			IsRunning = false;
		}

		protected abstract Task InternalOnUninstallResultAsync(IProgress<ProgressInfo> progress,
			CancellationToken token);

		protected void DisplayUninstallMessage(int titleId, int messageId, int buttonTextId, string packageName)
		{
			var data = MessageDataUtils.CreateUninstallMessageData(titleId, messageId, buttonTextId, packageName);
			UninstallMessageGenerated?.Invoke(this, data);
		}

		protected void DisplayInstallMessage(int titleId, int messageId, int buttonTextId, string filePath)
		{
			var data = MessageDataUtils.CreateInstallMessageData(titleId, messageId, buttonTextId, filePath);
			InstallMessageGenerated?.Invoke(this, data);
		}
	}
}