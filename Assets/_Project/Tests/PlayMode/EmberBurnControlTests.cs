using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-134.1 (KAN-199) readable Ember Bolts: the hit that sets a foe on fire stops it for IgniteStun s and flinches it
    /// (once per 1.5 s per foe, never a boss), a hit on a foe that already burns is no ignite, burning foes move BurnSlow
    /// slower for as long as they burn (highest slow wins, no stacking, bosses half), a stunned foe stands, the shipped
    /// levels carry 15 / 18 / 21 / 25 % slow + 0.2 s stun, flames and the flare emit into their shared systems, a burning
    /// death counts, and an ignite hit + a flame frame allocate nothing.
    /// </summary>
    public sealed class EmberBurnControlTests
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
            Random.InitState(134);
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
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, _shooter, health, _player.transform, rngSeed: 134);
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

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        private BurnOnHitEffect GrantEmber(params StatModifier[] mods)
        {
            var s = Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure("ember", "ember", "ember", SkillRarity.Legendary, 4, mods);
            var fx = Track(new GameObject("ember FX"));
            var effect = fx.AddComponent<BurnOnHitEffect>();
            Set(effect, "flames", NewParticles(fx, "Flames"));
            Set(effect, "flare", NewParticles(fx, "Flare"));
            Set(effect, "embers", NewParticles(fx, "Embers"));
            s.SetPresentation(null, effect);
            _manager.Grant(s);
            return (BurnOnHitEffect)_manager.OwnedSkills.Single().Effect;
        }

        private static ParticleSystem NewParticles(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = 500;
            main.startLifetime = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            return ps;
        }

        private static ParticleSystem Child(BurnOnHitEffect fx, string field) =>
            (ParticleSystem)typeof(BurnOnHitEffect).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx);

        private Enemy SpawnEnemy(Vector3 position, bool boss = false, Transform target = null)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            if (boss) Set(e, "isBoss", true);
            e.Spawn(position, target, _ => { }, healthMultiplier: 50f); // tanky: bolts and burn ticks never kill it here
            return e;
        }

        /// <summary>The effect's own bolt hit handler (no bolt flight: deterministic, allocation free).</summary>
        private static Action<BoltHit> HitHandler(BurnOnHitEffect fx) =>
            (Action<BoltHit>)Delegate.CreateDelegate(typeof(Action<BoltHit>), fx,
                typeof(BurnOnHitEffect).GetMethod("OnHit", BindingFlags.NonPublic | BindingFlags.Instance)!);

        private static void Fly(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>().Where(b => b.gameObject.scene.IsValid() && b.InFlight).ToArray())
                    b.Tick(Frame);
        }

        // ------------------------------------------------------------ Ignite flinch

        [Test]
        public void BoltIgnite_StunsAndCounts_HitOnABurningFoeIsNoIgnite()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnSlow, 0.15f), Add(StatId.IgniteStun, 0.2f));
            int ignited = 0;
            Action<Enemy> count = _ => ignited++;
            BurnOnHitEffect.Ignited += count;
            try
            {
                _shooter.Fire(e);
                Fly(0.4f);
                Assert.That(e.IsBurning, Is.True);
                Assert.That(e.IsStunned, Is.True, "the ignite stops the foe");
                Assert.That(ember.IgniteCount, Is.EqualTo(1));
                Assert.That(ember.StunCount, Is.EqualTo(1));
                Assert.That(ignited, Is.EqualTo(1), "Ignited event for sounds (KAN-201)");

                _shooter.Fire(e);
                Fly(0.4f);
                Assert.That(ember.IgniteCount, Is.EqualTo(1), "already burning: no second ignite");
                Assert.That(ignited, Is.EqualTo(1));
            }
            finally { BurnOnHitEffect.Ignited -= count; }
        }

        [Test]
        public void IgniteStun_EndsAfterItsTime_AndWaitsForTheCooldown()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            // Burn 0.5 s (3 - 2.5) so the foe can be lit again inside the 1.5 s flinch cooldown.
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnDuration, -2.5f), Add(StatId.IgniteStun, 0.2f));
            var hit = HitHandler(ember);
            var bolt = new BoltHit { Enemy = e, Point = e.AimPoint, Direction = Vector3.forward, Damage = 1f };
            Assert.That(ember.IgniteCooldown, Is.EqualTo(1.5f));

            hit(bolt);
            Assert.That(e.IsStunned, Is.True);
            e.TickStatus(0.21f);
            Assert.That(e.IsStunned, Is.False, "0.2 s stun");

            e.TickStatus(0.4f); // burn over (0.61 s)
            Assert.That(e.IsBurning, Is.False);
            hit(bolt); // lit again at 0.61 s: an ignite, but the flinch waits
            Assert.That(ember.IgniteCount, Is.EqualTo(2));
            Assert.That(ember.StunCount, Is.EqualTo(1), "once per 1.5 s per foe");
            Assert.That(e.IsStunned, Is.False);

            e.TickStatus(0.6f); // burn over again (1.21 s)
            e.TickStatus(0.35f); // 1.56 s: cooldown over
            hit(bolt);
            Assert.That(ember.IgniteCount, Is.EqualTo(3));
            Assert.That(ember.StunCount, Is.EqualTo(2));
            Assert.That(e.IsStunned, Is.True);
        }

        [Test]
        public void IgniteStun_OffWithoutTheStat()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f));
            HitHandler(ember)(new BoltHit { Enemy = e });
            Assert.That(e.IsBurning, Is.True);
            Assert.That(ember.IgniteCount, Is.EqualTo(1), "still an ignite (flare)");
            Assert.That(e.IsStunned, Is.False);
            Assert.That(e.Slow, Is.Zero);
        }

        [Test]
        public void Boss_NoFlinch_HalfSlow()
        {
            var boss = SpawnEnemy(new Vector3(0f, 0f, 4f), boss: true);
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnSlow, 0.2f), Add(StatId.IgniteStun, 0.2f));
            float before = boss.MoveSpeed;
            HitHandler(ember)(new BoltHit { Enemy = boss });
            Assert.That(boss.IsBurning, Is.True);
            Assert.That(boss.IsStunned, Is.False, "bosses never flinch");
            Assert.That(ember.StunCount, Is.Zero);
            Assert.That(boss.Slow, Is.EqualTo(0.1f).Within(1e-5f), "bosses take half the slow");
            Assert.That(boss.MoveSpeed, Is.EqualTo(before * 0.9f).Within(1e-4f));
            Assert.That(boss.Stun(1f, 1f), Is.False);
        }

        [UnityTest]
        public IEnumerator StunnedFoe_Stands_ThenWalksOn()
        {
            var target = Track(new GameObject("Target")).transform;
            target.position = new Vector3(0f, 0f, 20f);
            var e = SpawnEnemy(Vector3.zero, target: target);
            Assert.That(e.Stun(0.3f, 1.5f), Is.True);
            Vector3 start = e.transform.position;
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(e.transform.position, start), Is.LessThan(1e-4f), "stunned: no chase step");
            float t = 0f;
            while (e.IsStunned && t < 2f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return null;
            yield return null;
            Assert.That(e.transform.position.z, Is.GreaterThan(start.z + 1e-3f), "walks on after the stun");
        }

        // ------------------------------------------------------------ Burn slow

        [Test]
        public void BurnSlow_LastsAsLongAsTheBurn_AndLowersMoveSpeed()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnSlow, 0.15f));
            float before = e.MoveSpeed;
            HitHandler(ember)(new BoltHit { Enemy = e });
            Assert.That(e.Slow, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(e.MoveSpeed, Is.EqualTo(before * 0.85f).Within(1e-4f));
            Assert.That(e.SlowLeft, Is.EqualTo(ember.Duration).Within(1e-4f), "slow length = burn length");
            for (int i = 0; i < 7; i++) e.TickStatus(0.5f); // 3.5 s > 3 s burn
            Assert.That(e.IsBurning, Is.False);
            Assert.That(e.Slow, Is.Zero, "the slow ends with the burn");
            Assert.That(e.MoveSpeed, Is.EqualTo(before).Within(1e-4f));
        }

        [Test]
        public void ApplySlow_HighestWins_NoStacking_Capped()
        {
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            Assert.That(e.ApplySlow(0.25f, 1f), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(e.ApplySlow(0.15f, 3f), Is.EqualTo(0.25f).Within(1e-5f), "a weaker slow never stacks or lowers it");
            Assert.That(e.SlowLeft, Is.EqualTo(3f).Within(1e-5f), "the longer time wins");
            e.TickStatus(3.1f);
            Assert.That(e.Slow, Is.Zero);
            Assert.That(e.ApplySlow(5f, 1f), Is.EqualTo(Enemy.MaxSlow).Within(1e-5f), "capped");
        }

        [Test]
        public void SpreadFire_AlsoSlows()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var near = SpawnEnemy(new Vector3(1.5f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnSpread, 1f), Add(StatId.BurnSlow, 0.2f));
            HitHandler(ember)(new BoltHit { Enemy = a });
            a.TickStatus(0.51f); // first tick spreads
            Assert.That(near.BurnFromSpread, Is.True);
            Assert.That(near.Slow, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(near.IsStunned, Is.False, "a caught fire is no bolt ignite");
        }

        [Test]
        public void ShippedEmberBolts_SlowAndStunPerLevel()
        {
#if UNITY_EDITOR
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>("Assets/_Project/Data/Skills/Skill_EmberBolts.asset");
            Assert.That(skill, Is.Not.Null);
            _manager.Grant(skill);
            var ember = (BurnOnHitEffect)_manager.OwnedSkills.Single().Effect;
            float[] slow = { 0.15f, 0.18f, 0.21f, 0.25f };
            for (int level = 1; level <= 4; level++)
            {
                if (level > 1) _manager.Upgrade(skill);
                Assert.That(ember.SlowShare, Is.EqualTo(slow[level - 1]).Within(1e-4f), $"L{level} slow");
                Assert.That(ember.IgniteStun, Is.EqualTo(0.2f).Within(1e-4f), $"L{level} stun");
                var e = SpawnEnemy(new Vector3(level, 0f, 4f));
                HitHandler(ember)(new BoltHit { Enemy = e });
                Assert.That(e.Slow, Is.EqualTo(slow[level - 1]).Within(1e-4f), $"L{level} foe slow");
                Assert.That(e.IsStunned, Is.True, $"L{level} ignite flinch");
            }
            Assert.That(skill.DescriptionAt(1), Does.Contain("ablaze"), "the card says what it does");
#else
            Assert.Ignore("Editor only: loads the shipped asset.");
#endif
        }

        // ------------------------------------------------------------ FX + GC

        [Test]
        public void IgniteFlare_FlamesOnBurningFoes_AndBurningDeath()
        {
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var cold = SpawnEnemy(new Vector3(3f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.IgniteStun, 0.2f));
            var flare = Child(ember, "flare");
            var flames = Child(ember, "flames");
            HitHandler(ember)(new BoltHit { Enemy = a });
            Assert.That(flare.particleCount, Is.GreaterThan(10), "flash + puffs + sparks");

            ember.EmitFlames(0.125f); // 24 per s x 0.125 s = 3 per burning foe
            Assert.That(flames.particleCount, Is.EqualTo(3), "flames only on the burning foe");
            Assert.That(cold.IsBurning, Is.False);

            a.TakeDamage(1e6f, Vector3.forward);
            Assert.That(ember.BurningDeathCount, Is.EqualTo(1));
            Assert.That(flames.particleCount, Is.GreaterThan(3), "death flame puff");
            cold.TakeDamage(1e6f, Vector3.forward);
            Assert.That(ember.BurningDeathCount, Is.EqualTo(1), "a plain death is no burning death");
        }

        [Test]
        public void IgniteHit_AndFlameFrame_AllocateNothing()
        {
            var foes = new Enemy[8];
            for (int i = 0; i < foes.Length; i++) foes[i] = SpawnEnemy(new Vector3(-2f + i * 0.6f, 0f, 4f));
            var ember = GrantEmber(Add(StatId.BurnDps, 30f), Add(StatId.BurnSlow, 0.15f), Add(StatId.IgniteStun, 0.2f));
            var hit = HitHandler(ember);
            hit(new BoltHit { Enemy = foes[0] }); // warm up (JIT)
            ember.EmitFlames(0.25f);
            foes[2].TickStatus(Frame);
            var bolt = new BoltHit { Enemy = foes[1] };
            Assert.That(() =>
            {
                hit(bolt);
                ember.EmitFlames(0.25f);
                foes[2].TickStatus(Frame);
            }, Is.Not.AllocatingGCMemory());
            Assert.That(ember.IgniteCount, Is.EqualTo(2));
        }
    }
}
