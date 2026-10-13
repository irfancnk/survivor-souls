using System;
using System.Linq;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Play-mode skill debugger (KAN-19): every <see cref="SkillDefinition"/> asset with its stacks and Grant / Max buttons,
    /// a forced level-up (opens the real 3-card pick), run reset, and the live <see cref="PlayerStats"/> sheet.
    /// Menu: Survivor Souls/Debug/Skills. Quick grant without the window: Survivor Souls/Debug/Grant Random Skill.
    /// Offer odds (KAN-37): "Survivor Souls/Debug/Print Skill Offer Odds" (edit or Play mode) rolls 1000 offers at levels
    /// 2, 4, 6 and 10 with the shipped table and prints the share per tier, Rare+ offers and pity swaps to the console.
    /// </summary>
    public sealed class SkillDebugWindow : EditorWindow
    {
        private SkillDefinition[] _skills = new SkillDefinition[0];
        private Vector2 _scroll;
        private int _oddsLevel = 4;
        private string _odds = "";
        public const int OddsRolls = 1000;
        private static readonly int[] OddsLevels = { 2, 4, 6, 10 };

        [MenuItem("Survivor Souls/Debug/Skills")]
        private static void Open() => GetWindow<SkillDebugWindow>("Skills").Show();

        [MenuItem("Survivor Souls/Debug/Grant Random Skill")]
        private static void GrantRandom()
        {
            var manager = FindManager();
            if (manager == null) return;
            var open = LoadSkills().Where(manager.CanTake).ToArray();
            if (open.Length == 0) { Debug.Log("[Skills] Every skill is maxed."); return; }
            var pick = open[UnityEngine.Random.Range(0, open.Length)];
            manager.Grant(pick);
            Debug.Log($"[Skills] Granted {pick.Id} -> {manager.StacksOf(pick)}/{pick.MaxStack}");
        }

        [MenuItem("Survivor Souls/Debug/Grant Random Skill", true)]
        [MenuItem("Survivor Souls/Debug/Level Up Now", true)]
        private static bool InPlayMode() => Application.isPlaying;

        [MenuItem("Survivor Souls/Debug/Print Skill Offer Odds")]
        private static void PrintOdds()
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
            if (table == null) { Debug.LogWarning($"[Skills] Missing {SkillsBuilder.ProgressionPath}"); return; }
            var lines = OddsLevels.Select(l => SimulateFresh(table, l).ToString());
            Debug.Log($"[Skills] Offer odds, {OddsRolls} offers per level, no skills owned:\n" + string.Join("\n", lines));
        }

        /// <summary>
        /// Rolls <see cref="OddsRolls"/> offers at <paramref name="level"/> on a throw-away SkillManager (nothing owned, no
        /// scene needed), so the numbers show the table itself.
        /// </summary>
        public static SkillManager.OfferStats SimulateFresh(SkillProgression table, int level, int seed = 37)
        {
            var go = new GameObject("Skill Odds (temp)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var manager = go.AddComponent<SkillManager>();
                manager.Setup(table, null, null, null, go.transform, seed);
                return manager.SimulateOffers(OddsRolls, level);
            }
            finally
            {
                DestroyImmediate(go);
            }
        }

        [MenuItem("Survivor Souls/Debug/Level Up Now")]
        private static void LevelUpNow()
        {
            var xp = FindAnyObjectByType<XpManager>();
            if (xp != null) xp.AddXp(xp.XpToNext - xp.Xp);
        }

        private void OnEnable()
        {
            _skills = LoadSkills();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        private void OnPlayModeChanged(PlayModeStateChange _) => Repaint();

        // Stacks change at runtime: keep the window live while playing.
        private void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            DrawOdds();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode in the Game scene to grant skills.", MessageType.Info);
                if (GUILayout.Button("Reload skill assets")) _skills = LoadSkills();
                return;
            }
            var manager = FindAnyObjectByType<SkillManager>();
            if (manager == null)
            {
                EditorGUILayout.HelpBox("No SkillManager in the open scene.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Level up now")) LevelUpNow();
                if (GUILayout.Button("Grant random")) GrantRandom();
                if (GUILayout.Button("Reset run skills")) manager.ResetRun();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
            foreach (var skill in _skills)
            {
                if (skill == null) continue;
                int stacks = manager.StacksOf(skill);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(skill.Icon, GUILayout.Width(22), GUILayout.Height(22));
                    EditorGUILayout.LabelField($"{skill.DisplayName}  ({skill.Rarity})", GUILayout.MinWidth(160));
                    EditorGUILayout.LabelField($"{stacks}/{skill.MaxStack}", GUILayout.Width(40));
                    using (new EditorGUI.DisabledScope(!manager.CanTake(skill)))
                    {
                        if (GUILayout.Button("+1", GUILayout.Width(36))) manager.Grant(skill);
                        if (GUILayout.Button("Max", GUILayout.Width(40)))
                            while (manager.Grant(skill)) { }
                    }
                }
            }

            var stats = manager.Stats;
            if (stats != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Player stats (flat / percent)", EditorStyles.boldLabel);
                foreach (StatId id in Enum.GetValues(typeof(StatId)))
                {
                    float flat = stats.Flat(id), pct = stats.Percent(id);
                    if (flat == 0f && pct == 0f) continue;
                    EditorGUILayout.LabelField(id.ToString(), $"{flat:+0.##;-0.##;0}  /  {pct * 100f:+0.#;-0.#;0}%");
                }
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Offer odds box: in Play mode the live run (owned skills count), else the shipped table from scratch.</summary>
        private void DrawOdds()
        {
            EditorGUILayout.LabelField($"Offer odds ({OddsRolls} offers)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _oddsLevel = Mathf.Max(1, EditorGUILayout.IntField("Level", _oddsLevel));
                if (GUILayout.Button("Roll", GUILayout.Width(60)))
                {
                    var live = Application.isPlaying ? FindAnyObjectByType<SkillManager>() : null;
                    var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
                    var stats = live != null ? live.SimulateOffers(OddsRolls, _oddsLevel) : table != null ? SimulateFresh(table, _oddsLevel) : null;
                    _odds = stats != null ? (live != null ? "Live run: " : "Fresh run: ") + stats : "No skill table.";
                    Debug.Log("[Skills] " + _odds);
                }
            }
            if (!string.IsNullOrEmpty(_odds)) EditorGUILayout.HelpBox(_odds, MessageType.None);
            EditorGUILayout.Space();
        }

        private static SkillManager FindManager()
        {
            var m = FindAnyObjectByType<SkillManager>();
            if (m == null) Debug.LogWarning("[Skills] No SkillManager in the open scene (Play the Game scene).");
            return m;
        }

        private static SkillDefinition[] LoadSkills() =>
            AssetDatabase.FindAssets("t:" + nameof(SkillDefinition))
                .Select(g => AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null)
                .OrderBy(s => s.Rarity).ThenBy(s => s.DisplayName)
                .ToArray();
    }
}
