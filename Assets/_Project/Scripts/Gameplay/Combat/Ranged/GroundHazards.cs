using System;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>Look of a ground hazard (one particle system each). Toxic: the Plague Bloater's cloud (KAN-106). New kinds go at the end.</summary>
    public enum HazardKind { Fire, Acid, Toxic }

    /// <summary>One ground hazard: a circle that hurts (and maybe slows) the Knight while it stands inside.</summary>
    [Serializable]
    public struct HazardSpec
    {
        public HazardKind kind;
        [Min(0f)] public float radius;
        [Tooltip("Seconds on the ground.")]
        [Min(0f)] public float life;
        [Tooltip("Damage per second to the Knight inside.")]
        [Min(0f)] public float dps;
        [Tooltip("Share of the Knight's speed taken away inside (0.3 = 30 % slower).")]
        [Range(0f, 0.9f)] public float slow;

        public bool IsValid => radius > 0f && life > 0f && (dps > 0f || slow > 0f);
    }

    /// <summary>
    /// Ground hazards of enemy shots (KAN-105): burning patches of fire pots, acid puddles of bile globs, toxic clouds of burst Plague Bloaters (KAN-106).
    /// No GameObject per hazard: a struct array (fixed capacity, swap-remove), drawn as flat ground billboards of ONE particle
    /// system per <see cref="HazardKind"/> (<see cref="ParticleSystem.SetParticles(ParticleSystem.Particle[], int)"/>), so
    /// any count costs one draw call per kind and no allocation.
    /// <list type="bullet">
    /// <item>Overlapping hazards do not stack: the Knight takes the highest dps and the strongest slow of the circles it
    /// stands in (planar distance &lt;= radius), on scaled time (pauses with the level-up cards).</item>
    /// <item>The slow goes to <see cref="PlayerMover.Slow"/> every frame (0 outside).</item>
    /// <item>An owner (one enemy) keeps at most N hazards: a new one ends its oldest (Bile Spitter: 3 puddles).</item>
    /// <item>A hazard fades in fast and out over its last <see cref="FadeTime"/> s; it hurts for its whole life.</item>
    /// </list>
    /// On the "Enemy Shots" object (RangedEnemyBuilder); <see cref="EnemyProjectiles"/> drops hazards where shots land.
    /// </summary>
    public sealed class GroundHazards : MonoBehaviour
    {
        public const float FadeTime = 0.6f;
        private const float FadeInTime = 0.15f;
        private const float Lift = 0.04f;

        private struct Hazard
        {
            public Vector3 Centre;
            public HazardSpec Spec;
            public float Age;
            public int Owner;
            public int Serial; // spawn order: the oldest of an owner is replaced first
            public float Spin;
        }

        [Serializable]
        public struct Look
        {
            [Tooltip("Flat sprites (horizontal billboard), emission off, driven by SetParticles.")]
            public ParticleSystem sprites;
            public Color color;
            [Tooltip("Sprite diameter / hazard diameter (the texture's soft rim sits outside the hurt circle).")]
            [Min(0.5f)] public float sizeScale;
            [Tooltip("Size + alpha flicker (fire licks, acid stays calm).")]
            [Range(0f, 0.3f)] public float flicker;
        }

        /// <summary>The scene's hazard pool (enemy shots find it here). Set while enabled.</summary>
        public static GroundHazards Current { get; private set; }

        [SerializeField] private PlayerHealth player;
        [SerializeField] private PlayerMover mover;
        [SerializeField, Min(1)] private int capacity = 24;
        [Tooltip("Index = (int)HazardKind.")]
        [SerializeField] private Look[] looks = new Look[0];

        private Hazard[] _hazards;
        private int _count;
        private int _serial;
        private ParticleSystem.Particle[][] _particles;
        private int[] _drawn;

        /// <summary>A hazard was placed: centre, spec.</summary>
        public event Action<Vector3, HazardSpec> Spawned;

        public int Count => _count;
        public int Capacity => capacity;
        /// <summary>Damage per second the Knight took on the last step (highest of the circles it stands in).</summary>
        public float KnightDps { get; private set; }
        /// <summary>Slow applied on the last step.</summary>
        public float KnightSlow { get; private set; }
        public Vector3 CentreOf(int i) => _hazards[i].Centre;
        public HazardSpec SpecOf(int i) => _hazards[i].Spec;
        public int OwnerOf(int i) => _hazards[i].Owner;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(PlayerHealth knight, PlayerMover knightMover = null, int hazardCapacity = 24, Look[] hazardLooks = null)
        {
            player = knight;
            mover = knightMover;
            capacity = Mathf.Max(1, hazardCapacity);
            if (hazardLooks != null) looks = hazardLooks;
            Allocate();
        }

        private void Awake() => Allocate();

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (mover != null) mover.Slow = 0f;
            KnightDps = 0f;
            KnightSlow = 0f;
        }

        private void Allocate()
        {
            if (_hazards == null || _hazards.Length != capacity)
            {
                _hazards = new Hazard[capacity];
                _count = 0;
            }
            int kinds = Mathf.Max(looks.Length, 1);
            if (_particles != null && _particles.Length == kinds) return;
            _particles = new ParticleSystem.Particle[kinds][];
            _drawn = new int[kinds];
            for (int k = 0; k < kinds; k++)
            {
                _particles[k] = new ParticleSystem.Particle[capacity];
                _drawn[k] = -1;
            }
        }

        /// <summary>
        /// Puts a hazard at <paramref name="centre"/> (ground). <paramref name="owner"/> &gt; 0 with <paramref name="maxPerOwner"/>
        /// &gt; 0: the owner's oldest hazard ends first when it already has that many. A full pool ends the oldest hazard.
        /// Returns false for an invalid spec.
        /// </summary>
        public bool Spawn(Vector3 centre, in HazardSpec spec, int owner = 0, int maxPerOwner = 0)
        {
            Allocate();
            if (!spec.IsValid) return false;
            if (owner != 0 && maxPerOwner > 0)
                while (CountOwned(owner) >= maxPerOwner) Remove(Oldest(owner));
            if (_count >= capacity) Remove(Oldest(0));
            _hazards[_count++] = new Hazard
            {
                Centre = new Vector3(centre.x, centre.y, centre.z),
                Spec = spec,
                Age = 0f,
                Owner = owner,
                Serial = ++_serial,
                Spin = UnityEngine.Random.value * 360f,
            };
            Spawned?.Invoke(centre, spec);
            return true;
        }

        /// <summary>Hazards of <paramref name="owner"/> on the ground.</summary>
        public int CountOwned(int owner)
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (_hazards[i].Owner == owner) n++;
            return n;
        }

        /// <summary>Index of the oldest hazard of <paramref name="owner"/> (0 = any owner); -1 when there is none.</summary>
        private int Oldest(int owner)
        {
            int best = -1;
            for (int i = 0; i < _count; i++)
            {
                if (owner != 0 && _hazards[i].Owner != owner) continue;
                if (best < 0 || _hazards[i].Serial < _hazards[best].Serial) best = i;
            }
            return best;
        }

        /// <summary>Removes every hazard (scene end, tests).</summary>
        public void Clear()
        {
            _count = 0;
            if (mover != null) mover.Slow = 0f;
            Draw();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt > 0f) Step(dt);
            Draw();
        }

        private void Step(float dt)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                ref var h = ref _hazards[i];
                h.Age += dt;
                if (h.Age >= h.Spec.life) Remove(i);
            }

            float dps = 0f, slow = 0f;
            if (player != null && !player.IsDead)
            {
                Vector3 k = player.transform.position;
                for (int i = 0; i < _count; i++)
                {
                    ref readonly var h = ref _hazards[i];
                    float dx = k.x - h.Centre.x, dz = k.z - h.Centre.z;
                    if (dx * dx + dz * dz > h.Spec.radius * h.Spec.radius) continue;
                    dps = Mathf.Max(dps, h.Spec.dps);
                    slow = Mathf.Max(slow, h.Spec.slow);
                }
                if (dps > 0f) player.TakeDamage(dps * dt);
            }
            KnightDps = dps;
            KnightSlow = slow;
            if (mover != null) mover.Slow = slow;
        }

        /// <summary>Highest dps of the hazards covering <paramref name="point"/> (planar). Debug and tests.</summary>
        public float DpsAt(Vector3 point)
        {
            float dps = 0f;
            for (int i = 0; i < _count; i++)
            {
                ref readonly var h = ref _hazards[i];
                float dx = point.x - h.Centre.x, dz = point.z - h.Centre.z;
                if (dx * dx + dz * dz <= h.Spec.radius * h.Spec.radius) dps = Mathf.Max(dps, h.Spec.dps);
            }
            return dps;
        }

        private void Remove(int i)
        {
            if (i < 0 || i >= _count) return;
            _hazards[i] = _hazards[--_count];
        }

        private void Draw()
        {
            if (_particles == null) return;
            float t = Time.time;
            for (int k = 0; k < looks.Length; k++)
            {
                var look = looks[k];
                if (look.sprites == null) continue;
                var buffer = _particles[k];
                int n = 0;
                for (int i = 0; i < _count; i++)
                {
                    ref readonly var h = ref _hazards[i];
                    if ((int)h.Spec.kind != k) continue;
                    float fadeIn = Mathf.Clamp01(h.Age / FadeInTime);
                    float fadeOut = Mathf.Clamp01((h.Spec.life - h.Age) / FadeTime);
                    float wave = Mathf.Sin(t * 9f + h.Serial * 1.3f);
                    float size = h.Spec.radius * 2f * look.sizeScale * (0.85f + 0.15f * fadeIn) * (1f + look.flicker * 0.5f * wave);
                    var c = look.color;
                    c.a *= fadeIn * fadeOut * (1f - look.flicker * 0.5f * (1f - wave) * 0.5f);
                    ref var p = ref buffer[n++];
                    p.position = new Vector3(h.Centre.x, h.Centre.y + Lift, h.Centre.z);
                    p.velocity = Vector3.zero;
                    p.rotation = h.Spin + h.Age * 6f;
                    p.startSize = size;
                    p.startColor = c;
                    p.startLifetime = 1000f;
                    p.remainingLifetime = 1000f;
                }
                if (n == 0 && _drawn[k] == 0) continue;
                look.sprites.SetParticles(buffer, n);
                _drawn[k] = n;
            }
        }
    }
}
