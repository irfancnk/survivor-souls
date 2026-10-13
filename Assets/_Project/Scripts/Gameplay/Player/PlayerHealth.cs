using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// The player's health pool. <see cref="TakeDamage"/> and <see cref="Heal"/> clamp to 0..max and return the
    /// amount actually applied. <see cref="OnDeath"/> fires exactly once, the moment health reaches 0; after that the
    /// component ignores damage and healing (a retry reloads the scene).
    /// Damage can arrive every frame (contact damage), so feedback (flash, vignette, sound) should listen to
    /// <see cref="OnHurt"/>, which is throttled to one pulse per <see cref="hurtPulseInterval"/>.
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
        private float _lastHurt = float.NegativeInfinity;
        private bool _dead;

        public float Current => _current;
        public float Max => maxHealth;
        public float Normalized => _current / maxHealth;
        public bool IsDead => _dead;

        private void Awake() => _current = maxHealth;

        /// <summary>Deducts up to <paramref name="amount"/>. Returns the health actually deducted.</summary>
        public float TakeDamage(float amount)
        {
            if (_dead || amount <= 0f) return 0f;
            float deducted = Mathf.Min(amount, _current);
            _current -= deducted; // exactly 0 when amount >= current (no float drift past zero)
            _dead = _current <= 0f;

            OnHealthChanged?.Invoke(_current, maxHealth);
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

        /// <summary>Adds up to <paramref name="amount"/> (capped at max). Returns the health actually added. No effect once dead.</summary>
        public float Heal(float amount)
        {
            if (_dead || amount <= 0f) return 0f;
            float added = Mathf.Min(amount, maxHealth - _current);
            if (added <= 0f) return 0f;
            _current += added;
            OnHealthChanged?.Invoke(_current, maxHealth);
            return added;
        }
    }
}
