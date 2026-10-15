using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Meta
{
    /// <summary>Why a node level can or cannot be bought now.</summary>
    public enum SoulTreeBuyCheck { Ok, MaxLevel, Locked, NotEnoughSouls, Unknown }

    /// <summary>
    /// The soul tree (KAN-98, third slice of the KAN-89 economy): the nodes and the buy rules. A level can be bought when
    /// the node is below its max, every prerequisite node is at its required level, and the <see cref="SoulWallet"/>
    /// covers <see cref="SoulTreeNode.CostOf"/>. Owned levels are saved by <see cref="SoulTreeProgress"/> and applied to
    /// the Knight at the start of every run as permanent <see cref="PlayerStats"/> sources (<see cref="ApplyTo"/>,
    /// called by <see cref="SoulTreeBonuses"/>). Asset: <c>Data/Economy/SoulTree.asset</c> (EconomyBuilder).
    /// </summary>
    public sealed class SoulTree : ScriptableObject
    {
        [SerializeField] private SoulTreeNode[] nodes = Array.Empty<SoulTreeNode>();

        /// <summary>A level was bought: node, new level.</summary>
        public static event Action<SoulTreeNode, int> Bought;

        public IReadOnlyList<SoulTreeNode> Nodes => nodes;

        /// <summary>Highest tier (row) of any node; -1 when empty.</summary>
        public int MaxTier
        {
            get
            {
                int max = -1;
                foreach (var n in nodes)
                    if (n != null) max = Mathf.Max(max, n.Tier);
                return max;
            }
        }

        public SoulTreeNode Find(string id)
        {
            foreach (var n in nodes)
                if (n != null && n.Id == id) return n;
            return null;
        }

        public bool Contains(SoulTreeNode node) => node != null && Array.IndexOf(nodes, node) >= 0;

        /// <summary>Every prerequisite of <paramref name="node"/> is at its required level.</summary>
        public static bool IsUnlocked(SoulTreeNode node)
        {
            if (node == null) return false;
            foreach (var r in node.Requires)
                if (r.node != null && SoulTreeProgress.Level(r.node) < Mathf.Max(1, r.level)) return false;
            return true;
        }

        /// <summary>First prerequisite not met yet (null node when all are met).</summary>
        public static SoulTreeRequirement MissingRequirement(SoulTreeNode node)
        {
            if (node != null)
                foreach (var r in node.Requires)
                    if (r.node != null && SoulTreeProgress.Level(r.node) < Mathf.Max(1, r.level)) return r;
            return default;
        }

        /// <summary>Souls for the next level of <paramref name="node"/>; 0 at max level.</summary>
        public static int NextCost(SoulTreeNode node)
        {
            if (node == null) return 0;
            int level = SoulTreeProgress.Level(node);
            return level >= node.MaxLevel ? 0 : node.CostOf(level + 1);
        }

        /// <summary>Checks in order: in this tree, max level, prerequisites, wallet.</summary>
        public SoulTreeBuyCheck Check(SoulTreeNode node)
        {
            if (!Contains(node)) return SoulTreeBuyCheck.Unknown;
            if (SoulTreeProgress.Level(node) >= node.MaxLevel) return SoulTreeBuyCheck.MaxLevel;
            if (!IsUnlocked(node)) return SoulTreeBuyCheck.Locked;
            return SoulWallet.CanAfford(NextCost(node)) ? SoulTreeBuyCheck.Ok : SoulTreeBuyCheck.NotEnoughSouls;
        }

        /// <summary>
        /// Buys the next level of <paramref name="node"/>: spends the Souls, saves the level, raises <see cref="Bought"/>.
        /// Nothing changes when <see cref="Check"/> is not Ok.
        /// </summary>
        public SoulTreeBuyCheck TryBuy(SoulTreeNode node)
        {
            var check = Check(node);
            if (check != SoulTreeBuyCheck.Ok) return check;
            if (!SoulWallet.TrySpend(NextCost(node))) return SoulTreeBuyCheck.NotEnoughSouls;
            int level = SoulTreeProgress.Level(node) + 1;
            SoulTreeProgress.SetLevel(node, level);
            Bought?.Invoke(node, level);
            return SoulTreeBuyCheck.Ok;
        }

        /// <summary>
        /// Writes every owned node to <paramref name="stats"/> as a permanent source (modifiers x level); nodes at level 0
        /// are removed. Returns the number of owned nodes.
        /// </summary>
        public int ApplyTo(PlayerStats stats)
        {
            if (stats == null) return 0;
            int owned = 0;
            foreach (var node in nodes)
            {
                if (node == null) continue;
                int level = SoulTreeProgress.Level(node);
                stats.SetSource(node, node.Modifiers, level, permanent: true);
                if (level > 0) owned++;
            }
            return owned;
        }

        /// <summary>Souls spent on the whole tree so far.</summary>
        public int TotalSpent()
        {
            int sum = 0;
            foreach (var node in nodes)
                if (node != null) sum += node.TotalCost(SoulTreeProgress.Level(node));
            return sum;
        }

        /// <summary>Souls to buy every level of every node.</summary>
        public int FullCost()
        {
            int sum = 0;
            foreach (var node in nodes)
                if (node != null) sum += node.TotalCost(node.MaxLevel);
            return sum;
        }

        /// <summary>Debug: every node back to 0, the spent Souls back to the wallet. Returns the refund.</summary>
        public int ResetAndRefund()
        {
            int refund = TotalSpent();
            SoulTreeProgress.ResetAll(this);
            SoulWallet.Earn(refund);
            return refund;
        }

        /// <summary>Builder and tests.</summary>
        public static SoulTree Create(params SoulTreeNode[] treeNodes)
        {
            var tree = CreateInstance<SoulTree>();
            tree.name = "SoulTree";
            tree.SetNodes(treeNodes);
            return tree;
        }

        public void SetNodes(SoulTreeNode[] treeNodes) => nodes = treeNodes ?? Array.Empty<SoulTreeNode>();
    }
}
