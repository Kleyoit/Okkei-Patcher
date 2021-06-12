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
		private Dictionary<string, FileInfo> PatchFiles => Manifest?.Patches[Language.English];
		private FileInfo Scripts => PatchFiles?[PatchFile.Scripts.ToString()];
		private FileInfo Obb => PatchFiles?[PatchFile.Obb.ToString()];

		public override IPatchUpdates PatchUpdates =>
			new PatchUpdates(IsScriptsUpdateAvailable(), IsObbUpdateAvailable());

		public override int PatchSizeInMb
		{
			get
			{
				if (!PatchUpdates.Available)
				{
					var scriptsSize = (int) Math.Round(Scripts.Size / (double) 0x100000);
					var obbSize = (int) Math.Round(Obb.Size / (double) 0x100000);
					return scriptsSize + obbSize;
				}

				int scriptsUpdateSize = IsScriptsUpdateAvailable()
					? (int) Math.Round(Scripts.Size / (double) 0x100000)
					: 0;
				int obbUpdateSize = IsObbUpdateAvailable()
					? (int) Math.Round(Obb.Size / (double) 0x100000)
					: 0;
				return scriptsUpdateSize + obbUpdateSize;
			}
		}

		private bool IsScriptsUpdateAvailable()
		{
			if (!Preferences.Get(AppPrefkey.apk_is_patched.ToString(), false)) return false;

			if (!Preferences.ContainsKey(FileVersionPrefkey.scripts_version.ToString()))
				Preferences.Set(FileVersionPrefkey.scripts_version.ToString(), 1);
			int currentScriptsVersion = Preferences.Get(FileVersionPrefkey.scripts_version.ToString(), 1);
			var manifestScriptsVersion = 0;
			if (Scripts != null) manifestScriptsVersion = Scripts.Version;
			return manifestScriptsVersion > currentScriptsVersion;
		}

		private bool IsObbUpdateAvailable()
		{
			if (!Preferences.Get(AppPrefkey.apk_is_patched.ToString(), false)) return false;

			if (!Preferences.ContainsKey(FileVersionPrefkey.obb_version.ToString()))
				Preferences.Set(FileVersionPrefkey.obb_version.ToString(), 1);
			int currentObbVersion = Preferences.Get(FileVersionPrefkey.obb_version.ToString(), 1);
			var manifestObbVersion = 0;
			if (Obb != null) manifestObbVersion = Obb.Version;
			return manifestObbVersion > currentObbVersion;
		}
	}
}