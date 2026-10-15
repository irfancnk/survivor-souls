using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Editor.Platform;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-142 versioning: VERSION.json is valid, the build stamp writes PlayerSettings + BuildInfo.json and puts the
    /// committed values back byte for byte, BuildInfo labels, telemetry carries the version, and
    /// <c>Tools/bump-version.sh</c> follows the bump rules (build +1 always, changelog entry on top).
    /// </summary>
    public class VersionStampTests
    {
        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        [Test]
        public void VersionFileInTheRepoIsValid()
        {
            VersionStamp.VersionFile v = VersionStamp.ReadVersionFile();
            StringAssert.IsMatch(@"^\d+\.\d+\.\d+$", v.version);
            Assert.GreaterOrEqual(v.build, 1);
        }

        [Test]
        public void CommittedBuildInfoIsThePlaceholder()
        {
            BuildInfo info = BuildInfo.Parse(File.ReadAllText(VersionStamp.BuildInfoPath));
            Assert.AreEqual(BuildInfo.PlaceholderVersion, info.version, "a stamped BuildInfo.json must never be committed");
            Assert.AreEqual(0, info.build);
            Assert.AreEqual("", info.commit);
        }

        [Test]
        public void StampWritesTheVersionAndRestorePutsEverythingBack()
        {
            string bundle = PlayerSettings.bundleVersion, ios = PlayerSettings.iOS.buildNumber, mac = PlayerSettings.macOS.buildNumber;
            int android = PlayerSettings.Android.bundleVersionCode;
            byte[] infoBytes = File.ReadAllBytes(VersionStamp.BuildInfoPath);
            byte[] settingsBytes = File.ReadAllBytes(ProjectSettingsPath);
            try
            {
                VersionStamp.Stamp(new VersionStamp.VersionFile { version = "9.8.7", build = 654 }, "abc1234", "2026-10-15T00:00:00Z");

                Assert.IsTrue(VersionStamp.IsStamped);
                Assert.AreEqual("9.8.7", PlayerSettings.bundleVersion);
                Assert.AreEqual("654", PlayerSettings.iOS.buildNumber);
                Assert.AreEqual("654", PlayerSettings.macOS.buildNumber);
                Assert.AreEqual(654, PlayerSettings.Android.bundleVersionCode);
                BuildInfo.Reload();
                BuildInfo stamped = BuildInfo.Current;
                Assert.AreEqual("9.8.7", stamped.version, "the player reads the stamped Resources/BuildInfo.json");
                Assert.AreEqual(654, stamped.build);
                Assert.AreEqual("abc1234", stamped.commit);
                Assert.AreEqual("2026-10-15T00:00:00Z", stamped.date);
            }
            finally
            {
                VersionStamp.Restore();
                BuildInfo.Reload();
            }

            Assert.IsFalse(VersionStamp.IsStamped);
            Assert.AreEqual(bundle, PlayerSettings.bundleVersion);
            Assert.AreEqual(ios, PlayerSettings.iOS.buildNumber);
            Assert.AreEqual(mac, PlayerSettings.macOS.buildNumber);
            Assert.AreEqual(android, PlayerSettings.Android.bundleVersionCode);
            Assert.IsTrue(File.ReadAllBytes(VersionStamp.BuildInfoPath).SequenceEqual(infoBytes), "placeholder restored byte for byte");
            Assert.IsTrue(File.ReadAllBytes(ProjectSettingsPath).SequenceEqual(settingsBytes), "ProjectSettings.asset unchanged on disk");
            Assert.IsFalse(EditorUtility.IsDirty(AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath)[0]),
                "not dirty: the editor must not re-save ProjectSettings on exit");
            Assert.AreEqual(BuildInfo.PlaceholderVersion, BuildInfo.Current.version);
        }

        [Test]
        public void RestoreWithoutAStampDoesNothing()
        {
            string bundle = PlayerSettings.bundleVersion;
            VersionStamp.Restore();
            VersionStamp.Restore();
            Assert.AreEqual(bundle, PlayerSettings.bundleVersion);
            Assert.IsFalse(VersionStamp.IsStamped);
        }

        [Test]
        public void LabelsShowVersionBuildAndCommitOnlyWhenAsked()
        {
            var info = new BuildInfo { version = "0.3.0", build = 14, commit = "a1b2c3d" };
            Assert.AreEqual("v0.3.0 (14)", info.Label(false));
            Assert.AreEqual("v0.3.0 (14) a1b2c3d", info.Label(true));
            Assert.AreEqual("v0.0.0-dev", new BuildInfo().Label(true), "placeholder: no build number, no commit");
        }

        [Test]
        public void BadBuildInfoTextGivesThePlaceholder()
        {
            Assert.AreEqual(BuildInfo.PlaceholderVersion, BuildInfo.Parse(null).version);
            Assert.AreEqual(BuildInfo.PlaceholderVersion, BuildInfo.Parse("not json").version);
            Assert.AreEqual(BuildInfo.PlaceholderVersion, BuildInfo.Parse("{\"build\":3}").version);
            Assert.AreEqual(3, BuildInfo.Parse("{\"version\":\"1.2.3\",\"build\":3}").build);
        }

        [Test]
        public void TelemetryEventsCarryTheVersionAndBuild()
        {
            var e = new TelemetryEvent(GameTelemetry.SkillUpgraded, "test", 0, 1, "test", 0f, Vector3.zero);
            Assert.AreEqual(BuildInfo.Current.version, e.Version);
            Assert.AreEqual(BuildInfo.Current.build, e.Build);
        }

        [Test]
        public void BumpScriptFollowsTheRulesAndWritesTheChangelog()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) Assert.Ignore("bump-version.sh needs bash + python3");
            string dir = Path.Combine(Path.GetTempPath(), "ss-version-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "VERSION.json"), "{\n  \"version\": \"0.2.0\",\n  \"build\": 1\n}\n");
                File.WriteAllText(Path.Combine(dir, "CHANGELOG.md"), "# Changelog\n\nIntro.\n\n## 0.2.0 (build 1) - 2026-10-15\n- KAN-172 First\n");

                Assert.AreEqual("v0.2.1+2", Bump(dir, "patch \"KAN-200 Fix the thing\" \"KAN-201 Polish\""));
                Assert.AreEqual("v0.3.0+3", Bump(dir, "minor \"KAN-202 New skill\""));
                Assert.AreEqual("v1.0.0+4", Bump(dir, "major"));
                VersionStamp.VersionFile v = VersionStamp.ReadVersionFile(dir);
                Assert.AreEqual("1.0.0", v.version);
                Assert.AreEqual(4, v.build);

                string log = File.ReadAllText(Path.Combine(dir, "CHANGELOG.md"));
                StringAssert.StartsWith("# Changelog\n\nIntro.\n\n## 1.0.0 (build 4) - 2026-10-16\n- No ticket listed\n\n## 0.3.0 (build 3)", log);
                StringAssert.Contains("## 0.2.1 (build 2) - 2026-10-16\n- KAN-200 Fix the thing\n- KAN-201 Polish\n\n## 0.2.0 (build 1)", log);
                Assert.AreEqual(4, Regex.Matches(log, "^## ", RegexOptions.Multiline).Count, "one entry per bump, old entries kept");

                Assert.AreNotEqual(0, Run(dir, "sideways", out _), "unknown bump kind fails");
                Assert.AreEqual(4, VersionStamp.ReadVersionFile(dir).build, "a failed bump writes nothing");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static string Bump(string dir, string args)
        {
            Assert.AreEqual(0, Run(dir, args, out string output), output);
            return output.Trim();
        }

        private static int Run(string dir, string args, out string output)
        {
            var psi = new ProcessStartInfo("/bin/bash", $"\"{Path.Combine(VersionStamp.ProjectRoot, "Tools", "bump-version.sh")}\" {args}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["SS_VERSION_ROOT"] = dir;
            psi.EnvironmentVariables["SS_VERSION_DATE"] = "2026-10-16";
            psi.EnvironmentVariables["PATH"] = "/usr/bin:/bin:/usr/local/bin:/opt/homebrew/bin";
            using Process p = Process.Start(psi);
            output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            return p.ExitCode;
        }
    }
}
