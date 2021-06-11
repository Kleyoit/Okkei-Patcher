using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OkkeiPatcher.Model.Manifest
{
	internal class OkkeiManifest
	{
		[JsonInclude] public int Version { get; private set; }
		[JsonInclude] public OkkeiPatcherInfo OkkeiPatcher { get; private set; }
		[JsonInclude] public Dictionary<Language, Dictionary<string, FileInfo>> Patches { get; private set; }
	}
}