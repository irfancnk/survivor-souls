using System;
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
        public const string MobileQuality = "Mobile";
        private const string StandalonePlatform = "Standalone";
        private const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";

        /// <summary>
        /// The Mac dev player also gets the phone quality level (KAN-52), so <c>-ss-quality Mobile</c> renders with
        /// Mobile_RPAsset and the draw-call log matches the phone. The player still starts on the Standalone default (PC).
        /// QualitySettings.asset is restored byte for byte after the build.
        /// </summary>
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
            BuildReport report = null;
            WithQualityLevelOnStandalone(MobileQuality, () => report = Build(options));
            Debug.Log($"[DevBuildTools] {report.summary.result} errors={report.summary.totalErrors} -> {options.locationPathName}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Runs <paramref name="action"/> with the named quality level enabled for Standalone, then puts the
        /// exclusion back and restores QualitySettings.asset on disk (no ProjectSettings diff after a dev build).
        /// Unknown level = runs the action unchanged.
        /// </summary>
        public static void WithQualityLevelOnStandalone(string qualityName, Action action)
        {
            var so = LoadQualitySettings();
            SerializedProperty excluded = FindExcludedPlatforms(so, qualityName);
            int at = excluded == null ? -1 : IndexOf(excluded, StandalonePlatform);
            if (at < 0)
            {
                if (excluded == null) Debug.LogWarning($"[DevBuildTools] No quality level '{qualityName}'");
                action();
                return;
            }

            byte[] onDisk = File.Exists(QualitySettingsPath) ? File.ReadAllBytes(QualitySettingsPath) : null;
            excluded.DeleteArrayElementAtIndex(at);
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[DevBuildTools] Quality level '{qualityName}' enabled for {StandalonePlatform} for this build");
            try
            {
                action();
            }
            finally
            {
                so = LoadQualitySettings();
                excluded = FindExcludedPlatforms(so, qualityName);
                if (excluded != null && IndexOf(excluded, StandalonePlatform) < 0)
                {
                    at = Mathf.Min(at, excluded.arraySize);
                    excluded.InsertArrayElementAtIndex(at);
                    excluded.GetArrayElementAtIndex(at).stringValue = StandalonePlatform;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                // Memory matches the file again: not dirty, so the editor does not re-save (and re-serialize) it on exit.
                EditorUtility.ClearDirty(so.targetObject);
                if (onDisk != null && !File.ReadAllBytes(QualitySettingsPath).SequenceEqual(onDisk))
                    File.WriteAllBytes(QualitySettingsPath, onDisk);
            }
        }

        /// <summary>True when the named quality level is not excluded for Standalone (Mac / PC players).</summary>
        public static bool IsQualityLevelOnStandalone(string qualityName)
        {
            SerializedProperty excluded = FindExcludedPlatforms(LoadQualitySettings(), qualityName);
            return excluded != null && IndexOf(excluded, StandalonePlatform) < 0;
        }

        private static SerializedObject LoadQualitySettings() =>
            new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(QualitySettingsPath)[0]);

        private static SerializedProperty FindExcludedPlatforms(SerializedObject so, string qualityName)
        {
            SerializedProperty levels = so.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                if (level.FindPropertyRelative("name").stringValue == qualityName)
                    return level.FindPropertyRelative("excludedTargetPlatforms");
            }
            return null;
        }

        private static int IndexOf(SerializedProperty stringArray, string value)
        {
            for (int i = 0; i < stringArray.arraySize; i++)
                if (stringArray.GetArrayElementAtIndex(i).stringValue == value) return i;
            return -1;
        }

        /// <summary>
        /// iOS Xcode project for device installs. Sign and install with xcodebuild + devicectl
        /// (see STATE.md, "Deploy to iPhone"). Optional <c>-ss-dev</c> makes a development build; optional
        /// <c>-ss-store</c> makes a store release build (<see cref="PhoneDefines"/>).
        /// <c>-executeMethod SurvivorSouls.Editor.DevBuildTools.BuildIosXcode -ss-out &lt;dir&gt;</c>
        /// </summary>
        public static void BuildIosXcode()
        {
            string outDir = GetArg("-ss-out") ?? "Builds/iOS";
            bool dev = Environment.GetCommandLineArgs().Contains("-ss-dev");
            if (!TryPhoneDefines(dev, out string[] defines)) return;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outDir,
                target = BuildTarget.iOS,
                options = dev ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = defines,
            };
            BuildReport report = Build(options);
            Debug.Log($"[DevBuildTools] iOS {report.summary.result} errors={report.summary.totalErrors} defines=[{string.Join(",", defines)}] -> {outDir}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Android APK for sideload checks (adb install). Optional <c>-ss-dev</c> makes a development build; optional
        /// <c>-ss-store</c> makes a store release build (<see cref="PhoneDefines"/>).
        /// <c>-buildTarget Android -executeMethod SurvivorSouls.Editor.DevBuildTools.BuildAndroidApk -ss-out &lt;file.apk&gt;</c>
        /// </summary>
        public static void BuildAndroidApk()
        {
            string outPath = GetArg("-ss-out") ?? "Builds/Android/SurvivorSouls.apk";
            bool dev = Environment.GetCommandLineArgs().Contains("-ss-dev");
            if (!TryPhoneDefines(dev, out string[] defines)) return;
            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = dev ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = defines,
            };
            BuildReport report = Build(options);
            Debug.Log($"[DevBuildTools] Android {report.summary.result} errors={report.summary.totalErrors} defines=[{string.Join(",", defines)}] -> {outPath}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Extra scripting defines of a phone build (KAN-151). <paramref name="store"/> = store release build: adds
        /// <see cref="Core.TestMode.StoreDefine"/>, so Test Mode is locked off. A store build is never a development build.
        /// </summary>
        public static string[] PhoneDefines(bool store, bool dev)
        {
            if (store && dev) throw new ArgumentException("-ss-store and -ss-dev cannot be used together");
            return store ? new[] { Core.TestMode.StoreDefine } : Array.Empty<string>();
        }

        /// <summary>Reads <c>-ss-store</c> into <see cref="PhoneDefines"/>; on a bad flag mix logs an error and exits 1.</summary>
        private static bool TryPhoneDefines(bool dev, out string[] defines)
        {
            bool store = Environment.GetCommandLineArgs().Contains("-ss-store");
            try
            {
                defines = PhoneDefines(store, dev);
                return true;
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[DevBuildTools] {e.Message}");
                defines = null;
                EditorApplication.Exit(1);
                return false;
            }
        }

        /// <summary>
        /// BuildPipeline.BuildPlayer + version stamp cleanup (KAN-142): <see cref="Platform.VersionStamp"/> stamps in its
        /// preprocess step and restores in postprocess, which a failed build never reaches.
        /// </summary>
        private static BuildReport Build(BuildPlayerOptions options)
        {
            try
            {
                return BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                Platform.VersionStamp.Restore();
            }
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
