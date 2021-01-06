using Newtonsoft.Json;

namespace OkkeiPatcher
{
	public class OkkeiManifest
	{
		[JsonProperty]
		public int Version { get; private set; }

		[JsonProperty]
		public OkkeiPatcherInfo OkkeiPatcher { get; private set; }

		[JsonProperty]
		public ScriptsInfo Scripts { get; private set; }

		[JsonProperty]
		public ObbInfo Obb { get; private set; }

		private OkkeiManifest() { }
	}
}