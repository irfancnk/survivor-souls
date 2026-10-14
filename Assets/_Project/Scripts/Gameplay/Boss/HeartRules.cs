using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Healing hearts of the boss fight (KAN-60 = KAN-22.3): when they drop, how many can wait, how long they stay, where
    /// they land and how much they heal. All numbers live here, so balance needs no code (KAN-62 tunes them). Edit the
    /// asset; the builder never overwrites it.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Heart Rules", fileName = "HeartRules")]
    public sealed class HeartRules : ScriptableObject
    {
        [Header("Timed drops")]
        [Tooltip("Seconds between timed drops: a random value in this range for each drop. The clock only runs while the Knight is hurt.")]
        [SerializeField, Min(1f)] private float intervalMin = 20f;
        [SerializeField, Min(1f)] private float intervalMax = 25f;
        [Tooltip("Below this share of max health the clock runs faster (low-HP help).")]
        [SerializeField, Range(0f, 1f)] private float lowHealthShare = 0.3f;
        [Tooltip("Clock speed under the low-HP share: 2 = the next heart comes in half the time.")]
        [SerializeField, Range(1f, 4f)] private float lowHealthSpeed = 2f;
        [Tooltip("A boss phase change (KAN-61) drops one heart at once.")]
        [SerializeField] private bool dropOnPhaseChange = true;

        [Header("Ground")]
        [Tooltip("Hearts waiting at the same time (glowing ones count). A drop past the cap is skipped.")]
        [SerializeField, Range(1, 6)] private int maxOnGround = 2;
        [Tooltip("Seconds the ground glow shows before the heart appears (no pickup yet).")]
        [SerializeField, Min(0f)] private float appearTime = 1f;
        [Tooltip("Seconds a heart stays once it appeared, then it fades.")]
        [SerializeField, Min(1f)] private float lifetime = 15f;
        [Tooltip("Last seconds of the lifetime: the heart blinks faster and faster.")]
        [SerializeField, Min(0f)] private float warningTime = 3f;

        [Header("Placement")]
        [Tooltip("A heart never lands closer than this to the Knight (m): he has to go for it.")]
        [SerializeField, Min(0f)] private float minPlayerDistance = 4f;
        [Tooltip("...and never farther than this (m): it stays near the action, on screen.")]
        [SerializeField, Min(1f)] private float maxPlayerDistance = 9f;
        [Tooltip("Free ground around the boss where no heart lands (m).")]
        [SerializeField, Min(0f)] private float bossClearance = 3.5f;

        [Header("Pickup")]
        [Tooltip("Heal on pickup, share of the Knight's max health.")]
        [SerializeField, Range(0f, 1f)] private float healShare = 0.25f;
        [Tooltip("The Knight picks a heart up within this distance (m). Only when hurt: at full health it stays on the ground.")]
        [SerializeField, Min(0.2f)] private float pickupRadius = 1.1f;

        public float IntervalMin => Mathf.Min(intervalMin, intervalMax);
        public float IntervalMax => Mathf.Max(intervalMin, intervalMax);
        public float LowHealthShare => lowHealthShare;
        public float LowHealthSpeed => lowHealthSpeed;
        public bool DropOnPhaseChange => dropOnPhaseChange;
        public int MaxOnGround => maxOnGround;
        public float AppearTime => appearTime;
        public float Lifetime => lifetime;
        public float WarningTime => Mathf.Min(warningTime, lifetime);
        public float MinPlayerDistance => minPlayerDistance;
        public float MaxPlayerDistance => Mathf.Max(maxPlayerDistance, minPlayerDistance + 0.5f);
        public float BossClearance => bossClearance;
        public float HealShare => healShare;
        public float PickupRadius => pickupRadius;

        /// <summary>Health a heart gives a Knight with <paramref name="playerMax"/> max health.</summary>
        public float HealFor(float playerMax) => healShare * Mathf.Max(0f, playerMax);

        /// <summary>Clock speed of the timed drops at <paramref name="healthShare"/> (0..1): 0 at full health, faster when low.</summary>
        public float ClockSpeed(float healthShare) =>
            healthShare >= 1f ? 0f : healthShare < lowHealthShare ? lowHealthSpeed : 1f;

        /// <summary>Tests and tools: rules without an asset.</summary>
        public static HeartRules Create(float intervalMin = 20f, float intervalMax = 25f, float lowHealthShare = 0.3f,
            float lowHealthSpeed = 2f, int maxOnGround = 2, float appearTime = 1f, float lifetime = 15f, float warningTime = 3f,
            float minPlayerDistance = 4f, float maxPlayerDistance = 9f, float bossClearance = 3.5f, float healShare = 0.25f,
            float pickupRadius = 1.1f, bool dropOnPhaseChange = true)
        {
            var rules = CreateInstance<HeartRules>();
            rules.intervalMin = intervalMin;
            rules.intervalMax = intervalMax;
            rules.lowHealthShare = lowHealthShare;
            rules.lowHealthSpeed = lowHealthSpeed;
            rules.maxOnGround = maxOnGround;
            rules.appearTime = appearTime;
            rules.lifetime = lifetime;
            rules.warningTime = warningTime;
            rules.minPlayerDistance = minPlayerDistance;
            rules.maxPlayerDistance = maxPlayerDistance;
            rules.bossClearance = bossClearance;
            rules.healShare = healShare;
            rules.pickupRadius = pickupRadius;
            rules.dropOnPhaseChange = dropOnPhaseChange;
            return rules;
        }
    }
}
