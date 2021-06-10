using System;
using System.Threading;
using System.Threading.Tasks;
using OkkeiPatcher.Model.DTO;
using OkkeiPatcher.Model.DTO.Base;
using OkkeiPatcher.Model.DTO.Impl.English;
using OkkeiPatcher.Model.Manifest.Impl.English;
using OkkeiPatcher.Utils.Extensions;
using Xamarin.Essentials;

namespace OkkeiPatcher.Core.Impl.English
{
	internal class ManifestTools : Base.ManifestTools
	{
		public ManifestTools()
		{
			ManifestUrl = "https://raw.githubusercontent.com/ForrrmerBlack/okkei-patcher/master/Manifest.json";
		}

		private OkkeiManifest ManifestImpl => Manifest as OkkeiManifest;

		public override IPatchUpdates PatchUpdates =>
			new PatchUpdates(IsScriptsUpdateAvailable(), IsObbUpdateAvailable());

		public override int PatchSizeInMb
		{
			get
			{
				if (!PatchUpdates.Available)
				{
					var scriptsSize = (int) Math.Round(ManifestImpl.Scripts.Size / (double) 0x100000);
					var obbSize = (int) Math.Round(ManifestImpl.Obb.Size / (double) 0x100000);
					return scriptsSize + obbSize;
				}

				int scriptsUpdateSize = IsScriptsUpdateAvailable()
					? (int) Math.Round(ManifestImpl.Scripts.Size / (double) 0x100000)
					: 0;
				int obbUpdateSize = IsObbUpdateAvailable()
					? (int) Math.Round(ManifestImpl.Obb.Size / (double) 0x100000)
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
			return ManifestImpl.Scripts.Version > scriptsVersion;
		}

		private bool IsObbUpdateAvailable()
		{
			if (!Preferences.Get(AppPrefkey.apk_is_patched.ToString(), false)) return false;

			if (!Preferences.ContainsKey(FileVersionPrefkey.obb_version.ToString()))
				Preferences.Set(FileVersionPrefkey.obb_version.ToString(), 1);
			int obbVersion = Preferences.Get(FileVersionPrefkey.obb_version.ToString(), 1);
			return ManifestImpl.Obb.Version > obbVersion;
		}

		protected override bool VerifyManifest(Model.Manifest.Base.OkkeiManifest manifest)
		{
			return
				manifest is OkkeiManifest manifestImpl &&
				manifestImpl.Version != 0 &&
				manifestImpl.OkkeiPatcher != null &&
				manifestImpl.OkkeiPatcher.Version != 0 &&
				manifestImpl.OkkeiPatcher.Changelog != null &&
				manifestImpl.OkkeiPatcher.URL != null &&
				manifestImpl.OkkeiPatcher.MD5 != null &&
				manifestImpl.OkkeiPatcher.Size != 0 &&
				manifestImpl.Scripts != null &&
				manifestImpl.Scripts.Version != 0 &&
				manifestImpl.Scripts.URL != null &&
				manifestImpl.Scripts.MD5 != null &&
				manifestImpl.Scripts.Size != 0 &&
				manifestImpl.Obb != null &&
				manifestImpl.Obb.Version != 0 &&
				manifestImpl.Obb.URL != null &&
				manifestImpl.Obb.MD5 != null &&
				manifestImpl.Obb.Size != 0;
		}

		public override async Task<bool> RetrieveManifestAsync(IProgress<ProgressInfo> progress,
			CancellationToken token)
		{
			return await InternalRetrieveManifestAsync<OkkeiManifest>(progress, token).OnException(WriteBugReport);
		}
	}
}