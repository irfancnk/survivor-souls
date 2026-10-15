#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-106 special melee zombies on the real built prefabs (run the Game scene builder first): Plague Bloater (swell
    /// warns >= 0.6 s, burst only inside the circle, other zombies take half their max, bosses safe, toxic cloud dps, own
    /// burst = no kill, a kill bursts the corpse after the fuse and chains), Frenzied Ghoul (range band, locked lane, one hit,
    /// side-step miss, pillar = no leap, stun), Grave Brute (circle wind-up, ring band hits once, inside after the pass and
    /// outside the circle safe), cancels, no warning = no start, no GC, prefab + wave wiring.
    /// Attacks, hazards and warnings are stepped by hand (no Update inside a [Test]); pools have no renderers: logic only.
    /// </summary>
    public sealed class SpecialMeleeTests
    {
        private const float Frame = 1f / 60f;
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _knight;
        private GroundHazards _hazards;
        private EnemyTelegraphs _warnings;
        private GroundTelegraph[] _circles;
        private LineTelegraph[] _lines;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<Enemy> _killed = new List<Enemy>();

        /// <summary>One pillar (r 1) at (0, 0, 2.5): in front of a Ghoul at the origin leaping north.</summary>
        private sealed class PillarAhead : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r)
            {
                float dz = p.z - 2.5f;
                return p.x * p.x + dz * dz < (1f + r) * (1f + r);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(106);
            _killed.Clear();
            Enemy.Damaged += OnDamaged;

            var knight = Track(new GameObject("Knight"));
            knight.SetActive(false);
            knight.AddComponent<CharacterController>();
            var mover = knight.AddComponent<PlayerMover>();
            _knight = knight.AddComponent<PlayerHealth>(); // max 400
            knight.SetActive(true);
            mover.enabled = false; // tests place the Knight by hand

            var pools = Track(new GameObject("Enemy Shots"));
            _warnings = pools.AddComponent<EnemyTelegraphs>();
            _lines = new LineTelegraph[4];
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i] = Track(new GameObject("Lane " + i)).AddComponent<LineTelegraph>();
                _lines[i].Setup(new Renderer[3], new Renderer[3]);
            }
            _circles = new GroundTelegraph[8];
            for (int i = 0; i < _circles.Length; i++)
            {
                _circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                _circles[i].Setup(null, null);
            }
            _warnings.Setup(_lines, _circles);
            _hazards = pools.AddComponent<GroundHazards>();
            _hazards.Setup(_knight, mover);
            pools.AddComponent<EnemyProjectiles>().Setup(_knight, _hazards, _warnings); // attacks find the Knight here
        }

        [TearDown]
        public void TearDown()
        {
            Enemy.Damaged -= OnDamaged;
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
        }

        private void OnDamaged(Enemy enemy, float amount, bool killed)
        {
            if (killed) _killed.Add(enemy);
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy Spawn(string id, Vector3 position)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + id + ".prefab");
            Assert.That(asset, Is.Not.Null, id);
            var enemy = Object.Instantiate(asset);
            Track(enemy.gameObject);
            enemy.gameObject.SetActive(false);
            enemy.Spawn(position, _knight.transform, _ => { });
            return enemy;
        }

        /// <summary>Steps the attacks, hazards and warning flashes by hand.</summary>
        private void Run(float seconds, params SpecialAttack[] attacks)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame)
            {
                foreach (var a in attacks) a.Tick(Frame);
                _hazards.Tick(Frame);
                foreach (var c in _circles) c.Tick(Frame);
                foreach (var l in _lines) l.Tick(Frame);
            }
        }

        /// <summary>Steps until the attack winds up (spawn cooldown) or <paramref name="limit"/> s pass; returns the time.</summary>
        private float RunUntilWindUp(SpecialAttack attack, float limit = 3f)
        {
            float t = 0f;
            while (attack.CurrentPhase != SpecialAttack.Phase.WindUp && t < limit)
            {
                attack.Tick(Frame);
                t += Frame;
            }
            return t;
        }

        private void PlaceKnight(Vector3 p) => _knight.transform.position = p;

        // ------------------------------------------------------------ Plague Bloater

        [Test]
        public void Bloater_SwellWarns_BurstHitsInsideCircle_LeavesToxicCloud_OwnBurstIsNoKill()
        {
            var bloater = Spawn("PlagueBloater", Vector3.zero);
            var burst = bloater.GetComponent<BloaterBurst>();
            PlaceKnight(new Vector3(0f, 0f, 3f)); // outside the 2 m trigger
            Run(2f, burst);
            Assert.That(burst.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle), "waits outside the trigger range");

            PlaceKnight(new Vector3(0f, 0f, 1.8f));
            RunUntilWindUp(burst);
            Assert.That(burst.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.WindUp));
            Assert.That(bloater.Halted, Is.True, "stops to swell");
            Assert.That(burst.Circle, Is.Not.Null);
            Assert.That(burst.Circle.IsShowing, Is.True);
            Assert.That(burst.Circle.Radius, Is.EqualTo(2.8f).Within(0.001f));
            float scale = bloater.transform.localScale.x;

            Run(SpecialAttack.MinTelegraph - 0.05f, burst);
            Assert.That(_knight.Current, Is.EqualTo(400f), "no damage inside the first 0.6 s of warning");
            Assert.That(bloater.transform.localScale.x, Is.GreaterThan(scale * 1.05f), "swells");

            PlaceKnight(new Vector3(2.5f, 0f, 0f)); // still inside r 2.8
            Run(0.5f, burst);
            Assert.That(burst.HasBurst, Is.True);
            Assert.That(_knight.Current, Is.LessThanOrEqualTo(300f + 0.01f), "25 % of 400");
            Assert.That(_knight.Current, Is.GreaterThan(280f), "one burst + a few frames of cloud");
            Assert.That(bloater.IsAlive, Is.False, "dies by its own burst");
            Assert.That(_killed, Has.No.Member(bloater), "own burst: no Damaged kill (no wisp, no kill count)");
            Assert.That(_warnings.RentedCircles(), Is.EqualTo(0), "circle given back");
            Assert.That(_hazards.Count, Is.EqualTo(1));
            Assert.That(_hazards.SpecOf(0).kind, Is.EqualTo(HazardKind.Toxic));
            Assert.That(_hazards.SpecOf(0).radius, Is.EqualTo(2.2f));

            float before = _knight.Current; // r 2.2 cloud: the Knight at 2.5 m is outside
            Run(1f, burst);
            Assert.That(_knight.Current, Is.EqualTo(before), "outside the cloud: no dps");
            PlaceKnight(new Vector3(1f, 0f, 0f));
            Run(1f, burst);
            Assert.That(before - _knight.Current, Is.EqualTo(15f).Within(0.6f), "cloud 15 dps");
            Run(5f, burst);
            Assert.That(_hazards.Count, Is.EqualTo(0), "cloud gone after 6 s");
        }

        [Test]
        public void Bloater_KnightLeavesCircle_NoDamage_ZombiesTakeHalfTheirMax_BossSafe()
        {
            var bloater = Spawn("PlagueBloater", Vector3.zero);
            var burst = bloater.GetComponent<BloaterBurst>();
            var near = Spawn("GraveyardLaborer", new Vector3(-2f, 0f, 0f));
            var far = Spawn("GraveyardLaborer", new Vector3(-5f, 0f, 0f));
            var boss = Spawn("AshgraveButcher", new Vector3(0f, 0f, -1.5f));
            PlaceKnight(new Vector3(0f, 0f, 1.5f));
            RunUntilWindUp(burst);
            PlaceKnight(new Vector3(0f, 0f, 3.5f)); // ran out of the circle
            Run(1.1f, burst);
            Assert.That(burst.HasBurst, Is.True);
            Assert.That(_knight.Current, Is.EqualTo(400f), "outside the circle: safe");
            Assert.That(near.Health, Is.EqualTo(near.MaxHealth * 0.5f).Within(0.01f), "zombie inside: half its max");
            Assert.That(near.LastDamageKind, Is.EqualTo(DamageKind.Burst));
            Assert.That(far.Health, Is.EqualTo(far.MaxHealth), "zombie outside: untouched");
            Assert.That(boss.Health, Is.EqualTo(boss.MaxHealth), "bosses are safe");
        }

        [Test]
        public void Bloater_KilledByKnight_BurstsAfterFuse_ChainsIntoOtherBloater()
        {
            var a = Spawn("PlagueBloater", Vector3.zero);
            var b = Spawn("PlagueBloater", new Vector3(2f, 0f, 0f));
            var burstA = a.GetComponent<BloaterBurst>();
            var burstB = b.GetComponent<BloaterBurst>();
            PlaceKnight(new Vector3(0f, 0f, -6f)); // far from both
            b.TakeDamage(b.MaxHealth * 0.6f, Vector3.zero); // half its max will kill it now

            a.TakeDamage(a.MaxHealth + 1f, Vector3.right);
            Assert.That(_killed, Has.Member(a), "a Knight kill: wisp + kill count as usual");
            Run(SpecialAttack.MinTelegraph - 0.05f, burstA, burstB);
            Assert.That(burstA.HasBurst, Is.False, "the corpse waits for its fuse");
            Assert.That(burstA.Circle, Is.Not.Null);
            Assert.That(burstA.Circle.IsShowing, Is.True, "the circle warns round the corpse");
            Run(0.1f, burstA, burstB);
            Assert.That(burstA.HasBurst, Is.True, "bursts 0.6 s after the warning");
            Assert.That(b.IsAlive, Is.False, "the burst kills the hurt Bloater next to it");
            Assert.That(burstB.HasBurst, Is.False);
            Run(0.7f, burstA, burstB);
            Assert.That(burstB.HasBurst, Is.True, "chain: the second corpse bursts after its own fuse");
            Assert.That(_hazards.Count, Is.EqualTo(2), "two clouds");
            Assert.That(_knight.Current, Is.EqualTo(400f), "the Knight was outside both circles");
        }

        [Test]
        public void Bloater_KilledMidSwell_KeepsTheWarningTimeShown_HitsTheKnightInside()
        {
            var bloater = Spawn("PlagueBloater", Vector3.zero);
            var burst = bloater.GetComponent<BloaterBurst>();
            PlaceKnight(new Vector3(0f, 0f, 1.5f));
            RunUntilWindUp(burst);
            Run(0.7f, burst);
            bloater.TakeDamage(bloater.MaxHealth + 1f, Vector3.forward);
            Run(Frame, burst);
            Assert.That(burst.HasBurst, Is.True, "0.7 s of warning already shown: bursts at once");
            Assert.That(_knight.Current, Is.EqualTo(300f).Within(0.5f), "warned Knight inside the circle is hit");
        }

        [Test]
        public void Bloater_NoFreeWarning_NoSwell()
        {
            var bloater = Spawn("PlagueBloater", Vector3.zero);
            var burst = bloater.GetComponent<BloaterBurst>();
            var rented = new List<GroundTelegraph>();
            for (var c = _warnings.RentCircle(); c != null; c = _warnings.RentCircle()) rented.Add(c);
            PlaceKnight(new Vector3(0f, 0f, 1.5f));
            Run(2f, burst);
            Assert.That(burst.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle), "an attack never starts without its warning");
            Assert.That(burst.ForceAttack(), Is.False);
            foreach (var c in rented) _warnings.Release(c);
            Run(0.5f, burst);
            Assert.That(burst.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.WindUp), "starts once a warning is free");
        }

        // ------------------------------------------------------------ Frenzied Ghoul

        [Test]
        public void Ghoul_CrouchesInBand_LaneLocked_LeapHitsOnce_ThenStunned()
        {
            var ghoul = Spawn("FrenziedGhoul", Vector3.zero);
            var leap = ghoul.GetComponent<GhoulLeap>();
            PlaceKnight(new Vector3(0f, 0f, 5.6f)); // too far: a 5 m leap from here would land short
            Run(2f, leap);
            Assert.That(leap.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle));
            PlaceKnight(new Vector3(0f, 0f, 3f)); // too close
            Run(1f, leap);
            Assert.That(leap.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle));

            PlaceKnight(new Vector3(0f, 0f, 4.8f));
            RunUntilWindUp(leap);
            Assert.That(leap.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.WindUp));
            Assert.That(leap.Lane, Is.Not.Null);
            Assert.That(leap.Lane.IsShowing, Is.True);
            Assert.That(leap.LeapLength, Is.EqualTo(5f).Within(0.01f));
            Assert.That(Vector3.Angle(leap.LeapDirection, Vector3.forward), Is.LessThan(0.5f));
            Assert.That(leap.Config.crouch, Is.GreaterThanOrEqualTo(SpecialAttack.MinTelegraph), "warning rule");

            PlaceKnight(new Vector3(0.3f, 0f, 4.6f)); // moves a little: the lane stays
            Run(0.3f, leap);
            Assert.That(Vector3.Angle(leap.LeapDirection, Vector3.forward), Is.LessThan(0.5f), "lane locked at the crouch start");
            Assert.That(_knight.Current, Is.EqualTo(400f));
            Run(0.4f, leap);
            Assert.That(leap.IsLeaping, Is.True);
            Assert.That(ghoul.transform.position.y, Is.GreaterThan(0.2f), "in the air");
            Run(0.4f, leap);
            Assert.That(leap.IsStunned, Is.True, "lands and staggers");
            Assert.That(leap.HitThisLeap, Is.True);
            Assert.That(_knight.Current, Is.EqualTo(360f).Within(0.01f), "10 % once");
            Assert.That(ghoul.transform.position.z, Is.EqualTo(5f).Within(0.1f));
            Assert.That(ghoul.transform.position.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(ghoul.Halted, Is.True, "punish window");
            Run(0.85f, leap);
            Assert.That(leap.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle));
            Assert.That(ghoul.Halted, Is.False, "sprints again after the stun");
        }

        [Test]
        public void Ghoul_SideStepMisses_PillarAhead_NoLeap()
        {
            var ghoul = Spawn("FrenziedGhoul", Vector3.zero);
            var leap = ghoul.GetComponent<GhoulLeap>();
            PlaceKnight(new Vector3(0f, 0f, 4.8f));
            RunUntilWindUp(leap);
            PlaceKnight(new Vector3(1.6f, 0f, 3.5f)); // side-step out of the 1 m lane
            Run(1.2f, leap);
            Assert.That(leap.IsStunned || leap.CurrentPhase == SpecialAttack.Phase.Idle, Is.True);
            Assert.That(leap.HitThisLeap, Is.False);
            Assert.That(_knight.Current, Is.EqualTo(400f), "side-step: miss");

            var other = Spawn("FrenziedGhoul", new Vector3(20f, 0f, 0f));
            var otherLeap = other.GetComponent<GhoulLeap>();
            other.transform.position = Vector3.zero;
            Enemy.Walkability = new PillarAhead();
            PlaceKnight(new Vector3(0f, 0f, 4.8f));
            Run(3f, otherLeap);
            Assert.That(otherLeap.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle), "a pillar 1.5 m ahead: no leap");
            Assert.That(otherLeap.Attacks, Is.EqualTo(0));
        }

        [Test]
        public void Ghoul_KilledInTheAir_LandsOnTheGround_LaneGivenBack()
        {
            var ghoul = Spawn("FrenziedGhoul", Vector3.zero);
            var leap = ghoul.GetComponent<GhoulLeap>();
            PlaceKnight(new Vector3(0f, 0f, 4.8f));
            RunUntilWindUp(leap);
            Assert.That(_warnings.RentedLines(), Is.EqualTo(1));
            ghoul.TakeDamage(ghoul.MaxHealth + 1f, Vector3.back);
            Run(Frame, leap);
            Assert.That(_warnings.RentedLines(), Is.EqualTo(0), "kill in the crouch: lane given back");
            Assert.That(leap.Lane, Is.Null);

            var second = Spawn("FrenziedGhoul", new Vector3(10f, 0f, 0f));
            var leap2 = second.GetComponent<GhoulLeap>();
            PlaceKnight(new Vector3(10f, 0f, 4.8f));
            RunUntilWindUp(leap2);
            Run(0.75f, leap2);
            Assert.That(leap2.IsLeaping, Is.True);
            second.TakeDamage(second.MaxHealth + 1f, Vector3.back);
            Run(Frame, leap2);
            Assert.That(second.transform.position.y, Is.EqualTo(0f).Within(0.001f), "never left hanging in the air");
        }

        // ------------------------------------------------------------ Grave Brute

        [Test]
        public void Brute_CircleWarns_RingBandHitsOnce()
        {
            var brute = Spawn("GraveBrute", Vector3.zero);
            var slam = brute.GetComponent<BruteSlam>();
            PlaceKnight(new Vector3(0f, 0f, 3f));
            RunUntilWindUp(slam);
            Assert.That(slam.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.WindUp));
            Assert.That(slam.Circle, Is.Not.Null);
            Assert.That(slam.Circle.Radius, Is.EqualTo(4.5f).Within(0.001f), "the whole danger area warns");
            Run(0.85f, slam);
            Assert.That(_knight.Current, Is.EqualTo(400f), "no damage in the wind-up");
            Run(0.1f, slam);
            Assert.That(slam.Front, Is.GreaterThanOrEqualTo(0f), "ring grows");
            Assert.That(slam.Wave, Is.Not.Null);
            Run(0.7f, slam);
            Assert.That(slam.HitThisSlam, Is.True);
            Assert.That(_knight.Current, Is.EqualTo(400f - 72f).Within(0.01f), "18 % once");
            Assert.That(slam.CurrentPhase, Is.Not.EqualTo(SpecialAttack.Phase.Strike), "ring over");
            Assert.That(_warnings.RentedCircles(), Is.EqualTo(0), "circle + ring given back");
            Assert.That(new SerializedObject(brute).FindProperty("knockbackScale").floatValue, Is.EqualTo(0f), "knockback immune");
        }

        [Test]
        public void Brute_InsideAfterThePass_AndOutsideTheCircle_Safe()
        {
            var brute = Spawn("GraveBrute", Vector3.zero);
            var slam = brute.GetComponent<BruteSlam>();
            PlaceKnight(new Vector3(0f, 0f, 3f));
            RunUntilWindUp(slam);
            PlaceKnight(new Vector3(0f, 0f, 4.3f)); // near the edge, ahead of the ring
            Run(0.92f, slam);
            Assert.That(slam.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Strike));
            while (slam.Front >= 0f && slam.Front < 2.5f) Run(Frame, slam);
            PlaceKnight(new Vector3(0f, 0f, 1f)); // dashed through: behind the front
            Run(0.6f, slam);
            Assert.That(slam.HitThisSlam, Is.False);
            Assert.That(_knight.Current, Is.EqualTo(400f), "inside the ring after it passed: safe");

            PlaceKnight(new Vector3(0f, 0f, 20f)); // away while the Brute recovers and cools down
            Run(5f, slam);
            PlaceKnight(new Vector3(0f, 0f, 3f));
            RunUntilWindUp(slam, 6f);
            PlaceKnight(new Vector3(0f, 0f, 5.2f)); // outside the circle
            Run(1.6f, slam);
            Assert.That(_knight.Current, Is.EqualTo(400f), "outside the circle: safe");
        }

        [Test]
        public void Brute_KilledInWindUp_CircleHidden_EnemyFree()
        {
            var brute = Spawn("GraveBrute", Vector3.zero);
            var slam = brute.GetComponent<BruteSlam>();
            PlaceKnight(new Vector3(0f, 0f, 2f));
            RunUntilWindUp(slam);
            var circle = slam.Circle;
            brute.TakeDamage(brute.MaxHealth + 1f, Vector3.back);
            Run(Frame, slam);
            Assert.That(circle.IsShowing, Is.False);
            Assert.That(_warnings.RentedCircles(), Is.EqualTo(0));
            Assert.That(slam.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle));
        }

        [Test]
        public void DeadKnight_CancelsEveryAttack()
        {
            var brute = Spawn("GraveBrute", Vector3.zero);
            var slam = brute.GetComponent<BruteSlam>();
            var ghoul = Spawn("FrenziedGhoul", new Vector3(10f, 0f, 0f));
            var leap = ghoul.GetComponent<GhoulLeap>();
            PlaceKnight(new Vector3(0f, 0f, 2f));
            RunUntilWindUp(slam);
            _knight.TakeDamage(1000f);
            Run(Frame, slam, leap);
            Assert.That(slam.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle));
            Assert.That(brute.Halted, Is.False);
            Assert.That(_warnings.RentedCircles() + _warnings.RentedLines(), Is.EqualTo(0));
        }

        // ------------------------------------------------------------ Performance + wiring

        /// <summary>
        /// One full cycle of each attack first (first calls into Unity may allocate once), then a second cycle from the start
        /// must not allocate: wind-up, ring, leap, landing, burst, cloud.
        /// </summary>
        [Test]
        public void Attacks_NoGcPerFrame()
        {
            var brute = Spawn("GraveBrute", Vector3.zero);
            var ghoul = Spawn("FrenziedGhoul", new Vector3(-12f, 0f, 0f));
            var first = Spawn("PlagueBloater", new Vector3(12f, 0f, 0f));
            var second = Spawn("PlagueBloater", new Vector3(12f, 0f, 8f));
            var slam = brute.GetComponent<BruteSlam>();
            var leap = ghoul.GetComponent<GhoulLeap>();
            var burst = first.GetComponent<BloaterBurst>();
            var burst2 = second.GetComponent<BloaterBurst>();
            PlaceKnight(new Vector3(0f, 0f, 2f));
            Assert.That(slam.ForceAttack(), Is.True);
            Assert.That(leap.ForceAttack(), Is.True);
            Assert.That(burst.ForceAttack(), Is.True);
            var all = new SpecialAttack[] { slam, leap, burst, burst2 }; // made here: a params array would allocate in the check
            Run(2.2f, all); // warm-up cycle
            Assert.That(burst.HasBurst, Is.True);
            Assert.That(slam.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle));
            Assert.That(leap.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Idle));

            Assert.That(() =>
            {
                slam.ForceAttack();
                leap.ForceAttack();
                burst2.ForceAttack();
                Run(1.1f, all);
            }, Is.Not.AllocatingGCMemory(), "Brute ring, Ghoul leap + landing, Bloater burst");
            Assert.That(slam.CurrentPhase, Is.EqualTo(SpecialAttack.Phase.Strike));
            Assert.That(burst2.HasBurst, Is.True);
        }

        [Test]
        public void Prefabs_AttacksWired_WavesHaveTheSpecials()
        {
            var bloater = AssetDatabase.LoadAssetAtPath<BloaterBurst>(Dir + "Enemy_PlagueBloater.prefab");
            var ghoul = AssetDatabase.LoadAssetAtPath<GhoulLeap>(Dir + "Enemy_FrenziedGhoul.prefab");
            var brute = AssetDatabase.LoadAssetAtPath<BruteSlam>(Dir + "Enemy_GraveBrute.prefab");
            Assert.That(bloater, Is.Not.Null);
            Assert.That(ghoul, Is.Not.Null);
            Assert.That(brute, Is.Not.Null);
            Assert.That(bloater.Config.radius, Is.EqualTo(2.8f));
            Assert.That(bloater.Config.cloud.kind, Is.EqualTo(HazardKind.Toxic));
            Assert.That(ghoul.Config.leapDistance, Is.EqualTo(5f));
            Assert.That(brute.Config.radius, Is.EqualTo(4.5f));
            Assert.That(bloater.Config.swell, Is.GreaterThanOrEqualTo(SpecialAttack.MinTelegraph));
            Assert.That(brute.Config.windUp, Is.GreaterThanOrEqualTo(SpecialAttack.MinTelegraph));
            foreach (var other in new[] { "GraveyardLaborer", "BoneArcher", "HexboundShaman" })
                Assert.That(AssetDatabase.LoadAssetAtPath<SpecialAttack>(Dir + "Enemy_" + other + ".prefab"), Is.Null, other);

            var waves = AssetDatabase.LoadAssetAtPath<EnemyWaveTable>("Assets/_Project/Data/Combat/EnemyWaves_Realm1.asset");
            var oldBrute = AssetDatabase.LoadAssetAtPath<Enemy>("Assets/_Project/Prefabs/Enemies/Enemy_ZombieBrute.prefab");
            for (int i = 0; i < waves.PhaseCount; i++)
            {
                var phase = waves.GetPhase(i);
                var types = phase.enemies.Select(e => e.prefab != null ? e.prefab.name : "").ToArray();
                Assert.That(types, Has.No.Member(oldBrute != null ? oldBrute.name : "Enemy_ZombieBrute"), "the Grave Brute replaced the old Brute");
                bool late = phase.startTime >= 45f;
                Assert.That(types.Contains("Enemy_PlagueBloater"), Is.EqualTo(late), $"phase {i} Bloater");
                Assert.That(types.Contains("Enemy_FrenziedGhoul"), Is.EqualTo(late), $"phase {i} Ghoul");
                if (late) Assert.That(types, Has.Member("Enemy_GraveBrute"), $"phase {i} Brute");
            }
        }
    }
}
#endif
