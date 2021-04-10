namespace OkkeiPatcher
{
	internal readonly struct ProcessState
	{
		public ProcessState(bool processSavedata, bool scriptsUpdate, bool obbUpdate)
		{
			ProcessSavedata = processSavedata;
			ScriptsUpdate = scriptsUpdate;
			ObbUpdate = obbUpdate;
		}

		public bool ProcessSavedata { get; }
		public bool ScriptsUpdate { get; }
		public bool ObbUpdate { get; }
		public bool PatchUpdate => ScriptsUpdate || ObbUpdate;
	}
}