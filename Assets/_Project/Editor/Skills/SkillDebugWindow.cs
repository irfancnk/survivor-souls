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
    /// </summary>
    public sealed class SkillDebugWindow : EditorWindow
    {
        private SkillDefinition[] _skills = new SkillDefinition[0];
        private Vector2 _scroll;

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
