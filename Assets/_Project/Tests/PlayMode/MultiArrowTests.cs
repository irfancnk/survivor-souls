using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-83 multi-arrow rework: <see cref="VolleyBuilder"/> (Double Shot chain 2 / 3 / 4 bolts with delay and side step,
    /// Spread Shot 3 / 5 / 7 bolts at 15 deg steps with the main bolt straight, both together up to the cap, no GC), the
    /// shooter (follow-ups launch after the delay, one Fired + one FollowUpFired per step, damage shares, crit / pierce on
    /// every bolt, active bolt budget, one reload per volley, pending bolts keep their own volley's damage), repeat-offer odds
    /// (no forced repeat by guarantee or pity, chest weight) and the shipped data (Spread Shot = old Multishot asset and id,
    /// Double Shot Epic max 3, level-3 guarantee, no third multi-arrow skill, stack odds of 1000 simulated runs).
    /// </summary>
    public sealed class MultiArrowTests
    {
        private const float Frame = 1f / 60f;
        private const string Dir = "Assets/_Project/Data/Skills/";

        private readonly List<Object> _objects = new List<Object>();
        private readonly List<BoltHit> _hits = new List<BoltHit>();
        private readonly VolleyArrow[] _buffer = new VolleyArrow[24];
        private GameObject _player;
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private SkillManager _manager;
        private int _fired, _followUps;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(83);
            _fired = _followUps = 0; // NUnit reuses the fixture instance
            _player = Track(new GameObject("Player"));
            _player.SetActive(false);
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            _player.SetActive(true);
            _shooter.enabled = false; // tests fire and tick by hand
            _shooter.EnemyHit += h => _hits.Add(h);
            _shooter.Fired += (_, __) => _fired++;
            _shooter.FollowUpFired += (_, __) => _followUps++;

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());

            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, _shooter, null, _player.transform, rngSeed: 83);
            _manager.SpawnEffects = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _hits.Clear();
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static SkillDefinition Load(string file)
        {
#if UNITY_EDITOR
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>($"{Dir}Skill_{file}.asset");
            Assert.That(skill, Is.Not.Null, file);
            return skill;
#else
            Assert.Ignore("needs the editor asset database");
            return null;
#endif
        }

        private static SkillProgression LoadTable()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(Dir + "SkillProgression_Run.asset");
#else
            return null;
