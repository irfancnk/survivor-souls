using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chests
{
    /// <summary>Kind of Reliquary chest (KAN-50): Light = the frequent skill drop (1 upgrade), Rare = more upgrades.</summary>
    public enum ChestTier { Light, Rare }

    /// <summary>
    /// Rules for Reliquary chest drops (KAN-72): what counts as an elite kill, the pity timer, the ground cap, the lifetime
    /// and its warning pulse, and what a dropped chest holds. Drop CHANCES are not here: they are stats of the Reliquary
    /// Hunter skill levels (<see cref="Skills.StatId.ChestChance"/>, <see cref="Skills.StatId.ChestEliteChance"/>), so the
    /// level table stays the one source of skill numbers. First guesses; tune with playtests (KAN-53).
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Chest Drop Config", fileName = "ChestDropConfig")]
    public sealed class ChestDropConfig : ScriptableObject
    {
        [Tooltip("An enemy whose soul wisp holds at least this much XP is an elite (Brute = 25, the elite wisp tier).")]
        [SerializeField, Min(1)] private int eliteMinXp = 25;
        [Tooltip("Boss kills roll with the elite chance.")]
        [SerializeField] private bool bossIsElite = true;
        [Tooltip("Seconds without a chest (skill owned, game running) after which the next kill always drops one.")]
        [SerializeField, Min(1f)] private float pitySeconds = 90f;
        [Tooltip("Hard cap of chests waiting on the ground. A kill while full drops nothing (the pity clock keeps running).")]
        [SerializeField, Min(1)] private int maxOnGround = 2;
        [Tooltip("Seconds an unopened chest stays on the ground before it fades away.")]
        [SerializeField, Min(1f)] private float lifetime = 40f;
        [Tooltip("Last seconds of the lifetime with a fast warning pulse.")]
        [SerializeField, Min(0f)] private float warningTime = 5f;
        [Tooltip("Cards on a chest's upgrade screen before skill levels add more (ChestChoices stat).")]
        [SerializeField, Min(1)] private int baseChoices = 2;
        [Tooltip("Levels a Rare chest gives (a Light chest gives 1).")]
        [SerializeField, Min(1)] private int rareUpgrades = 2;
        [Tooltip("Chest lands up to this far (m) from the dead enemy, on free ground.")]
        [SerializeField, Min(0f)] private float scatter = 0.8f;
        [Tooltip("The Knight opens a chest by walking within this distance (m, ground plane) of it. No hold time (KAN-73).")]
        [SerializeField, Min(0.1f)] private float pickupRadius = 1.1f;
        [Tooltip("A chest that just landed cannot be opened for this long (s): the land pop plays first.")]
        [SerializeField, Min(0f)] private float pickupDelay = 0.3f;
        [Tooltip("Seconds of the open animation before the upgrade screen (light chest: 0.4 s).")]
        [SerializeField, Min(0.05f)] private float openTime = 0.4f;
        [Tooltip("Fallback reward when no owned skill can level up (or the queue is full): share of max health healed.")]
        [SerializeField, Range(0f, 1f)] private float fallbackHeal = 0.25f;
        [Tooltip("Fallback reward: XP in soul wisps that pop out of the chest (0 = none).")]
        [SerializeField, Min(0)] private int fallbackXp = 30;
        [Tooltip("Fallback reward: number of soul wisps the XP is split into.")]
        [SerializeField, Min(1)] private int fallbackWisps = 3;

        public int EliteMinXp => eliteMinXp;
        public bool BossIsElite => bossIsElite;
        public float PitySeconds => pitySeconds;
        public int MaxOnGround => maxOnGround;
        public float Lifetime => lifetime;
        public float WarningTime => Mathf.Min(warningTime, lifetime);
        public int BaseChoices => baseChoices;
        public int RareUpgrades => rareUpgrades;
        public float Scatter => scatter;
        public float PickupRadius => pickupRadius;
        public float PickupDelay => pickupDelay;
        public float OpenTime => openTime;
        public float FallbackHeal => fallbackHeal;
        public int FallbackXp => fallbackXp;
        public int FallbackWisps => fallbackWisps;

        /// <summary>Elite kill: boss (when <see cref="BossIsElite"/>) or an enemy worth at least <see cref="EliteMinXp"/> XP.</summary>
        public bool IsElite(Enemy enemy) => enemy != null && ((bossIsElite && enemy.IsBoss) || enemy.XpValue >= eliteMinXp);

        /// <summary>Code-side setup (asset builder, tests). The defaults are the shipped first guesses.</summary>
        public static ChestDropConfig Create(float pity = 90f, int cap = 2, float life = 40f, float warning = 5f, int choices = 2,
            int rareLevels = 2, int eliteXp = 25)
        {
            var c = CreateInstance<ChestDropConfig>();
            c.pitySeconds = Mathf.Max(1f, pity);
            c.maxOnGround = Mathf.Max(1, cap);
            c.lifetime = Mathf.Max(1f, life);
            c.warningTime = Mathf.Max(0f, warning);
            c.baseChoices = Mathf.Max(1, choices);
            c.rareUpgrades = Mathf.Max(1, rareLevels);
            c.eliteMinXp = Mathf.Max(1, eliteXp);
            return c;
        }

        /// <summary>Tests: pickup and open rules (the asset keeps its Inspector values).</summary>
        public void SetOpening(float radius, float delay, float open, float heal = 0.25f, int xp = 30, int wisps = 3)
        {
            pickupRadius = Mathf.Max(0.1f, radius);
            pickupDelay = Mathf.Max(0f, delay);
            openTime = Mathf.Max(0.05f, open);
            fallbackHeal = Mathf.Clamp01(heal);
            fallbackXp = Mathf.Max(0, xp);
            fallbackWisps = Mathf.Max(1, wisps);
        }
    }
}
