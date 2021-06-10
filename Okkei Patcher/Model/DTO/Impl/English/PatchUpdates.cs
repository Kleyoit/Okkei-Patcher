using OkkeiPatcher.Model.DTO.Base;

namespace OkkeiPatcher.Model.DTO.Impl.English
{
	internal class PatchUpdates : IPatchUpdates
	{
		public PatchUpdates(bool scriptsUpdate, bool obbUpdate)
		{
			ScriptsUpdate = scriptsUpdate;
			ObbUpdate = obbUpdate;
		}

		public bool ScriptsUpdate { get; }
		public bool ObbUpdate { get; }
		public bool Available => ScriptsUpdate || ObbUpdate;
	}
}