#endif
        }

        private Enemy SpawnEnemy(Vector3 position, float healthMultiplier = 1f)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { }, healthMultiplier);
            return e;
        }

        /// <summary>Raw stat modifiers on the Knight (the player object owns the source).</summary>
        private void Give(params StatModifier[] mods) => _stats.SetSource(_player, mods, 1);

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        /// <summary>Steps the shooter (pending bolts) and every bolt in flight.</summary>
        private void Run(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _shooter.Tick(Frame);
                foreach (var b in LiveBolts()) b.Tick(Frame);
            }
        }

        private static Bolt[] LiveBolts() =>
            Resources.FindObjectsOfTypeAll<Bolt>().Where(b => b.gameObject.scene.IsValid() && b.InFlight).ToArray();

        /// <summary>Past the shooter's 10 m range: Tick never auto-fires at it, only Fire does (bolts reach 15 m).</summary>
        private const float Far = 12f;

        private static float Yaw(Bolt b) => Mathf.DeltaAngle(0f, b.transform.eulerAngles.y);

        private static VolleySpec Spec(int spread, int chain, float spreadDamage = 0.5f, float chainDamage = 1f, int cap = 12) =>
            new VolleySpec
            {
                Spread = spread, SpreadStep = 15f, SpreadDamage = spreadDamage,
                Chain = chain, ChainDelay = 0.09f, ChainLateral = 0.18f, ChainDamage = chainDamage, Cap = cap,
            };

        // ------------------------------------------------------------ VolleyBuilder

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 4)]
        public void DoubleShot_ChainFiresOneBoltPerStep_SameLine_WithDelayAndSideStep(int chain, int bolts)
        {
            int n = VolleyBuilder.Build(Spec(0, chain), _buffer);
            Assert.That(n, Is.EqualTo(bolts));
            for (int i = 0; i < n; i++)
            {
                Assert.That(_buffer[i].Yaw, Is.Zero, "same direction");
                Assert.That(_buffer[i].Delay, Is.EqualTo(i * 0.09f).Within(1e-5f));
                Assert.That(_buffer[i].Chain, Is.EqualTo(i));
                Assert.That(_buffer[i].Lead, Is.True);
                Assert.That(_buffer[i].DamageScale, Is.EqualTo(1f));
            }
            var sides = Enumerable.Range(0, n).Select(i => _buffer[i].Lateral).ToArray();
            Assert.That(sides[0], Is.Zero, "first bolt leaves the muzzle");
            Assert.That(sides.Distinct().Count(), Is.EqualTo(n), "no two bolts overlap");
            Assert.That(sides.All(s => Mathf.Abs(s) <= 0.37f), Is.True, "tiny side step");
        }

        [TestCase(2, new[] { 0f, 15f, -15f })]
        [TestCase(4, new[] { 0f, 15f, -15f, 30f, -30f })]
        [TestCase(6, new[] { 0f, 15f, -15f, 30f, -30f, 45f, -45f })]
        public void SpreadShot_MainBoltStraight_SideBoltsFanOutBothWays(int spread, float[] yaws)
        {
            int n = VolleyBuilder.Build(Spec(spread, 0, spreadDamage: 0.65f), _buffer);
            Assert.That(n, Is.EqualTo(yaws.Length));
            for (int i = 0; i < n; i++)
            {
                Assert.That(_buffer[i].Yaw, Is.EqualTo(yaws[i]).Within(1e-4f), $"bolt {i}");
                Assert.That(_buffer[i].Delay, Is.Zero, "all leave together");
                Assert.That(_buffer[i].DamageScale, Is.EqualTo(i == 0 ? 1f : 0.65f).Within(1e-5f), "side bolts at their share");
                Assert.That(_buffer[i].Lead, Is.EqualTo(i == 0));
            }
        }

        [Test]
        public void Both_EveryFanBoltRepeats_UpToTheCap_StraightBoltsSurviveLongest()
        {
            int n = VolleyBuilder.Build(Spec(2, 1, chainDamage: 0.9f), _buffer);
            Assert.That(n, Is.EqualTo(6), "3 directions x 2");
            Assert.That(_buffer.Take(6).Count(a => a.Delay > 0f), Is.EqualTo(3));
            Assert.That(_buffer[3].DamageScale, Is.EqualTo(0.9f).Within(1e-5f), "follow-up main bolt");
            Assert.That(_buffer[4].DamageScale, Is.EqualTo(0.45f).Within(1e-5f), "follow-up side bolt: both shares");

            n = VolleyBuilder.Build(Spec(6, 3), _buffer);
            Assert.That(n, Is.EqualTo(12), "7 x 4 = 28 is capped at 12");
            Assert.That(VolleyBuilder.Count(Spec(6, 3)), Is.EqualTo(12));
            var last = _buffer.Take(n).Where(a => a.Chain == 1).ToArray();
            Assert.That(last.Length, Is.EqualTo(5));
            Assert.That(last[0].Lead, Is.True, "the straight bolt of a cut step is kept");
            Assert.That(_buffer.Take(n).Max(a => a.Chain), Is.EqualTo(1));
        }

        [Test]
        public void Build_DoesNotAllocate()
        {
            var spec = Spec(6, 3);
            VolleyBuilder.Build(spec, _buffer);
            Assert.That(() => { VolleyBuilder.Build(spec, _buffer); }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Shooter

        [Test]
        public void DoubleShot_Level1_SecondBoltLeavesAfterTheDelay_SameDirection_OneRecoilEach()
        {
            _manager.Grant(Load("DoubleShot"));
            var enemy = SpawnEnemy(new Vector3(0f, 0f, Far));
            _shooter.Fire(enemy);
            Assert.That(LiveBolts().Length, Is.EqualTo(1), "first bolt at once");
            Assert.That(_shooter.PendingBolts, Is.EqualTo(1));
            Assert.That(_fired, Is.EqualTo(1));

            _shooter.Tick(0.05f);
            Assert.That(LiveBolts().Length, Is.EqualTo(1), "still waiting");
            _shooter.Tick(0.05f);
            var bolts = LiveBolts();
            Assert.That(bolts.Length, Is.EqualTo(2), "second bolt after ~0.09 s");
            Assert.That(_shooter.PendingBolts, Is.Zero);
            Assert.That(_followUps, Is.EqualTo(1), "one lighter kick + quiet shot layer");
            Assert.That(_fired, Is.EqualTo(1), "one volley = one Fired (reload ring, main shot sound)");
            Assert.That(Yaw(bolts[0]), Is.EqualTo(Yaw(bolts[1])).Within(0.5f), "same direction");
        }

        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 4)]
        [TestCase(4, 4)] // KAN-221: levels 4-5 add damage, not bolts
        [TestCase(5, 4)]
        public void DoubleShot_ShippedLevels_FireTwoThreeFourBolts(int level, int bolts)
        {
            _manager.SetLevel(Load("DoubleShot"), level);
            _shooter.Fire(SpawnEnemy(new Vector3(0f, 0f, Far)));
            for (int i = 0; i < 30; i++) _shooter.Tick(Frame);
            Assert.That(LiveBolts().Length, Is.EqualTo(bolts));
            Assert.That(_followUps, Is.EqualTo(bolts - 1));
        }

        [TestCase(1, 3)]
        [TestCase(2, 5)]
        [TestCase(3, 7)]
        [TestCase(4, 7)] // KAN-221: levels 4-5 add damage, not bolts
        [TestCase(5, 7)]
        public void SpreadShot_ShippedLevels_MainBoltForward_SideBoltsBothWays(int level, int bolts)
        {
            _manager.SetLevel(Load("Multishot"), level);
            _shooter.Fire(SpawnEnemy(new Vector3(0f, 0f, Far)));
            var yaws = LiveBolts().Select(Yaw).OrderBy(y => y).ToArray();
            Assert.That(yaws.Length, Is.EqualTo(bolts), "all at once, every shot");
            Assert.That(yaws.Count(y => Mathf.Abs(y) < 0.5f), Is.EqualTo(1), "one main bolt straight at the target");
            Assert.That(yaws.Max(), Is.EqualTo(15f * (bolts - 1) / 2f).Within(0.5f));
            Assert.That(yaws.Min(), Is.EqualTo(-yaws.Max()).Within(0.5f), "symmetric");
            Assert.That(_shooter.PendingBolts, Is.Zero);
        }

        [Test]
        public void SideBoltsAndFollowUps_DealTheirDamageShare()
        {
            Give(Add(StatId.Multishot, 2f), Add(StatId.DoubleShot, 1f), new StatModifier(StatId.ChainDamage, ModifierKind.Percent, -0.1f));
            var target = SpawnEnemy(new Vector3(0f, 0f, Far), 20f);
            var right = SpawnEnemy(Quaternion.AngleAxis(15f, Vector3.up) * new Vector3(0f, 0f, Far), 20f);
            _shooter.Fire(target);
            Run(90);
            var onTarget = _hits.Where(h => h.Enemy == target).Select(h => h.Damage).OrderBy(d => d).ToArray();
            var onRight = _hits.Where(h => h.Enemy == right).Select(h => h.Damage).OrderBy(d => d).ToArray();
            Assert.That(onTarget.Length, Is.EqualTo(2), "main bolt + follow-up");
            Assert.That(onTarget[0], Is.EqualTo(90f).Within(0.01f), "follow-up at 90 %");
            Assert.That(onTarget[1], Is.EqualTo(100f).Within(0.01f), "main bolt full damage");
            Assert.That(onRight.Length, Is.EqualTo(2), "the 15 deg side bolt and its follow-up");
            Assert.That(onRight[0], Is.EqualTo(45f).Within(0.01f), "side follow-up: 50 % x 90 %");
            Assert.That(onRight[1], Is.EqualTo(50f).Within(0.01f), "side bolt at the 50 % base share");
        }

        [Test]
        public void EveryBolt_CarriesCritAndPierce()
        {
            Give(Add(StatId.DoubleShot, 1f), Add(StatId.CritChance, 1f), Add(StatId.Pierce, 1f));
            var front = SpawnEnemy(new Vector3(0f, 0f, 10.6f), 20f);
            var back = SpawnEnemy(new Vector3(0f, 0f, 13f), 20f);
            _shooter.Fire(front);
            Run(90);
            Assert.That(_hits.Count(h => h.Enemy == front), Is.EqualTo(2), "both bolts of the chain hit");
            Assert.That(_hits.Count(h => h.Enemy == back), Is.EqualTo(2), "both pierce into the next body");
            Assert.That(_hits.All(h => h.Crit), Is.True, "crit on every bolt");
        }

        [Test]
        public void ActiveBoltBudget_SkipsBoltsOverIt_PoolKeepsCount()
        {
            _shooter.SetBoltLimits(12, 5);
            Give(Add(StatId.Multishot, 6f));
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 30f));
            _shooter.Fire(enemy);
            Assert.That(LiveBolts().Length, Is.EqualTo(5));
            Assert.That(_shooter.ActiveBolts, Is.EqualTo(5));
            for (int i = 0; i < 600 && LiveBolts().Length > 0; i++)
                foreach (var b in LiveBolts()) b.Tick(Frame);
            Assert.That(_shooter.ActiveBolts, Is.Zero, "released bolts free the budget");
            _shooter.Fire(enemy);
            Assert.That(LiveBolts().Length, Is.EqualTo(5));
        }

        [Test]
        public void PendingFollowUps_CancelOnDisable()
        {
            Give(Add(StatId.DoubleShot, 3f));
            _shooter.Fire(SpawnEnemy(new Vector3(0f, 0f, Far)));
            Assert.That(_shooter.PendingBolts, Is.EqualTo(3));
            _shooter.enabled = true;
            _shooter.enabled = false; // OnDisable: run reset / death drops the queue
            Assert.That(_shooter.PendingBolts, Is.Zero);
            Run(30);
            Assert.That(_followUps, Is.Zero, "no follow-up left the crossbow");
        }

        [Test]
        public void Volley_IsOneReload_AttackSpeedAppliesToTheVolley()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 6f), 1000f);
            int ticks = Mathf.CeilToInt(_shooter.CurrentInterval * 3f / Frame) + 2;
            for (int i = 0; i < ticks; i++) _shooter.Tick(Frame);
            int plain = _fired;
            Assert.That(plain, Is.InRange(2, 4));

            _fired = 0;
            _followUps = 0;
            Give(Add(StatId.DoubleShot, 3f), Add(StatId.Multishot, 2f));
            for (int i = 0; i < ticks; i++) _shooter.Tick(Frame);
            // Phase 1 starts ready (shot at frame 1), phase 2 mid-reload: one volley less at most.
            Assert.That(_fired, Is.InRange(plain - 1, plain), "same volley rate with both skills: the extra bolts are free");
            Assert.That(_followUps, Is.InRange((_fired - 1) * 3, _fired * 3), "3 follow-up steps a volley");
            Assert.That(enemy.Health, Is.GreaterThan(0f));
        }

        [Test]
        public void TickWithPendingBolts_DoesNotAllocate_AfterWarmUp()
        {
            Give(Add(StatId.DoubleShot, 3f), Add(StatId.Multishot, 2f));
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 30f));
            _shooter.SetBoltLimits(12, 1000);
            for (int i = 0; i < 3; i++) // warm the pool: every bolt comes back
            {
                _shooter.Fire(enemy);
                for (int t = 0; t < 30; t++) _shooter.Tick(Frame);
                for (int t = 0; t < 600 && LiveBolts().Length > 0; t++)
                    foreach (var b in LiveBolts()) b.Tick(Frame);
            }
            Assert.That(() =>
            {
                _shooter.Fire(enemy);
                for (int t = 0; t < 20; t++) _shooter.Tick(Frame);
            }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Offer odds

        [Test]
        public void RepeatWeights_DampOwnedDoubleShot_GuaranteeAndPityNeverForceIt()
        {
            var ds = Load("DoubleShot");
            Assert.That(ds.RepeatOfferWeight(0), Is.EqualTo(1f));
            Assert.That(ds.RepeatOfferWeight(1), Is.EqualTo(0.25f).Within(1e-5f), "second stack");
            Assert.That(ds.RepeatOfferWeight(2), Is.EqualTo(0.06f).Within(1e-5f), "third stack");
            Assert.That(ds.UpgradeOfferWeight, Is.EqualTo(0.35f).Within(1e-5f), "chest card");

            var only = Track(ScriptableObject.CreateInstance<SkillProgression>());
            only.Configure(new[]
            {
                new SkillProgression.Tier { fromLevel = 2, common = 0f, rare = 0f, epic = 0f, legendary = 100f, unlocks = new[] { ds } }, // KAN-246: Multishot is Legendary
            }, new[] { new SkillProgression.Guarantee { level = 3, skill = ds } }, pity: 1);
            _manager.Setup(only, _stats, _shooter, null, _player.transform, rngSeed: 5);
            var offer = new List<SkillDefinition>();
            Assert.That(_manager.Roll(3, 3, offer), Is.EqualTo(1));
            Assert.That(offer[0], Is.SameAs(ds), "first offer: the guarantee");
            _manager.Grant(ds);
            int shown = 0;
            for (int i = 0; i < 400; i++)
            {
                _manager.Roll(3, 3, offer);
                if (offer.Contains(ds)) shown++;
            }
            Assert.That(shown, Is.EqualTo(400), "alone in the pool it still shows (weight > 0, never 0)");

            var other = Track(ScriptableObject.CreateInstance<SkillDefinition>())
                .Configure("other", "Other", "other", SkillRarity.Legendary, 5, new StatModifier(StatId.Damage, ModifierKind.Percent, 0.1f));
            only.Configure(new[]
            {
                new SkillProgression.Tier { fromLevel = 2, common = 0f, rare = 0f, epic = 0f, legendary = 100f, unlocks = new[] { ds, other } },
            }, new[] { new SkillProgression.Guarantee { level = 3, skill = ds } }, pity: 1);
            shown = 0;
            for (int i = 0; i < 400; i++)
            {
                _manager.Roll(1, 3, offer);
                if (offer[0] == ds) shown++;
            }
            Assert.That(shown, Is.InRange(40, 120), "owned once: ~0.25 / 1.25 = 20 % of single-card offers, guarantee skipped");
        }

        [Test]
        public void ShippedTable_DoubleShotStacksStayRare_1000Runs()
        {
            var table = LoadTable();
            Assert.That(table, Is.Not.Null);
            var odds = SkillStackOdds.Simulate(table, "double_shot", 1000, 20);
            Debug.Log("[MultiArrow] " + odds);
            Assert.That(odds.Share(1), Is.GreaterThan(0.25f), "one stack is the common case");
            Assert.That(odds.Share(2), Is.LessThan(0.15f), "two stacks: uncommon");
            Assert.That(odds.Share(3), Is.LessThan(0.03f), "three stacks: rare");
            Assert.That(odds.GreedyShare(1), Is.EqualTo(1f), "level 3 guarantee offers it in every run");
        }

        // ------------------------------------------------------------ Shipped data

        [Test]
        public void ShippedData_SpreadShotReplacesMultishot_DoubleShotEpic_NoThirdMultiArrowSkill()
        {
            var spread = Load("Multishot");
            Assert.That(spread.Id, Is.EqualTo("multishot"), "same id: saves and debug configs load");
            Assert.That(spread.DisplayName, Is.EqualTo("Spread Shot"));
            Assert.That(spread.Rarity, Is.EqualTo(SkillRarity.Epic), "KAN-246 Ember Boons: Epic");
            Assert.That(spread.MaxLevel, Is.EqualTo(5), "KAN-221: 5 levels, levels 4-5 add damage");

            var ds = Load("DoubleShot");
            Assert.That(ds.Id, Is.EqualTo("double_shot"));
            Assert.That(ds.Rarity, Is.EqualTo(SkillRarity.Legendary), "KAN-246 Ember Boons: Multishot, Legendary");
            Assert.That(ds.DisplayName, Is.EqualTo("Multishot"), "KAN-246: Double Shot renamed, same id");
            Assert.That(ds.MaxLevel, Is.EqualTo(5), "KAN-221: 5 levels, levels 4-5 add damage");
            Assert.That(ds.Icon, Is.Not.Null);

#if UNITY_EDITOR
            var all = UnityEditor.AssetDatabase.FindAssets("t:SkillDefinition", new[] { Dir.TrimEnd('/') })
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)));
            var multiArrow = all.Where(s => s.Levels.Any(l => l.modifiers.Any(m => m.stat == StatId.Multishot || m.stat == StatId.DoubleShot)))
                .Select(s => s.Id).OrderBy(id => id).ToArray();
            Assert.That(multiArrow, Is.EqualTo(new[] { "double_shot", "multishot" }), "exactly two multi-arrow skills");
#endif
            var table = LoadTable();
            Assert.That(table.GuaranteedAt(3), Is.SameAs(ds), "KAN-36: offered early");
            Assert.That(table.GuaranteedAt(6), Is.SameAs(spread));
        }
    }
}
