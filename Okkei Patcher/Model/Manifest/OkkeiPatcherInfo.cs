using System.Text.Json.Serialization;

namespace OkkeiPatcher.Model.Manifest
{
	internal class OkkeiPatcherInfo
	{
		[JsonInclude] public int Version { get; private set; }
		[JsonInclude] public string Changelog { get; private set; }
		[JsonInclude] public string URL { get; private set; }
		[JsonInclude] public string MD5 { get; private set; }
		[JsonInclude] public long Size { get; private set; }
	}
}