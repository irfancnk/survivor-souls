using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Progression
{
    /// <summary>
    /// The screen that opens on each level-up while the game is paused (<see cref="LevelUpFlow"/>). KAN-19 implements the
    /// 3-card skill choice as a subclass; until then the HUD uses a short "LEVEL N" banner. Runs on unscaled time.
    /// </summary>
    public abstract class LevelUpChoice : MonoBehaviour
    {
        /// <summary>Opens the choice for <paramref name="level"/>. Call <paramref name="onDone"/> exactly once, after the pick.</summary>
        public abstract void Present(int level, Action onDone);
    }
}
