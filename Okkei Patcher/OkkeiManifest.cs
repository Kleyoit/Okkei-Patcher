using System.Text.Json.Serialization;

namespace OkkeiPatcher
{
	internal class OkkeiManifest
	{
		[JsonInclude] public int Version { get; private set; }

		[JsonInclude] public OkkeiPatcherInfo OkkeiPatcher { get; private set; }

		[JsonInclude] public FileInfo Scripts { get; private set; }

		[JsonInclude] public FileInfo Obb { get; private set; }
	}
}