using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Repelling Bolts" (KAN-246, Rare): every <see cref="StatId.KnockEvery"/>-th weapon hit that leaves the foe alive pushes
    /// it back along the bolt at <see cref="StatId.KnockForce"/> m/s (<see cref="Enemy.TryKnock"/>). Bosses and foes still on
    /// their <see cref="perTargetCooldown"/> ignore it; the charge then waits for the next hit. Look: a pale shock puff at the
    /// hit point (one shared system).
    /// </summary>
    public sealed class RepellingBoltsEffect : SkillEffect
    {
        [SerializeField, Min(0f)] private float perTargetCooldown = 1.5f;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Puff = new Color(0.85f, 0.9f, 1f, 0.8f);

        private System.Action<BoltHit> _onHit;
        private int _charge;

        public int Every => Mathf.Max(0, Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.KnockEvery, 0f)));
        public float Force => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.KnockForce, 0f));
        public int Charge => _charge;
        /// <summary>Knockbacks that took this run (tests).</summary>
        public int KnockCount { get; private set; }

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
            int every = Every;
            if (every <= 0 || hit.Killed || hit.Enemy == null || !hit.Enemy.IsAlive) return;
            if (_charge < every) _charge++;
            if (_charge < every) return;
            if (!hit.Enemy.TryKnock(hit.Direction, Force, perTargetCooldown)) return; // boss or cooldown: the charge waits
            _charge = 0;
            KnockCount++;
            SkillFx.Flash(fx, hit.Point, 1.1f, Puff, 0.16f);
            SkillFx.Sparks(fx, hit.Point, 6, Puff, 3.2f, 0.1f, 0.25f);
        }
    }
}
