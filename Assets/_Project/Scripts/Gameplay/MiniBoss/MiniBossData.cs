using SurvivorSouls.Gameplay.Chests;
using UnityEngine;

namespace SurvivorSouls.Gameplay.MiniBosses
{
    /// <summary>
    /// One mini boss (KAN-23): which roster enemy is promoted, how much stronger and bigger it gets, its colours, when and
    /// how it arrives, and what it leaves. A mini boss is a normal roster type (it keeps its own attacks: the Grave Brute
    /// keeps its telegraphed slam, KAN-106) with <see cref="Combat.Enemy.PromoteToMiniBoss"/> on top. Which Chambers have
    /// one is the <see cref="Chamber.ChamberDifficulty"/> rule. Edit the asset; the builder never overwrites it.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Mini Boss", fileName = "MiniBoss")]
    public sealed class MiniBossData : ScriptableObject
    {
        [Tooltip("Name for the HUD and logs.")]
        [SerializeField] private string displayName = "Gravewarden Brute";
        [Tooltip("Prefab name of the roster enemy that is promoted (must be in the wave table or the spawner's extra types).")]
        [SerializeField] private string enemyType = "Enemy_GraveBrute";

        [Header("Body")]
        [Tooltip("Max health x this, on top of the Chamber / horde health multiplier of the spawn.")]
        [SerializeField, Min(1f)] private float healthMultiplier = 4f;
        [Tooltip("Body size x this (hit radius, height and the health bar follow).")]
        [SerializeField, Min(0.5f)] private float sizeMultiplier = 1.35f;
        [Tooltip("Calm body colour (x the texture). Above 1 brightens.")]
        [SerializeField] private Color tint = new Color(1.25f, 0.55f, 0.45f, 1f);
        [Tooltip("Ground aura under the mini boss (alpha blend: dark colours read on snow).")]
        [SerializeField] private Color auraColor = new Color(0.62f, 0.05f, 0.04f, 0.9f);
        [Tooltip("Aura radius (m) at size 1; it grows with the body.")]
        [SerializeField, Min(0.2f)] private float auraRadius = 1.1f;

        [Header("Arrival")]
        [Tooltip("Game seconds after the Chamber starts before the warning shows.")]
        [SerializeField, Min(0f)] private float spawnDelay = 20f;
        [Tooltip("Seconds the ground warning grows before the mini boss rises.")]
        [SerializeField, Min(0.1f)] private float warningTime = 1.6f;
        [Tooltip("Warning ring radius (m).")]
        [SerializeField, Min(0.5f)] private float warningRadius = 1.8f;
        [Tooltip("Distance (m) from the Knight to the spawn point (ahead of its run direction, on screen).")]
        [SerializeField, Min(2f)] private float spawnDistance = 6.5f;

        [Header("Reward")]
        [Tooltip("Bonus XP on the kill (on top of the type's own wisp), split over the bonus wisps.")]
        [SerializeField, Min(0)] private int bonusXp = 100;
        [SerializeField, Min(1)] private int bonusWisps = 6;
        [Tooltip("Tier of the guaranteed chest (Light = wooden, Rare = gilded).")]
        [SerializeField] private ChestTier chestTier = ChestTier.Light;
        [Tooltip("Upgrade cards the chest offers.")]
        [SerializeField, Min(1)] private int chestChoices = 3;
        [Tooltip("Skill levels the chest gives.")]
        [SerializeField, Min(1)] private int chestUpgrades = 1;
        [Tooltip("Extra levels when the Knight owns Reliquary Hunter (KAN-50: L1+ gives mini boss chests +1 upgrade).")]
        [SerializeField, Min(0)] private int hunterBonusUpgrades = 1;
        [SerializeField] private string hunterSkillId = "reliquary_hunter";

        public string DisplayName => displayName;
        public string EnemyType => enemyType;
        public float HealthMultiplier => healthMultiplier;
        public float SizeMultiplier => sizeMultiplier;
        public Color Tint => tint;
        public Color AuraColor => auraColor;
        public float AuraRadius => auraRadius;
        public float SpawnDelay => spawnDelay;
        public float WarningTime => warningTime;
        public float WarningRadius => warningRadius;
        public float SpawnDistance => spawnDistance;
        public int BonusXp => bonusXp;
        public int BonusWisps => bonusWisps;
        public ChestTier ChestTier => chestTier;
        public int ChestChoices => chestChoices;
        public int ChestUpgrades => chestUpgrades;
        public int HunterBonusUpgrades => hunterBonusUpgrades;
        public string HunterSkillId => hunterSkillId;

        /// <summary>Tests and tools: data without an asset.</summary>
        public static MiniBossData Create(string type = "Enemy_GraveBrute", float health = 4f, float size = 1.35f,
            float delay = 20f, float warning = 1.6f, int xp = 100, int wisps = 6, ChestTier tier = ChestTier.Light,
            int choices = 3, int upgrades = 1, int hunterBonus = 1, float distance = 6.5f)
        {
            var d = CreateInstance<MiniBossData>();
            d.enemyType = type;
            d.healthMultiplier = Mathf.Max(1f, health);
            d.sizeMultiplier = Mathf.Max(0.5f, size);
            d.spawnDelay = Mathf.Max(0f, delay);
            d.warningTime = Mathf.Max(0.1f, warning);
            d.bonusXp = Mathf.Max(0, xp);
            d.bonusWisps = Mathf.Max(1, wisps);
            d.chestTier = tier;
            d.chestChoices = Mathf.Max(1, choices);
            d.chestUpgrades = Mathf.Max(1, upgrades);
            d.hunterBonusUpgrades = Mathf.Max(0, hunterBonus);
            d.spawnDistance = Mathf.Max(2f, distance);
            return d;
        }
    }
}
