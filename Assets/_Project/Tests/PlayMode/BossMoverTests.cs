using System;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-164.3 (KAN-239) acceptance, logic side: each new mover follows its path (boomerang out to its range, stop, back
    /// to the start inside its lane; sine inside its amplitude, one wave per wavelength; bounce turns off walls the set
    /// number of times, then breaks); the wall's gap is at least 1.5 x the Knight's body width with no other hole, sits off
    /// the Knight's line, its lanes never cover the gap; a Knight in the gap is never hit, a still Knight is hit once; spike
    /// lines warn at least 0.6 s, hit only inside a line, a pillar stops a line; every cast hits once; the per-boss looks;
    /// the samples and the dev arg names; no GC per frame. Look and feel: screenshots (Docs/screenshots/kan239_*).
    /// </summary>
    public sealed class BossMoverTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private SurvivalTimer _timer;
        private EnemySpawner _spawner;
        private SoulWispField _souls;
        private GroundTelegraph _telegraph;
        private LineTelegraph _lines;
        private BossProjectiles _bolts;
        private Enemy _body;
        private BossFight _fight;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private int _hits, _bounced, _blocked;

        /// <summary>A square room: blocked outside |x|, |z| &lt;= half.</summary>
        private sealed class Room : IWalkability
        {
            private readonly float _half;
            public Room(float half) => _half = half;
            public bool IsBlocked(Vector3 p, float r) => Mathf.Abs(p.x) > _half - r || Mathf.Abs(p.z) > _half - r;
        }

        private sealed class WallNorth : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r) => p.z > 4f - r;
        }

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(2390);

            var playerGo = Track(new GameObject("Player"));
            _player = playerGo.AddComponent<PlayerHealth>(); // max 400
            var collector = playerGo.AddComponent<SoulCollector>();
            collector.Setup(0.01f, 1f);

            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false);
            var xp = xpGo.AddComponent<XpManager>();
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100, 1f, 1);
            xp.Setup(curve);
            xpGo.SetActive(true);
            _souls = Track(new GameObject("Souls")).AddComponent<SoulWispField>();
            _souls.Setup(xp, collector, 64);
            _souls.DriftEnabled = false;

            _timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            _timer.Setup(Track(ChamberRules.Create(600f)), _player.transform, _player);

            var spawnerGo = Track(new GameObject("Enemies"));
            spawnerGo.SetActive(false);
            _spawner = spawnerGo.AddComponent<EnemySpawner>();

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
            _bolts.Setup(_player, null, 64);
            _bolts.HitPlayer += OnHit;
            _bolts.Bounced += OnBounced;
            _bolts.Blocked += OnBlocked;

            var bodyGo = Track(new GameObject("BossBody"));
            bodyGo.SetActive(false);
            _body = bodyGo.AddComponent<Enemy>();
        }

        private void OnHit(Vector3 at, float dealt) => _hits++;
        private void OnBounced(Vector3 at) => _bounced++;
        private void OnBlocked(Vector3 at) => _blocked++;

        [TearDown]
        public void TearDown()
        {
            if (_fight != null && _fight.Boss != null) Object.DestroyImmediate(_fight.Boss.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _fight = null;
            _hits = _bounced = _blocked = 0;
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

        private BossData Data(BossAttack[] attacks, float maxHitShare = 0.35f) =>
            Track(BossData.CreateWithAttacks(_body, attacks, health: 3000f, introDelay: 0.5f, spawnWarning: 0.5f,
                firstAttackDelay: 0.5f, maxHitShare: maxHitShare));

        /// <summary>Begin + rise; then the boss stands at the origin facing +Z with the Knight <paramref name="knightDistance"/> m north.</summary>
        private void StartFight(BossData data, float knightDistance = 8f)
        {
            _fight = Track(new GameObject("Boss")).AddComponent<BossFight>();
            _fight.Setup(data, Track(ChamberRules.Create(600f, null, 10f, 2f, OpenCondition.KillBoss)), _timer, _spawner,
                _player, _souls, _telegraph, _lines, _bolts, null, null);
            Assert.That(_fight.Begin(new ChamberPlan(1, 5, ChamberType.Boss, false, 1f, 1f, 1f, 77)), Is.True);
            Run(data.IntroDelay + data.SpawnWarning + 2f * Frame);
            Assert.That(_fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            _fight.Boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _player.transform.position = new Vector3(0f, 0f, knightDistance);
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) Step();
        }

        private void Step()
        {
            if (_fight != null) _fight.Tick(Frame);
            _bolts.Tick(Frame);
        }

        private void RunUntil(BossFight.Attack attack, float limit = 10f)
        {
            for (float t = 0f; t < limit && _fight.CurrentAttack != attack; t += Frame) Step();
        }

        private void FarKnight() => _player.transform.position = new Vector3(500f, 0f, 500f);

        // ------------------------------------------------------------ Mover paths

        [Test]
        public void Boomerang_FliesToItsRange_StopsThere_ComesBackToTheStart_InsideItsLane()
        {
            FarKnight();
            _bolts.NewVolley();
            Assert.That(_bolts.FireBoomerang(Vector3.zero, Vector3.forward, 12f, 9f, 0.6f, 10f, 0.5f), Is.True);
            Assert.That(_bolts.KindOf(0), Is.EqualTo(BossProjectiles.Mover.Boomerang));
            float total = BossProjectiles.BoomerangTime(12f, 9f);
            Assert.That(total, Is.EqualTo(3f).Within(1e-4f), "4 x 9 m / 12 m/s");
            float maxZ = 0f, maxSide = 0f, slowest = float.MaxValue;
            Vector3 last = Vector3.zero;
            int frames = 0;
            while (_bolts.Count > 0 && frames < 400)
            {
                _bolts.Tick(Frame);
                frames++;
                if (_bolts.Count == 0) break;
                last = _bolts.PositionOf(0);
                maxZ = Mathf.Max(maxZ, last.z);
                maxSide = Mathf.Max(maxSide, Mathf.Abs(last.x));
                slowest = Mathf.Min(slowest, _bolts.VelocityOf(0).magnitude);
            }
            Assert.That(frames * Frame, Is.EqualTo(total).Within(2f * Frame), "gone when back at the start");
            Assert.That(maxZ, Is.EqualTo(9f).Within(0.05f), "turns at its range");
            Assert.That(maxSide, Is.EqualTo(0.6f).Within(0.02f), "bows its bend, no more");
            Assert.That(last.z, Is.LessThan(0.5f), "back at the start");
            Assert.That(slowest, Is.LessThan(1.5f), "hangs at the turn point");
            Vector3 mid = BossProjectiles.BoomerangPoint(Vector3.zero, Vector3.forward, 12f, 9f, 0.6f, total * 0.5f);
            Assert.That(mid.z, Is.EqualTo(9f).Within(1e-4f));
            Assert.That(mid.x, Is.EqualTo(0.6f).Within(1e-4f), "bows to the right of its line");
        }

        [Test]
        public void Sine_WeavesInsideItsAmplitude_OneWavePerWavelength_EndsAtItsLength()
        {
            FarKnight();
            _bolts.NewVolley();
            Assert.That(_bolts.FireSine(Vector3.zero, Vector3.forward, 7f, 14f, 0.7f, 4f, 0f, 10f, 0.5f), Is.True);
            float maxSide = 0f;
            int crossings = 0;
            float prevX = 0f;
            int frames = 0;
            while (_bolts.Count > 0 && frames < 400)
            {
                _bolts.Tick(Frame);
                frames++;
                if (_bolts.Count == 0) break;
                Vector3 p = _bolts.PositionOf(0);
                maxSide = Mathf.Max(maxSide, Mathf.Abs(p.x));
                if (frames > 1 && Mathf.Sign(p.x) != Mathf.Sign(prevX) && Mathf.Abs(prevX) > 1e-4f) crossings++;
                prevX = p.x;
                float expected = 0.7f * Mathf.Sin(2f * Mathf.PI * p.z / 4f);
                Assert.That(p.x, Is.EqualTo(expected).Within(1e-3f), $"on the sine at z {p.z:F2}");
            }
            Assert.That(frames * Frame, Is.EqualTo(2f).Within(2f * Frame), "14 m at 7 m/s");
            Assert.That(maxSide, Is.EqualTo(0.7f).Within(0.02f));
            Assert.That(crossings, Is.InRange(6, 7), "14 m / 4 m per wave = 3.5 waves = 7 line crossings (last at the end)");
            Assert.That(BossProjectiles.SinePoint(Vector3.zero, Vector3.forward, 7f, 0.7f, 4f, Mathf.PI, 1f / 7f).x,
                Is.LessThan(0f), "opposite phase weaves the other way first");
        }

        [Test]
        public void Bounce_TurnsOffTheWalls_ItsSetNumberOfTimes_ThenBreaks()
        {
            FarKnight();
            Enemy.Walkability = new Room(4f);
            _bolts.NewVolley();
            Vector3 dir = new Vector3(0.6f, 0f, 0.8f);
            Assert.That(_bolts.FireBounce(Vector3.zero, dir, 8f, 100f, 2, 10f, 0.5f), Is.True);
            Assert.That(_bolts.BouncesLeftOf(0), Is.EqualTo(2));
            for (int f = 0; f < 600 && _bolts.Count > 0 && _bounced == 0; f++) _bolts.Tick(Frame);
            Assert.That(_bounced, Is.EqualTo(1), "first wall: turns");
            Vector3 v = _bolts.VelocityOf(0);
            Assert.That(v.magnitude, Is.EqualTo(8f).Within(1e-3f), "keeps its speed");
            Assert.That(v.x * dir.x < 0f || v.z * dir.z < 0f, Is.True, "mirrored off the wall");
            Assert.That(_bolts.BouncesLeftOf(0), Is.EqualTo(1));
            for (int f = 0; f < 600 && _bolts.Count > 0; f++) _bolts.Tick(Frame);
            Assert.That(_bounced, Is.EqualTo(2), "second wall: turns again");
            Assert.That(_blocked, Is.EqualTo(1), "third wall: breaks");
            Assert.That(_bolts.Count, Is.EqualTo(0));

            _bounced = _blocked = 0; // a straight bolt still breaks on the first wall
            _bolts.NewVolley();
            _bolts.Fire(Vector3.zero, dir, 8f, 100f, 10f, 0.5f);
            for (int f = 0; f < 600 && _bolts.Count > 0; f++) _bolts.Tick(Frame);
            Assert.That(_bounced, Is.EqualTo(0));
            Assert.That(_blocked, Is.EqualTo(1));
        }

        [Test]
        public void Boomerang_OutAndBackThroughTheKnight_HitsOnce()
        {
            _player.transform.position = new Vector3(0f, 0f, 4f);
            _bolts.NewVolley();
            _bolts.FireBoomerang(Vector3.zero, Vector3.forward, 12f, 9f, 0f, 20f, 0.6f);
            _bolts.FireBoomerang(Vector3.zero, Vector3.forward, 12f, 9f, 0.3f, 20f, 0.6f);
            for (int f = 0; f < 400; f++) _bolts.Tick(Frame);
            Assert.That(_hits, Is.EqualTo(1), "one volley hits once, out or back");
            Assert.That(_player.Current, Is.EqualTo(_player.Max - 20f).Within(1e-3f));
        }

        // ------------------------------------------------------------ Wall maths

        [Test]
        public void Wall_GapIsAtLeastOneAndAHalfKnightBodies_NoOtherHole_OffTheKnightLine_LanesNeverCoverTheGap()
        {
            Assert.That(BossFight.KnightBodyWidth, Is.EqualTo(2f * BossBoulders.KnightRadius));
            Assert.That(BossFight.MinWallGap, Is.GreaterThanOrEqualTo(1.5f * BossFight.KnightBodyWidth - 1e-5f));
            var offsets = new float[BossFight.MaxWallBolts];
            var lanes = new float[LineTelegraph.MaxLanes];
            foreach (float width in new[] { 8f, 12f, 16f })
            foreach (float r in new[] { 0.4f, 0.5f, 0.6f })
            foreach (float gap in new[] { BossFight.MinWallGap, 2f })
            foreach (float t in new[] { 0f, 0.5f, 1f })
            foreach (bool right in new[] { false, true })
            {
                string at = $"w {width} r {r} gap {gap} t {t} right {right}";
                float c = BossFight.PickWallGap(width, r, gap, t, right);
                Assert.That(c > 0f, Is.EqualTo(right), at);
                if (width >= 12f) Assert.That(Mathf.Abs(c) - gap * 0.5f, Is.GreaterThan(0f), $"{at}: the Knight's line is not in the gap");
                int n = BossFight.WallOffsets(offsets, width, r, gap, c);
                Assert.That(n, Is.LessThanOrEqualTo(BossFight.MaxWallBolts), at);
                var sorted = new List<float>(n);
                for (int i = 0; i < n; i++) sorted.Add(offsets[i]);
                sorted.Sort();
                Assert.That(sorted[0], Is.EqualTo(-width * 0.5f).Within(1e-4f), $"{at}: reaches the left end");
                Assert.That(sorted[n - 1], Is.EqualTo(width * 0.5f).Within(1e-4f), $"{at}: reaches the right end");
                int holes = 0;
                for (int i = 1; i < n; i++)
                {
                    float d = sorted[i] - sorted[i - 1];
                    if (d <= 2f * r) continue; // hit circles touch: no way through
                    holes++;
                    Assert.That(d - 2f * r, Is.GreaterThanOrEqualTo(gap - 1e-4f), $"{at}: the clear gap");
                    Assert.That(d - 2f * r, Is.GreaterThanOrEqualTo(1.5f * BossFight.KnightBodyWidth - 1e-4f), at);
                    Assert.That((sorted[i] + sorted[i - 1]) * 0.5f, Is.EqualTo(c).Within(1e-4f), $"{at}: gap where picked");
                }
                Assert.That(holes, Is.EqualTo(1), $"{at}: exactly one way through");

                int k = BossFight.WallLaneOffsets(lanes, LineTelegraph.MaxLanes, width, r, gap, c, out float laneWidth);
                Assert.That(k, Is.InRange(2, LineTelegraph.MaxLanes), at);
                for (int i = 0; i < k; i++)
                {
                    float lo = lanes[i] - laneWidth * 0.5f, hi = lanes[i] + laneWidth * 0.5f;
                    Assert.That(hi <= c - gap * 0.5f + 1e-4f || lo >= c + gap * 0.5f - 1e-4f, Is.True, $"{at}: lane {i} off the gap");
                }
                for (int i = 0; i < n; i++) // every bolt's hit band is warned
                {
                    bool covered = false;
                    for (int j = 0; j < k && !covered; j++)
                        covered = offsets[i] >= lanes[j] - laneWidth * 0.5f - 1e-4f && offsets[i] <= lanes[j] + laneWidth * 0.5f + 1e-4f;
                    Assert.That(covered, Is.True, $"{at}: bolt at {offsets[i]:F2} under a lane");
                }
            }
        }

        // ------------------------------------------------------------ In the fight

        private static BossAttack WallAttack(float damage = 0.2f) =>
            BossAttack.Wall("Wall", BossPhases.Both, 0f, 20f, wallWidth: 12f, gapWidth: BossFight.MinWallGap, telegraph: 0.8f,
                speed: 6f, distance: 16f, hitRadius: 0.5f, damage: damage, recover: 0.5f, cooldown: 30f);

        [Test]
        public void Wall_KnightInTheGap_IsNeverHit()
        {
            StartFight(Data(new[] { WallAttack() }), knightDistance: 8f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Wall));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_lines.IsShowing, Is.True);
            Assert.That(_fight.WallGap, Is.GreaterThanOrEqualTo(BossFight.MinWallGap));
            for (int i = 0; i < _lines.LaneCount; i++)
                Assert.That(Vector3.Dot(_lines.LaneDirection(i), Vector3.forward), Is.EqualTo(1f).Within(1e-4f), "parallel lanes");
            // boss faces +Z: + offset = right = +x. Step into the middle of the gap.
            _player.transform.position = new Vector3(_fight.WallGapCentre, 0f, 8f);
            RunUntil(BossFight.Attack.Recover, 2f);
            Assert.That(_bolts.Count, Is.EqualTo(_fight.WallBolts), "the whole row flies");
            Run(3.5f);
            Assert.That(_hits, Is.EqualTo(0), "the gap is clear");
            Assert.That(_player.Current, Is.EqualTo(_player.Max));
        }

        [Test]
        public void Wall_StillKnight_IsHitOnce()
        {
            StartFight(Data(new[] { WallAttack(0.2f) }), knightDistance: 8f);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(Mathf.Abs(_fight.WallGapCentre) - _fight.WallGap * 0.5f, Is.GreaterThan(0f), "the gap is off the Knight");
            Run(3.5f);
            Assert.That(_hits, Is.EqualTo(1), "a row of bolts hits once");
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.8f).Within(0.01f));
        }

        private static BossAttack SpikeAttack(float telegraph = 0.9f) =>
            BossAttack.Spikes("Spikes", BossPhases.Both, 0f, 20f, lines: 3, spread: 90f, length: 10f, width: 1.2f,
                telegraph: telegraph, damage: 0.25f, recover: 0.5f, cooldown: 30f);

        [Test]
        public void Spikes_WarnAtLeast06Seconds_HitInsideALine_Once()
        {
            var data = Data(new[] { SpikeAttack(telegraph: 0.2f) }); // below the floor on purpose
            StartFight(data, knightDistance: 6f);
            var fxGo = Track(new GameObject("SpikeFx"));
            var fx = fxGo.AddComponent<BossSpikeFx>();
            fx.Setup(_fight, null);
            RunUntil(BossFight.Attack.WindUp);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Spikes));
            Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
            Assert.That(_lines.LaneCount, Is.EqualTo(3));
            Run(BossData.MinTelegraph - 0.05f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "nothing while it warns");
            RunUntil(BossFight.Attack.Recover, 1f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max * 0.75f).Within(0.01f), "on the middle line: one hit");
            Assert.That(fx.Bursts, Is.EqualTo(1));
            Assert.That(fx.Emitted, Is.GreaterThanOrEqualTo(3 * 8), "spikes along all three lines");
        }

        [Test]
        public void Spikes_KnightBetweenLines_OrBehindAPillar_IsSafe()
        {
            StartFight(Data(new[] { SpikeAttack() }), knightDistance: 6f);
            RunUntil(BossFight.Attack.WindUp);
            // lanes at -45, 0, +45 deg: 22.5 deg off the middle one at 6 m is 2.3 m from both lines
            _player.transform.position = Quaternion.Euler(0f, 22.5f, 0f) * new Vector3(0f, 0f, 6f);
            RunUntil(BossFight.Attack.Recover, 2f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "between the lines");

            Enemy.Walkability = new WallNorth(); // a wall at z 4 stops the middle line
            _player.transform.position = new Vector3(0f, 0f, 6f); // the next cast aims north
            Run(30f);
            RunUntil(BossFight.Attack.WindUp, 5f);
            Assert.That(_fight.CurrentMove, Is.EqualTo(BossFight.Move.Spikes));
            Assert.That(_lines.LaneLength(1), Is.LessThan(4f), "the line ends at the wall");
            _player.transform.position = new Vector3(0f, 0f, 5f);
            RunUntil(BossFight.Attack.Recover, 2f);
            Assert.That(_player.Current, Is.EqualTo(_player.Max), "no spikes through the wall");
        }

        [Test]
        public void Movers_InTheFight_FireOneBoltPerLane_OfTheirKind_OneHitPerCast()
        {
            var attacks = new[]
            {
                BossAttack.Sample(BossFight.Move.Boomerang),
                BossAttack.Sample(BossFight.Move.Sine),
                BossAttack.Sample(BossFight.Move.Bounce),
            };
            for (int i = 0; i < attacks.Length; i++) attacks[i].cooldown = 60f;
            StartFight(Data(attacks), knightDistance: 6f);
            var kinds = new[] { BossProjectiles.Mover.Boomerang, BossProjectiles.Mover.Sine, BossProjectiles.Mover.Bounce };
            for (int k = 0; k < attacks.Length; k++)
            {
                _bolts.Clear();
                _hits = 0;
                _player.Heal(_player.Max);
                RunUntil(BossFight.Attack.Chase, 5f);
                Assert.That(_fight.ForceAttack(k), Is.True, attacks[k].ToString());
                Assert.That(_fight.WindUpTime, Is.GreaterThanOrEqualTo(BossData.MinTelegraph));
                Assert.That(_lines.LaneCount, Is.EqualTo(attacks[k].count), "one lane per bolt");
                RunUntil(BossFight.Attack.Recover, 2f);
                Assert.That(_bolts.Count, Is.EqualTo(attacks[k].count), attacks[k].ToString());
                for (int i = 0; i < _bolts.Count; i++) Assert.That(_bolts.KindOf(i), Is.EqualTo(kinds[k]));
                Run(4f);
                Assert.That(_hits, Is.LessThanOrEqualTo(1), $"{attacks[k]}: one cast hits once");
            }
        }

        // ------------------------------------------------------------ Looks, samples, dev arg, GC

        [Test]
        public void Looks_SetBySetLook_AndByTheBossData_AtTheFightStart()
        {
            var cleaver = Track(new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default")));
            var ps = Track(new GameObject("Sprites")).AddComponent<ParticleSystem>();
            var r = ps.GetComponent<ParticleSystemRenderer>();
            _bolts.Setup(_player, ps, 64);
            _bolts.SetLooks(new[]
            {
                new BossProjectiles.LookStyle { material = null, color = Color.white, size = 0.75f, ghostSpacing = 0.38f, ghostAlpha = 0.55f },
                new BossProjectiles.LookStyle { material = cleaver, color = Color.gray, size = 0.95f, ghostSpacing = 0.3f, ghostAlpha = 0.35f, spin = -900f },
            });
            _bolts.SetLook(BossProjectiles.BoltLook.Cleaver);
            Assert.That(_bolts.Look, Is.EqualTo(BossProjectiles.BoltLook.Cleaver));
            Assert.That(r.sharedMaterial, Is.SameAs(cleaver));
            Assert.That(_bolts.Size, Is.EqualTo(0.95f));
            FarKnight();
            _bolts.NewVolley();
            _bolts.Fire(Vector3.zero, Vector3.forward, 5f, 20f, 1f, 0.5f);
            _bolts.Tick(0.1f);
            Assert.That(_bolts.RollOf(0), Is.EqualTo(-90f).Within(0.01f), "the cleaver spins");
            _bolts.SetLook(BossProjectiles.BoltLook.Skull); // no such look in this list: keeps the last one
            Assert.That(r.sharedMaterial, Is.SameAs(cleaver));

            var data = Data(new[] { WallAttack() }).WithBoltLook(BossProjectiles.BoltLook.Cleaver);
            _bolts.SetLook(BossProjectiles.BoltLook.Ember);
            Assert.That(r.sharedMaterial, Is.SameAs(cleaver), "Ember without a material keeps the current one");
            StartFight(data);
            Assert.That(_bolts.Look, Is.EqualTo(BossProjectiles.BoltLook.Cleaver), "the fight start sets the boss's look");
        }

        [Test]
        public void ShippedBosses_HaveTheirBoltLooks()
        {
            var want = new Dictionary<string, BossProjectiles.BoltLook>
            {
                { "Boss_Realm1_Butcher", BossProjectiles.BoltLook.Cleaver },
                { "Boss_PyreLord", BossProjectiles.BoltLook.Fireball },
                { "Boss_RottingColossus", BossProjectiles.BoltLook.Boulder },
                { "Boss_BoneMatriarch", BossProjectiles.BoltLook.Skull },
                { "Boss_Realm1_Warden", BossProjectiles.BoltLook.Ember },
            };
            foreach (var pair in want)
            {
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BossData>($"Assets/_Project/Data/Boss/{pair.Key}.asset");
                Assert.That(data, Is.Not.Null, pair.Key);
                Assert.That(data.BoltLook, Is.EqualTo(pair.Value), pair.Key);
            }
        }

        [Test]
        public void Samples_ObeyTheWarningFloor_AndTheDevArgNamesParse()
        {
            var moves = new[]
            {
                BossFight.Move.Boomerang, BossFight.Move.Sine, BossFight.Move.Bounce, BossFight.Move.Wall, BossFight.Move.Spikes,
            };
            Assert.That((int)BossFight.Move.Boomerang, Is.EqualTo(15), "appended after Barrage (serialized ints)");
            foreach (var move in moves)
            {
                var a = BossAttack.Sample(move);
                Assert.That(a.type, Is.EqualTo(move));
                Assert.That(a.Telegraph, Is.GreaterThanOrEqualTo(BossData.MinTelegraph), move.ToString());
                Assert.That(a.damage, Is.InRange(0.15f, 0.35f), move.ToString());
                Assert.That(a.maxRange, Is.GreaterThan(a.minRange), move.ToString());
                Assert.That(Enum.TryParse(move.ToString().ToLowerInvariant(), true, out BossFight.Move parsed), Is.True,
                    "-ss-boss-attack <move> parses");
                Assert.That(parsed, Is.EqualTo(move));
            }
            var wall = BossAttack.Sample(BossFight.Move.Wall);
            Assert.That(wall.gap, Is.GreaterThanOrEqualTo(BossFight.MinWallGap));
            var boomerang = BossAttack.Sample(BossFight.Move.Boomerang);
            Assert.That(boomerang.wave, Is.LessThanOrEqualTo(boomerang.width * 0.5f), "bows inside its lane");
            var sine = BossAttack.Sample(BossFight.Move.Sine);
            Assert.That(sine.wave, Is.LessThanOrEqualTo(sine.width * 0.5f), "weaves inside its lane");
            Assert.That(BossAttack.Sample(BossFight.Move.Bounce).bounces, Is.GreaterThan(0));
        }

        [Test]
        public void MoverTicks_DoNotAllocate()
        {
            Enemy.Walkability = new Room(9f);
            var attacks = new[]
            {
                BossAttack.Sample(BossFight.Move.Boomerang),
                BossAttack.Sample(BossFight.Move.Sine),
                BossAttack.Sample(BossFight.Move.Bounce),
                WallAttack(),
                SpikeAttack(),
            };
            for (int i = 0; i < attacks.Length; i++)
            {
                attacks[i].cooldown = 0.3f;
                attacks[i].minRange = 0f;
                attacks[i].maxRange = 30f;
            }
            StartFight(Data(attacks), knightDistance: 5f);
            _player.Invulnerable = true;
            var fx = Track(new GameObject("SpikeFx")).AddComponent<BossSpikeFx>();
            fx.Setup(_fight, null);
            Run(25f); // warm-up: every move once (first calls JIT in the editor)
            Assert.That(_fight.WallBolts, Is.GreaterThan(0));
            Assert.That(fx.Bursts, Is.GreaterThan(0));
            Assert.That(() => Run(12f), Is.Not.AllocatingGCMemory());
        }
    }
}
