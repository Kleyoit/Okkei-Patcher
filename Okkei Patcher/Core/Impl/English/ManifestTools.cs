using System;
using System.Collections.Generic;
using OkkeiPatcher.Model;
using OkkeiPatcher.Model.DTO.Base;
using OkkeiPatcher.Model.DTO.Impl.English;
using OkkeiPatcher.Model.Manifest;
using Xamarin.Essentials;

namespace OkkeiPatcher.Core.Impl.English
{
	internal class ManifestTools : Base.ManifestTools
	{
		private Dictionary<string, FileInfo> PatchFiles => Manifest.Patches[Language.English];

		public override IPatchUpdates PatchUpdates =>
			new PatchUpdates(IsScriptsUpdateAvailable(), IsObbUpdateAvailable());

		public override int PatchSizeInMb
		{
			get
			{
				if (!PatchUpdates.Available)
				{
					var scriptsSize =
						(int) Math.Round(PatchFiles[PatchFile.Scripts.ToString()].Size / (double) 0x100000);
					var obbSize = (int) Math.Round(PatchFiles[PatchFile.Obb.ToString()].Size / (double) 0x100000);
					return scriptsSize + obbSize;
				}

				int scriptsUpdateSize = IsScriptsUpdateAvailable()
					? (int) Math.Round(PatchFiles[PatchFile.Scripts.ToString()].Size / (double) 0x100000)
					: 0;
				int obbUpdateSize = IsObbUpdateAvailable()
					? (int) Math.Round(PatchFiles[PatchFile.Obb.ToString()].Size / (double) 0x100000)
					: 0;
				return scriptsUpdateSize + obbUpdateSize;
			}
		}

		private bool IsScriptsUpdateAvailable()
		{
			if (!Preferences.Get(AppPrefkey.apk_is_patched.ToString(), false)) return false;

			if (!Preferences.ContainsKey(FileVersionPrefkey.scripts_version.ToString()))
				Preferences.Set(FileVersionPrefkey.scripts_version.ToString(), 1);
			int scriptsVersion = Preferences.Get(FileVersionPrefkey.scripts_version.ToString(), 1);
			return PatchFiles[PatchFile.Scripts.ToString()].Version > scriptsVersion;
		}

		private bool IsObbUpdateAvailable()
		{
			if (!Preferences.Get(AppPrefkey.apk_is_patched.ToString(), false)) return false;

			if (!Preferences.ContainsKey(FileVersionPrefkey.obb_version.ToString()))
				Preferences.Set(FileVersionPrefkey.obb_version.ToString(), 1);
			int obbVersion = Preferences.Get(FileVersionPrefkey.obb_version.ToString(), 1);
			return PatchFiles[PatchFile.Obb.ToString()].Version > obbVersion;
		}
	}
}