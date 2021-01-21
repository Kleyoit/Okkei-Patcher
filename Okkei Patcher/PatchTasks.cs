using System;

namespace OkkeiPatcher
{
	internal static class PatchTasks
	{
		private static Lazy<BasePatchTasks> _instance;

		public static bool IsInstantiated => _instance.IsValueCreated;

		public static BasePatchTasks Instance => _instance.Value;

		public static void SetInstanceFactory(Func<BasePatchTasks> factory)
		{
			_instance = new Lazy<BasePatchTasks>(factory);
		}
	}
}