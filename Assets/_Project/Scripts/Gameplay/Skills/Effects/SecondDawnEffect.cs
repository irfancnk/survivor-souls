using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Second Dawn" (KAN-246, Legendary): once per run (<see cref="RunState.TrySpendRevive"/>: one revive across all ranks
    /// and Chambers; upgrading never gives a new charge) a fatal hit instead brings the Knight back with
    /// <see cref="StatId.ReviveHealth"/> of his max health (<see cref="Player.PlayerHealth.LastStand"/>), grants
    /// <see cref="immunity"/> s of immunity at once, and next frame a dawn shockwave hits every foe within <see cref="Radius"/>
    /// m for <see cref="shockShare"/> x the bolt damage and pushes them out. The shockwave's kills start no on-kill skill
    /// (<see cref="ProcGuard.NoKillChains"/>). Look: a golden flash, a sunrise ring and rising light motes.
    /// </summary>
    public sealed class SecondDawnEffect : SkillEffect
    {
        [SerializeField, Min(0f)] private float immunity = 2f;
        [SerializeField, Min(0.5f)] private float baseRadius = 3.5f;
        [SerializeField, Range(0f, 10f)] private float shockShare = 2f;
        [SerializeField, Min(0f)] private float knock = 7f;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Gold = new Color(1f, 0.85f, 0.45f, 1f);
        private static readonly Color Dawn = new Color(1f, 0.62f, 0.25f, 0.9f);
        private static readonly Color Mote = new Color(1f, 0.95f, 0.75f, 1f);

        private System.Func<float> _lastStand;
        private bool _pendingWave;

        public float ReviveShare => Mathf.Clamp01(PlayerStats.Eval(Context?.Stats, StatId.ReviveHealth, 0f));
        public float Radius => Mathf.Max(0.5f, PlayerStats.Eval(Context?.Stats, StatId.ReviveRadius, baseRadius));
        public float Immunity => immunity;
        /// <summary>The run's revive is still there.</summary>
        public bool Ready => !RunState.ReviveSpent && ReviveShare > 0f;
        /// <summary>Shockwaves played this run (tests).</summary>
        public int Waves { get; private set; }
        /// <summary>Foes hit by the last shockwave (tests).</summary>
        public int LastWaveHits { get; private set; }

        protected override void Subscribe()
        {
            _lastStand ??= OnLastStand;
            if (Context.Health != null) Context.Health.LastStand = _lastStand;
        }

        protected override void Unsubscribe()
        {
            if (Context.Health != null && Context.Health.LastStand == _lastStand) Context.Health.LastStand = null;
        }

        private float OnLastStand()
        {
            float share = ReviveShare;
            if (share <= 0f || Context.Health == null || !RunState.TrySpendRevive()) return 0f;
            Context.Health.GrantImmunity(immunity);
            _pendingWave = true;
            return Mathf.Max(1f, Context.Health.Max * share);
        }

        private void Update()
        {
            if (!_pendingWave) return;
            _pendingWave = false;
            Shockwave();
        }

        /// <summary>The dawn shockwave round the Knight (no kill chains). Public for tests.</summary>
        public void Shockwave()
        {
            Waves++;
            Vector3 c = Context?.Player != null ? Context.Player.position : transform.position;
            float r = Radius;
            float damage = SkillFx.BoltShare(Context, shockShare);
            int hits = 0;
            using (ProcGuard.NoKillChains())
            {
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
                    hits++;
                    e.Knock(d, knock);
                    if (damage > 0f) e.TakeDamage(damage, d, DamageKind.Burst);
                }
            }
            LastWaveHits = hits;
            SkillFx.Flash(fx, c + Vector3.up * 1f, 3.2f, Gold, 0.25f);
            SkillFx.Ring(fx, c, r, 56, Dawn, 0.6f, 0.45f);
            SkillFx.Rise(fx, c, 1.2f, 2.2f, 24, Mote, 0.16f, 1f);
        }
    }
}
