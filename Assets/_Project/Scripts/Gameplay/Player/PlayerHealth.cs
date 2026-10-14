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

        private float _current;
        private float _max;
        private PlayerStats _stats;
        private float _lastHurt = float.NegativeInfinity;
        private bool _dead;

        public float Current => _current;
        public float Max => _max;
        /// <summary>Base max health before skills (KAN-75 power curve).</summary>
        public float BaseMax => maxHealth;
        public float Normalized => _current / _max;
        public bool IsDead => _dead;
        /// <summary>Takes no damage while set (boss intro, KAN-62: the Knight cannot move, so nothing may hurt it).</summary>
        public bool Invulnerable { get; set; }

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
        /// Sets health to a carried value (next Chamber, KAN-40), clamped to 1..max: a living Knight stays alive.
        /// No damage or hurt events. No effect once dead.
        /// </summary>
        public void Restore(float current)
        {
            if (_dead) return;
            _current = Mathf.Clamp(current, 1f, _max);
            OnHealthChanged?.Invoke(_current, _max);
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
}
