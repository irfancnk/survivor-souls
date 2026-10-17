using SurvivorSouls.Editor.World.Island;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-245 (KAN-247) island Chambers, Game scene side: builds one inactive <see cref="IslandChamber"/> per theme
    /// (<see cref="IslandChamberBuilder"/>), wires them and the Realm 1 theme schedule into <see cref="ChamberSetup"/>.
    /// The schedule asset (Data/Chamber/ChamberThemes_Realm1.asset) is made once; later edits to it are kept.
    /// </summary>
    public sealed class IslandChamberStep : IGameSceneStep
    {
        public const string SchedulePath = "Assets/_Project/Data/Chamber/ChamberThemes_Realm1.asset";

        public int Order => 245;

        public void Build(GameSceneContext ctx)
        {
            var sunGo = GameObject.Find("Sun Light");
            var islands = IslandChamberBuilder.CreateAll(ctx.Kit, ctx.Realm, ctx.Sfx.PortalHum, ctx.World, ctx.Spawner, ctx.Player.transform,
                ctx.FollowCamera, ctx.Player.GetComponent<ObjectiveArrow>(), sunGo != null ? sunGo.GetComponent<Light>() : null);

            var so = new SerializedObject(ctx.ChamberSetup);
            var array = so.FindProperty("islands");
            array.arraySize = islands.Count;
            for (int i = 0; i < islands.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = islands[i];
            so.FindProperty("themes").objectReferenceValue = LoadOrCreateSchedule();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Realm 1 order from KAN-245 (only the themes that exist so far: KAN-247 = Chamber 1 Drowned Cathedral).</summary>
        public static ChamberThemeSchedule LoadOrCreateSchedule()
        {
            var schedule = AssetDatabase.LoadAssetAtPath<ChamberThemeSchedule>(SchedulePath);
            if (schedule != null) return schedule;
            schedule = ChamberThemeSchedule.Create(1, 10, new ChamberThemeSchedule.Entry { chamber = 1, theme = IslandThemes.DrownedCathedral });
            AssetDatabase.CreateAsset(schedule, SchedulePath);
            AssetDatabase.SaveAssets();
            return schedule;
        }
    }
}
