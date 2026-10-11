using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SurvivorSouls.Editor
{
    /// <summary>
    /// Batchmode build helpers for automated checks.
    /// Mac dev build (pair with DevScreenshot):
    /// <c>-executeMethod SurvivorSouls.Editor.DevBuildTools.BuildMacDev -ss-out &lt;dir&gt;</c>
    /// </summary>
    public static class DevBuildTools
    {
        public static void BuildMacDev()
        {
            string outDir = GetArg("-ss-out") ?? "Builds/MacDev";
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(outDir, "SurvivorSouls.app"),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[DevBuildTools] {report.summary.result} errors={report.summary.totalErrors} -> {options.locationPathName}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
