using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Healing hearts of the boss fight (KAN-60 = KAN-22.3). Rules from <see cref="HeartRules"/>:
    /// <list type="bullet">
    /// <item><b>Timed drops</b>: from the moment the boss rises (<see cref="BossFight.Spawned"/>) until it dies, a clock runs
    /// while the Knight is hurt (stopped at full health) and drops a heart every <see cref="HeartRules.IntervalMin"/> ..
    /// <see cref="HeartRules.IntervalMax"/> s (random per drop). Under <see cref="HeartRules.LowHealthShare"/> it runs
    /// <see cref="HeartRules.LowHealthSpeed"/> times faster (low-HP help).</item>
    /// <item><b>Phase drop</b>: <see cref="DropForPhase"/> drops one heart at once, on the boss's
    /// <see cref="BossFight.PhaseChanged"/> (KAN-61, phase 2 at half health).</item>
    /// <item><b>Cap</b>: at most <see cref="HeartRules.MaxOnGround"/> hearts glow or float at once; a drop past the cap is
    /// skipped (the clock starts over). Unpicked hearts fade after <see cref="HeartRules.Lifetime"/>.</item>
    /// <item><b>Placement</b>: random walkable ground (<see cref="Enemy.Walkability"/>: inside the arena ring, off the
    /// pillars) between <see cref="HeartRules.MinPlayerDistance"/> and <see cref="HeartRules.MaxPlayerDistance"/> from the
    /// Knight, never within <see cref="HeartRules.BossClearance"/> of the boss and not on another heart. A ground glow shows
    /// for <see cref="HeartRules.AppearTime"/> before the heart appears.</item>
    /// <item><b>Pickup</b>: the living, hurt Knight walks within <see cref="HeartRules.PickupRadius"/> of a floating heart
    /// and heals <see cref="HeartRules.HealShare"/> of max health (<see cref="PickedUp"/>: sound, haptic). At full health
    /// the heart stays on the ground for later.</item>
    /// </list>
    /// Scaled time (pauses with the level-up cards). Hearts are pooled children of this object; one Update ticks them all.
    /// On the "Boss" object (BossBuilder) next to <see cref="BossFight"/>.
    /// </summary>
    public sealed class BossHearts : MonoBehaviour
    {
        public enum DropReason
        {
            Timed = 0,
            Phase = 1,
            Debug = 2,
        }

        private const int PlacementTries = 24;
        private const float HeartClearance = 0.6f;   // free ground a heart needs (walkability check)
        private const float HeartSpacing = 2f;       // min distance between two hearts

        [SerializeField] private HeartRules rules;
        [SerializeField] private HealingHeart heartPrefab;
        [SerializeField] private PlayerHealth player;
        [Tooltip("Timed drops run from the boss rising to its death.")]
        [SerializeField] private BossFight fight;
        [Tooltip("0 = random every run. Fixed seeds make drops repeatable (tests, bug repro).")]
        [SerializeField] private int seed;

        /// <summary>A heart started to appear (its ground glow shows).</summary>
        public event Action<HealingHeart, DropReason> Dropped;
        /// <summary>The Knight took a heart: health actually restored.</summary>
        public event Action<HealingHeart, float> PickedUp;
        /// <summary>A heart faded away unpicked.</summary>
        public event Action<HealingHeart> Expired;

        private readonly List<HealingHeart> _ground = new List<HealingHeart>(4);
        private readonly List<HealingHeart> _bursting = new List<HealingHeart>(2);
        private readonly List<HealingHeart> _free = new List<HealingHeart>(4);
        private System.Random _rng;
        private Transform _boss;
        private float _clock;
        private float _nextDelay;
        private Action<Enemy> _onSpawned;
        private Action _onDefeated;
        private Action<int> _onPhase;

        public HeartRules Rules => rules;
        /// <summary>Hearts glowing or floating (count against the cap).</summary>
        public IReadOnlyList<HealingHeart> OnGround => _ground;
        /// <summary>Hearts playing their healing burst.</summary>
        public int Bursting => _bursting.Count;
        public bool IsFull => rules != null && _ground.Count >= rules.MaxOnGround;
        /// <summary>Timed drops on (boss alive).</summary>
        public bool Running { get; private set; }
        /// <summary>Timed clock in seconds (it runs faster at low HP and stops at full HP).</summary>
        public float Clock => _clock;
        /// <summary>Clock value of the next timed drop.</summary>
        public float NextDelay => _nextDelay;
        public int DroppedCount { get; private set; }
        public int PickedUpCount { get; private set; }
        public int ExpiredCount { get; private set; }

        private void Awake()
        {
            _rng ??= NewRng();
            Prewarm();
        }

        /// <summary>Wiring for code-built objects and tests (no <paramref name="bossFight"/>: call <see cref="Begin"/> by hand).</summary>
        public void Setup(HeartRules heartRules, HealingHeart prefab, PlayerHealth knight, BossFight bossFight = null, int rngSeed = 0)
        {
            if (isActiveAndEnabled) Unsubscribe();
            rules = heartRules;
            heartPrefab = prefab;
            player = knight;
            fight = bossFight;
            seed = rngSeed;
            _rng = NewRng();
            if (isActiveAndEnabled) Subscribe();
            Prewarm();
        }

        private System.Random NewRng() => seed != 0 ? new System.Random(seed) : new System.Random(Environment.TickCount);

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (fight == null) return;
            _onSpawned ??= body => Begin(body != null ? body.transform : null);
            _onDefeated ??= Stop;
            _onPhase ??= phase => DropForPhase();
            fight.Spawned += _onSpawned;
            fight.Defeated += _onDefeated;
            fight.PhaseChanged += _onPhase;
        }

        private void Unsubscribe()
        {
            if (fight == null || _onSpawned == null) return;
            fight.Spawned -= _onSpawned;
            fight.Defeated -= _onDefeated;
            fight.PhaseChanged -= _onPhase;
        }

        /// <summary>The boss stands: timed drops start (clock from zero). <paramref name="boss"/> keeps hearts away from it.</summary>
        public void Begin(Transform boss)
        {
            if (rules == null) return;
            _boss = boss;
            Running = true;
            _clock = 0f;
            _nextDelay = RollDelay();
        }

        /// <summary>The boss died: no more timed drops. Hearts already out stay until they are taken or fade.</summary>
        public void Stop() => Running = false;

        /// <summary>Boss phase change (<see cref="BossFight.PhaseChanged"/>, KAN-61): one heart at once, if the rules allow it and the cap has room.</summary>
        public HealingHeart DropForPhase() => rules != null && rules.DropOnPhaseChange ? TryDrop(DropReason.Phase) : null;

        /// <summary>Debug menu and dev screenshots: a heart now, at <paramref name="at"/> or by the placement rules.</summary>
        public HealingHeart DropNow(Vector3? at = null) => TryDrop(DropReason.Debug, at);

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f || rules == null) return;
            bool alive = player != null && !player.IsDead;
            if (Running && alive)
            {
                _clock += dt * rules.ClockSpeed(player.Normalized);
                // Past the cap the drop is skipped; with no free ground it retries next frame (clock kept).
                if (_clock >= _nextDelay && (IsFull || TryDrop(DropReason.Timed) != null))
                {
                    _clock = 0f;
                    _nextDelay = RollDelay();
                }
            }

            for (int i = _ground.Count - 1; i >= 0; i--)
            {
                var heart = _ground[i];
                if (!heart.Tick(dt)) continue;
                _ground.RemoveAt(i);
                ExpiredCount++;
                Expired?.Invoke(heart);
                Release(heart);
            }

            if (alive && player.Current < player.Max) PickUp(player.transform.position);

            for (int i = _bursting.Count - 1; i >= 0; i--)
            {
                var heart = _bursting[i];
                if (!heart.Tick(dt)) continue;
                _bursting.RemoveAt(i);
                Release(heart);
            }
        }

        private void PickUp(Vector3 at)
        {
            float r2 = rules.PickupRadius * rules.PickupRadius;
            for (int i = _ground.Count - 1; i >= 0; i--)
            {
                var heart = _ground[i];
                if (!heart.CanPickUp) continue;
                var d = heart.transform.position - at;
                if (d.x * d.x + d.z * d.z > r2 || !heart.BeginCollect()) continue;
                _ground.RemoveAt(i);
                _bursting.Add(heart);
                float healed = player.Heal(rules.HealFor(player.Max));
                PickedUpCount++;
                PickedUp?.Invoke(heart, healed);
                if (player.Current >= player.Max) break; // full: the next heart waits
            }
        }

        private HealingHeart TryDrop(DropReason reason, Vector3? at = null)
        {
            if (rules == null || heartPrefab == null || IsFull) return null;
            Vector3 p;
            if (at.HasValue) p = at.Value;
            else if (!FindDropPoint(out p)) return null;
            var heart = Take();
            heart.Spawn(p, rules.AppearTime, rules.Lifetime, rules.WarningTime);
            _ground.Add(heart);
            DroppedCount++;
            Dropped?.Invoke(heart, reason);
            return heart;
        }

        /// <summary>
        /// Random ground point by the placement rules (see the class summary). False when none of the tries fits (the
        /// Knight cornered against the wall by the boss, for example).
        /// </summary>
        public bool FindDropPoint(out Vector3 point)
        {
            point = default;
            if (player == null || rules == null) return false;
            Vector3 from = player.transform.position;
            var walk = Enemy.Walkability;
            float minD = rules.MinPlayerDistance, maxD = rules.MaxPlayerDistance;
            float boss2 = rules.BossClearance * rules.BossClearance;
            for (int i = 0; i < PlacementTries; i++)
            {
                float a = (float)(_rng.NextDouble() * Math.PI * 2.0);
                float d = Mathf.Lerp(minD, maxD, (float)_rng.NextDouble());
                var p = new Vector3(from.x + Mathf.Cos(a) * d, from.y, from.z + Mathf.Sin(a) * d);
                if (!float.IsInfinity(Enemy.LandMaxZ) && p.z > Enemy.LandMaxZ - HeartClearance) continue; // not in the sea
                if (walk != null && walk.IsBlocked(p, HeartClearance)) continue;
                if (_boss != null && _boss.gameObject.activeInHierarchy && PlanarSqr(p, _boss.position) < boss2) continue;
                if (NearOtherHeart(p)) continue;
                point = p;
                return true;
            }
            return false;
        }

        private bool NearOtherHeart(Vector3 p)
        {
            for (int i = 0; i < _ground.Count; i++)
                if (PlanarSqr(p, _ground[i].transform.position) < HeartSpacing * HeartSpacing) return true;
            return false;
        }

        private float RollDelay() => Mathf.Lerp(rules.IntervalMin, rules.IntervalMax, (float)_rng.NextDouble());

        private static float PlanarSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private void Prewarm()
        {
            if (heartPrefab == null || rules == null) return;
            while (_free.Count + _ground.Count + _bursting.Count < rules.MaxOnGround + 1) _free.Add(Create());
        }

        private HealingHeart Take()
        {
            if (_free.Count == 0) return Create();
            int last = _free.Count - 1;
            var heart = _free[last];
            _free.RemoveAt(last);
            return heart;
        }

        private HealingHeart Create()
        {
            var heart = Instantiate(heartPrefab, transform);
            heart.name = heartPrefab.name;
            heart.gameObject.SetActive(false);
            return heart;
        }

        private void Release(HealingHeart heart)
        {
            heart.Despawn();
            _free.Add(heart);
        }
    }
}
