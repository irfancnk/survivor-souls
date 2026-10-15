#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-105 ranged zombies: the keep-distance band (back off, walk in, hold), the shared shot pool (straight hit / miss /
    /// pillar, one hit per volley, lobbed impact only inside the circle, low lobs stopped by walls), ground hazards (dps,
    /// slow, no stacking, max per owner, life), the attack loop of the two shipped types + straight arrows (warning >= 0.6 s before any
    /// damage, aim lanes, 1-1-3 pattern, lead aim, kill cancels the warning), no GC per frame, prefab and Game scene wiring.
    /// Uses the real built prefabs (run the Game scene builder first). Pools have no sprites: logic only.
    /// </summary>
    public sealed class RangedEnemyTests
    {
        private const float Frame = 1f / 60f;
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";
        private const int TestRealm = 96; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _knight;
        private PlayerMover _mover;
        private EnemyProjectiles _shots;
        private GroundHazards _hazards;
        private EnemyTelegraphs _warnings;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private int _fired;

        /// <summary>One pillar (r 1) at (0, 0, 5): between a shooter at the origin and a Knight further north.</summary>
        private sealed class PillarAhead : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r)
            {
                float dz = p.z - 5f;
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
            Random.InitState(105);
            _fired = 0;

            var knight = Track(new GameObject("Knight"));
            knight.SetActive(false);
            knight.AddComponent<CharacterController>();
            _mover = knight.AddComponent<PlayerMover>();
            _knight = knight.AddComponent<PlayerHealth>(); // max 400
            knight.SetActive(true);
            _mover.enabled = false; // tests place the Knight by hand

            var pools = Track(new GameObject("Enemy Shots"));
            _warnings = pools.AddComponent<EnemyTelegraphs>();
            var lines = new LineTelegraph[4];
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = Track(new GameObject("Lane " + i)).AddComponent<LineTelegraph>();
                lines[i].Setup(new Renderer[3], new Renderer[3]);
            }
            var circles = new GroundTelegraph[8];
            for (int i = 0; i < circles.Length; i++)
            {
                circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                circles[i].Setup(null, null);
            }
            _warnings.Setup(lines, circles);
            _hazards = pools.AddComponent<GroundHazards>();
            _hazards.Setup(_knight, _mover);
            _shots = pools.AddComponent<EnemyProjectiles>();
            _shots.Setup(_knight, _hazards, _warnings);
            RangedAttack.Fired += OnFired;
        }

        [TearDown]
        public void TearDown()
        {
            RangedAttack.Fired -= OnFired;
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
        }

        private void OnFired(RangedAttack attack, int shots) => _fired++;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy Spawn(string id, Vector3 position, System.Action<Enemy> setUp = null)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + id + ".prefab");
            Assert.That(asset, Is.Not.Null, id);
            var enemy = Object.Instantiate(asset);
            Track(enemy.gameObject);
            enemy.gameObject.SetActive(false);
            setUp?.Invoke(enemy);
            enemy.Spawn(position, _knight.transform, _ => { });
            return enemy;
        }

        /// <summary>
        /// Straight arrows (hold band 7-9 m, aim lane that locks, 1-1-3 fan, needs a clear line). No shipped type shoots them
        /// since KAN-128 cut the Bone Archer; the framework keeps them, so tests put these numbers on a Pyre Thrower body.
        /// </summary>
        private static readonly RangedAttack.Settings StraightShot = new RangedAttack.Settings
        {
            kind = ShotKind.Arrow, lobbed = false,
            windUp = 0.8f, lockShare = 0.35f, cooldown = 2.2f, pattern = new[] { 1, 1, 3 }, spread = 30f,
            speed = 14f, range = 14f, damageShare = 0.1f, hitRadius = 0.45f, laneWidth = 0.3f, needsLineOfFire = true,
        };

        private Enemy SpawnArcher(Vector3 position) => Spawn("PyreThrower", position, e =>
        {
            e.SetHoldBand(7f, 9f);
            e.GetComponent<RangedAttack>().Configure(StraightShot);
        });

        /// <summary>Steps attack, shots, hazards and warnings by hand (no frames: Update does not run inside a [Test]).</summary>
        private void Run(RangedAttack attack, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
            {
                if (attack != null) attack.Tick(Frame);
                _shots.Tick(Frame);
                _hazards.Tick(Frame);
            }
        }

        // ------------------------------------------------------------ Keep distance

        [UnityTest]
        public IEnumerator HoldBand_BacksOff_WalksIn_ThenHolds()
        {
            _shots.enabled = false; // movement only: no pool, no attack
            var archer = SpawnArcher(Vector3.zero);
            Assert.That(archer.KeepsDistance, Is.True);
            Assert.That(archer.HoldMin, Is.EqualTo(7f));
            Assert.That(archer.HoldMax, Is.EqualTo(9f));

            _knight.transform.position = new Vector3(0f, 0f, 3f); // too close: back off
            yield return new WaitForSeconds(0.6f);
            Assert.That(archer.transform.position.z, Is.LessThan(-0.3f), "backs away from the Knight");
            yield return WaitHolding(archer, 8f);
            float d = Planar(archer.transform.position, _knight.transform.position);
            Assert.That(d, Is.InRange(7f, 9f), "holds inside the band");

            Vector3 held = archer.transform.position;
            yield return new WaitForSeconds(0.5f);
            Assert.That(Planar(archer.transform.position, held), Is.LessThan(0.15f), "stands still while holding");

            _knight.transform.position = archer.transform.position + new Vector3(0f, 0f, 16f); // too far: walk in
            yield return new WaitForSeconds(0.6f);
            Assert.That(archer.IsHolding, Is.False);
            yield return WaitHolding(archer, 8f);
            Assert.That(Planar(archer.transform.position, _knight.transform.position), Is.InRange(7f, 9f));

            archer.ViewReach = 6f; // the screen edge is 6 m away in this direction: the band shrinks to stay on screen
            Assert.That(archer.EffectiveBand, Is.EqualTo(new Vector2(4.5f, 6f)));
            yield return new WaitForSeconds(0.3f);
            yield return WaitHolding(archer, 6f);
            Assert.That(Planar(archer.transform.position, _knight.transform.position), Is.InRange(4.5f, 6f), "holds on screen");
            archer.ViewReach = 1f; // never closer than the floor
            Assert.That(archer.EffectiveBand, Is.EqualTo(new Vector2(Enemy.MinHoldDistance, Enemy.MinHoldDistance + Enemy.MinBandWidth)));
            archer.ViewReach = float.PositiveInfinity;

            yield return WaitHolding(archer, 6f);
            float before = Planar(archer.transform.position, _knight.transform.position);
            archer.NeedsClearShot = true; // a pillar in the way: walk in again
            yield return new WaitForSeconds(0.4f);
            Assert.That(archer.IsHolding, Is.False);
            Assert.That(Planar(archer.transform.position, _knight.transform.position), Is.LessThan(before - 0.3f), "walks in");
        }

        private static IEnumerator WaitHolding(Enemy enemy, float limit)
        {
            while (!enemy.IsHolding && limit > 0f)
            {
                limit -= Time.deltaTime;
                yield return null;
            }
            Assert.That(enemy.IsHolding, Is.True, "reaches the band");
        }

        private static float Planar(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ------------------------------------------------------------ Shots

        [Test]
        public void Straight_HitsOnLane_MissesBeside_StopsOnPillar_OncePerVolley()
        {
            _knight.transform.position = new Vector3(0f, 0f, 8f);
            int v = _shots.NextVolley();
            Assert.That(_shots.FireStraight(v, ShotKind.Arrow, Vector3.zero, Vector3.forward, 14f, 14f, 40f, 0.45f), Is.True);
            Run(null, 1f);
            Assert.That(_knight.Current, Is.EqualTo(360f).Within(0.01f), "hit on the lane");
            Assert.That(_shots.Count, Is.EqualTo(0));

            _knight.transform.position = new Vector3(1.2f, 0f, 8f); // beside the lane: hit radius 0.45
            _shots.FireStraight(_shots.NextVolley(), ShotKind.Arrow, Vector3.zero, Vector3.forward, 14f, 14f, 40f, 0.45f);
            Run(null, 1.2f);
            Assert.That(_knight.Current, Is.EqualTo(360f).Within(0.01f), "miss beside the lane");

            _knight.transform.position = new Vector3(0f, 0f, 8f);
            Enemy.Walkability = new PillarAhead();
            int blocked = 0;
            _shots.Blocked += _ => blocked++;
            _shots.FireStraight(_shots.NextVolley(), ShotKind.Arrow, Vector3.zero, Vector3.forward, 14f, 14f, 40f, 0.45f);
            Run(null, 1f);
            Assert.That(blocked, Is.EqualTo(1), "the pillar stops the arrow");
            Assert.That(_knight.Current, Is.EqualTo(360f).Within(0.01f));
            Enemy.Walkability = null;

            v = _shots.NextVolley(); // a fan of 3 all on the Knight: one hit
            for (int i = 0; i < 3; i++) _shots.FireStraight(v, ShotKind.Arrow, new Vector3(i * 0.2f - 0.2f, 0f, 0f), Vector3.forward, 14f, 14f, 40f, 0.45f);
            Run(null, 1f);
            Assert.That(_knight.Current, Is.EqualTo(320f).Within(0.01f), "one volley hits once");
        }

        [Test]
        public void Lobbed_DamagesOnlyInsideTheCircle_OnLanding_LeavesHazard()
        {
            var fire = new HazardSpec { kind = HazardKind.Fire, radius = 1.6f, life = 3f, dps = 8f };
            var circle = _warnings.RentCircle();
            circle.Show(new Vector3(0f, 0f, 8f), 1.6f);
            _knight.transform.position = new Vector3(0.9f, 0f, 8f); // inside r 1.6
            Assert.That(_shots.FireLobbed(_shots.NextVolley(), ShotKind.FirePot, Vector3.zero, new Vector3(0f, 0f, 8f), 1.1f, 3.2f, 1.6f, 48f,
                fire, 1, 0, false, circle, 0.3f), Is.True);
            Run(null, 0.55f);
            Assert.That(_knight.Current, Is.EqualTo(400f), "no damage in flight");
            Assert.That(_shots.PositionOf(0).y, Is.GreaterThan(3f), "high arc");
            Assert.That(circle.Progress, Is.InRange(0.6f, 0.7f), "circle fills during the flight");
            Run(null, 0.6f);
            Assert.That(_shots.Count, Is.EqualTo(0), "landed");
            Assert.That(_knight.Current, Is.LessThan(352.5f), "impact 48 (+ burning ground)");
            Assert.That(_hazards.Count, Is.EqualTo(1));
            Assert.That(_hazards.CentreOf(0).z, Is.EqualTo(8f).Within(0.01f));
            Assert.That(_warnings.RentedCircles(), Is.EqualTo(0), "circle given back");

            _hazards.Clear();
            float before = _knight.Current;
            _knight.transform.position = new Vector3(2.2f, 0f, 8f); // outside the circle
            _shots.FireLobbed(_shots.NextVolley(), ShotKind.FirePot, Vector3.zero, new Vector3(0f, 0f, 8f), 1.1f, 3.2f, 1.6f, 48f, fire, 1, 0, false, null, 0f);
            Run(null, 1.2f);
            Assert.That(_knight.Current, Is.EqualTo(before), "outside the shown circle: no damage");
            Assert.That(_hazards.Count, Is.EqualTo(1));
        }

        [Test]
        public void LowLob_StoppedByPillar_NoHazard_CircleHidden()
        {
            Enemy.Walkability = new PillarAhead();
            var acid = new HazardSpec { kind = HazardKind.Acid, radius = 1.4f, life = 5f, dps = 12f, slow = 0.3f };
            var circle = _warnings.RentCircle();
            circle.Show(new Vector3(0f, 0f, 9f), 1.4f);
            _knight.transform.position = new Vector3(0f, 0f, 9f);
            _shots.FireLobbed(_shots.NextVolley(), ShotKind.AcidGlob, Vector3.zero, new Vector3(0f, 0f, 9f), 1.3f, 1.1f, 1.4f, 0f, acid, 1, 3, true, circle, 0.3f);
            Run(null, 1.5f);
            Assert.That(_shots.Count, Is.EqualTo(0));
            Assert.That(_hazards.Count, Is.EqualTo(0), "nothing lands outside the shown area");
            Assert.That(circle.IsShowing, Is.False);
            Assert.That(_warnings.RentedCircles(), Is.EqualTo(0));

            // A high fire pot flies over the same pillar.
            var fire = new HazardSpec { kind = HazardKind.Fire, radius = 1.6f, life = 3f, dps = 8f };
            _shots.FireLobbed(_shots.NextVolley(), ShotKind.FirePot, Vector3.zero, new Vector3(0f, 0f, 9f), 1.1f, 3.2f, 1.6f, 48f, fire, 1, 0, false, null, 0f);
            Run(null, 1.2f);
            Assert.That(_hazards.Count, Is.EqualTo(1), "high arc clears the pillar");
        }

        // ------------------------------------------------------------ Hazards

        [Test]
        public void Hazard_DpsAndSlow_NoStacking_MaxPerOwner_Expires()
        {
            var acid = new HazardSpec { kind = HazardKind.Acid, radius = 1.4f, life = 5f, dps = 12f, slow = 0.3f };
            var fire = new HazardSpec { kind = HazardKind.Fire, radius = 1.6f, life = 3f, dps = 8f };
            _knight.transform.position = Vector3.zero;
            _hazards.Spawn(Vector3.zero, acid, 1, 3);
            _hazards.Spawn(new Vector3(0.5f, 0f, 0f), fire, 2, 0); // overlapping: no stacking
            Run(null, 1f);
            Assert.That(400f - _knight.Current, Is.EqualTo(12f).Within(0.3f), "highest dps only");
            Assert.That(_mover.Slow, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(_mover.MaxSpeed, Is.EqualTo(_mover.BaseSpeed * 0.7f).Within(1e-3f), "30 % slower");

            _knight.transform.position = new Vector3(5f, 0f, 0f);
            float hp = _knight.Current;
            Run(null, 0.5f);
            Assert.That(_knight.Current, Is.EqualTo(hp), "outside: no damage");
            Assert.That(_mover.Slow, Is.EqualTo(0f), "outside: full speed");

            for (int i = 0; i < 3; i++) _hazards.Spawn(new Vector3(10f + i * 3f, 0f, 0f), acid, 1, 3);
            Assert.That(_hazards.CountOwned(1), Is.EqualTo(3), "max 3 per spitter");
            for (int i = 0; i < _hazards.Count; i++)
                Assert.That(_hazards.OwnerOf(i) == 1 && _hazards.CentreOf(i).x < 1f, Is.False, "the oldest puddle went first");

            Run(null, 5.1f);
            Assert.That(_hazards.Count, Is.EqualTo(0), "all expired");
        }

        // ------------------------------------------------------------ Attack loop (shipped prefabs)

        [Test]
        public void ShippedTypes_WarnAtLeast06s_BeforeAnyDamage([Values("PyreThrower", "BileSpitter")] string id)
        {
            var enemy = Spawn(id, Vector3.zero);
            var attack = enemy.GetComponent<RangedAttack>();
            Assert.That(attack, Is.Not.Null);
            var cfg = attack.Config;
            Assert.That(cfg.WarningTime(enemy.HoldMin), Is.GreaterThanOrEqualTo(RangedAttack.MinTelegraph), "shipped numbers");
            Assert.That(cfg.windUp, Is.GreaterThan(0f));

            _knight.transform.position = new Vector3(0f, 0f, (enemy.HoldMin + enemy.HoldMax) * 0.5f);
            Assert.That(attack.ForceAttack(), Is.True);
            float warned = 0f;
            bool shown = false;
            float hp = _knight.Current;
            while (_knight.Current >= hp && warned < 5f)
            {
                shown |= attack.AimLane != null && attack.AimLane.IsShowing || attack.CircleOf(0) != null && attack.CircleOf(0).IsShowing;
                attack.Tick(Frame);
                _shots.Tick(Frame);
                _hazards.Tick(Frame);
                warned += Frame;
            }
            Assert.That(shown, Is.True, "a warning showed");
            Assert.That(_knight.Current, Is.LessThan(hp), "the standing Knight gets hit");
            Assert.That(warned, Is.GreaterThanOrEqualTo(RangedAttack.MinTelegraph), "warning before damage");
            Assert.That(_fired, Is.EqualTo(1));
        }

        [Test]
        public void StraightShot_AimLaneFollowsThenLocks_FanEveryThirdVolley()
        {
            var archer = SpawnArcher(Vector3.zero);
            var attack = archer.GetComponent<RangedAttack>();
            _knight.Invulnerable = true;
            _knight.transform.position = new Vector3(0f, 0f, 8f);
            Assert.That(attack.ForceAttack(), Is.True);
            Assert.That(attack.AimLane.LaneCount, Is.EqualTo(1));
            Assert.That(archer.Halted, Is.True, "stops to aim");

            _knight.transform.position = new Vector3(3f, 0f, 8f); // the Knight moves early in the wind-up: the lane follows
            Run(attack, 0.25f);
            Assert.That(attack.AimLane.LaneDirection(0).x, Is.GreaterThan(0.25f), "follows");
            Run(attack, attack.Config.windUp * (1f - attack.Config.lockShare) - 0.2f);
            Vector3 locked = attack.AimLane.LaneDirection(0);
            _knight.transform.position = new Vector3(-3f, 0f, 8f); // late: the lane stays
            Run(attack, 0.15f);
            Assert.That(Vector3.Angle(attack.AimLane.LaneDirection(0), locked), Is.LessThan(0.5f), "locked");

            for (int volley = 1; volley < 3; volley++)
            {
                RunUntilIdle(attack);
                Assert.That(attack.ForceAttack(), Is.True);
                Assert.That(attack.AimLane.LaneCount, Is.EqualTo(volley == 2 ? 3 : 1), "pattern 1, 1, 3");
            }
            float spread = Vector3.Angle(attack.AimLane.LaneDirection(0), attack.AimLane.LaneDirection(2));
            Assert.That(spread, Is.EqualTo(30f).Within(0.5f));
            Run(attack, attack.Config.windUp + Frame);
            Assert.That(_shots.Count, Is.EqualTo(3), "3 arrows");
            Assert.That(archer.Halted, Is.True, "recover");
            Run(attack, RangedAttack.RecoverTime + Frame);
            Assert.That(archer.Halted, Is.False);
        }

        private void RunUntilIdle(RangedAttack attack)
        {
            for (int i = 0; i < 600 && attack.CurrentPhase != RangedAttack.Phase.Idle; i++) Run(attack, Frame);
        }

        [Test]
        public void Pyre_LeadAims_SpreadsThreePots_LeavesFire()
        {
            var pyre = Spawn("PyreThrower", Vector3.zero);
            var attack = pyre.GetComponent<RangedAttack>();
            _knight.transform.position = new Vector3(0f, 0f, 8f);
            // A running Knight: the lead puts the circle ahead of it (velocity is read from the mover).
            var velocity = typeof(PlayerMover).GetField("_velocity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            velocity.SetValue(_mover, new Vector3(5f, 0f, 0f));
            Assert.That(attack.ForceAttack(), Is.True);
            Assert.That(attack.TargetOf(0).x, Is.EqualTo(attack.Config.maxLead).Within(0.05f), "lead aim, capped");
            velocity.SetValue(_mover, Vector3.zero);

            Run(attack, attack.Config.windUp + attack.Config.flightTime + 0.1f); // volley 1 lands
            RunUntilIdle(attack);
            Assert.That(attack.ForceAttack(), Is.True); // volley 2: one pot
            Assert.That(attack.CircleOf(1), Is.Null);
            Run(attack, attack.Config.windUp + attack.Config.flightTime + 0.1f);
            RunUntilIdle(attack);
            _hazards.Clear();

            Assert.That(attack.ForceAttack(), Is.True); // volley 3: three pots
            Assert.That(attack.CircleOf(2), Is.Not.Null, "third volley: 3 pots");
            Assert.That(Vector3.Distance(attack.TargetOf(0), attack.TargetOf(1)), Is.EqualTo(attack.Config.spread).Within(0.01f));
            Assert.That(_warnings.RentedCircles(), Is.EqualTo(3));
            Run(attack, attack.Config.windUp + attack.Config.flightTime + 0.1f);
            Assert.That(_warnings.RentedCircles(), Is.EqualTo(0), "circles given back on impact");
            Assert.That(_hazards.Count, Is.EqualTo(3), "three burning patches");
            for (int i = 0; i < _hazards.Count; i++) Assert.That(_hazards.SpecOf(i).kind, Is.EqualTo(HazardKind.Fire));
        }

        [Test]
        public void Spitter_Puddle_SlowsKnight_MaxThree()
        {
            var spitter = Spawn("BileSpitter", Vector3.zero);
            var attack = spitter.GetComponent<RangedAttack>();
            _knight.transform.position = new Vector3(0f, 0f, 6f);
            for (int volley = 0; volley < 4; volley++)
            {
                RunUntilIdle(attack);
                Assert.That(attack.ForceAttack(), Is.True);
                Run(attack, attack.Config.windUp + attack.Config.FlightTimeFor(Planar(spitter.transform.position, _knight.transform.position) + 0.4f) + 0.05f);
                Assert.That(_hazards.KnightSlow, Is.EqualTo(0.3f).Within(1e-4f),
                    $"acid slows (volley {volley}, hazards {_hazards.Count}, shots {_shots.Count}, phase {attack.CurrentPhase}, knight {_knight.transform.position})");
                _knight.transform.position += new Vector3(1.5f, 0f, 0f); // new spot each volley (distance ~6 m)
            }
            Assert.That(_hazards.Count, Is.EqualTo(3), "max 3 puddles per spitter");
        }

        [Test]
        public void KillDuringWindUp_HidesWarnings_FreesThem()
        {
            var archer = SpawnArcher(Vector3.zero);
            var attack = archer.GetComponent<RangedAttack>();
            _knight.transform.position = new Vector3(0f, 0f, 8f);
            Assert.That(attack.ForceAttack(), Is.True);
            var lane = attack.AimLane;
            Run(attack, 0.3f);
            archer.TakeDamage(1e6f, Vector3.zero);
            Run(attack, Frame);
            Assert.That(lane.IsShowing, Is.False);
            Assert.That(_warnings.RentedLines(), Is.EqualTo(0));
            Assert.That(_shots.Count, Is.EqualTo(0), "no arrow");
        }

        [Test]
        public void LineOfFire_PillarBlocks_SetsNeedsClearShot()
        {
            Assert.That(RangedAttack.ClearShot(Vector3.zero, new Vector3(0f, 0f, 9f)), Is.True);
            Enemy.Walkability = new PillarAhead();
            Assert.That(RangedAttack.ClearShot(Vector3.zero, new Vector3(0f, 0f, 9f)), Is.False);
            Assert.That(RangedAttack.ClearShot(new Vector3(4f, 0f, 0f), new Vector3(4f, 0f, 9f)), Is.True);

            var archer = SpawnArcher(Vector3.zero);
            var attack = archer.GetComponent<RangedAttack>();
            _knight.transform.position = new Vector3(0f, 0f, 9f);
            Run(attack, RangedAttack.SightInterval + Frame);
            Assert.That(archer.NeedsClearShot, Is.True);
            Assert.That(attack.CurrentPhase, Is.EqualTo(RangedAttack.Phase.Idle), "no shot into the pillar");
        }

        [Test]
        public void FlyingShots_AndHazards_DoNotAllocate()
        {
            var archer = SpawnArcher(Vector3.zero);
            var attack = archer.GetComponent<RangedAttack>();
            _knight.Invulnerable = true;
            _knight.transform.position = new Vector3(0f, 0f, 8f);
            var acid = new HazardSpec { kind = HazardKind.Acid, radius = 1.4f, life = 50f, dps = 12f, slow = 0.3f };
            for (int i = 0; i < 6; i++) _hazards.Spawn(new Vector3(i, 0f, 8f), acid, 0, 0);
            for (int i = 0; i < 4; i++)
                _shots.FireLobbed(_shots.NextVolley(), ShotKind.FirePot, Vector3.zero, new Vector3(i, 0f, 8f), 5f, 3f, 1.6f, 1f, acid, 0, 0, false, null, 0f);
            attack.ForceAttack();
            Run(attack, 0.2f); // warm-up
            Assert.That(() => Run(attack, 1.2f), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Wiring

        [Test]
        public void Prefabs_RangedTypesWired_MeleeUntouched()
        {
            (string id, ShotKind kind, bool lobbed, float min, float max)[] ranged =
            {
                ("PyreThrower", ShotKind.FirePot, true, 6f, 10f),
                ("BileSpitter", ShotKind.AcidGlob, true, 5f, 8f),
            };
            foreach (var r in ranged)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + r.id + ".prefab");
                var attack = prefab.GetComponent<RangedAttack>();
                Assert.That(attack, Is.Not.Null, r.id);
                Assert.That(attack.Config.kind, Is.EqualTo(r.kind), r.id);
                Assert.That(attack.Config.lobbed, Is.EqualTo(r.lobbed), r.id);
                Assert.That(prefab.HoldMin, Is.EqualTo(r.min), r.id);
                Assert.That(prefab.HoldMax, Is.EqualTo(r.max), r.id);
            }
            var spitter = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_BileSpitter.prefab").GetComponent<RangedAttack>();
            Assert.That(spitter.Config.maxHazards, Is.EqualTo(3));
            Assert.That(spitter.Config.hazard.slow, Is.EqualTo(0.3f).Within(1e-4f));
            var pyre = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_PyreThrower.prefab").GetComponent<RangedAttack>();
            Assert.That(pyre.Config.hazard.kind, Is.EqualTo(HazardKind.Fire));
            Assert.That(pyre.Config.flightTime, Is.EqualTo(1.1f).Within(1e-4f));

            foreach (var id in new[] { "GraveyardLaborer", "RustedWatchman" })
            {
                var melee = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + id + ".prefab");
                Assert.That(melee.GetComponent<RangedAttack>(), Is.Null, id);
                Assert.That(melee.KeepsDistance, Is.False, id);
            }
        }

        /// <summary>The built Game scene: shot pools wired, ranged types in the build, each one fires at the Knight.</summary>
        [UnityTest]
        public IEnumerator GameScene_RangedTypesSpawnAndFire()
        {
            TearDown(); // this test uses the real scene's objects
            RangedAttack.Fired += OnFired;

            RunState.NewRun(TestRealm, 1051);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var shots = EnemyProjectiles.Current;
            Assert.That(shots, Is.Not.Null, "Enemy Shots in the scene");
            Assert.That(shots.Player, Is.Not.Null);
            Assert.That(EnemyTelegraphs.Current, Is.Not.Null);
            Assert.That(EnemyTelegraphs.Current.LineCapacity, Is.GreaterThanOrEqualTo(4));
            Assert.That(EnemyTelegraphs.Current.CircleCapacity, Is.GreaterThanOrEqualTo(9));
            Assert.That(GroundHazards.Current, Is.Not.Null);
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            var knight = shots.Player;
            knight.Invulnerable = true;
            foreach (var shooter in Object.FindObjectsByType<AutoShooter>()) shooter.enabled = false;

            foreach (string cut in new[] { "FallenMilitiaman", "GraveboundKnight", "BoneArcher" }) // KAN-128
                Assert.That(spawner.FindType("Enemy_" + cut), Is.Null, cut + " still in the build");
            foreach (var extra in spawner.ExtraTypes) Assert.That(extra != null, Is.True, "missing extra type (a deleted prefab)");

            foreach (string id in new[] { "PyreThrower", "BileSpitter" })
            {
                var prefab = spawner.FindType("Enemy_" + id);
                Assert.That(prefab, Is.Not.Null, id + " in the build");
                int before = _fired;
                var enemy = spawner.SpawnAt(prefab, knight.transform.position + new Vector3(0f, 0f, -8f));
                float limit = 10f;
                while (_fired == before && limit > 0f)
                {
                    limit -= Time.deltaTime;
                    yield return null;
                }
                Assert.That(_fired, Is.GreaterThan(before), id + " fires");
                enemy.TakeDamage(1e6f, Vector3.zero);
            }

            RangedAttack.Fired -= OnFired;
            var empty = SceneManager.CreateScene("RangedEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
#endif
