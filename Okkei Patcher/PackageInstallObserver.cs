using System;
using Android.Content.PM;

namespace OkkeiPatcher
{
	internal class PackageInstallObserver : PackageInstaller.SessionCallback
	{
		public PackageInstallObserver(PackageInstaller packageInstaller)
		{
			PackageInstaller = packageInstaller;
		}

		private PackageInstaller PackageInstaller { get; }

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
		}

		public override void OnProgressChanged(int sessionId, float progress)
		{
		}
	}
}