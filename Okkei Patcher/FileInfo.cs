using System.Text.Json.Serialization;

namespace OkkeiPatcher
{
	internal class FileInfo
	{
		[JsonInclude] public int Version { get; private set; }

		[JsonInclude] public string URL { get; private set; }

		[JsonInclude] public string MD5 { get; private set; }

		[JsonInclude] public long Size { get; private set; }
	}
}