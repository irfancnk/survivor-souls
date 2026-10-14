using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Progression
{
    /// <summary>
    /// What a Reliquary chest offers on the upgrade screen (KAN-73): how many owned skills to show, how many levels the
    /// pick gives, and whether it is a Rare chest (title, colour). Queued by <see cref="LevelUpFlow.EnqueueUpgrade"/>.
    /// </summary>
    [Serializable]
    public struct UpgradeOffer
    {
        public int Choices;
        public int Levels;
        public bool Rare;

        public UpgradeOffer(int choices, int levels, bool rare)
        {
            Choices = Mathf.Max(1, choices);
            Levels = Mathf.Max(1, levels);
            Rare = rare;
        }
    }

    /// <summary>
    /// The screen that opens on each level-up while the game is paused (<see cref="LevelUpFlow"/>). KAN-19 implements the
    /// 3-card skill choice as a subclass; until then the HUD uses a short "LEVEL N" banner. Runs on unscaled time.
    /// The same screen shows a chest's upgrade choice (KAN-73, <see cref="PresentUpgrade"/>): one modal, one card layout.
    /// </summary>
    public abstract class LevelUpChoice : MonoBehaviour
    {
        /// <summary>Opens the choice for <paramref name="level"/>. Call <paramref name="onDone"/> exactly once, after the pick.</summary>
        public abstract void Present(int level, Action onDone);

        /// <summary>
        /// Opens a chest's upgrade choice (owned skills that can still level up). Returns false, without calling
        /// <paramref name="onDone"/>, when there is nothing to upgrade (the flow then gives the fallback reward). Otherwise
        /// call <paramref name="onDone"/> exactly once, after the pick. Default: no upgrade screen.
        /// </summary>
        public virtual bool PresentUpgrade(UpgradeOffer offer, Action onDone) => false;
    }
}
