using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Realms;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-20 (KAN-55) acceptance: the lock rule (Realm N opens when N-1 is completed), the shipped Realm catalog
    /// (Realm 1 playable with art and the Chamber count of its difficulty asset, 2 and 3 "coming soon"), and the Realm
    /// page on the real MainMenu.uxml: cards with the right state class, badge and progress; the detail sheet's START
    /// only on an unlocked, playable Realm and it hands that Realm over; Back closes the sheet, then the page; PLAY on
    /// the menu opens the page. Test Realms <see cref="First"/>.. so the editor's real progress is never touched.
    /// </summary>
    public sealed class RealmSelectTests
    {
        private const int First = 91; // test Realms 91, 92, 93; 90 is the "previous" Realm that unlocks 91
        private const string LastRealmKey = "progress.lastRealm";

        private readonly List<Object> _objects = new List<Object>();
        private int _savedLastRealm;
        private bool _hadLastRealm;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            for (int r = First - 1; r <= First + 2; r++) RunProgress.Clear(r);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            for (int r = First - 1; r <= First + 2; r++) RunProgress.Clear(r);
            if (_hadLastRealm) PlayerPrefs.SetInt(LastRealmKey, _savedLastRealm);
            else PlayerPrefs.DeleteKey(LastRealmKey);
        }

        // ------------------------------------------------------------ Lock rule

        [Test]
        public void LockRule_Realm1AlwaysOpen_NextOpensWhenPreviousCompleted()
        {
            Assert.That(RunProgress.IsUnlocked(1), Is.True, "Realm 1 is always open");
            Assert.That(RunProgress.StateOf(First), Is.EqualTo(RealmState.Locked));
            Assert.That(RunProgress.StateOf(First + 1), Is.EqualTo(RealmState.Locked));

            RunProgress.MarkCompleted(First - 1);
            Assert.That(RunProgress.StateOf(First), Is.EqualTo(RealmState.Unlocked));
            Assert.That(RunProgress.StateOf(First + 1), Is.EqualTo(RealmState.Locked), "only the next one opens");

            RunProgress.RecordChamberStart(First, 4);
            Assert.That(RunProgress.StateOf(First), Is.EqualTo(RealmState.Unlocked), "progress alone does not complete");

            RunProgress.MarkCompleted(First);
            Assert.That(RunProgress.StateOf(First), Is.EqualTo(RealmState.Completed));
            Assert.That(RunProgress.StateOf(First + 1), Is.EqualTo(RealmState.Unlocked));
            Assert.That(RunProgress.StateOf(First + 2), Is.EqualTo(RealmState.Locked));

            RunProgress.Clear(First);
            Assert.That(RunProgress.IsCompleted(First), Is.False, "Clear removes the completed flag");
            Assert.That(RunProgress.BestChamber(First), Is.EqualTo(0));
        }

        // ------------------------------------------------------------ Shipped data

