﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SiegeUp.ModdingPlugin
{
	public class FileUtils
	{
		public const string ModsFolderName = "mods";
		public const string MetaFileName = "meta.json";
		public const string PackageMetaFileName = "package.json";

		public static string GetDefaultGameFolder()
		{
			return Path.Combine(
				   Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				   "..",
				   "LocalLow",
				   "Abuksigun",
				   "SiegeUp!");
		}

		public static string GetDefaultModsFolder()
		{
			string gameFolder = GetDefaultGameFolder();
			if (!Directory.Exists(gameFolder))
				return null;
			string modsFolder = Path.Combine(gameFolder, ModsFolderName);
			CheckOrCreateDirectory(modsFolder);
			return modsFolder;
		}

		public static void OpenExplorer(string path)
		{
			if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
				System.Diagnostics.Process.Start("explorer.exe", path);
			else
				Debug.LogWarning($"Unable to open {path}");
		}

		public static string FixPathSeparator(string path)
		{
			if (path.Contains(Path.AltDirectorySeparatorChar.ToString()))
				return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return path;
		}

		public static void CheckOrCreateDirectory(string path)
		{
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);
		}

		public static string GetModsFolder()
		{
			string path = "";
#if UNITY_EDITOR
			path = SiegeUpModdingPluginConfig.Instance.ModsFolder;
#else
			path = Path.Combine(Application.persistentDataPath, ModsFolderName);
#endif
			CheckOrCreateDirectory(path);
			return path;
		}

		public static string GetExpectedModFolder(SiegeUpModMeta modMeta)
		{
#if UNITY_EDITOR
			if (!SiegeUpModdingPluginConfig.Instance.IsValidModsFolder)
				return null;
#endif
			string path = Path.Combine(GetModsFolder(), $"{modMeta.ModName}_{modMeta.Id}");
			CheckOrCreateDirectory(path);
			return path;
		}

		public static void CreateModMetaFile(string modDirectory, SiegeUpModMeta modInfo)
		{
			string manifestData = JsonUtility.ToJson(modInfo, true);
			string filePath = Path.Combine(modDirectory, MetaFileName);
			File.WriteAllText(filePath, manifestData);
		}

		public static bool IsValidFolderName(string name)
		{
			return name.IndexOfAny(Path.GetInvalidPathChars()) == -1;
		}

		public static string GetBundleFileName(SiegeUpModMeta modMeta, PlatformShortName platform)
		{
            var path = $"{platform.ToString().ToLower()}_{modMeta.Id}.assetbundle";
            Debug.Log($"BundlePath: {path}");
            return path;
        }

		public static string GetBundlePath(SiegeUpModMeta modMeta, PlatformShortName platform)
		{
			return Path.Combine(GetExpectedModFolder(modMeta), GetBundleFileName(modMeta, platform));
		}

		public static IEnumerable<SiegeUpModMeta> GetInstalledModsMeta()
		{
			var metaFiles = Directory.GetFiles(GetModsFolder(), MetaFileName, SearchOption.AllDirectories);
			foreach (var meta in metaFiles)
				yield return JsonUtility.FromJson<SiegeUpModMeta>(File.ReadAllText(meta));
		}

		public static bool TryRemoveMod(string modName)
		{
			var folder = FindModFolder(modName);
			if (Directory.Exists(folder))
			{
				RemoveFolder(folder);
				return true;
			}
			return false;
		}

		public static void RemoveFolder(string modDirectory)
		{
			Directory.Delete(modDirectory, true);
		}

		public static SiegeUpModMeta GetModMeta(string modName)
		{
			var modFolder = FindModFolder(modName);
			if (modFolder == null)
				return null;
			var path = Path.Combine(modFolder, MetaFileName);
			if (!File.Exists(path))
				return null;
			return JsonUtility.FromJson<SiegeUpModMeta>(File.ReadAllText(path));
		}

		public static string GetModNameFromPathFast(string path)
		{
			path = FixPathSeparator(path);
			int nameStartIndex = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;
			int nameLength = path.LastIndexOf('_') - nameStartIndex;
			if (nameLength < 0)
				nameLength = path.Length - nameStartIndex;
			return path.Substring(nameStartIndex, nameLength);
		}

        static string FindModFolder(string modName)
		{
			modName = modName.ToLower();
			var modsFolders = Directory
				.GetDirectories(GetModsFolder())
				.Where(x => GetModNameFromPathFast(x).ToLower() == modName)
				.ToArray();
			if (modsFolders.Length == 0)
				return null;
			if (modsFolders.Length == 1)
				return modsFolders[0];
			Debug.LogWarning("Multiple mods with similar names. Selected first mod");
			return modsFolders[0]; //TODO do smth if there are multiple mods with similar names when searching for mod folder
		}

		public static void CreatePackageMetaFile(SiegeUpModMeta modInfo, string modFolder)
		{
			var info = modInfo.GetShortInfo();
			File.WriteAllText(Path.Combine(modFolder, PackageMetaFileName), JsonUtility.ToJson(info, true));
		}
	}
}