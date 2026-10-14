using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Telemetry;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Skill debug tools and telemetry (KAN-75 = KAN-50.6): <see cref="SkillManager.SetLevel(SkillDefinition, int)"/> (up,
    /// down, 0 removes, clamp, effect told the level / destroyed, no pick event), the change source, telemetry events for
    /// skill changes and chests (dropped / opened / expired, reward source), the ring buffer and no GC per event, the power
    /// curve (stat math, shipped table rises with level, repeatable, no leftovers), drop statistics per Reliquary Hunter
    /// level, and the Game scene wiring.
    /// </summary>
    public sealed class SkillDebugTelemetryTests
    {
        private const string HunterPath = "Assets/_Project/Data/Skills/Skill_ReliquaryHunter.asset";
        private const string ConfigPath = "Assets/_Project/Data/Chests/ChestDropConfig.asset";
        private const string ProgressionPath = "Assets/_Project/Data/Skills/SkillProgression_Run.asset";
        private const int TestRealm = 92; // the Game scene test never touches real Realm progress
        private const string LastRealmKey = "progress.lastRealm";

        private readonly List<Object> _objects = new List<Object>();
        private readonly List<(int from, int to, SkillChangeSource source)> _changes = new List<(int, int, SkillChangeSource)>();
        private GameObject _player;
        private PlayerStats _stats;
        private SkillManager _manager;
        private int _granted;
        private bool _savedLog;
        private int _savedLastRealm;
        private bool _hadLastRealm;

        private sealed class LevelProbe : SkillEffect
        {
            public readonly List<int> Levels = new List<int>();
            protected override void Subscribe() { }
            protected override void Unsubscribe() { }
            protected override void OnStacksChanged(int stacks) => Levels.Add(stacks);
        }

        [SetUp]
        public void SetUp()
        {
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            _savedLog = GameTelemetry.LogToConsole;
            GameTelemetry.LogToConsole = false;
            GameTelemetry.Clear();
            _player = Track(new GameObject("Player"));
            _stats = _player.AddComponent<PlayerStats>();
            var table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(table, _stats, null, null, _player.transform, rngSeed: 75);
            _manager.SkillChanged += (s, a, b) => _changes.Add((a, b, _manager.LastChangeSource));
            _manager.Granted += (s, l) => _granted++;
            Enemy.Walkability = null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _changes.Clear();
            _granted = 0;
            GameTelemetry.Clear();
            GameTelemetry.LogToConsole = _savedLog;
            GamePause.Clear();
            Time.timeScale = 1f;
            Enemy.Walkability = null;
            RunProgress.Clear(TestRealm);
            if (_hadLastRealm) PlayerPrefs.SetInt(LastRealmKey, _savedLastRealm);
            else PlayerPrefs.DeleteKey(LastRealmKey);
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);
        private static StatModifier Pct(StatId s, float v) => new StatModifier(s, ModifierKind.Percent, v);

        /// <summary>Damage +10 / +25 / +50, level 3 also pierces one enemy.</summary>
        private SkillDefinition Laddered(string id = "ladder") =>
            Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure(id, id, id + " text", SkillRarity.Common, 1)
                .SetLevels(
                    new SkillLevelData("+10 damage", Add(StatId.Damage, 10f)),
                    new SkillLevelData("+25 damage", Add(StatId.Damage, 25f)),
                    new SkillLevelData("", Add(StatId.Damage, 50f), Add(StatId.Pierce, 1f)));

        private ReliquaryChest MakeChestPrefab()
        {
            var root = Track(new GameObject("Chest Template"));
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var beam = new GameObject("Beam").AddComponent<MeshRenderer>();
            beam.transform.SetParent(root.transform, false);
            var glow = new GameObject("Glow").AddComponent<MeshRenderer>();
            glow.transform.SetParent(root.transform, false);
            var chest = root.AddComponent<ReliquaryChest>();
            chest.Setup(body.transform, beam, glow);
            root.SetActive(false);
            return chest;
        }

        // ------------------------------------------------------------ SetLevel

        [Test]
        public void SetLevel_GoesUpDownAndZeroRemoves_StatsFollowTheTable()
        {
            var s = Laddered();
            Assert.That(_manager.SetLevel(s, 3), Is.True, "not owned -> level 3 in one step");
            Assert.That(_manager.GetLevel(s), Is.EqualTo(3));
            Assert.That(_stats.Flat(StatId.Damage), Is.EqualTo(50f));
            Assert.That(_stats.Flat(StatId.Pierce), Is.EqualTo(1f));

            Assert.That(_manager.SetLevel(s, 1), Is.True, "down");
            Assert.That(_manager.GetLevel(s), Is.EqualTo(1));
            Assert.That(_stats.Flat(StatId.Damage), Is.EqualTo(10f));
            Assert.That(_stats.Flat(StatId.Pierce), Is.EqualTo(0f));

            Assert.That(_manager.SetLevel(s, 0), Is.True, "0 removes");
            Assert.That(_manager.GetLevel(s), Is.EqualTo(0));
            Assert.That(_manager.OwnedSkills, Is.Empty);
            Assert.That(_stats.Flat(StatId.Damage), Is.EqualTo(0f));

            Assert.That(_changes, Is.EqualTo(new[]
            {
                (0, 3, SkillChangeSource.Debug), (3, 1, SkillChangeSource.Debug), (1, 0, SkillChangeSource.Debug),
            }));
            Assert.That(_granted, Is.Zero, "a debug level set is not a level-up pick (no pick sound / pop)");
        }

        [Test]
        public void SetLevel_ClampsToMax_SameLevelNullAndUnknownAreFalse()
        {
            var s = Laddered();
            Assert.That(_manager.SetLevel(s, 99), Is.True);
            Assert.That(_manager.GetLevel(s), Is.EqualTo(3), "clamped to max");
            Assert.That(_manager.SetLevel(s, 3), Is.False, "already there");
            Assert.That(_manager.SetLevel(s, -4), Is.True);
            Assert.That(_manager.GetLevel(s), Is.EqualTo(0), "below 0 = removed");
            Assert.That(_manager.SetLevel(s, 0), Is.False, "not owned and 0");
            Assert.That(_manager.SetLevel((SkillDefinition)null, 2), Is.False);
            Assert.That(_manager.SetLevel("no_such_skill", 2), Is.False);
        }

        [UnityTest]
        public IEnumerator SetLevel_TellsTheEffectTheLevel_ZeroDestroysIt()
        {
            var s = Laddered("fx");
            s.SetPresentation(null, Track(new GameObject("Probe FX")).AddComponent<LevelProbe>());
            _manager.SetLevel(s, 2);
            var probe = _player.GetComponentInChildren<LevelProbe>();
            Assert.That(probe, Is.Not.Null, "effect spawned under the player");
            _manager.SetLevel(s, 1);
            Assert.That(probe.Levels, Is.EqualTo(new[] { 2, 1 }));
            _manager.SetLevel(s, 0);
            yield return null;
            Assert.That(probe == null, Is.True, "effect destroyed with the skill");
            _manager.SetLevel(s, 3);
            Assert.That(_player.GetComponentsInChildren<LevelProbe>().Length, Is.EqualTo(1), "a fresh effect, not a second one");
        }

        [Test]
        public void SkillChanged_ReportsItsSource_PickUpgradeDebug()
        {
            var s = Laddered();
            _manager.Grant(s);
            _manager.Upgrade(s);
            _manager.SetLevel(s, 1);
            _manager.Restore(s, 3); // silent Chamber carry: no event
            Assert.That(_changes.Select(c => c.source), Is.EqualTo(new[]
            {
                SkillChangeSource.Pick, SkillChangeSource.Upgrade, SkillChangeSource.Debug,
            }));
        }

        // ------------------------------------------------------------ Telemetry

        [Test]
        public void Telemetry_SkillUpgraded_ForPickUpgradeAndDebug_NotForRestore()
        {
            var telemetry = Track(new GameObject("Telemetry")).AddComponent<RunTelemetry>();
            telemetry.Setup(_manager, null);
            var s = Laddered("tele");
            _manager.Grant(s);
            _manager.Upgrade(s);
            _manager.SetLevel(s, 0);
            _manager.Restore(s, 2);
            Assert.That(GameTelemetry.TotalCount, Is.EqualTo(3));
            var events = Enumerable.Range(0, 3).Select(GameTelemetry.Recent).Reverse().ToArray();
            Assert.That(events.All(e => e.Name == GameTelemetry.SkillUpgraded && e.Subject == "tele"), Is.True);
            Assert.That(events.Select(e => (e.From, e.To, e.Source)), Is.EqualTo(new[]
            {
                (0, 1, "pick"), (1, 2, "upgrade"), (2, 0, "debug"),
            }));
        }

        [Test]
        public void Telemetry_ChestDroppedOpenedExpired_WithTierChoicesAndSource()
        {
            var config = Track(ChestDropConfig.Create(cap: 3, life: 10f, warning: 2f, choices: 2, rareLevels: 2));
            config.SetOpening(1.2f, 0f, 0.4f);
            var dropper = Track(new GameObject("Chests")).AddComponent<ChestDropper>();
            dropper.Setup(config, _stats, MakeChestPrefab(), rngSeed: 75);
            var telemetry = Track(new GameObject("Telemetry")).AddComponent<RunTelemetry>();
            telemetry.Setup(null, dropper);

            var rare = dropper.DropNow(new Vector3(3f, 0f, 0f), ChestTier.Rare);
            var e = GameTelemetry.Recent(0);
            Assert.That((e.Name, e.Subject, e.From, e.To, e.Source), Is.EqualTo((GameTelemetry.ChestDropped, "Rare", 2, 2, "skill")));
            Assert.That(e.Position.x, Is.EqualTo(rare.transform.position.x).Within(1e-4f));

            dropper.DropReward(new Vector3(-3f, 0f, 0f), ChestTier.Light, 3, 1);
            e = GameTelemetry.Recent(0);
            Assert.That((e.Name, e.Subject, e.From, e.To, e.Source), Is.EqualTo((GameTelemetry.ChestDropped, "Light", 3, 1, "reward")));

            Assert.That(dropper.Open(rare), Is.True);
            dropper.Tick(0.5f);
            e = GameTelemetry.Recent(0);
            Assert.That((e.Name, e.Subject, e.Source), Is.EqualTo((GameTelemetry.ChestOpened, "Rare", "skill")));

            dropper.DropNow(Vector3.zero, ChestTier.Light);
            dropper.Tick(11f); // past the 10 s lifetime; the reward chest never fades
            e = GameTelemetry.Recent(0);
            Assert.That((e.Name, e.Subject, e.Source), Is.EqualTo((GameTelemetry.ChestExpired, "Light", "skill")));
            Assert.That(GameTelemetry.TotalCount, Is.EqualTo(5));
            Assert.That(dropper.Active.Count, Is.EqualTo(1), "the reward chest is still there");
        }

        [Test]
        public void Telemetry_RingBufferKeepsTheNewest64_AndRaisesTracked()
        {
            int heard = 0;
            System.Action<TelemetryEvent> listen = _ => heard++;
            GameTelemetry.Tracked += listen;
            try
            {
                for (int i = 0; i < 70; i++)
                    GameTelemetry.Track(new TelemetryEvent("Test", null, i, i, null, 0f, Vector3.zero));
            }
            finally
            {
                GameTelemetry.Tracked -= listen;
            }
            Assert.That(heard, Is.EqualTo(70));
            Assert.That(GameTelemetry.TotalCount, Is.EqualTo(70));
            Assert.That(GameTelemetry.RecentCount, Is.EqualTo(GameTelemetry.RecentCapacity));
            Assert.That(GameTelemetry.Recent(0).From, Is.EqualTo(69), "0 = newest");
            Assert.That(GameTelemetry.Recent(63).From, Is.EqualTo(6), "oldest kept");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => GameTelemetry.Recent(64));
        }

        [Test]
        public void Telemetry_TrackAndChestEvents_DoNotAllocate()
        {
            var e = new TelemetryEvent(GameTelemetry.ChestDropped, "Light", 2, 1, "skill", 1f, Vector3.one);
            GameTelemetry.Track(e); // warm
            Assert.That(() => { for (int i = 0; i < 100; i++) GameTelemetry.Track(e); }, Is.Not.AllocatingGCMemory());

            var config = Track(ChestDropConfig.Create(cap: 2, life: 5f, warning: 1f));
            var dropper = Track(new GameObject("Chests")).AddComponent<ChestDropper>();
            dropper.Setup(config, _stats, MakeChestPrefab(), rngSeed: 75);
            var telemetry = Track(new GameObject("Telemetry")).AddComponent<RunTelemetry>();
            telemetry.Setup(null, dropper);
            dropper.DropNow(Vector3.zero, ChestTier.Light); // warm the pool and the handlers
            dropper.Tick(6f);
            Assert.That(() =>
            {
                dropper.DropNow(Vector3.zero, ChestTier.Light);
                dropper.Tick(6f);
            }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Power curve

        [Test]
        public void PowerSnapshot_MatchesTheCombatFormulas()
        {
            var b = PowerBase.Default;
            var empty = PowerSnapshot.Measure(_stats, b);
            Assert.That(empty.SingleTargetDps, Is.EqualTo(100f).Within(1e-3f), "100 dmg x 1 shot/s, no crit");
            Assert.That(empty.CrowdDps, Is.EqualTo(100f).Within(1e-3f));

            _stats.SetSource(_player, new[]
            {
                Pct(StatId.Damage, 0.2f), Add(StatId.AttackSpeed, 0.5f), Add(StatId.CritChance, 0.5f), Add(StatId.Multishot, 1f),
                Add(StatId.Pierce, 1f), Add(StatId.BurnDps, 25f), Add(StatId.MaxHealth, 80f),
            }, 1);
            var s = PowerSnapshot.Measure(_stats, b, 4);
            Assert.That(s.BoltDamage, Is.EqualTo(120f).Within(1e-3f));
            Assert.That(s.ShotsPerSecond, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(s.CritFactor, Is.EqualTo(1.5f).Within(1e-4f), "50 % x (2 - 1)");
            Assert.That(s.HitsPerVolley, Is.EqualTo(4f), "2 bolts x (1 + 1 pierce)");
            Assert.That(s.SingleTargetDps, Is.EqualTo(120f * 1.5f * 1.5f + 25f).Within(1e-3f));
            // Crowd: the angled Spread Shot bolt counts at its 50 % base share (KAN-83): (1 + 0.5) bolts x (1 + 1 pierce).
            Assert.That(s.CrowdDps, Is.EqualTo((120f * 1.5f * 1.5f + 25f) * 1.5f * 2f).Within(1e-2f));
            Assert.That(s.MaxHealth, Is.EqualTo(480f));
            Assert.That(s.Skills, Is.EqualTo(4f));
        }

        [Test]
        public void PowerCurve_ShippedTable_RisesWithLevel_IsRepeatable_LeavesNothingBehind()
        {
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            Assert.That(table, Is.Not.Null);
            var curve = SkillPowerCurve.Simulate(table, PowerBase.Default, runs: 60, maxLevel: 12, seed: 7);
            Assert.That(curve.PowerAt(1), Is.EqualTo(1f).Within(1e-4f), "level 1 = no skills");
            Assert.That(curve.AtLevel(1).Skills, Is.Zero);
            for (int l = 2; l <= 12; l++)
            {
                Assert.That(curve.PowerAt(l), Is.GreaterThanOrEqualTo(curve.PowerAt(l - 1) - 1e-4f), $"level {l}");
                Assert.That(curve.AtLevel(l).Skills, Is.LessThanOrEqualTo(l - 1 + 1e-4f), "at most one new skill per pick");
            }
            Assert.That(curve.PowerAt(12), Is.GreaterThan(2f), "11 picks at least double the crowd damage");
            Assert.That(curve.ToString(), Does.Contain("crowd DPS"));

            var again = SkillPowerCurve.Simulate(table, PowerBase.Default, runs: 60, maxLevel: 12, seed: 7);
            Assert.That(again.PowerAt(12), Is.EqualTo(curve.PowerAt(12)).Within(1e-5f), "same seed, same curve");
            Assert.That(Resources.FindObjectsOfTypeAll<SkillManager>().Count(m => m.name.StartsWith("Power Curve")), Is.Zero);
        }

        // ------------------------------------------------------------ Drop statistics

        [Test]
        public void DropStatisticsPerLevel_ShippedHunter_RateRisesAndMatchesTheChances()
        {
            var hunter = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(HunterPath);
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<ChestDropConfig>(ConfigPath);
            Assert.That(hunter, Is.Not.Null);
            Assert.That(config, Is.Not.Null);
            const int kills = 40000;
            var rows = ChestDropper.SimulatePerLevel(config, hunter, kills, 0.1f, 0f, 75); // pity off: chance only
            Assert.That(rows.Length, Is.EqualTo(hunter.MaxLevel));
            for (int i = 0; i < rows.Length; i++)
            {
                hunter.ApplyLevel(_stats, i + 1);
                float normal = PlayerStats.Eval(_stats, StatId.ChestChance, 0f);
                float elite = PlayerStats.Eval(_stats, StatId.ChestEliteChance, 0f);
                float expected = 0.9f * normal + 0.1f * elite;
                Assert.That(rows[i].Kills, Is.EqualTo(kills));
                Assert.That(rows[i].PerKill, Is.EqualTo(expected).Within(expected * 0.1f), $"level {i + 1}");
                if (i > 0) Assert.That(rows[i].PerKill, Is.GreaterThan(rows[i - 1].PerKill), $"level {i + 1} drops more");
                Assert.That(rows[i].PityDrops, Is.Zero);
            }

            var paced = ChestDropper.SimulatePerLevel(config, hunter, 1000, 0.1f, 0.6f, 75);
            Assert.That(paced[0].PityDrops, Is.GreaterThan(0), "a kill every 0.6 s: the 90 s pity fires at level 1");
            Assert.That(paced[0].Seconds, Is.EqualTo(600f).Within(0.1f));
            Assert.That(Resources.FindObjectsOfTypeAll<ChestDropper>().Count(d => d.name.StartsWith("Chest Statistics")), Is.Zero);
        }

        // ------------------------------------------------------------ Game scene

        [UnityTest]
        public IEnumerator GameScene_HasRunTelemetry_SkillAndChestEventsAreTracked()
        {
            RunState.NewRun(TestRealm, 7575);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var telemetry = Object.FindAnyObjectByType<RunTelemetry>();
            var skills = Object.FindAnyObjectByType<SkillManager>();
            var dropper = Object.FindAnyObjectByType<ChestDropper>();
            Assert.That(telemetry, Is.Not.Null);
            GameTelemetry.Clear();

            Assert.That(skills.SetLevel("reliquary_hunter", 3), Is.True);
            var e = GameTelemetry.Recent(0);
            Assert.That((e.Name, e.Subject, e.From, e.To, e.Source),
                Is.EqualTo((GameTelemetry.SkillUpgraded, "reliquary_hunter", 0, 3, "debug")));
            Assert.That(dropper.DropNow(skills.Stats.transform.position + Vector3.forward * 3f, ChestTier.Rare), Is.Not.Null);
            e = GameTelemetry.Recent(0);
            Assert.That((e.Name, e.Subject), Is.EqualTo((GameTelemetry.ChestDropped, "Rare")));

            var empty = SceneManager.CreateScene("TelemetryEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
