using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Balance report (KAN-86): the KAN-75 power curve with the shipped skill table (200 runs, levels 1-20), the
    /// single-target and crowd DPS growth at levels 5 / 10 / 20 against the KAN-86 targets, and the KAN-92 horde scaling
    /// table. Menu "Survivor Souls/Debug/Print Balance Report"; batch:
    /// <c>-executeMethod SurvivorSouls.Editor.Skills.BalanceReport.PrintFromCommandLine</c> (exit code 0, report in the log).
    /// </summary>
    public static class BalanceReport
    {
        public const string HordeScalingPath = "Assets/_Project/Data/Combat/HordeScaling_Realm1.asset";

        [MenuItem("Survivor Souls/Debug/Print Balance Report")]
        private static void Print() => Debug.Log("[Balance] " + Build());

        public static string Build(int runs = 200, int levels = 20)
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
            if (table == null) return $"Missing {SkillsBuilder.ProgressionPath}";
            var curve = SkillPowerCurve.Simulate(table, PowerBase.Default, runs, levels);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(curve.ToString());
            var bare = curve.AtLevel(1);
            sb.AppendLine("Growth over the bare Knight (KAN-86 target at level 20: 1-target 3-4x, crowd 8-10x):");
            foreach (int level in new[] { 5, 10, 15, 20 })
            {
                if (level > levels) break;
                var s = curve.AtLevel(level);
                sb.AppendLine($"  L{level,2}: 1-target {s.SingleTargetDps / bare.SingleTargetDps:0.00}x, crowd {s.CrowdDps / bare.CrowdDps:0.00}x");
            }
            var scaling = AssetDatabase.LoadAssetAtPath<HordeScaling>(HordeScalingPath);
            if (scaling != null) sb.AppendLine(scaling.Table(curve));
            return sb.ToString();
        }

        /// <summary>Batchmode entry point. Exit code 0 on success.</summary>
        public static void PrintFromCommandLine()
        {
            try
            {
                Debug.Log("[Balance] " + Build());
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
