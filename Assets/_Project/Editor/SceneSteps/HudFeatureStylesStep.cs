using System.Linq;
using SurvivorSouls.UI.Hud;
using UnityEditor;
using UnityEngine.UIElements;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// Puts every style sheet in <see cref="Folder"/> on the HUD (KAN-228), sorted by path. New HUD styles go in a new
    /// <c>UI/Hud/Features/&lt;Feature&gt;.uss</c> file: no edit to GameHud.uss or GameHud.uxml.
    /// </summary>
    public sealed class HudFeatureStylesStep : IGameSceneStep
    {
        public const string Folder = "Assets/_Project/UI/Hud/Features";

        public int Order => 0;

        public void Build(GameSceneContext ctx)
        {
            var sheets = FindSheets();
            var styles = ctx.Hud.AddComponent<HudFeatureStyles>();
            var so = new SerializedObject(styles);
            var list = so.FindProperty("sheets");
            list.arraySize = sheets.Length;
            for (int i = 0; i < sheets.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = sheets[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static StyleSheet[] FindSheets()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return new StyleSheet[0];
            return AssetDatabase.FindAssets("t:StyleSheet", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".uss"))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<StyleSheet>)
                .Where(s => s != null)
                .ToArray();
        }
    }
}
