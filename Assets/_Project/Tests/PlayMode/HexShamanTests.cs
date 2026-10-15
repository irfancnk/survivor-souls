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
using UnityEditor;
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
    /// KAN-107 Hexbound Shaman on the real built prefab (run the Game scene builder first): orb ring (warns >= 0.6 s, 8 orbs
    /// 45 deg apart with one aimed at the Knight, 5 m/s, one hit per ring, a side step into a gap dodges, a kill cancels),
    /// hex (zombies in 6 m only, +30 % speed + expiry, no stacking, bosses immune), raise (2 Laborers per cast on a ring,
    /// circles warn, cap of 4 alive, a kill frees a slot), blink (Knight within 3 m, 6 m away, shrink / grow back, cooldown,
    /// walkable point, drops a cast), markers + puffs, no GC, prefab + Game scene wiring.
    /// The Shaman, shots and warnings are stepped by hand (no Update inside a [Test]); pools have no renderers: logic only.
    /// </summary>
    public sealed class HexShamanTests
    {
        private const float Frame = 1f / 60f;
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";
        private const int TestRealm = 96; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _knight;
        private EnemyProjectiles _shots;
        private EnemyTelegraphs _warnings;
        private GroundHazards _hazards;
        private GroundTelegraph[] _circles;
        private HexFx _fx;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private int _orbsFired, _hexed, _raised, _blinks;

        /// <summary>Everything south of z = 0 is blocked (a wall behind the Shaman).</summary>
        private sealed class WallSouth : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r) => p.z - r < 0f;
        }

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(107);
            _orbsFired = _hexed = _raised = _blinks = 0;
            HexShaman.OrbsFired += OnOrbs;
            HexShaman.Hexed += OnHexed;
            HexShaman.Raised += OnRaised;
            HexShaman.Blinked += OnBlinked;

            var knight = Track(new GameObject("Knight"));
            knight.SetActive(false);
            knight.AddComponent<CharacterController>();
            var mover = knight.AddComponent<PlayerMover>();
            _knight = knight.AddComponent<PlayerHealth>(); // max 400
            knight.SetActive(true);
            mover.enabled = false; // tests place the Knight by hand

            var pools = Track(new GameObject("Enemy Shots"));
            _warnings = pools.AddComponent<EnemyTelegraphs>();
            var lines = new LineTelegraph[2];
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = Track(new GameObject("Lane " + i)).AddComponent<LineTelegraph>();
                lines[i].Setup(new Renderer[3], new Renderer[3]);
            }
            _circles = new GroundTelegraph[8];
            for (int i = 0; i < _circles.Length; i++)
            {
                _circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                _circles[i].Setup(null, null);
            }
            _warnings.Setup(lines, _circles);
            _hazards = pools.AddComponent<GroundHazards>();
            _hazards.Setup(_knight, mover);
            _shots = pools.AddComponent<EnemyProjectiles>();
            _shots.Setup(_knight, _hazards, _warnings);
            _fx = pools.AddComponent<HexFx>();
        }

        [TearDown]
        public void TearDown()
        {
            HexShaman.OrbsFired -= OnOrbs;
            HexShaman.Hexed -= OnHexed;
            HexShaman.Raised -= OnRaised;
            HexShaman.Blinked -= OnBlinked;
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
            HexShaman.Active.Clear();
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
        }

        private void OnOrbs(HexShaman s, int n) => _orbsFired += n;
        private void OnHexed(HexShaman s, int n) => _hexed += n;
        private void OnRaised(HexShaman s, int n) => _raised += n;
        private void OnBlinked(HexShaman s, Vector3 from, Vector3 to) => _blinks++;

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

        private HexShaman SpawnShaman(Vector3 position) => Spawn("HexboundShaman", position).GetComponent<HexShaman>();

        /// <summary>Steps the Shamans, shots, hazards and warning flashes by hand.</summary>
        private void Run(float seconds, params HexShaman[] shamans)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame)
            {
                foreach (var s in shamans) s.Tick(Frame);
                _shots.Tick(Frame);
                _hazards.Tick(Frame);
                foreach (var c in _circles) c.Tick(Frame);
            }
        }

        private void PlaceKnight(Vector3 p) => _knight.transform.position = p;

        private static float Planar(Vector3 a, Vector3 b) => SpecialAttack.Planar(a, b);

        // ------------------------------------------------------------ Orb ring

        [Test]
        public void OrbRing_Warns_ThenEightOrbs45DegApart_OneAimedAtKnight_AtFiveMetresPerSecond()
        {
            var shaman = SpawnShaman(Vector3.zero);
            var cfg = shaman.Config;
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(shaman.ForceCast(HexShaman.Cast.Orbs), Is.True);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.WindUp));
            Assert.That(shaman.Enemy.Halted, Is.True, "stands to cast");
            var circle = shaman.CircleOf(0);
            Assert.That(circle, Is.Not.Null);
            Assert.That(circle.IsShowing, Is.True);
            Assert.That(circle.Radius, Is.EqualTo(cfg.orbWarnRadius).Within(0.001f));
            Assert.That(cfg.orbWindUp, Is.GreaterThanOrEqualTo(HexShaman.MinTelegraph));

            Run(HexShaman.MinTelegraph, shaman);
            Assert.That(_shots.Count, Is.Zero, "nothing flies inside the first 0.6 s of warning");
            Run(cfg.orbWindUp - HexShaman.MinTelegraph + Frame, shaman);
            Assert.That(_shots.Count, Is.EqualTo(8));
            Assert.That(_orbsFired, Is.EqualTo(8));
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Recover));
            Assert.That(shaman.OrbCooldown, Is.EqualTo(cfg.orbCooldown).Within(0.05f));

            Run(1f, shaman);
            var angles = new List<float>();
            bool aimed = false;
            for (int i = 0; i < _shots.Count; i++)
            {
                Assert.That(_shots.KindOf(i), Is.EqualTo(ShotKind.SoulOrb));
                Vector3 p = _shots.PositionOf(i);
                Assert.That(Planar(p, Vector3.zero), Is.EqualTo(cfg.orbSpeed).Within(0.25f), "5 m/s (1 s + the release frames)");
                float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
                angles.Add(Mathf.Repeat(a, 360f));
                if (Mathf.Abs(Mathf.DeltaAngle(a, 0f)) < 0.5f) aimed = true;
            }
            Assert.That(aimed, Is.True, "one orb flies straight at the Knight");
            angles.Sort();
            for (int i = 0; i < angles.Count; i++)
            {
                float gap = Mathf.Repeat(angles[(i + 1) % angles.Count] - angles[i], 360f);
                Assert.That(gap, Is.EqualTo(45f).Within(0.5f), "evenly spaced");
            }
            // The gap between two orbs 4 m out leaves room for the Knight (its body + the orbs' hit radius each side).
            float gapWidth = 2f * 4f * Mathf.Sin(22.5f * Mathf.Deg2Rad) - 2f * cfg.orbHitRadius;
            Assert.That(gapWidth, Is.GreaterThan(1.5f));
        }

        [Test]
        public void OrbRing_HitsKnightOnce_SideStepIntoGapDodges()
        {
            var shaman = SpawnShaman(Vector3.zero);
            var cfg = shaman.Config;
            PlaceKnight(new Vector3(0f, 0f, 4f));
            shaman.ForceCast(HexShaman.Cast.Orbs);
            Run(cfg.orbWindUp + 3f, shaman);
            Assert.That(_knight.Current, Is.EqualTo(400f - 400f * cfg.orbDamageShare).Within(0.01f), "aimed orb hits once");
            Run(cfg.recover, shaman);

            float hp = _knight.Current;
            shaman.ForceCast(HexShaman.Cast.Orbs);
            Run(cfg.orbWindUp + Frame, shaman);
            Assert.That(_shots.Count, Is.EqualTo(8));
            float side = 4f * Mathf.Sin(22.5f * Mathf.Deg2Rad);
            PlaceKnight(new Vector3(side, 0f, 4f * Mathf.Cos(22.5f * Mathf.Deg2Rad))); // step into the gap beside it
            Run(3f, shaman);
            Assert.That(_knight.Current, Is.EqualTo(hp), "the gap is safe");
        }

        [Test]
        public void OrbRing_KillDuringWindUp_HidesWarning_NoOrbs_DeadKnightCancels()
        {
            var shaman = SpawnShaman(Vector3.zero);
            PlaceKnight(new Vector3(0f, 0f, 9f));
            shaman.ForceCast(HexShaman.Cast.Orbs);
            var circle = shaman.CircleOf(0);
            Run(0.3f, shaman);
            shaman.Enemy.TakeDamage(1e6f, Vector3.zero);
            Run(1f, shaman);
            Assert.That(circle.IsShowing, Is.False, "warning hidden");
            Assert.That(_shots.Count, Is.Zero);
            Assert.That(_warnings.FreeCircles(), Is.EqualTo(_circles.Length), "circle back in the pool");

            var other = SpawnShaman(new Vector3(5f, 0f, 0f));
            other.ForceCast(HexShaman.Cast.Orbs);
            _knight.TakeDamage(1e6f);
            Run(1f, other);
            Assert.That(other.CurrentPhase, Is.EqualTo(HexShaman.Phase.Idle));
            Assert.That(_shots.Count, Is.Zero, "no ring at a dead Knight");
            Assert.That(_warnings.FreeCircles(), Is.EqualTo(_circles.Length));
        }

        // ------------------------------------------------------------ Hex

        [Test]
        public void Hex_BuffsZombiesInReachOnly_SpeedAndTint_ThenExpires()
        {
            var shaman = SpawnShaman(Vector3.zero);
            var cfg = shaman.Config;
            PlaceKnight(new Vector3(0f, 0f, 10f));
            var near = new[]
            {
                Spawn("GraveyardLaborer", new Vector3(2f, 0f, 0f)),
                Spawn("GraveyardLaborer", new Vector3(0f, 0f, -3f)),
                Spawn("RustedWatchman", new Vector3(-4f, 0f, 3f)),
            };
            var far = Spawn("GraveyardLaborer", new Vector3(8f, 0f, 0f));
            float speed = near[0].MoveSpeed;
            Assert.That(shaman.HexTargets(), Is.EqualTo(3));
            Assert.That(shaman.ChooseCast(), Is.EqualTo(HexShaman.Cast.Hex));

            Assert.That(shaman.ForceCast(HexShaman.Cast.Hex), Is.True);
            Assert.That(shaman.CurrentCast, Is.EqualTo(HexShaman.Cast.Hex));
            Assert.That(shaman.CircleOf(0).Radius, Is.EqualTo(cfg.hexRadius).Within(0.001f), "circle = the hex reach");
            Run(cfg.castWindUp - 0.1f, shaman);
            Assert.That(near[0].IsHexed, Is.False, "not before the cast ends");
            Run(0.1f + Frame, shaman);
            Assert.That(_hexed, Is.EqualTo(3));
            foreach (var e in near)
            {
                Assert.That(e.IsHexed, Is.True);
                Assert.That(e.SpeedBoost, Is.EqualTo(cfg.hexBoost).Within(1e-4f));
            }
            Assert.That(near[0].MoveSpeed, Is.EqualTo(speed * cfg.hexBoost).Within(1e-3f), "+30 % speed");
            Assert.That(far.IsHexed, Is.False, "outside the 6 m reach");
            Assert.That(shaman.Enemy.IsHexed, Is.False, "never hexes itself");
            Assert.That(shaman.CastCooldown, Is.EqualTo(cfg.castCooldown).Within(0.05f));

            near[0].TickStatus(cfg.hexDuration - 0.5f);
            Assert.That(near[0].IsHexed, Is.True);
            near[0].TickStatus(0.6f);
            Assert.That(near[0].IsHexed, Is.False, "expires after 5 s");
            Assert.That(near[0].SpeedBoost, Is.EqualTo(1f));
            Assert.That(near[0].MoveSpeed, Is.EqualTo(speed).Within(1e-3f));
        }

        [Test]
        public void ApplyHex_RefreshesWithoutStacking_BossesAndDeadIgnore()
        {
            var zombie = Spawn("GraveyardLaborer", Vector3.zero);
            Assert.That(zombie.ApplyHex(1.3f, 5f), Is.True);
            zombie.TickStatus(3f);
            Assert.That(zombie.ApplyHex(1.3f, 5f), Is.True);
            Assert.That(zombie.SpeedBoost, Is.EqualTo(1.3f).Within(1e-4f), "no stacking");
            Assert.That(zombie.HexLeft, Is.EqualTo(5f).Within(1e-3f), "refreshed");
            zombie.ApplyHex(1.1f, 1f);
            Assert.That(zombie.SpeedBoost, Is.EqualTo(1.3f).Within(1e-4f), "keeps the stronger boost");

            var boss = Spawn("AshgraveButcher", new Vector3(10f, 0f, 0f));
            Assert.That(boss.ApplyHex(1.3f, 5f), Is.False, "bosses keep their own phase speed");
            Assert.That(boss.SpeedBoost, Is.EqualTo(1f));

            zombie.TakeDamage(1e6f, Vector3.zero);
            Assert.That(zombie.IsHexed, Is.False);
            Assert.That(zombie.ApplyHex(1.3f, 5f), Is.False);
        }

        // ------------------------------------------------------------ Raise

        [Test]
        public void Raise_NoZombiesNear_RaisesTwoLaborersOnRing_CircleWarns_CapFourAlive()
        {
            var spawner = Track(new GameObject("Spawner")).AddComponent<EnemySpawner>();
            Assert.That(EnemySpawner.Current, Is.EqualTo(spawner));
            var shaman = SpawnShaman(Vector3.zero);
            var cfg = shaman.Config;
            Assert.That(shaman.RaiseType, Is.Not.Null);
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(shaman.HexTargets(), Is.Zero);
            Assert.That(shaman.ChooseCast(), Is.EqualTo(HexShaman.Cast.Raise));

            Assert.That(shaman.ForceCast(HexShaman.Cast.Hex), Is.True, "hex with no one near falls back to raise");
            Assert.That(shaman.CurrentCast, Is.EqualTo(HexShaman.Cast.Raise));
            Assert.That(shaman.PlannedRaises, Is.EqualTo(2));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(shaman.CircleOf(i).IsShowing, Is.True, "a circle warns where each rises");
                Assert.That(Planar(shaman.RaisePoint(i), Vector3.zero), Is.EqualTo(cfg.raiseRing).Within(0.01f));
                Assert.That(Mathf.Abs(shaman.RaisePoint(i).x), Is.GreaterThan(1f), "beside the Knight's line, not on it");
            }
            Run(HexShaman.MinTelegraph, shaman);
            Assert.That(_raised, Is.Zero, "nothing rises inside the first 0.6 s");
            Run(cfg.castWindUp - HexShaman.MinTelegraph + Frame, shaman);
            Assert.That(_raised, Is.EqualTo(2));
            Assert.That(shaman.RaisedAlive, Is.EqualTo(2));
            Assert.That(spawner.InUse, Is.EqualTo(2));
            Run(cfg.recover, shaman);

            Assert.That(shaman.ForceCast(HexShaman.Cast.Raise), Is.True);
            Run(cfg.castWindUp + cfg.recover + Frame, shaman);
            Assert.That(shaman.RaisedAlive, Is.EqualTo(4), "second cast: 4 alive");
            Assert.That(shaman.ForceCast(HexShaman.Cast.Raise), Is.False, "cap: no more than 4 of its own alive");
            Assert.That(shaman.ChooseCast(), Is.EqualTo(HexShaman.Cast.Hex), "with its dead around it, it hexes them instead");

            // A raised one dies: one slot frees, the next raise plans only one.
            Enemy victim = null;
            foreach (var e in Enemy.Active)
                if (e != shaman.Enemy && e.IsAlive) { victim = e; break; }
            victim.TakeDamage(1e6f, Vector3.zero);
            Assert.That(shaman.RaisedAlive, Is.EqualTo(3));
            Assert.That(shaman.ForceCast(HexShaman.Cast.Raise), Is.True);
            Assert.That(shaman.PlannedRaises, Is.EqualTo(1));
            shaman.Cancel();
            Assert.That(_warnings.FreeCircles(), Is.EqualTo(_circles.Length), "cancel returns the raise circles");
        }

        // ------------------------------------------------------------ Blink

        [Test]
        public void Blink_KnightWithin3m_ShrinksAway_Reappears6mAway_GrowsBack_Cooldown()
        {
            var shaman = SpawnShaman(Vector3.zero);
            var cfg = shaman.Config;
            float scale = shaman.transform.localScale.x;
            PlaceKnight(new Vector3(0f, 0f, 3.5f));
            Run(0.5f, shaman);
            Assert.That(shaman.CurrentPhase, Is.Not.EqualTo(HexShaman.Phase.Vanish), "3.5 m: no blink");

            PlaceKnight(new Vector3(0f, 0f, 2.5f));
            shaman.Tick(Frame);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Vanish));
            Assert.That(shaman.BlinkCooldown, Is.EqualTo(cfg.blinkCooldown).Within(0.05f));
            Assert.That(_fx.Puffs, Is.EqualTo(1), "puff where it leaves");
            Run(cfg.blinkTime * 0.6f, shaman);
            Assert.That(shaman.transform.localScale.x, Is.LessThan(scale * 0.6f), "shrinks away");

            Run(cfg.blinkTime * 0.4f + Frame, shaman);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Appear));
            Assert.That(_blinks, Is.EqualTo(1));
            Assert.That(_fx.Puffs, Is.EqualTo(2), "puff where it comes back");
            Vector3 to = shaman.transform.position;
            Assert.That(Planar(to, _knight.transform.position), Is.EqualTo(cfg.blinkDistance).Within(0.01f));
            Assert.That(to.z, Is.LessThan(-3f), "straight away from the Knight");
            Run(cfg.blinkTime + Frame, shaman);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Idle));
            Assert.That(shaman.transform.localScale.x, Is.EqualTo(scale).Within(1e-4f), "full size again");
            Assert.That(shaman.Enemy.Halted, Is.False);

            PlaceKnight(to + new Vector3(0f, 0f, 2f)); // close again: cooldown first
            Run(cfg.blinkCooldown - cfg.blinkTime * 2f - 0.3f, shaman);
            Assert.That(_blinks, Is.EqualTo(1), "no blink inside the cooldown");
            Run(0.6f, shaman);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Vanish).Or.EqualTo(HexShaman.Phase.Appear), "blinks after 6 s");
        }

        [Test]
        public void Blink_PicksWalkablePoint_DropsCastAndHidesWarning()
        {
            Enemy.Walkability = new WallSouth();
            var shaman = SpawnShaman(new Vector3(0f, 0f, 1f));
            PlaceKnight(new Vector3(0f, 0f, 6f));
            Assert.That(shaman.ForceCast(HexShaman.Cast.Orbs), Is.True);
            var circle = shaman.CircleOf(0);
            Run(0.2f, shaman);
            PlaceKnight(new Vector3(0f, 0f, 3f)); // 2 m: blink; straight away (south) is the wall
            shaman.Tick(Frame);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Vanish), "blink beats the cast");
            Assert.That(circle.IsShowing, Is.False, "the cast's warning hides");
            Assert.That(_warnings.FreeCircles(), Is.EqualTo(_circles.Length));
            Vector3 to = shaman.BlinkTarget;
            Assert.That(to.z, Is.GreaterThanOrEqualTo(0.5f), "not into the wall");
            Assert.That(Planar(to, _knight.transform.position), Is.EqualTo(shaman.Config.blinkDistance).Within(0.01f));
            Run(1f, shaman);
            Assert.That(_shots.Count, Is.Zero, "the dropped ring never fires");
        }

        [Test]
        public void KilledWhileVanishing_RestoresSize()
        {
            var shaman = SpawnShaman(Vector3.zero);
            float scale = shaman.transform.localScale.x;
            PlaceKnight(new Vector3(0f, 0f, 2f));
            Run(0.15f, shaman);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Vanish));
            shaman.Enemy.TakeDamage(1e6f, Vector3.zero);
            shaman.Tick(Frame);
            Assert.That(shaman.transform.localScale.x, Is.EqualTo(scale).Within(1e-4f));
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Idle));
        }

        // ------------------------------------------------------------ Markers, GC, wiring

        [Test]
        public void Markers_OnePerLivingShaman_OverTheHead()
        {
            var a = SpawnShaman(Vector3.zero);
            var b = SpawnShaman(new Vector3(6f, 0f, 0f));
            PlaceKnight(new Vector3(0f, 0f, 20f));
            Assert.That(HexShaman.Active.Count, Is.EqualTo(2));
            _fx.DrawMarkers(0f);
            Assert.That(_fx.MarkerCount, Is.EqualTo(2));
            Assert.That(a.MarkerPoint.y, Is.GreaterThan(a.Enemy.Height), "over the head");
            b.Enemy.TakeDamage(1e6f, Vector3.zero);
            _fx.DrawMarkers(0.1f);
            Assert.That(_fx.MarkerCount, Is.EqualTo(1), "a dead Shaman loses its marker");
        }

        [Test]
        public void OrbsHexBlink_NoGcPerFrame()
        {
            var shaman = SpawnShaman(Vector3.zero);
            var zombie = Spawn("GraveyardLaborer", new Vector3(2f, 0f, 0f));
            Spawn("GraveyardLaborer", new Vector3(-2f, 0f, 0f));
            var all = new[] { shaman }; // made here: a params array would allocate in the check
            PlaceKnight(new Vector3(0f, 0f, 9f));
            shaman.ForceCast(HexShaman.Cast.Orbs);
            Run(1.5f, all); // warm-up
            shaman.ForceCast(HexShaman.Cast.Hex);
            Run(1.5f, all);
            zombie.TickStatus(Frame); // first violet tint makes the body's property block once
            _fx.DrawMarkers(0f);

            Assert.That(() =>
            {
                shaman.ForceCast(HexShaman.Cast.Orbs);
                Run(1.3f, all);
                shaman.ForceCast(HexShaman.Cast.Hex);
                Run(1.3f, all);
                zombie.TickStatus(Frame);
                _fx.DrawMarkers(1f);
                PlaceKnight(shaman.transform.position + new Vector3(0f, 0f, 2f));
                Run(0.8f, all);
            }, Is.Not.AllocatingGCMemory(), "orb ring, hex, blink, markers");
            Assert.That(_blinks, Is.EqualTo(1));
        }

        [Test]
        public void Prefab_HasShamanWithDocNumbers_HoldBand_RaisesLaborers()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_HexboundShaman.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.HoldMin, Is.EqualTo(8f));
            Assert.That(prefab.HoldMax, Is.EqualTo(11f));
            Assert.That(prefab.BaseMaxHealth, Is.EqualTo(130f));
            Assert.That(prefab.XpValue, Is.EqualTo(12));
            var shaman = prefab.GetComponent<HexShaman>();
            Assert.That(shaman, Is.Not.Null);
            var c = shaman.Config;
            Assert.That(c.orbCount, Is.EqualTo(8));
            Assert.That(c.orbSpeed, Is.EqualTo(5f));
            Assert.That(c.orbCooldown, Is.EqualTo(5f));
            Assert.That(c.castCooldown, Is.EqualTo(12f));
            Assert.That(c.hexRadius, Is.EqualTo(6f));
            Assert.That(c.hexBoost, Is.EqualTo(1.3f).Within(1e-4f));
            Assert.That(c.hexDuration, Is.EqualTo(5f));
            Assert.That(c.raiseCount, Is.EqualTo(2));
            Assert.That(c.blinkTrigger, Is.EqualTo(3f));
            Assert.That(c.blinkDistance, Is.EqualTo(6f));
            Assert.That(c.blinkCooldown, Is.EqualTo(6f));
            Assert.That(Mathf.Min(c.orbWindUp, c.castWindUp), Is.GreaterThanOrEqualTo(HexShaman.MinTelegraph));
            Assert.That(shaman.RaiseType, Is.Not.Null);
            Assert.That(shaman.RaiseType.name, Is.EqualTo("Enemy_GraveyardLaborer"));
            Assert.That(prefab.GetComponent<RangedAttack>(), Is.Null);
            Assert.That(prefab.GetComponent<SpecialAttack>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator GameScene_ShamanInBuild_CastsRing_HasMarker()
        {
            TearDown(); // this test uses the real scene's objects
            HexShaman.OrbsFired += OnOrbs;

            RunState.NewRun(TestRealm, 1071);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            Assert.That(HexFx.Current, Is.Not.Null, "Hex FX in the scene");
            var shots = EnemyProjectiles.Current;
            Assert.That(shots, Is.Not.Null);
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            Assert.That(EnemySpawner.Current, Is.EqualTo(spawner));
            var prefab = spawner.FindType("Enemy_HexboundShaman");
            Assert.That(prefab, Is.Not.Null, "Shaman in the build (spawner extra type)");
            var knight = shots.Player;
            knight.Invulnerable = true;
            foreach (var shooter in Object.FindObjectsByType<AutoShooter>()) shooter.enabled = false;

            var enemy = spawner.SpawnAt(prefab, knight.transform.position + new Vector3(0f, 0f, -9f));
            float limit = 12f;
            while (_orbsFired == 0 && limit > 0f)
            {
                limit -= Time.deltaTime;
                yield return null;
            }
            Assert.That(_orbsFired, Is.GreaterThan(0), "casts an orb ring from its hold band");
            Assert.That(HexFx.Current.MarkerCount, Is.GreaterThanOrEqualTo(1), "priority marker drawn");
            enemy.TakeDamage(1e6f, Vector3.zero);

            HexShaman.OrbsFired -= OnOrbs;
            var empty = SceneManager.CreateScene("ShamanEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
#endif
