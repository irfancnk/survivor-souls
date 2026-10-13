using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>What opens the exit portal of a Chamber.</summary>
    public enum OpenCondition
    {
        /// <summary>Normal Chamber: survive the timer (KAN-39).</summary>
        Survive = 0,
        /// <summary>Boss Chamber (KAN-22): no timer on screen; the boss death calls <see cref="SurvivalTimer.Complete"/>.</summary>
        KillBoss = 1,
    }

    /// <summary>
    /// Chamber exit rules (KAN-39): how long the player must survive before the portal opens, when the HUD warns, and how
    /// long the portal takes to open. One asset per Realm / Chamber type, so tests and later Chambers can use other numbers.
    /// Mini bosses (KAN-23) do not change the timer. Edit the asset; the builder never overwrites it.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Chamber Rules", fileName = "ChamberRules")]
    public sealed class ChamberRules : ScriptableObject
    {
        [SerializeField] private OpenCondition openCondition = OpenCondition.Survive;
        [Tooltip("Seconds of play (scaled game time) before the portal opens.")]
        [SerializeField, Min(1f)] private float survivalSeconds = 600f;
        [Tooltip("Remaining seconds at which a short toast tells the time left (e.g. 300, 120, 60). Any order.")]
        [SerializeField] private int[] warnings = { 300, 120, 60 };
        [Tooltip("Last seconds in which the timer pulses red.")]
        [SerializeField, Min(0f)] private float finalCountdown = 10f;
        [Tooltip("Seconds of the portal opening sequence (runes, cracks, burst).")]
        [SerializeField, Min(0.1f)] private float openSequence = 2f;

        public OpenCondition OpenCondition => openCondition;
        public float SurvivalSeconds => survivalSeconds;
        public int[] Warnings => warnings;
        public float FinalCountdown => finalCountdown;
        public float OpenSequence => openSequence;

        /// <summary>Tests and tools: rules without an asset.</summary>
        public static ChamberRules Create(float survivalSeconds, int[] warnings = null, float finalCountdown = 10f,
            float openSequence = 2f, OpenCondition condition = OpenCondition.Survive)
        {
            var rules = CreateInstance<ChamberRules>();
            rules.survivalSeconds = survivalSeconds;
            rules.warnings = warnings ?? new[] { 300, 120, 60 };
            rules.finalCountdown = finalCountdown;
            rules.openSequence = openSequence;
            rules.openCondition = condition;
            return rules;
        }
    }
}
