using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Venom Bolts" (KAN-246, Rare): every weapon hit that leaves the foe alive poisons it (<see cref="Enemy.ApplyPoison"/>):
    /// <see cref="StatId.PoisonDps"/> per stack for <see cref="StatId.PoisonDuration"/> s, up to
    /// <see cref="StatId.PoisonStacks"/> stacks. A longer, slower damage-over-time than Ember Bolts; it never spreads and a
    /// poison tick sets off nothing (ticks are not weapon hits). Look: green bubbles rise off poisoned foes from ONE shared
    /// system (<see cref="fx"/>, Emit only), a small green splash on each poisoning hit. No GC per hit.
    /// </summary>
    public sealed class VenomBoltsEffect : SkillEffect
    {
        [SerializeField] private ParticleSystem fx;
        [Tooltip("Bubbles per second per poisoned foe.")]
        [SerializeField, Min(0f)] private float bubbleRate = 6f;

        private static readonly Color Bubble = new Color(0.45f, 0.95f, 0.3f, 0.9f);
        private static readonly Color Splash = new Color(0.55f, 1f, 0.35f, 1f);

        private System.Action<BoltHit> _onHit;
        private float _debt;

        public float DpsPerStack => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.PoisonDps, 0f));
        public float Duration => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.PoisonDuration, 0f));
        public int MaxStacks => Mathf.Max(1, Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.PoisonStacks, 1f)));
        /// <summary>Poisoning hits this run (tests).</summary>
        public int PoisonCount { get; private set; }

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
            if (hit.Killed || hit.Enemy == null || !hit.Enemy.IsAlive) return;
            float dps = DpsPerStack, length = Duration;
            if (dps <= 0f || length <= 0f) return;
            hit.Enemy.ApplyPoison(dps, length, MaxStacks);
            PoisonCount++;
            SkillFx.Sparks(fx, hit.Point, 5, Splash, 2.2f, 0.12f, 0.3f);
        }

        private void Update()
        {
            if (fx == null || bubbleRate <= 0f) return;
            _debt += bubbleRate * Time.deltaTime;
            int per = (int)_debt;
            if (per <= 0) return;
            _debt -= per;
            if (per > 3) per = 3;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.IsPoisoned) continue;
                SkillFx.Rise(fx, e.transform.position, e.HitRadius, e.Height * 0.9f, per + e.PoisonStacks / 2, Bubble, 0.13f * e.Scale, 0.7f);
            }
        }
    }
}
