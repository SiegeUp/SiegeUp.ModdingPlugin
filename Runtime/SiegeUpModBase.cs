using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.Serialization;

namespace SiegeUp.ModdingPlugin
{
	public abstract class SiegeUpModBase : ScriptableObject
	{
		[SerializeField, FormerlySerializedAs("ModInfo")]
		SiegeUpModMeta modInfo = new();

        public SiegeUpModMeta ModInfo => modInfo;
        public abstract IEnumerable<GameObject> AllObjects { get; }

        public abstract bool Validate();

        public void UpdateBuildInfo(PlatformShortName platform, string gameVersion)
		{
            UpdateBuildInfo(platform, new SiegeUpModBundleInfo(platform, ModsLoader.Version, gameVersion));
		}

		public void UpdateBuildInfo(PlatformShortName platform, SiegeUpModBundleInfo prevBuildInfo)
		{
			if (prevBuildInfo == null)
				ModInfo.RemoveBuildInfo(platform);
			else
				ModInfo.UpdateBuildInfo(prevBuildInfo);
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}