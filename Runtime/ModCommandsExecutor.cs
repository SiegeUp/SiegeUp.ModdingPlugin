﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SiegeUp.ModdingPlugin
{
	public class ModCommandsExecutor : MonoBehaviour
	{
		readonly Dictionary<string, Func<IEnumerable<string>, IEnumerator>> Arguments = new()
		{
			{ "add", args => DownloadModFromGit(string.Join(" ", args)) },
			{ "list", args => ListInstalledMods(args)},
			{ "update", args => UpdateMod(string.Join(" ", args)) },
			{ "remove", args => RemoveMod(string.Join(" ", args)) },
		};

		public void Execute(List<string> args)
		{
			if (!Arguments.ContainsKey(args[0]))
			{
				Debug.LogError($"Unknown command argument: {args[0]}");
				return;
			}
			StartCoroutine(Arguments[args[0]](args.Skip(1)));
		}

		public static IEnumerator DownloadModFromGit(string url)
		{
			var fileName = NetworkUtils.GetFileNameFromUrl(url);
			if (fileName != FileUtils.MetaFileName)
			{
				Debug.LogError($"Unsupported mod link: {url}");
				yield break;
			}
			string data = "";
			yield return NetworkUtils.GetData(url, (result) => data = result);
			if (string.IsNullOrEmpty(data))
				yield break;
			var meta = JsonUtility.FromJson<SiegeUpModMeta>(data);
			if (meta == null)
				yield break;
			meta.ModSourceUrl = url;
			if (meta.TryGetBuildInfo(Utils.GetCurrentPlatform(), out SiegeUpModBundleInfo buildInfo))
			{
				if (ModsLoader.Instance != null && !ModsLoader.Instance.CanLoadMod(buildInfo))
				{
					Debug.LogError($"Failed to load {meta.ModName}. It is not compatible with current game version.");
					yield break;
				}
				string modDirectory = FileUtils.GetExpectedModFolder(meta);
				var bundlePath = FileUtils.GetBundlePath(meta, Utils.GetCurrentPlatform());
				var bundleFileName = FileUtils.GetBundleFileName(meta, Utils.GetCurrentPlatform());
				var bundleUrl = url.Replace(FileUtils.MetaFileName, bundleFileName);
				bool downloadResult = false;
				yield return NetworkUtils.TryDownloadFile(bundleUrl, bundlePath, (result) => downloadResult = result);
				if (downloadResult)
				{
					FileUtils.CreateModMetaFile(modDirectory, meta);
					yield break;
				}
				FileUtils.RemoveFolder(modDirectory);
			}
			Debug.LogError($"Failed to find \"{meta.ModName}\" build for current platform");
		}

		public static IEnumerator RemoveMod(string modName)
		{
			if (FileUtils.TryRemoveMod(modName))
				Debug.Log($"{modName} was successfully removed");
			else
				Debug.LogError($"Failed to remove {modName}");
			yield break;
		}

		public static IEnumerator UpdateMod(string modName)
		{
			var meta = FileUtils.GetModMeta(modName);
			if (string.IsNullOrEmpty(meta?.ModSourceUrl))
			{
				Debug.LogError($"Unable to find mod link for {modName}");
				yield break;
			}
			yield return DownloadModFromGit(meta.ModSourceUrl);
		}

		public static IEnumerator ListInstalledMods(IEnumerable<string> args)
		{
			var argument = args.FirstOrDefault();
			var showOnlySupported = string.IsNullOrEmpty(argument) ? false : argument.StartsWith("support");

			bool anyModsListed = false;
			foreach (var mod in FileUtils.GetInstalledModsMeta())
			{
				anyModsListed = true;
				var hasBuild = mod.TryGetBuildInfo(Utils.GetCurrentPlatform(), out SiegeUpModBundleInfo buildInfo);
				var isSupported = hasBuild && (ModsLoader.Instance?.CanLoadMod(buildInfo) ?? true);
				if (!showOnlySupported || isSupported)
					Debug.Log($"{mod.ModName} v{mod.Version} by {mod.AuthorName}" + (isSupported ? "" : " {unsupported}"));
#if !UNITY_EDITOR
				yield return null;
#endif
			}
			if (!anyModsListed)
				Debug.Log("No mods installed");
			yield break;
		}
	}
}
