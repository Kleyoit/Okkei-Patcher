using System;

namespace OkkeiPatcher.Patcher
{
	internal class PackageInstallObserver : Android.Content.PM.PackageInstaller.SessionCallback
	{
		public PackageInstallObserver(Android.Content.PM.PackageInstaller packageInstaller)
		{
			PackageInstaller = packageInstaller;
		}

		private Android.Content.PM.PackageInstaller PackageInstaller { get; }

		public event EventHandler<float> ProgressChanged;
		public event EventHandler InstallFailed;

		public override void OnActiveChanged(int sessionId, bool active)
		{
		}

		public override void OnBadgingChanged(int sessionId)
		{
		}

		public override void OnCreated(int sessionId)
		{
		}

		public override void OnFinished(int sessionId, bool success)
		{
			PackageInstaller.UnregisterSessionCallback(this);
			PackageInstaller.Dispose();
			if (!success) InstallFailed?.Invoke(this, EventArgs.Empty);
			InstallFailed = null;
			ProgressChanged = null;
		}

		public override void OnProgressChanged(int sessionId, float progress)
		{
			ProgressChanged?.Invoke(this, progress);
		}
	}
}