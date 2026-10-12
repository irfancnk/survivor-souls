using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.FX;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using SurvivorSouls.UI.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace SurvivorSouls.Editor
{
    /// <summary>
    /// Builds the Game scene from code: art kit (textures, materials), Archer prefab, arena, camera,
    /// joystick input, HUD and combat (auto-shooting crossbow, enemy spawner, health bars). Re-run after edits: menu "Survivor Souls/Rebuild Game Scene", or batchmode
    /// <c>-executeMethod SurvivorSouls.Editor.GameSceneBuilder.BuildFromCommandLine</c>.
    /// Hand edits to the generated scene are overwritten on rebuild.
    /// </summary>
    public static class GameSceneBuilder
    {
        private const string Root = "Assets/_Project";
        private const string ScenePath = Root + "/Scenes/" + SceneNames.Game + ".unity";
        private const string HudUxmlPath = Root + "/UI/Hud/GameHud.uxml";
        private const string HudPanelSettingsPath = Root + "/UI/Hud/GameHudPanelSettings.asset";
        private const string ThemePath = Root + "/UI/Theme/SurvivorSoulsTheme.tss";

        // Night courtyard: cool moonlit ambient, warm braziers. Readable, not grim.
        // Morning: pale sky haze, soft warm low sun, braziers still burning. Bright and readable, not grim.
        private static readonly Color BackgroundColor = new Color(0.6f, 0.65f, 0.7f);
        private static readonly Color AmbientSky = new Color(0.66f, 0.72f, 0.84f);
        private static readonly Color AmbientEquator = new Color(0.56f, 0.53f, 0.5f);
        private static readonly Color AmbientGround = new Color(0.36f, 0.31f, 0.26f);
        private static readonly Color MoonColor = new Color(1f, 0.9f, 0.74f); // morning sun

        [MenuItem("Survivor Souls/Rebuild Game Scene")]
        public static void Build()
        {
            var kit = GameArtKit.Build();
            // Main character: the Meshy model when its source GLB exists, else the procedural placeholder.
            var archerPrefab = ArcherModelBuilder.BuildPrefab(kit.Palette, kit.BlobShadow, kit.Quad);
            if (MeshyCharacterImporter.SourceExists) archerPrefab = MeshyCharacterImporter.BuildPrefab(kit.BlobShadow, kit.Quad);
            CombatBuilder.Build(kit);
            var panelSettings = CreatePanelSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment();
            new GameObject("Game").AddComponent<GameSceneController>();
            CreateMoonLight();

            var joystick = new GameObject("Input").AddComponent<FloatingJoystick>();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(archerPrefab);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // centre of the spawn tile
            SetRef(player.GetComponent<PlayerMover>(), "joystick", joystick);
            var world = WorldBuilder.Build(kit, player.transform, player.GetComponent<PlayerMover>());
            var combat = CombatBuilder.Load();
            var shooter = CreateCombat(player, combat.bolt, combat.enemy, world);
            CombatBuilder.CreateFx(kit, shooter);
            CreateAudio(SfxBuilder.Build(), shooter, player.GetComponent<PlayerMover>());
            player.AddComponent<GrassPusher>();

            var camera = CreateCamera(player.transform);
            CreateHud(panelSettings, joystick, camera);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuild(ScenePath, index: 1);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameSceneBuilder] Built {ScenePath}");
        }

        /// <summary>Batchmode entry point. Exit code 0 on success.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        private static void SetupEnvironment()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;

            // Distance fog only softens the far edge; ground mist particles give the near haze.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 34f;
            RenderSettings.fogEndDistance = 80f;
            RenderSettings.fogColor = BackgroundColor;
        }

        private static void CreateMoonLight()
        {
            var go = new GameObject("Sun Light");
            go.transform.rotation = Quaternion.Euler(38f, -50f, 0f); // low morning sun from the front-left
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = MoonColor;
            light.intensity = 1.25f;
            light.shadows = LightShadows.None; // blob shadows instead of shadow maps
        }

        /// <summary>
        /// Auto-shooter on the player (bolts leave the crossbow muzzle), upper-body recoil, enemy spawner around them.
        /// </summary>
        private static AutoShooter CreateCombat(GameObject player, Bolt bolt, Enemy enemy, WorldStreamer world)
        {
            var shooter = player.AddComponent<AutoShooter>();
            SetRef(shooter, "boltPrefab", bolt);
            var muzzle = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Crossbow_Muzzle");
            if (muzzle != null) SetRef(shooter, "muzzle", muzzle);
            SetRef(player.AddComponent<CrossbowRecoil>(), "rig", player.GetComponentInChildren<HumanoidRig>());

            var spawner = new GameObject("Enemies").AddComponent<EnemySpawner>();
            SetRef(spawner, "prefab", enemy);
            SetRef(spawner, "player", player.transform);
            SetRef(spawner, "world", world);
            return shooter;
        }

        /// <summary>Procedural sound set (shots, impacts, deaths, footsteps, meadow ambience) on one audio object.</summary>
        private static void CreateAudio(SfxBuilder.Set sfx, AutoShooter shooter, PlayerMover mover)
        {
            var audio = new GameObject("Audio").AddComponent<GameAudio>();
            var so = new SerializedObject(audio);
            so.FindProperty("shooter").objectReferenceValue = shooter;
            so.FindProperty("player").objectReferenceValue = mover;
            SetClips(so.FindProperty("shots"), sfx.Shots);
            SetClips(so.FindProperty("impacts"), sfx.Impacts);
            SetClips(so.FindProperty("deaths"), sfx.Deaths);
            SetClips(so.FindProperty("footsteps"), sfx.Footsteps);
            so.FindProperty("ambience").objectReferenceValue = sfx.Ambience;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }

        private static Camera CreateCamera(Transform target)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 5f;
            cam.farClipPlane = 90f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            go.AddComponent<AudioListener>();

            var follow = go.AddComponent<TopDownFollowCamera>();
            follow.Target = target;
            var so = new SerializedObject(follow);
            // Open world: follow the player freely in X and Z.
            so.FindProperty("followX").floatValue = 1f;
            so.FindProperty("lookZRange").vector2Value = new Vector2(-10000f, 10000f);
            so.ApplyModifiedPropertiesWithoutUndo();
            go.transform.SetPositionAndRotation(target.position + new Vector3(0f, 34f, -20f), Quaternion.Euler(60f, 0f, 0f));
            return cam;
        }

        private static void CreateHud(PanelSettings panelSettings, FloatingJoystick joystick, Camera camera)
        {
            var go = new GameObject("HUD");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            if (doc.visualTreeAsset == null) throw new System.InvalidOperationException($"UXML missing: {HudUxmlPath}");
            SetRef(go.AddComponent<GameHudController>(), "joystick", joystick);
            SetRef(go.AddComponent<CombatHud>(), "worldCamera", camera);
        }

        private static PanelSettings CreatePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(HudPanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, HudPanelSettingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1080, 1920);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0f; // portrait: lock to width
            settings.sortingOrder = 0;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void SetRef(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuild(string scenePath, int index)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Insert(Mathf.Min(index, scenes.Count), new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
