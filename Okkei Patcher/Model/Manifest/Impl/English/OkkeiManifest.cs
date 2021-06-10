using System.Text.Json.Serialization;

namespace OkkeiPatcher.Model.Manifest.Impl.English
{
	internal class OkkeiManifest : Base.OkkeiManifest
	{
		[JsonInclude] public FileInfo Scripts { get; private set; }
		[JsonInclude] public FileInfo Obb { get; private set; }
	}
}