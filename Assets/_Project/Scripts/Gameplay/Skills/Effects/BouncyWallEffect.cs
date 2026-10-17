using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Bouncy Wall" (KAN-246, Epic) look only: the rebound itself is bolt data (<see cref="StatId.WallBounces"/>,
    /// <see cref="StatId.WallDamage"/> → <see cref="BoltShot.WallBounces"/>, handled in <see cref="Bolt"/>). Each rebound
    /// throws a small spark fan off the wall (one shared system).
    /// </summary>
    public sealed class BouncyWallEffect : SkillEffect
    {
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Spark = new Color(1f, 0.85f, 0.55f, 1f);

        private System.Action<Vector3, Vector3> _onRebound;

        /// <summary>Rebounds seen this run (tests).</summary>
        public int ReboundCount { get; private set; }

        protected override void Subscribe()
        {
            _onRebound ??= OnRebound;
            Bolt.Rebounded += _onRebound;
        }

        protected override void Unsubscribe() => Bolt.Rebounded -= _onRebound;

        private void OnRebound(Vector3 at, Vector3 dir)
        {
            ReboundCount++;
            SkillFx.Flash(fx, at, 0.6f, Spark, 0.1f);
            SkillFx.Sparks(fx, at, 7, Spark, 3.5f, 0.07f, 0.22f);
        }
    }
}
