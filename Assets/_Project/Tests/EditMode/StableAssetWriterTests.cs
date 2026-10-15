using System.IO;
using NUnit.Framework;
using SurvivorSouls.Editor;

namespace SurvivorSouls.Tests
{
    /// <summary>Generated assets stay byte-stable across rebuilds: fileID churn is detected and undone (KAN-157).</summary>
    public class StableAssetWriterTests
    {
        private const string Pre = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";

        // Root GameObject (1) with Transform (2) and two same-named children (3/4 -> Transforms 5/6).
        private static string Prefab(long root, long rootT, long a, long aT, long b, long bT, float ax = 1f, bool swapDocs = false)
        {
            string rootGo = $"--- !u!1 &{root}\nGameObject:\n  m_Component:\n  - component: {{fileID: {rootT}}}\n  m_Name: Tile\n";
            string rootTr = $"--- !u!4 &{rootT}\nTransform:\n  m_GameObject: {{fileID: {root}}}\n  m_Children:\n  - {{fileID: {aT}}}\n  - {{fileID: {bT}}}\n  m_Father: {{fileID: 0}}\n";
            string goA = $"--- !u!1 &{a}\nGameObject:\n  m_Component:\n  - component: {{fileID: {aT}}}\n  m_Name: Block\n";
            string trA = $"--- !u!4 &{aT}\nTransform:\n  m_GameObject: {{fileID: {a}}}\n  m_LocalPosition: {{x: {ax}, y: 0, z: 0}}\n  m_Children: []\n  m_Father: {{fileID: {rootT}}}\n";
            string goB = $"--- !u!1 &{b}\nGameObject:\n  m_Component:\n  - component: {{fileID: {bT}}}\n  m_Name: Block\n";
            string trB = $"--- !u!4 &{bT}\nTransform:\n  m_GameObject: {{fileID: {b}}}\n  m_LocalPosition: {{x: 2, y: 0, z: 0}}\n  m_Children: []\n  m_Father: {{fileID: {rootT}}}\n";
            return swapDocs
                ? Pre + rootGo + rootTr + goB + trB + goA + trA
                : Pre + rootGo + rootTr + goA + trA + goB + trB;
        }

        [Test]
        public void Identical_AreEquivalent()
        {
            string a = Prefab(100, 101, 200, 201, 300, 301);
            Assert.IsTrue(UnityYamlCanon.AreEquivalent(a, a));
        }

        [Test]
        public void RenamedChildIdsAndNewOrder_AreEquivalent()
        {
            string old = Prefab(100, 101, 200, 201, 300, 301);
            string rebuilt = Prefab(100, 101, 9_876_543_210, -42, 77, 5_555, swapDocs: true);
            Assert.IsTrue(UnityYamlCanon.AreEquivalent(old, rebuilt));
        }

        [Test]
        public void ChangedValue_IsNotEquivalent()
        {
            string old = Prefab(100, 101, 200, 201, 300, 301);
            string changed = Prefab(100, 101, 200, 201, 300, 301, ax: 1.5f);
            Assert.IsFalse(UnityYamlCanon.AreEquivalent(old, changed));
        }

        [Test]
        public void SwappedChildOrder_IsNotEquivalent()
        {
            // Same documents, but the root lists child B before child A: hierarchy order is real content.
            string old = Prefab(100, 101, 200, 201, 300, 301);
            string swapped = Prefab(100, 101, 300, 301, 200, 201).Replace("x: 2,", "x: TMP,").Replace("x: 1,", "x: 2,").Replace("x: TMP,", "x: 1,");
            Assert.IsFalse(UnityYamlCanon.AreEquivalent(old, swapped));
        }

        [Test]
        public void NewRootId_IsNotEquivalent()
        {
            // Other assets reference a prefab by its root GameObject fileID: a new root id must stay.
            string old = Prefab(100, 101, 200, 201, 300, 301);
            string newRoot = Prefab(111, 101, 200, 201, 300, 301);
            Assert.IsFalse(UnityYamlCanon.AreEquivalent(old, newRoot));
        }

        [Test]
        public void ExternalReferences_StayLiteral()
        {
            string Ext(long id) => Pre + $"--- !u!1102 &1\nAnimatorState:\n  m_Motion: {{fileID: {id}, guid: abc, type: 2}}\n";
            Assert.IsTrue(UnityYamlCanon.AreEquivalent(Ext(7400000), Ext(7400000)));
            Assert.IsFalse(UnityYamlCanon.AreEquivalent(Ext(7400000), Ext(7400002)));
        }

        [Test]
        public void ExtraDocument_IsNotEquivalent()
        {
            string old = Prefab(100, 101, 200, 201, 300, 301);
            string extra = old + "--- !u!114 &999\nMonoBehaviour:\n  m_Enabled: 1\n";
            Assert.IsFalse(UnityYamlCanon.AreEquivalent(old, extra));
        }

        [Test]
        public void Settle_RestoresChurnOnly_KeepsRealChange()
        {
            const string dir = "Temp/StableAssetWriterTests"; // outside Assets: no import
            string churn = dir + "/Churn.prefab";
            string real = dir + "/Real.prefab";
            Directory.CreateDirectory(dir);
            try
            {
                string original = Prefab(100, 101, 200, 201, 300, 301);
                File.WriteAllText(churn, original);
                File.WriteAllText(real, original);

                StableAssetWriter.Remember(churn);
                StableAssetWriter.Remember(real);
                File.WriteAllText(churn, Prefab(100, 101, 900, 901, 800, 801, swapDocs: true));
                string changed = Prefab(100, 101, 200, 201, 300, 301, ax: 3f);
                File.WriteAllText(real, changed);

                StableAssetWriter.SettleRemembered(); // no SaveAssets: other tests' loaded assets stay untouched

                Assert.AreEqual(original, File.ReadAllText(churn), "fileID churn only: old bytes come back");
                Assert.AreEqual(changed, File.ReadAllText(real), "a real change stays");
                Assert.AreEqual(1, StableAssetWriter.LastRestored);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
