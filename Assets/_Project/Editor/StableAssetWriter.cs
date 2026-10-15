using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SurvivorSouls.Editor
{
    /// <summary>
    /// Keeps generated assets byte-stable across rebuilds (KAN-157). Builders that recreate an asset from scratch
    /// (AnimatorController via CreateAnimatorControllerAtPath, tile prefabs with same-named children, the generated
    /// scenes) get new random local fileIDs on every write, so git shows thousands of changed lines with no real change.
    /// Builders call <see cref="Remember"/> before they write; <see cref="SettleAll"/> (after SaveAssets / SaveScene)
    /// puts the old bytes back for every file whose content is the same as before up to fileID renaming and document order
    /// (<see cref="UnityYamlCanon.AreEquivalent"/>). A real change keeps the new file.
    /// </summary>
    public static class StableAssetWriter
    {
        private static readonly Dictionary<string, byte[]> Before = new Dictionary<string, byte[]>();

        /// <summary>Restored file count of the last <see cref="SettleAll"/> (tests, logs).</summary>
        public static int LastRestored { get; private set; }

        /// <summary>Stores the current bytes of <paramref name="assetPath"/> once per build. Call it before the write.</summary>
        public static void Remember(string assetPath)
        {
            if (Before.ContainsKey(assetPath)) return;
            string full = Path.GetFullPath(assetPath);
            Before[assetPath] = File.Exists(full) ? File.ReadAllBytes(full) : null;
        }

        /// <summary>
        /// Saves dirty assets, then restores the old bytes of every remembered file whose new content is equivalent.
        /// Reimports the restored files and reopens a restored scene that is open, so the editor matches the disk.
        /// </summary>
        public static int SettleAll()
        {
            AssetDatabase.SaveAssets();
            int restored = 0;
            foreach (var pair in Before)
            {
                if (pair.Value == null) continue; // new asset: nothing to keep
                string full = Path.GetFullPath(pair.Key);
                if (!File.Exists(full)) continue;
                string oldText = Encoding.UTF8.GetString(pair.Value);
                string newText = File.ReadAllText(full);
                if (oldText == newText) continue;
                if (!UnityYamlCanon.AreEquivalent(oldText, newText))
                {
                    Debug.Log($"[StableAssetWriter] Changed: {pair.Key}");
                    continue;
                }

                File.WriteAllBytes(full, pair.Value);
                restored++;
                if (!pair.Key.StartsWith("Assets/")) continue; // plain file (tests): nothing to reimport
                AssetDatabase.ImportAsset(pair.Key, ImportAssetOptions.ForceUpdate);
                if (pair.Key.EndsWith(".unity"))
                {
                    var scene = EditorSceneManager.GetSceneByPath(pair.Key);
                    if (scene.IsValid() && scene.isLoaded) EditorSceneManager.OpenScene(pair.Key, OpenSceneMode.Single);
                }
            }
            Before.Clear();
            LastRestored = restored;
            if (restored > 0) Debug.Log($"[StableAssetWriter] Kept {restored} unchanged generated file(s) as they were (fileID churn only).");
            return restored;
        }
    }

    /// <summary>
    /// Compares two Unity text-serialized files (YAML: scene, prefab, controller) up to local fileID renaming and
    /// document order (KAN-157). Each document gets a hash of its text with local references masked, refined a few rounds
    /// with the hashes of the documents it references (Weisfeiler-Lehman style); the files are equivalent when the
    /// multisets of document hashes match. External references (with a guid) stay literal. Prefabs also keep their root
    /// GameObject fileID, because other assets reference the root by that id.
    /// </summary>
    public static class UnityYamlCanon
    {
        private static readonly Regex Header = new Regex(@"^--- !u!(\d+) &(-?\d+)( stripped)?", RegexOptions.Compiled);
        private static readonly Regex LocalRef = new Regex(@"\{fileID: (-?\d+)\}", RegexOptions.Compiled);
        private static readonly Regex Father = new Regex(@"^  m_Father: \{fileID: 0\}", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex GameObjectRef = new Regex(@"^  m_GameObject: \{fileID: (-?\d+)\}", RegexOptions.Compiled | RegexOptions.Multiline);
        private const int MaxRounds = 64;

        private sealed class Doc
        {
            public long Id;
            public string Masked;
            public readonly List<long> Refs = new List<long>();
            public string Body;
        }

        public static bool AreEquivalent(string a, string b)
        {
            if (!Parse(a, out var preambleA, out var docsA) || !Parse(b, out var preambleB, out var docsB)) return false;
            if (preambleA != preambleB || docsA.Count != docsB.Count) return false;
            if (RootGameObject(docsA) != RootGameObject(docsB)) return false;

            var hashA = InitialHashes(docsA);
            var hashB = InitialHashes(docsB);
            for (int round = 0; ; round++)
            {
                if (!SameMultiset(hashA, hashB)) return false;
                if (round >= MaxRounds) return true;
                int distinctBefore = Distinct(hashA);
                hashA = Refine(docsA, hashA);
                hashB = Refine(docsB, hashB);
                if (Distinct(hashA) == distinctBefore && round > 0) return SameMultiset(hashA, hashB);
            }
        }

        private static bool Parse(string text, out string preamble, out List<Doc> docs)
        {
            preamble = null;
            var list = new List<Doc>();
            docs = list;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var pre = new StringBuilder();
            Doc current = null;
            var body = new StringBuilder();
            var ids = new HashSet<long>();

            void Close()
            {
                if (current == null) return;
                current.Body = body.ToString().TrimEnd('\n'); // the file's last document carries the final newline
                list.Add(current);
            }

            foreach (string line in lines)
            {
                var m = Header.Match(line);
                if (m.Success)
                {
                    Close();
                    current = new Doc { Id = long.Parse(m.Groups[2].Value) };
                    if (!ids.Add(current.Id)) return false;
                    body.Clear();
                    body.Append("!u!").Append(m.Groups[1].Value).Append(m.Groups[3].Value).Append('\n');
                    continue;
                }
                if (current == null) pre.Append(line).Append('\n');
                else body.Append(line).Append('\n');
            }
            Close();
            preamble = pre.ToString();

            foreach (var doc in docs)
            {
                doc.Masked = LocalRef.Replace(doc.Body, match =>
                {
                    long id = long.Parse(match.Groups[1].Value);
                    if (id == 0 || !ids.Contains(id)) return match.Value; // null or dangling: literal
                    doc.Refs.Add(id);
                    return "{fileID: #}";
                });
            }
            return true;
        }

        /// <summary>FileID of the GameObject whose Transform has no father, or 0 when the file is not a prefab-like hierarchy with one root.</summary>
        private static long RootGameObject(List<Doc> docs)
        {
            long root = 0;
            int roots = 0;
            foreach (var doc in docs)
            {
                if (!doc.Body.StartsWith("!u!4\n") && !doc.Body.StartsWith("!u!224\n")) continue; // Transform, RectTransform
                if (!Father.IsMatch(doc.Body)) continue;
                var go = GameObjectRef.Match(doc.Body);
                if (!go.Success) continue;
                root = long.Parse(go.Groups[1].Value);
                roots++;
            }
            return roots == 1 ? root : 0;
        }

        private static Dictionary<long, ulong> InitialHashes(List<Doc> docs)
        {
            var h = new Dictionary<long, ulong>(docs.Count);
            foreach (var doc in docs) h[doc.Id] = Fnv(Fnv(14695981039346656037UL, doc.Masked), 0);
            return h;
        }

        private static Dictionary<long, ulong> Refine(List<Doc> docs, Dictionary<long, ulong> prev)
        {
            var h = new Dictionary<long, ulong>(docs.Count);
            foreach (var doc in docs)
            {
                ulong x = prev[doc.Id];
                foreach (long r in doc.Refs) x = Mix(x, prev[r]);
                h[doc.Id] = x;
            }
            return h;
        }

        private static bool SameMultiset(Dictionary<long, ulong> a, Dictionary<long, ulong> b)
        {
            var count = new Dictionary<ulong, int>(a.Count);
            foreach (ulong v in a.Values) count[v] = count.TryGetValue(v, out int n) ? n + 1 : 1;
            foreach (ulong v in b.Values)
            {
                if (!count.TryGetValue(v, out int n) || n == 0) return false;
                count[v] = n - 1;
            }
            return true;
        }

        private static int Distinct(Dictionary<long, ulong> h) => new HashSet<ulong>(h.Values).Count;

        private static ulong Fnv(ulong hash, string s)
        {
            foreach (char c in s)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        private static ulong Fnv(ulong hash, int marker)
        {
            hash ^= (ulong)marker;
            return hash * 1099511628211UL;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
