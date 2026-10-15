using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SurvivorSouls.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SurvivorSouls.Editor.Platform
{
    /// <summary>
    /// Build stamp (KAN-142). Before every build: reads <c>VERSION.json</c> (repo root, written only by
    /// <c>Tools/bump-version.sh</c>) into <see cref="PlayerSettings.bundleVersion"/>, the iOS / macOS build number and the
    /// Android version code, and writes <c>Resources/BuildInfo.json</c> (version, build, short git commit, date) for
    /// <see cref="BuildInfo"/>. After the build it puts the committed values back (ProjectSettings.asset and the
    /// <c>0.0.0-dev</c> placeholder byte for byte), so a build leaves no version diff in git. Failed builds get no
    /// postprocess call: <see cref="DevBuildTools"/> calls <see cref="Restore"/> in a finally.
    /// </summary>
    public sealed class VersionStamp : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string VersionFileName = "VERSION.json";
        public const string BuildInfoPath = "Assets/_Project/Resources/BuildInfo.json";
        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        /// <summary>Contents of VERSION.json.</summary>
        [Serializable]
        public struct VersionFile
        {
            public string version;
            public int build;
        }

        private sealed class Saved
        {
            public string BundleVersion, IosBuild, MacBuild;
            public int AndroidCode;
            public byte[] BuildInfo, ProjectSettings;
        }

        private static Saved _saved;

        public int callbackOrder => -100; // before other build steps read the version

        public void OnPreprocessBuild(BuildReport report)
        {
            VersionFile v = ReadVersionFile();
            string commit = GitCommit();
            Stamp(v, commit, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Debug.Log($"[VersionStamp] {v.version} (build {v.build}) commit {commit} -> {report.summary.platform}");
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Reads VERSION.json from the repo root. Missing or bad file throws (a build must carry a version).</summary>
        public static VersionFile ReadVersionFile(string root = null)
        {
            string path = Path.Combine(root ?? ProjectRoot, VersionFileName);
            if (!File.Exists(path)) throw new BuildFailedException($"[VersionStamp] {path} not found");
            var v = JsonUtility.FromJson<VersionFile>(File.ReadAllText(path));
            if (string.IsNullOrEmpty(v.version) || v.build < 1)
                throw new BuildFailedException($"[VersionStamp] bad {VersionFileName}: version '{v.version}' build {v.build}");
            return v;
        }

        /// <summary>True between <see cref="Stamp"/> and <see cref="Restore"/>.</summary>
        public static bool IsStamped => _saved != null;

        /// <summary>Writes the version into PlayerSettings and BuildInfo.json; remembers the old values once.</summary>
        public static void Stamp(VersionFile v, string commit, string date)
        {
            _saved ??= new Saved
            {
                BundleVersion = PlayerSettings.bundleVersion,
                IosBuild = PlayerSettings.iOS.buildNumber,
                MacBuild = PlayerSettings.macOS.buildNumber,
                AndroidCode = PlayerSettings.Android.bundleVersionCode,
                BuildInfo = File.Exists(BuildInfoPath) ? File.ReadAllBytes(BuildInfoPath) : null,
                ProjectSettings = File.ReadAllBytes(ProjectSettingsPath),
            };
            PlayerSettings.bundleVersion = v.version;
            PlayerSettings.iOS.buildNumber = v.build.ToString();
            PlayerSettings.macOS.buildNumber = v.build.ToString();
            PlayerSettings.Android.bundleVersionCode = v.build;

            var info = new BuildInfo { version = v.version, build = v.build, commit = commit ?? "", date = date ?? "" };
            File.WriteAllText(BuildInfoPath, JsonUtility.ToJson(info) + "\n");
            AssetDatabase.ImportAsset(BuildInfoPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        /// <summary>Puts the values from before <see cref="Stamp"/> back (memory and disk). Safe to call twice.</summary>
        public static void Restore()
        {
            Saved s = _saved;
            if (s == null) return;
            _saved = null;
            PlayerSettings.bundleVersion = s.BundleVersion;
            PlayerSettings.iOS.buildNumber = s.IosBuild;
            PlayerSettings.macOS.buildNumber = s.MacBuild;
            PlayerSettings.Android.bundleVersionCode = s.AndroidCode;

            if (s.BuildInfo != null) File.WriteAllBytes(BuildInfoPath, s.BuildInfo);
            else if (File.Exists(BuildInfoPath)) File.Delete(BuildInfoPath);
            AssetDatabase.ImportAsset(BuildInfoPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            // Memory matches the file again: not dirty, so the editor does not re-save ProjectSettings on exit.
            UnityEngine.Object settings = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath)[0];
            EditorUtility.ClearDirty(settings);
            byte[] now = File.ReadAllBytes(ProjectSettingsPath);
            if (!now.SequenceEqual(s.ProjectSettings)) File.WriteAllBytes(ProjectSettingsPath, s.ProjectSettings);
        }

        /// <summary>Short commit hash of HEAD, "" without git.</summary>
        public static string GitCommit()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = ProjectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                return p.ExitCode == 0 ? output : "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
