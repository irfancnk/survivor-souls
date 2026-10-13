using UnityEngine;

namespace SurvivorSouls.Gameplay.Progression
{
    /// <summary>
    /// XP needed to go from one level to the next (KAN-21). Tuning data only: no numbers live in code.
    /// Level <c>n</c> needs <c>table[n - 1]</c> when the explicit table has that entry, else
    /// <c>baseXp * growth^(n - 1)</c> rounded to a multiple of <see cref="roundTo"/> (readable numbers).
    /// The table wins for the early levels designers hand-tune; the formula carries the rest of the run.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/XP Curve", fileName = "XpCurve")]
    public sealed class XpCurve : ScriptableObject
    {
        [Tooltip("XP from level 1 to level 2 (formula part).")]
        [SerializeField, Min(1)] private int baseXp = 50;
        [Tooltip("Each level needs this many times the XP of the one before (formula part).")]
        [SerializeField, Min(1f)] private float growth = 1.22f;
        [Tooltip("Formula results are rounded to a multiple of this.")]
        [SerializeField, Min(1)] private int roundTo = 5;
        [Tooltip("Optional explicit XP for level 1, 2, 3... Overrides the formula for the levels it covers.")]
        [SerializeField] private int[] table = new int[0];

        /// <summary>XP needed to leave <paramref name="level"/> (1-based). Always at least 1.</summary>
        public int XpToNext(int level)
        {
            level = Mathf.Max(1, level);
            if (table != null && level - 1 < table.Length && table[level - 1] > 0) return table[level - 1];
            float raw = baseXp * Mathf.Pow(growth, level - 1);
            int rounded = Mathf.RoundToInt(raw / roundTo) * roundTo;
            return Mathf.Max(1, rounded);
        }

        /// <summary>Code-side setup (asset builder, tests). Inspector edits are the normal way to tune.</summary>
        public void Configure(int baseXpValue, float growthValue, int roundToValue, int[] explicitTable = null)
        {
            baseXp = Mathf.Max(1, baseXpValue);
            growth = Mathf.Max(1f, growthValue);
            roundTo = Mathf.Max(1, roundToValue);
            table = explicitTable ?? new int[0];
        }
    }
}
