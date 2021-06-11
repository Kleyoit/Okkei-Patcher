using OkkeiPatcher.Model.DTO.Base;

namespace OkkeiPatcher.Model.DTO.Impl.English
{
	internal class PatchUpdates : IPatchUpdates
	{
		public PatchUpdates(bool scripts, bool obb)
		{
			Scripts = scripts;
			Obb = obb;
		}

		public bool Scripts { get; }
		public bool Obb { get; }
		public bool Available => Scripts || Obb;
	}
}