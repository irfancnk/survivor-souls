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

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-60 (KAN-22.3) healing hearts, drop rules: no timed drop at full health; a hurt Knight gets one per interval, twice
    /// as fast under 30 %; a phase change drops one at once; never more than the cap (a drop past it is skipped); the
    /// ground glow comes first (no pickup), the heart fades after its lifetime and blinks before; pickup heals 25 % of max
    /// once, never at full health or when dead; placement keeps the distance band to the Knight, clears the boss and
    /// stays on walkable ground; Stop ends timed drops; no GC allocation per frame. The built Game scene wires the hearts
    /// to the boss fight. Look and feel: screenshots and a phone.
    /// </summary>
    public sealed class BossHeartsTests
    {
        private const float Frame = 1f / 60f;
        private const int TestRealm = 97; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _player;
        private HealingHeart _prefab;
        private BossHearts _hearts;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;
        private readonly List<BossHearts.DropReason> _drops = new List<BossHearts.DropReason>();
        private readonly List<float> _heals = new List<float>();
        private int _expired;

        /// <summary>Round floor of radius 16 at the origin with one pillar (r 1) at (0, 6), like the arena.</summary>
        private sealed class Ring : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r)
            {
                if (p.x * p.x + p.z * p.z > (16f - r) * (16f - r)) return true;
                float dz = p.z - 6f;
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

            _player = Track(new GameObject("Player")).AddComponent<PlayerHealth>(); // max 400

            var template = Track(new GameObject("HeartTemplate"));
            _prefab = template.AddComponent<HealingHeart>(); // no renderers: logic only
            template.SetActive(false);

            _hearts = NewHearts(Track(HeartRules.Create(intervalMin: 20f, intervalMax: 20f)));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _drops.Clear();
            _heals.Clear();
            _expired = 0;
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

        private BossHearts NewHearts(HeartRules rules, int seed = 6060)
        {
            var hearts = Track(new GameObject("Hearts")).AddComponent<BossHearts>();
            hearts.Setup(rules, _prefab, _player, null, seed);
            hearts.Dropped += (h, reason) => _drops.Add(reason);
            hearts.PickedUp += (h, healed) => _heals.Add(healed);
            hearts.Expired += h => _expired++;
            return hearts;
        }

        private void Run(BossHearts hearts, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) hearts.Tick(Frame);
        }

        private void HurtTo(float share) => _player.TakeDamage(_player.Current - share * _player.Max);

        // ------------------------------------------------------------ Timed drops

        [Test]
        public void FullHealth_NoTimedDrop()
        {
            _hearts.Begin(null);
            Run(_hearts, 60f);
            Assert.That(_drops, Is.Empty);
            Assert.That(_hearts.Clock, Is.EqualTo(0f), "clock stopped at full health");
        }

        [Test]
        public void Hurt_DropsOnePerInterval()
        {
            HurtTo(0.7f);
            _hearts.Begin(null);
            Run(_hearts, 19.8f);
            Assert.That(_drops, Is.Empty, "not before the interval");
            Run(_hearts, 0.4f);
            Assert.That(_drops, Is.EqualTo(new[] { BossHearts.DropReason.Timed }));
            Assert.That(_hearts.Clock, Is.LessThan(0.5f), "clock starts over");
        }

        [Test]
        public void LowHealth_HalvesTheDelay()
        {
            HurtTo(0.2f);
            _hearts.Begin(null);
            Run(_hearts, 9.8f);
            Assert.That(_drops, Is.Empty);
            Run(_hearts, 0.4f);
            Assert.That(_drops.Count, Is.EqualTo(1), "under 30 % the clock runs twice as fast");
        }

        [Test]
        public void Interval_RandomInsideTheRange()
        {
            var hearts = NewHearts(Track(HeartRules.Create(intervalMin: 20f, intervalMax: 25f)), seed: 77);
            for (int i = 0; i < 20; i++)
            {
                hearts.Begin(null);
                Assert.That(hearts.NextDelay, Is.InRange(20f, 25f));
            }
        }

        [Test]
        public void Stop_EndsTimedDrops_HeartsStay()
        {
            HurtTo(0.7f);
            _hearts.Begin(null);
            Run(_hearts, 20.2f);
            Assert.That(_drops.Count, Is.EqualTo(1));
            _hearts.Stop();
            Run(_hearts, 5f);
            Assert.That(_hearts.OnGround.Count, Is.EqualTo(1), "the heart out stays after the boss dies");
            Run(_hearts, 40f);
            Assert.That(_drops.Count, Is.EqualTo(1), "no timed drops without a boss");
        }

        [Test]
        public void DeadKnight_NoClock_NoPickup()
        {
            HurtTo(0.5f);
            _hearts.Begin(null);
            var heart = _hearts.DropNow(_player.transform.position);
            _player.TakeDamage(9999f);
            Run(_hearts, 30f);
            Assert.That(_hearts.Clock, Is.EqualTo(0f));
            Assert.That(_heals, Is.Empty);
            Assert.That(heart.Current, Is.EqualTo(HealingHeart.State.Off), "faded, not taken");
        }

        // ------------------------------------------------------------ Phase drop and cap

        [Test]
        public void PhaseChange_DropsOneAtOnce()
        {
            var heart = _hearts.DropForPhase();
            Assert.That(heart, Is.Not.Null);
            Assert.That(_drops, Is.EqualTo(new[] { BossHearts.DropReason.Phase }));

            var off = NewHearts(Track(HeartRules.Create(dropOnPhaseChange: false)));
            Assert.That(off.DropForPhase(), Is.Null, "rules can turn it off");
        }

        [Test]
        public void PhaseDrops_NeverMoreThanTheCap()
        {
            Assert.That(_hearts.DropForPhase(), Is.Not.Null);
            Assert.That(_hearts.DropForPhase(), Is.Not.Null);
            Assert.That(_hearts.DropForPhase(), Is.Null, "cap 2");
            Assert.That(_hearts.OnGround.Count, Is.EqualTo(2));
            Assert.That(_drops.Count, Is.EqualTo(2));
        }

        [Test]
        public void Cap_TimedDropWhileFull_IsSkipped_ClockStartsOver()
        {
            var rules = Track(HeartRules.Create(intervalMin: 5f, intervalMax: 5f, lifetime: 60f));
            var hearts = NewHearts(rules);
            HurtTo(0.7f);
            hearts.Begin(null);
            hearts.DropForPhase();
            hearts.DropForPhase();
            Run(hearts, 5.2f);
            Assert.That(hearts.OnGround.Count, Is.EqualTo(2));
            Assert.That(_drops.Count, Is.EqualTo(2), "drop past the cap skipped");
            Assert.That(hearts.Clock, Is.LessThan(0.5f), "clock starts over");
        }

        // ------------------------------------------------------------ Life of a heart

        [Test]
        public void Heart_GlowsFirst_ThenFloats_BlinksAndFades()
        {
            var heart = _hearts.DropForPhase();
            Assert.That(heart.Current, Is.EqualTo(HealingHeart.State.Appearing));
            Assert.That(heart.CanPickUp, Is.False, "ground glow only");
            Run(_hearts, 1.05f);
            Assert.That(heart.Current, Is.EqualTo(HealingHeart.State.Floating));
            Assert.That(heart.CanPickUp, Is.True);
            Run(_hearts, 11f);
            Assert.That(heart.IsWarning, Is.False);
            Run(_hearts, 1.5f);
            Assert.That(heart.IsWarning, Is.True, "blinks in the last 3 s");
            Run(_hearts, 2.6f);
            Assert.That(_expired, Is.EqualTo(1), "gone 15 s after it appeared");
            Assert.That(_hearts.OnGround.Count, Is.EqualTo(0));
            Assert.That(heart.Current, Is.EqualTo(HealingHeart.State.Off));
        }

        [Test]
        public void Pickup_HealsQuarter_Once_AfterTheGlow()
        {
            HurtTo(0.5f); // 200 / 400
            var heart = _hearts.DropNow(_player.transform.position + new Vector3(0.5f, 0f, 0f));
            Run(_hearts, 0.5f);
            Assert.That(_heals, Is.Empty, "no pickup while it only glows");
            Run(_hearts, 0.6f);
            Assert.That(_heals, Is.EqualTo(new[] { 100f }), "25 % of 400");
            Assert.That(_player.Current, Is.EqualTo(300f));
            Assert.That(heart.Current, Is.EqualTo(HealingHeart.State.Collected), "healing burst");
            Assert.That(_hearts.OnGround.Count, Is.EqualTo(0), "frees a cap slot at once");
            Run(_hearts, 1f);
            Assert.That(_heals.Count, Is.EqualTo(1));
            Assert.That(heart.Current, Is.EqualTo(HealingHeart.State.Off));
            Assert.That(_hearts.Bursting, Is.EqualTo(0));
        }

        [Test]
        public void Pickup_ClampsAtMax_SecondHeartWaits()
        {
            HurtTo(0.9f); // 360: one heart fills it, the second must stay
            var at = _player.transform.position;
            _hearts.DropNow(at + new Vector3(0.3f, 0f, 0f));
            _hearts.DropNow(at + new Vector3(-0.3f, 0f, 0f));
            Run(_hearts, 1.2f);
            Assert.That(_heals, Is.EqualTo(new[] { 40f }), "only what was missing");
            Assert.That(_hearts.OnGround.Count, Is.EqualTo(1), "the other heart waits for later");
        }

        [Test]
        public void FullHealth_WalkingOverAHeart_LeavesIt()
        {
            _hearts.DropNow(_player.transform.position);
            Run(_hearts, 3f);
            Assert.That(_heals, Is.Empty);
            Assert.That(_hearts.OnGround.Count, Is.EqualTo(1));
        }

        // ------------------------------------------------------------ Placement

        [Test]
        public void Placement_InTheBand_AwayFromBoss_OnWalkableGround()
        {
            var ring = new Ring();
            Enemy.Walkability = ring;
            var boss = Track(new GameObject("Boss")).transform;
            boss.position = new Vector3(0f, 0f, -4f); // 6 m from the Knight: its clearance cuts into the band
            _player.transform.position = new Vector3(0f, 0f, -10f); // near the south wall, like the arena start
            _hearts.Begin(boss);
            var rules = _hearts.Rules;
            int found = 0;
            for (int i = 0; i < 200; i++)
            {
                if (!_hearts.FindDropPoint(out var p)) continue;
                found++;
                float toPlayer = Planar(p, _player.transform.position);
                Assert.That(toPlayer, Is.InRange(rules.MinPlayerDistance - 0.01f, rules.MaxPlayerDistance + 0.01f));
                Assert.That(Planar(p, boss.position), Is.GreaterThanOrEqualTo(rules.BossClearance));
                Assert.That(ring.IsBlocked(p, 0.5f), Is.False, $"{p} inside the ring, off the pillar");
            }
            Assert.That(found, Is.GreaterThan(190), "almost always finds ground");
        }

        [Test]
        public void Placement_NotOnAnotherHeart()
        {
            var first = _hearts.DropForPhase();
            var second = _hearts.DropForPhase();
            Assert.That(Planar(first.transform.position, second.transform.position), Is.GreaterThanOrEqualTo(2f));
        }

        [Test]
        public void Placement_NoGround_NoDrop_ClockKept()
        {
            Enemy.Walkability = new BlockedEverywhere();
            HurtTo(0.7f);
            _hearts.Begin(null);
            Run(_hearts, 21f);
            Assert.That(_drops, Is.Empty);
            Assert.That(_hearts.Clock, Is.GreaterThanOrEqualTo(_hearts.NextDelay), "retries every frame");
            Enemy.Walkability = null;
            _hearts.Tick(Frame);
            Assert.That(_drops.Count, Is.EqualTo(1), "drops as soon as ground is free");
        }

        private sealed class BlockedEverywhere : IWalkability
        {
            public bool IsBlocked(Vector3 p, float r) => true;
        }

        private static float Planar(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ------------------------------------------------------------ Performance

        [Test]
        public void Tick_NoGcAlloc()
        {
            HurtTo(0.5f);
            _hearts.Begin(null);
            _hearts.DropForPhase();
            _hearts.DropNow(_player.transform.position + Vector3.right * 6f);
            _hearts.Tick(Frame); // warm-up
            Assert.That(() => { for (int i = 0; i < 120; i++) _hearts.Tick(Frame); }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Real scene

        /// <summary>The built Game scene: boss Chamber 5 has hearts wired to the fight; they run once the boss rises and land in the ring.</summary>
        [UnityTest]
        public IEnumerator GameScene_HeartsFollowTheBossFight()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 9001);
            RunState.JumpToChamber(5);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var fight = BossFight.Active;
            Assert.That(fight, Is.Not.Null);
            var hearts = fight.GetComponent<BossHearts>();
            Assert.That(hearts, Is.Not.Null, "hearts on the Boss object");
            Assert.That(hearts.Rules, Is.Not.Null);
            Assert.That(hearts.Running, Is.False, "not before the boss rises");

            float wait = fight.Data.IntroDelay + fight.Data.SpawnWarning + 4f; // + the Chamber fade-in pause
            while (fight.Current != BossFight.Stage.Fighting && wait > 0f)
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Fighting));
            Assert.That(hearts.Running, Is.True, "timed drops start with the boss");

            var heart = hearts.DropForPhase();
            Assert.That(heart, Is.Not.Null, "found ground in the arena");
            Assert.That(BossArena.Active.IsBlocked(heart.transform.position, 0.5f), Is.False, "lands inside the ring");
            Assert.That(Planar(heart.transform.position, fight.Boss.transform.position),
                Is.GreaterThanOrEqualTo(hearts.Rules.BossClearance), "not under the boss");

            fight.Boss.TakeDamage(fight.Boss.Health + 1f, Vector3.zero);
            Assert.That(fight.Current, Is.EqualTo(BossFight.Stage.Defeated));
            Assert.That(hearts.Running, Is.False, "no timed drops after the kill");

            var empty = SceneManager.CreateScene("BossHeartsEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
