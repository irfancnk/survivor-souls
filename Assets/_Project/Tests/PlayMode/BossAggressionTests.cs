using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-164.2 boss aggression model: the defaults keep the old fight; with no summon alive the cooldown is scaled, while
    /// summons live it is not; combos chain only from phase 2, only alone, only fair pairs, and never out of a roar; the
    /// enrage starts once at its share and never before phase 2 (faster bolts, shorter cooldowns, faster walk, a roar);
    /// the enrage aura follows the boss and stops at the kill; the shipped bosses have fair combos and an enrage under
    /// phase 2; no GC allocation per frame.
    /// </summary>
    public sealed class BossAggressionTests
    {
        private const float Frame = 1f / 60f;
        private const string GhoulName = "Enemy_TestGhoul";
        private const float NovaCooldown = 3f;
        private const float NovaSpeed = 7f;

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private EnemySpawner _spawner;
        private GroundTelegraph _telegraph;
        private LineTelegraph _lines;
        private BossProjectiles _bolts;
        private Enemy _body;
        private Enemy _ghoul;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<int> _phases = new List<int>();
        private readonly List<(int opener, int partner)> _combos = new List<(int, int)>(256); // no growth in the GC test
        private int _enraged;

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(16402);

            _player = Track(new GameObject("Player")).AddComponent<PlayerHealth>();

            var ghoulGo = Track(new GameObject(GhoulName));
            ghoulGo.SetActive(false);
            _ghoul = ghoulGo.AddComponent<Enemy>();
            _spawner = Track(new GameObject("Enemies")).AddComponent<EnemySpawner>();
            _spawner.enabled = false; // no waves; summons still live under an active parent
            _spawner.SetExtraTypes(new[] { _ghoul });

            _telegraph = Track(new GameObject("Telegraph")).AddComponent<GroundTelegraph>();
            _telegraph.Setup(Quad(), Quad());
            _lines = Track(new GameObject("Lanes")).AddComponent<LineTelegraph>();
            var edges = new Renderer[LineTelegraph.MaxLanes];
            var fills = new Renderer[LineTelegraph.MaxLanes];
            for (int i = 0; i < LineTelegraph.MaxLanes; i++)
            {
                edges[i] = Quad();
                fills[i] = Quad();
            }
            _lines.Setup(edges, fills);
            _bolts = Track(new GameObject("Bolts")).AddComponent<BossProjectiles>();
            _bolts.Setup(_player, null, 128);

            var bodyGo = Track(new GameObject("BossBody"));
            bodyGo.SetActive(false);
            _body = bodyGo.AddComponent<Enemy>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_fight != null && _fight.Boss != null) Object.DestroyImmediate(_fight.Boss.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _phases.Clear();
            _combos.Clear();
            _enraged = 0;
            _fight = null;
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Renderer Quad()
        {
            var go = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.GetComponent<Renderer>();
        }

        private static BossAttack Nova(string label, BossPhases phases = BossPhases.Both) =>
            BossAttack.Nova(label, phases, 30f, bolts: 8, gaps: 0, warnRadius: 2f, telegraph: 0.8f, speed: NovaSpeed,
                distance: 14f, hitRadius: 0.4f, damage: 0.05f, recover: 0.4f, cooldown: NovaCooldown);

        private static BossAttack Summon(int count = 2) =>
            BossAttack.Summon("Summon", BossPhases.Both, 0f, 30f, "TestGhoul", count, ring: 2.6f, telegraph: 0.8f, recover: 0.4f,
                cooldown: 1f);

        private BossData Data(BossAttack[] attacks, float phase2At = 0.5f) =>
            Track(BossData.CreateWithAttacks(_body, attacks, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: 0.5f, phase2At: phase2At));

        /// <summary>Begin + rise; the boss stands at the origin, the Knight 6 m north (in range of everything).</summary>
        private void StartFight(BossData data)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), null, _spawner,
                _player, null, _telegraph, _lines, _bolts);
            _fight.PhaseChanged += p => _phases.Add(p);
            _fight.ComboStarted += (a, b) => _combos.Add((a, b));
            _fight.Enraged += () => _enraged++;
            Assert.That(_fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77)), Is.True);
            for (float t = 0f; t < data.IntroDelay + data.SpawnWarning + 2f * Frame; t += Frame) _fight.Tick(Frame);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            _fight.Boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _player.transform.position = new Vector3(0f, 0f, 6f);
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame) _fight.Tick(Frame);
        }

        /// <summary>Starts attack <paramref name="index"/> now and runs until the boss chases again (its cooldown is set).</summary>
        private void Finish(int index)
        {
            Assert.That(_fight.ForceAttack(index), Is.True, $"attack {index} starts");
            RunUntil(BossFight.Attack.Chase);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase));
        }

        private void HealthTo(float share) =>
            _fight.Boss.TakeDamage(_fight.Boss.Health - _fight.Boss.MaxHealth * share, Vector3.zero);

        private void KillSummons()
        {
            foreach (Transform child in _spawner.transform)
            {
                var e = child.GetComponent<Enemy>();
                if (e != null && e.IsAlive) e.TakeDamage(e.Health + 1f, Vector3.zero);
            }
        }

        // ------------------------------------------------------------ Defaults

        [Test]
        public void Defaults_KeepTheOldFight_NoScale_NoCombo_NoEnrage()
        {
            var data = Data(new[] { Nova("A"), Nova("B") });
            Assert.That(data.AggressionScale(true, false), Is.EqualTo(1f));
            Assert.That(data.ComboChanceFor(2, false), Is.EqualTo(0f));
            Assert.That(data.Phase3At, Is.EqualTo(0f));
            StartFight(data);
            Finish(0);
            Assert.That(_fight.CooldownLeft, Is.EqualTo(NovaCooldown).Within(1e-4f));
            HealthTo(0.1f);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            Assert.That(_fight.IsEnraged, Is.False);
            Assert.That(_enraged, Is.EqualTo(0));
            Finish(0);
            Assert.That(_fight.ComboPending, Is.EqualTo(-1));
            Assert.That(_fight.CooldownLeft, Is.EqualTo(data.CooldownFor(NovaCooldown, 2)).Within(1e-4f));
        }

        // ------------------------------------------------------------ Alone vs. summons

        [Test]
        public void Alone_ScalesTheCooldown()
        {
            StartFight(Data(new[] { Nova("A") }).WithAggression(aloneScale: 0.5f));
            Assert.That(_fight.Alone, Is.True);
            Finish(0);
            Assert.That(_fight.CooldownLeft, Is.EqualTo(NovaCooldown * 0.5f).Within(1e-4f));
        }

        [Test]
        public void SummonsAlive_KeepTheNormalCooldown_TheDuelGetsTheShortOne()
        {
            StartFight(Data(new[] { Summon(), Nova("A") }).WithAggression(aloneScale: 0.5f));
            Finish(0);
            Assert.That(_fight.SummonsAlive, Is.EqualTo(2));
            Assert.That(_fight.Alone, Is.False);
            Assert.That(_fight.CooldownLeft, Is.EqualTo(1f).Within(1e-4f), "summons alive: no aggression factor");
            Finish(1);
            Assert.That(_fight.CooldownLeft, Is.EqualTo(NovaCooldown).Within(1e-4f));
            KillSummons();
            Assert.That(_fight.Alone, Is.True);
            Finish(1);
            Assert.That(_fight.CooldownLeft, Is.EqualTo(NovaCooldown * 0.5f).Within(1e-4f));
        }

        // ------------------------------------------------------------ Combos

        [Test]
        public void Combo_ChainsOnlyFromPhase2_ThePartnerWarnsOnItsOwn()
        {
            var data = Data(new[] { Nova("A"), Nova("B") })
                .WithAggression(comboList: new[] { new BossCombo(0, 1) }, chance: 1f, gap: 0.25f);
            StartFight(data);
            Finish(0);
            Assert.That(_fight.ComboPending, Is.EqualTo(-1), "phase 1: no combo");
            Assert.That(_fight.CooldownLeft, Is.EqualTo(NovaCooldown).Within(1e-4f));

            HealthTo(0.45f);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            Finish(0);
            Assert.That(_fight.ComboPending, Is.EqualTo(1));
            Assert.That(_fight.CooldownLeft, Is.EqualTo(0.25f).Within(1e-4f), "the gap, not the cooldown");
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentAttackIndex, Is.EqualTo(1));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), "the partner still warns");
            Assert.That(_fight.CombosStarted, Is.EqualTo(1));
            Assert.That(_combos, Is.EqualTo(new List<(int, int)> { (0, 1) }));

            RunUntil(BossFight.Attack.Chase);
            Assert.That(_fight.ComboPending, Is.EqualTo(-1), "the partner opens nothing");
            Assert.That(_fight.CooldownLeft, Is.EqualTo(data.CooldownFor(NovaCooldown, 2)).Within(1e-4f));
        }

        [Test]
        public void Combo_NeverWhileSummonsLive()
        {
            StartFight(Data(new[] { Nova("A"), Nova("B"), Summon() })
                .WithAggression(comboList: new[] { new BossCombo(0, 1) }, chance: 1f));
            HealthTo(0.45f);
            Finish(2);
            Assert.That(_fight.SummonsAlive, Is.GreaterThan(0));
            Finish(0);
            Assert.That(_fight.ComboPending, Is.EqualTo(-1));
            KillSummons();
            Finish(0);
            Assert.That(_fight.ComboPending, Is.EqualTo(1), "alone again: the combo chains");
        }

        [Test]
        public void Combo_FairPairsOnly()
        {
            var spiral = BossAttack.Sample(BossFight.Move.Spiral);
            var rain = BossAttack.Sample(BossFight.Move.Rain);
            var list = new[] { Nova("A"), spiral, rain, Summon(), Nova("Phase 1 only", BossPhases.Phase1), BossAttack.BlackFireballs() };
            Assert.That(BossCombo.IsFair(list, new BossCombo(0, 1)), Is.True, "nova then spiral");
            Assert.That(BossCombo.IsFair(list, new BossCombo(1, 0)), Is.True, "spiral then nova");
            Assert.That(BossCombo.IsFair(list, new BossCombo(1, 2)), Is.False, "two area fillers");
            Assert.That(BossCombo.IsFair(list, new BossCombo(0, 3)), Is.False, "no summon");
            Assert.That(BossCombo.IsFair(list, new BossCombo(0, 4)), Is.False, "partner off in phase 2");
            Assert.That(BossCombo.IsFair(list, new BossCombo(0, 5)), Is.False, "no barrage");
            Assert.That(BossCombo.IsFair(list, new BossCombo(0, 0)), Is.False, "two different attacks");
            Assert.That(BossCombo.IsFair(list, new BossCombo(0, 9)), Is.False, "index out of the list");
        }

        [Test]
        public void Combo_UnfairPair_IsSkippedInTheFight()
        {
            StartFight(Data(new[] { Nova("A"), Nova("A again") })
                .WithAggression(comboList: new[] { new BossCombo(0, 0), new BossCombo(0, 7) }, chance: 1f));
            HealthTo(0.45f);
            Finish(0);
            Assert.That(_fight.ComboPending, Is.EqualTo(-1));
        }

        // ------------------------------------------------------------ Enrage

        [Test]
        public void Enrage_OnceAtTheShare_AfterPhase2_FasterBoltsWalkAndCooldowns()
        {
            var data = Data(new[] { Nova("A") })
                .WithAggression(enrageAt: 0.25f, enrageCooldown: 0.5f, enrageSpeed: 1.2f, walk: 1.1f);
            StartFight(data);
            HealthTo(0.4f);
            Assert.That(_fight.Phase, Is.EqualTo(2));
            Assert.That(_fight.IsEnraged, Is.False);
            HealthTo(0.2f);
            Assert.That(_fight.IsEnraged, Is.True);
            Assert.That(_enraged, Is.EqualTo(1));
            Assert.That(_fight.Phase, Is.EqualTo(2), "phase 2 attacks and damage hold");
            Assert.That(_fight.Boss.SpeedBoost, Is.EqualTo(data.Phase2Speed * 1.1f).Within(1e-4f));
            HealthTo(0.1f);
            Assert.That(_enraged, Is.EqualTo(1), "once");

            Assert.That(_fight.ForceAttack(0), Is.True);
            for (float t = 0f; t < _fight.WindUpTime + 2f * Frame; t += Frame) _fight.Tick(Frame);
            Assert.That(_bolts.Count, Is.GreaterThan(0));
            Assert.That(_bolts.VelocityOf(0).magnitude, Is.EqualTo(NovaSpeed * 1.2f).Within(1e-3f));
            RunUntil(BossFight.Attack.Chase);
            Assert.That(_fight.CooldownLeft, Is.EqualTo(data.CooldownFor(NovaCooldown, 2) * 0.5f).Within(1e-4f));
        }

        [Test]
        public void Enrage_NeverWithoutPhase2_OneBigHitGoesThroughPhase2()
        {
            StartFight(Data(new[] { Nova("A") }, phase2At: 0f).WithAggression(enrageAt: 0.25f));
            HealthTo(0.1f);
            Assert.That(_fight.IsEnraged, Is.False, "no phase 2 = no enrage");
            TearDown();
            SetUp();

            StartFight(Data(new[] { Nova("A") }).WithAggression(enrageAt: 0.25f));
            HealthTo(0.1f);
            Assert.That(_phases, Is.EqualTo(new List<int> { 2 }));
            Assert.That(_enraged, Is.EqualTo(1));
        }

        [Test]
        public void EnrageRoar_StandsTheBoss_AndOpensNoCombo()
        {
            StartFight(Data(new[] { Nova("A"), Nova("B") })
                .WithAggression(comboList: new[] { new BossCombo(0, 1) }, chance: 1f, enrageAt: 0.25f, roar: 1f));
            Finish(0); // the last attack is the opener
            HealthTo(0.45f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Chase), "no phase 2 roar in this data");
            HealthTo(0.2f);
            Assert.That(_fight.CurrentAttack, Is.EqualTo(BossFight.Attack.Recover), "the enrage roar");
            Assert.That(_fight.RecoverTime, Is.EqualTo(1f).Within(1e-4f));
            RunUntil(BossFight.Attack.Chase);
            Assert.That(_fight.ComboPending, Is.EqualTo(-1), "a roar is no combo opener");
        }

        [Test]
        public void EnrageFx_AuraOnTheBoss_StopsAtTheKill()
        {
            StartFight(Data(new[] { Nova("A") }).WithAggression(enrageAt: 0.25f));
            var auraGo = Track(new GameObject("Aura"));
            var aura = auraGo.AddComponent<ParticleSystem>();
            var main = aura.main;
            main.playOnAwake = false;
            aura.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var fx = Track(new GameObject("Enrage FX")).AddComponent<BossEnrageFx>();
            fx.Setup(_fight, aura);
            _fight.Boss.transform.position = new Vector3(3f, 0f, -2f);
            HealthTo(0.4f);
            Assert.That(fx.AuraOn, Is.False, "phase 2 is no enrage");
            HealthTo(0.2f);
            Assert.That(fx.AuraOn, Is.True);
            Assert.That(aura.isEmitting, Is.True);
            Assert.That(Vector3.Distance(aura.transform.position, _fight.Boss.transform.position), Is.LessThan(1e-4f));
            _fight.Boss.TakeDamage(_fight.Boss.Health + 1f, Vector3.zero);
            Assert.That(fx.AuraOn, Is.False);
            Assert.That(aura.isEmitting, Is.False);
        }

        // ------------------------------------------------------------ Shipped data + GC

        [Test]
        public void ShippedBosses_FairCombos_EnrageUnderPhase2_DuelFaster()
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:BossData", new[] { "Assets/_Project/Data/Boss" });
            Assert.That(guids.Length, Is.GreaterThanOrEqualTo(5));
            foreach (var guid in guids)
            {
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                string name = data.name;
                Assert.That(data.Phase3At, Is.GreaterThan(0f).And.LessThan(data.Phase2At), $"{name}: enrage after phase 2");
                Assert.That(data.AloneCooldownScale, Is.LessThan(1f), $"{name}: the duel pushes harder");
                Assert.That(data.Combos.Length, Is.GreaterThan(0), $"{name}: has combos");
                Assert.That(data.ComboChance, Is.GreaterThan(0f).And.LessThan(1f), name);
                Assert.That(data.EnrageComboChance, Is.GreaterThanOrEqualTo(data.ComboChance), name);
                foreach (var c in data.Combos)
                    Assert.That(BossCombo.IsFair(data.Attacks, c), Is.True, $"{name}: combo {c.first} -> {c.second} is fair");
                // the hardest the duel gets: phase 2 x alone x enrage keeps a cooldown floor
                Assert.That(data.Phase2Cooldown * data.AggressionScale(true, true), Is.GreaterThanOrEqualTo(0.35f), name);
            }
        }

        [Test]
        public void EnragedComboFight_AllocatesNothingPerFrame()
        {
            StartFight(Data(new[] { Nova("A"), Nova("B") })
                .WithAggression(aloneScale: 0.5f, comboList: new[] { new BossCombo(0, 1), new BossCombo(1, 0) }, chance: 1f,
                    enrageAt: 0.25f));
            _player.Invulnerable = true; // the bolts fly and hit; the Knight stays alive, so the boss keeps attacking
            HealthTo(0.2f);
            Assert.That(_fight.IsEnraged, Is.True);
            for (int i = 0; i < 240; i++) // warm-up: both attacks, a combo
            {
                _fight.Tick(Frame);
                _bolts.Tick(Frame);
            }
            Assert.That(_fight.CombosStarted, Is.GreaterThan(0));
            Assert.That(() =>
            {
                for (int i = 0; i < 240; i++)
                {
                    _fight.Tick(Frame);
                    _bolts.Tick(Frame);
                }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
