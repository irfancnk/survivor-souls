using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor;
using UnityEditor;

namespace SurvivorSouls.Tests
{
    /// <summary>KAN-52: the Mac dev build ships the Mobile quality level and leaves QualitySettings.asset untouched.</summary>
    public class DevBuildToolsTests
    {
        private const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";

        [Test]
        public void MobileLevelIsOnStandaloneOnlyDuringTheBuild()
        {
            bool before = DevBuildTools.IsQualityLevelOnStandalone(DevBuildTools.MobileQuality);
            byte[] onDisk = File.ReadAllBytes(QualitySettingsPath);
            bool during = false;

            DevBuildTools.WithQualityLevelOnStandalone(DevBuildTools.MobileQuality,
                () => during = DevBuildTools.IsQualityLevelOnStandalone(DevBuildTools.MobileQuality));

            Assert.IsTrue(during, "Mobile must be enabled for Standalone while the Mac dev player builds");
            Assert.AreEqual(before, DevBuildTools.IsQualityLevelOnStandalone(DevBuildTools.MobileQuality), "exclusion restored");
            Assert.IsTrue(File.ReadAllBytes(QualitySettingsPath).SequenceEqual(onDisk), "QualitySettings.asset unchanged on disk");
            Assert.IsFalse(EditorUtility.IsDirty(AssetDatabase.LoadAllAssetsAtPath(QualitySettingsPath)[0]),
                "not dirty: the editor must not re-save (re-serialize) the settings on exit");
        }

        [Test]
        public void FailedBuildStillRestoresTheSettings()
        {
            bool before = DevBuildTools.IsQualityLevelOnStandalone(DevBuildTools.MobileQuality);
            byte[] onDisk = File.ReadAllBytes(QualitySettingsPath);

            Assert.Throws<InvalidOperationException>(() => DevBuildTools.WithQualityLevelOnStandalone(
                DevBuildTools.MobileQuality, () => throw new InvalidOperationException("build failed")));

            Assert.AreEqual(before, DevBuildTools.IsQualityLevelOnStandalone(DevBuildTools.MobileQuality));
            Assert.IsTrue(File.ReadAllBytes(QualitySettingsPath).SequenceEqual(onDisk));
        }

        [Test]
        public void UnknownLevelRunsTheBuildUnchanged()
        {
            byte[] onDisk = File.ReadAllBytes(QualitySettingsPath);
            int calls = 0;

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, "[DevBuildTools] No quality level 'NoSuchLevel'");
            DevBuildTools.WithQualityLevelOnStandalone("NoSuchLevel", () => calls++);

            Assert.AreEqual(1, calls);
            Assert.IsFalse(DevBuildTools.IsQualityLevelOnStandalone("NoSuchLevel"));
            Assert.IsTrue(File.ReadAllBytes(QualitySettingsPath).SequenceEqual(onDisk));
        }
    }
}
