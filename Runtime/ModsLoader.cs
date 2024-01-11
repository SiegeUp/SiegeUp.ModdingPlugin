using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SiegeUp.ModdingPlugin
{
	public class ModsLoader
	{
		public static ModsLoader Instance { get; private set; }
		public readonly VersionInfo CurrentPluginVersion;
		public readonly VersionInfo CurrentGameVersion;
		public IReadOnlyList<AssetBundle> LoadedBundles => loadedBundles;
		public const string Version = "1.3.7";

        readonly List<AssetBundle> loadedBundles = new();

		public ModsLoader(string gameVersion)
		{
			Instance = this;
			CurrentGameVersion = new VersionInfo(gameVersion);
			CurrentPluginVersion = new VersionInfo(Version);
		}

		public List<SiegeUpModBase> LoadInstalledMods()
        {
			List<SiegeUpModBase> mods = new();

			var platform = Utils.GetCurrentPlatform();
			if (platform == PlatformShortName.Unsupported)
			{
				Debug.LogError("Unable to load mods for current platform");
				return mods;
			}
			foreach (var meta in FileUtils.GetInstalledModsMeta())
			{
                if (!meta.TryGetBuildInfo(platform, out SiegeUpModBundleInfo modBuildInfo))
                {
                    Debug.LogError($"Failed to load mod {meta.ModName}: failed to retrieve build information");
                    continue;
                }
                if (!CanLoadMod(modBuildInfo))
                {
                    Debug.LogError($"Failed to load {meta.ModName}: It is not compatible with current game version");
                    continue;
                }

				var mod = LoadBundle(FileUtils.GetBundlePath(meta, platform));
				if (mod == null)
					continue;
				mods.Add(mod);
			}
			return mods;
		}

		public SiegeUpModBase LoadBundle(string path)
		{
			var loadedAssetBundle = AssetBundle.LoadFromFile(path);
			if (loadedAssetBundle == null)
			{
				Debug.LogWarning($"Failed to load AssetBundle from {path}");
				return null;
			}
            var bundleAssets = loadedAssetBundle.LoadAllAssets();
            var modBases = bundleAssets.Cast<SiegeUpModBase>().Where(i => i);
            var mod = modBases.FirstOrDefault();
            if (!mod)
			{
				loadedAssetBundle.Unload(true);
				Debug.LogWarning($"Failed to load AssetBundle from {path} because it has no {nameof(SiegeUpModBase)} asset");
				return null;
			}
			loadedBundles.Add(loadedAssetBundle);
			return mod;
		}

		public void UnloadMods()
		{
			foreach (var bundle in loadedBundles)
				bundle.Unload(true);
			loadedBundles.Clear();
		}

		public bool CanLoadMod(SiegeUpModBundleInfo buildInfo)
		{
            bool supportsPluginVersion = CurrentPluginVersion.Supports(new VersionInfo(buildInfo.PluginVersion));
            bool supportsGameVersion  = CurrentGameVersion.Supports(new VersionInfo(buildInfo.GameVersion));

            if (!supportsPluginVersion)
                Debug.LogError($"Current Modding Plugin v.{CurrentPluginVersion} doesn't support Modding Plugin v.{buildInfo.PluginVersion}");
            if (!supportsGameVersion)
                Debug.LogError($"Current game version {CurrentPluginVersion} doesn't support mods for v.{buildInfo.PluginVersion}");

            return supportsPluginVersion && supportsGameVersion;
		}
	}
}
