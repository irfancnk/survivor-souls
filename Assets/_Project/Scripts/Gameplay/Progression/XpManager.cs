using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Progression
{
    /// <summary>
    /// Run XP and level (KAN-21). <see cref="AddXp"/> carries the remainder over: one big gain that crosses several
    /// thresholds raises <see cref="OnLevelUp"/> once per level, in order (LevelUpFlow queues them and shows one choice
    /// per level). Run-only: state starts fresh in Awake (a new run reloads the Game scene) and on <see cref="ResetRun"/>.
    /// The level curve is the <see cref="XpCurve"/> asset.
    /// </summary>
    public sealed class XpManager : MonoBehaviour
    {
        [SerializeField] private XpCurve curve;

        /// <summary>XP into the current level, XP needed for the next level, current level. Raised after every change.</summary>
        public event Action<int, int, int> OnXpChanged;
        /// <summary>Raised once per level gained, with the new level. During the call <see cref="Level"/> equals it.</summary>
        public event Action<int> OnLevelUp;

        public int Level { get; private set; } = 1;
        /// <summary>XP collected into the current level (0 .. XpToNext - 1).</summary>
        public int Xp { get; private set; }
        public int XpToNext { get; private set; } = 1;
        /// <summary>All XP collected this run.</summary>
        public int TotalXp { get; private set; }
        /// <summary>0..1 fill of the current level.</summary>
        public float Progress => XpToNext > 0 ? Xp / (float)XpToNext : 0f;

        private void Awake()
        {
            if (curve == null)
            {
                Debug.LogWarning("[XpManager] No XpCurve assigned; using the asset defaults.", this);
                curve = ScriptableObject.CreateInstance<XpCurve>();
            }
            ResetRun();
        }

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(XpCurve value)
        {
            curve = value;
            ResetRun();
        }

        /// <summary>Back to level 1 with no XP (start of a run).</summary>
        public void ResetRun()
        {
            Level = 1;
            Xp = 0;
            TotalXp = 0;
            XpToNext = curve != null ? curve.XpToNext(1) : 1;
            OnXpChanged?.Invoke(Xp, XpToNext, Level);
        }

        /// <summary>
        /// Puts back a carried state (next Chamber, KAN-40): level, XP into it (clamped below the next threshold) and the
        /// run total. No <see cref="OnLevelUp"/> (those levels were picked already); one <see cref="OnXpChanged"/>.
        /// </summary>
        public void Restore(int level, int xp, int totalXp)
        {
            Level = Mathf.Max(1, level);
            XpToNext = curve != null ? curve.XpToNext(Level) : 1;
            Xp = Mathf.Clamp(xp, 0, Mathf.Max(0, XpToNext - 1));
            TotalXp = Mathf.Max(0, totalXp);
            OnXpChanged?.Invoke(Xp, XpToNext, Level);
        }

        /// <summary>Adds XP. Returns the number of levels gained by this call.</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0) return 0;
            TotalXp += amount;
            Xp += amount;
            int gained = 0;
            while (Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                XpToNext = curve.XpToNext(Level);
                gained++;
                OnLevelUp?.Invoke(Level);
            }
            OnXpChanged?.Invoke(Xp, XpToNext, Level);
            return gained;
        }
    }
}
