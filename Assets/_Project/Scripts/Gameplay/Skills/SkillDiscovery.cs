using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// Skills the player has found (KAN-64, Skill Guide Book): a skill counts as found once it was taken in a run
    /// (<see cref="SkillManager.Grant(SkillDefinition)"/> at level 1). One PlayerPrefs flag per skill id. The guide shows
    /// every skill for now; with its "hide unfound" option a skill not found yet is a silhouette with "???".
    /// </summary>
    public static class SkillDiscovery
    {
        public const string Prefix = "skill.found.";

        public static bool IsFound(string skillId) => !string.IsNullOrEmpty(skillId) && PlayerPrefs.GetInt(Prefix + skillId, 0) == 1;

        /// <summary>Marks <paramref name="skillId"/> found. Written at the next PlayerPrefs save (menu, pause, quit).</summary>
        public static void Mark(string skillId)
        {
            if (string.IsNullOrEmpty(skillId) || IsFound(skillId)) return;
            PlayerPrefs.SetInt(Prefix + skillId, 1);
        }

        /// <summary>Forgets <paramref name="skillId"/> (tests, dev reset).</summary>
        public static void Forget(string skillId)
        {
            if (!string.IsNullOrEmpty(skillId)) PlayerPrefs.DeleteKey(Prefix + skillId);
        }
    }
}
