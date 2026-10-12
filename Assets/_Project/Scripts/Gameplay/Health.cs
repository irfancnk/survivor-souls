using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>Hit points for players and enemies. Damage only; healing comes later with boons.</summary>
    public sealed class Health : MonoBehaviour
    {
        [SerializeField, Min(1)] private int max = 100;

        public int Max => max;
        public int Current { get; private set; }
        public bool IsDead => Current <= 0;
        public float Fraction => (float)Current / max;

        /// <summary>Amount of damage taken.</summary>
        public event Action<int> Damaged;
        public event Action Died;

        private void Awake() => Current = max;

        public void Damage(int amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Mathf.Max(0, Current - amount);
            Damaged?.Invoke(amount);
            if (Current == 0) Died?.Invoke();
        }
    }
}
