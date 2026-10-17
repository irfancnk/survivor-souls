using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Storm Satellites" (KAN-246, Epic): <see cref="StatId.SatelliteCount"/> charged orbs circle at medium range
    /// (<see cref="radius"/> m). Every <see cref="interval"/> + <see cref="StatId.SatelliteInterval"/> s each orb zaps the
    /// nearest foe within <see cref="zapRange"/> m of it for <see cref="damageShare"/> x the bolt damage; with
    /// <see cref="StatId.SatelliteArc"/> the zap jumps on to that many more foes near the target at half damage. Ranged
    /// damage, no contact. Zaps are secondary damage (<see cref="ProcGuard"/>): they trigger no Storm Touch and no weapon-hit
    /// counter. Look: bright orbs, jagged blue-white arcs (<see cref="arcs"/>).
    /// </summary>
    public sealed class StormSatellitesEffect : OrbitRing
    {
        private const int MaxArcs = 3;

        [SerializeField, Min(0.5f)] private float radius = 3.2f;
        [SerializeField, Min(0.3f)] private float spin = 2.8f;
        [SerializeField, Min(0.1f)] private float interval = 1.6f;
        [SerializeField, Min(0.5f)] private float zapRange = 6f;
        [SerializeField, Min(0.5f)] private float arcRange = 3.5f;
        [SerializeField, Range(0f, 3f)] private float damageShare = 0.6f;
        [SerializeField] private ArcLines arcs;

        private static readonly Color Bolt = new Color(0.7f, 0.85f, 1f, 1f);
        private static readonly Color Spark = new Color(0.85f, 0.95f, 1f, 1f);

        private readonly Enemy[] _struck = new Enemy[MaxArcs + 1];
        private float _timer;

        protected override int BodyCount => Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.SatelliteCount, 0f));
        public override float Radius => radius;
        public override float SecondsPerTurn => spin;
        protected override bool HitsOnContact => false;
        protected override int FormationSlot => 5;
        /// <summary>Seconds between two discharges after skill levels.</summary>
        public float Interval => Mathf.Max(0.25f, PlayerStats.Eval(Context?.Stats, StatId.SatelliteInterval, interval));
        public int ExtraArcs => Mathf.Clamp(Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.SatelliteArc, 0f)), 0, MaxArcs);
        public float ZapRange => zapRange;
        /// <summary>Zaps (first targets) this run (tests).</summary>
        public int Zaps { get; private set; }
        /// <summary>Secondary arc hits this run (tests).</summary>
        public int ArcHits { get; private set; }

        protected override void AfterMove(float dt, int count)
        {
            _timer += dt;
            if (_timer < Interval) return;
            _timer = 0f;
            float damage = SkillFx.BoltShare(Context, damageShare);
            for (int i = 0; i < count; i++)
            {
                if (bodies[i] == null) continue;
                Vector3 from = bodies[i].position;
                var target = SkillFx.Nearest(from, zapRange);
                if (target == null) continue;
                Zap(from, target, damage);
            }
        }

        /// <summary>One discharge from <paramref name="from"/> into <paramref name="target"/> and its extra arcs. Public for tests.</summary>
        public void Zap(Vector3 from, Enemy target, float damage)
        {
            Zaps++;
            int n = 0;
            _struck[n++] = target;
            Strike(from, target, damage);
            int extra = ExtraArcs;
            Vector3 at = target.AimPoint;
            for (int k = 0; k < extra; k++)
            {
                var next = NextNotStruck(target.transform.position, n);
                if (next == null) break;
                _struck[n++] = next;
                Strike(at, next, damage * 0.5f);
                ArcHits++;
                at = next.AimPoint;
            }
            for (int i = 0; i < n; i++) _struck[i] = null;
        }

        private void Strike(Vector3 from, Enemy e, float damage)
        {
            Vector3 to = e.AimPoint;
            if (arcs != null) arcs.Arc(from, to, Bolt, 0.16f);
            SkillFx.Sparks(fx, to, 5, Spark, 2.6f, 0.08f, 0.2f);
            e.Shock(0.15f);
            e.TakeDamage(damage, to - from, DamageKind.Burst);
        }

        private Enemy NextNotStruck(Vector3 from, int count)
        {
            Enemy best = null;
            float bestSq = arcRange * arcRange;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.IsAlive) continue;
                bool seen = false;
                for (int k = 0; k < count; k++) if (_struck[k] == e) { seen = true; break; }
                if (seen) continue;
                Vector3 d = e.transform.position - from;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        protected override Quaternion BodyRotation(int index, float angle, Vector3 tangent) =>
            Quaternion.Euler(Clock * 200f, Clock * 310f, 0f); // crackling orb spin
    }
}
