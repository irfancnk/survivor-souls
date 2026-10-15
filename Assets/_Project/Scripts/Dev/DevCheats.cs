#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Debug cheats for the open Game scene (KAN-87), shared by the Editor menus ("Survivor Souls/Debug/...") and the
    /// in-game touch panel (<see cref="DevDebugPanel"/>), so both run the same code. Each cheat only calls gameplay APIs
    /// (<see cref="SkillManager.SetLevel(SkillDefinition, int)"/>, <see cref="ChestDropper.DropNow"/>, <see cref="XpManager.AddXp"/>,
    /// <see cref="SurvivalTimer.SetRemaining"/>, boss damage, <see cref="RunState.JumpToChamber"/>); none holds game rules.
    /// Each returns false (does nothing) when the scene has no target for it.
    /// Compiled only into the Editor and development players: a release build has no cheats.
    /// </summary>
    public static class DevCheats
    {
        /// <summary>A chest drops this far ahead of the Knight.</summary>
        public const float ChestAhead = 2.5f;

        public static SkillManager Skills => Object.FindAnyObjectByType<SkillManager>();

        /// <summary>
        /// Every skill a run can hold: the progression table's tier unlocks, then owned skills not in the table.
        /// Table order, no duplicates. Fills <paramref name="into"/> (cleared first).
        /// </summary>
        public static void CollectSkills(SkillManager manager, List<SkillDefinition> into)
        {
            into.Clear();
            if (manager == null) return;
            var table = manager.Progression;
            if (table != null)
                foreach (var tier in table.Tiers)
                    if (tier?.unlocks != null)
                        foreach (var s in tier.unlocks)
                            if (s != null && !into.Contains(s)) into.Add(s);
            foreach (var o in manager.OwnedSkills)
                if (o.Skill != null && !into.Contains(o.Skill)) into.Add(o.Skill);
        }

        /// <summary>Debug set level (0 removes, clamped to the skill's max).</summary>
        public static bool SetSkillLevel(SkillDefinition skill, int level)
        {
            var manager = Skills;
            return manager != null && skill != null && manager.SetLevel(skill, level);
        }

        /// <summary>A chest of <paramref name="tier"/> <see cref="ChestAhead"/> m ahead of the Knight (KAN-75), counted as a skill chest.</summary>
        public static ReliquaryChest DropChest(ChestTier tier)
        {
            var dropper = Object.FindAnyObjectByType<ChestDropper>();
            var player = Object.FindAnyObjectByType<PlayerHealth>();
            if (dropper == null || player == null) return null;
            var chest = dropper.DropNow(player.transform.position + player.transform.forward * ChestAhead, tier);
            if (chest == null) Debug.Log("[Chests] Ground full (cap reached).");
            return chest;
        }

        /// <summary>Grants the XP missing to the next level: the level-up burst and the real 3-card pick follow.</summary>
        public static bool LevelUpNow()
        {
            var xp = Object.FindAnyObjectByType<XpManager>();
            if (xp == null) return false;
            xp.AddXp(xp.XpToNext - xp.Xp);
            return true;
        }

        public static bool HasBoss => BossFight.Active != null && BossFight.Active.Boss != null && BossFight.Active.Boss.IsAlive;

        public static bool KillBoss()
        {
            if (!HasBoss) return false;
            var boss = BossFight.Active.Boss;
            boss.TakeDamage(boss.Health + 1f, Vector3.zero);
            return true;
        }

        /// <summary>Lowers the boss to <paramref name="share"/> of max health (as damage: phase 2 and the HUD react).</summary>
        public static bool SetBossHealth(float share)
        {
            if (!HasBoss) return false;
            var boss = BossFight.Active.Boss;
            if (boss.Health > boss.MaxHealth * share) boss.TakeDamage(boss.Health - boss.MaxHealth * share, Vector3.zero);
            return true;
        }

        public static bool InGameScene => Object.FindAnyObjectByType<ChamberSetup>() != null;

        /// <summary>Reloads the Game scene as the next boss Chamber of this run (the first one after the last). Returns that Chamber, 0 when not in the Game scene.</summary>
        public static int JumpToBossChamber()
        {
            var setup = Object.FindAnyObjectByType<ChamberSetup>();
            if (setup == null) return 0;
            var difficulty = setup.Difficulty;
            int chamber = difficulty != null ? difficulty.NextBossChamber(RunState.Chamber) : 5;
            RunState.JumpToChamber(chamber);
            GamePause.Clear();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            Debug.Log($"[DevCheats] Jump to boss Chamber {chamber}");
            return chamber;
        }

        public static bool HasPortalTimer => SurvivalTimer.Active != null;

        public static bool PortalTimerTo(float seconds)
        {
            if (SurvivalTimer.Active == null) return false;
            SurvivalTimer.Active.SetRemaining(seconds);
            return true;
        }

        public static bool OpenPortalNow()
        {
            if (SurvivalTimer.Active == null) return false;
            SurvivalTimer.Active.Complete();
            return true;
        }
    }
}
#endif
