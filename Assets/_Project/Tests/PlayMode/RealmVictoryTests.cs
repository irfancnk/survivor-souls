using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Realms;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.UI.Hud;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-47 (KAN-40.3) acceptance: the portal of the last Chamber ends the Realm (no next Chamber, nothing carried,
    /// Realm saved as completed, next Realm unlocked, game paused, RealmCleared once with the run), any other Chamber
    /// still advances; a stale plan never ends a run; the victory screen on the real GameHud.uxml (Realm title, unlock
    /// line, counters count up to the run's souls / kills / level); the unlock line texts; the menu opens on the Realm
    /// page once after a cleared Realm; and the real Game scene through 10 portal transitions in a row (Chamber 1 to
    /// the victory after Chamber 10) with no growth in objects and each transition timed.
    /// Test Realms <see cref="TestRealm"/> and the one after it, so the editor's real progress is never touched.
    /// </summary>
    public sealed class RealmVictoryTests
    {
        private const int TestRealm = 96;
        private const string LastRealmKey = "progress.lastRealm";
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private int _savedLastRealm;
        private bool _hadLastRealm;

        private sealed class Run
        {
            public GameObject Player;
            public PlayerHealth Health;
            public PlayerMover Mover;
            public XpManager Xp;
            public SkillManager Skills;
            public RunCounters Counters;
            public ChamberTransition Transition;
        }

        [SetUp]
        public void SetUp()
        {
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            RunProgress.Clear(TestRealm);
            RunProgress.Clear(TestRealm + 1);
            RunState.NewRun(TestRealm);
            RunState.TakeOpenRealms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
            Time.timeScale = 1f;
            RunProgress.Clear(TestRealm);
            RunProgress.Clear(TestRealm + 1);
            if (_hadLastRealm) PlayerPrefs.SetInt(LastRealmKey, _savedLastRealm);
            else PlayerPrefs.DeleteKey(LastRealmKey);
            RunState.NewRun();
            RunState.TakeOpenRealms();
            // Leave a plain Chamber 1 plan behind for the next tests.
            var go = new GameObject("PlanReset");
            go.AddComponent<ChamberSetup>().Apply(1, RunState.FirstChamber, 1);
            Object.DestroyImmediate(go);
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Run NewRun()
        {
            var r = new Run { Player = Track(new GameObject("Player")) };
            r.Player.SetActive(false);
            var stats = r.Player.AddComponent<PlayerStats>();
            r.Player.AddComponent<CharacterController>();
            r.Mover = r.Player.AddComponent<PlayerMover>();
            var shooter = r.Player.AddComponent<AutoShooter>();
            r.Health = r.Player.AddComponent<PlayerHealth>();
            r.Player.SetActive(true);
            shooter.enabled = false;

            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(10, 1.5f, 1);
            r.Xp = Track(new GameObject("Xp")).AddComponent<XpManager>();
            r.Xp.Setup(curve);
            r.Skills = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            r.Skills.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), stats, shooter, r.Health, r.Player.transform, rngSeed: 3);

            var chamber = Track(new GameObject("Chamber"));
            r.Counters = chamber.AddComponent<RunCounters>();
            r.Transition = chamber.AddComponent<ChamberTransition>();
            r.Transition.Setup(r.Player.transform, r.Health, r.Xp, r.Skills, new Behaviour[] { r.Mover }, load: false, r.Counters);
            return r;
        }

        /// <summary>Plans <paramref name="chamber"/> of a 3-Chamber test Realm and puts the run there.</summary>
        private void PlanChamber(int chamber)
        {
            RunState.JumpToChamber(chamber);
            var setup = Track(new GameObject("Setup")).AddComponent<ChamberSetup>();
            setup.Setup(Track(ChamberDifficulty.Create(chambers: 3, boss: 3)), null, null);
            setup.Apply(TestRealm, chamber, 77);
        }

        private static void TickFor(ChamberTransition t, float seconds)
        {
            for (float s = 0f; s < seconds; s += Frame) t.Tick(Frame);
        }

        private RealmCatalog TestCatalog(bool nextPlayable = true) => Track(RealmCatalog.Create(
            Track(RealmDefinition.Create(TestRealm, "Test Shore", 3)),
            Track(RealmDefinition.Create(TestRealm + 1, "Test Burg", 8, playable: nextPlayable))));

        // ------------------------------------------------------------ Transition

        [Test]
        public void LastChamber_Portal_ClearsRealm_UnlocksNext_NoNextChamber()
        {
            PlanChamber(3);
            Assert.That(ChamberSetup.Current.IsLast, Is.True, "test setup: Chamber 3 of 3 is the last");
            var r = NewRun();
            r.Counters.Restore(140, 2600);
            r.Xp.AddXp(60);
            int level = r.Xp.Level;
            var cleared = new List<RunSnapshot>();
            r.Transition.RealmCleared += cleared.Add;
            Assert.That(RunProgress.StateOf(TestRealm + 1), Is.EqualTo(RealmState.Locked));

            Assert.That(r.Transition.Enter(new Vector3(0f, 0f, 6f)), Is.True);
            TickFor(r.Transition, 1.1f);

            Assert.That(r.Transition.Current, Is.EqualTo(ChamberTransition.Phase.Cleared));
            Assert.That(RunState.Chamber, Is.EqualTo(3), "no Chamber after the last one");
            Assert.That(RunState.HasCarry, Is.False, "nothing carried");
            Assert.That(RunProgress.IsCompleted(TestRealm), Is.True);
            Assert.That(RunProgress.StateOf(TestRealm + 1), Is.EqualTo(RealmState.Unlocked));
            Assert.That(GamePause.IsPaused, Is.True, "paused under the victory screen");
            Assert.That(cleared.Count, Is.EqualTo(1));
            Assert.That(cleared[0], Is.SameAs(r.Transition.ClearedRun));
            Assert.That(cleared[0].Kills, Is.EqualTo(140));
            Assert.That(cleared[0].Souls, Is.EqualTo(2600));
            Assert.That(cleared[0].Level, Is.EqualTo(level));
            Assert.That(r.Mover.enabled, Is.False, "input stays locked");

            TickFor(r.Transition, 2f);
            Assert.That(r.Transition.Fade, Is.EqualTo(0f), "the white fades back to the world");
            Assert.That(r.Transition.Current, Is.EqualTo(ChamberTransition.Phase.Cleared), "stays until the scene is left");
            Assert.That(GamePause.IsPaused, Is.True);
            Assert.That(cleared.Count, Is.EqualTo(1), "once");
            Assert.That(r.Transition.Enter(Vector3.zero), Is.False, "no second exit");
        }

        [Test]
        public void NotLastChamber_Portal_StillAdvances()
        {
            PlanChamber(2);
            var r = NewRun();
            int cleared = 0;
            r.Transition.RealmCleared += _ => cleared++;
            Assert.That(r.Transition.Enter(new Vector3(0f, 0f, 6f)), Is.True);
            TickFor(r.Transition, 1.1f);
            Assert.That(RunState.Chamber, Is.EqualTo(3));
            Assert.That(RunState.HasCarry, Is.True);
            Assert.That(r.Transition.Current, Is.EqualTo(ChamberTransition.Phase.Loading));
            Assert.That(cleared, Is.EqualTo(0));
            Assert.That(RunProgress.IsCompleted(TestRealm), Is.False);
        }

        [Test]
        public void IsRealmEnd_OnlyForTheLastChamberOfThisRun()
        {
            RunState.JumpToChamber(3);
            Assert.That(ChamberTransition.IsRealmEnd(new ChamberPlan(TestRealm, 3, ChamberType.Boss, true, 1f, 1f, 1f, 1)), Is.True);
            Assert.That(ChamberTransition.IsRealmEnd(new ChamberPlan(TestRealm, 3, ChamberType.Boss, false, 1f, 1f, 1f, 1)), Is.False);
            Assert.That(ChamberTransition.IsRealmEnd(new ChamberPlan(TestRealm + 1, 3, ChamberType.Boss, true, 1f, 1f, 1f, 1)), Is.False,
                "stale plan of another Realm");
            Assert.That(ChamberTransition.IsRealmEnd(new ChamberPlan(TestRealm, 2, ChamberType.Boss, true, 1f, 1f, 1f, 1)), Is.False,
                "stale plan of another Chamber");
        }

        [Test]
        public void EndRealm_OpensRealmPageOnce_NewRunKeepsNothingCarried()
        {
            RunState.AdvanceChamber(new RunSnapshot { Kills = 3 });
            RunState.EndRealm();
            Assert.That(RunState.HasCarry, Is.False);
            Assert.That(RunState.TakeOpenRealms(), Is.True);
            Assert.That(RunState.TakeOpenRealms(), Is.False, "once");
        }

        // ------------------------------------------------------------ Texts

        [Test]
        public void UnlockLine_NextPlayable_ComingSoon_Last_NoCatalog()
        {
            var catalog = TestCatalog();
            Assert.That(RealmVictoryHud.UnlockLine(catalog, TestRealm, out bool unlocked), Is.EqualTo("REALM XCVII UNLOCKED · TEST BURG"));
            Assert.That(unlocked, Is.True);
            Assert.That(RealmVictoryHud.UnlockLine(TestCatalog(nextPlayable: false), TestRealm, out unlocked),
                Is.EqualTo("REALM XCVII · TEST BURG · COMING SOON"));
            Assert.That(unlocked, Is.False);
            Assert.That(RealmVictoryHud.UnlockLine(catalog, TestRealm + 1, out unlocked), Is.EqualTo("ALL REALMS CLEARED"));
            Assert.That(unlocked, Is.False);
            Assert.That(RealmVictoryHud.UnlockLine(null, 1, out unlocked), Is.EqualTo("REALM II UNLOCKED"));
            Assert.That(RealmVictoryHud.RealmTitle(catalog, TestRealm), Is.EqualTo("REALM XCVI · TEST SHORE"));
            Assert.That(RealmVictoryHud.RealmTitle(null, 1), Is.EqualTo("REALM I"));
        }

        [Test]
        public void ShippedCatalog_Realm1Victory_NamesRealm2()
        {
#if UNITY_EDITOR
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<RealmCatalog>("Assets/_Project/Data/Realms/RealmCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(RealmVictoryHud.RealmTitle(catalog, 1), Is.EqualTo("REALM I · FROSTBITTEN SHORE"));
            StringAssert.StartsWith("REALM II", RealmVictoryHud.UnlockLine(catalog, 1, out _));
#else
            Assert.Ignore("editor only");
#endif
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------ Screen

        [UnityTest]
        public IEnumerator VictoryScreen_ShowsRealmUnlockAndCountsUp()
        {
            PlanChamber(3);
            var r = NewRun();
            r.Counters.Restore(1234, 56789);
            r.Xp.AddXp(60);
            int level = r.Xp.Level;

            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            var hud = go.AddComponent<RealmVictoryHud>();
            hud.Setup(r.Transition, TestCatalog());
            go.SetActive(true);
            yield return null;

            var root = doc.rootVisualElement;
            var screen = root.Q(RealmVictoryHud.ScreenName);
            Assert.That(screen, Is.Not.Null);
            Assert.That(screen.ClassListContains("hud-hidden"), Is.True, "hidden during play");

            r.Transition.Enter(new Vector3(0f, 0f, 6f));
            TickFor(r.Transition, 1.1f);
            Assert.That(hud.IsShown, Is.True);
            Assert.That(screen.ClassListContains("hud-hidden"), Is.False);
            Assert.That(root.Q<Label>("victory-realm").text, Is.EqualTo("REALM XCVI · TEST SHORE"));
            var unlock = root.Q<Label>("victory-unlock");
            Assert.That(unlock.text, Is.EqualTo("REALM XCVII UNLOCKED · TEST BURG"));
            Assert.That(unlock.ClassListContains("victory__unlock--new"), Is.True);
            Assert.That(root.Q<Label>("victory-souls").text, Is.EqualTo("0"), "counts up from 0");
            Assert.That(root.Q<Button>("btn-victory-continue"), Is.Not.Null);

            yield return new WaitForSecondsRealtime(2.3f); // delay 0.6 + count 1.4, unscaled (the game is paused)
            Assert.That(root.Q<Label>("victory-souls").text, Is.EqualTo("56,789"));
            Assert.That(root.Q<Label>("victory-kills").text, Is.EqualTo("1,234"));
            Assert.That(root.Q<Label>("victory-level").text, Is.EqualTo(level.ToString()));
            Assert.That(screen.ClassListContains("victory--shown"), Is.True);
        }

        [UnityTest]
        public IEnumerator Menu_AfterClearedRealm_OpensOnRealmPage_Once()
        {
            RunState.EndRealm();
            var go = Track(new GameObject("Menu"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(TestCatalog());
            go.SetActive(true);
            yield return null;
            yield return null;
            Assert.That(menu.RealmSelect, Is.Not.Null);
            Assert.That(menu.RealmSelect.IsOpen, Is.True, "opens on the Realm page");
            Assert.That(RunState.OpenRealmsOnMenu, Is.False, "the request is used once");
        }

        // ------------------------------------------------------------ Real scene: 10 transitions

        /// <summary>
        /// Leak check (logic side of the device profiler check): the built Game scene from Chamber 1 through 9 portals to
        /// Chamber 10 (boss, last), then the 10th portal clears the Realm. Object counts after the 2nd and the 9th load
        /// must match (no duplicates left behind by a reload), each transition (portal to play) is timed and logged.
        /// </summary>
        /// <summary>Transforms outside enemy hierarchies; also the non-boss enemy count and what the live spawner says it made.</summary>
        private static int NonEnemyObjects(out int enemies, out int pooled)
        {
            int all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
            var list = Object.FindObjectsByType<Enemy>(FindObjectsInactive.Include);
            enemies = 0;
            int enemyTransforms = 0;
            foreach (var e in list)
            {
                enemyTransforms += e.GetComponentsInChildren<Transform>(true).Length;
                if (!e.IsBoss) enemies++; // the boss body is made by BossFight, not the spawner pools
            }
            pooled = 0;
            foreach (var spawner in Object.FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include)) pooled += spawner.Created;
            return all - enemyTransforms;
        }

        [UnityTearDown]
        public IEnumerator UnloadGameScene()
        {
            if (!SceneManager.GetSceneByName("Game").isLoaded) yield break;
            GamePause.Clear();
            var empty = SceneManager.CreateScene("RealmVictoryEmpty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }

        [UnityTest]
        public IEnumerator GameScene_TenTransitions_NoGrowth_EndsWithVictory()
        {
            RunState.NewRun(TestRealm, 4242);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            Assert.That(ChamberSetup.Current.Realm, Is.EqualTo(TestRealm));
            int chambers = Object.FindAnyObjectByType<ChamberSetup>().Difficulty.ChambersInRealm;
            Assert.That(chambers, Is.EqualTo(10), "Realm difficulty asset: 10 Chambers");

            int objectsAt2 = 0, meshesAt2 = 0, materialsAt2 = 0, texturesAt2 = 0;
            float slowest = 0f;
            var times = new List<string>();
            for (int i = 1; i <= chambers; i++)
            {
                var transition = ChamberTransition.Active;
                var player = Object.FindAnyObjectByType<PlayerHealth>();
                Assert.That(transition, Is.Not.Null, $"Chamber {i}: transition");
                Assert.That(player, Is.Not.Null, $"Chamber {i}: player");
                Assert.That(transition.Current, Is.EqualTo(ChamberTransition.Phase.Idle));
                Assert.That(RunState.Chamber, Is.EqualTo(i));

                float start = Time.realtimeSinceStartup;
                var t = player.transform;
                Assert.That(transition.Enter(t.position + t.forward * 1.5f), Is.True, $"Chamber {i}: portal");
                if (i == chambers)
                {
                    while (transition.Current != ChamberTransition.Phase.Cleared && Time.realtimeSinceStartup - start < 10f) yield return null;
                    break;
                }
                // Wait for the next scene's transition to run its fade-in (Arriving) and finish it (back to play).
                bool arriving = false;
                while (Time.realtimeSinceStartup - start < 15f)
                {
                    var next = ChamberTransition.Active;
                    if (next != null && next != transition)
                    {
                        if (next.Current == ChamberTransition.Phase.Arriving) arriving = true;
                        else if (arriving && next.Current == ChamberTransition.Phase.Idle) break;
                    }
                    yield return null;
                }
                Assert.That(arriving, Is.True, $"portal {i}: the new Chamber faded in");
                float took = Time.realtimeSinceStartup - start;
                slowest = Mathf.Max(slowest, took);
                times.Add($"{i}->{i + 1}: {took:F2}s");
                Assert.That(RunState.Chamber, Is.EqualTo(i + 1), $"portal {i} reached the next Chamber");

                yield return null;
                int objects = NonEnemyObjects(out int enemies, out int pooled);
                Assert.That(enemies, Is.EqualTo(pooled), "every enemy belongs to this scene's spawner pools");
                int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
                int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
                Assert.That(Object.FindObjectsByType<ChamberTransition>().Length, Is.EqualTo(1), "one transition");
                Assert.That(Object.FindObjectsByType<PlayerHealth>().Length, Is.EqualTo(1), "one Knight");
                if (i + 1 == 2)
                {
                    objectsAt2 = objects;
                    meshesAt2 = meshes;
                    materialsAt2 = materials;
                    texturesAt2 = textures;
                }
                else if (i + 1 == chambers - 1)
                {
                    // Chamber 9 (normal) against Chamber 2 (normal). Enemy pools grow with the Chamber cap on purpose (KAN-46
                    // prewarm), so enemy hierarchies are left out; everything else must be the same.
                    Debug.Log($"[RealmVictoryTests] objects without enemies {objectsAt2}->{objects}, enemies {enemies}, meshes {meshesAt2}->{meshes}, " +
                              $"materials {materialsAt2}->{materials}, textures {texturesAt2}->{textures}");
                    Assert.That(objects, Is.LessThanOrEqualTo(objectsAt2 + 20), "scene objects do not pile up");
                    Assert.That(meshes, Is.LessThanOrEqualTo(meshesAt2 + 10), "meshes do not pile up");
                    Assert.That(materials, Is.LessThanOrEqualTo(materialsAt2 + 10), "materials do not pile up");
                    Assert.That(textures, Is.LessThanOrEqualTo(texturesAt2 + 10), "textures do not pile up");
                }
            }
            Debug.Log("[RealmVictoryTests] transitions " + string.Join(", ", times) + $" (slowest {slowest:F2}s)");

            var last = ChamberTransition.Active;
            Assert.That(last.Current, Is.EqualTo(ChamberTransition.Phase.Cleared), "the 10th portal clears the Realm");
            Assert.That(RunProgress.IsCompleted(TestRealm), Is.True);
            yield return null;
            Assert.That(Object.FindAnyObjectByType<RealmVictoryHud>().IsShown, Is.True, "victory screen");
            Assert.That(slowest, Is.LessThan(4f), "portal to play (pull 0.5 + fade 0.4 + load + fade-in 0.45) in the editor");
        }
#endif
    }
}
