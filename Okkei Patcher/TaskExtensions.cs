using System;
using System.Threading.Tasks;

namespace OkkeiPatcher
{
	public static class TaskExtensions
	{
		public static async Task OnException(this Task task, Action<Exception> onException)
		{
			try
			{
				await task;
			}
			catch (Exception ex)
			{
				onException(ex);
			}
		}

		public static async Task<T> OnException<T>(this Task<T> task, Action<Exception> onException)
		{
			try
			{
				return await task;
			}
			catch (Exception ex)
			{
				onException(ex);
				return default;
			}
		}
	}
}