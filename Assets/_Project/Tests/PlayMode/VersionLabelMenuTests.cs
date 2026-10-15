using System.Collections;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>KAN-142: the real MainMenu.uxml shows the build version on the menu and in Settings.</summary>
    public sealed class VersionLabelMenuTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator MenuAndSettingsShowTheBuildVersion()
        {
            var go = new GameObject("Menu");
            try
            {
                var doc = go.AddComponent<UIDocument>();
                doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
                doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
                go.SetActive(false);
                go.AddComponent<MainMenuController>();
                go.SetActive(true);
                yield return null;

                VisualElement root = doc.rootVisualElement;
                string expected = BuildInfo.DisplayLabel;
                StringAssert.StartsWith("v", expected);
                Assert.AreEqual(expected, root.Q<Label>(MainMenuController.VersionName).text);
                Assert.AreEqual(expected, root.Q<Label>(MainMenuController.SettingsVersionName).text);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
#endif
    }
}
