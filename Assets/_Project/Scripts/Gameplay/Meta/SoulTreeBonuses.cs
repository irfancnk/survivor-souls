using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Meta
{
    /// <summary>
    /// Applies the bought soul tree levels (KAN-98) to the Knight's <see cref="PlayerStats"/> as permanent sources when
    /// the Game scene wakes. Runs before every other script (execution order -400), so health, movement, the shooter and
    /// the horde scaling read the upgraded Knight from their first frame, and Chamber carry (KAN-40) restores skills on
    /// top. On the player (GameSceneBuilder).
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public sealed class SoulTreeBonuses : MonoBehaviour
    {
        [SerializeField] private SoulTree tree;
        [SerializeField] private PlayerStats stats;

        /// <summary>Owned nodes found by the last <see cref="Apply"/>.</summary>
        public int OwnedNodes { get; private set; }
        public SoulTree Tree => tree;

        /// <summary>Tests and code-built scenes: wiring, then apply.</summary>
        public void Setup(SoulTree soulTree, PlayerStats playerStats)
        {
            tree = soulTree;
            stats = playerStats;
            Apply();
        }

        private void Awake()
        {
            if (stats == null) stats = GetComponent<PlayerStats>();
            Apply();
        }

        /// <summary>Writes the saved levels to the stats again (also after a debug change).</summary>
        public void Apply()
        {
            OwnedNodes = tree != null ? tree.ApplyTo(stats) : 0;
            if (OwnedNodes > 0) Debug.Log($"[SoulTree] {OwnedNodes} node(s) applied at run start");
        }
    }
}
