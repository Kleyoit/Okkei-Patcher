using System;

namespace OkkeiPatcher
{
	internal static class ManifestTasks
	{
		private static Lazy<BaseManifestTasks> _instance;

		public static bool IsInstantiated => _instance.IsValueCreated;

		public static BaseManifestTasks Instance => _instance.Value;

		public static void SetInstanceFactory(Func<BaseManifestTasks> factory)
		{
			_instance = new Lazy<BaseManifestTasks>(factory);
		}
	}
}