using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Meta;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Economy
{
    /// <summary>
    /// Economy data assets (KAN-97): the free Ember Shard sources (<see cref="GemRewardConfig"/>, welcome / first-run bonus
    /// and Rare chest shards). Made once with the first guesses, never overwritten, so Inspector tuning stays. Used by
    /// GameSceneBuilder (run end bonus, chest shards) and MainMenuSceneBuilder (welcome bonus).
    /// Soul tree (KAN-98): <see cref="SoulTree"/> + one <see cref="SoulTreeNode"/> asset per node, made the same way (only
    /// when missing; a missing node is added to an existing tree). Node icons reuse the in-run skill icons.
    /// Store (KAN-99): <see cref="StoreCatalog"/> with the shipped packs and shard offers (<see cref="StoreCatalog.CreateDefault"/>),
    /// made once and never overwritten. Used by MainMenuSceneBuilder (store page).
    /// </summary>
    public static class EconomyBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Economy";
        public const string GemRewardsPath = DataDir + "/GemRewards.asset";
        public const string SoulTreePath = DataDir + "/SoulTree.asset";
        public const string StoreCatalogPath = DataDir + "/StoreCatalog.asset";
        private const string SoulNodeDir = DataDir + "/SoulTree";
        private const string IconDir = "Assets/_Project/Art/Skills/Icons";

        private static readonly Color Ember = new Color(0.93f, 0.45f, 0.2f);
        private static readonly Color Blood = new Color(0.78f, 0.25f, 0.24f);
        private static readonly Color SoulBlue = new Color(0.45f, 0.72f, 0.95f);

        /// <summary>One node of the shipped tree (first balance guesses, see the KAN-98 ticket comment).</summary>
        private readonly struct NodeSpec
        {
            public readonly string Id, Name, Label, Text, Icon;
            public readonly int Tier, Max, Cost;
            public readonly float Growth;
            public readonly StatModifier Modifier;
            public readonly Color Accent;
            public readonly string RequireId;
            public readonly int RequireLevel;

            public NodeSpec(string id, string name, int tier, int max, int cost, float growth, StatModifier modifier,
                string label, string icon, Color accent, string text, string requireId = null, int requireLevel = 0)
            {
                Id = id; Name = name; Tier = tier; Max = max; Cost = cost; Growth = growth; Modifier = modifier;
                Label = label; Icon = icon; Accent = accent; Text = text; RequireId = requireId; RequireLevel = requireLevel;
            }
        }

        // Offense branch (left): Might -> Quickened Draw -> Hawk's Eye -> Cruel Edge. Survival branch (right): Vigor ->
        // Soul Pull / Pilgrim's Stride. Per level a bit under a single in-run pick (Sharpened Bolts L1 = +20 % damage).
        private static readonly NodeSpec[] Specs =
        {
            new NodeSpec("might", "Kindled Might", 0, 5, 250, 1.45f, new StatModifier(StatId.Damage, ModifierKind.Percent, 0.04f),
                "bolt damage", "SharpenedBolts", Ember, "The bonfire hardens every bolt you loose."),
            new NodeSpec("vigor", "Ashen Vigor", 0, 5, 250, 1.45f, new StatModifier(StatId.MaxHealth, ModifierKind.Percent, 0.05f),
                "max health", "Vigor", Blood, "Ash in the blood. You take more before you fall."),
            new NodeSpec("draw", "Quickened Draw", 1, 5, 400, 1.45f, new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, 0.03f),
                "fire rate", "QuickDraw", Ember, "Hands that remember the crank reload faster.", "might", 2),
            new NodeSpec("pull", "Soul Pull", 1, 3, 300, 1.5f, new StatModifier(StatId.PickupRadius, ModifierKind.Percent, 0.1f),
                "soul reach", "SoulSiphon", SoulBlue, "Loose souls drift to you from farther away.", "vigor", 1),
            new NodeSpec("stride", "Pilgrim's Stride", 1, 3, 400, 1.5f, new StatModifier(StatId.MoveSpeed, ModifierKind.Percent, 0.03f),
                "move speed", "FleetFoot", Blood, "Long roads made your step light.", "vigor", 2),
            new NodeSpec("eye", "Hawk's Eye", 2, 5, 600, 1.45f, new StatModifier(StatId.CritChance, ModifierKind.Flat, 0.02f),
                "crit chance", "KeenEye", Ember, "You see the gap in the armour before it opens.", "draw", 2),
            new NodeSpec("edge", "Cruel Edge", 3, 3, 800, 1.5f, new StatModifier(StatId.CritDamage, ModifierKind.Percent, 0.05f),
                "crit damage", "PiercingBolts", Ember, "Critical bolts bite deeper.", "eye", 1),
        };

        public static GemRewardConfig LoadOrCreateGemRewards()
        {
            var config = AssetDatabase.LoadAssetAtPath<GemRewardConfig>(GemRewardsPath);
            if (config != null) return config;
            EditorAssetUtil.EnsureFolder(DataDir);
            config = GemRewardConfig.Create(); // welcome 50, first run 25, Rare chest 35 % for 3-8
            AssetDatabase.CreateAsset(config, GemRewardsPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>The store catalog asset (KAN-99). Made with the shipped packs and offers only when missing.</summary>
        public static StoreCatalog LoadOrCreateStoreCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StoreCatalog>(StoreCatalogPath);
            if (catalog != null) return catalog;
            EditorAssetUtil.EnsureFolder(DataDir);
            catalog = StoreCatalog.CreateDefault();
            AssetDatabase.CreateAsset(catalog, StoreCatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>The soul tree asset with every shipped node. Missing assets are made; existing ones are not changed.</summary>
        public static SoulTree LoadOrCreateSoulTree()
        {
            EditorAssetUtil.EnsureFolder(SoulNodeDir);
            var nodes = new SoulTreeNode[Specs.Length];
            bool created = false;
            for (int i = 0; i < Specs.Length; i++)
            {
                string path = $"{SoulNodeDir}/SoulNode_{Specs[i].Id}.asset";
                nodes[i] = AssetDatabase.LoadAssetAtPath<SoulTreeNode>(path);
                if (nodes[i] != null) continue;
                var spec = Specs[i];
                var node = ScriptableObject.CreateInstance<SoulTreeNode>();
                var requires = string.IsNullOrEmpty(spec.RequireId)
                    ? System.Array.Empty<SoulTreeRequirement>()
                    : new[] { new SoulTreeRequirement(FindNode(nodes, spec.RequireId), spec.RequireLevel) };
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconDir}/T_Skill_{spec.Icon}.png");
                node.Configure(spec.Id, spec.Name, spec.Tier, spec.Max, spec.Cost, spec.Growth, new[] { spec.Modifier },
                    spec.Label, spec.Text, icon, requires);
                node.SetAccent(spec.Accent);
                AssetDatabase.CreateAsset(node, path);
                nodes[i] = node;
                created = true;
            }

            var tree = AssetDatabase.LoadAssetAtPath<SoulTree>(SoulTreePath);
            if (tree == null)
            {
                tree = SoulTree.Create(nodes);
                AssetDatabase.CreateAsset(tree, SoulTreePath);
                created = true;
            }
            else
            {
                foreach (var node in nodes)
                    if (!tree.Contains(node)) { AddNode(tree, node); created = true; }
            }
            if (created) AssetDatabase.SaveAssets();
            return tree;
        }

        private static SoulTreeNode FindNode(SoulTreeNode[] nodes, string id)
        {
            foreach (var n in nodes)
                if (n != null && n.Id == id) return n;
            return null;
        }

        private static void AddNode(SoulTree tree, SoulTreeNode node)
        {
            var list = new System.Collections.Generic.List<SoulTreeNode>(tree.Nodes) { node };
            tree.SetNodes(list.ToArray());
            EditorUtility.SetDirty(tree);
        }
    }
}
