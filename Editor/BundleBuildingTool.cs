using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace SiegeUp.ModdingPlugin.Editor
{
    public static partial class Extentions
    {
        public static TaskAwaiter<PackageCollection> GetAwaiter(this ListRequest searchRequest)
        {
            var tcs = new TaskCompletionSource<PackageCollection>();
			Parallel.Invoke(() =>
            {
                while (!searchRequest.IsCompleted)
                {
					// Do nothing, wait until completed
                }
                tcs.SetResult(searchRequest.Result);
            });
            return tcs.Task.GetAwaiter();
        }
    }

	public class BundleBuildingTool
	{
		public static readonly Dictionary<BuildTarget, PlatformShortName> SupportedPlatforms = new Dictionary<BuildTarget, PlatformShortName>
		{
			{BuildTarget.StandaloneWindows, PlatformShortName.Windows },
			{BuildTarget.Android, PlatformShortName.Android },
			{BuildTarget.StandaloneLinux64, PlatformShortName.Linux },
			{BuildTarget.StandaloneOSX, PlatformShortName.MacOS },
			{BuildTarget.iOS, PlatformShortName.IOS },
		};

		public static async Task BuildAssetBundle(SiegeUpModBase modBase, params BuildTarget[] targetPlatforms)
		{
			if (!modBase.Validate())
				return;
			RegeneratePrefabIds(modBase);
			string modDirectory = FileUtils.GetExpectedModFolder(modBase.ModInfo);
			if (modDirectory == null)
				return;
			Debug.Log("Output directory: " + modDirectory);

			var map = new AssetBundleBuild[1];
			map[0].assetNames = new[] { AssetDatabase.GetAssetPath(modBase) };

			foreach (var platform in targetPlatforms)
			{
				map[0].assetBundleName = FileUtils.GetBundleFileName(modBase.ModInfo, SupportedPlatforms[platform]);
				await BuildAssetBundle(modBase, map, platform, modDirectory);
				FileUtils.CreateModMetaFile(modDirectory, modBase.ModInfo);
			}
			CreateModPackage(modBase, modDirectory);
			AssetDatabase.Refresh();
            CommandGameToReloadMods();
        }

        static void CommandGameToReloadMods()
        {
            UnityWebRequest.Get("http://localhost:9005/reload").SendWebRequest();
        }

		public static void CreateModPackage(SiegeUpModBase modBase, string outputFolder)
		{
            string tempFolder = FileUtil.GetUniqueTempPathInProject();
			Directory.CreateDirectory(tempFolder);
			foreach (string file in modBase.AllObjects.Select(x => AssetDatabase.GetAssetPath(x)))
			{
				File.Copy(file, Path.Combine(tempFolder, Path.GetFileName(file)));
				File.Copy(file+".meta", Path.Combine(tempFolder, Path.GetFileName(file) + ".meta"));
			}
			FileUtils.CreatePackageMetaFile(modBase.ModInfo, tempFolder);
			Client.Pack(tempFolder, outputFolder);
		}

        static async Task BuildAssetBundle(SiegeUpModBase modBase, AssetBundleBuild[] map, BuildTarget targetPlatform, string outputDir)
        {
            var packages = await Client.List();
            var package = packages.FirstOrDefault(i => i.name == "com.siegeup.reference");

            string gameVersion;
            if (package != null)
            {
                gameVersion = package?.version ?? Application.version;
                Debug.Log($"com.siegeup.reference package found successfully. Game version: {gameVersion}");
            }
            else
            {
                Debug.LogError("Can't find com.siegeup.reference package!");
                return;
            }

			modBase.ModInfo.TryGetBuildInfo(SupportedPlatforms[targetPlatform], out var prevBuildInfo);
			modBase.UpdateBuildInfo(SupportedPlatforms[targetPlatform], gameVersion);
			var manifest = BuildPipeline.BuildAssetBundles(outputDir, map, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.DeterministicAssetBundle, targetPlatform);
			if (manifest != null)
			{
				Debug.Log($"Mod \"{modBase.ModInfo.ModName}\" for \"{SupportedPlatforms[targetPlatform]}\" platform was builded successfully!");
				return;
			}
			modBase.UpdateBuildInfo(SupportedPlatforms[targetPlatform], prevBuildInfo);
		}

        static void RegeneratePrefabIds(SiegeUpModBase modBase)
		{
			const string prefabRefName = "PrefabRef";
            var objectsWithDeps = modBase.AllObjects
                .Distinct()
                .Where(x => x != null && AssetDatabase.GetAssetPath(x).EndsWith(".prefab"));
            foreach (var obj in objectsWithDeps)
			{
				var prefabRef = obj.GetComponent(prefabRefName);
				if (!prefabRef)
					throw new UnityException($"Please add {prefabRefName} component in object {obj.name}");
                string newGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefabRef.gameObject));
                prefabRef.GetType().GetMethod("ResetId").Invoke(prefabRef, new [] { newGuid });
			}
		}
	}
}