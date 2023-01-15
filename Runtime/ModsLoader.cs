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
				if (!meta.TryGetBuildInfo(platform, out SiegeUpModBundleInfo buildInfo) || !CanLoad(buildInfo))
					continue;
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
			var bundleAssets = loadedAssetBundle.LoadAllAssets().Cast<SiegeUpModBase>().Where(i => i);

            var mod = bundleAssets.FirstOrDefault();
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

		public bool CanLoad(SiegeUpModBundleInfo buildInfo)
		{
			return CurrentPluginVersion.Supports(new VersionInfo(buildInfo.PluginVersion))
				&& CurrentGameVersion.Supports(new VersionInfo(buildInfo.GameVersion));
		}
	}
}