#if UNITY_EDITOR
        [Test]
        public void ShippedCatalog_Realm1Playable_WithArtAndDifficultyChambers_OthersComingSoon()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<RealmCatalog>("Assets/_Project/Data/Realms/RealmCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Realms.Select(r => r.Id), Is.EqualTo(new[] { 1, 2, 3 }), "play order = ids 1, 2, 3");

            var realm1 = catalog.Get(1);
            Assert.That(realm1.Playable, Is.True);
            Assert.That(realm1.Thumbnail, Is.Not.Null);
            Assert.That(realm1.Difficulty, Is.Not.Null);
            Assert.That(realm1.Chambers, Is.EqualTo(realm1.Difficulty.ChambersInRealm), "page and game agree");
            Assert.That(realm1.DisplayName, Is.Not.Empty);
            Assert.That(realm1.Description, Is.Not.Empty);
            Assert.That(realm1.EstusCost, Is.EqualTo(5));
            Assert.That(catalog.Get(2).Playable, Is.False);
            Assert.That(catalog.Get(3).Playable, Is.False);
            Assert.That(catalog.Get(4), Is.Null);

            var scene = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>("Assets/_Project/Scenes/MainMenu.unity");
            Assert.That(scene, Is.Not.Null);
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Scenes/MainMenu.unity");
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID("Assets/_Project/Data/Realms/RealmCatalog.asset");
            StringAssert.Contains($"realms: {{fileID: 11400000, guid: {guid}", yaml, "MainMenu controller wired to the catalog");
        }

        // ------------------------------------------------------------ Page

        private RealmCatalog TestCatalog() => Track(RealmCatalog.Create(
            Track(RealmDefinition.Create(First, "Test Shore", 10)),
            Track(RealmDefinition.Create(First + 1, "Test Burg", 8)),
            Track(RealmDefinition.Create(First + 2, "Test Marsh", 12, playable: false))));

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

        [UnityTest]
        public IEnumerator Page_CardsShowState_StartOnlyWhenOpenAndPlayable_BackClosesSheetThenPage()
        {
            RunProgress.MarkCompleted(First - 1); // First is open, First + 1 locked
            RunProgress.RecordChamberStart(First, 4);
            var root = MenuTree(out _);
            yield return null;

            var started = new List<RealmDefinition>();
            var catalog = TestCatalog();
            var page = new RealmSelectScreen(root, catalog, started.Add);
            var screen = root.Q(RealmSelectScreen.ScreenName);
            Assert.That(screen.ClassListContains("ss-hidden"), Is.True, "closed until PLAY");
            Assert.That(screen.parent.pickingMode, Is.EqualTo(PickingMode.Ignore), "the full-screen host never eats menu taps");

            page.Open();
            yield return null;
            yield return null;
            Assert.That(page.IsOpen, Is.True);
            Assert.That(screen.ClassListContains("ss-hidden"), Is.False);
            Assert.That(screen.ClassListContains("rs-screen--closed"), Is.False, "fades in");

            var open = page.CardOf(First);
            var locked = page.CardOf(First + 1);
            var soon = page.CardOf(First + 2);
            Assert.That(open.ClassListContains("rs-card--unlocked"), Is.True);
            Assert.That(locked.ClassListContains("rs-card--locked"), Is.True);
            Assert.That(soon.ClassListContains("rs-card--locked"), Is.True);
            Assert.That(open.ClassListContains("rs-card--locked"), Is.False, "one state class per card");
            Assert.That(open.Q<Label>(className: "rs-card__name").text, Is.EqualTo("Test Shore"));
            Assert.That(open.Q<Label>(className: "rs-card__numeral").text, Is.EqualTo("REALM XCI"));
            Assert.That(open.Query<Label>(className: "rs-card__meta").ToList().Select(l => l.text),
                Is.EqualTo(new[] { "10 CHAMBERS", "BEST 4 / 10" }));
            Assert.That(locked.Q<Label>(className: "rs-card__badge").text, Is.EqualTo("LOCKED"));
            Assert.That(RealmSelectScreen.Progress(catalog.Get(First), RealmState.Unlocked), Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(RealmSelectScreen.Progress(catalog.Get(First), RealmState.Completed), Is.EqualTo(1f));
            Assert.That(RealmSelectScreen.Progress(catalog.Get(First + 1), RealmState.Locked), Is.EqualTo(0f));

            // Locked Realm: sheet opens, START is off and says why.
            Assert.That(page.OpenDetail(First + 1), Is.True);
            yield return null;
            Assert.That(page.IsDetailOpen, Is.True);
            Assert.That(page.CanStart, Is.False);
            Assert.That(root.Q<Label>("rs-detail-note").text, Does.Contain("XCI"));
            Press(root.Q<Button>("rs-start"));
            Assert.That(started, Is.Empty, "a locked Realm never starts");

            // Back closes the sheet, the page stays.
            Assert.That(page.Back(), Is.True);
            Assert.That(page.IsDetailOpen, Is.False);
            Assert.That(page.IsOpen, Is.True);

            // Open Realm: START hands it over once.
            Press(open);
            Assert.That(page.IsDetailOpen, Is.True, "tapping a card opens its sheet");
            Assert.That(page.Selected.Id, Is.EqualTo(First));
            Assert.That(page.CanStart, Is.True);
            Assert.That(root.Q<Label>("rs-detail-best").text, Is.EqualTo("BEST 4 / 10"));
            Assert.That(root.Q<Label>("rs-detail-chambers").text, Is.EqualTo("10"));
            Press(root.Q<Button>("rs-start"));
            Assert.That(started.Select(r => r.Id), Is.EqualTo(new[] { First }));

            // Completing the Realm: next one opens on the next Open; the "coming soon" one opens but cannot start.
            page.Back();
            page.Back();
            Assert.That(page.IsOpen, Is.False);
            Assert.That(page.Back(), Is.False, "nothing left to close");
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(screen.ClassListContains("ss-hidden"), Is.True, "hidden after the fade out");

            RunProgress.MarkCompleted(First);
            page.Open();
            Assert.That(open.ClassListContains("rs-card--completed"), Is.True);
            Assert.That(open.Q<Label>(className: "rs-card__badge").text, Is.EqualTo("CLEARED"));
            Assert.That(locked.ClassListContains("rs-card--unlocked"), Is.True);
            Assert.That(locked.Q<Label>(className: "rs-card__badge").text, Is.EqualTo("NEW"));
            Assert.That(soon.ClassListContains("rs-card--locked"), Is.True);

            RunProgress.MarkCompleted(First + 1);
            page.Refresh();
            Assert.That(soon.ClassListContains("rs-card--unlocked"), Is.True);
            Assert.That(soon.Q<Label>(className: "rs-card__badge").text, Is.EqualTo("SOON"));
            page.OpenDetail(First + 2);
            Assert.That(page.CanStart, Is.False, "no content yet");
        }

        [UnityTest]
        public IEnumerator Menu_PlayOpensRealmPage_BackClosesIt()
        {
            MenuTree(out var go);
            go.SetActive(false);
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(TestCatalog());
            go.SetActive(true);
            yield return null;

            Assert.That(menu.RealmSelect, Is.Not.Null);
            Press(go.GetComponent<UIDocument>().rootVisualElement.Q<Button>("btn-play"));
            Assert.That(menu.RealmSelect.IsOpen, Is.True, "PLAY opens the Realm page, no scene load");
            Assert.That(menu.Back(), Is.True);
            Assert.That(menu.RealmSelect.IsOpen, Is.False);
            Assert.That(menu.Back(), Is.False);
        }
#endif
    }
}
