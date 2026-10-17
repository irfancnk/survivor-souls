using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Eclipse Crown" (KAN-246, Legendary; offered only with an orbital skill owned). Every <see cref="Cooldown"/> s, while
    /// at least one orbital shows (<see cref="OrbitRing.Live"/> or Holy Tomes), all orbiters bend into one sweeping arc
    /// (<see cref="EclipseFormation"/>: <see cref="gather"/> s in, <see cref="sweep"/> s of a fast sweep round the Knight),
    /// then a dark pulse hits every foe within <see cref="Radius"/> m for <see cref="StatId.EclipsePulse"/> x the bolt damage,
    /// +<see cref="perOrbital"/> per extra orbital skill (capped at <see cref="maxOrbitalBonus"/> extra), and the orbiters go
    /// back to their own orbits (<see cref="gather"/> s). Each orbiter keeps its role during the formation (shield fragments
    /// still block, they deal nothing). Look: a dark violet ring and wisps (one shared system).
    /// </summary>
    public sealed class EclipseCrownEffect : SkillEffect
    {
        public enum Phase { Idle, Gather, Sweep, Release }

        [SerializeField, Min(1f)] private float baseCooldown = 14f;
        [SerializeField, Min(0.05f)] private float gather = 0.45f;
        [SerializeField, Min(0.1f)] private float sweep = 1.1f;
        [Tooltip("Radians per second the formation sweeps round the Knight.")]
        [SerializeField] private float sweepSpeed = 7f;
        [SerializeField, Min(0.5f)] private float baseRadius = 4f;
        [SerializeField, Min(0.5f)] private float formationRadius = 2.4f;
        [SerializeField, Range(0f, 1f)] private float perOrbital = 0.25f;
        [SerializeField, Min(0)] private int maxOrbitalBonus = 3;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Ring = new Color(0.42f, 0.12f, 0.7f, 0.9f);
        private static readonly Color Core = new Color(0.85f, 0.7f, 1f, 0.9f);
        private static readonly Color Wisp = new Color(0.25f, 0.05f, 0.4f, 0.85f);

        private float _timer;
        private float _phaseTime;

        public Phase Current { get; private set; }
        public float Cooldown => Mathf.Max(3f, PlayerStats.Eval(Context?.Stats, StatId.EclipseCooldown, baseCooldown));
        public float Radius => Mathf.Max(0.5f, PlayerStats.Eval(Context?.Stats, StatId.EclipseRadius, baseRadius));
        public float PulseShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.EclipsePulse, 0f));
        /// <summary>Pulses released this run (tests).</summary>
        public int Pulses { get; private set; }
        /// <summary>Foes hit by the last pulse (tests).</summary>
        public int LastPulseHits { get; private set; }

        /// <summary>Distinct orbital skills on show now (rings + Holy Tomes).</summary>
        public static int OrbitalSkills => OrbitRing.Live.Count + (OrbitingTomesEffect.VisibleTomes > 0 ? 1 : 0);

        protected override void Subscribe() { }

        protected override void Unsubscribe()
        {
            EclipseFormation.Reset();
            Current = Phase.Idle;
        }

        private void OnDestroy() => EclipseFormation.Reset();

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Cooldown, formation and pulse for <paramref name="dt"/> scaled s. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (Context == null) return;
            if (SkillFx.ShouldHide(Context))
            {
                if (Current != Phase.Idle) { Current = Phase.Idle; EclipseFormation.Reset(); }
                return;
            }
            switch (Current)
            {
                case Phase.Idle:
                    _timer += dt;
                    if (_timer < Cooldown || OrbitalSkills == 0) return;
                    _timer = 0f;
                    Begin();
                    break;
                case Phase.Gather:
                    _phaseTime += dt;
                    EclipseFormation.Weight = Mathf.Clamp01(_phaseTime / gather);
                    EclipseFormation.Angle += sweepSpeed * 0.4f * dt;
                    if (_phaseTime >= gather) { Current = Phase.Sweep; _phaseTime = 0f; }
                    break;
                case Phase.Sweep:
                    _phaseTime += dt;
                    EclipseFormation.Weight = 1f;
                    EclipseFormation.Angle += sweepSpeed * dt;
                    EmitTrail();
                    if (_phaseTime >= sweep) { Pulse(); Current = Phase.Release; _phaseTime = 0f; }
                    break;
                case Phase.Release:
                    _phaseTime += dt;
                    EclipseFormation.Weight = 1f - Mathf.Clamp01(_phaseTime / gather);
                    if (_phaseTime >= gather) { Current = Phase.Idle; EclipseFormation.Reset(); }
                    break;
            }
        }

        /// <summary>Starts the formation now (debug, tests).</summary>
        public void Begin()
        {
            Current = Phase.Gather;
            _phaseTime = 0f;
            EclipseFormation.Radius = formationRadius;
            Vector3 c = Centre;
            var near = SkillFx.Nearest(c, 12f);
            EclipseFormation.Angle = near != null
                ? Mathf.Atan2(near.transform.position.z - c.z, near.transform.position.x - c.x)
                : 0f;
        }

        private Vector3 Centre => Context?.Player != null ? Context.Player.position : transform.position;

        /// <summary>The dark pulse: damage round the Knight, scaled by the orbital skills on show. Public for tests.</summary>
        public void Pulse()
        {
            Pulses++;
            int extra = Mathf.Clamp(OrbitalSkills - 1, 0, maxOrbitalBonus);
            float damage = SkillFx.BoltShare(Context, PulseShare * (1f + perOrbital * extra));
            float r = Radius;
            Vector3 c = Centre;
            int hits = 0;
            var enemies = Enemy.Active;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (i >= enemies.Count) continue;
                var e = enemies[i];
                if (!e.IsAlive) continue;
                Vector3 d = e.transform.position - c;
                d.y = 0f;
                float reach = r + e.HitRadius;
                if (d.sqrMagnitude > reach * reach) continue;
                if (damage > 0f) e.TakeDamage(damage, d, DamageKind.Burst);
                hits++;
            }
            LastPulseHits = hits;
            SkillFx.Flash(fx, c + Vector3.up * 0.8f, 2.5f, Core, 0.2f);
            SkillFx.Ring(fx, c, r, 48, Ring, 0.55f, 0.4f);
            SkillFx.Ring(fx, c, r * 0.6f, 24, Wisp, 0.8f, 0.5f);
        }

        private void EmitTrail()
        {
            if (fx == null) return;
            Vector3 c = Centre;
            float a = EclipseFormation.Angle;
            Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * EclipseFormation.Radius + Vector3.up * 1.1f;
            SkillFx.Rise(fx, p, 0.3f, 0.4f, 2, Wisp, 0.35f, 0.45f);
        }
    }
}
