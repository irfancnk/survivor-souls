using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Holy Tomes" (KAN-123, Rare, 8 levels): <see cref="StatId.TomeCount"/> glowing books circle the Knight at chest
    /// height, evenly spaced, on a <see cref="StatId.TomeRadius"/> m orbit that turns once per <see cref="StatId.TomeSpin"/>
    /// s, and hit every foe they pass through for <see cref="StatId.TomeDamage"/> x the bolt damage (the same input as the
    /// bolts, so damage skills help the tomes; no crits). The same book hits the same foe at most once per
    /// <see cref="hitCooldown"/> s (a fixed ring of recent hits per book, no alloc). <see cref="StatId.TomeKnockback"/> m/s
    /// pushes the foe out (level 7), <see cref="StatId.TomeFlash"/> m splashes <see cref="flashShare"/> of the hit round it
    /// (level 8). Works on bosses (no push). Hit test: distance on the ground plane to every living foe
    /// (<see cref="Enemy.Active"/>), no physics. The books hide (and stop hitting) while the Knight is dead, in a Chamber
    /// transition and while the boss intro holds the Knight.
    /// Look: <see cref="tomes"/> are child renderers of one shared mesh + material (SRP batched); one shared world-space
    /// system (<see cref="glow"/>, Emit only, alpha blend so the gold reads on the Realm 1 snow) for the trail motes, the
    /// hit sparks and the holy flash. Static events drive the sound (TomeSounds). No GC per frame.
    /// </summary>
    public sealed class OrbitingTomesEffect : SkillEffect
    {
        [Tooltip("Book renderers (children). The level's TomeCount shows the first N; the rest stay hidden.")]
        [SerializeField] private Transform[] tomes = new Transform[0];
        [Tooltip("Orbit height above the Knight's feet (chest).")]
        [SerializeField, Min(0f)] private float height = 1.15f;
        [Tooltip("Hit radius of one book on the ground plane (added to the foe's own radius).")]
        [SerializeField, Min(0.05f)] private float tomeRadius = 0.4f;
        [Tooltip("Seconds before the same book can hit the same foe again.")]
        [SerializeField, Min(0.05f)] private float hitCooldown = 0.5f;
        [Tooltip("Holy flash damage share of the tome hit (level 8 splash).")]
        [SerializeField, Range(0f, 1f)] private float flashShare = 0.5f;

        [Header("Look")]
        [SerializeField] private ParticleSystem glow;
        [Tooltip("Cover tilt out of the orbit plane, degrees (the top-down camera sees the cover).")]
        [SerializeField, Range(0f, 90f)] private float tilt = 55f;
        [SerializeField, Min(0f)] private float bobHeight = 0.08f;
        [SerializeField, Min(0f)] private float bobRate = 2.4f;
        [Tooltip("Seconds a book takes to grow in when it appears.")]
        [SerializeField, Min(0.01f)] private float growTime = 0.25f;
        [Tooltip("Trail motes per second per book.")]
        [SerializeField, Min(0f)] private float trailRate = 22f;
        [SerializeField, Min(0)] private int hitSparks = 7;

        /// <summary>A tome hit a foe (sound): the foe and the hit point. Raised after the damage.</summary>
        public static event Action<Enemy, Vector3> TomeHit;
        /// <summary>Books on show (sound: the quiet page whoosh loop follows it). 0 while hidden.</summary>
        public static int VisibleTomes { get; private set; }

        /// <summary>Most books one effect can show (the prefab has this many children at most).</summary>
        public const int MaxTomes = 8;
        private const int HitSlots = 24; // recent hits remembered per book (more foes than this inside one book never happens)

        private static readonly Color TrailColour = new Color(1f, 0.82f, 0.38f, 0.75f);
        private static readonly Color SparkColour = new Color(1f, 0.9f, 0.5f, 1f);
        private static readonly Color FlashColour = new Color(1f, 0.95f, 0.72f, 0.85f);

        private readonly Enemy[] _hitEnemy = new Enemy[MaxTomes * HitSlots];
        private readonly float[] _hitUntil = new float[MaxTomes * HitSlots];
        private readonly int[] _hitNext = new int[MaxTomes];
        private readonly float[] _grow = new float[MaxTomes];
        private readonly Vector3[] _position = new Vector3[MaxTomes];
        private float _angle;
        private float _clock;
        private float _trailDebt;
        private bool _shown;

        /// <summary>Books at the current level (clamped to the prefab's books).</summary>
        public int Count => Mathf.Clamp(Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.TomeCount, 0f)), 0,
            Mathf.Min(MaxTomes, tomes != null ? tomes.Length : 0));
        /// <summary>Orbit radius (m).</summary>
        public float Radius => Mathf.Max(0.5f, PlayerStats.Eval(Context?.Stats, StatId.TomeRadius, 0f));
        /// <summary>Seconds per full turn.</summary>
        public float SecondsPerTurn => Mathf.Max(0.2f, PlayerStats.Eval(Context?.Stats, StatId.TomeSpin, 0f));
        /// <summary>Hit damage share of the bolt damage.</summary>
        public float DamageShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.TomeDamage, 0f));
        /// <summary>Knockback speed of a hit (m/s, 0 = none).</summary>
        public float Knockback => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.TomeKnockback, 0f));
        /// <summary>Holy flash splash radius (m, 0 = none).</summary>
        public float FlashRadius => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.TomeFlash, 0f));
        /// <summary>Damage of one tome hit: bolt damage (with damage skills) x <see cref="DamageShare"/>.</summary>
        public float Damage => (Context?.Shooter != null ? Context.Shooter.CurrentDamage : 100f) * DamageShare;
        public float HitCooldown => hitCooldown;
        public float TomeRadius => tomeRadius;
        public float Height => height;
        public float FlashShare => flashShare;
        /// <summary>Orbit angle of book 0 (radians).</summary>
        public float Angle => _angle;
        /// <summary>True while the books are hidden (dead, Chamber transition, boss intro).</summary>
        public bool Hidden { get; private set; }
        /// <summary>Tome hits this run (tests, dev log).</summary>
        public int HitCount { get; private set; }
        /// <summary>Holy flash splash hits this run (tests).</summary>
        public int FlashHits { get; private set; }
        public Transform[] Tomes => tomes;

        protected override void Subscribe() { }

        protected override void Unsubscribe()
        {
            if (_shown) VisibleTomes = 0;
            _shown = false;
            ShowBooks(0);
        }

        private void OnDestroy()
        {
            if (_shown) VisibleTomes = 0;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>World position of book <paramref name="index"/> of <paramref name="count"/> at the current angle (no bob).</summary>
        public Vector3 BookPosition(int index, int count)
        {
            Vector3 centre = Context?.Player != null ? Context.Player.position : transform.position;
            float a = _angle + index * Mathf.PI * 2f / Mathf.Max(1, count);
            float r = Radius;
            return centre + new Vector3(Mathf.Cos(a) * r, height, Mathf.Sin(a) * r);
        }

        /// <summary>Turns the orbit, places the books and hits what they pass, for <paramref name="dt"/> scaled s. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (Context == null) return;
            int count = Count;
            Hidden = count == 0 || ShouldHide();
            if (Hidden)
            {
                if (_shown) { _shown = false; VisibleTomes = 0; ShowBooks(0); }
                return;
            }
            if (!_shown) { _shown = true; ShowBooks(count); }
            VisibleTomes = count;
            _clock += dt;
            _angle = Mathf.Repeat(_angle + dt * Mathf.PI * 2f / SecondsPerTurn, Mathf.PI * 2f);
            PlaceBooks(count, dt);
            if (dt > 0f) HitFoes(count);
            EmitTrail(count, dt);
        }

        private bool ShouldHide()
        {
            var health = Context.Health;
            if (health != null && health.IsDead) return true;
            var transition = ChamberTransition.Active;
            if (transition != null && transition.Current != ChamberTransition.Phase.Idle) return true;
            var boss = BossFight.Active;
            return boss != null && boss.KnightHeld;
        }

        private void ShowBooks(int count)
        {
            if (tomes == null) return;
            for (int i = 0; i < tomes.Length; i++)
            {
                if (tomes[i] == null) continue;
                bool on = i < count;
                if (tomes[i].gameObject.activeSelf != on) tomes[i].gameObject.SetActive(on);
                if (!on && i < MaxTomes) _grow[i] = 0f;
            }
        }

        private void PlaceBooks(int count, float dt)
        {
            float step = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                var book = tomes[i];
                if (book == null) continue;
                if (!book.gameObject.activeSelf) book.gameObject.SetActive(true);
                Vector3 p = BookPosition(i, count);
                _position[i] = p;
                float a = _angle + i * step;
                p.y += Mathf.Sin(_clock * bobRate * Mathf.PI * 2f + i * 1.7f) * bobHeight;
                // Facing along the orbit (tangent), cover rolled outward and up so the camera reads it as a book.
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                var rot = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, -tilt);
                _grow[i] = Mathf.Min(1f, _grow[i] + dt / growTime);
                float g = _grow[i];
                float scale = g < 1f ? EaseOutBack(g) : 1f;
                book.SetPositionAndRotation(p, rot);
                book.localScale = new Vector3(scale, scale, scale);
            }
        }

        private void HitFoes(int count)
        {
            float now = _clock;
            var enemies = Enemy.Active;
            float damage = Damage;
            if (damage <= 0f) return;
            float knock = Knockback;
            Vector3 centre = Context.Player != null ? Context.Player.position : transform.position;
            for (int b = 0; b < count; b++)
            {
                Vector3 p = _position[b];
                for (int i = enemies.Count - 1; i >= 0; i--)
                {
                    if (i >= enemies.Count) continue; // a kill this frame shrank the list
                    var e = enemies[i];
                    if (e == null || !e.IsAlive) continue;
                    Vector3 ep = e.transform.position;
                    float dx = ep.x - p.x, dz = ep.z - p.z, reach = tomeRadius + e.HitRadius;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    if (OnCooldown(b, e, now)) continue;
                    Remember(b, e, now);
                    Vector3 away = new Vector3(ep.x - centre.x, 0f, ep.z - centre.z);
                    Vector3 point = new Vector3(ep.x, p.y, ep.z) - new Vector3(dx, 0f, dz) * 0.5f;
                    Hit(e, damage, away, knock, point);
                    float flash = FlashRadius;
                    if (flash > 0f) Flash(e, point, flash, damage * flashShare);
                }
            }
        }

        private void Hit(Enemy e, float damage, Vector3 away, float knock, Vector3 point)
        {
            e.TakeDamage(damage, away, DamageKind.Burst);
            if (knock > 0f && e.IsAlive) e.Knock(away, knock);
            HitCount++;
            EmitSparks(point);
            TomeHit?.Invoke(e, point);
        }

        /// <summary>Level 8 holy flash: <paramref name="damage"/> to every other living foe within <paramref name="radius"/> of the hit.</summary>
        private void Flash(Enemy struck, Vector3 point, float radius, float damage)
        {
            EmitFlash(point, radius);
            if (damage <= 0f) return;
            var enemies = Enemy.Active;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (i >= enemies.Count) continue;
                var e = enemies[i];
                if (e == null || e == struck || !e.IsAlive) continue;
                Vector3 ep = e.transform.position;
                float dx = ep.x - point.x, dz = ep.z - point.z, reach = radius + e.HitRadius;
                if (dx * dx + dz * dz > reach * reach) continue;
                e.TakeDamage(damage, new Vector3(dx, 0f, dz), DamageKind.Burst);
                FlashHits++;
            }
        }

        private bool OnCooldown(int book, Enemy e, float now)
        {
            int start = book * HitSlots;
            for (int k = 0; k < HitSlots; k++)
                if (_hitEnemy[start + k] == e && now < _hitUntil[start + k]) return true;
            return false;
        }

        private void Remember(int book, Enemy e, float now)
        {
            int start = book * HitSlots;
            int slot = -1;
            for (int k = 0; k < HitSlots; k++)
            {
                if (_hitEnemy[start + k] == e || now >= _hitUntil[start + k]) { slot = k; break; }
            }
            if (slot < 0)
            {
                slot = _hitNext[book];
                _hitNext[book] = (slot + 1) % HitSlots;
            }
            _hitEnemy[start + slot] = e;
            _hitUntil[start + slot] = now + hitCooldown;
        }

        private void EmitTrail(int count, float dt)
        {
            if (glow == null || trailRate <= 0f || dt <= 0f) return;
            _trailDebt += trailRate * dt;
            int per = (int)_trailDebt;
            if (per <= 0) return;
            _trailDebt -= per;
            if (per > 3) per = 3; // a hitch never dumps a burst
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            float step = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                if (tomes[i] == null) continue;
                float a = _angle + i * step;
                Vector3 back = new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a)); // against the orbit direction
                Vector3 at = tomes[i].position;
                for (int k = 0; k < per; k++)
                {
                    p.position = at + back * UnityEngine.Random.Range(0.05f, 0.25f) + UnityEngine.Random.insideUnitSphere * 0.1f;
                    p.velocity = back * 0.4f + Vector3.up * UnityEngine.Random.Range(0.05f, 0.25f);
                    p.startSize = UnityEngine.Random.Range(0.1f, 0.18f);
                    p.startLifetime = UnityEngine.Random.Range(0.3f, 0.45f);
                    p.startColor = TrailColour;
                    glow.Emit(p, 1);
                }
            }
        }

        private void EmitSparks(Vector3 point)
        {
            if (glow == null) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            p.position = point;
            p.velocity = Vector3.zero;
            p.startSize = 0.7f;
            p.startLifetime = 0.12f;
            p.startColor = FlashColour;
            glow.Emit(p, 1);
            for (int i = 0; i < hitSparks; i++)
            {
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.7f + 0.2f;
                p.position = point;
                p.velocity = dir.normalized * UnityEngine.Random.Range(2f, 3.8f);
                p.startSize = UnityEngine.Random.Range(0.08f, 0.14f);
                p.startLifetime = UnityEngine.Random.Range(0.2f, 0.35f);
                p.startColor = SparkColour;
                glow.Emit(p, 1);
            }
        }

        private void EmitFlash(Vector3 point, float radius)
        {
            if (glow == null) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            p.position = point;
            p.velocity = Vector3.zero;
            p.startSize = radius * 2f;
            p.startLifetime = 0.2f;
            p.startColor = FlashColour;
            glow.Emit(p, 1);
            for (int i = 0; i < 10; i++)
            {
                float a = (i + UnityEngine.Random.value * 0.4f) * Mathf.PI * 0.2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                p.position = point + dir * (radius * 0.3f);
                p.velocity = dir * (radius * 3f) + Vector3.up * 0.4f;
                p.startSize = UnityEngine.Random.Range(0.12f, 0.2f);
                p.startLifetime = 0.28f;
                p.startColor = SparkColour;
                glow.Emit(p, 1);
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
