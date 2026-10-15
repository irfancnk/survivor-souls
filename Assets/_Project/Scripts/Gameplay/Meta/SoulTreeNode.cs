using System;
using System.Globalization;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Meta
{
    /// <summary>Prerequisite of a soul tree node: <see cref="node"/> must be at <see cref="level"/> or higher.</summary>
    [Serializable]
    public struct SoulTreeRequirement
    {
        public SoulTreeNode node;
        [Min(1)] public int level;

        public SoulTreeRequirement(SoulTreeNode node, int level)
        {
            this.node = node;
            this.level = level;
        }
    }

    /// <summary>
    /// One node of the soul tree (KAN-98): a permanent upgrade bought with Souls in the main menu. Each level adds
    /// <see cref="Modifiers"/> once more to the Knight's <see cref="PlayerStats"/> at the start of every run
    /// (<see cref="SoulTreeBonuses"/>). Cost of level L = <see cref="baseCost"/> x <see cref="costGrowth"/> ^ (L - 1),
    /// rounded to 5. <see cref="Tier"/> is the row on the tree screen (0 = roots). Data only: levels are saved by
    /// <see cref="SoulTreeProgress"/>, buy rules live in <see cref="SoulTree"/>.
    /// </summary>
    public sealed class SoulTreeNode : ScriptableObject
    {
        [Tooltip("Save key part. Never change it after release: saved levels are stored under it.")]
        [SerializeField] private string id = "node";
        [SerializeField] private string displayName = "Node";
        [SerializeField, TextArea(2, 4)] private string description = "";
        [Tooltip("Word after the number on the screen, e.g. 'bolt damage' -> '+8% bolt damage'.")]
        [SerializeField] private string effectLabel = "";
        [SerializeField] private Texture2D icon;
        [SerializeField] private Color accent = new Color(0.91f, 0.77f, 0.47f);
        [Tooltip("Row on the tree screen (0 = roots).")]
        [SerializeField, Min(0)] private int tier;
        [SerializeField, Min(1)] private int maxLevel = 5;
        [SerializeField, Min(1)] private int baseCost = 250;
        [SerializeField, Min(1f)] private float costGrowth = 1.45f;
        [Tooltip("Applied once per owned level.")]
        [SerializeField] private StatModifier[] modifiers = Array.Empty<StatModifier>();
        [SerializeField] private SoulTreeRequirement[] requires = Array.Empty<SoulTreeRequirement>();

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public string EffectLabel => effectLabel;
        public Texture2D Icon => icon;
        public Color Accent => accent;
        public int Tier => tier;
        public int MaxLevel => Mathf.Max(1, maxLevel);
        public int BaseCost => baseCost;
        public float CostGrowth => costGrowth;
        public StatModifier[] Modifiers => modifiers;
        public SoulTreeRequirement[] Requires => requires;

        /// <summary>Souls to buy <paramref name="level"/> (1..max) from the level below it; 0 outside that range.</summary>
        public int CostOf(int level)
        {
            if (level < 1 || level > MaxLevel) return 0;
            double raw = baseCost * Math.Pow(Mathf.Max(1f, costGrowth), level - 1);
            return Math.Max(5, (int)Math.Round(raw / 5.0, MidpointRounding.AwayFromZero) * 5);
        }

        /// <summary>Souls spent to reach <paramref name="level"/> from 0 (refund of a reset).</summary>
        public int TotalCost(int level)
        {
            int sum = 0;
            for (int l = 1; l <= Mathf.Min(level, MaxLevel); l++) sum += CostOf(l);
            return sum;
        }

        /// <summary>
        /// Effect at <paramref name="level"/> from the first modifier: "+8% bolt damage" (percent, or a flat 0..1 chance
        /// stat), "+40 max health" (other flat). Empty at level 0 or without modifiers.
        /// </summary>
        public string EffectText(int level)
        {
            if (level <= 0 || modifiers == null || modifiers.Length == 0) return "";
            var m = modifiers[0];
            float v = m.value * level;
            string number = m.kind == ModifierKind.Percent || IsChance(m.stat)
                ? (v * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : v.ToString("0.#", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(effectLabel) ? "+" + number : $"+{number} {effectLabel}";
        }

        private static bool IsChance(StatId stat) =>
            stat == StatId.CritChance || stat == StatId.LifeSteal || stat == StatId.ChestChance || stat == StatId.ChestEliteChance;

        /// <summary>Builder and tests: a node in memory.</summary>
        public static SoulTreeNode Create(string id, string name, int tier, int maxLevel, int baseCost, float costGrowth,
            StatModifier[] perLevel, string effectLabel = "", string description = "", Texture2D icon = null,
            params SoulTreeRequirement[] requires)
        {
            var node = CreateInstance<SoulTreeNode>();
            node.name = "SoulNode_" + id;
            node.Configure(id, name, tier, maxLevel, baseCost, costGrowth, perLevel, effectLabel, description, icon, requires);
            return node;
        }

        /// <summary>Builder: sets every field (keeps the asset object, so references stay).</summary>
        public void Configure(string nodeId, string name, int row, int max, int cost, float growth, StatModifier[] perLevel,
            string label, string text, Texture2D iconTexture, SoulTreeRequirement[] prerequisites)
        {
            id = nodeId;
            displayName = name;
            tier = Mathf.Max(0, row);
            maxLevel = Mathf.Max(1, max);
            baseCost = Mathf.Max(1, cost);
            costGrowth = Mathf.Max(1f, growth);
            modifiers = perLevel ?? Array.Empty<StatModifier>();
            effectLabel = label ?? "";
            description = text ?? "";
            icon = iconTexture;
            requires = prerequisites ?? Array.Empty<SoulTreeRequirement>();
        }

        public void SetAccent(Color color) => accent = color;
    }
}
