using System.Text.Json.Serialization;

namespace OkkeiPatcher
{
	public class OkkeiManifest
	{
		[JsonInclude] public int Version { get; private set; }

		[JsonInclude] public OkkeiPatcherInfo OkkeiPatcher { get; private set; }

		[JsonInclude] public ScriptsInfo Scripts { get; private set; }

		[JsonInclude] public ObbInfo Obb { get; private set; }
	}
}