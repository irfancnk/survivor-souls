using System;
using System.Linq;
using SurvivorSouls.Core;
using SurvivorSouls.Editor.Chests;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
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
    /// KAN-75: a level slider per skill (<see cref="SkillManager.SetLevel(SkillDefinition, int)"/>: any level 0..max, 0 removes),
    /// chest buttons (drop Light / Rare, open the upgrade screen), drop statistics per Reliquary Hunter level, the power curve
    /// (<see cref="SkillPowerCurve"/>, also "Survivor Souls/Debug/Print Power Curve") and the last telemetry events.
    /// KAN-83 volley menus: "Volley: Double Shot Level 0..3", "Volley: Spread Shot Level 0..3" (Play mode, set level),
    /// "Volley: Print" (the live bolt list) and "Volley: Print Double Shot Stack Odds" (1000 runs, edit or Play mode).
    /// </summary>
    public sealed class SkillDebugWindow : EditorWindow
    {
        private SkillDefinition[] _skills = new SkillDefinition[0];
        private Vector2 _scroll;
        private int _oddsLevel = 4;
        private string _odds = "";
        public const int OddsRolls = 1000;
        private static readonly int[] OddsLevels = { 2, 4, 6, 10 };
        private int _curveRuns = 200, _curveLevels = 20;
        private string _curve = "", _drops = "";
        private const int TelemetryRows = 10;

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
            Debug.Log($"[Skills] Granted {pick.Id} -> {manager.GetLevel(pick)}/{pick.MaxLevel}");
        }

        /// <summary>KAN-67: +1 level-up re-roll for the rest of this run (an open level-up screen shows it at once).</summary>
        [MenuItem("Survivor Souls/Debug/Add Re-roll")]
        private static void AddReroll()
        {
            SurvivorSouls.Gameplay.Chamber.RunState.AddRerolls(1);
            var manager = FindManager();
            Debug.Log($"[Skills] Re-roll added: {(manager != null ? manager.RerollsLeft : 0)} left this run");
        }

        [MenuItem("Survivor Souls/Debug/Grant Random Skill", true)]
        [MenuItem("Survivor Souls/Debug/Add Re-roll", true)]
        [MenuItem("Survivor Souls/Debug/Level Up Now", true)]
        private static bool InPlayMode() => Application.isPlaying;

        // ------------------------------------------------------------ Volley (KAN-83)

        private const string DoubleShotId = "double_shot", SpreadShotId = "multishot";

        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 0")] private static void Double0() => SetVolleySkill(DoubleShotId, 0);
        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 1")] private static void Double1() => SetVolleySkill(DoubleShotId, 1);
        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 2")] private static void Double2() => SetVolleySkill(DoubleShotId, 2);
        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 3")] private static void Double3() => SetVolleySkill(DoubleShotId, 3);
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 0")] private static void Spread0() => SetVolleySkill(SpreadShotId, 0);
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 1")] private static void Spread1() => SetVolleySkill(SpreadShotId, 1);
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 2")] private static void Spread2() => SetVolleySkill(SpreadShotId, 2);
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 3")] private static void Spread3() => SetVolleySkill(SpreadShotId, 3);

        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 0", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 1", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 2", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Double Shot Level 3", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 0", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 1", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 2", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Spread Shot Level 3", true)]
        [MenuItem("Survivor Souls/Debug/Volley: Print", true)]
        private static bool VolleyInPlayMode() => Application.isPlaying;

        /// <summary>Debug set level (0 removes) of a multi-arrow skill, then prints the volley it fires now.</summary>
        private static void SetVolleySkill(string id, int level)
        {
            var manager = FindManager();
            if (manager == null) return;
            if (!manager.SetLevel(id, level)) { Debug.LogWarning($"[Skills] Cannot set {id} to level {level}"); return; }
            PrintVolley();
        }

        [MenuItem("Survivor Souls/Debug/Volley: Print")]
        private static void PrintVolley()
        {
            var shooter = FindAnyObjectByType<AutoShooter>();
            Debug.Log(shooter != null ? "[Skills] " + shooter.DescribeVolley() : "[Skills] No AutoShooter in the open scene.");
        }

        [MenuItem("Survivor Souls/Debug/Volley: Print Double Shot Stack Odds")]
        private static void PrintStackOdds()
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
            if (table == null) { Debug.LogWarning($"[Skills] Missing {SkillsBuilder.ProgressionPath}"); return; }
            Debug.Log("[Skills] " + SkillStackOdds.Simulate(table, DoubleShotId));
        }

        [MenuItem("Survivor Souls/Debug/Print Skill Offer Odds")]
        private static void PrintOdds()
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
            if (table == null) { Debug.LogWarning($"[Skills] Missing {SkillsBuilder.ProgressionPath}"); return; }
            var lines = OddsLevels.Select(l => SimulateFresh(table, l).ToString());
            Debug.Log($"[Skills] Offer odds, {OddsRolls} offers per level, no skills owned:\n" + string.Join("\n", lines));
            var rerolled = OddsLevels.Select(l => SimulateFresh(table, l, reroll: true).ToString());
            Debug.Log($"[Skills] Re-rolled offer odds (KAN-67), {OddsRolls} re-rolls per level, no skills owned:\n" + string.Join("\n", rerolled));
        }

        /// <summary>
        /// Rolls <see cref="OddsRolls"/> offers at <paramref name="level"/> on a throw-away SkillManager (nothing owned, no
        /// scene needed), so the numbers show the table itself.
        /// </summary>
        public static SkillManager.OfferStats SimulateFresh(SkillProgression table, int level, int seed = 37, bool reroll = false)
        {
            var go = new GameObject("Skill Odds (temp)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var manager = go.AddComponent<SkillManager>();
                manager.Setup(table, null, null, null, go.transform, seed);
                return manager.SimulateOffers(OddsRolls, level, reroll: reroll);
            }
            finally
            {
                DestroyImmediate(go);
            }
        }

        [MenuItem("Survivor Souls/Debug/Print Power Curve")]
        private static void PrintPowerCurve() => Debug.Log("[Skills] " + PowerCurveNow(200, 20));

        /// <summary>
        /// Power curve with the shipped table (KAN-75). Play mode takes the base numbers from the live Knight (shooter,
        /// health, mover), edit mode the defaults.
        /// </summary>
        public static string PowerCurveNow(int runs, int levels)
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(SkillsBuilder.ProgressionPath);
            if (table == null) return $"Missing {SkillsBuilder.ProgressionPath}";
            return SkillPowerCurve.Simulate(table, LiveBase(), runs, levels).ToString();
        }

        private static PowerBase LiveBase()
        {
            var b = PowerBase.Default;
            if (!Application.isPlaying) return b;
            var shooter = FindAnyObjectByType<AutoShooter>();
            var health = FindAnyObjectByType<PlayerHealth>();
            var mover = FindAnyObjectByType<PlayerMover>();
            if (shooter != null)
            {
                b.Damage = shooter.BaseDamage;
                b.ShotsPerSecond = shooter.BaseShotsPerSecond;
                b.CritChance = shooter.BaseCritChance;
                b.CritMultiplier = shooter.BaseCritMultiplier;
                b.SpreadDamage = shooter.BaseSpreadDamage;
            }
            if (health != null) b.MaxHealth = health.BaseMax;
            if (mover != null) b.MoveSpeed = mover.BaseSpeed;
            return b;
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
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawOdds();
            DrawCurveAndDrops();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode in the Game scene to grant skills.", MessageType.Info);
                if (GUILayout.Button("Reload skill assets")) _skills = LoadSkills();
                EditorGUILayout.EndScrollView();
                return;
            }
            var manager = FindAnyObjectByType<SkillManager>();
            if (manager == null)
            {
                EditorGUILayout.HelpBox("No SkillManager in the open scene.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Level up now")) LevelUpNow();
                if (GUILayout.Button("Grant random")) GrantRandom();
                if (GUILayout.Button("Reset run skills")) manager.ResetRun();
                if (GUILayout.Button("Print volley")) PrintVolley();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Drop Light chest")) ChestBuilder.DropTier(ChestTier.Light);
                if (GUILayout.Button("Drop Rare chest")) ChestBuilder.DropTier(ChestTier.Rare);
                if (GUILayout.Button("Upgrade screen"))
                {
                    var flow = FindAnyObjectByType<LevelUpFlow>();
                    if (flow != null) flow.EnqueueUpgrade(new UpgradeOffer(3, 1, false));
                }
            }

            EditorGUILayout.LabelField("Skills (slider = set level, 0 removes; +1 = level-up pick)", EditorStyles.boldLabel);
            foreach (var skill in _skills)
            {
                if (skill == null) continue;
                int level = manager.GetLevel(skill);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(skill.Icon, GUILayout.Width(22), GUILayout.Height(22));
                    EditorGUILayout.LabelField($"{skill.DisplayName}  ({skill.Rarity})", GUILayout.MinWidth(160));
                    int set = EditorGUILayout.IntSlider(level, 0, skill.MaxLevel, GUILayout.Width(150));
                    if (set != level) manager.SetLevel(skill, set);
                    using (new EditorGUI.DisabledScope(!manager.CanTake(skill)))
                    {
                        if (GUILayout.Button("+1", GUILayout.Width(36))) manager.Grant(skill);
                        if (GUILayout.Button("Max", GUILayout.Width(40))) manager.SetLevel(skill, skill.MaxLevel);
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

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Telemetry (last {TelemetryRows} of {GameTelemetry.TotalCount})", EditorStyles.boldLabel);
            for (int i = 0; i < Mathf.Min(TelemetryRows, GameTelemetry.RecentCount); i++)
                EditorGUILayout.LabelField(GameTelemetry.Recent(i).ToString(), EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Power curve and chest drops per Reliquary Hunter level (KAN-75): edit or Play mode.</summary>
        private void DrawCurveAndDrops()
        {
            EditorGUILayout.LabelField("Power curve / chest drops", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _curveRuns = Mathf.Clamp(EditorGUILayout.IntField("Runs", _curveRuns), 1, 5000);
                _curveLevels = Mathf.Clamp(EditorGUILayout.IntField("Levels", _curveLevels), 2, 60);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Power curve"))
                {
                    _curve = PowerCurveNow(_curveRuns, _curveLevels);
                    Debug.Log("[Skills] " + _curve);
                }
                if (GUILayout.Button("Drops per Hunter level"))
                {
                    _drops = ChestBuilder.DropTable();
                    Debug.Log(_drops);
                }
            }
            var mono = new GUIStyle(EditorStyles.helpBox) { font = EditorStyles.standardFont, fontSize = 10, wordWrap = false };
            if (!string.IsNullOrEmpty(_curve)) EditorGUILayout.TextArea(_curve, mono);
            if (!string.IsNullOrEmpty(_drops)) EditorGUILayout.TextArea(_drops, mono);
            EditorGUILayout.Space();
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
