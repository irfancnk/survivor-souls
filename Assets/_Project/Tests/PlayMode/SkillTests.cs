using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-19 acceptance (logic side; card looks and feel need the device): stat math, grant + stacking up to max, rolls
    /// (3 distinct valid skills, maxed left out, unlock level, rarity odds, guarantee, short pool, no GC), every stat
    /// reader (mover, health, pickup, shooter), bolt behaviours (multishot fan, pierce, ricochet, crit), behaviour skills
    /// (burn, life steal, kill burst + chain), run reset, the level-up flow with the card screen, and the shipped assets.
    /// </summary>
    public sealed class SkillTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private PlayerMover _mover;
        private AutoShooter _shooter;
        private PlayerHealth _health;
        private SoulCollector _collector;
        private SkillManager _manager;
        private SkillProgression _table;
        private readonly List<BoltHit> _hits = new List<BoltHit>();

        [SetUp]
        public void SetUp()
        {
            Random.InitState(1907);
            _player = Track(new GameObject("Player"));
            _player.SetActive(false); // Awake after every component exists (readers grab PlayerStats in Awake)
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _mover = _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            _health = _player.AddComponent<PlayerHealth>();
            _collector = _player.AddComponent<SoulCollector>();
            _player.SetActive(true);
            _shooter.enabled = false; // tests fire by hand
            _shooter.EnemyHit += h => _hits.Add(h);

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());

            _table = Track(ScriptableObject.CreateInstance<SkillProgression>());
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(_table, _stats, _shooter, _health, _player.transform, rngSeed: 19);
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
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private SkillDefinition Skill(string id, SkillRarity rarity = SkillRarity.Common, int max = 3, params StatModifier[] mods) =>
            Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure(id, id, id, rarity, max, mods);

        private SkillDefinition Behaviour<T>(string id, params StatModifier[] mods) where T : SkillEffect
        {
            var s = Skill(id, SkillRarity.Rare, 3, mods);
            s.SetPresentation(null, Track(new GameObject(id + " FX")).AddComponent<T>());
            return s;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);
        private static StatModifier Pct(StatId s, float v) => new StatModifier(s, ModifierKind.Percent, v);

        private void Tier(int from, float common, float rare, float epic, params SkillDefinition[] unlocks) =>
            _table.Configure(_table.Tiers.Concat(new[]
            {
                new SkillProgression.Tier { fromLevel = from, common = common, rare = rare, epic = epic, unlocks = unlocks },
            }).ToArray());

        private Enemy SpawnEnemy(Vector3 position)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { });
            return e;
        }

        private static IEnumerable<Bolt> LiveBolts() =>
            Resources.FindObjectsOfTypeAll<Bolt>().Where(b => b.gameObject.scene.IsValid() && b.InFlight);

        private static void Fly(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                foreach (var b in LiveBolts().ToArray()) b.Tick(Frame);
        }

        // ------------------------------------------------------------ Stats

        [Test]
        public void Stats_FlatAndPercentFromSeveralSources_Combine()
        {
            var a = Skill("a");
            var b = Skill("b");
            int changes = 0;
            _stats.Changed += () => changes++;
            _stats.SetSource(a, new[] { Add(StatId.Damage, 10f), Pct(StatId.Damage, 0.2f) }, 1);
            _stats.SetSource(b, new[] { Pct(StatId.Damage, 0.1f) }, 2);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(110f * 1.4f).Within(1e-3f));
            Assert.That(_stats.Modify(StatId.MoveSpeed, 5f), Is.EqualTo(5f));
            _stats.RemoveSource(a);
            Assert.That(_stats.Modify(StatId.Damage, 100f), Is.EqualTo(120f).Within(1e-3f));
            Assert.That(changes, Is.EqualTo(3));
            Assert.That(() => { _stats.Modify(StatId.Damage, 100f); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Stats_PercentBelowMinus100_NeverGoesNegative()
        {
            _stats.SetSource(Skill("curse"), new[] { Pct(StatId.MoveSpeed, -0.6f) }, 2);
            Assert.That(_stats.Modify(StatId.MoveSpeed, 5f), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------ Grant + stacking

        [Test]
        public void Grant_EachStackAddsTheEffect_UpToMaxStack()
        {
            var sharp = Skill("sharp", SkillRarity.Common, 3, Pct(StatId.Damage, 0.2f));
            int granted = 0;
            _manager.Granted += (_, __) => granted++;
            for (int i = 1; i <= 3; i++)
            {
                Assert.That(_manager.Grant(sharp), Is.True);
                Assert.That(_manager.GetLevel(sharp), Is.EqualTo(i));
                Assert.That(_shooter.CurrentDamage, Is.EqualTo(100f * (1f + 0.2f * i)).Within(1e-3f));
            }
            Assert.That(_manager.Grant(sharp), Is.False, "4th stack over max 3");
            Assert.That(_manager.CanTake(sharp), Is.False);
            Assert.That(_shooter.CurrentDamage, Is.EqualTo(160f).Within(1e-3f));
            Assert.That(granted, Is.EqualTo(3));
            Assert.That(_manager.OwnedSkills.Count, Is.EqualTo(1));
        }

        // ------------------------------------------------------------ Rolls

        [Test]
        public void Roll_OffersThreeDistinctTakeableSkills_FromThePool()
        {
            var pool = Enumerable.Range(0, 6).Select(i => Skill("s" + i)).ToArray();
            Tier(2, 1f, 1f, 1f, pool);
            var offer = new List<SkillDefinition>();
            for (int n = 0; n < 200; n++)
            {
                Assert.That(_manager.Roll(3, 2, offer), Is.EqualTo(3));
                Assert.That(offer.Distinct().Count(), Is.EqualTo(3));
                Assert.That(offer.All(s => pool.Contains(s) && _manager.CanTake(s)), Is.True);
            }
        }

        [Test]
        public void Roll_LeavesOutMaxedSkills_AndReturnsFewerWhenThePoolRunsDry()
        {
            var a = Skill("a", max: 1);
            var b = Skill("b", max: 1);
            var c = Skill("c", max: 2);
            Tier(2, 1f, 1f, 1f, a, b, c);
            _manager.Grant(a);
            var offer = new List<SkillDefinition>();
            for (int n = 0; n < 50; n++)
            {
                _manager.Roll(3, 2, offer);
                Assert.That(offer, Has.No.Member(a));
                Assert.That(offer.Count, Is.EqualTo(2));
            }
            _manager.Grant(b);
            _manager.Grant(c);
            _manager.Grant(c);
            Assert.That(_manager.Roll(3, 2, offer), Is.Zero, "everything maxed: nothing to offer");
        }

        [Test]
        public void Roll_LaterTiersUnlockOnlyFromTheirLevel()
        {
            var early = Enumerable.Range(0, 3).Select(i => Skill("early" + i)).ToArray();
            var late = Skill("late", SkillRarity.Epic);
            Tier(2, 1f, 1f, 1f, early);
            Tier(5, 1f, 1f, 1000f, late);
            var offer = new List<SkillDefinition>();
            for (int n = 0; n < 100; n++)
            {
                _manager.Roll(3, 4, offer);
                Assert.That(offer, Has.No.Member(late), "level 4 is below the tier");
            }
            _manager.Roll(1, 5, offer);
            Assert.That(offer[0], Is.EqualTo(late), "tier 5 odds favour the epic heavily");
        }

        [Test]
        public void Roll_FollowsRarityWeights_ZeroWeightOnlyWhenNothingElseIsLeft()
        {
            var commons = Enumerable.Range(0, 4).Select(i => Skill("c" + i)).ToArray();
            var rare = Skill("rare", SkillRarity.Rare);
            var epic = Skill("epic", SkillRarity.Epic);
            Tier(2, 10f, 90f, 0f, commons.Append(rare).Append(epic).ToArray());
            var offer = new List<SkillDefinition>();
            int rareFirst = 0;
            for (int n = 0; n < 400; n++)
            {
                _manager.Roll(3, 2, offer);
                Assert.That(offer, Has.No.Member(epic), "epic weight 0 while commons remain");
                if (offer[0] == rare) rareFirst++;
            }
            // First pick: rare 90 vs 4 x 10 commons -> 69 %.
            Assert.That(rareFirst / 400f, Is.InRange(0.6f, 0.78f));

            foreach (var c in commons) _manager.Grant(c);
            foreach (var c in commons) _manager.Grant(c);
            foreach (var c in commons) _manager.Grant(c); // all commons maxed (3)
            _manager.Roll(3, 2, offer);
            Assert.That(offer, Is.EquivalentTo(new[] { rare, epic }), "zero-weight epic fills the last slot");
        }

        [Test]
        public void Roll_GuaranteedSkillAppearsAtItsLevel()
        {
            var pool = Enumerable.Range(0, 8).Select(i => Skill("s" + i)).ToArray();
            var star = Skill("star", SkillRarity.Epic, 1);
            _table.Configure(new[] { new SkillProgression.Tier { fromLevel = 2, common = 1f, rare = 1f, epic = 0f, unlocks = pool } },
                new[] { new SkillProgression.Guarantee { level = 4, skill = star } });
            var offer = new List<SkillDefinition>();
            _manager.Roll(3, 4, offer);
            Assert.That(offer[0], Is.EqualTo(star));
            Assert.That(offer.Distinct().Count(), Is.EqualTo(3));
            _manager.Roll(3, 3, offer);
            Assert.That(offer, Has.No.Member(star));
            _manager.Grant(star);
            _manager.Roll(3, 4, offer);
            Assert.That(offer, Has.No.Member(star), "a maxed guarantee is skipped");
        }

        [Test]
        public void Roll_DoesNotAllocate()
        {
            Tier(2, 70f, 25f, 5f, Enumerable.Range(0, 12).Select(i => Skill("s" + i, (SkillRarity)(i % 3))).ToArray());
            var offer = new List<SkillDefinition>(3);
            _manager.Roll(3, 2, offer); // warm-up: internal lists reach their size
            Assert.That(() => { _manager.Roll(3, 2, offer); }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Stat readers

        [Test]
        public void StatReaders_MoveSpeedPickupAttackSpeed_FollowSkills()
        {
            float speed = _mover.MaxSpeed, radius = _collector.PickupRadius, interval = _shooter.CurrentInterval;
            _manager.Grant(Skill("fleet", mods: Pct(StatId.MoveSpeed, 0.1f)));
            _manager.Grant(Skill("siphon", mods: Pct(StatId.PickupRadius, 0.4f)));
            _manager.Grant(Skill("quick", mods: Pct(StatId.AttackSpeed, 0.25f)));
            Assert.That(_mover.MaxSpeed, Is.EqualTo(speed * 1.1f).Within(1e-3f));
            Assert.That(_collector.PickupRadius, Is.EqualTo(radius * 1.4f).Within(1e-3f));
            Assert.That(_shooter.CurrentInterval, Is.EqualTo(interval / 1.25f).Within(1e-3f));
        }

        [Test]
        public void MaxHealthSkill_RaisesMaxAndHealsTheGain_ResetClamps()
        {
            _health.TakeDamage(100f);
            float max = _health.Max;
            var vigor = Skill("vigor", mods: Add(StatId.MaxHealth, 80f));
            _manager.Grant(vigor);
            Assert.That(_health.Max, Is.EqualTo(max + 80f));
            Assert.That(_health.Current, Is.EqualTo(max - 100f + 80f));
            _manager.Grant(vigor);
            Assert.That(_health.Max, Is.EqualTo(max + 160f));
            _manager.ResetRun();
            Assert.That(_health.Max, Is.EqualTo(max));
            Assert.That(_health.Current, Is.LessThanOrEqualTo(max));
        }

        // ------------------------------------------------------------ Bolts

        [Test]
        public void Multishot_FiresASymmetricFanOfBolts()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 6f));
            _manager.Grant(Skill("multi", SkillRarity.Epic, 2, Add(StatId.Multishot, 1f)));
            _manager.Grant(Skill("multi2", SkillRarity.Epic, 2, Add(StatId.Multishot, 1f)));
            _shooter.Fire(enemy);
            var yaws = LiveBolts().Select(b => Mathf.DeltaAngle(0f, b.transform.eulerAngles.y)).OrderBy(y => y).ToArray();
            Assert.That(yaws.Length, Is.EqualTo(3));
            Assert.That(yaws[1], Is.EqualTo(0f).Within(0.5f));
            Assert.That(yaws[0], Is.EqualTo(-yaws[2]).Within(0.5f));
            Assert.That(yaws[2], Is.GreaterThan(5f));
        }

        [Test]
        public void Pierce_FliesThroughTheFirstEnemy_HitsEachOnce()
        {
            var front = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var back = SpawnEnemy(new Vector3(0f, 0f, 6f));
            _shooter.Fire(front);
            Fly(0.6f);
            Assert.That(back.Health, Is.EqualTo(back.MaxHealth), "no pierce: bolt stops at the first body");

            _hits.Clear();
            _manager.Grant(Skill("pierce", SkillRarity.Rare, 3, Add(StatId.Pierce, 1f)));
            front.gameObject.SetActive(false);
            back.gameObject.SetActive(false);
            var front2 = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var back2 = SpawnEnemy(new Vector3(0f, 0f, 7f));
            _shooter.Fire(front2);
            Fly(0.6f);
            Assert.That(_hits.Select(h => h.Enemy), Is.EqualTo(new[] { front2, back2 }));
        }

        [Test]
        public void Ricochet_BouncesToTheNearestEnemyNotHitYet()
        {
            var first = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var second = SpawnEnemy(new Vector3(3f, 0f, 5f));
            SpawnEnemy(new Vector3(-9f, 0f, 4f)); // out of ricochet range
            _manager.Grant(Skill("rico", SkillRarity.Rare, 3, Add(StatId.Ricochet, 1f)));
            _shooter.Fire(first);
            Fly(1f);
            Assert.That(_hits.Select(h => h.Enemy), Is.EqualTo(new[] { first, second }));
            Assert.That(LiveBolts(), Is.Empty, "bounces used up: bolt returned to the pool");
        }

        [Test]
        public void PierceAndBounce_LoseDamagePerEnemy_ArcheroFalloff()
        {
            // KAN-86: -33 % per pierce, -30 % per bounce (AutoShooter defaults). 100 dmg bolt, 150 HP enemies: no kills.
            var front = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var middle = SpawnEnemy(new Vector3(0f, 0f, 6f));
            var side = SpawnEnemy(new Vector3(3f, 0f, 7f));
            _manager.Grant(Skill("pierce", SkillRarity.Rare, 3, Add(StatId.Pierce, 1f)));
            _manager.Grant(Skill("rico", SkillRarity.Rare, 3, Add(StatId.Ricochet, 1f)));
            _shooter.Fire(front);
            Fly(1.2f);
            Assert.That(_hits.Select(h => h.Enemy), Is.EqualTo(new[] { front, middle, side }), "pierce first, then the bounce");
            Assert.That(_hits[0].Damage, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(_hits[1].Damage, Is.EqualTo(100f * (1f - BoltShot.DefaultPierceFalloff)).Within(1e-3f));
            Assert.That(_hits[2].Damage,
                Is.EqualTo(100f * (1f - BoltShot.DefaultPierceFalloff) * (1f - BoltShot.DefaultBounceFalloff)).Within(1e-3f));
            Assert.That(_shooter.BasePierceFalloff, Is.EqualTo(BoltShot.DefaultPierceFalloff));
            Assert.That(_shooter.BaseBounceFalloff, Is.EqualTo(BoltShot.DefaultBounceFalloff));
        }

        [Test]
        public void Crit_DealsTheCritMultiplier_AndMarksTheHit()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 4f));
            _manager.Grant(Skill("eye", SkillRarity.Rare, 4, Add(StatId.CritChance, 1f)));
            _shooter.Fire(enemy);
            Fly(0.4f);
            Assert.That(_hits.Count, Is.EqualTo(1));
            Assert.That(_hits[0].Crit, Is.True);
            Assert.That(enemy.LastHitCrit, Is.True);
            Assert.That(enemy.IsAlive, Is.False, "200 crit damage kills a 150 HP enemy");
        }

        // ------------------------------------------------------------ Behaviour skills

        [Test]
        public void EmberBolts_BurnInTicks_AfterTheHit()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 4f));
            _manager.Grant(Behaviour<BurnOnHitEffect>("ember", Add(StatId.BurnDps, 30f)));
            _shooter.Fire(enemy);
            Fly(0.4f);
            Assert.That(enemy.IsBurning, Is.True);
            float hp = enemy.Health;
            enemy.TickStatus(0.25f);
            Assert.That(enemy.Health, Is.EqualTo(hp), "first tick after 0.5 s");
            enemy.TickStatus(0.26f);
            Assert.That(enemy.Health, Is.EqualTo(hp - 15f).Within(1e-3f));
            Assert.That(enemy.LastDamageKind, Is.EqualTo(DamageKind.Burn));
            for (int i = 0; i < 12; i++) enemy.TickStatus(0.5f);
            Assert.That(enemy.IsAlive, Is.False, "50 HP left, 15 per tick for 3 s");
        }

        [Test]
        public void BloodCovenant_HealsAShareOfBoltDamage()
        {
            _health.TakeDamage(200f);
            float hp = _health.Current;
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 4f));
            _manager.Grant(Behaviour<LifeStealEffect>("blood", Add(StatId.LifeSteal, 0.05f)));
            _shooter.Fire(enemy);
            Fly(0.4f);
            Assert.That(_health.Current, Is.EqualTo(hp + 5f).Within(1e-3f));
        }

        [Test]
        public void SoulBurst_DamagesNeighbours_AndChainsOnBurstKills()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var b = SpawnEnemy(new Vector3(2f, 0f, 4f));
            var c = SpawnEnemy(new Vector3(4.2f, 0f, 4f));
            _manager.Grant(Skill("eye", SkillRarity.Rare, 4, Add(StatId.CritChance, 1f))); // one-shot the first enemy
            _manager.Grant(Behaviour<KillBurstEffect>("burst", Add(StatId.KillBurst, 1.6f)));
            var burst = (KillBurstEffect)_manager.OwnedSkills.First(o => o.Effect != null).Effect;

            _shooter.Fire(a);
            Fly(0.4f);
            Assert.That(a.IsAlive, Is.False);
            Assert.That(b.Health, Is.EqualTo(b.MaxHealth), "bursts resolve in the effect's Update, not inside the hit");
            burst.Tick();
            Assert.That(b.IsAlive, Is.False, "160 burst damage within 2.6 m");
            Assert.That(b.LastDamageKind, Is.EqualTo(DamageKind.Burst));
            Assert.That(c.Health, Is.EqualTo(c.MaxHealth), "4.2 m: outside the first burst");
            burst.Tick();
            Assert.That(c.IsAlive, Is.False, "b's death bursts again");
            Assert.That(burst.BurstCount, Is.EqualTo(2), "c's own burst waits for the next frame");
        }

        // ------------------------------------------------------------ Run reset

        [UnityTest]
        public IEnumerator ResetRun_DropsSkillsStatsAndEffects()
        {
            var ember = Behaviour<BurnOnHitEffect>("ember", Add(StatId.BurnDps, 30f));
            _manager.Grant(ember);
            _manager.Grant(Skill("sharp", mods: Pct(StatId.Damage, 0.2f)));
            var effect = _manager.OwnedSkills[0].Effect;
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.transform.parent, Is.EqualTo(_player.transform));

            _manager.ResetRun();
            yield return null;
            Assert.That(_manager.OwnedSkills, Is.Empty);
            Assert.That(_stats.Flat(StatId.BurnDps), Is.Zero);
            Assert.That(_shooter.CurrentDamage, Is.EqualTo(100f));
            Assert.That(effect == null, Is.True, "effect destroyed");
            Assert.That(_manager.GetLevel(ember), Is.Zero);
        }

        // ------------------------------------------------------------ Level-up flow + card screen

        [Test]
        public void LevelUp_ShowsCards_PickAppliesAtOnce_ThenResumes()
        {
            var pool = new[]
            {
                Skill("sharp", mods: Pct(StatId.Damage, 0.2f)), Skill("quick", mods: Pct(StatId.AttackSpeed, 0.15f)),
                Skill("fleet", mods: Pct(StatId.MoveSpeed, 0.1f)), Skill("vigor", mods: Add(StatId.MaxHealth, 80f)),
            };
            Tier(2, 1f, 1f, 1f, pool);
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100, 1f, 1);
            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false);
            var xp = xpGo.AddComponent<XpManager>();
            xp.Setup(curve);
            var flow = xpGo.AddComponent<LevelUpFlow>();
            var screen = Track(new GameObject("HUD")).AddComponent<SkillChoiceScreen>(); // no panel: logic only
            screen.Setup(_manager, armSeconds: 0.4f);
            flow.Setup(xp, _health, screen);
            xpGo.SetActive(true);

            xp.AddXp(100);
            flow.Tick(0f);
            Assert.That(GamePause.IsPaused, Is.True);
            flow.Tick(1f); // celebration over: cards open
            Assert.That(screen.IsShowing, Is.True);
            Assert.That(screen.Options.Count, Is.EqualTo(3));
            Assert.That(screen.Options.Distinct().Count(), Is.EqualTo(3));

            Assert.That(screen.Choose(0), Is.False, "not armed yet: an early tap is ignored");
            screen.Tick(0.45f);
            var picked = screen.Options[1];
            Assert.That(screen.Choose(1), Is.True);
            Assert.That(_manager.GetLevel(picked), Is.EqualTo(1), "applied at once, game still paused");
            Assert.That(screen.Choose(2), Is.False, "one pick per level");
            Assert.That(GamePause.IsPaused, Is.True);

            screen.Tick(1f); // pick animation done: control back to the flow
            Assert.That(screen.IsShowing, Is.False);
            Assert.That(GamePause.IsPaused, Is.False);
            flow.Tick(1f);
            Assert.That(flow.IsBusy, Is.False);
            Assert.That(_manager.OwnedSkills.Count, Is.EqualTo(1));
        }

        [Test]
        public void LevelUp_WithEverySkillMaxed_ClosesWithoutCards()
        {
            var only = Skill("only", max: 1);
            Tier(2, 1f, 1f, 1f, only);
            _manager.Grant(only);
            var screen = Track(new GameObject("HUD")).AddComponent<SkillChoiceScreen>();
            screen.Setup(_manager);
            int done = 0;
            screen.Present(2, () => done++);
            Assert.That(done, Is.EqualTo(1));
            Assert.That(screen.IsShowing, Is.False);
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------ Shipped data

        [Test]
        public void ShippedSkills_AreValid_AndEachChangesThePlayerWhenGranted()
        {
            var skills = UnityEditor.AssetDatabase.FindAssets("t:SkillDefinition", new[] { "Assets/_Project/Data/Skills" })
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .ToArray();
            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>("Assets/_Project/Data/Skills/SkillProgression_Run.asset");
            Assert.That(skills.Length, Is.GreaterThanOrEqualTo(8));
            Assert.That(table, Is.Not.Null);
            Assert.That(skills.Select(s => s.Id).Distinct().Count(), Is.EqualTo(skills.Length), "unique ids");
            var pool = new List<SkillDefinition>();
            table.CollectPool(int.MaxValue, pool);

            _manager.Setup(table, _stats, _shooter, _health, _player.transform, rngSeed: 3);
            foreach (var s in skills)
            {
                Assert.That(string.IsNullOrEmpty(s.Id) || string.IsNullOrEmpty(s.DisplayName) || string.IsNullOrEmpty(s.Description), Is.False, s.name);
                Assert.That(s.Icon, Is.Not.Null, s.Id + " icon");
                Assert.That(s.Modifiers.Length, Is.GreaterThan(0), s.Id + " modifiers");
                Assert.That(pool, Has.Member(s), s.Id + " is in the level table");
                int version = _stats.Version;
                Assert.That(_manager.Grant(s), Is.True, s.Id);
                Assert.That(_stats.Version, Is.GreaterThan(version), s.Id + " changed the stat sheet");
                if (s.EffectPrefab != null) Assert.That(_manager.OwnedSkills.Last().Effect, Is.Not.Null, s.Id + " effect");
            }
            Assert.That(skills.Count(s => s.EffectPrefab != null), Is.GreaterThanOrEqualTo(3), "behaviour skills");
        }
#endif
    }
}
