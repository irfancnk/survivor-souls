#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-44: the KAN-30 horde tests that were missing. Foot slide (the shamble clip rate follows the real ground speed, rate
    /// 1 at the clip's own ground speed), lockstep spread (one-frame spawns get different clip phases, speeds and sizes), the
    /// <see cref="EnemyCrowd"/> grid gives the same push as the old all-pairs loop, <see cref="EnemyWaveTable.PrewarmFor"/>
    /// pool sizes, and <see cref="SoulWispField"/> horde merge (24+ wisps out: a drop within 1.6 m of a resting wisp joins it,
    /// no XP lost). The animation tests use the real built zombie prefab.
    /// </summary>
    public sealed class HordeTests
    {
        private const string ZombiePrefab = "Assets/_Project/Prefabs/Combat/Enemy_Zombie.prefab";
        private const float Frame = 1f / 60f;
        private static readonly int WalkHash = Animator.StringToHash("Walk");

        private readonly List<Object> _objects = new List<Object>();
        private Transform _player;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(44);
            _savedWalk = Enemy.Walkability;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.Walkability = null; // open ground: walk straight
            Enemy.LandMaxZ = float.PositiveInfinity;
            Enemy.Active.Clear();
            EnemyCrowd.Invalidate();
            _player = Track(new GameObject("Player")).transform;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
            EnemyCrowd.Invalidate();
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy SpawnZombie(Vector3 position)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(ZombiePrefab);
            Assert.That(asset, Is.Not.Null, ZombiePrefab);
            var enemy = Track(Object.Instantiate(asset));
            enemy.gameObject.SetActive(false);
            enemy.Spawn(position, _player, null);
            return enemy;
        }

        /// <summary>Bare enemy (no body, no animator) standing at <paramref name="position"/> in <see cref="Enemy.Active"/>.</summary>
        private Enemy Dummy(Vector3 position)
        {
            var go = Track(new GameObject("Dummy"));
            go.SetActive(false); // no Update: nothing moves while the test measures
            var enemy = go.AddComponent<Enemy>();
            go.transform.position = position;
            Enemy.Active.Add(enemy);
            return enemy;
        }

        // ------------------------------------------------------------ Foot slide

        [Test]
        public void WalkRate_IsOne_AtTheClipsOwnGroundSpeed()
        {
            var enemy = SpawnZombie(new Vector3(0f, 0f, -20f));
            Assert.That(enemy.AnimRefSpeed, Is.GreaterThan(0f), "shipped zombie has a measured clip ground speed");
            float own = enemy.AnimRefSpeed * enemy.Scale;
            Assert.That(enemy.WalkRate(own), Is.EqualTo(1f).Within(1e-4f), "clip ground speed x body scale = rate 1");
            Assert.That(enemy.WalkRate(own * 1.3f), Is.EqualTo(1.3f).Within(1e-4f), "rate grows with the ground speed");
            Assert.That(enemy.WalkRate(own * 0.6f), Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(enemy.WalkRate(0f), Is.GreaterThan(0f), "a crowded, nearly still zombie still shuffles (rate floor)");
        }

        [UnityTest]
        public IEnumerator Shambler_WalkingAtItsSpeed_PlaysTheClipAtTheMatchingRate()
        {
            var enemy = SpawnZombie(Vector3.zero);
            var animator = enemy.Animator;
            _player.position = new Vector3(0f, 0f, 60f); // far: walks straight the whole test
            yield return new WaitForSeconds(0.8f);        // the smoothed ground speed settles (time constant 0.1 s)

            Vector3 from = enemy.transform.position;
            float start = Time.time;
            yield return new WaitForSeconds(0.5f);
            Vector3 delta = enemy.transform.position - from;
            delta.y = 0f;
            float measured = delta.magnitude / (Time.time - start);

            Assert.That(measured, Is.EqualTo(enemy.MoveSpeed).Within(enemy.MoveSpeed * 0.05f), "walks at its own move speed");
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(WalkHash), "plays the walk clip");
            float expected = measured / (enemy.AnimRefSpeed * enemy.Scale);
            Assert.That(animator.speed, Is.EqualTo(expected).Within(expected * 0.05f),
                "animator speed = ground speed / (clip ground speed x scale): feet stay planted");
            Assert.That(animator.speed, Is.InRange(0.8f, 1.5f), "close to rate 1 at the type's own walk speed");
        }

        // ------------------------------------------------------------ Lockstep spread

        [UnityTest]
        public IEnumerator SameFrameSpawns_GetDifferentPhasesSpeedsAndSizes()
        {
            const int count = 12;
            _player.position = new Vector3(0f, 0f, 60f);
            var zombies = new List<Enemy>();
            for (int i = 0; i < count; i++) zombies.Add(SpawnZombie(new Vector3(-11f + 2f * i, 0f, 0f)));
            yield return null; // the animators evaluate the spawn pose

            var phases = new HashSet<int>();
            var speeds = new HashSet<int>();
            var scales = new HashSet<int>();
            foreach (var z in zombies)
            {
                var state = z.Animator.GetCurrentAnimatorStateInfo(0);
                Assert.That(state.shortNameHash, Is.EqualTo(WalkHash));
                phases.Add(Mathf.RoundToInt(Mathf.Repeat(state.normalizedTime, 1f) * 50f)); // 0.02 buckets
                speeds.Add(Mathf.RoundToInt(z.MoveSpeed * 1000f));
                scales.Add(Mathf.RoundToInt(z.Scale * 1000f));
            }
            Assert.That(phases.Count, Is.GreaterThanOrEqualTo(count - 3), "clip phases spread, not in lockstep");
            Assert.That(speeds.Count, Is.GreaterThanOrEqualTo(count - 3), "per-spawn move speed spread");
            Assert.That(scales.Count, Is.GreaterThanOrEqualTo(count - 3), "per-spawn body size spread");

            // The walk rates differ as well (each rate follows its own speed and size).
            yield return new WaitForSeconds(0.5f);
            var rates = new HashSet<int>();
            foreach (var z in zombies) rates.Add(Mathf.RoundToInt(z.Animator.speed * 1000f));
            Assert.That(rates.Count, Is.GreaterThanOrEqualTo(count - 3), "walk rates spread");
        }

        // ------------------------------------------------------------ EnemyCrowd grid vs all pairs

        /// <summary>The pre-KAN-30 loop: every enemy against every other one, radius clamped like the grid.</summary>
        private static Vector3 AllPairs(Enemy self, Vector3 position, float radius, float strength)
        {
            radius = Mathf.Min(radius, EnemyCrowd.CellSize);
            Vector3 push = Vector3.zero;
            foreach (var other in Enemy.Active)
            {
                if (other == self) continue;
                Vector3 offset = position - other.transform.position;
                offset.y = 0f;
                float d = offset.magnitude;
                if (d >= radius || d < 1e-3f) continue;
                push += offset / d * ((radius - d) / radius * strength);
            }
            return push;
        }

        [Test]
        public void CrowdGrid_GivesTheSamePush_AsAllPairs()
        {
            // A tight knot straddling cell borders (multiples of 1.25 m) on both sides of 0, plus loners far away whose
            // hashed buckets may collide with the knot's cells.
            var positions = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(0.6f, 0f, 0.2f), new Vector3(-0.4f, 0f, 0.5f), new Vector3(1.2f, 0f, 1.3f),
                new Vector3(1.3f, 0f, 1.2f), new Vector3(-1.26f, 0f, -0.1f), new Vector3(-0.7f, 0f, -0.8f), new Vector3(2.4f, 0f, 2.6f),
                new Vector3(0.1f, 0f, -1.24f), new Vector3(0.05f, 0f, 0.02f),
                new Vector3(40f, 0f, -37.5f), new Vector3(-80f, 0f, 160f), new Vector3(320f, 0f, 2.5f),
            };
            var crowd = new List<Enemy>();
            foreach (var p in positions) crowd.Add(Dummy(p));
            EnemyCrowd.Invalidate();

            foreach (float radius in new[] { 0.5f, 0.9f, 1.25f, 2f })
            foreach (var e in crowd)
            {
                Vector3 pos = e.transform.position;
                Vector3 grid = EnemyCrowd.Separation(e, pos, radius, 3f);
                Vector3 pairs = AllPairs(e, pos, radius, 3f);
                Assert.That(Vector3.Distance(grid, pairs), Is.LessThan(1e-3f),
                    $"radius {radius}, enemy at {pos}: grid {grid} vs all pairs {pairs}");
            }
            Assert.That(EnemyCrowd.Count, Is.EqualTo(positions.Length), "one snapshot of every active enemy");

            // Sanity: the knot really pushes, the loners do not.
            Assert.That(EnemyCrowd.Separation(crowd[0], positions[0], 1f, 3f).sqrMagnitude, Is.GreaterThan(0.01f));
            Assert.That(EnemyCrowd.Separation(crowd[10], positions[10], 1f, 3f), Is.EqualTo(Vector3.zero));
            Assert.That(EnemyCrowd.Separation(crowd[0], positions[0], 0f, 3f), Is.EqualTo(Vector3.zero), "radius 0 = no push");
        }

        [Test]
        public void CrowdGrid_ManyEnemies_MatchesAllPairs_AndDoesNotAllocate()
        {
            Random.InitState(4430);
            var crowd = new List<Enemy>();
            for (int i = 0; i < 90; i++) crowd.Add(Dummy(new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f))));
            EnemyCrowd.Invalidate();
            EnemyCrowd.Separation(crowd[0], crowd[0].transform.position, 1f, 2f); // grows the arrays once

            foreach (var e in crowd)
            {
                Vector3 pos = e.transform.position;
                Assert.That(Vector3.Distance(EnemyCrowd.Separation(e, pos, 1f, 2f), AllPairs(e, pos, 1f, 2f)), Is.LessThan(1e-3f));
            }
            EnemyCrowd.Invalidate();
            Enemy self = crowd[5];
            Vector3 at = self.transform.position;
            Assert.That(new TestDelegate(() => EnemyCrowd.Separation(self, at, 1f, 2f)),
                Is.Not.AllocatingGCMemory(), "snapshot + query reuse the arrays");
        }

        // ------------------------------------------------------------ PrewarmFor

        [Test]
        public void PrewarmFor_WeightedShareOfTheBiggestCap_TimesOneAndAHalf_AtLeastTheFloor()
        {
            Enemy a = Dummy(Vector3.zero), b = Dummy(Vector3.zero), c = Dummy(Vector3.zero), unused = Dummy(Vector3.zero);
            Enemy.Active.Clear(); // only used as table entries
            var table = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            table.Configure(0f, 12, new[]
            {
                Phase(0f, 36, (a, 3f), (b, 1f)),   // a: 36 x 3/4 x 1.5 = 40.5 -> 41; b: 36 x 1/4 x 1.5 = 13.5 -> 14
                Phase(30f, 20, (a, 1f)),           // a alone: 20 x 1.5 = 30, but at most 1.25 x the cap = 25
                Phase(60f, 10, (a, 99f), (c, 1f)), // c: 10 x 0.01 x 1.5 = 0.15 -> 1, under the floor
            });

            Assert.That(table.PrewarmFor(a), Is.EqualTo(41), "biggest share over all phases (phase 1)");
            Assert.That(table.PrewarmFor(b), Is.EqualTo(14), "rounded up");
            Assert.That(table.PrewarmFor(c), Is.EqualTo(12), "rare type: the per-type floor");
            Assert.That(table.PrewarmFor(unused), Is.EqualTo(12), "a type in no phase still gets the floor");

            // One type filling a big phase: at most BodyHeadroom x the cap (bodies still falling are out of the pool too).
            var solo = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            solo.Configure(0f, 12, new[] { Phase(0f, 60, (a, 1f)) });
            Assert.That(solo.PrewarmFor(a), Is.EqualTo(Mathf.CeilToInt(60 * EnemyWaveTable.BodyHeadroom)));
            Assert.That(solo.PrewarmFor(a), Is.LessThan(90), "not the full 1.5 x");

            // The floor wins over a small share; the share wins over a small floor.
            var low = Track(ScriptableObject.CreateInstance<EnemyWaveTable>());
            low.Configure(0f, 4, new[] { Phase(0f, 36, (a, 3f), (b, 1f)) });
            Assert.That(low.PrewarmFor(b), Is.EqualTo(14));
            Assert.That(low.PrewarmFor(c), Is.EqualTo(4));
        }

        private static EnemyWaveTable.Phase Phase(float start, int cap, params (Enemy prefab, float weight)[] entries)
        {
            var list = new EnemyWaveTable.EnemyEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                list[i] = new EnemyWaveTable.EnemyEntry { prefab = entries[i].prefab, weight = entries[i].weight };
            return new EnemyWaveTable.Phase
            {
                startTime = start, maxAlive = cap, spawnInterval = 1f, groupSize = Vector2Int.one, enemies = list,
            };
        }

        // ------------------------------------------------------------ Soul wisp horde merge

        private SoulWispField WispField(int mergeAbove, float mergeRadius)
        {
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100000, 1f, 1);
            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false);
            var xp = xpGo.AddComponent<XpManager>();
            xp.Setup(curve);
            xpGo.SetActive(true);
            var field = Track(new GameObject("Souls")).AddComponent<SoulWispField>();
            field.Setup(xp, null, 128); // no collector: wisps land and rest, nothing is picked up
            field.SetMerge(mergeAbove, mergeRadius);
            return field;
        }

        private static void Run(SoulWispField field, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) field.Tick(Frame);
        }

        private static int XpOut(SoulWispField field)
        {
            int sum = 0;
            for (int i = 0; i < field.Count; i++) sum += field.XpAt(i);
            return sum;
        }

        /// <summary>Drops <paramref name="count"/> wisps on a 4 m grid (far from each other) and lets them land.</summary>
        private static void Scatter(SoulWispField field, int count, int xp)
        {
            for (int i = 0; i < count; i++) field.Spawn(GridSlot(i), xp);
            Run(field, 3f);
        }

        private static Vector3 GridSlot(int i) => new Vector3(-20f + 4f * (i % 10), 1f, 4f * (i / 10));

        [Test]
        public void WispMerge_AboveTheThreshold_DropNearARestingWisp_JoinsIt_NoXpLost()
        {
            var field = WispField(24, 1.6f);
            Scatter(field, 24, 3);
            Assert.That(field.Count, Is.EqualTo(24));
            Assert.That(field.MergedDrops, Is.EqualTo(0), "below the threshold nothing merges");

            const int target = 0;
            Vector3 rest = field.PositionAt(target);
            field.Spawn(rest + new Vector3(0.9f, 0.6f, 0.6f), 5); // ~1.08 m away: inside the 1.6 m merge radius
            Assert.That(field.Count, Is.EqualTo(24), "no new wisp");
            Assert.That(field.MergedDrops, Is.EqualTo(1));
            Assert.That(field.XpAt(target), Is.EqualTo(8), "the resting wisp carries both drops");
            Assert.That(XpOut(field), Is.EqualTo(24 * 3 + 5), "no XP lost");
            Assert.That(field.SpawnedXp, Is.EqualTo(24 * 3 + 5));

            // A big pile grows a tier.
            for (int i = 0; i < 30; i++) field.Spawn(rest + new Vector3(0.2f, 0.5f, 0f), 1);
            Assert.That(field.XpAt(target), Is.EqualTo(38));
            Assert.That(field.TierAt(target), Is.EqualTo(field.TierOf(38)));
            Assert.That(field.TierAt(target), Is.GreaterThan(field.TierOf(3)), "merged pile shows a bigger orb");
            Assert.That(field.Count, Is.EqualTo(24));
            Assert.That(XpOut(field), Is.EqualTo(field.SpawnedXp), "no XP lost");
        }

        [Test]
        public void WispMerge_FarDrop_OrBelowThreshold_OrOnlyPoppingWisps_MakesANewWisp()
        {
            // Far drop: more than 1.6 m from every resting wisp -> a new wisp.
            var field = WispField(24, 1.6f);
            Scatter(field, 24, 3);
            field.Spawn(new Vector3(-18f, 1f, 30f), 4);
            Assert.That(field.Count, Is.EqualTo(25));
            Assert.That(field.MergedDrops, Is.EqualTo(0));

            // Below the threshold: a drop right on a resting wisp still makes its own.
            var few = WispField(24, 1.6f);
            Scatter(few, 10, 3);
            few.Spawn(few.PositionAt(0) + Vector3.up * 0.6f, 3);
            Assert.That(few.Count, Is.EqualTo(11));
            Assert.That(few.MergedDrops, Is.EqualTo(0));

            // Above the threshold, but the near wisp is still popping (in the air): no merge into a moving wisp.
            var popping = WispField(24, 1.6f);
            for (int i = 0; i < 24; i++) popping.Spawn(GridSlot(i), 2);
            popping.Spawn(GridSlot(0), 2);
            Assert.That(popping.Count, Is.EqualTo(25));
            Assert.That(popping.MergedDrops, Is.EqualTo(0));

            // Merge off (0): never merges.
            var off = WispField(0, 1.6f);
            Scatter(off, 30, 3);
            off.Spawn(off.PositionAt(0) + Vector3.up * 0.6f, 3);
            Assert.That(off.Count, Is.EqualTo(31));
            Assert.That(XpOut(off), Is.EqualTo(off.SpawnedXp));
        }
    }
}
#endif
