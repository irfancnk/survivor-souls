using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Soul Burst": every enemy death bursts a ring of soul fire that deals <see cref="StatId.KillBurst"/> times the
    /// current bolt damage to every enemy within <see cref="Radius"/> (<see cref="radius"/> + <see cref="StatId.BurstRadius"/>).
    /// Deaths are queued (fixed ring buffer) and resolved in Update, never inside the damage event, so
    /// <see cref="Enemy.Active"/> is not edited while someone walks it. KAN-246 (Ember Boons, Epic): kills by a burst damage
    /// the crowd but never burst again (no chain reaction), and neither do kills inside a
    /// <see cref="ProcGuard.NoKillChains"/> scope. The chain-depth look (fade, shake) stays for depth 0 only.
    /// <para>
    /// Look (KAN-66): a small dark-magic explosion from six pooled, Emit-only systems: white-violet core flash with a scale
    /// punch, a violet ground shockwave that ends exactly on the damage radius (<see cref="RingSize"/>), a faint dome shell,
    /// dark soul-fire wisps with a magenta rim + a few smoke puffs (one alpha-blended system), soul shards that arc out and
    /// fall, and a rune mark on the ground. A chain fades with its depth (<see cref="ChainScale"/>) so 20 bursts do not
    /// white out; bursts in the same frame close together merge into one stronger visual; over
    /// <see cref="particleCap"/> live fire / shard particles a burst shows only the flash + ring. A deep chain gives a
    /// small camera shake and a rate-limited haptic tap. <see cref="Burst"/> feeds the sound. No allocation per burst.
    /// </para>
    /// </summary>
    public sealed class KillBurstEffect : SkillEffect
    {
        private const int QueueSize = 32;
        private const int BudgetSlots = 64;
        /// <summary>Ring line radius in the ring texture, as a share of the particle size (T_Ring: line at 0.8 of the half-size).</summary>
        public const float RingTextureRadius = 0.4f;
        /// <summary>Outer band radius in the rune circle texture, as a share of the particle size (T_RuneCircle: band at 0.94).</summary>
        public const float RuneTextureRadius = 0.47f;
        /// <summary>Weakest look of a deep chain (share of a first burst).</summary>
        public const float MinChainScale = 0.45f;
        /// <summary>Longest life (s) of a fire / shard particle: how long a burst counts against the cap.</summary>
        private const float RichLife = 0.75f;

        /// <summary>Visual burst played (centre, chain depth: 0 = a kill by the Knight, 1+ = a kill by a burst). After merging.</summary>
        public static event Action<Vector3, int> Burst;

        [SerializeField, Min(0.5f)] private float radius = 2.6f;
        [Header("Layers (KAN-66)")]
        [SerializeField] private ParticleSystem flash;
        [Tooltip("Ground shockwave (ring texture, horizontal). Ends on the damage radius.")]
        [SerializeField] private ParticleSystem ring;
        [Tooltip("Faint dome shell (ring texture, camera-facing).")]
        [SerializeField] private ParticleSystem shell;
        [Tooltip("Soul fire wisps + smoke puffs (alpha blend).")]
        [SerializeField] private ParticleSystem fire;
        [Tooltip("Soul shards that fly out and fall.")]
        [SerializeField] private ParticleSystem sparks;
        [Tooltip("Rune mark on the ground (rune circle texture, horizontal).")]
        [SerializeField] private ParticleSystem mark;
        [SerializeField, Min(0)] private int fireCount = 10;
        [SerializeField, Min(0)] private int smokeCount = 3;
        [SerializeField, Min(0)] private int sparkCount = 12;
        [Header("Limits")]
        [Tooltip("Live fire + smoke + shard particles from all bursts. Over it a burst plays the flash + ring only.")]
        [SerializeField, Min(0)] private int particleCap = 400;
        [Tooltip("Bursts in the same frame closer than this (m) merge into one stronger visual.")]
        [SerializeField, Min(0f)] private float mergeDistance = 1.2f;
        [Header("Big chain")]
        [SerializeField, Min(1)] private int shakeChainDepth = 4;
        [SerializeField, Min(0f)] private float shakeAmplitude = 0.07f;
        [SerializeField, Min(0f)] private float shakeTime = 0.25f;

        private static readonly Color FlashColor = new Color(0.93f, 0.85f, 1f, 1f);
        private static readonly Color RingColor = new Color(0.6f, 0.2f, 0.92f, 0.95f);
        private static readonly Color ShellColor = new Color(0.72f, 0.45f, 1f, 0.32f);
        private static readonly Color FireDark = new Color(0.36f, 0.08f, 0.56f, 0.8f);
        private static readonly Color FireRim = new Color(0.72f, 0.22f, 0.86f, 0.8f);
        private static readonly Color SmokeColor = new Color(0.1f, 0.06f, 0.14f, 0.42f);
        private static readonly Color ShardA = new Color(1f, 0.62f, 1f, 1f);
        private static readonly Color ShardB = new Color(0.72f, 0.42f, 1f, 1f);
        private static readonly Color MarkColor = new Color(0.32f, 0.06f, 0.5f, 0.6f);

        private readonly Vector3[] _queue = new Vector3[QueueSize];
        private readonly int[] _queueDepth = new int[QueueSize];
        private int _head, _count;
        private Action<Enemy, float, bool> _onDamaged;
        private bool _resolving;
        private int _resolvingDepth;

        // Visual bursts of the current Tick (merged), emitted after the damage pass.
        private readonly Vector3[] _fxCentre = new Vector3[QueueSize];
        private readonly int[] _fxDepth = new int[QueueSize];
        private readonly int[] _fxWeight = new int[QueueSize];
        private int _fxCount;

        // Live-particle budget: (time, count) per rich burst, expired after RichLife.
        private readonly float[] _budgetTime = new float[BudgetSlots];
        private readonly int[] _budgetCount = new int[BudgetSlots];
        private int _budgetNext;

        private TopDownFollowCamera _camera;
        private bool _cameraLooked;

        /// <summary>Burst reach in metres after skill levels (KAN-71: higher Soul Burst levels reach further).</summary>
        public float Radius => Mathf.Max(0.5f, PlayerStats.Eval(Context?.Stats, StatId.BurstRadius, radius));
        /// <summary>Bursts resolved this run (damage pulses, before visual merging).</summary>
        public int BurstCount { get; private set; }
        /// <summary>Visual bursts played (after merging).</summary>
        public int VisualCount { get; private set; }
        /// <summary>Visual bursts that fell back to flash + ring (particle cap reached).</summary>
        public int DegradedCount { get; private set; }
        /// <summary>Visual bursts that merged into a neighbour this run.</summary>
        public int MergedCount { get; private set; }
        /// <summary>Particles the last visual burst emitted (all layers).</summary>
        public int LastBurstParticles { get; private set; }
        /// <summary>Chain depth of the last visual burst.</summary>
        public int LastDepth { get; private set; }
        /// <summary>Camera shakes asked for (deep chains).</summary>
        public int Shakes { get; private set; }
        /// <summary>Layers wired (the shipped prefab has all six).</summary>
        public int LayerCount => (flash != null ? 1 : 0) + (ring != null ? 1 : 0) + (shell != null ? 1 : 0) + (fire != null ? 1 : 0)
                                 + (sparks != null ? 1 : 0) + (mark != null ? 1 : 0);
        /// <summary>Ground shockwave system (tests read its last particle size).</summary>
        public ParticleSystem RingLayer => ring;
        /// <summary>Live fire / shard particle cap.</summary>
        public int ParticleCap => particleCap;
        /// <summary>Most particles one burst may emit (ticket budget: about 40).</summary>
        public int MaxBurstParticles => 3 + 1 + MergedFire(int.MaxValue) + smokeCount + MergedSparks(int.MaxValue);

        /// <summary>Particle size of a ring-texture layer whose ring line sits on <paramref name="reach"/> metres.</summary>
        public static float RingSize(float reach) => reach / RingTextureRadius;

        /// <summary>Look strength of a burst at <paramref name="depth"/> in a chain: 1 for a Knight kill, fading to <see cref="MinChainScale"/>.</summary>
        public static float ChainScale(int depth) => Mathf.Max(MinChainScale, 1f - 0.12f * Mathf.Max(0, depth));

        /// <summary>Live fire / shard particles counted against <see cref="particleCap"/> right now.</summary>
        public int LiveBudget
        {
            get
            {
                float now = Time.time;
                int sum = 0;
                for (int i = 0; i < BudgetSlots; i++)
                    if (_budgetCount[i] > 0 && now - _budgetTime[i] < RichLife) sum += _budgetCount[i];
                return sum;
            }
        }

        protected override void Subscribe()
        {
            _onDamaged ??= OnDamaged;
            Enemy.Damaged += _onDamaged;
        }

        protected override void Unsubscribe() => Enemy.Damaged -= _onDamaged;

        private void OnDamaged(Enemy enemy, float amount, bool killed)
        {
            // KAN-246: a burst kill damages the crowd but starts no new burst (no chain reaction), nor does a kill inside a
            // ProcGuard.NoKillChains scope (Second Dawn's shockwave).
            if (!killed || _resolving || !ProcGuard.KillsChain) return;
            if (_count == QueueSize) { _head = (_head + 1) % QueueSize; _count--; } // drop the oldest under a flood
            int slot = (_head + _count) % QueueSize;
            _queue[slot] = enemy.transform.position;
            _queueDepth[slot] = _resolving ? _resolvingDepth + 1 : 0;
            _count++;
        }

        private void Update() => Tick();

        /// <summary>Resolves the bursts queued so far (new kills queue for the next call). Public for tests.</summary>
        public void Tick()
        {
            int n = _count;
            if (n == 0) return;
            float share = PlayerStats.Eval(Context.Stats, StatId.KillBurst, 0f);
            float damage = share * (Context.Shooter != null ? Context.Shooter.CurrentDamage : 0f);
            _fxCount = 0;
            for (int k = 0; k < n; k++)
            {
                Vector3 c = _queue[_head];
                int depth = _queueDepth[_head];
                _head = (_head + 1) % QueueSize;
                _count--;
                QueueFx(c, depth);
                Explode(c, depth, damage);
            }
            int deepest = -1;
            for (int i = 0; i < _fxCount; i++)
            {
                PlayFx(_fxCentre[i], _fxDepth[i], _fxWeight[i]);
                if (_fxDepth[i] > deepest) deepest = _fxDepth[i];
            }
            _fxCount = 0;
            if (deepest >= shakeChainDepth) BigChain();
        }

        private void QueueFx(Vector3 c, int depth)
        {
            float m2 = mergeDistance * mergeDistance;
            for (int i = 0; i < _fxCount; i++)
            {
                Vector3 d = _fxCentre[i] - c;
                d.y = 0f;
                if (d.sqrMagnitude > m2) continue;
                _fxWeight[i]++;
                if (depth < _fxDepth[i]) _fxDepth[i] = depth; // the stronger look wins
                MergedCount++;
                return;
            }
            _fxCentre[_fxCount] = c;
            _fxDepth[_fxCount] = depth;
            _fxWeight[_fxCount] = 1;
            _fxCount++;
        }

        private void Explode(Vector3 centre, int depth, float damage)
        {
            BurstCount++;
            if (damage <= 0f) return;
            float r = Radius;
            float r2 = r * r;
            var enemies = Enemy.Active;
            _resolving = true;
            _resolvingDepth = depth;
            try
            {
                for (int i = enemies.Count - 1; i >= 0; i--) // backwards: a kill removes enemies[i] only
                {
                    if (i >= enemies.Count) continue;
                    var e = enemies[i];
                    Vector3 d = e.transform.position - centre;
                    d.y = 0f;
                    if (d.sqrMagnitude > r2) continue;
                    e.TakeDamage(damage, d, DamageKind.Burst);
                }
            }
            finally
            {
                _resolving = false;
            }
        }

        private int MergedFire(int weight) => fireCount + 2 * Mathf.Clamp(weight - 1, 0, 2);
        private int MergedSparks(int weight) => sparkCount + 3 * Mathf.Clamp(weight - 1, 0, 2);

        private void PlayFx(Vector3 centre, int depth, int weight)
        {
            VisualCount++;
            LastDepth = depth;
            float scale = ChainScale(depth);
            float strength = 1f + 0.2f * Mathf.Min(3, weight - 1); // merged bursts: one bigger pop
            float reach = Radius;
            int emitted = 0;

            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            if (flash != null)
            {
                p.position = centre + Vector3.up * 0.7f;
                p.velocity = Vector3.zero;
                p.startSize = (1.5f + 0.25f * reach) * strength * Mathf.Lerp(0.8f, 1f, scale);
                p.startColor = Fade(FlashColor, scale);
                flash.Emit(p, 1);
                emitted++;
            }
            if (ring != null)
            {
                p.position = centre + Vector3.up * 0.06f;
                p.velocity = Vector3.zero;
                p.startSize = RingSize(reach);
                p.startColor = Fade(RingColor, scale);
                ring.Emit(p, 1);
                emitted++;
            }

            int fires = Mathf.RoundToInt(MergedFire(weight) * scale);
            int smokes = Mathf.RoundToInt(smokeCount * scale);
            int shards = Mathf.RoundToInt(MergedSparks(weight) * scale);
            int rich = fires + smokes + shards;
            if (rich > 0 && LiveBudget + rich > particleCap)
            {
                DegradedCount++;
                Finish(centre, depth, emitted);
                return;
            }

            if (shell != null)
            {
                p.position = centre + Vector3.up * 0.9f;
                p.velocity = Vector3.zero;
                p.startSize = RingSize(reach) * 0.85f;
                p.startColor = Fade(ShellColor, scale);
                shell.Emit(p, 1);
                emitted++;
            }
            if (mark != null)
            {
                p.position = centre + Vector3.up * 0.04f;
                p.velocity = Vector3.zero;
                p.startSize = reach / RuneTextureRadius;
                p.startColor = Fade(MarkColor, scale);
                p.rotation = UnityEngine.Random.Range(0f, 360f);
                mark.Emit(p, 1);
                p.rotation = 0f;
                emitted++;
            }
            if (fire != null)
            {
                float smokeScale = scale * scale; // fire / smoke thin out faster down a chain: the Knight stays visible
                float start = 0.3f + reach * 0.15f; // a ring of soul fire round an open core, not one dark blot
                float speed = Mathf.Lerp(0.7f, 1f, scale) * (1.6f + reach * 0.7f);
                for (int i = 0; i < fires; i++)
                {
                    float a = i / (float)fires * Mathf.PI * 2f + UnityEngine.Random.Range(-0.3f, 0.3f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    p.position = centre + dir * start + Vector3.up * UnityEngine.Random.Range(0.3f, 0.8f);
                    p.velocity = dir * (speed * UnityEngine.Random.Range(0.75f, 1.15f)) + Vector3.up * UnityEngine.Random.Range(1f, 2.4f);
                    p.startSize = UnityEngine.Random.Range(0.6f, 1f) * Mathf.Lerp(0.75f, 1f, scale);
                    p.startColor = Fade(Color.Lerp(FireDark, FireRim, UnityEngine.Random.value), smokeScale);
                    p.rotation = UnityEngine.Random.Range(0f, 360f);
                    p.startLifetime = UnityEngine.Random.Range(0.42f, 0.62f);
                    fire.Emit(p, 1);
                }
                for (int i = 0; i < smokes; i++)
                {
                    var dir = UnityEngine.Random.insideUnitSphere;
                    dir.y = 0f;
                    p.position = centre + dir * (reach * 0.35f) + Vector3.up * 0.5f;
                    p.velocity = dir * 1.2f + Vector3.up * UnityEngine.Random.Range(0.8f, 1.4f);
                    p.startSize = UnityEngine.Random.Range(1f, 1.4f);
                    p.startColor = Fade(SmokeColor, smokeScale);
                    p.rotation = UnityEngine.Random.Range(0f, 360f);
                    p.startLifetime = UnityEngine.Random.Range(0.6f, RichLife);
                    fire.Emit(p, 1);
                }
                p.rotation = 0f;
                emitted += fires + smokes;
            }
            if (sparks != null)
            {
                var s = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                for (int i = 0; i < shards; i++)
                {
                    float a = i / (float)shards * Mathf.PI * 2f + UnityEngine.Random.Range(-0.2f, 0.2f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    s.position = centre + dir * 0.3f + Vector3.up * 0.5f;
                    s.velocity = dir * UnityEngine.Random.Range(4f, 6.5f) + Vector3.up * UnityEngine.Random.Range(2f, 3.6f);
                    s.startColor = Color.Lerp(ShardA, ShardB, UnityEngine.Random.value);
                    sparks.Emit(s, 1);
                }
                emitted += shards;
            }
            Spend(rich);
            Finish(centre, depth, emitted);
        }

        private void Finish(Vector3 centre, int depth, int emitted)
        {
            LastBurstParticles = emitted;
            Burst?.Invoke(centre, depth);
        }

        private static Color Fade(Color c, float scale) => new Color(c.r, c.g, c.b, c.a * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01((scale - MinChainScale * MinChainScale) / (1f - MinChainScale * MinChainScale))));

        private void Spend(int count)
        {
            if (count <= 0) return;
            _budgetTime[_budgetNext] = Time.time;
            _budgetCount[_budgetNext] = count;
            _budgetNext = (_budgetNext + 1) % BudgetSlots;
        }

        /// <summary>A deep chain: a small shake (no shake for single bursts) and one light tap through the haptics rate limit.</summary>
        private void BigChain()
        {
            Shakes++;
            Haptics.Light();
            if (!_cameraLooked)
            {
                _cameraLooked = true;
                _camera = FindAnyObjectByType<TopDownFollowCamera>();
            }
            if (_camera != null) _camera.Shake(shakeAmplitude, shakeTime);
        }
    }
}
