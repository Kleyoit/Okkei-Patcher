using System.Threading;

namespace OkkeiPatcher.Extensions
{
	public static class CancellationTokenExtensions
	{
		public static void Throw(this CancellationToken token)
		{
			throw new System.OperationCanceledException("The operation was canceled.", token);
		}
	}
}