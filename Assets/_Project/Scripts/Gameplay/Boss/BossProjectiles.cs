using System;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Boss fan bolts (KAN-61), nova rings and spirals (KAN-116), homing orbs (KAN-121: a bolt with a turn rate steers
    /// towards the living Knight, at most that many degrees per second, at its own speed). No GameObject per bolt: a struct array (fixed capacity, swap-remove), drawn as billboards
    /// of ONE particle system through <see cref="ParticleSystem.SetParticles(ParticleSystem.Particle[], int)"/>: a hot
    /// ember head and two fading ghosts behind it (a cheap trail). One draw call for any count, no lights, no allocation.
    /// <list type="bullet">
    /// <item>Bolts fly straight at chest height on scaled time (they pause with the level-up cards).</item>
    /// <item><see cref="Enemy.Walkability"/> stops them: the arena pillars give cover, the ring wall ends them.</item>
    /// <item>A bolt hits the Knight inside its hit radius (planar). One volley hits at most once, so a close fan can never
    /// stack its bolts into an instant kill. Nothing hits a dead Knight.</item>
    /// </list>
    /// On the "Boss" object (BossBuilder); <see cref="BossFight"/> fires the volleys and clears them when the boss dies.
    /// KAN-239 movers: a <b>boomerang</b> flies out, slows to a stop at its range and comes back the same way to where it left
    /// (a small sideways bow that stays inside its warned lane); a <b>sine</b> bolt weaves across its lane; a <b>bounce</b>
    /// bolt (rolling boulder) turns off pillars and the ring wall a few times instead of breaking. Boomerang and sine follow
    /// a closed-form path of their age (<see cref="BoomerangPoint"/>, <see cref="SinePoint"/>), so tests and the warning
    /// lanes know exactly where they go. Per-boss look (<see cref="SetLook"/>): material, colour, size, trail and spin
    /// (cleaver, fireball, boulder, skull); one look per fight, so still one draw call.
    /// </summary>
    public sealed class BossProjectiles : MonoBehaviour
    {
        /// <summary>KAN-239: how a bolt moves (straight and homing bolts are <see cref="Straight"/>; homing = a turn rate).</summary>
        public enum Mover : byte
        {
            Straight = 0,
            Boomerang = 1,
            Sine = 2,
            Bounce = 3,
        }

        /// <summary>KAN-239: per-boss bolt look (index into the builder's look list; serialized as an int in BossData).</summary>
        public enum BoltLook
        {
            Ember = 0,
            Cleaver = 1,
            Fireball = 2,
            Boulder = 3,
            Skull = 4,
        }

        /// <summary>KAN-239: one bolt look. A missing material keeps the current one.</summary>
        [Serializable]
        public struct LookStyle
        {
            public Material material;
            public Color color;
            [Min(0.05f)] public float size;
            [Tooltip("Spacing of the trail ghosts behind the head (m). 0 = no trail.")]
            [Min(0f)] public float ghostSpacing;
            [Tooltip("Alpha of the first trail ghost (the second gets less).")]
            [Range(0f, 1f)] public float ghostAlpha;
            [Tooltip("Degrees per second the sprite turns (a spinning cleaver).")]
            public float spin;
            [Tooltip("The sprite turns with the ground speed instead (a rolling boulder).")]
            public bool roll;
        }
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
            public float Turn;     // radians per second towards the Knight (0 = straight)
            public int Volley;
            public Mover Kind;     // KAN-239
            public Vector3 Origin; // KAN-239: boomerang / sine: start point (flight height)
            public Vector3 Dir;    // unit ground direction of the path
            public float Speed;
            public float Range;    // boomerang: turn point (m); sine: path length (m)
            public float Wave;     // boomerang: sideways bow (m); sine: amplitude (m)
            public float WaveLength; // sine: metres per full wave
            public float Phase;    // sine: start phase (radians)
            public float Age;      // seconds in flight
            public int Bounces;    // bounce: turns left off walls / pillars
            public float Roll;     // sprite rotation (degrees)
        }

        [SerializeField] private PlayerHealth player;
        [Tooltip("Bolt sprites: emission off, driven by SetParticles. maxParticles >= capacity x 3.")]
        [SerializeField] private ParticleSystem bodies;
        [SerializeField, Min(1)] private int capacity = 32;
        [SerializeField, Min(0.05f)] private float size = 0.75f;
        [SerializeField] private Color headColor = Color.white;
        [Tooltip("Spacing of the trail ghosts behind the head (m).")]
        [SerializeField, Min(0f)] private float ghostSpacing = 0.38f;
        [Tooltip("KAN-239: looks by BoltLook index (Ember, Cleaver, Fireball, Boulder, Skull). Empty = the fields above.")]
        [SerializeField] private LookStyle[] looks = new LookStyle[0];

        private Projectile[] _bolts;
        private int _count;
        private int _volley;
        private int _hitVolley = -1;
        private ParticleSystem.Particle[] _particles;
        private int _drawn = -1;
        private float _spin;
        private bool _roll;
        private float _ghostAlpha = 0.55f;
        private ParticleSystemRenderer _renderer;

        /// <summary>A bolt hit the Knight: point, damage dealt.</summary>
        public event Action<Vector3, float> HitPlayer;
        /// <summary>A bolt struck a pillar or the ring wall: point.</summary>
        public event Action<Vector3> Blocked;
        /// <summary>KAN-239: a bounce bolt turned off a pillar or the ring wall: point.</summary>
        public event Action<Vector3> Bounced;

        public int Count => _count;
        public int Capacity => capacity;
        public Vector3 PositionOf(int i) => _bolts[i].Position;
        public Vector3 VelocityOf(int i) => _bolts[i].Velocity;
        public Mover KindOf(int i) => _bolts[i].Kind;
        public int BouncesLeftOf(int i) => _bolts[i].Bounces;
        public float RollOf(int i) => _bolts[i].Roll;
        /// <summary>KAN-239: the look in use (<see cref="SetLook"/>).</summary>
        public BoltLook Look { get; private set; }
        /// <summary>KAN-239: sprite size of the look in use (m).</summary>
        public float Size => size;
        public Color HeadColor => headColor;
        /// <summary>KAN-239: looks the builder set (by <see cref="BoltLook"/> index).</summary>
        public int LookCount => looks != null ? looks.Length : 0;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(PlayerHealth knight, ParticleSystem sprites = null, int boltCapacity = 32)
        {
            player = knight;
            bodies = sprites;
            capacity = Mathf.Max(1, boltCapacity);
            Allocate();
        }

        private void Awake()
        {
            Allocate();
            if (looks != null && looks.Length > 0) SetLook(BoltLook.Ember);
        }

        /// <summary>
        /// KAN-239: the bolt look of this fight (the boss's <see cref="BossData.BoltLook"/>). Unknown or unset looks keep the
        /// serialized fields. Swaps the one renderer's material: still one draw call.
        /// </summary>
        public void SetLook(BoltLook look)
        {
            Look = look;
            int i = (int)look;
            if (looks == null || i < 0 || i >= looks.Length) return;
            var style = looks[i];
            if (style.size > 0f) size = style.size;
            headColor = style.color;
            ghostSpacing = style.ghostSpacing;
            _ghostAlpha = style.ghostAlpha;
            _spin = style.spin;
            _roll = style.roll;
            if (style.material == null || bodies == null) return;
            if (_renderer == null) _renderer = bodies.GetComponent<ParticleSystemRenderer>();
            if (_renderer != null && _renderer.sharedMaterial != style.material) _renderer.sharedMaterial = style.material;
        }

        /// <summary>Tests and tools: looks without the builder.</summary>
        public void SetLooks(LookStyle[] styles) => looks = styles ?? new LookStyle[0];

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
            NewVolley();
            int fired = 0;
            for (int i = 0; i < count; i++)
                if (Fire(origin, directions[i], speed, distance, damage, hitRadius)) fired++;
            return fired;
        }

        /// <summary>
        /// Starts a new volley: the bolts <see cref="Fire"/> adds from now on share one hit (KAN-116: a whole spiral is one
        /// volley, so its stream of bolts never stacks into an instant kill).
        /// </summary>
        public void NewVolley()
        {
            Allocate();
            _volley++;
        }

        /// <summary>
        /// One bolt into the current volley from <paramref name="origin"/> (ground point under the boss) along
        /// <paramref name="direction"/>. <paramref name="turnDegrees"/> above 0 makes it a homing orb (KAN-121). False when
        /// the pool is full or the direction is zero.
        /// </summary>
        public bool Fire(Vector3 origin, Vector3 direction, float speed, float distance, float damage, float hitRadius,
            float turnDegrees = 0f)
        {
            Allocate();
            direction.y = 0f;
            if (_count >= capacity || direction.sqrMagnitude < 0.0001f) return false;
            _bolts[_count++] = new Projectile
            {
                Position = new Vector3(origin.x, origin.y + FlightHeight, origin.z),
                Velocity = direction.normalized * speed,
                Left = distance,
                Damage = damage,
                HitRadius = hitRadius,
                Turn = Mathf.Max(0f, turnDegrees) * Mathf.Deg2Rad,
                Volley = _volley,
                Kind = Mover.Straight,
                Speed = speed,
            };
            return true;
        }

        /// <summary>
        /// KAN-239: a boomerang into the current volley: out along <paramref name="direction"/>, slowing to a stop at
        /// <paramref name="distance"/> m, then back the same way to the start (<see cref="BoomerangTime"/> s in all), bowing
        /// <paramref name="bend"/> m to the right of the line (negative = left). False when the pool is full.
        /// </summary>
        public bool FireBoomerang(Vector3 origin, Vector3 direction, float speed, float distance, float bend, float damage,
            float hitRadius) =>
            FirePath(Mover.Boomerang, origin, direction, speed, distance, bend, 0f, 0f, damage, hitRadius);

        /// <summary>
        /// KAN-239: a sine bolt into the current volley: along <paramref name="direction"/> for <paramref name="distance"/> m,
        /// weaving <paramref name="amplitude"/> m to each side, one full wave per <paramref name="wavelength"/> m, starting
        /// at <paramref name="phase"/> radians (0 = on the line, heading right).
        /// </summary>
        public bool FireSine(Vector3 origin, Vector3 direction, float speed, float distance, float amplitude, float wavelength,
            float phase, float damage, float hitRadius) =>
            FirePath(Mover.Sine, origin, direction, speed, distance, amplitude, Mathf.Max(0.5f, wavelength), phase, damage,
                hitRadius);

        /// <summary>
        /// KAN-239: a bounce bolt (rolling boulder) into the current volley: straight, but a pillar or the ring wall turns it
        /// (mirror) up to <paramref name="bounces"/> times; the next one breaks it. <paramref name="distance"/> = the whole
        /// path, bounces included.
        /// </summary>
        public bool FireBounce(Vector3 origin, Vector3 direction, float speed, float distance, int bounces, float damage,
            float hitRadius)
        {
            if (!Fire(origin, direction, speed, distance, damage, hitRadius)) return false;
            ref var b = ref _bolts[_count - 1];
            b.Kind = Mover.Bounce;
            b.Bounces = Mathf.Max(0, bounces);
            return true;
        }

        private bool FirePath(Mover kind, Vector3 origin, Vector3 direction, float speed, float distance, float wave,
            float wavelength, float phase, float damage, float hitRadius)
        {
            if (speed <= 0f || distance <= 0f || !Fire(origin, direction, speed, distance, damage, hitRadius)) return false;
            ref var b = ref _bolts[_count - 1];
            b.Kind = kind;
            b.Origin = b.Position;
            b.Dir = b.Velocity / speed;
            b.Range = distance;
            b.Wave = wave;
            b.WaveLength = wavelength;
            b.Phase = phase;
            b.Left = float.MaxValue; // the path ends by age (boomerang) or by range (sine)
            if (kind == Mover.Sine) b.Position = SinePoint(b.Origin, b.Dir, speed, wave, wavelength, phase, 0f);
            return true;
        }

        /// <summary>KAN-239: seconds a boomerang flies (out and back): 4 x range / launch speed.</summary>
        public static float BoomerangTime(float speed, float distance) => speed > 0f ? 4f * distance / speed : 0f;

        /// <summary>
        /// KAN-239: where a boomerang is <paramref name="t"/> s after it left <paramref name="origin"/>: constant pull back
        /// (stops at <paramref name="distance"/> m at half time), plus a sideways bow of <paramref name="bend"/> m (sine over
        /// the whole flight: widest at the turn point, zero at both ends).
        /// </summary>
        public static Vector3 BoomerangPoint(Vector3 origin, Vector3 dir, float speed, float distance, float bend, float t)
        {
            float total = BoomerangTime(speed, distance);
            if (total <= 0f) return origin;
            t = Mathf.Clamp(t, 0f, total);
            float pull = speed * speed / (2f * distance);
            float along = speed * t - 0.5f * pull * t * t;
            float side = bend * Mathf.Sin(Mathf.PI * t / total);
            return origin + dir * along + new Vector3(dir.z, 0f, -dir.x) * side;
        }

        /// <summary>KAN-239: where a sine bolt is <paramref name="t"/> s after it left <paramref name="origin"/>.</summary>
        public static Vector3 SinePoint(Vector3 origin, Vector3 dir, float speed, float amplitude, float wavelength, float phase,
            float t)
        {
            float along = speed * t;
            float side = amplitude * Mathf.Sin(2f * Mathf.PI * along / Mathf.Max(0.01f, wavelength) + phase);
            return origin + dir * along + new Vector3(dir.z, 0f, -dir.x) * side;
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
                if (b.Turn > 0f && canHit) Steer(ref b, knight, dt);
                Vector3 from = b.Position;
                b.Age += dt;
                bool ended = false;
                if (b.Kind == Mover.Boomerang || b.Kind == Mover.Sine)
                {
                    if (b.Kind == Mover.Boomerang)
                    {
                        float total = BoomerangTime(b.Speed, b.Range);
                        b.Position = BoomerangPoint(b.Origin, b.Dir, b.Speed, b.Range, b.Wave, b.Age);
                        ended = b.Age >= total; // back where it left
                    }
                    else
                    {
                        b.Position = SinePoint(b.Origin, b.Dir, b.Speed, b.Wave, b.WaveLength, b.Phase, b.Age);
                        ended = b.Speed * b.Age >= b.Range;
                    }
                    b.Velocity = (b.Position - from) / dt;
                }
                else
                {
                    Vector3 step = b.Velocity * dt;
                    b.Position += step;
                    b.Left -= step.magnitude;
                }
                Spin(ref b, dt);

                if (canHit && b.Volley != _hitVolley && SegmentDistance(from, b.Position, knight) <= b.HitRadius)
                {
                    _hitVolley = b.Volley;
                    float dealt = player.TakeDamage(b.Damage, b.Position - b.Velocity); // KAN-187: hit direction
                    HitPlayer?.Invoke(b.Position, dealt);
                    Remove(i);
                    canHit = !player.IsDead;
                    continue;
                }
                if (walk != null && walk.IsBlocked(b.Position, BlockRadius))
                {
                    if (b.Kind == Mover.Bounce && b.Bounces > 0 && Reflect(ref b, from, walk))
                    {
                        Bounced?.Invoke(b.Position);
                        continue;
                    }
                    Blocked?.Invoke(b.Position);
                    Remove(i);
                    continue;
                }
                if (ended || b.Left <= 0f) Remove(i);
            }
        }

        /// <summary>
        /// KAN-239: a bounce bolt that ran into a wall goes back to where it was this frame and mirrors the velocity part
        /// that hit: the x part when moving only along x is blocked, the z part when only z is, both in a corner. False when
        /// the bolt is stuck (even its old point is blocked).
        /// </summary>
        private static bool Reflect(ref Projectile b, Vector3 from, IWalkability walk)
        {
            if (walk.IsBlocked(from, BlockRadius)) return false;
            Vector3 step = b.Position - from;
            bool xBlocked = walk.IsBlocked(new Vector3(from.x + step.x, from.y, from.z), BlockRadius);
            bool zBlocked = walk.IsBlocked(new Vector3(from.x, from.y, from.z + step.z), BlockRadius);
            if (!xBlocked && !zBlocked) xBlocked = zBlocked = true; // a corner tip: straight back
            var v = b.Velocity;
            if (xBlocked) v.x = -v.x;
            if (zBlocked) v.z = -v.z;
            b.Velocity = v;
            b.Position = from;
            b.Bounces--;
            return true;
        }

        /// <summary>KAN-239: sprite turn of the look (spin per second, or rolling with the ground speed).</summary>
        private void Spin(ref Projectile b, float dt)
        {
            if (_roll)
            {
                float speed = Mathf.Sqrt(b.Velocity.x * b.Velocity.x + b.Velocity.z * b.Velocity.z);
                b.Roll += speed * dt / Mathf.Max(0.05f, size * 0.5f) * Mathf.Rad2Deg;
            }
            else b.Roll += _spin * dt;
            if (b.Roll > 3600f || b.Roll < -3600f) b.Roll %= 360f;
        }

        /// <summary>
        /// Turns a homing orb's ground velocity towards the Knight by at most its turn rate (speed kept). Yaw maths, not
        /// RotateTowards: a Knight right behind the orb must not tip it out of the ground plane.
        /// </summary>
        private static void Steer(ref Projectile b, Vector3 knight, float dt)
        {
            float tx = knight.x - b.Position.x, tz = knight.z - b.Position.z;
            if (tx * tx + tz * tz < 0.0001f) return;
            float speed = Mathf.Sqrt(b.Velocity.x * b.Velocity.x + b.Velocity.z * b.Velocity.z);
            float yaw = Mathf.Atan2(b.Velocity.x, b.Velocity.z);
            float delta = Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, Mathf.Atan2(tx, tz) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            yaw += Mathf.Clamp(delta, -b.Turn * dt, b.Turn * dt);
            b.Velocity = new Vector3(Mathf.Sin(yaw) * speed, 0f, Mathf.Cos(yaw) * speed);
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
                    c.a *= g == 0 ? 1f : ghostSpacing > 0f ? _ghostAlpha * k : 0f;
                    p.position = b.Position + back * (ghostSpacing * g);
                    p.velocity = Vector3.zero;
                    p.rotation = b.Roll;
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
