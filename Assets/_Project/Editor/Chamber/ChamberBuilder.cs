using System.Collections.Generic;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Chamber
{
    /// <summary>
    /// Chamber exit rules of the Game scene (KAN-39): the rules asset (survival time, warnings, open sequence; never
    /// overwritten, so designer edits stay) and the "Chamber" object with the <see cref="SurvivalTimer"/>.
    /// The portal itself lives in the exit tile prefab (<see cref="World.PortalBuilder"/>). Called by GameSceneBuilder.
    /// KAN-40: the same object carries the <see cref="ChamberTransition"/> (portal enter -> fade -> next Chamber).
    /// KAN-46: and the <see cref="ChamberSetup"/> (difficulty asset -> world seed and spawner multipliers per Chamber).
    /// Debug menus (Play mode): "Survivor Souls/Debug/Portal Timer To 10 s" and ".../Open Portal Now".
    /// </summary>
    public static class ChamberBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Chamber";
        public const string RulesPath = DataDir + "/ChamberRules_Realm1.asset";
        public const string DifficultyPath = DataDir + "/ChamberDifficulty_Realm1.asset";

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

        /// <summary>
        /// Portal exit on the "Chamber" object (KAN-40). Locked on entry: stick, mover, shooter, contact damage and the
        /// soul pickup (wisps stop flying to a Knight who is leaving). Animation drivers stay on.
        /// </summary>
        public static ChamberTransition CreateTransition(GameObject chamber, GameObject player, PlayerHealth health, XpManager xp,
            SkillManager skills, RunCounters counters, EnemySpawner spawner, FloatingJoystick joystick)
        {
            var transition = chamber.AddComponent<ChamberTransition>();
            var locked = new List<Object> { joystick, player.GetComponent<PlayerMover>() };
            locked.AddRange(player.GetComponents<AutoShooter>());
            locked.AddRange(player.GetComponents<ContactDamageReceiver>());
            locked.AddRange(player.GetComponents<SoulCollector>());
            var so = new SerializedObject(transition);
            so.FindProperty("player").objectReferenceValue = player.transform;
            so.FindProperty("health").objectReferenceValue = health;
            so.FindProperty("xp").objectReferenceValue = xp;
            so.FindProperty("skills").objectReferenceValue = skills;
            so.FindProperty("counters").objectReferenceValue = counters;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("controller").objectReferenceValue = player.GetComponent<CharacterController>();
            var array = so.FindProperty("lockOnExit");
            array.arraySize = locked.Count;
            for (int i = 0; i < locked.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = locked[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return transition;
        }

        /// <summary>Kills and souls this run (KAN-51) on the "Chamber" object; carried by the transition.</summary>
        public static RunCounters CreateCounters(GameObject chamber, SoulWispField souls)
        {
            var counters = chamber.AddComponent<RunCounters>();
            var so = new SerializedObject(counters);
            so.FindProperty("souls").objectReferenceValue = souls;
            so.ApplyModifiedPropertiesWithoutUndo();
            return counters;
        }

        /// <summary>Per-Chamber difficulty and layout seed (KAN-46) on the "Chamber" object. Runs before the world and spawner wake.</summary>
        public static ChamberSetup CreateSetup(GameObject chamber, WorldStreamer world, EnemySpawner spawner)
        {
            var setup = chamber.AddComponent<ChamberSetup>();
            var so = new SerializedObject(setup);
            so.FindProperty("difficulty").objectReferenceValue = LoadOrCreateDifficulty();
            so.FindProperty("world").objectReferenceValue = world;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.ApplyModifiedPropertiesWithoutUndo();
            return setup;
        }

        private static ChamberDifficulty LoadOrCreateDifficulty()
        {
            var difficulty = AssetDatabase.LoadAssetAtPath<ChamberDifficulty>(DifficultyPath);
            if (difficulty != null) return difficulty;
            EditorAssetUtil.EnsureFolder(DataDir);
            // Realm 1: 10 Chambers, boss every 5th; +15 % enemy health, +5 % caps (max x1.35), 4 % faster spawns (min x0.7) per Chamber.
            difficulty = ChamberDifficulty.Create();
            AssetDatabase.CreateAsset(difficulty, DifficultyPath);
            AssetDatabase.SaveAssets();
            return difficulty;
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
