using System.IO;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor.SceneSteps;
using SurvivorSouls.UI.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.Tests
{
    /// <summary>KAN-228: scene step registry (own-file scene wiring) and HUD feature style sheets.</summary>
    public class GameSceneStepTests
    {
        private const string GameScenePath = "Assets/_Project/Scenes/Game.unity";

        [Test]
        public void RegistryFindsTheHudStylesStepInStableOrder()
        {
            var steps = GameSceneSteps.All();
            Assert.That(steps.Any(s => s is HudFeatureStylesStep), "HudFeatureStylesStep not found");
            for (int i = 1; i < steps.Count; i++)
            {
                var a = steps[i - 1];
                var b = steps[i];
                bool ordered = a.Order < b.Order
                    || (a.Order == b.Order && string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName) < 0);
                Assert.That(ordered, $"{a.GetType().Name} ({a.Order}) runs before {b.GetType().Name} ({b.Order})");
            }
        }

        [Test]
        public void FeatureSheetsAreAddedOnce()
        {
            var root = new VisualElement();
            var one = ScriptableObject.CreateInstance<StyleSheet>();
            var two = ScriptableObject.CreateInstance<StyleSheet>();
            try
            {
                Assert.AreEqual(2, HudFeatureStyles.Apply(root, new[] { one, null, two }));
                HudFeatureStyles.Apply(root, new[] { one, two }); // OnEnable + Start both apply
                Assert.AreEqual(2, root.styleSheets.count);
                Assert.AreEqual(0, HudFeatureStyles.Apply(null, new[] { one }));
            }
            finally
            {
                Object.DestroyImmediate(one);
                Object.DestroyImmediate(two);
            }
        }

        [Test]
        public void FeatureFolderHoldsOnlyStyleSheetsTheStepFinds()
        {
            Assert.That(AssetDatabase.IsValidFolder(HudFeatureStylesStep.Folder), $"missing {HudFeatureStylesStep.Folder}");
            int ussFiles = Directory.GetFiles(HudFeatureStylesStep.Folder, "*.uss").Length;
            Assert.AreEqual(ussFiles, HudFeatureStylesStep.FindSheets().Length);
        }

        [Test]
        public void GameSceneHasTheHudFeatureStyles()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Project/Scripts/UI/Hud/HudFeatureStyles.cs");
            Assert.NotNull(script);
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
            Assert.That(File.ReadAllText(GameScenePath).Contains(guid), "rebuild the Game scene: HudFeatureStyles is missing on the HUD");
        }
    }
}
