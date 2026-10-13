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

        /// <summary>
        /// iOS Xcode project for device installs. Sign and install with xcodebuild + devicectl
        /// (see STATE.md, "Deploy to iPhone"). Optional <c>-ss-dev</c> makes a development build.
        /// <c>-executeMethod SurvivorSouls.Editor.DevBuildTools.BuildIosXcode -ss-out &lt;dir&gt;</c>
        /// </summary>
        public static void BuildIosXcode()
        {
            string outDir = GetArg("-ss-out") ?? "Builds/iOS";
            bool dev = System.Environment.GetCommandLineArgs().Contains("-ss-dev");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outDir,
                target = BuildTarget.iOS,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[DevBuildTools] iOS {report.summary.result} errors={report.summary.totalErrors} -> {outDir}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Android APK for sideload checks (adb install). Optional <c>-ss-dev</c> makes a development build.
        /// <c>-buildTarget Android -executeMethod SurvivorSouls.Editor.DevBuildTools.BuildAndroidApk -ss-out &lt;file.apk&gt;</c>
        /// </summary>
        public static void BuildAndroidApk()
        {
            string outPath = GetArg("-ss-out") ?? "Builds/Android/SurvivorSouls.apk";
            bool dev = System.Environment.GetCommandLineArgs().Contains("-ss-dev");
            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[DevBuildTools] Android {report.summary.result} errors={report.summary.totalErrors} -> {outPath}");
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
