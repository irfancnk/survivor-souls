using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.Progression;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.World;
using SurvivorSouls.UI.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// What <see cref="GameSceneBuilder"/> made, handed to every <see cref="IGameSceneStep"/> (KAN-228).
    /// Add a field here only when a step needs an object that is not reachable from these.
    /// </summary>
    public sealed class GameSceneContext
    {
        public GameArtKit Kit;
        public RealmArt Realm;
        public SfxBuilder.Set Sfx;
        public GameSceneController Game;
        public FloatingJoystick Joystick;
        public GameObject Player;
        public PlayerStats Stats;
        public PlayerHealth Health;
        public PlayerDeath Death;
        public WorldStreamer World;
        public AutoShooter Shooter;
        public EnemySpawner Spawner;
        public ProgressionBuilder.Result Progression;
        public SkillManager Skills;
        public ChestDropper Chests;
        public SurvivalTimer Timer;
        public ChamberTransition Transition;
        public RunCounters Counters;
        public ChamberSetup ChamberSetup;
        public BossFight Boss;
        public BossHearts Hearts;
        public Transform EnemyShots;
        public GameAudio Audio;
        public Camera Camera;
        public TopDownFollowCamera FollowCamera;
        /// <summary>The HUD root (UIDocument with GameHud.uxml, GameHudController, CombatHud, SkillChoiceScreen ...).</summary>
        public GameObject Hud;
        public UIDocument HudDocument;

        /// <summary>Sets a serialized object reference field (private [SerializeField] too) without undo.</summary>
        public static void SetRef(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{component.GetType().Name} has no serialized field '{field}'");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
