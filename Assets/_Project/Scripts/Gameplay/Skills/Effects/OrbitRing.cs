using System.Collections.Generic;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// Eclipse Crown formation (KAN-246): while <see cref="Weight"/> &gt; 0 every orbiter (<see cref="OrbitRing"/> rings and
    /// Holy Tomes) bunches into one sweeping arc at <see cref="Angle"/> on a <see cref="Radius"/> m orbit; at 0 they keep their
    /// own orbits. Written only by <see cref="EclipseCrownEffect"/>; reset on its removal.
    /// </summary>
    public static class EclipseFormation
    {
        /// <summary>0 = own orbits, 1 = full formation.</summary>
        public static float Weight;
        /// <summary>Centre angle of the formation (radians).</summary>
        public static float Angle;
        /// <summary>Formation orbit radius (m).</summary>
        public static float Radius = 2.4f;
        /// <summary>Radians between two neighbouring orbiters in the formation.</summary>
        public const float Spacing = 0.32f;

        /// <summary>
        /// Orbit angle and radius of orbiter <paramref name="index"/> of <paramref name="count"/>, blended from its own
        /// <paramref name="angle"/> / <paramref name="radius"/> toward the formation by <see cref="Weight"/>.
        /// <paramref name="slot"/> offsets one ring's orbiters from another ring's inside the formation.
        /// </summary>
        public static void Apply(int index, int count, int slot, ref float angle, ref float radius)
        {
            float w = Mathf.Clamp01(Weight);
            if (w <= 0f) return;
            float target = Angle + (index - (count - 1) * 0.5f) * Spacing + slot * Spacing * 0.5f;
            angle = Mathf.LerpAngle(angle * Mathf.Rad2Deg, target * Mathf.Rad2Deg, w) * Mathf.Deg2Rad;
            radius = Mathf.Lerp(radius, Radius, w);
        }

        public static void Reset()
        {
            Weight = 0f;
            Angle = 0f;
        }
    }

    /// <summary>
    /// Base of the Ember Boons orbitals (KAN-246): <see cref="Count"/> bodies (child transforms, <see cref="bodies"/>) circle
    /// the Knight at <see cref="Height"/>, evenly spaced, on a <see cref="Radius"/> m orbit that turns once per
    /// <see cref="SecondsPerTurn"/> s. Contact rings (<see cref="HitsOnContact"/>) call <see cref="OnContact"/> for every
    /// living foe within reach of a body, at most once per <see cref="hitCooldown"/> s per body per foe (a fixed ring of
    /// recent hits per body: no repeated hits in one pass, no allocation). Hidden and harmless while the Knight cannot fight
    /// (<see cref="SkillFx.ShouldHide"/>). Bodies grow in when they appear. Every live ring is in <see cref="Live"/> (Eclipse
    /// Crown counts them and bends them into its formation, <see cref="EclipseFormation"/>). Subclasses read their numbers
    /// from <see cref="PlayerStats"/> and add the skill's own rule.
    /// </summary>
    public abstract class OrbitRing : SkillEffect
    {
        public const int MaxBodies = 6;
        private const int HitSlots = 16;

        /// <summary>Orbit rings on show now (Eclipse Crown).</summary>
        public static readonly List<OrbitRing> Live = new List<OrbitRing>(8);

        [Tooltip("Body renderers (children). The level's count shows the first N; the rest stay hidden.")]
        [SerializeField] protected Transform[] bodies = new Transform[0];
        [SerializeField, Min(0f)] private float height = 1.1f;
        [Tooltip("Contact reach of one body on the ground plane (added to the foe's own radius).")]
        [SerializeField, Min(0.05f)] private float contactRadius = 0.45f;
        [Tooltip("Seconds before the same body can touch the same foe again.")]
        [SerializeField, Min(0.05f)] private float hitCooldown = 0.6f;
        [SerializeField, Min(0.01f)] private float growTime = 0.25f;
        [Tooltip("Base body size (the level may scale it, see BodyScale).")]
        [SerializeField, Min(0.05f)] private float bodyScale = 1f;
        [Tooltip("Shared world-space system for trails and hit sparks (Emit only).")]
        [SerializeField] protected ParticleSystem fx;
        [Tooltip("Trail motes per second per body (0 = none).")]
        [SerializeField, Min(0f)] private float trailRate = 14f;
        [SerializeField] private Color trailColour = Color.white;

        private readonly Enemy[] _hitEnemy = new Enemy[MaxBodies * HitSlots];
        private readonly float[] _hitUntil = new float[MaxBodies * HitSlots];
        private readonly int[] _hitNext = new int[MaxBodies];
        private readonly float[] _grow = new float[MaxBodies];
        private readonly Vector3[] _position = new Vector3[MaxBodies];
        private float _angle;
        private float _clock;
        private float _trailDebt;
        private bool _shown;

        /// <summary>Bodies at the current level (clamped to the prefab's bodies).</summary>
        public int Count => Mathf.Clamp(BodyCount, 0, Mathf.Min(MaxBodies, bodies != null ? bodies.Length : 0));
        /// <summary>Raw body count from the stats.</summary>
        protected abstract int BodyCount { get; }
        /// <summary>Orbit radius (m).</summary>
        public abstract float Radius { get; }
        /// <summary>Seconds per full turn (lower = faster). Negative turns the other way.</summary>
        public abstract float SecondsPerTurn { get; }
        /// <summary>True: <see cref="OnContact"/> runs for foes the bodies touch.</summary>
        protected virtual bool HitsOnContact => true;
        /// <summary>Size multiplier of the bodies at the current level (1 = base).</summary>
        protected virtual float BodyScale => 1f;
        /// <summary>Order of this ring inside the Eclipse formation (rings sit side by side).</summary>
        protected virtual int FormationSlot => 0;

        public float Height => height;
        public float ContactRadius => contactRadius * BodyScale;
        public float HitCooldown => hitCooldown;
        /// <summary>Orbit angle of body 0 (radians, own orbit).</summary>
        public float Angle => _angle;
        /// <summary>True while hidden (dead, Chamber transition, boss intro) or at count 0.</summary>
        public bool Hidden { get; private set; } = true;
        /// <summary>Contacts this run (tests).</summary>
        public int ContactCount { get; private set; }
        public Transform[] Bodies => bodies;

        /// <summary>A foe within reach of body <paramref name="body"/> (cooldown checked). The skill's own rule.</summary>
        protected virtual void OnContact(Enemy enemy, int body, Vector3 point) { }
        /// <summary>After the bodies moved (skill timers: discharges, projectile blocks).</summary>
        protected virtual void AfterMove(float dt, int count) { }

        protected override void Subscribe() { }

        protected override void Unsubscribe()
        {
            Live.Remove(this);
            _shown = false;
            ShowBodies(0);
        }

        protected virtual void OnDestroy() => Live.Remove(this);

        private void Update() => Tick(Time.deltaTime);

        /// <summary>World position of body <paramref name="index"/> of <paramref name="count"/> now (formation included, no bob).</summary>
        public Vector3 BodyPosition(int index, int count)
        {
            Vector3 centre = Context?.Player != null ? Context.Player.position : transform.position;
            float a = BodyAngle(index, count, out float r);
            return centre + new Vector3(Mathf.Cos(a) * r, height, Mathf.Sin(a) * r);
        }

        private float BodyAngle(int index, int count, out float radius)
        {
            float a = _angle + index * Mathf.PI * 2f / Mathf.Max(1, count);
            radius = Radius;
            EclipseFormation.Apply(index, count, FormationSlot, ref a, ref radius);
            return a;
        }

        /// <summary>Turns the orbit, places the bodies and runs the contacts, for <paramref name="dt"/> scaled s. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (Context == null) return;
            int count = Count;
            Hidden = count == 0 || SkillFx.ShouldHide(Context);
            if (Hidden)
            {
                if (_shown) { _shown = false; ShowBodies(0); Live.Remove(this); }
                return;
            }
            if (!_shown) { _shown = true; ShowBodies(count); if (!Live.Contains(this)) Live.Add(this); }
            _clock += dt;
            float turn = SecondsPerTurn;
            if (Mathf.Abs(turn) > 0.05f) _angle = Mathf.Repeat(_angle + dt * Mathf.PI * 2f / turn, Mathf.PI * 2f);
            Place(count, dt);
            if (dt > 0f && HitsOnContact) Contacts(count);
            AfterMove(dt, count);
            Trail(count, dt);
        }

        private void ShowBodies(int count)
        {
            if (bodies == null) return;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                bool on = i < count;
                if (bodies[i].gameObject.activeSelf != on) bodies[i].gameObject.SetActive(on);
                if (!on && i < MaxBodies) _grow[i] = 0f;
            }
        }

        private void Place(int count, float dt)
        {
            float scaleNow = bodyScale * BodyScale;
            for (int i = 0; i < count; i++)
            {
                var body = bodies[i];
                if (body == null) continue;
                if (!body.gameObject.activeSelf) body.gameObject.SetActive(true);
                float a = BodyAngle(i, count, out _);
                Vector3 p = BodyPosition(i, count);
                _position[i] = p;
                float dir = SecondsPerTurn >= 0f ? 1f : -1f;
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * dir;
                _grow[i] = Mathf.Min(1f, _grow[i] + dt / growTime);
                float g = _grow[i];
                float s = (g < 1f ? 1f - (1f - g) * (1f - g) : 1f) * scaleNow;
                body.SetPositionAndRotation(p, BodyRotation(i, a, tangent));
                body.localScale = new Vector3(s, s, s);
            }
        }

        /// <summary>Body facing: along the orbit (tangent) by default. Subclasses add spin or tilt.</summary>
        protected virtual Quaternion BodyRotation(int index, float angle, Vector3 tangent) =>
            Quaternion.LookRotation(tangent, Vector3.up);

        /// <summary>Clock of this ring (s since it first showed), for spin and bob looks.</summary>
        protected float Clock => _clock;

        private void Contacts(int count)
        {
            float now = _clock;
            var enemies = Enemy.Active;
            float reachBase = ContactRadius;
            for (int b = 0; b < count; b++)
            {
                Vector3 p = _position[b];
                for (int i = enemies.Count - 1; i >= 0; i--)
                {
                    if (i >= enemies.Count) continue; // a kill this frame shrank the list
                    var e = enemies[i];
                    if (e == null || !e.IsAlive) continue;
                    Vector3 ep = e.transform.position;
                    float dx = ep.x - p.x, dz = ep.z - p.z, reach = reachBase + e.HitRadius;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    if (OnCooldown(b, e, now)) continue;
                    Remember(b, e, now);
                    ContactCount++;
                    OnContact(e, b, new Vector3(ep.x, p.y, ep.z) - new Vector3(dx, 0f, dz) * 0.5f);
                }
            }
        }

        private bool OnCooldown(int body, Enemy e, float now)
        {
            int start = body * HitSlots;
            for (int k = 0; k < HitSlots; k++)
                if (_hitEnemy[start + k] == e && now < _hitUntil[start + k]) return true;
            return false;
        }

        private void Remember(int body, Enemy e, float now)
        {
            int start = body * HitSlots;
            int slot = -1;
            for (int k = 0; k < HitSlots; k++)
                if (_hitEnemy[start + k] == e || now >= _hitUntil[start + k]) { slot = k; break; }
            if (slot < 0)
            {
                slot = _hitNext[body];
                _hitNext[body] = (slot + 1) % HitSlots;
            }
            _hitEnemy[start + slot] = e;
            _hitUntil[start + slot] = now + hitCooldown;
        }

        private void Trail(int count, float dt)
        {
            if (fx == null || trailRate <= 0f || dt <= 0f) return;
            _trailDebt += trailRate * dt;
            int per = (int)_trailDebt;
            if (per <= 0) return;
            _trailDebt -= per;
            if (per > 3) per = 3;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                if (bodies[i] == null) continue;
                Vector3 at = bodies[i].position;
                for (int k = 0; k < per; k++)
                {
                    p.position = at + Random.insideUnitSphere * 0.12f * BodyScale;
                    p.velocity = Vector3.up * Random.Range(0.05f, 0.3f);
                    p.startSize = Random.Range(0.1f, 0.2f) * BodyScale;
                    p.startLifetime = Random.Range(0.25f, 0.4f);
                    p.startColor = trailColour;
                    fx.Emit(p, 1);
                }
            }
        }
    }
}
