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
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Keeps the editor's real wallets (Souls KAN-96, Ember Shards + granted bonuses KAN-97) out of the PlayMode suite:
    /// every test that runs the Game scene to a death, a cleared Realm or a menu exit banks souls (and may grant the
    /// first-run shard bonus). Saved before the first test of the assembly, put back after the last.
    /// Soul tree levels (KAN-98) are saved too and zeroed for the suite: every Game scene test sees the bare Knight, even
    /// when nodes were bought in the editor.
    /// </summary>
    [SetUpFixture]
    public sealed class WalletPrefsGuard
    {
        private readonly Dictionary<string, int> _saved = new Dictionary<string, int>();

        private static IEnumerable<string> Keys()
        {
            yield return SoulWallet.KeyBalance;
            yield return SoulWallet.KeyCheck;
            foreach (var k in GemWallet.Keys) yield return k;
            foreach (var k in TreeKeys()) yield return k;
        }

        private static IEnumerable<string> TreeKeys()
        {
            var tree = UnityEditor.AssetDatabase.LoadAssetAtPath<SurvivorSouls.Gameplay.Meta.SoulTree>("Assets/_Project/Data/Economy/SoulTree.asset");
            if (tree == null) yield break;
            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                string key = SurvivorSouls.Gameplay.Meta.SoulTreeProgress.KeyOf(node);
                yield return key;
                yield return key + ".check";
            }
        }

        [OneTimeSetUp]
        public void Save()
        {
            _saved.Clear();
            foreach (var k in Keys())
                if (PlayerPrefs.HasKey(k)) _saved[k] = PlayerPrefs.GetInt(k);
            foreach (var k in TreeKeys()) PlayerPrefs.DeleteKey(k); // no bought levels during the suite
        }

        [OneTimeTearDown]
        public void Restore()
        {
            foreach (var k in Keys())
            {
                if (_saved.TryGetValue(k, out int v)) PlayerPrefs.SetInt(k, v);
                else PlayerPrefs.DeleteKey(k);
            }
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// KAN-96 run end banking: the run's souls go into the wallet once per run (a second call banks nothing, a new run banks
    /// again), death banks at once, the last Chamber's portal (Realm cleared) banks, a portal to the next Chamber does not,
    /// the Banked event, the HUD counter text cache, the wallet line text, and the element names on the real UXML files.
    /// </summary>
    public sealed class SoulBankTests
    {
        private const int TestRealm = 95;
        private const string LastRealmKey = "progress.lastRealm";
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private int _savedLastRealm;
        private bool _hadLastRealm;

        private sealed class Run
        {
            public GameObject Player;
            public PlayerHealth Health;
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
            SoulWallet.Clear();
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
            var mover = r.Player.AddComponent<PlayerMover>();
            var shooter = r.Player.AddComponent<AutoShooter>();
            r.Health = r.Player.AddComponent<PlayerHealth>();
            r.Player.SetActive(true);
            shooter.enabled = false;

            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(10, 1.5f, 1);
            var xp = Track(new GameObject("Xp")).AddComponent<XpManager>();
            xp.Setup(curve);
            var skills = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            skills.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), stats, shooter, r.Health, r.Player.transform, rngSeed: 3);

            var chamber = Track(new GameObject("Chamber"));
            r.Counters = chamber.AddComponent<RunCounters>();
            r.Transition = chamber.AddComponent<ChamberTransition>();
            r.Transition.Setup(r.Player.transform, r.Health, xp, skills, new Behaviour[] { mover }, load: false, r.Counters);
            r.Counters.SetupRunEnds(r.Health, r.Transition);
            return r;
        }

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

        [Test]
        public void BankToWallet_OncePerRun_NewRunBanksAgain()
        {
            var counters = Track(new GameObject("Chamber")).AddComponent<RunCounters>();
            counters.Restore(12, 500);
            int events = 0, lastAmount = -1, lastBalance = -1;
            counters.Banked += (amount, balance) => { events++; lastAmount = amount; lastBalance = balance; };

            Assert.That(counters.BankToWallet(), Is.EqualTo(500));
            Assert.That(SoulWallet.Balance, Is.EqualTo(500));
            Assert.That(counters.BankedSouls, Is.EqualTo(500));
            Assert.That(RunState.SoulsBanked, Is.True);
            Assert.That(events, Is.EqualTo(1));
            Assert.That(lastAmount, Is.EqualTo(500));
            Assert.That(lastBalance, Is.EqualTo(500));

            counters.AddSouls(80);
            Assert.That(counters.BankToWallet(), Is.EqualTo(0), "second run end of the same run banks nothing");
            Assert.That(SoulWallet.Balance, Is.EqualTo(500));
            Assert.That(events, Is.EqualTo(1));

            RunState.NewRun(TestRealm);
            Assert.That(RunState.SoulsBanked, Is.False);
            Assert.That(counters.BankToWallet(), Is.EqualTo(580));
            Assert.That(SoulWallet.Balance, Is.EqualTo(1080), "runs add up");
        }

        [Test]
        public void Death_BanksAtOnce()
        {
            var r = NewRun();
            r.Counters.Restore(30, 1240);
            r.Health.TakeDamage(r.Health.Max * 10f);
            Assert.That(r.Health.IsDead, Is.True);
            Assert.That(SoulWallet.Balance, Is.EqualTo(1240));
            Assert.That(r.Counters.BankToWallet(), Is.EqualTo(0), "the death screen's call banks nothing more");
            Assert.That(SoulWallet.Balance, Is.EqualTo(1240));
        }

        [Test]
        public void LastChamberPortal_Banks_OtherPortal_DoesNot()
        {
            PlanChamber(2);
            var r = NewRun();
            r.Counters.Restore(10, 300);
            Assert.That(r.Transition.Enter(new Vector3(0f, 0f, 6f)), Is.True);
            TickFor(r.Transition, 1.1f);
            Assert.That(RunState.HasCarry, Is.True, "test setup: Chamber 2 advances");
            Assert.That(SoulWallet.Balance, Is.EqualTo(0), "a portal to the next Chamber is not a run end");
            Assert.That(RunState.SoulsBanked, Is.False);

            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            RunState.TakeCarry();
            GamePause.Clear();

            PlanChamber(3);
            r = NewRun();
            r.Counters.Restore(140, 2600);
            Assert.That(r.Transition.Enter(new Vector3(0f, 0f, 6f)), Is.True);
            TickFor(r.Transition, 1.1f);
            Assert.That(r.Transition.Current, Is.EqualTo(ChamberTransition.Phase.Cleared));
            Assert.That(SoulWallet.Balance, Is.EqualTo(2600), "Realm cleared banks the run");
            Assert.That(RunState.SoulsBanked, Is.True);
        }

        [Test]
        public void Texts_CounterCache_WalletLine()
        {
            Assert.That(SoulCounterHud.TextFor(0), Is.EqualTo("0"));
            Assert.That(SoulCounterHud.TextFor(1240), Is.EqualTo("1,240"));
            Assert.That(SoulCounterHud.TextFor(1240), Is.SameAs(SoulCounterHud.TextFor(1240)), "cached: no new string per pickup");
            Assert.That(SoulCounterHud.TextFor(-3), Is.EqualTo("0"));
            Assert.That(SoulCounterHud.TextFor(1234567), Is.EqualTo("1,234,567"));
            Assert.That(PlayerHealthHud.WalletLine(1240, 12480), Is.EqualTo("+1,240 BANKED · 12,480 SOULS HELD"));
        }

#if UNITY_EDITOR
        [Test]
        public void Uxml_HasWalletElements()
        {
            var hud = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            Assert.That(hud, Is.Not.Null);
            var root = hud.Instantiate();
            Assert.That(root.Q(SoulCounterHud.CounterName), Is.Not.Null);
            Assert.That(root.Q(SoulCounterHud.ValueName), Is.Not.Null);
            Assert.That(root.Q("death-wallet"), Is.Not.Null);
            Assert.That(root.Q("victory-wallet"), Is.Not.Null);
            var menu = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.Instantiate().Q("lbl-wallet-souls"), Is.Not.Null);
        }
#endif
    }
}
