using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>Look (and particle system) of an enemy shot. Index into <see cref="EnemyProjectiles"/> looks.</summary>
    public enum ShotKind { Arrow, FirePot, AcidGlob, SoulOrb, BlackFireball } // SoulOrb: Hexbound Shaman ring (KAN-107); BlackFireball: Butcher (KAN-174); new kinds at the end

    /// <summary>
    /// Shots of the ranged zombies (KAN-105), shared by every shooter in the scene and reused by later casters and bosses.
    /// No GameObject per shot: a struct array (fixed capacity, swap-remove), drawn through
    /// <see cref="ParticleSystem.SetParticles(ParticleSystem.Particle[], int)"/> on ONE particle system per
    /// <see cref="ShotKind"/> (a head plus fading ghosts as a cheap trail; arrows use a stretched billboard). One draw call
    /// per kind for any count, no lights, no allocation.
    /// <list type="bullet">
    /// <item>Straight shots (arrows) fly at chest height on scaled time; a segment test hits the Knight inside the hit radius,
    /// so a fast arrow cannot skip it. One volley hits at most once (a close fan never stacks into an instant kill).</item>
    /// <item>Lobbed shots (fire pots, acid globs) fly a parabola to a fixed ground point in a fixed time and cannot hit
    /// anything in the air. On landing: impact damage when the Knight is inside the impact radius (the landing circle the
    /// shooter showed), then a ground hazard (<see cref="GroundHazards"/>). The circle warning
    /// (<see cref="GroundTelegraph"/>) fills during the flight and flashes on impact.</item>
    /// <item><see cref="Enemy.Walkability"/> stops straight shots and low (blockable) lobbed shots: pillars give cover, walls
    /// end them. A stopped lob hides its circle and leaves no hazard (nothing lands outside the shown area).</item>
    /// <item>Nothing hits a dead Knight.</item>
    /// </list>
    /// KAN-174: a look can be a 3D mesh (<see cref="Look.mesh"/>: mesh particles turned along the flight, rolling) with a
    /// spark trail (<see cref="Look.trail"/>: a second particle system fed by Emit, no allocation) and a spark burst on
    /// landing: the Butcher's black fireballs (<see cref="ShotKind.BlackFireball"/>, Blender model SM_BlackFireball).
    /// On the "Enemy Shots" object (RangedEnemyBuilder). <see cref="RangedAttack"/> fires; <see cref="Clear"/> ends all.
    /// </summary>
    public sealed class EnemyProjectiles : MonoBehaviour
    {
        /// <summary>Height (m) of straight shots above the ground (Knight chest).</summary>
        public const float FlightHeight = 1.1f;
        /// <summary>Height (m) a lobbed shot leaves the hand.</summary>
        public const float ThrowHeight = 1.5f;
        /// <summary>A blockable lob is stopped by walls only below this height (m); higher it flies over the pillars.</summary>
        public const float LowArcHeight = 1.6f;
        private const float BlockRadius = 0.15f;
        private const int MaxGhosts = 3;

        private struct Shot
        {
            public ShotKind Kind;
            public bool Lobbed;
            public Vector3 Position;
            public Vector3 Velocity;   // straight: constant; lobbed: last step (trail direction)
            public Vector3 Start, Target;
            public float Left;         // straight: metres still to fly
            public float Time, Flight; // lobbed: seconds flown / total
            public float Arc;          // lobbed: apex height over the straight line (m)
            public float Damage;
            public float Radius;       // straight: hit radius on the Knight; lobbed: impact radius
            public int Volley;
            public HazardSpec Hazard;
            public int Owner, MaxPerOwner;
            public bool Blockable;
            public GroundTelegraph Circle;
            public float ProgressStart; // circle progress at launch (the wind-up already filled part of it)
            public float Sparks;        // KAN-174: trail sparks owed (fractions carry over)
            public float Roll;          // KAN-174: mesh roll phase (degrees)
        }

        [Serializable]
        public struct Look
        {
            [Tooltip("Shot sprites: emission off, driven by SetParticles. maxParticles >= capacity x (ghosts + 1).")]
            public ParticleSystem sprites;
            [Min(0.05f)] public float size;
            public Color color;
            [Tooltip("Fading copies behind the head (0..3).")]
            [Range(0, MaxGhosts)] public int ghosts;
            [Min(0f)] public float ghostSpacing;
            [Tooltip("Write the velocity into the particle (stretched billboard renderer: arrows).")]
            public bool stretch;
            [Tooltip("KAN-174: mesh particles (3D start rotation on): the mesh's +Z turns along the flight and rolls.")]
            public bool mesh;
            [Tooltip("KAN-174: roll of a mesh shot round its flight line (degrees per second).")]
            public float spin;
            [Tooltip("KAN-174: spark trail (a particle system with emission off; the pool emits into it). Empty = none.")]
            public ParticleSystem trail;
            [Tooltip("KAN-174: trail sparks per second per shot.")]
            [Min(0f)] public float trailRate;
            [Tooltip("KAN-174: sparks thrown out where a lobbed shot lands.")]
            [Min(0)] public int impactSparks;
            public Color trailColor;
        }

        /// <summary>The scene's shot pool (ranged attacks find it here). Set while enabled.</summary>
        public static EnemyProjectiles Current { get; private set; }

        [SerializeField] private PlayerHealth player;
        [SerializeField] private GroundHazards hazards;
        [SerializeField] private EnemyTelegraphs telegraphs;
        [SerializeField, Min(1)] private int capacity = 48;
        [Tooltip("Index = (int)ShotKind.")]
        [SerializeField] private Look[] looks = new Look[0];

        private Shot[] _shots;
        private int _count;
        private int _volley;
        private int _hitVolley = -1;
        private ParticleSystem.Particle[][] _particles;
        private int[] _drawn;
        private ParticleSystem.EmitParams _spark;

        /// <summary>A shot hurt the Knight: point, damage dealt.</summary>
        public event Action<Vector3, float> HitPlayer;
        /// <summary>A shot struck a pillar or wall: point.</summary>
        public event Action<Vector3> Blocked;
        /// <summary>A lobbed shot landed: ground point, kind.</summary>
        public event Action<Vector3, ShotKind> Landed;

        public int Count => _count;
        public int Capacity => capacity;
        public PlayerHealth Player => player;
        public Vector3 PositionOf(int i) => _shots[i].Position;
        public ShotKind KindOf(int i) => _shots[i].Kind;
        public bool IsLobbed(int i) => _shots[i].Lobbed;
        public Vector3 TargetOf(int i) => _shots[i].Target;
        /// <summary>KAN-174: trail and landing sparks emitted since the scene started (tests).</summary>
        public int SparksEmitted { get; private set; }
        public Look LookOf(ShotKind kind) => (int)kind < looks.Length ? looks[(int)kind] : default;

        /// <summary>KAN-174: shots of <paramref name="kind"/> in flight.</summary>
        public int CountOf(ShotKind kind)
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (_shots[i].Kind == kind) n++;
            return n;
        }

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(PlayerHealth knight, GroundHazards hazardPool, EnemyTelegraphs warnings, int shotCapacity = 48, Look[] shotLooks = null)
        {
            player = knight;
            hazards = hazardPool;
            telegraphs = warnings;
            capacity = Mathf.Max(1, shotCapacity);
            if (shotLooks != null) looks = shotLooks;
            Allocate();
        }

        private void Awake() => Allocate();

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        private void Allocate()
        {
            if (_shots == null || _shots.Length != capacity)
            {
                _shots = new Shot[capacity];
                _count = 0;
            }
            int kinds = Mathf.Max(looks.Length, 1);
            if (_particles != null && _particles.Length == kinds && _particles[0].Length == capacity * (MaxGhosts + 1)) return;
            _particles = new ParticleSystem.Particle[kinds][];
            _drawn = new int[kinds];
            for (int k = 0; k < kinds; k++)
            {
                _particles[k] = new ParticleSystem.Particle[capacity * (MaxGhosts + 1)];
                _drawn[k] = -1;
            }
        }

        private GroundHazards Hazards => hazards != null ? hazards : GroundHazards.Current;
        private EnemyTelegraphs Telegraphs => telegraphs != null ? telegraphs : EnemyTelegraphs.Current;

        /// <summary>A new volley id: shots fired with the same id hurt the Knight once at most.</summary>
        public int NextVolley() => ++_volley;

        /// <summary>
        /// Fires a straight shot from <paramref name="origin"/> (ground point under the shooter) along
        /// <paramref name="direction"/> at chest height for <paramref name="distance"/> m. False when the pool is full.
        /// </summary>
        public bool FireStraight(int volley, ShotKind kind, Vector3 origin, Vector3 direction, float speed, float distance, float damage, float hitRadius)
        {
            Allocate();
            direction.y = 0f;
            if (_count >= capacity || direction.sqrMagnitude < 0.0001f || speed <= 0f) return false;
            _shots[_count++] = new Shot
            {
                Kind = kind,
                Position = new Vector3(origin.x, origin.y + FlightHeight, origin.z),
                Velocity = direction.normalized * speed,
                Left = distance,
                Damage = damage,
                Radius = hitRadius,
                Volley = volley,
            };
            return true;
        }

        /// <summary>
        /// Lobs a shot from <paramref name="origin"/> (ground point under the thrower) to <paramref name="target"/> (ground)
        /// in <paramref name="flightTime"/> s with an apex <paramref name="arcHeight"/> m over the straight line. On landing:
        /// <paramref name="damage"/> when the Knight is within <paramref name="impactRadius"/>, then <paramref name="hazard"/>
        /// (owner rules: <see cref="GroundHazards.Spawn"/>). <paramref name="circle"/> (rented, may be null) is filled from
        /// <paramref name="progressStart"/> to 1 during the flight, flashed on impact and released. False when the pool is full.
        /// </summary>
        public bool FireLobbed(int volley, ShotKind kind, Vector3 origin, Vector3 target, float flightTime, float arcHeight,
            float impactRadius, float damage, in HazardSpec hazard, int owner, int maxPerOwner, bool blockable,
            GroundTelegraph circle, float progressStart)
        {
            Allocate();
            if (_count >= capacity || flightTime <= 0f) return false;
            var start = new Vector3(origin.x, origin.y + ThrowHeight, origin.z);
            _shots[_count++] = new Shot
            {
                Kind = kind,
                Lobbed = true,
                Position = start,
                Start = start,
                Target = target,
                Flight = flightTime,
                Arc = Mathf.Max(0f, arcHeight),
                Damage = damage,
                Radius = impactRadius,
                Volley = volley,
                Hazard = hazard,
                Owner = owner,
                MaxPerOwner = maxPerOwner,
                Blockable = blockable,
                Circle = circle,
                ProgressStart = Mathf.Clamp01(progressStart),
            };
            return true;
        }

        /// <summary>Point on a lob from <paramref name="start"/> to <paramref name="target"/> at <paramref name="t"/> (0..1).</summary>
        public static Vector3 ArcPoint(Vector3 start, Vector3 target, float arc, float t) =>
            Vector3.LerpUnclamped(start, target, t) + Vector3.up * (4f * arc * t * (1f - t));

        /// <summary>KAN-174: removes the shots of one <paramref name="kind"/> and hides their warnings (the boss died).</summary>
        public void ClearKind(ShotKind kind)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_shots[i].Kind != kind) continue;
                EndCircle(ref _shots[i], hide: true);
                Remove(i);
            }
            Draw();
        }

        /// <summary>Removes every shot and hides their warnings (scene end, tests).</summary>
        public void Clear()
        {
            for (int i = _count - 1; i >= 0; i--) EndCircle(ref _shots[i], hide: true);
            _count = 0;
            Draw();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt > 0f && _count > 0) Step(dt);
            Draw();
        }

        private void Step(float dt)
        {
            bool canHit = player != null && !player.IsDead;
            Vector3 knight = canHit ? player.transform.position : Vector3.zero;
            var walk = Enemy.Walkability;
            for (int i = _count - 1; i >= 0; i--)
            {
                ref var s = ref _shots[i];
                if (s.Lobbed)
                {
                    s.Time += dt;
                    float t = Mathf.Clamp01(s.Time / s.Flight);
                    Vector3 from = s.Position;
                    s.Position = ArcPoint(s.Start, s.Target, s.Arc, t);
                    s.Velocity = (s.Position - from) / dt;
                    if (s.Circle != null) s.Circle.SetProgress(Mathf.Lerp(s.ProgressStart, 1f, t));
                    Trail(ref s, dt);
                    float ground = Mathf.Lerp(s.Start.y - ThrowHeight, s.Target.y, t);
                    if (t < 1f && s.Blockable && s.Position.y - ground < LowArcHeight && walk != null && walk.IsBlocked(s.Position, BlockRadius))
                    {
                        Blocked?.Invoke(s.Position);
                        EndCircle(ref s, hide: true);
                        Remove(i);
                        continue;
                    }
                    if (t < 1f) continue;
                    Land(ref s, canHit, knight);
                    canHit = canHit && !player.IsDead;
                    Remove(i);
                    continue;
                }

                Vector3 step = s.Velocity * dt;
                float stepLength = step.magnitude;
                Vector3 a = s.Position;
                s.Position += step;
                s.Left -= stepLength;
                if (canHit && s.Volley != _hitVolley && BossProjectiles.SegmentDistance(a, s.Position, knight) <= s.Radius)
                {
                    _hitVolley = s.Volley;
                    float dealt = player.TakeDamage(s.Damage, s.Position - s.Velocity); // KAN-187: arc on the shooter's side
                    HitPlayer?.Invoke(s.Position, dealt);
                    Remove(i);
                    canHit = !player.IsDead;
                    continue;
                }
                if (walk != null && walk.IsBlocked(s.Position, BlockRadius))
                {
                    Blocked?.Invoke(s.Position);
                    Remove(i);
                    continue;
                }
                if (s.Left <= 0f) Remove(i);
            }
        }

        /// <summary>Impact: damage inside the circle (once per volley), the hazard, the circle flash.</summary>
        private void Land(ref Shot s, bool canHit, Vector3 knight)
        {
            if (canHit && s.Damage > 0f && s.Volley != _hitVolley)
            {
                float dx = knight.x - s.Target.x, dz = knight.z - s.Target.z;
                if (dx * dx + dz * dz <= s.Radius * s.Radius)
                {
                    _hitVolley = s.Volley;
                    float dealt = player.TakeDamage(s.Damage, s.Start); // KAN-187: lobbed from the thrower
                    HitPlayer?.Invoke(s.Target, dealt);
                }
            }
            var pool = Hazards;
            if (pool != null && s.Hazard.IsValid) pool.Spawn(s.Target, s.Hazard, s.Owner, s.MaxPerOwner);
            Burst(s.Kind, s.Target);
            EndCircle(ref s, hide: false);
            Landed?.Invoke(s.Target, s.Kind);
        }

        private void EndCircle(ref Shot s, bool hide)
        {
            if (s.Circle == null) return;
            if (hide) s.Circle.Hide();
            else s.Circle.Flash();
            var pool = Telegraphs;
            if (pool != null) pool.Release(s.Circle);
            s.Circle = null;
        }

        private void Remove(int i) => _shots[i] = _shots[--_count];

        /// <summary>KAN-174: trail sparks behind a shot whose look has a trail (rate per second, fractions carry over).</summary>
        private void Trail(ref Shot s, float dt)
        {
            int k = (int)s.Kind;
            if (k >= looks.Length) return;
            var look = looks[k];
            s.Roll += look.spin * dt;
            if (look.trail == null || look.trailRate <= 0f) return;
            s.Sparks += look.trailRate * dt;
            Vector3 back = s.Velocity.sqrMagnitude > 0.0001f ? -s.Velocity.normalized : Vector3.zero;
            while (s.Sparks >= 1f)
            {
                s.Sparks -= 1f;
                Spark(look, s.Position + UnityEngine.Random.insideUnitSphere * (look.size * 0.35f),
                    back * UnityEngine.Random.Range(0.4f, 1.4f) + UnityEngine.Random.insideUnitSphere * 0.5f + Vector3.up * 0.3f,
                    UnityEngine.Random.Range(0.35f, 0.65f));
            }
        }

        /// <summary>KAN-174: sparks thrown out where a shot with a trail look lands.</summary>
        private void Burst(ShotKind kind, Vector3 at)
        {
            int k = (int)kind;
            if (k >= looks.Length) return;
            var look = looks[k];
            if (look.trail == null) return;
            for (int i = 0; i < look.impactSparks; i++)
            {
                Vector2 d = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(1.5f, 3.5f);
                Spark(look, at + Vector3.up * 0.2f, new Vector3(d.x, UnityEngine.Random.Range(1.2f, 3f), d.y),
                    UnityEngine.Random.Range(0.4f, 0.75f));
            }
        }

        private void Spark(in Look look, Vector3 position, Vector3 velocity, float life)
        {
            _spark.position = position;
            _spark.velocity = velocity;
            _spark.startLifetime = life;
            _spark.startSize = look.size * UnityEngine.Random.Range(0.12f, 0.24f);
            _spark.startColor = look.trailColor;
            look.trail.Emit(_spark, 1);
            SparksEmitted++;
        }

        private void Draw()
        {
            if (_particles == null) return;
            float time = Time.time;
            for (int k = 0; k < looks.Length; k++)
            {
                var look = looks[k];
                if (look.sprites == null) continue;
                var buffer = _particles[k];
                int n = 0;
                int ghosts = Mathf.Clamp(look.ghosts, 0, MaxGhosts);
                for (int i = 0; i < _count; i++)
                {
                    ref readonly var s = ref _shots[i];
                    if ((int)s.Kind != k) continue;
                    Vector3 back = s.Velocity.sqrMagnitude > 0.0001f ? -s.Velocity.normalized : Vector3.zero;
                    float flicker = 1f + 0.08f * Mathf.Sin(time * 37f + i * 1.7f);
                    Vector3 rotation = Vector3.zero;
                    if (look.mesh) // KAN-174: the mesh's +Z (head) along the flight, rolling round it
                    {
                        Vector3 ahead = back != Vector3.zero ? -back : s.Target - s.Start;
                        if (ahead.sqrMagnitude < 0.0001f) ahead = Vector3.forward;
                        rotation = (Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, 0f, s.Roll)).eulerAngles;
                    }
                    for (int g = 0; g <= ghosts; g++)
                    {
                        ref var p = ref buffer[n++];
                        float scale = 1f - g * 0.25f;
                        var c = look.color;
                        c.a *= g == 0 ? 1f : 0.5f * scale;
                        p.position = s.Position + back * (look.ghostSpacing * g);
                        p.velocity = look.stretch ? s.Velocity : Vector3.zero;
                        p.rotation = 0f;
                        if (look.mesh) p.rotation3D = rotation;
                        p.startSize = look.size * scale * (look.stretch ? 1f : flicker);
                        p.startColor = c;
                        p.startLifetime = 1000f;
                        p.remainingLifetime = 1000f;
                    }
                }
                if (n == 0 && _drawn[k] == 0) continue;
                look.sprites.SetParticles(buffer, n);
                _drawn[k] = n;
            }
        }
    }
}
