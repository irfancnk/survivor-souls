using System;
using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Meta
{
    /// <summary>
    /// Saved soul tree levels (KAN-98): one <see cref="CheckedPrefInt"/> per node, key <c>soultree.&lt;id&gt;</c>, salt from
    /// the id (a level + checksum pair copied to another node fails). A level that fails its checksum (hand-edited
    /// prefs) reads as 0, is written back as 0 and sets <see cref="TamperDetected"/>. Levels above the node's max read as
    /// the max. Saved on every buy (rare), so they survive app restarts. Basic anti-tamper only, like the wallets.
    /// </summary>
    public static class SoulTreeProgress
    {
        public const string KeyPrefix = "soultree.";
        private const uint SaltBase = 0x50D17EE5u;

        /// <summary>Node whose level changed (buy, reset, debug).</summary>
        public static event Action<SoulTreeNode> Changed;

        /// <summary>A saved level failed its checksum (reset to 0). Cleared by <see cref="ResetAll"/>.</summary>
        public static bool TamperDetected { get; private set; }

        public static string KeyOf(SoulTreeNode node) => KeyPrefix + node.Id;

        /// <summary>Owned level of <paramref name="node"/> (0 = not owned, null node, or tampered save).</summary>
        public static int Level(SoulTreeNode node)
        {
            if (node == null) return 0;
            var store = Store(node);
            int value = store.Read(0, out bool tampered);
            if (tampered)
            {
                TamperDetected = true;
                Debug.LogWarning($"[SoulTree] Saved level of '{node.Id}' fails its checksum: reset to 0.");
                store.Write(0);
                return 0;
            }
            return Mathf.Min(value, node.MaxLevel);
        }

        /// <summary>Saves <paramref name="level"/> (clamped to 0..max) and raises <see cref="Changed"/>.</summary>
        public static void SetLevel(SoulTreeNode node, int level)
        {
            if (node == null) return;
            Store(node).Write(Mathf.Clamp(level, 0, node.MaxLevel));
            Changed?.Invoke(node);
        }

        /// <summary>Every node of <paramref name="tree"/> back to 0 (debug, tests). Does not refund: see <see cref="SoulTree.ResetAndRefund"/>.</summary>
        public static void ResetAll(SoulTree tree)
        {
            TamperDetected = false;
            if (tree == null) return;
            foreach (var node in tree.Nodes)
                if (node != null) SetLevel(node, 0);
        }

        /// <summary>Salt of a node: FNV-1a of its id xor a base (stable across runs and platforms).</summary>
        public static uint SaltOf(string id)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in id ?? "")
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash ^ SaltBase;
            }
        }

        private static CheckedPrefInt Store(SoulTreeNode node) => new CheckedPrefInt(KeyOf(node), SaltOf(node.Id));
    }
}
