using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Heroes;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using TestMode = SurvivorSouls.Core.TestMode;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-153 in play mode: the hero page on the real MainMenu.uxml (one card per hero with portrait, pros / cons and
    /// price; the Knight starts picked; a locked hero says how much is missing, buys on the second tap only, then is
    /// picked; Test Mode makes every hero free with a note and charges nothing), the menu's HEROES button (opens the page;
    /// hidden without a roster), both scenes wired to the shipped roster, and the Game scene spawning the picked Shadow
    /// Ranger with his model (Knight model off, bow muzzle, crossbow parts off, death clip) and his stats (max health,
    /// attack speed, horde power 1), his own arrow + bow twang (KAN-227; the Knight keeps the bolt and crossbow shots).
    /// Wallets and hero saves are kept by <see cref="WalletPrefsGuard"/> and reset per test.
    /// </summary>
    public sealed class HeroSelectTests
    {
        private const string RosterPath = "Assets/_Project/Data/Heroes/HeroRoster.asset";
        private readonly List<Object> _objects = new List<Object>();
        private HeroRoster _roster;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            _roster = UnityEditor.AssetDatabase.LoadAssetAtPath<HeroRoster>(RosterPath);
            Assert.That(_roster, Is.Not.Null);
            TestMode.ResetAll();
            SoulWallet.Clear();
            GemWallet.Clear();
            HeroProgress.ResetAll(_roster);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            TestMode.ResetAll();
            SoulWallet.Clear();
            GemWallet.Clear();
            HeroProgress.ResetAll(_roster);
        }

        private MainMenuController Menu(out VisualElement root, bool heroes = true)
        {
            var go = Track(new GameObject("Menu"));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            go.SetActive(false);
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(null, heroes: heroes ? _roster : null);
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

        private static IEnumerable<string> Texts(VisualElement card, string className)
        {
            foreach (var l in card.Query<Label>(className: className).ToList()) yield return l.text;
        }

        [UnityTest]
        public IEnumerator Page_KnightPicked_RangerLocked_PoorThenConfirmBuy_ThenPicked()
        {
            var menu = Menu(out var root);
            yield return null;
            var page = menu.HeroPage;
            Assert.That(page, Is.Not.Null);
            Assert.That(root.Q(HeroSelectScreen.ScreenName).ClassListContains("ss-hidden"), Is.True, "closed until HEROES");
            Press(root.Q<Button>(MainMenuController.HeroesButtonName));
            yield return null;
            Assert.That(page.IsOpen, Is.True);
            Assert.That(page.TestNoteVisible, Is.False);

            var knightCard = page.CardOf("knight");
            var rangerCard = page.CardOf("shadow_ranger");
            Assert.That(knightCard, Is.Not.Null);
            Assert.That(rangerCard, Is.Not.Null);
            Assert.That(rangerCard.Q(className: "hs-portrait").resolvedStyle.backgroundImage.texture, Is.Not.Null, "portrait");
            Assert.That(Texts(rangerCard, "hs-trait__text"), Is.EquivalentTo(new[] { "+30% attack speed", "+18% move speed", "-25% max health", "-20% arrow damage" }));
            Assert.That(rangerCard.Query(className: "hs-trait--pro").ToList().Count, Is.EqualTo(2));
            Assert.That(rangerCard.Query(className: "hs-trait--con").ToList().Count, Is.EqualTo(2));
            Assert.That(page.StateOf("knight"), Is.EqualTo(HeroCardState.Current));
            Assert.That(knightCard.ClassListContains("hs-card--selected"), Is.True);
            Assert.That(page.ActionOf("knight").text, Is.EqualTo("SELECTED"));
            Assert.That(page.PriceTextOf("shadow_ranger"), Is.EqualTo("1,000"), "price shown");

            // Not enough shards: says what is missing, nothing changes.
            GemWallet.Earn(400);
            page.Refresh();
            Assert.That(page.StateOf("shadow_ranger"), Is.EqualTo(HeroCardState.Poor));
            Assert.That(page.ActionOf("shadow_ranger").text, Is.EqualTo("NEED 1,000"));
            Press(page.ActionOf("shadow_ranger"));
            Assert.That(page.ToastText, Is.EqualTo("Need 600 more Ember Shards."));
            Assert.That(GemWallet.Balance, Is.EqualTo(400));

            // Enough: first tap only asks, second tap buys and picks.
            GemWallet.Earn(800); // 1,200
            page.Refresh();
            Assert.That(page.StateOf("shadow_ranger"), Is.EqualTo(HeroCardState.Buy));
            Press(page.ActionOf("shadow_ranger"));
            Assert.That(page.Confirming, Is.Not.Null);
            Assert.That(page.ActionOf("shadow_ranger").text, Is.EqualTo("CONFIRM 1,000?"));
            Assert.That(GemWallet.Balance, Is.EqualTo(1200), "first tap charges nothing");
            Assert.That(HeroProgress.IsOwned(_roster.Find("shadow_ranger")), Is.False);
            Press(page.ActionOf("shadow_ranger"));
            Assert.That(GemWallet.Balance, Is.EqualTo(200), "charged once");
            Assert.That(HeroProgress.IsOwned(_roster.Find("shadow_ranger")), Is.True);
            Assert.That(page.Current.Id, Is.EqualTo("shadow_ranger"), "bought hero is picked");
            Assert.That(page.ActionOf("shadow_ranger").text, Is.EqualTo("SELECTED"));
            Assert.That(page.PriceTextOf("shadow_ranger"), Is.EqualTo("OWNED"));
            Assert.That(page.ToastText, Is.EqualTo("The Shadow Ranger joins you."));

            // Back to the Knight: a plain select.
            Assert.That(page.StateOf("knight"), Is.EqualTo(HeroCardState.Select));
            Press(page.ActionOf("knight"));
            Assert.That(page.Current.Id, Is.EqualTo("knight"));
            Assert.That(GemWallet.Balance, Is.EqualTo(200));

            Assert.That(menu.Back(), Is.True, "Back closes the page");
            Assert.That(page.IsOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator TestMode_RangerFree_NoteShows_PickChargesNothing_WritesNoOwnership()
        {
            var menu = Menu(out _);
            yield return null;
            menu.SetTestMode(true);
            var page = menu.HeroPage;
            menu.OpenHeroes();
            yield return null;
            Assert.That(page.TestNoteVisible, Is.True);
            Assert.That(page.StateOf("shadow_ranger"), Is.EqualTo(HeroCardState.Select));
            Assert.That(page.PriceTextOf("shadow_ranger"), Is.EqualTo("FREE: TEST MODE"));
            int shards = GemWallet.Balance, souls = SoulWallet.Balance;
            Press(page.ActionOf("shadow_ranger"));
            Assert.That(page.Current.Id, Is.EqualTo("shadow_ranger"));
            Assert.That(GemWallet.Balance, Is.EqualTo(shards));
            Assert.That(SoulWallet.Balance, Is.EqualTo(souls));
            Assert.That(HeroProgress.IsOwned(_roster.Find("shadow_ranger")), Is.False, "no fake ownership");

            menu.SetTestMode(false);
            Assert.That(page.Current.Id, Is.EqualTo("knight"), "off: real progress, back to the Knight");
            Assert.That(page.StateOf("shadow_ranger"), Is.EqualTo(HeroCardState.Poor));
            Assert.That(page.TestNoteVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator HeroesButton_HiddenWithoutRoster()
        {
            var menu = Menu(out var root, heroes: false);
            yield return null;
            Assert.That(menu.HeroPage, Is.Null);
            Assert.That(root.Q<Button>(MainMenuController.HeroesButtonName).ClassListContains("ss-hidden"), Is.True);
        }

        [Test]
        public void Scenes_WireTheShippedRoster()
        {
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(RosterPath);
            StringAssert.Contains($"heroRoster: {{fileID: 11400000, guid: {guid}",
                System.IO.File.ReadAllText("Assets/_Project/Scenes/MainMenu.unity"), "MainMenu controller wired to the roster");
            StringAssert.Contains($"roster: {{fileID: 11400000, guid: {guid}",
                System.IO.File.ReadAllText("Assets/_Project/Scenes/Game.unity"), "Game scene HeroLoadout wired to the roster");
        }

        [UnityTest]
        public IEnumerator GameScene_PickedRanger_SpawnsWithHisModelAndStats_KnightByDefault()
        {
            // Default: the Knight, no profile.
            RunState.NewRun(97, 9302);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var loadout = Object.FindAnyObjectByType<HeroLoadout>();
            Assert.That(loadout, Is.Not.Null, "HeroLoadout on the player");
            Assert.That(loadout.Hero.Id, Is.EqualTo("knight"));
            Assert.That(loadout.Model.name, Is.EqualTo(HeroLoadout.ModelName));
            Assert.That(loadout.Model.GetComponent<BowShootDriver>(), Is.Null, "Knight model");
            var knightHealth = Object.FindAnyObjectByType<PlayerHealth>().Max;
            var knightInterval = Object.FindAnyObjectByType<AutoShooter>().CurrentInterval;
            var sceneBolt = Object.FindAnyObjectByType<AutoShooter>().BoltPrefab;
            Assert.That(sceneBolt.name, Is.EqualTo("Bolt"), "Knight fires the crossbow bolt");
            var knightTurn = Object.FindAnyObjectByType<AutoShooter>().AimTurnSpeed;
            Assert.That(knightTurn, Is.EqualTo(_roster.Find("knight").AimTurnSpeed), "Knight: his own spring turn (KAN-231)");
            Assert.That(Object.FindAnyObjectByType<AutoShooter>().AimTurnSmoothing, Is.GreaterThan(0f));
            var sound = Object.FindAnyObjectByType<SurvivorSouls.Audio.GameAudio>();
            Assert.That(sound.PlayerShots[0].name, Does.StartWith("sfx_crossbow_shot"), "Knight plays the crossbow");
            var empty = SceneManager.CreateScene("HeroEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");

            // Bought + picked Ranger.
            var ranger = _roster.Find("shadow_ranger");
            GemWallet.Earn(1000);
            Assert.That(HeroProgress.TryBuy(ranger), Is.EqualTo(HeroBuyCheck.Ok));
            Assert.That(HeroProgress.Select(_roster, ranger), Is.True);
            RunState.NewRun(97, 9302);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            loadout = Object.FindAnyObjectByType<HeroLoadout>();
            var player = loadout.gameObject;
            Assert.That(loadout.Hero, Is.SameAs(ranger));
            var model = player.transform.Find(HeroLoadout.ModelName);
            Assert.That(model, Is.SameAs(loadout.Model));
            Assert.That(model.GetComponent<BowShootDriver>(), Is.Not.Null, "Ranger model under the player");
            var knight = player.transform.Find(HeroLoadout.KnightModelName);
            Assert.That(knight, Is.Not.Null);
            Assert.That(knight.gameObject.activeSelf, Is.False, "Knight model off");

            var shooter = player.GetComponent<AutoShooter>();
            var muzzle = (Transform)typeof(AutoShooter)
                .GetField("muzzle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(shooter);
            Assert.That(muzzle.name, Is.EqualTo(HeroLoadout.MuzzleName), "arrows leave the bow hand");
            Assert.That(muzzle.IsChildOf(model), Is.True);
            Assert.That(player.GetComponent<CrossbowAim>().enabled, Is.False);
            Assert.That(player.GetComponent<CrossbowRecoil>().enabled, Is.False);
            Assert.That(player.GetComponent<PlayerDeath>().UsesDeathClip, Is.True, "Ranger death clip");
            Assert.That(player.GetComponent<PlayerDeath>().Model, Is.SameAs(model));
            foreach (var r in player.GetComponent<PlayerHitFlash>().Renderers) Assert.That(r.transform.IsChildOf(model), Is.True);
            Assert.That(player.GetComponent<LocomotionAnimatorDriver>().Animator.transform.IsChildOf(model), Is.True);

            var stats = player.GetComponent<PlayerStats>();
            Assert.That(stats.Percent(StatId.AttackSpeed), Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(Object.FindAnyObjectByType<PlayerHealth>().Max, Is.EqualTo(knightHealth * 0.75f).Within(0.5f), "-25 % max health");
            Assert.That(shooter.CurrentInterval, Is.EqualTo(knightInterval / 1.3f).Within(1e-3f), "+30 % attack speed");
            Assert.That(shooter.CurrentDamage, Is.EqualTo(shooter.BaseDamage * 0.8f).Within(1e-3f), "-20 % damage");
            Assert.That(Object.FindAnyObjectByType<HordeScaler>().Power, Is.EqualTo(1f).Within(0.01f), "hero profile is the horde baseline");
            Assert.That(shooter.BoltPrefab, Is.SameAs(ranger.Projectile), "Ranger fires his arrow (KAN-227)");
            Assert.That(shooter.AimTurnSpeed, Is.EqualTo(ranger.AimTurnSpeed).And.GreaterThan(knightTurn), "Ranger turns quicker (KAN-231)");
            Assert.That(shooter.AimTurnSmoothing, Is.EqualTo(ranger.AimTurnSmoothing).And.GreaterThan(0f), "eased turn");
            Assert.That(shooter.BoltPrefab.name, Is.EqualTo("Arrow_Ranger"));
            sound = Object.FindAnyObjectByType<SurvivorSouls.Audio.GameAudio>();
            Assert.That(sound.PlayerShots, Is.EqualTo(ranger.ShotClips), "Ranger plays the bow twang");
            Assert.That(sound.PlayerShots[0].name, Does.StartWith("sfx_bow_twang"));

            empty = SceneManager.CreateScene("HeroEmpty2");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }

        [UnityTest]
        public IEnumerator Ranger_ShootsAnEnemy_PlaysTheShootStateOnTheShooterClock()
        {
            var ranger = _roster.Find("shadow_ranger");
            GemWallet.Earn(1000);
            HeroProgress.TryBuy(ranger);
            HeroProgress.Select(_roster, ranger);
            RunState.NewRun(97, 9303);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var loadout = Object.FindAnyObjectByType<HeroLoadout>();
            var shooter = loadout.GetComponent<AutoShooter>();
            var driver = loadout.Model.GetComponent<BowShootDriver>();
            var animator = driver.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no camera render in batch
            Transform hand = null;
            foreach (var tr in loadout.Model.GetComponentsInChildren<Transform>()) if (tr.name == "LeftHand") hand = tr;
            Transform restHand = null;
            foreach (var tr in ranger.ModelPrefab.GetComponentsInChildren<Transform>(true)) if (tr.name == "LeftHand") restHand = tr;
            float handOffRest = 0f;
            int shots = 0;
            void OnFired(Vector3 m, Vector3 d) => shots++;
            shooter.Fired += OnFired;
            try
            {
                float t = 0f;
                bool shooting = false;
                while (t < 12f && (shots < 2 || !shooting))
                {
                    t += Time.deltaTime;
                    shooting |= driver.Shooting && animator.GetCurrentAnimatorStateInfo(0).IsName("Shoot");
                    handOffRest = Mathf.Max(handOffRest, Quaternion.Angle(hand.localRotation, restHand.localRotation));
                    yield return null;
                }
                Assert.That(handOffRest, Is.GreaterThan(5f), "the clips move the bones (KAN-227: was the rest pose)");
                Assert.That(shots, Is.GreaterThanOrEqualTo(2), "the Ranger shoots the horde");
                Assert.That(shooting, Is.True, "Shoot state while engaged");
                int arrows = 0;
                foreach (var b in Object.FindObjectsByType<Bolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    Assert.That(b.name, Does.StartWith("Arrow_Ranger"), "every projectile in flight / pooled is his arrow (KAN-227)");
                    arrows++;
                }
                Assert.That(arrows, Is.GreaterThan(0));
            }
            finally { shooter.Fired -= OnFired; }

            var empty = SceneManager.CreateScene("HeroEmpty3");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
