using System;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Boss fan bolts (KAN-61). No GameObject per bolt: a struct array (fixed capacity, swap-remove), drawn as billboards
    /// of ONE particle system through <see cref="ParticleSystem.SetParticles(ParticleSystem.Particle[], int)"/>: a hot
    /// ember head and two fading ghosts behind it (a cheap trail). One draw call for any count, no lights, no allocation.
    /// <list type="bullet">
    /// <item>Bolts fly straight at chest height on scaled time (they pause with the level-up cards).</item>
    /// <item><see cref="Enemy.Walkability"/> stops them: the arena pillars give cover, the ring wall ends them.</item>
    /// <item>A bolt hits the Knight inside its hit radius (planar). One volley hits at most once, so a close fan can never
    /// stack its bolts into an instant kill. Nothing hits a dead Knight.</item>
    /// </list>
    /// On the "Boss" object (BossBuilder); <see cref="BossFight"/> fires the volleys and clears them when the boss dies.
    /// </summary>
    public sealed class BossProjectiles : MonoBehaviour
    {
        private const float FlightHeight = 1.1f;
        private const float BlockRadius = 0.15f;
        private const int ParticlesPerBolt = 3;

        private struct Projectile
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Left;     // metres still to fly
            public float Damage;
            public float HitRadius;
            public int Volley;
        }

        [SerializeField] private PlayerHealth player;
        [Tooltip("Bolt sprites: emission off, driven by SetParticles. maxParticles >= capacity x 3.")]
        [SerializeField] private ParticleSystem bodies;
        [SerializeField, Min(1)] private int capacity = 32;
        [SerializeField, Min(0.05f)] private float size = 0.75f;
        [SerializeField] private Color headColor = Color.white;
        [Tooltip("Spacing of the trail ghosts behind the head (m).")]
        [SerializeField, Min(0f)] private float ghostSpacing = 0.38f;

        private Projectile[] _bolts;
        private int _count;
        private int _volley;
        private int _hitVolley = -1;
        private ParticleSystem.Particle[] _particles;
        private int _drawn = -1;

        /// <summary>A bolt hit the Knight: point, damage dealt.</summary>
        public event Action<Vector3, float> HitPlayer;
        /// <summary>A bolt struck a pillar or the ring wall: point.</summary>
        public event Action<Vector3> Blocked;

        public int Count => _count;
        public int Capacity => capacity;
        public Vector3 PositionOf(int i) => _bolts[i].Position;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(PlayerHealth knight, ParticleSystem sprites = null, int boltCapacity = 32)
        {
            player = knight;
            bodies = sprites;
            capacity = Mathf.Max(1, boltCapacity);
            Allocate();
        }

        private void Awake() => Allocate();

        private void Allocate()
        {
            if (_bolts != null && _bolts.Length == capacity) return;
            _bolts = new Projectile[capacity];
            _particles = new ParticleSystem.Particle[capacity * ParticlesPerBolt];
            _count = 0;
        }

        /// <summary>
        /// Fires one volley: a bolt from <paramref name="origin"/> (ground point under the boss) along each direction.
        /// Returns the bolts fired (a full pool fires fewer). One volley hits the Knight once at most.
        /// </summary>
        public int FireVolley(Vector3 origin, Vector3[] directions, int count, float speed, float distance, float damage, float hitRadius)
        {
            Allocate();
            _volley++;
            int fired = 0;
            Vector3 start = new Vector3(origin.x, origin.y + FlightHeight, origin.z);
            for (int i = 0; i < count && _count < capacity; i++)
            {
                Vector3 dir = directions[i];
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) continue;
                _bolts[_count++] = new Projectile
                {
                    Position = start,
                    Velocity = dir.normalized * speed,
                    Left = distance,
                    Damage = damage,
                    HitRadius = hitRadius,
                    Volley = _volley,
                };
                fired++;
            }
            return fired;
        }

        /// <summary>Removes every bolt (boss died, fight over).</summary>
        public void Clear()
        {
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
                ref var b = ref _bolts[i];
                Vector3 step = b.Velocity * dt;
                float stepLength = step.magnitude;
                Vector3 from = b.Position;
                b.Position += step;
                b.Left -= stepLength;

                if (canHit && b.Volley != _hitVolley && SegmentDistance(from, b.Position, knight) <= b.HitRadius)
                {
                    _hitVolley = b.Volley;
                    float dealt = player.TakeDamage(b.Damage);
                    HitPlayer?.Invoke(b.Position, dealt);
                    Remove(i);
                    canHit = !player.IsDead;
                    continue;
                }
                if (walk != null && walk.IsBlocked(b.Position, BlockRadius))
                {
                    Blocked?.Invoke(b.Position);
                    Remove(i);
                    continue;
                }
                if (b.Left <= 0f) Remove(i);
            }
        }

        private void Remove(int i) => _bolts[i] = _bolts[--_count];

        /// <summary>Planar distance from <paramref name="p"/> to the segment a-b (fast bolts cannot skip the Knight).</summary>
        public static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float len2 = abx * abx + abz * abz;
            float t = len2 > 1e-6f ? Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / len2) : 0f;
            float dx = a.x + abx * t - p.x, dz = a.z + abz * t - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void Draw()
        {
            if (bodies == null || (_count == 0 && _drawn == 0)) return;
            float t = Time.time;
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                ref readonly var b = ref _bolts[i];
                Vector3 back = b.Velocity.sqrMagnitude > 0.0001f ? -b.Velocity.normalized : Vector3.zero;
                float flicker = 1f + 0.08f * Mathf.Sin(t * 37f + i * 1.7f);
                for (int g = 0; g < ParticlesPerBolt; g++)
                {
                    ref var p = ref _particles[n++];
                    float k = 1f - g * 0.3f;
                    var c = headColor;
                    c.a *= g == 0 ? 1f : 0.55f * k;
                    p.position = b.Position + back * (ghostSpacing * g);
                    p.velocity = Vector3.zero;
                    p.rotation = 0f;
                    p.startSize = size * k * flicker;
                    p.startColor = c;
                    p.startLifetime = 1000f;
                    p.remainingLifetime = 1000f;
                }
            }
            bodies.SetParticles(_particles, n);
            _drawn = n;
        }
    }
}
