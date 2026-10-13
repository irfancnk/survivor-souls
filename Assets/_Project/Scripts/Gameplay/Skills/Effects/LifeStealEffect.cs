using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Blood Covenant": heals the player by <see cref="StatId.LifeSteal"/> (a share) of the damage every bolt deals.
    /// Healing is summed per frame; a crimson wisp burst rises at the Knight's chest at most every
    /// <see cref="feedbackInterval"/> seconds, and only when the heal actually restored health.
    /// </summary>
    public sealed class LifeStealEffect : SkillEffect
    {
        [SerializeField] private ParticleSystem motes;
        [SerializeField, Min(0)] private int motesPerPulse = 5;
        [SerializeField, Min(0.05f)] private float feedbackInterval = 0.35f;

        private System.Action<BoltHit> _onHit;
        private float _lastFeedback = float.NegativeInfinity;

        /// <summary>Health restored by this skill this run.</summary>
        public float TotalHealed { get; private set; }

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
            if (Context.Health == null) return;
            float share = PlayerStats.Eval(Context.Stats, StatId.LifeSteal, 0f);
            if (share <= 0f || hit.Damage <= 0f) return;
            float healed = Context.Health.Heal(hit.Damage * share);
            if (healed <= 0f) return;
            TotalHealed += healed;
            if (motes == null || Time.time - _lastFeedback < feedbackInterval) return;
            _lastFeedback = Time.time;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            Vector3 chest = Context.Player != null ? Context.Player.position + Vector3.up * 1.1f : transform.position;
            for (int i = 0; i < motesPerPulse; i++)
            {
                Vector2 r = Random.insideUnitCircle * 0.35f;
                p.position = chest + new Vector3(r.x, Random.Range(-0.3f, 0.2f), r.y);
                p.velocity = new Vector3(r.x, Random.Range(1.2f, 2f), r.y);
                motes.Emit(p, 1);
            }
        }
    }
}
