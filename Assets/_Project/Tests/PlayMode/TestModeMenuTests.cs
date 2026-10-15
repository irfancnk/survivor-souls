using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Realms;
using SurvivorSouls.UI.Hud;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using TestMode = SurvivorSouls.Core.TestMode;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-144 Test Mode on the real MainMenu.uxml / GameHud.uxml: the settings row shows in a development build and stays
    /// hidden in other builds until the 7th tap on the settings title; the toggle switches Test Mode, grants the Souls once
    /// and shows the "TEST MODE" tag (menu + HUD); with it on every Realm card is open, a "coming soon" Realm can start,
    /// and the sheet's Start Chamber stepper wraps 1..last and is handed to START; with it off the stepper hides and
    /// START begins at Chamber 1 again. Real keys are kept by <see cref="WalletPrefsGuard"/> plus this fixture.
    /// </summary>
    public sealed class TestModeMenuTests
    {
#if UNITY_EDITOR
        private const int First = 91;
        private readonly List<Object> _objects = new List<Object>();
        private int? _savedGrant;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            _savedGrant = PlayerPrefs.HasKey(TestMode.KeyGranted) ? PlayerPrefs.GetInt(TestMode.KeyGranted) : (int?)null;
            TestMode.ResetAll();
            for (int r = First - 1; r <= First + 2; r++) RunProgress.Clear(r);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            for (int r = First - 1; r <= First + 2; r++) RunProgress.Clear(r);
            TestMode.ResetAll(); // the suite runs with Test Mode off (WalletPrefsGuard restores the editor's value)
            if (_savedGrant.HasValue) PlayerPrefs.SetInt(TestMode.KeyGranted, _savedGrant.Value);
        }

        private RealmCatalog TestCatalog() => Track(RealmCatalog.Create(
            Track(RealmDefinition.Create(First, "Test Shore", 10)),
            Track(RealmDefinition.Create(First + 1, "Test Burg", 8)),
            Track(RealmDefinition.Create(First + 2, "Test Marsh", 12, playable: false))));

        private MainMenuController Menu(bool devBuild, out VisualElement root, bool realms = true)
        {
            var go = Track(new GameObject("Menu"));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            go.SetActive(false);
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(realms ? TestCatalog() : null);
            menu.DevBuildForTestMode = devBuild;
            go.SetActive(true);
            root = doc.rootVisualElement;
            return menu;
        }

        private static void Press(Button button)
        {
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = button;
            button.SendEvent(e);
        }

        [UnityTest]
        public IEnumerator SettingsRow_DevBuildShown_OtherBuildsAfterSevenTitleTaps()
        {
            var dev = Menu(devBuild: true, out var devRoot);
            yield return null;
            dev.OpenSettings();
            Assert.That(dev.TestModeRowVisible, Is.True, "dev build: row always visible");
            Assert.That(devRoot.Q<Toggle>(MainMenuController.TestModeToggleName).value, Is.False, "off by default");
            Assert.That(dev.TestModeTagVisible, Is.False);

            var store = Menu(devBuild: false, out _);
            yield return null;
            store.OpenSettings();
            Assert.That(store.TestModeRowVisible, Is.False, "non-dev build: hidden");
            for (int i = 1; i < TestMode.RevealTaps; i++) store.TapSettingsTitle();
            Assert.That(store.TestModeRowVisible, Is.False, "6 taps are not enough");
            store.TapSettingsTitle();
            Assert.That(store.TestModeRowVisible, Is.True, "7th tap shows the row");
        }

        [UnityTest]
        public IEnumerator Toggle_SwitchesTestMode_GrantsSoulsOnce_ShowsTag()
        {
            int before = SoulWallet.Balance;
            var menu = Menu(devBuild: true, out var root);
            yield return null;
            menu.OpenSettings();
            var toggle = root.Q<Toggle>(MainMenuController.TestModeToggleName);

            toggle.value = true; // fires the change callback like a tap
            Assert.That(TestMode.IsOn, Is.True);
            Assert.That(SoulWallet.Balance, Is.GreaterThanOrEqualTo(TestMode.SoulGrant));
            Assert.That(menu.TestModeTagVisible, Is.True, "TEST MODE tag on the menu");
            Assert.That(root.Q<Label>("lbl-wallet-souls").text, Is.EqualTo(PlayerHealthHud.FormatCount(SoulWallet.Balance)));
            int granted = SoulWallet.Balance;

            toggle.value = false;
            Assert.That(TestMode.IsOn, Is.False);
            Assert.That(menu.TestModeTagVisible, Is.False);
            Assert.That(menu.TestModeRowVisible, Is.True);
            toggle.value = true;
            Assert.That(SoulWallet.Balance, Is.EqualTo(granted), "second switch-on adds nothing");

            // Put the editor wallet back (WalletPrefsGuard restores the keys after the suite anyway).
            SoulWallet.Clear();
            SoulWallet.Earn(before);
        }

        [UnityTest]
        public IEnumerator RealmPage_TestModeOpensAll_StepperPicksStartChamber()
        {
            var started = new List<RealmDefinition>();
            var menu = Menu(devBuild: true, out var root, realms: false); // own page below: START must not load a scene
            yield return null;
            var page = new RealmSelectScreen(root, TestCatalog(), started.Add);

            // Off: real locks, no stepper, Chamber 1.
            page.Open();
            Assert.That(page.CardOf(First + 1).ClassListContains("rs-card--locked"), Is.True);
            page.OpenDetail(First);
            Assert.That(page.StepperVisible, Is.False, "no stepper in normal play");
            page.StepChamber(4);
            Assert.That(page.StartChamber, Is.EqualTo(RunState.FirstChamber), "normal play always starts at Chamber 1");
            page.Back();

            // On: every card open, the "coming soon" Realm starts, stepper wraps.
            menu.SetTestMode(true);
            page.Refresh();
            for (int r = First; r <= First + 2; r++)
                Assert.That(page.CardOf(r).ClassListContains("rs-card--unlocked"), Is.True, $"Realm {r} open");
            Assert.That(page.CardOf(First + 2).Q<Label>(className: "rs-card__badge").text, Is.EqualTo("NEW"), "no SOON in Test Mode");
            Assert.That(page.CardOf(First).Q<Label>(className: "rs-card__badge").text, Is.EqualTo("NEW"),
                "progress was not written by switching on");

            page.OpenDetail(First + 1);
            Assert.That(page.CanStart, Is.True, "locked Realm starts in Test Mode");
            Assert.That(page.StepperVisible, Is.True);
            var value = root.Q<Label>("rs-chamber-value");
            Assert.That(value.text, Is.EqualTo("1 / 8"));
            Press(root.Q<Button>("rs-chamber-prev"));
            Assert.That(page.StartChamber, Is.EqualTo(8), "1 back wraps to the last (boss) Chamber");
            Assert.That(value.text, Is.EqualTo("8 / 8"));
            Press(root.Q<Button>("rs-chamber-next"));
            Assert.That(page.StartChamber, Is.EqualTo(1), "past the last wraps to 1");
            for (int i = 0; i < 4; i++) Press(root.Q<Button>("rs-chamber-next"));
            Assert.That(page.StartChamber, Is.EqualTo(5));
            Press(root.Q<Button>("rs-start"));
            Assert.That(started.Select(r => r.Id), Is.EqualTo(new[] { First + 1 }));

            page.Back();
            page.OpenDetail(First + 2);
            Assert.That(page.CanStart, Is.True, "a Realm without own content starts (Realm 1 content until KAN-57)");
            Assert.That(page.StartChamber, Is.EqualTo(1), "each sheet opens on Chamber 1");
            Assert.That(root.Q<Label>("rs-chamber-value").text, Is.EqualTo("1 / 12"));

            // Off again: locks back, stepper hidden on the next sheet, Chamber 1.
            page.StepChamber(3);
            menu.SetTestMode(false);
            page.Refresh();
            Assert.That(page.CardOf(First + 1).ClassListContains("rs-card--locked"), Is.True, "locks back from the real save");
            Assert.That(page.StartChamber, Is.EqualTo(RunState.FirstChamber));
            page.OpenDetail(First + 1);
            Assert.That(page.StepperVisible, Is.False);
            Assert.That(page.CanStart, Is.False);
        }

        [UnityTest]
        public IEnumerator Hud_ShowsTestModeTag_OnlyWhileOn()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool on = pass == 1;
                GameSettings.TestMode = on;
                var go = Track(new GameObject("Hud"));
                go.SetActive(false);
                var doc = go.AddComponent<UIDocument>();
                doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
                doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
                go.AddComponent<GameHudController>();
                go.SetActive(true);
                yield return null;
                var tag = doc.rootVisualElement.Q<Label>(GameHudController.TestModeTagName);
                Assert.That(tag, Is.Not.Null);
                Assert.That(tag.ClassListContains("hud-hidden"), Is.EqualTo(!on), on ? "tag shows in Test Mode" : "hidden in normal play");
                Object.DestroyImmediate(go);
            }
        }
#endif
    }
}
