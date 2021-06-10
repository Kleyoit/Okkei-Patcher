using OkkeiPatcher.Model.DTO.Base;

namespace OkkeiPatcher.Model.DTO
{
	internal readonly struct ProcessState
	{
		public ProcessState(bool processSavedata, IPatchUpdates patchUpdates)
		{
			ProcessSavedata = processSavedata;
			PatchUpdates = patchUpdates;
		}

		public bool ProcessSavedata { get; }
		public IPatchUpdates PatchUpdates { get; }
	}
}