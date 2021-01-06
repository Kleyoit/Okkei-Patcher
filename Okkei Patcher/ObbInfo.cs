using Newtonsoft.Json;

namespace OkkeiPatcher
{
	public class ObbInfo
	{
		[JsonProperty] public int Version { get; private set; }

		[JsonProperty] public string URL { get; private set; }

		[JsonProperty] public string MD5 { get; private set; }

		[JsonProperty] public long Size { get; private set; }
	}
}