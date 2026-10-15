using System.Text;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Meta;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Economy
{
    /// <summary>
    /// Debug menus for the soul tree (KAN-98): print the levels and costs, reset with a refund, max every node. Work in
    /// edit and play mode (PlayerPrefs); in play mode the Knight's bonuses are applied again.
    /// </summary>
    public static class SoulTreeDebugMenu
    {
        [MenuItem("Survivor Souls/Debug/Soul Tree: Print")]
        private static void Print()
        {
            var tree = EconomyBuilder.LoadOrCreateSoulTree();
            var sb = new StringBuilder($"[SoulTree] {tree.TotalSpent()} of {tree.FullCost()} Souls spent, {SoulWallet.Balance} held\n");
            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                int level = SoulTreeProgress.Level(node);
                sb.Append($"  {node.Id}: {level}/{node.MaxLevel} {node.EffectText(level)} next {SoulTree.NextCost(node)}\n");
            }
            Debug.Log(sb.ToString());
        }

        [MenuItem("Survivor Souls/Debug/Soul Tree: Reset + Refund")]
        private static void Reset()
        {
            int refund = EconomyBuilder.LoadOrCreateSoulTree().ResetAndRefund();
            Reapply();
            Debug.Log($"[SoulTree] reset, {refund} Souls back -> {SoulWallet.Balance}");
        }

        [MenuItem("Survivor Souls/Debug/Soul Tree: Max Every Node (free)")]
        private static void MaxAll()
        {
            foreach (var node in EconomyBuilder.LoadOrCreateSoulTree().Nodes)
                if (node != null) SoulTreeProgress.SetLevel(node, node.MaxLevel);
            Reapply();
            Debug.Log("[SoulTree] every node at max level (no Souls spent)");
        }

        private static void Reapply()
        {
            if (!Application.isPlaying) return;
            var bonuses = Object.FindAnyObjectByType<SoulTreeBonuses>();
            if (bonuses != null) bonuses.Apply();
            var menu = Object.FindAnyObjectByType<SurvivorSouls.UI.MainMenu.MainMenuController>();
            menu?.SoulTreePage?.Refresh();
        }
    }
}
