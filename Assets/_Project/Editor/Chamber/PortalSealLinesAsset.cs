using System.IO;
using SurvivorSouls.UI.Hud;
using UnityEditor;

namespace SurvivorSouls.Editor.Chamber
{
    /// <summary>
    /// The sealed-portal bubble's text table (KAN-49). Created once with the code defaults of <see cref="PortalSealLines"/>;
    /// after that the asset is hand-authored data and the scene builder only loads it (never overwrites the lines).
    /// </summary>
    public static class PortalSealLinesAsset
    {
        public const string Path = "Assets/_Project/Data/Chamber/PortalSealLines.asset";

        public static PortalSealLines LoadOrCreate()
        {
            var lines = AssetDatabase.LoadAssetAtPath<PortalSealLines>(Path);
            if (lines != null) return lines;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            lines = UnityEngine.ScriptableObject.CreateInstance<PortalSealLines>();
            AssetDatabase.CreateAsset(lines, Path);
            AssetDatabase.SaveAssets();
            return lines;
        }
    }
}
