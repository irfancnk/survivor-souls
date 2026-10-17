using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Storm Touch" (KAN-246, Rare): every weapon hit releases a short electrical arc to <see cref="StatId.ArcTargets"/>
    /// other foes within <see cref="arcRange"/> m (nearest first, never the struck one, never the same foe twice per hit),
    /// each taking <see cref="StatId.ArcDamage"/> x the bolt damage. Arcs are secondary damage (<see cref="ProcGuard"/>):
    /// they never make more arcs and never count as weapon hits. Look: pooled lightning lines (<see cref="arcs"/>) plus a
    /// spark at each end; the arced foe flickers yellow (<see cref="Enemy.Shock"/>).
    /// </summary>
    public sealed class StormTouchEffect : SkillEffect
    {
        private const int MaxTargets = 4;

        [SerializeField, Min(0.5f)] private float arcRange = 4f;
        [SerializeField] private ArcLines arcs;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color ArcColour = new Color(1f, 0.92f, 0.45f, 1f);
        private static readonly Color SparkColour = new Color(1f, 0.95f, 0.6f, 1f);

        private readonly Enemy[] _picked = new Enemy[MaxTargets + 1];
        private System.Action<BoltHit> _onHit;

        public int Targets => Mathf.Clamp(Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.ArcTargets, 0f)), 0, MaxTargets);
        public float DamageShare => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.ArcDamage, 0f));
        public float ArcRange => arcRange;
        /// <summary>Arc hits this run (tests).</summary>
        public int ArcHits { get; private set; }

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
        }

        private void OnHit(BoltHit hit)
        {
            int targets = Targets;
            if (targets <= 0 || hit.Enemy == null) return;
            float damage = SkillFx.BoltShare(Context, DamageShare);
            Vector3 from = hit.Point;
            int picked = 0;
            _picked[picked++] = hit.Enemy;
            for (int n = 0; n < targets; n++)
            {
                var next = NearestNotPicked(hit.Enemy.transform.position, picked);
                if (next == null) break;
                _picked[picked++] = next;
                Vector3 to = next.AimPoint;
                if (arcs != null) arcs.Arc(from, to, ArcColour, 0.16f);
                SkillFx.Sparks(fx, to, 4, SparkColour, 2.5f, 0.08f, 0.2f);
                next.Shock(0.15f);
                next.TakeDamage(damage, to - from, DamageKind.Burst);
                ArcHits++;
            }
            for (int i = 0; i < picked; i++) _picked[i] = null;
        }

        private Enemy NearestNotPicked(Vector3 from, int picked)
        {
            Enemy best = null;
            float bestSq = arcRange * arcRange;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.IsAlive || Picked(e, picked)) continue;
                Vector3 d = e.transform.position - from;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        private bool Picked(Enemy e, int picked)
        {
            for (int i = 0; i < picked; i++)
                if (_picked[i] == e) return true;
            return false;
        }
    }
}
