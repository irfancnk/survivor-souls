using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-45 (KAN-40.1) acceptance, logic side (the fade, card and sound need screenshots and a phone): the run survives
    /// a capture/apply round trip (level, XP, total XP, skills with stacks, health; max-health skills raise the max), the
    /// portal starts the exit once (never for a dead Knight), locks the listed behaviours and the spawner, pulls the Knight
    /// into the arch, fades to white and advances the Chamber with the run carried; the arrival holds a pause through the
    /// fade-in (taken again if the old scene clears it) and raises Arrived once; a new run resets everything; progress
    /// save keeps the last and best Chamber per Realm; the title card text.
    /// Uses Realm <see cref="TestRealm"/> so the editor's real Realm 1 progress is never touched.
    /// </summary>
    public sealed class ChamberTransitionTests
    {
        private const int TestRealm = 98;
        private const string LastRealmKey = "progress.lastRealm";
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private int _savedLastRealm;
        private bool _hadLastRealm;

        private sealed class Run
        {
            public GameObject Player;
            public PlayerStats Stats;
            public PlayerMover Mover;
            public AutoShooter Shooter;
            public PlayerHealth Health;
            public XpManager Xp;
            public SkillManager Skills;
            public ChamberTransition Transition;
        }

        [SetUp]
        public void SetUp()
        {
            _hadLastRealm = PlayerPrefs.HasKey(LastRealmKey);
            _savedLastRealm = PlayerPrefs.GetInt(LastRealmKey, 1);
            RunProgress.Clear(TestRealm);
            RunState.NewRun(TestRealm);
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
            if (_hadLastRealm) PlayerPrefs.SetInt(LastRealmKey, _savedLastRealm);
            else PlayerPrefs.DeleteKey(LastRealmKey);
            RunState.NewRun();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Run NewRun()
        {
            var r = new Run { Player = Track(new GameObject("Player")) };
            r.Player.SetActive(false); // Awake after every component exists (readers grab PlayerStats in Awake)
            r.Stats = r.Player.AddComponent<PlayerStats>();
            r.Player.AddComponent<CharacterController>();
            r.Mover = r.Player.AddComponent<PlayerMover>();
            r.Shooter = r.Player.AddComponent<AutoShooter>();
            r.Health = r.Player.AddComponent<PlayerHealth>();
            r.Player.SetActive(true);
            r.Shooter.enabled = false;

            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(10, 1.5f, 1);
            r.Xp = Track(new GameObject("Xp")).AddComponent<XpManager>();
            r.Xp.Setup(curve);
            r.Skills = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            r.Skills.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), r.Stats, r.Shooter, r.Health, r.Player.transform, rngSeed: 7);

            r.Transition = Track(new GameObject("Chamber")).AddComponent<ChamberTransition>();
            r.Transition.Setup(r.Player.transform, r.Health, r.Xp, r.Skills, new Behaviour[] { r.Mover }, load: false);
            return r;
        }

        private SkillDefinition Skill(string id, int max, params StatModifier[] mods) =>
            Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure(id, id, id, SkillRarity.Common, max, mods);

        private static void TickFor(ChamberTransition t, float seconds)
        {
            for (float s = 0f; s < seconds; s += Frame) t.Tick(Frame);
        }

        [Test]
        public void CaptureApply_RoundTrip_KeepsLevelXpSkillsAndHealth()
        {
            var a = NewRun();
            var vigor = Skill("vigor", 3, new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 20f));
            var power = Skill("power", 5, new StatModifier(StatId.Damage, ModifierKind.Percent, 0.1f));
            a.Skills.Grant(vigor);
            a.Skills.Grant(vigor);
            a.Skills.Grant(power);
            a.Xp.AddXp(37);
            a.Health.TakeDamage(25f);
            float maxA = a.Health.Max, hpA = a.Health.Current;
            Assert.Greater(a.Xp.Level, 1, "test setup: some levels gained");

            var snap = ChamberTransition.Capture(a.Xp, a.Skills, a.Health);
            var b = NewRun();
            int levelUps = 0;
            b.Xp.OnLevelUp += _ => levelUps++;
            ChamberTransition.Apply(snap, b.Xp, b.Skills, b.Health, heal: 0f);

            Assert.AreEqual(a.Xp.Level, b.Xp.Level);
            Assert.AreEqual(a.Xp.Xp, b.Xp.Xp);
            Assert.AreEqual(a.Xp.TotalXp, b.Xp.TotalXp);
            Assert.AreEqual(0, levelUps, "restored levels were picked already: no new skill pick");
            Assert.AreEqual(2, b.Skills.StacksOf(vigor));
            Assert.AreEqual(1, b.Skills.StacksOf(power));
            Assert.AreEqual(maxA, b.Health.Max, 0.001f, "max-health skill raised the max again");
            Assert.AreEqual(hpA, b.Health.Current, 0.001f);
        }

        [Test]
        public void Apply_HealOnArrive_IsCappedAtMax()
        {
            var a = NewRun();
            a.Health.TakeDamage(a.Health.Max * 0.5f);
            var snap = ChamberTransition.Capture(a.Xp, a.Skills, a.Health);
            var b = NewRun();
            ChamberTransition.Apply(snap, b.Xp, b.Skills, b.Health, heal: 0.25f);
            Assert.AreEqual(b.Health.Max * 0.75f, b.Health.Current, 0.01f);
            ChamberTransition.Apply(snap, b.Xp, b.Skills, b.Health, heal: 2f);
            Assert.AreEqual(b.Health.Max, b.Health.Current, 0.001f);
        }

        [Test]
        public void Enter_StartsOnce_LocksPullsFadesAndAdvancesChamber()
        {
            var r = NewRun();
            int departing = 0;
            r.Transition.Departing += () => departing++;
            r.Xp.AddXp(15);
            Vector3 portal = new Vector3(0f, 0f, 6f);

            Assert.IsTrue(r.Transition.Enter(portal));
            Assert.IsFalse(r.Transition.Enter(portal), "second entry ignored");
            Assert.AreEqual(1, departing);
            Assert.IsFalse(r.Mover.enabled, "movement locked");
            Assert.AreEqual(ChamberTransition.Phase.Pulling, r.Transition.Current);

            TickFor(r.Transition, 0.55f);
            Assert.Greater(r.Player.transform.position.z, portal.z, "drawn through the arch");
            Assert.AreEqual(ChamberTransition.Phase.FadingOut, r.Transition.Current);
            Assert.AreEqual(RunState.FirstChamber, RunState.Chamber, "Chamber advances only at full white");

            TickFor(r.Transition, 0.45f);
            Assert.AreEqual(ChamberTransition.Phase.Loading, r.Transition.Current);
            Assert.AreEqual(1f, r.Transition.Fade);
            Assert.AreEqual(2, RunState.Chamber);
            Assert.IsTrue(RunState.HasCarry);
            var carry = RunState.TakeCarry();
            Assert.AreEqual(r.Xp.Level, carry.Level);
            Assert.IsFalse(RunState.HasCarry, "the carry is handed over once");
        }

        [Test]
        public void Enter_IgnoredWhenDead()
        {
            var r = NewRun();
            r.Health.TakeDamage(10000f);
            Assert.IsFalse(r.Transition.Enter(Vector3.forward * 3f));
            Assert.AreEqual(ChamberTransition.Phase.Idle, r.Transition.Current);
            Assert.IsTrue(r.Mover.enabled);
        }

        [Test]
        public void BeginChamber_WithCarry_HoldsPauseThroughFadeIn_AndRaisesArrivedOnce()
        {
            var r = NewRun();
            var arrived = new List<int>();
            r.Transition.Arrived += c => arrived.Add(c);
            RunState.AdvanceChamber(new RunSnapshot { Level = 4, Xp = 3, TotalXp = 80, Health = 50f });

            r.Transition.BeginChamber(RunState.TakeCarry());
            Assert.AreEqual(new[] { 2 }, arrived.ToArray());
            Assert.AreEqual(4, r.Xp.Level);
            Assert.AreEqual(ChamberTransition.Phase.Arriving, r.Transition.Current);
            Assert.AreEqual(1f, r.Transition.Fade);
            Assert.IsTrue(GamePause.IsPaused);

            GamePause.Clear(); // the old scene's controller clears pauses on destroy, possibly after our Start
            r.Transition.Tick(Frame);
            Assert.IsTrue(GamePause.IsPaused, "the hold is taken again until the fade-in ends");

            TickFor(r.Transition, 0.6f);
            Assert.AreEqual(ChamberTransition.Phase.Idle, r.Transition.Current);
            Assert.AreEqual(0f, r.Transition.Fade);
            Assert.IsFalse(GamePause.IsPaused);
            Assert.AreEqual(1, arrived.Count);
        }

        [Test]
        public void BeginChamber_Fresh_NoPauseNoArrival_SavesProgress()
        {
            var r = NewRun();
            int arrived = 0;
            r.Transition.Arrived += _ => arrived++;
            r.Transition.BeginChamber(null);
            Assert.AreEqual(0, arrived);
            Assert.IsFalse(GamePause.IsPaused);
            Assert.AreEqual(ChamberTransition.Phase.Idle, r.Transition.Current);
            Assert.AreEqual(TestRealm, RunProgress.LastRealm);
            Assert.AreEqual(1, RunProgress.LastChamber(TestRealm));
        }

        [Test]
        public void NewRun_ResetsChamberAndDropsCarry()
        {
            RunState.AdvanceChamber(new RunSnapshot());
            RunState.AdvanceChamber(new RunSnapshot());
            Assert.AreEqual(3, RunState.Chamber);
            RunState.NewRun(TestRealm);
            Assert.AreEqual(RunState.FirstChamber, RunState.Chamber);
            Assert.AreEqual(TestRealm, RunState.Realm);
            Assert.IsFalse(RunState.HasCarry);
            Assert.IsNull(RunState.TakeCarry());
        }

        [Test]
        public void RunProgress_KeepsLastAndBestChamber()
        {
            Assert.AreEqual(0, RunProgress.BestChamber(TestRealm));
            RunProgress.RecordChamberStart(TestRealm, 3);
            RunProgress.RecordChamberStart(TestRealm, 2);
            Assert.AreEqual(2, RunProgress.LastChamber(TestRealm));
            Assert.AreEqual(3, RunProgress.BestChamber(TestRealm));
            Assert.AreEqual(TestRealm, RunProgress.LastRealm);
        }

        [Test]
        public void TitleCard_UsesRomanNumerals()
        {
            Assert.AreEqual("CHAMBER II", ChamberTransitionHud.CardTitle(2));
            Assert.AreEqual("I", ChamberTransitionHud.Roman(1));
            Assert.AreEqual("IV", ChamberTransitionHud.Roman(4));
            Assert.AreEqual("IX", ChamberTransitionHud.Roman(9));
            Assert.AreEqual("XIV", ChamberTransitionHud.Roman(14));
            Assert.AreEqual("XL", ChamberTransitionHud.Roman(40));
            Assert.AreEqual("0", ChamberTransitionHud.Roman(0));
        }
    }
}
