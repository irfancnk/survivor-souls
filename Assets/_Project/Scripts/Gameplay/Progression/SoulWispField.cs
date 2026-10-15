using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Gameplay.Progression
{
    /// <summary>
    /// Soul wisps, the XP pickups (KAN-21). Every enemy kill (<see cref="Enemy.Damaged"/> with the kill flag) pops one
    /// wisp holding the enemy's <see cref="Enemy.XpValue"/>: it jumps up out of the body, lands nearby and rests with a
    /// slow bob and pulse. When the player's <see cref="SoulCollector"/> comes within its pickup radius the wisp flies to
    /// the Knight's chest with an ease-in acceleration and is absorbed: XP goes to <see cref="XpManager"/>, a small
    /// flash, a light haptic tap (rate limited), and <see cref="Collected"/> (sound, HUD). Wisps left alone for
    /// <see cref="driftAfter"/> seconds drift to the player, so XP is never lost.
    /// <para>
    /// Look (KAN-34): an opaque, shiny gold orb (the KAN-21 pale see-through billboard washed out on the snow). The body
    /// is a low-poly sphere drawn as mesh particles with the <c>SurvivorSouls/XpOrb</c> shader (gold, hotspot, rim,
    /// moving shine sweep, thin dark silhouette edge for white snow); a soft additive halo sits behind it; resting orbs
    /// flash a small white sparkle every 1-2 s.
    /// </para>
    /// <para>
    /// Performance: no GameObject per wisp. All wisps are structs in one fixed array (swap-remove, capacity set at load),
    /// drawn by ONE body particle system (opaque mesh particles, <see cref="ParticleSystem.SetParticles(ParticleSystem.Particle[], int)"/>
    /// each frame), ONE halo particle system (additive billboards, same call) and ONE additive mote system for trails,
    /// sparkles and absorb flashes (halos and motes share one material). Three draw calls for any number of wisps, no
    /// lights, no shadows, no allocations after Awake. When the array is full a new drop merges its XP into the oldest
    /// wisp (it grows to the matching tier) instead of being dropped.
    /// </para>
    /// Size and colour come from <see cref="tiers"/> by XP value: small (normal enemy), elite, boss orb; all gold, bigger
    /// halo for more XP.
    /// </summary>
    [DefaultExecutionOrder(100)] // after PlayerMover: wisps chase this frame's player position
    public sealed class SoulWispField : MonoBehaviour
    {
        [Serializable]
        public struct Tier
        {
            [Tooltip("Smallest XP value drawn with this tier. Keep tiers sorted by this, ascending.")]
            public int minXp;
            [Tooltip("Orb diameter (m).")]
            public float size;
            [Tooltip("Gold tint of the orb body (alpha unused: the shader gets the shine phase there).")]
            public Color body;
            [Tooltip("Colour of the trailing motes and the absorb flash.")]
            public Color mote;
            [Tooltip("Additive halo behind the orb (alpha = strength).")]
            public Color halo;
            [Tooltip("Halo diameter as a multiple of the orb diameter.")]
            public float haloScale;
        }

        private enum State : byte { Pop, Rest, Drift, Magnet }

        private struct Wisp
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public int Xp;
            public int Tier;
            public State State;
            public float Age;
            public float StateTime;
            public float Speed;
            public float Phase;
            public float MoteDebt;
            public float SparkleIn;
        }

        [SerializeField] private XpManager xp;
        [SerializeField] private SoulCollector collector;
        [Tooltip("Orb bodies (opaque mesh particles): emission off, driven by SetParticles. Its maxParticles must be >= capacity.")]
        [SerializeField] private ParticleSystem bodies;
        [Tooltip("Halos behind the orbs (additive billboards): emission off, driven by SetParticles. Optional.")]
        [SerializeField] private ParticleSystem halos;
        [Tooltip("Trail motes and absorb flashes: emission off, fed by Emit.")]
        [SerializeField] private ParticleSystem motes;
        [Tooltip("Wisps alive at once. Further drops merge into the oldest wisp.")]
        [SerializeField, Min(1)] private int capacity = 128;
        [Tooltip("Horde clutter (KAN-30): with at least this many wisps out, a drop within mergeRadius of a resting wisp " +
                 "adds its XP to that wisp instead of making a new one (the pile grows a tier). 0 = never.")]
        [SerializeField, Min(0)] private int mergeAbove;
        [SerializeField, Min(0f)] private float mergeRadius = 1.6f;
        [SerializeField] private Tier[] tiers =
        {
            // Warm gold #FFC93C; the elite pile is a paler, brighter gold, the boss orb a deep gold with the biggest halo.
            new Tier { minXp = 0, size = 0.46f, body = new Color(1f, 0.79f, 0.24f, 1f), mote = new Color(1f, 0.84f, 0.4f, 0.85f),
                halo = new Color(1f, 0.72f, 0.22f, 0.3f), haloScale = 2.3f },
            new Tier { minXp = 25, size = 0.6f, body = new Color(1f, 0.86f, 0.42f, 1f), mote = new Color(1f, 0.9f, 0.52f, 0.9f),
                halo = new Color(1f, 0.78f, 0.32f, 0.4f), haloScale = 2.6f },
            new Tier { minXp = 100, size = 0.85f, body = new Color(1f, 0.7f, 0.14f, 1f), mote = new Color(1f, 0.78f, 0.3f, 0.95f),
                halo = new Color(1f, 0.62f, 0.14f, 0.55f), haloScale = 3f },
        };

        [Header("Pop (drop from the body)")]
        [Tooltip("Spawn height above the dead enemy's feet (m).")]
        [SerializeField, Min(0f)] private float spawnHeight = 1f;
        [SerializeField] private Vector2 popUpSpeed = new Vector2(3.6f, 4.8f);
        [SerializeField] private Vector2 popOutSpeed = new Vector2(0.8f, 2.2f);
        [SerializeField, Min(0.1f)] private float gravity = 16f;
        [Tooltip("Hover height of a resting wisp (m).")]
        [SerializeField, Min(0f)] private float restHeight = 0.45f;
        [Tooltip("Seconds to grow from a small spark to full size after the drop.")]
        [SerializeField, Min(0.01f)] private float growTime = 0.15f;

        [Header("Rest")]
        [SerializeField, Min(0f)] private float bobHeight = 0.09f;
        [SerializeField, Min(0f)] private float bobHz = 0.7f;
        [Tooltip("Size pulse of the halo (the opaque orb keeps a calmer share of it).")]
        [SerializeField, Range(0f, 0.5f)] private float pulseAmount = 0.12f;
        [SerializeField, Min(0f)] private float pulseHz = 1.6f;
        [Tooltip("Seconds between two sparkle flashes of a resting orb (random in this range).")]
        [SerializeField] private Vector2 sparkleInterval = new Vector2(1f, 2f);
        [Tooltip("Sparkle flash diameter as a multiple of the orb diameter.")]
        [SerializeField, Min(0f)] private float sparkleSize = 0.9f;

        [Header("Magnet (inside the pickup radius)")]
        [Tooltip("A popping wisp can be grabbed only after this many seconds (the drop reads first).")]
        [SerializeField, Min(0f)] private float magnetDelay = 0.3f;
        [Tooltip("Upward speed (m/s) when grabbed: a small lift before it flies in.")]
        [SerializeField, Min(0f)] private float magnetLift = 1.6f;
        [Tooltip("Flight acceleration (m/s^2): starts slow, ends fast (ease-in).")]
        [SerializeField, Min(0.1f)] private float magnetAccel = 40f;
        [SerializeField, Min(0.1f)] private float magnetMaxSpeed = 22f;
        [Tooltip("How fast the velocity turns to the target (m/s^2).")]
        [SerializeField, Min(0.1f)] private float magnetSteer = 60f;
        [Tooltip("Distance (m) to the absorb point that counts as collected.")]
        [SerializeField, Min(0.01f)] private float absorbDistance = 0.35f;
        [Tooltip("Safety: a flying wisp is collected after this many seconds even if it has not arrived.")]
        [SerializeField, Min(0.1f)] private float magnetTimeout = 1.5f;

        [Header("Drift (no XP is lost)")]
        [SerializeField] private bool driftEnabled = true;
        [Tooltip("Seconds a wisp rests before it starts drifting to the player.")]
        [SerializeField, Min(0f)] private float driftAfter = 10f;
        [SerializeField, Min(0.01f)] private float driftAccel = 2.5f;
        [Tooltip("Above the player's run speed (5 m/s), so a drifting wisp always catches up.")]
        [SerializeField, Min(0.1f)] private float driftMaxSpeed = 8f;

        [Header("Feedback")]
        [SerializeField, Min(0f)] private float moteRateResting = 2.5f;
        [SerializeField, Min(0f)] private float moteRateFlying = 30f;
        [SerializeField] private Vector2 moteLifetime = new Vector2(0.4f, 0.7f);
        [SerializeField] private Vector2 moteSize = new Vector2(0.04f, 0.08f);
        [Tooltip("Upper bound of motes emitted in one frame for all wisps (no spike with 50+ wisps).")]
        [SerializeField, Min(1)] private int maxMotesPerFrame = 24;
        [SerializeField, Min(0)] private int absorbSparks = 8;
        [Tooltip("Minimum seconds (unscaled) between two pickup haptic taps: a stream of wisps is a soft patter, not a buzz.")]
        [SerializeField, Min(0f)] private float hapticInterval = 0.12f;

        /// <summary>Raised per absorbed wisp: absorb point (world), XP value.</summary>
        public event Action<Vector3, int> Collected;

        private Wisp[] _wisps;
        private ParticleSystem.Particle[] _particles;
        private ParticleSystem.Particle[] _haloParticles;
        private ParticleSystem.EmitParams _emit;
        private int _count;
        private int _moteCursor;
        private float _lastHaptic = float.NegativeInfinity;

        public int Count => _count;
        public int Capacity => capacity;
        /// <summary>XP dropped this run (merged drops included).</summary>
        public int SpawnedXp { get; private set; }
        /// <summary>XP absorbed by the player this run.</summary>
        public int CollectedXp { get; private set; }
        public bool DriftEnabled { get => driftEnabled; set => driftEnabled = value; }
        public float DriftAfter => driftAfter;

        private void Awake() => Allocate();

        private void Allocate()
        {
            if (_wisps != null && _wisps.Length == capacity) return;
            _wisps = new Wisp[capacity];
            _particles = new ParticleSystem.Particle[capacity];
            _haloParticles = new ParticleSystem.Particle[capacity];
            _count = 0;
        }

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(XpManager xpManager, SoulCollector soulCollector, int wispCapacity = 128,
            ParticleSystem bodySystem = null, ParticleSystem moteSystem = null, ParticleSystem haloSystem = null)
        {
            xp = xpManager;
            collector = soulCollector;
            capacity = Mathf.Max(1, wispCapacity);
            bodies = bodySystem;
            motes = moteSystem;
            halos = haloSystem;
            Allocate();
        }

        private void OnEnable()
        {
            Enemy.Damaged += OnEnemyDamaged;
            if (bodies != null) bodies.Play();
            if (halos != null) halos.Play();
        }

        private void OnDisable() => Enemy.Damaged -= OnEnemyDamaged;

        private void OnEnemyDamaged(Enemy enemy, float deducted, bool killed)
        {
            if (killed && enemy.XpValue > 0) Spawn(enemy.transform.position + Vector3.up * spawnHeight, enemy.XpValue);
        }

        /// <summary>Drops a wisp worth <paramref name="xpValue"/> at <paramref name="position"/> (it pops up and lands).</summary>
        public void Spawn(Vector3 position, int xpValue)
        {
            if (xpValue <= 0) return;
            Allocate();
            SpawnedXp += xpValue;
            if (_count >= _wisps.Length)
            {
                MergeIntoOldest(xpValue);
                return;
            }
            if (mergeAbove > 0 && _count >= mergeAbove && MergeIntoNearby(position, xpValue)) return;
            float angle = Random.value * Mathf.PI * 2f;
            float outSpeed = Random.Range(popOutSpeed.x, popOutSpeed.y);
            _wisps[_count++] = new Wisp
            {
                Position = position,
                Velocity = new Vector3(Mathf.Cos(angle) * outSpeed, Random.Range(popUpSpeed.x, popUpSpeed.y), Mathf.Sin(angle) * outSpeed),
                Xp = xpValue,
                Tier = TierOf(xpValue),
                State = State.Pop,
                Phase = Random.value * 10f,
                SparkleIn = Random.Range(sparkleInterval.x, sparkleInterval.y),
            };
        }

        /// <summary>Index into <see cref="tiers"/> for an XP value (highest tier whose minXp it reaches).</summary>
        public int TierOf(int xpValue)
        {
            int tier = 0;
            for (int i = 0; i < tiers.Length; i++)
                if (xpValue >= tiers[i].minXp) tier = i;
            return tier;
        }

        internal int TierAt(int index) => _wisps[index].Tier;
        internal int XpAt(int index) => _wisps[index].Xp;
        internal Vector3 PositionAt(int index) => _wisps[index].Position;
        internal Tier TierStyle(int tier) => tiers[tier];
        internal int TierCount => tiers.Length;

        /// <summary>Adds the XP to the nearest resting wisp within <see cref="mergeRadius"/> (planar). False when there is none.</summary>
        private bool MergeIntoNearby(Vector3 position, int xpValue)
        {
            int best = -1;
            float bestSq = mergeRadius * mergeRadius;
            for (int i = 0; i < _count; i++)
            {
                if (_wisps[i].State != State.Rest) continue;
                float dx = _wisps[i].Position.x - position.x, dz = _wisps[i].Position.z - position.z;
                float sq = dx * dx + dz * dz;
                if (sq > bestSq) continue;
                bestSq = sq;
                best = i;
            }
            if (best < 0) return false;
            ref var w = ref _wisps[best];
            w.Xp += xpValue;
            w.Tier = TierOf(w.Xp);
            MergedDrops++;
            return true;
        }

        /// <summary>Drops folded into a nearby resting wisp (horde clutter control). For tests and stats.</summary>
        public int MergedDrops { get; private set; }

        /// <summary>Tests and builders: horde merge settings.</summary>
        public void SetMerge(int above, float radius)
        {
            mergeAbove = Mathf.Max(0, above);
            mergeRadius = Mathf.Max(0f, radius);
        }

        private void MergeIntoOldest(int xpValue)
        {
            int oldest = 0;
            float age = -1f;
            for (int i = 0; i < _count; i++)
            {
                if (_wisps[i].State == State.Magnet || _wisps[i].Age <= age) continue;
                age = _wisps[i].Age;
                oldest = i;
            }
            ref var w = ref _wisps[oldest];
            w.Xp += xpValue;
            w.Tier = TierOf(w.Xp);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One simulation + render step. Public for tests (Update calls it with the scaled frame time).</summary>
        public void Tick(float dt)
        {
            if (_wisps == null) return;
            bool canCollect = collector != null && collector.isActiveAndEnabled;
            Vector3 feet = canCollect ? collector.transform.position : Vector3.zero;
            Vector3 target = canCollect ? collector.AbsorbPoint : Vector3.zero;
            float radius = canCollect ? collector.PickupRadius : 0f;
            float radiusSqr = radius * radius;

            for (int i = 0; i < _count;)
            {
                if (Step(ref _wisps[i], dt, canCollect, feet, target, radiusSqr))
                {
                    Collect(i, target);
                    continue; // swap-remove moved the last wisp into slot i
                }
                i++;
            }
            EmitMotes(dt);
            Render(target);
        }

        /// <summary>Advances one wisp. Returns true when it reached the player this step.</summary>
        private bool Step(ref Wisp w, float dt, bool canCollect, Vector3 feet, Vector3 target, float radiusSqr)
        {
            w.Age += dt;
            w.StateTime += dt;

            if (canCollect && w.State != State.Magnet && (w.State != State.Pop || w.StateTime >= magnetDelay))
            {
                float dx = w.Position.x - feet.x, dz = w.Position.z - feet.z;
                if (dx * dx + dz * dz <= radiusSqr)
                {
                    w.State = State.Magnet;
                    w.StateTime = 0f;
                    w.Speed = 0f;
                    w.Velocity = Vector3.up * magnetLift;
                }
            }

            switch (w.State)
            {
                case State.Pop:
                    w.Velocity.y -= gravity * dt;
                    w.Position += w.Velocity * dt;
                    if (w.Position.y <= restHeight && w.Velocity.y < 0f)
                    {
                        w.Position.y = restHeight;
                        w.Velocity = Vector3.zero;
                        SetState(ref w, State.Rest);
                    }
                    break;

                case State.Rest:
                    if (driftEnabled && canCollect && w.Age >= driftAfter) SetState(ref w, State.Drift);
                    break;

                case State.Drift:
                {
                    if (!canCollect)
                    {
                        SetState(ref w, State.Rest);
                        break;
                    }
                    Vector3 to = feet - w.Position;
                    to.y = 0f;
                    float d = to.magnitude;
                    w.Speed = Mathf.Min(w.Speed + driftAccel * dt, driftMaxSpeed);
                    if (d > 0.001f) w.Position += to / d * Mathf.Min(w.Speed * dt, d);
                    w.Position.y = restHeight;
                    break;
                }

                case State.Magnet:
                {
                    if (!canCollect)
                    {
                        SetState(ref w, State.Pop); // player gone (death): the wisp drops back to the ground
                        w.Velocity = Vector3.zero;
                        break;
                    }
                    Vector3 to = target - w.Position;
                    float d = to.magnitude;
                    if (d <= absorbDistance || w.StateTime >= magnetTimeout) return true;
                    w.Speed = Mathf.Min(w.Speed + magnetAccel * dt, magnetMaxSpeed);
                    w.Velocity = Vector3.MoveTowards(w.Velocity, to / d * w.Speed, magnetSteer * dt);
                    Vector3 step = w.Velocity * dt;
                    if (step.sqrMagnitude >= d * d && Vector3.Dot(step, to) > 0f) return true; // would pass through the chest
                    w.Position += step;
                    break;
                }
            }
            return false;
        }

        private static void SetState(ref Wisp w, State state)
        {
            w.State = state;
            w.StateTime = 0f;
            w.Speed = 0f;
        }

        private void Collect(int index, Vector3 at)
        {
            int value = _wisps[index].Xp;
            int tier = _wisps[index].Tier;
            _wisps[index] = _wisps[--_count];
            CollectedXp += value;
            AbsorbFlash(at, tier);

            float now = Time.unscaledTime;
            if (now - _lastHaptic >= hapticInterval)
            {
                _lastHaptic = now;
                Haptics.Light();
            }
            Collected?.Invoke(at, value);
            if (xp != null) xp.AddXp(value); // last: a level-up pauses the game from here
        }

        private void EmitMotes(float dt)
        {
            if (motes == null || _count == 0 || dt <= 0f || tiers.Length == 0) return;
            int budget = maxMotesPerFrame;
            // Start at a rotating index so a tight budget is shared fairly across wisps.
            _moteCursor = _moteCursor >= _count ? 0 : _moteCursor;
            for (int n = 0; n < _count; n++)
            {
                ref var w = ref _wisps[(_moteCursor + n) % _count];
                w.MoteDebt += (w.State == State.Magnet ? moteRateFlying : moteRateResting) * dt;
                ref readonly var tier = ref tiers[w.Tier];
                while (w.MoteDebt >= 1f && budget > 0)
                {
                    w.MoteDebt -= 1f;
                    budget--;
                    _emit.position = w.Position + Random.insideUnitSphere * (tier.size * 0.3f);
                    _emit.velocity = Vector3.up * Random.Range(0.15f, 0.45f) - w.Velocity * 0.15f;
                    _emit.startColor = tier.mote;
                    _emit.startSize = Random.Range(moteSize.x, moteSize.y) * (0.6f + tier.size);
                    _emit.startLifetime = Random.Range(moteLifetime.x, moteLifetime.y);
                    motes.Emit(_emit, 1);
                }
                if (budget == 0) w.MoteDebt = Mathf.Min(w.MoteDebt, 1f); // no burst of saved-up motes later

                // Sparkle: a short white star flash on the lit upper side of a resting orb, every 1-2 s.
                if (w.State == State.Magnet || w.State == State.Pop) continue;
                w.SparkleIn -= dt;
                if (w.SparkleIn > 0f || budget == 0) continue;
                budget--;
                w.SparkleIn = Random.Range(sparkleInterval.x, sparkleInterval.y);
                _emit.position = w.Position + new Vector3(-0.18f, 0.32f, -0.12f) * tier.size;
                _emit.velocity = Vector3.zero;
                _emit.startColor = Color.white;
                _emit.startSize = tier.size * sparkleSize;
                _emit.startLifetime = 0.22f;
                motes.Emit(_emit, 1);
            }
            _moteCursor++;
        }

        private void AbsorbFlash(Vector3 at, int tierIndex)
        {
            if (motes == null || tiers.Length == 0) return;
            ref readonly var tier = ref tiers[tierIndex];
            _emit.position = at;
            _emit.velocity = Vector3.zero;
            _emit.startColor = tier.mote;
            _emit.startSize = tier.size * 2.2f;
            _emit.startLifetime = 0.2f;
            motes.Emit(_emit, 1);
            for (int i = 0; i < absorbSparks; i++)
            {
                _emit.velocity = Random.onUnitSphere * Random.Range(1.5f, 3f);
                _emit.startSize = Random.Range(moteSize.x, moteSize.y) * 1.4f;
                _emit.startLifetime = Random.Range(0.2f, 0.35f);
                motes.Emit(_emit, 1);
            }
        }

        private void Render(Vector3 target)
        {
            if (bodies == null || tiers.Length == 0) return;
            float t = Time.time;
            const float TwoPi = Mathf.PI * 2f;
            for (int i = 0; i < _count; i++)
            {
                ref readonly var w = ref _wisps[i];
                ref readonly var tier = ref tiers[w.Tier];
                Vector3 pos = w.Position;
                float pulse = Mathf.Sin((t * pulseHz + w.Phase) * TwoPi);
                float size = tier.size * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(w.Age / growTime));
                if (w.State == State.Rest || w.State == State.Drift)
                    pos.y += bobHeight * Mathf.Sin((t * bobHz + w.Phase * 0.37f) * TwoPi);
                else if (w.State == State.Magnet)
                    size *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((target - pos).magnitude / 1.5f)); // shrinks into the Knight

                // Body: opaque gold mesh orb. Colour alpha carries the shine phase so orbs do not flash in sync.
                Color body = tier.body;
                body.a = w.Phase * 0.1f % 1f;
                ref var p = ref _particles[i];
                p.position = pos;
                p.velocity = Vector3.zero;
                p.rotation = 0f;
                p.startSize = size * (1f + pulseAmount * 0.25f * pulse);
                p.startColor = body;
                p.startLifetime = 1000f; // never expires on its own: this script owns the lifetime
                p.remainingLifetime = 1000f;

                if (halos == null) continue;
                ref var h = ref _haloParticles[i];
                h.position = pos;
                h.velocity = Vector3.zero;
                h.rotation = 0f;
                h.startSize = size * tier.haloScale * (1f + pulseAmount * pulse);
                h.startColor = tier.halo;
                h.startLifetime = 1000f;
                h.remainingLifetime = 1000f;
            }
            bodies.SetParticles(_particles, _count);
            if (halos != null) halos.SetParticles(_haloParticles, _count);
        }
    }
}
