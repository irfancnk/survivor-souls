using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Last Oath" (KAN-246, Legendary): when the Knight's health drops below <see cref="threshold"/> of max, a surge runs for
    /// <see cref="surgeTime"/> s: +<see cref="StatId.OathDamage"/> weapon damage (a temporary <see cref="PlayerStats"/> source)
    /// and life steal of <see cref="lifeSteal"/> of the weapon damage dealt, at most <see cref="healCap"/> of max health per
    /// surge. Then a long cooldown (<see cref="baseCooldown"/> + <see cref="StatId.OathCooldown"/> s), and the oath only rearms
    /// once health is back above the threshold. No invulnerability. Look: crimson-gold embers rise off the Knight while the
    /// surge runs, a flare when it starts.
    /// </summary>
    public sealed class LastOathEffect : SkillEffect
    {
        [SerializeField, Range(0.05f, 0.9f)] private float threshold = 0.3f;
        [SerializeField, Min(0.5f)] private float surgeTime = 6f;
        [SerializeField, Min(1f)] private float baseCooldown = 45f;
        [SerializeField, Range(0f, 1f)] private float lifeSteal = 0.1f;
        [SerializeField, Range(0f, 1f)] private float healCap = 0.25f;
        [SerializeField] private ParticleSystem fx;

        private static readonly Color Ember = new Color(1f, 0.35f, 0.2f, 0.95f);
        private static readonly Color Flare = new Color(1f, 0.75f, 0.35f, 1f);

        private readonly StatModifier[] _surge = { new StatModifier(StatId.Damage, ModifierKind.Percent, 0f) };
        private System.Action<BoltHit> _onHit;
        private bool _armed;
        private float _surgeLeft;
        private float _cooldown;
        private float _healed;
        private float _emberDebt;

        public float Threshold => threshold;
        public float DamageBonus => Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.OathDamage, 0f));
        public float Cooldown => Mathf.Max(5f, PlayerStats.Eval(Context?.Stats, StatId.OathCooldown, baseCooldown));
        public bool Surging => _surgeLeft > 0f;
        public bool Armed => _armed;
        public float CooldownLeft => Mathf.Max(0f, _cooldown);
        /// <summary>Health healed by the running (or last) surge.</summary>
        public float Healed => _healed;
        /// <summary>Surges this run (tests).</summary>
        public int Surges { get; private set; }

        protected override void Subscribe()
        {
            _onHit ??= OnHit;
            if (Context.Shooter != null) Context.Shooter.EnemyHit += _onHit;
            _armed = Context.Health == null || Context.Health.Normalized > threshold;
        }

        protected override void Unsubscribe()
        {
            if (Context.Shooter != null) Context.Shooter.EnemyHit -= _onHit;
            EndSurge();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Arm, trigger, run and cool the surge for <paramref name="dt"/> scaled s. Public for tests.</summary>
        public void Tick(float dt)
        {
            var health = Context?.Health;
            if (health == null) return;
            if (health.IsDead) { EndSurge(); return; }
            if (_surgeLeft > 0f)
            {
                _surgeLeft -= dt;
                EmitEmbers(dt);
                if (_surgeLeft <= 0f) EndSurge();
                return;
            }
            if (_cooldown > 0f) _cooldown -= dt;
            float share = health.Normalized;
            if (!_armed)
            {
                if (_cooldown <= 0f && share > threshold) _armed = true;
                return;
            }
            if (share < threshold && DamageBonus > 0f) Begin();
        }

        private void Begin()
        {
            _armed = false;
            _surgeLeft = surgeTime;
            _cooldown = Cooldown;
            _healed = 0f;
            Surges++;
            _surge[0] = new StatModifier(StatId.Damage, ModifierKind.Percent, DamageBonus);
            if (Context.Stats != null) Context.Stats.SetSource(this, _surge, 1);
            Vector3 at = (Context.Player != null ? Context.Player.position : transform.position) + Vector3.up * 1f;
            SkillFx.Flash(fx, at, 2.4f, Flare, 0.2f);
            SkillFx.Sparks(fx, at, 18, Ember, 4f, 0.12f, 0.45f);
        }

        private void EndSurge()
        {
            if (_surgeLeft > 0f) _surgeLeft = 0f;
            if (Context?.Stats != null) Context.Stats.RemoveSource(this);
        }

        private void OnHit(BoltHit hit)
        {
            if (_surgeLeft <= 0f || Context.Health == null || hit.Damage <= 0f) return;
            float cap = Context.Health.Max * healCap - _healed;
            if (cap <= 0f) return;
            _healed += Context.Health.Heal(Mathf.Min(cap, hit.Damage * lifeSteal));
        }

        private void EmitEmbers(float dt)
        {
            if (fx == null || Context.Player == null) return;
            _emberDebt += 30f * dt;
            int n = (int)_emberDebt;
            if (n <= 0) return;
            _emberDebt -= n;
            SkillFx.Rise(fx, Context.Player.position, 0.45f, 1.8f, Mathf.Min(n, 4), Ember, 0.14f, 0.6f);
        }
    }
}
