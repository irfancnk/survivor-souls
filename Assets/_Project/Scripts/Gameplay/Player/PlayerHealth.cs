using System;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// The player's health pool. <see cref="TakeDamage"/> and <see cref="Heal"/> clamp to 0..max and return the
    /// amount actually applied. <see cref="OnDeath"/> fires exactly once, the moment health reaches 0; after that the
    /// component ignores damage and healing (a retry reloads the scene).
    /// Damage can arrive every frame (contact damage), so feedback (flash, vignette, sound) should listen to
    /// <see cref="OnHurt"/>, which is throttled to one pulse per <see cref="hurtPulseInterval"/>.
    /// <see cref="maxHealth"/> is the base: max health skills raise it through <see cref="PlayerStats"/>; a raise also heals
    /// by the amount gained (Archero rule), a drop only clamps.
    /// <see cref="Shield"/> (KAN-82, for KAN-93 Temporary Shield) is a separate pool on top of health: damage takes the
    /// shield first, the rest comes off health. No skill grants a shield yet: KAN-93 Hallowed Shield uses the full block
    /// <see cref="Warded"/> instead (the ticket asks for no damage at all while it is up).
    /// <see cref="TakeDamage(float, Vector3)"/> (KAN-187) is a discrete hit from a known point (ranged shot, boss attack,
    /// telegraphed special): it also raises <see cref="OnHit"/>, which drives the hit direction arc, hit-stop, camera shake,
    /// grunt and haptic. Contact damage (horde, burning ground) keeps the plain overload and never raises it. A warded hit
    /// (KAN-93) raises no <see cref="OnHit"/>: the ward's own flash and tink are the feedback.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float maxHealth = 400f;
        [Tooltip("Minimum seconds between two OnHurt pulses while damage keeps coming in. The first hit pulses at once.")]
        [SerializeField, Min(0.05f)] private float hurtPulseInterval = 0.45f;

        /// <summary>Raised on every change: current health, max health.</summary>
        public event Action<float, float> OnHealthChanged;
        /// <summary>Raised on every damage call that deducted health: amount deducted.</summary>
        public event Action<float> OnDamaged;
        /// <summary>Throttled damage pulse for feedback (see class summary).</summary>
        public event Action OnHurt;
        /// <summary>Raised once, when health reaches 0.</summary>
        public event Action OnDeath;
        /// <summary>Raised when the shield value changes (KAN-82): new shield.</summary>
        public event Action<float> OnShieldChanged;
        /// <summary>Raised on every damage call a ward blocked (KAN-93): amount blocked. Can fire every frame (contact damage).</summary>
        public event Action<float> OnWardBlocked;
        /// <summary>Discrete hit from a known point (KAN-187), after the health events. Never for contact damage.</summary>
        public event Action<PlayerHit> OnHit;

        private float _current;
        private float _max;
        private PlayerStats _stats;
        private float _lastHurt = float.NegativeInfinity;
        private bool _dead;
        private float _shield;

        public float Current => _current;
        public float Max => _max;
        /// <summary>Base max health before skills (KAN-75 power curve).</summary>
        public float BaseMax => maxHealth;
        public float Normalized => _current / _max;
        public bool IsDead => _dead;
        /// <summary>Shield on top of health (KAN-82). Damage takes it first.</summary>
        public float Shield => _shield;
        /// <summary>Takes no damage while set (boss intro, KAN-62: the Knight cannot move, so nothing may hurt it).</summary>
        public bool Invulnerable { get; set; }
        /// <summary>
        /// Ward up (KAN-93 Hallowed Shield): no damage at all, the shield pool is not touched either. Separate from
        /// <see cref="Invulnerable"/> so the skill and the boss intro never clear each other's flag.
        /// </summary>
        public bool Warded { get; set; }

        private void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _max = PlayerStats.Eval(_stats, StatId.MaxHealth, maxHealth);
            _current = _max;
        }

        private void OnEnable()
        {
            if (_stats != null) _stats.Changed += OnStatsChanged;
        }

        private void OnDisable()
        {
            if (_stats != null) _stats.Changed -= OnStatsChanged;
        }

        private void OnStatsChanged()
        {
            float max = Mathf.Max(1f, PlayerStats.Eval(_stats, StatId.MaxHealth, maxHealth));
            if (_dead || Mathf.Approximately(max, _max)) return;
            float gained = max - _max;
            _max = max;
            _current = gained > 0f ? _current + gained : Mathf.Min(_current, _max);
            OnHealthChanged?.Invoke(_current, _max);
        }

        /// <summary>Deducts up to <paramref name="amount"/>. Returns the health actually deducted.</summary>
        public float TakeDamage(float amount)
        {
            if (_dead || Invulnerable || amount <= 0f) return 0f;
            if (Warded)
            {
                OnWardBlocked?.Invoke(amount);
                return 0f;
            }
            if (_shield > 0f)
            {
                float absorbed = Mathf.Min(_shield, amount);
                SetShield(_shield - absorbed);
                amount -= absorbed;
                if (amount <= 0f) return 0f;
            }
            float deducted = Mathf.Min(amount, _current);
            _current -= deducted; // exactly 0 when amount >= current (no float drift past zero)
            _dead = _current <= 0f;

            OnHealthChanged?.Invoke(_current, _max);
            OnDamaged?.Invoke(deducted);
            float now = Time.time;
            if (now - _lastHurt >= hurtPulseInterval)
            {
                _lastHurt = now;
                OnHurt?.Invoke();
            }
            if (_dead) OnDeath?.Invoke();
            return deducted;
        }

        /// <summary>
        /// Discrete hit from <paramref name="source"/> (world position the hit came from; KAN-187). Same damage rules as
        /// <see cref="TakeDamage(float)"/>, then raises <see cref="OnHit"/> (also when the shield took it all). Returns the
        /// health actually deducted.
        /// </summary>
        public float TakeDamage(float amount, Vector3 source)
        {
            if (_dead || Invulnerable || amount <= 0f) return 0f;
            if (Warded) return TakeDamage(amount); // KAN-93: blocked, ward feedback only (no OnHit)
            float dealt = TakeDamage(amount);
            OnHit?.Invoke(new PlayerHit(dealt, _max > 0f ? dealt / _max : 0f, source, _dead));
            return dealt;
        }

        /// <summary>
        /// Sets health to a carried value (next Chamber, KAN-40), clamped to 1..max: a living Knight stays alive.
        /// No damage or hurt events. No effect once dead.
        /// </summary>
        public void Restore(float current)
        {
            if (_dead) return;
            _current = Mathf.Clamp(current, 1f, _max);
            OnHealthChanged?.Invoke(_current, _max);
        }

        /// <summary>Sets the shield (KAN-82 API for KAN-93), never below 0. No effect once dead.</summary>
        public void SetShield(float amount)
        {
            if (_dead) return;
            float shield = Mathf.Max(0f, amount);
            if (Mathf.Approximately(shield, _shield)) return;
            _shield = shield;
            OnShieldChanged?.Invoke(_shield);
        }

        /// <summary>Adds up to <paramref name="amount"/> (capped at max). Returns the health actually added. No effect once dead.</summary>
        public float Heal(float amount)
        {
            if (_dead || amount <= 0f) return 0f;
            float added = Mathf.Min(amount, _max - _current);
            if (added <= 0f) return 0f;
            _current += added;
            OnHealthChanged?.Invoke(_current, _max);
            return added;
        }
    }

    /// <summary>A discrete hit on the Knight (KAN-187): health lost, its share of max health, where it came from.</summary>
    public readonly struct PlayerHit
    {
        /// <summary>Health deducted (0 when the shield took it all).</summary>
        public readonly float Dealt;
        /// <summary><see cref="Dealt"/> / max health.</summary>
        public readonly float Share;
        /// <summary>World position the hit came from.</summary>
        public readonly Vector3 Source;
        /// <summary>The hit killed the Knight.</summary>
        public readonly bool Killed;

        public PlayerHit(float dealt, float share, Vector3 source, bool killed)
        {
            Dealt = dealt;
            Share = share;
            Source = source;
            Killed = killed;
        }
    }
}
