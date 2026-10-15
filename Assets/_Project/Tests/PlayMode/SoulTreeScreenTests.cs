using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Meta;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-98 acceptance in play mode: the soul tree page on the real MainMenu.uxml (tier rows joined by links, card
    /// states locked / poor / max, prices and requirement text, pips; the detail sheet's KINDLE only when the buy is
    /// allowed, a buy spends Souls, fills a pip and keeps the sheet open; Back closes the sheet, then the page), the
    /// menu's SOUL TREE button (opens the page; hidden without a tree), the scene wiring (MainMenu controller and the
    /// Game scene's <see cref="SoulTreeBonuses"/> point to the shipped tree), and bought levels reaching the Knight's
    /// health in a real Game scene. Test nodes use ids "test-*"; the wallets are kept by <see cref="WalletPrefsGuard"/>.
    /// </summary>
    public sealed class SoulTreeScreenTests
    {
        private const string TreePath = "Assets/_Project/Data/Economy/SoulTree.asset";
        private readonly List<Object> _objects = new List<Object>();
        private SoulTreeNode _might, _vigor, _draw;
        private SoulTree _tree;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            SoulWallet.Clear();
            _might = Track(SoulTreeNode.Create("test-might", "Kindled Might", 0, 3, 250, 1.45f,
                new[] { new StatModifier(StatId.Damage, ModifierKind.Percent, 0.04f) }, "bolt damage"));
            _vigor = Track(SoulTreeNode.Create("test-vigor", "Ashen Vigor", 0, 1, 100, 2f,
                new[] { new StatModifier(StatId.MaxHealth, ModifierKind.Percent, 0.05f) }, "max health"));
            _draw = Track(SoulTreeNode.Create("test-draw", "Quickened Draw", 1, 3, 400, 1.5f,
                new[] { new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, 0.03f) }, "fire rate", "",
                null, new SoulTreeRequirement(_might, 2)));
            _tree = Track(SoulTree.Create(_might, _vigor, _draw));
            SoulTreeProgress.ResetAll(_tree);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var n in new[] { _might, _vigor, _draw })
            {
                PlayerPrefs.DeleteKey(SoulTreeProgress.KeyOf(n));
                PlayerPrefs.DeleteKey(SoulTreeProgress.KeyOf(n) + ".check");
            }
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            SoulWallet.Clear();
        }

        private VisualElement MenuTree(out GameObject go)
        {
            go = Track(new GameObject("Menu"));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            return doc.rootVisualElement;
        }

        private static void Press(Button button)
        {
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = button;
            button.SendEvent(e);
        }

        private static string Price(Button card) => card.Q<Label>(className: "st-node__price").text;
        private static int PipsOn(Button card) => card.Query(className: "st-pip--on").ToList().Count;

        [UnityTest]
        public IEnumerator Page_CardsShowState_BuyOnlyWhenAllowed_BuySpendsAndStaysOpen_BackClosesSheetThenPage()
        {
            var root = MenuTree(out _);
            yield return null;
            var page = new SoulTreeScreen(root, _tree);
            var screen = root.Q(SoulTreeScreen.ScreenName);
            Assert.That(screen.ClassListContains("ss-hidden"), Is.True, "closed until SOUL TREE");
            Assert.That(screen.parent.pickingMode, Is.EqualTo(PickingMode.Ignore), "the full-screen host never eats menu taps");

            page.Open();
            yield return null;
            yield return null;
            Assert.That(page.IsOpen, Is.True);
            Assert.That(screen.ClassListContains("st-screen--closed"), Is.False, "fades in");

            var tiers = screen.Query(className: "st-tier").ToList();
            Assert.That(tiers.Count, Is.EqualTo(2), "one row per tier");
            Assert.That(tiers[0].childCount, Is.EqualTo(2), "two roots");
            Assert.That(screen.Query(className: "st-link").ToList().Count, Is.EqualTo(1), "rows joined by one link");

            var might = page.CardOf("test-might");
            var draw = page.CardOf("test-draw");
            Assert.That(might.Q<Label>(className: "st-node__name").text, Is.EqualTo("Kindled Might"));
            Assert.That(might.Q<Label>(className: "st-node__effect").text, Is.EqualTo("+4% bolt damage / LV"));
            Assert.That(might.Query(className: "st-pip").ToList().Count, Is.EqualTo(3), "one pip per level");
            Assert.That(Price(might), Is.EqualTo("250"));
            Assert.That(might.ClassListContains("st-node--poor"), Is.True, "empty wallet");
            Assert.That(draw.ClassListContains("st-node--locked"), Is.True);
            Assert.That(Price(draw), Is.EqualTo("Needs Kindled Might II"));

            // Not enough Souls: sheet opens, KINDLE is off and says why.
            Assert.That(page.OpenDetail("test-might"), Is.True);
            yield return null;
            Assert.That(page.IsDetailOpen, Is.True);
            Assert.That(might.ClassListContains("st-node--selected"), Is.True);
            Assert.That(page.CanBuy, Is.False);
            Assert.That(page.BuyText, Is.EqualTo("NOT ENOUGH SOULS"));
            Assert.That(root.Q<Label>("st-detail-next").text, Is.EqualTo("+4% bolt damage"));
            Assert.That(root.Q<Label>("st-detail-cost").text, Is.EqualTo("250 Souls"));
            Assert.That(root.Q<Label>("st-detail-note").ClassListContains("ss-hidden"), Is.False);

            // Souls arrive: KINDLE turns on; pressing it buys and the sheet stays for the next level.
            SoulWallet.Earn(1000);
            page.Refresh();
            Assert.That(page.CanBuy, Is.True);
            Assert.That(page.BuyText, Is.EqualTo("KINDLE"));
            Assert.That(root.Q<Label>("st-souls").text, Is.EqualTo("1,000"));
            var bought = new List<int>();
            page.Bought += (_, level) => bought.Add(level);
            Press(root.Q<Button>("st-buy"));
            yield return null;
            Assert.That(SoulTreeProgress.Level(_might), Is.EqualTo(1));
            Assert.That(SoulWallet.Balance, Is.EqualTo(750));
            Assert.That(bought, Is.EqualTo(new[] { 1 }));
            Assert.That(page.IsDetailOpen, Is.True, "sheet stays open");
            Assert.That(PipsOn(might), Is.EqualTo(1));
            Assert.That(might.Q<Label>(className: "st-node__effect").text, Is.EqualTo("+4% bolt damage"));
            Assert.That(page.BuyText, Is.EqualTo("KINDLE II"));
            Assert.That(root.Q<Label>("st-detail-level").text, Is.EqualTo("LEVEL 1 / 3"));
            Assert.That(root.Q<Label>("st-souls").text, Is.EqualTo("750"));

            Assert.That(page.BuySelected(), Is.EqualTo(SoulTreeBuyCheck.Ok));
            Assert.That(draw.ClassListContains("st-node--locked"), Is.False, "Might II opens Quickened Draw");
            Assert.That(Price(draw), Is.EqualTo("400"));
            Assert.That(page.BuySelected(), Is.EqualTo(SoulTreeBuyCheck.NotEnoughSouls), "level 3 costs 525 > 385");
            SoulWallet.Earn(200);
            page.Refresh();
            Assert.That(page.BuySelected(), Is.EqualTo(SoulTreeBuyCheck.Ok));
            Assert.That(might.ClassListContains("st-node--max"), Is.True);
            Assert.That(Price(might), Is.EqualTo("MAX"));
            Assert.That(page.CanBuy, Is.False);
            Assert.That(page.BuyText, Is.EqualTo("MAX LEVEL"));

            // Locked node: sheet says why.
            SoulTreeProgress.SetLevel(_might, 0);
            page.Refresh();
            Assert.That(page.OpenDetail("test-draw"), Is.True);
            Assert.That(page.BuyText, Is.EqualTo("LOCKED"));
            Assert.That(root.Q<Label>("st-detail-note").text, Is.EqualTo("Needs Kindled Might II to kindle this."));
            Assert.That(page.OpenDetail("nope"), Is.False);

            Assert.That(page.Back(), Is.True);
            Assert.That(page.IsDetailOpen, Is.False);
            Assert.That(page.IsOpen, Is.True, "first Back closes only the sheet");
            Assert.That(page.Back(), Is.True);
            Assert.That(page.IsOpen, Is.False);
            Assert.That(page.Back(), Is.False);
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(screen.ClassListContains("ss-hidden"), Is.True, "hidden after the fade");
        }

        [UnityTest]
        public IEnumerator Menu_SoulTreeButton_OpensThePage_HiddenWithoutATree()
        {
            var root = MenuTree(out var go);
            go.SetActive(false);
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(null, null, _tree);
            go.SetActive(true);
            yield return null;
            Assert.That(menu.SoulTreePage, Is.Not.Null);
            var button = go.GetComponent<UIDocument>().rootVisualElement.Q<Button>("btn-soul-tree");
            Assert.That(button.ClassListContains("ss-hidden"), Is.False);
            Press(button);
            yield return null;
            Assert.That(menu.SoulTreePage.IsOpen, Is.True);
            Assert.That(menu.Back(), Is.True);
            Assert.That(menu.SoulTreePage.IsOpen, Is.False);

            var root2 = MenuTree(out var bare);
            bare.SetActive(false);
            var plain = bare.AddComponent<MainMenuController>();
            plain.Setup(null);
            bare.SetActive(true);
            yield return null;
            Assert.That(plain.SoulTreePage, Is.Null);
            Assert.That(bare.GetComponent<UIDocument>().rootVisualElement.Q<Button>("btn-soul-tree").ClassListContains("ss-hidden"), Is.True);
        }

        [Test]
        public void Scenes_WireTheShippedTree()
        {
            var tree = UnityEditor.AssetDatabase.LoadAssetAtPath<SoulTree>(TreePath);
            Assert.That(tree, Is.Not.Null);
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(TreePath);
            StringAssert.Contains($"soulTree: {{fileID: 11400000, guid: {guid}",
                System.IO.File.ReadAllText("Assets/_Project/Scenes/MainMenu.unity"), "MainMenu controller wired to the tree");
            StringAssert.Contains($"tree: {{fileID: 11400000, guid: {guid}",
                System.IO.File.ReadAllText("Assets/_Project/Scenes/Game.unity"), "Game scene SoulTreeBonuses wired to the tree");
        }

        [UnityTest]
        public IEnumerator GameScene_BoughtVigorRaisesTheKnightsHealth_PowerStaysOne()
        {
            var shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<SoulTree>(TreePath);
            var vigor = shipped.Find("vigor");
            var might = shipped.Find("might");
            Assert.That(vigor, Is.Not.Null);
            try
            {
                SoulTreeProgress.SetLevel(vigor, 2); // +10 % max health
                SoulTreeProgress.SetLevel(might, 5); // +20 % damage
                RunState.NewRun(97, 9301);
                SceneManager.LoadScene("Game");
                yield return null;
                yield return null;
                var bonuses = Object.FindAnyObjectByType<SoulTreeBonuses>();
                var health = Object.FindAnyObjectByType<PlayerHealth>();
                var stats = Object.FindAnyObjectByType<PlayerStats>();
                var scaler = Object.FindAnyObjectByType<HordeScaler>();
                Assert.That(bonuses, Is.Not.Null, "SoulTreeBonuses on the player");
                Assert.That(bonuses.Tree, Is.SameAs(shipped));
                Assert.That(bonuses.OwnedNodes, Is.EqualTo(2));
                Assert.That(stats.Percent(StatId.MaxHealth), Is.EqualTo(0.1f).Within(1e-4f));
                float baseHealth = (float)typeof(PlayerHealth)
                    .GetField("maxHealth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .GetValue(health);
                Assert.That(health.Max, Is.EqualTo(baseHealth * 1.1f).Within(0.5f), "max health read with the tree");
                Assert.That(health.Current, Is.EqualTo(health.Max).Within(0.5f), "starts full");
                Assert.That(scaler.Power, Is.EqualTo(1f).Within(0.01f), "tree bonuses are the baseline");

                var empty = SceneManager.CreateScene("SoulTreeEmpty");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync("Game");
            }
            finally
            {
                SoulTreeProgress.SetLevel(vigor, 0);
                SoulTreeProgress.SetLevel(might, 0);
            }
        }
    }
}
