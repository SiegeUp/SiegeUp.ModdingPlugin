using System;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#endif

namespace SiegeUp.ModdingPlugin.DevUtils
{
	[ExecuteInEditMode]
	public class PluginVersionChecker : ScriptableObject
	{
        static DateTime _lastUpdateTime = DateTime.MinValue;
        static string _pluginManifestPath;
        const string _pluginPackageName = "com.siegeup.moddingplugin";
        const string _manifestFileName = @"package.json";
        const int UpdatePeriodSec = 2;

#if UNITY_EDITOR
		[InitializeOnLoadMethod]
        static void Init()
		{
			var res = AssetDatabase.FindAssets("package")
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(x => AssetDatabase.LoadAssetAtPath<TextAsset>(x) != null)
				.Select(PackageInfo.FindForAssetPath)
				.FirstOrDefault(x => x?.name == _pluginPackageName);
			if (res == null)
				return;
			_pluginManifestPath = Path.Combine(res.source == PackageSource.Local ? res.resolvedPath : res.assetPath, _manifestFileName);
			if (!File.Exists(_pluginManifestPath))
				return;
			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.update += OnEditorUpdate;
		}

        static void OnEditorUpdate()
		{
			if ((DateTime.UtcNow - _lastUpdateTime).TotalSeconds < UpdatePeriodSec)
				return;
			_lastUpdateTime = DateTime.UtcNow;
			var versionInManifest = GetPluginVersionFromManifest();
			if (ModsLoader.Version != versionInManifest)
				Debug.LogError($"Don't forget to update plugin version!\n" +
					$"Manifest ver: {versionInManifest}. ModsLoader ver: {ModsLoader.Version}");
		}

        static string GetPluginVersionFromManifest()
		{
			var data = File.ReadAllLines(_pluginManifestPath);
			var versionInfo = data.FirstOrDefault(x => x.Contains("\"version\":"));
			return GetVersionFromJsonString(versionInfo);
		}

        static string GetVersionFromJsonString(string infoLine)
		{
			infoLine = infoLine.Replace(",", "");
			int separatorIndex = infoLine.LastIndexOf(':');
			return infoLine.Substring(separatorIndex + 3, infoLine.Length - separatorIndex - 4);
		}
#endif
	}
}