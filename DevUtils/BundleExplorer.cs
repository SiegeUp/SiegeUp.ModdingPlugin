using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SiegeUp.ModdingPlugin.DevUtils
{
	[ExecuteInEditMode]
	public class BundleExplorer : MonoBehaviour
	{
		[SerializeField]
        List<GameObject> spawnedObjects = new();
		[SerializeField]
        List<SiegeUpModBase> loadedMods = new();

        ModsLoader modsLoader;
        const int ObjectsInterval = 2;

        void OnEnable()
		{
			modsLoader = new ModsLoader("1.1.102r19");
		}

		public void LoadBundle(string path)
		{
			loadedMods.Add(modsLoader.LoadBundle(path));
		}

		public void SpawnObjects()
		{
			int x = spawnedObjects.Count * ObjectsInterval;
            foreach (var prefab in loadedMods.Last().AllObjects)
			{
				var go = Instantiate(prefab, new Vector3(x, 0, 0), Quaternion.identity, transform);
				spawnedObjects.Add(go);
				x += ObjectsInterval;
			}
		}

		public void UnloadAllBundles()
		{
			foreach (var go in spawnedObjects)
				DestroyImmediate(go.gameObject);
			spawnedObjects.Clear();
			modsLoader.UnloadMods();
			loadedMods.Clear();
		}

        void OnDestroy()
		{
			UnloadAllBundles();
		}
	}

#if UNITY_EDITOR
	[CustomEditor(typeof(BundleExplorer))]
	public class BundleExplorerGUI : Editor
	{
        BundleExplorer targetObject;

        void OnEnable() => targetObject = (BundleExplorer)target;

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			if (GUILayout.Button("Load bundle"))
			{
				string selectedPath = EditorUtility.OpenFilePanel("Select mod file", "", "");
				targetObject.LoadBundle(selectedPath);
				EditorUtility.SetDirty(targetObject);
			}

			if (GUILayout.Button("Spawn objects from last loaded bundle"))
			{
				targetObject.SpawnObjects();
				EditorUtility.SetDirty(targetObject);
			}

			if (GUILayout.Button("Unload all bundles"))
			{
				targetObject.UnloadAllBundles();
				EditorUtility.SetDirty(targetObject);
			}
		}
	}
#endif
}