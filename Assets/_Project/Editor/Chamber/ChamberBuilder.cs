using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Player;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Chamber
{
    /// <summary>
    /// Chamber exit rules of the Game scene (KAN-39): the rules asset (survival time, warnings, open sequence; never
    /// overwritten, so designer edits stay) and the "Chamber" object with the <see cref="SurvivalTimer"/>.
    /// The portal itself lives in the exit tile prefab (<see cref="World.PortalBuilder"/>). Called by GameSceneBuilder.
    /// Debug menus (Play mode): "Survivor Souls/Debug/Portal Timer To 10 s" and ".../Open Portal Now".
    /// </summary>
    public static class ChamberBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Chamber";
        public const string RulesPath = DataDir + "/ChamberRules_Realm1.asset";

        public static SurvivalTimer Create(GameObject player, PlayerHealth health)
        {
            var rules = LoadOrCreateRules();
            var timer = new GameObject("Chamber").AddComponent<SurvivalTimer>();
            var so = new SerializedObject(timer);
            so.FindProperty("rules").objectReferenceValue = rules;
            so.FindProperty("player").objectReferenceValue = player.transform;
            so.FindProperty("health").objectReferenceValue = health;
            so.ApplyModifiedPropertiesWithoutUndo();
            return timer;
        }

        private static ChamberRules LoadOrCreateRules()
        {
            var rules = AssetDatabase.LoadAssetAtPath<ChamberRules>(RulesPath);
            if (rules != null) return rules;
            EditorAssetUtil.EnsureFolder(DataDir);
            rules = ChamberRules.Create(600f); // defaults: 10 min, warnings 5 / 2 / 1 min, 10 s pulse, 2 s opening
            AssetDatabase.CreateAsset(rules, RulesPath);
            AssetDatabase.SaveAssets();
            return rules;
        }

        [MenuItem("Survivor Souls/Debug/Portal Timer To 10 s")]
        private static void TimerTo10() => SurvivalTimer.Active?.SetRemaining(10f);

        [MenuItem("Survivor Souls/Debug/Open Portal Now")]
        private static void OpenNow() => SurvivalTimer.Active?.Complete();

        [MenuItem("Survivor Souls/Debug/Portal Timer To 10 s", true)]
        [MenuItem("Survivor Souls/Debug/Open Portal Now", true)]
        private static bool InPlayMode() => Application.isPlaying && SurvivalTimer.Active != null;
    }
}
