using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-77 Ember Bolts level perks: a bolt-lit fire spreads on each burn tick to the nearest non-burning foe within
    /// 2.5 m (only one step: a caught fire does not spread on), a burning death blasts once (queued, resolved in the effect's
    /// Tick, never inside the damage event), a plain death does not blast, the shipped L3 / L4 rows carry the perks, and a
    /// spreading tick + a blast allocate nothing.
    /// </summary>
    public sealed class EmberPerkTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private SkillManager _manager;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(77);
            _player = Track(new GameObject("Player"));
            _player.SetActive(false); // Awake after every component exists (readers grab PlayerStats in Awake)
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            var health = _player.AddComponent<PlayerHealth>();
            _player.AddComponent<SoulCollector>();
            _player.SetActive(true);
            _shooter.enabled = false; // tests fire by hand

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());

            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, _shooter, health, _player.transform, rngSeed: 77);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        private BurnOnHitEffect GrantEmber(params StatModifier[] mods)
        {
            var s = Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure("ember", "ember", "ember", SkillRarity.Legendary, 4, mods);
            s.SetPresentation(null, Track(new GameObject("ember FX")).AddComponent<BurnOnHitEffect>());
            _manager.Grant(s);
            return (BurnOnHitEffect)_manager.OwnedSkills.Single().Effect;
        }

        private Enemy SpawnEnemy(Vector3 position)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { });
            return e;
        }

        private static void Fly(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>().Where(b => b.gameObject.scene.IsValid() && b.InFlight).ToArray())
                    b.Tick(Frame);
        }

        private void Shoot(Enemy target)
        {
            _shooter.Fire(target);
            Fly(0.4f);
        }

        // ------------------------------------------------------------ Spread

        [Test]
        public void Spread_EachTickIgnitesTheNearestNonBurningFoeInRange_OneStepOnly()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var near = SpawnEnemy(new Vector3(1.5f, 0f, 4f));
            var next = SpawnEnemy(new Vector3(-2.2f, 0f, 4f));
            var far = SpawnEnemy(new Vector3(0f, 0f, 7f)); // 3 m from a: out of the 2.5 m reach
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnSpread, 1f));
            Assert.That(ember.SpreadRadius, Is.EqualTo(2.5f));

            Shoot(a);
            Assert.That(a.IsBurning, Is.True);
            Assert.That(a.BurnFromSpread, Is.False, "lit by a bolt");
            Assert.That(near.IsBurning || next.IsBurning, Is.False, "fire spreads on a burn tick, not on the hit");

            a.TickStatus(0.51f); // first burn tick
            Assert.That(near.IsBurning, Is.True, "nearest foe caught fire");
            Assert.That(near.BurnFromSpread, Is.True);
            Assert.That(near.BurnDps, Is.EqualTo(30f), "same fire damage");
            Assert.That(next.IsBurning, Is.False, "one foe per tick at BurnSpread 1");

            near.TickStatus(0.51f); // a caught fire ticks but does not spread on
            Assert.That(next.IsBurning, Is.False, "one step: a caught fire does not spread");
            Assert.That(ember.SpreadCount, Is.EqualTo(1));

            a.TickStatus(0.5f); // next tick of the bolt fire: the nearest NON-burning foe in range
            Assert.That(next.IsBurning, Is.True);
            a.TickStatus(0.5f);
            Assert.That(far.IsBurning, Is.False, "out of range");
            Assert.That(ember.SpreadCount, Is.EqualTo(2));
        }

        [Test]
        public void Spread_OffWithoutTheStat()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var near = SpawnEnemy(new Vector3(1f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f));
            Shoot(a);
            a.TickStatus(0.51f);
            a.TickStatus(0.5f);
            Assert.That(near.IsBurning, Is.False);
            Assert.That(ember.SpreadCount, Is.Zero);
        }

        [Test]
        public void ApplyBurn_BoltFireOverASpreadFire_SpreadsAgain()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            e.ApplyBurn(30f, 3f, spread: true);
            Assert.That(e.BurnFromSpread, Is.True);
            e.ApplyBurn(30f, 3f, spread: true);
            Assert.That(e.BurnFromSpread, Is.True, "a second caught fire keeps it caught");
            e.ApplyBurn(30f, 3f);
            Assert.That(e.BurnFromSpread, Is.False, "a bolt hit makes it a bolt fire");
            e.ApplyBurn(30f, 3f, spread: true);
            Assert.That(e.BurnFromSpread, Is.False, "a caught fire never downgrades a bolt fire");
        }

        // ------------------------------------------------------------ Death blast

        [Test]
        public void DeathBlast_FiresOncePerBurningDeath_OnTheEffectTick()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var b = SpawnEnemy(new Vector3(1.5f, 0f, 4f));
            var c = SpawnEnemy(new Vector3(0f, 0f, 6.5f)); // 2.5 m: outside the 2.2 m blast
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnDeathBlast, 0.6f));

            Shoot(a); // 100 of 150 HP, now burning
            Assert.That(a.IsBurning, Is.True);
            Shoot(a); // killing blow on a burning foe
            Assert.That(a.IsAlive, Is.False);
            Assert.That(b.Health, Is.EqualTo(b.MaxHealth), "the blast resolves in the effect's Tick, not inside the hit");

            ember.Tick();
            Assert.That(ember.BlastCount, Is.EqualTo(1));
            Assert.That(b.Health, Is.EqualTo(b.MaxHealth - 60f).Within(1e-3f), "60 % of 100 bolt damage");
            Assert.That(b.LastDamageKind, Is.EqualTo(DamageKind.Burst));
            Assert.That(c.Health, Is.EqualTo(c.MaxHealth), "outside the blast");

            ember.Tick();
            Assert.That(ember.BlastCount, Is.EqualTo(1), "once per death");
        }

        [Test]
        public void DeathBlast_NotOnAPlainDeath()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnDeathBlast, 0.6f));
            a.TakeDamage(1000f, Vector3.forward); // never burned
            ember.Tick();
            Assert.That(ember.BlastCount, Is.Zero, "only burning deaths blast");
        }

        [Test]
        public void DeathBlast_OffWithoutTheStat()
        {
            var b = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var plain = GrantEmber(Add(StatId.BurnDps, 30f));
            Shoot(b);
            Shoot(b);
            Assert.That(b.IsAlive, Is.False);
            plain.Tick();
            Assert.That(plain.BlastCount, Is.Zero, "no blast without BurnDeathBlast");
        }

        [Test]
        public void DeathBlast_BlastKillOfABurningFoe_BlastsAgainNextTick()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var b = SpawnEnemy(new Vector3(1.5f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnDeathBlast, 1.6f));
            b.ApplyBurn(30f, 5f);
            Shoot(a);
            Shoot(a);
            ember.Tick();
            Assert.That(b.IsAlive, Is.False, "160 blast damage kills a 150 HP foe");
            Assert.That(ember.BlastCount, Is.EqualTo(1), "b's own blast waits for the next tick");
            ember.Tick();
            Assert.That(ember.BlastCount, Is.EqualTo(2));
        }

        // ------------------------------------------------------------ Data + GC

        [Test]
        public void ShippedEmberBolts_LevelThreeSpreads_LevelFourBlasts()
        {
#if UNITY_EDITOR
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>("Assets/_Project/Data/Skills/Skill_EmberBolts.asset");
            Assert.That(skill, Is.Not.Null);
            Assert.That(skill.MaxLevel, Is.EqualTo(7), "KAN-220: 7 levels");
            _manager.Grant(skill);
            float[] spread = { 0f, 0f, 1f, 1f, 1f, 1f, 2f };
            float[] blast = { 0f, 0f, 0f, 0.6f, 0.7f, 0.8f, 1f };
            for (int level = 1; level <= 7; level++)
            {
                if (level > 1) _manager.Upgrade(skill);
                Assert.That(_stats.Modify(StatId.BurnSpread, 0f), Is.EqualTo(spread[level - 1]).Within(1e-4f), $"L{level} spread");
                Assert.That(_stats.Modify(StatId.BurnDeathBlast, 0f), Is.EqualTo(blast[level - 1]).Within(1e-4f), $"L{level} blast");
            }
#else
            Assert.Ignore("Editor only: loads the shipped asset.");
#endif
        }

        [Test]
        public void SpreadingTick_AndBlast_AllocateNothing()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            for (int i = 0; i < 6; i++) SpawnEnemy(new Vector3(-1.5f + i * 0.5f, 0f, 5f));
            var victim = SpawnEnemy(new Vector3(1.2f, 0f, 4.6f)); // off the bolt line, burning: never a spread target
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnSpread, 2f), Add(StatId.BurnDeathBlast, 0.3f));
            Shoot(a);
            victim.ApplyBurn(30f, 5f);
            a.TickStatus(0.51f); // warm up the first spread
            Assert.That(() =>
            {
                a.TickStatus(0.5f); // spreads to 2 more foes
                victim.TakeDamage(1000f, Vector3.forward); // burning death: queued
                ember.Tick(); // blast
            }, Is.Not.AllocatingGCMemory());
            Assert.That(ember.SpreadCount, Is.GreaterThanOrEqualTo(4), "2 per tick");
            Assert.That(ember.BlastCount, Is.GreaterThanOrEqualTo(1));
        }
    }
}
