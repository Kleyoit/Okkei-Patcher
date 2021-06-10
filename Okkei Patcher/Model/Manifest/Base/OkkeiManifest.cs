using System.Text.Json.Serialization;

namespace OkkeiPatcher.Model.Manifest.Base
{
	internal class OkkeiManifest
	{
		[JsonInclude] public int Version { get; protected set; }
		[JsonInclude] public OkkeiPatcherInfo OkkeiPatcher { get; protected set; }
	}
}