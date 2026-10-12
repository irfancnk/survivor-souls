using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Core;
using SurvivorSouls.Editor.Arena;
using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
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
    /// joystick input and HUD. Re-run after edits: menu "Survivor Souls/Rebuild Game Scene", or batchmode
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
        private static readonly Color BackgroundColor = new Color(0.055f, 0.065f, 0.09f);
        private static readonly Color AmbientSky = new Color(0.46f, 0.48f, 0.57f);
        private static readonly Color AmbientEquator = new Color(0.36f, 0.33f, 0.33f);
        private static readonly Color AmbientGround = new Color(0.24f, 0.2f, 0.17f);
        private static readonly Color MoonColor = new Color(0.62f, 0.7f, 0.9f);

        [MenuItem("Survivor Souls/Rebuild Game Scene")]
        public static void Build()
        {
            var kit = GameArtKit.Build();
            // Main character: the Meshy model when its source GLB exists, else the procedural placeholder.
            var archerPrefab = ArcherModelBuilder.BuildPrefab(kit.Palette, kit.BlobShadow, kit.Quad);
            if (MeshyCharacterImporter.SourceExists) archerPrefab = MeshyCharacterImporter.BuildPrefab(kit.BlobShadow, kit.Quad);
            var panelSettings = CreatePanelSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment();
            new GameObject("Game").AddComponent<GameSceneController>();
            CreateMoonLight();
            ArenaBuilder.Build(null, kit);

            var joystick = new GameObject("Input").AddComponent<FloatingJoystick>();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(archerPrefab);
            player.transform.SetPositionAndRotation(ArenaBuilder.PlayerSpawn, Quaternion.identity);
            SetRef(player.GetComponent<PlayerMover>(), "joystick", joystick);

            CreateCamera(player.transform);
            CreateHud(panelSettings, joystick);

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
            RenderSettings.fogStartDistance = 38f;
            RenderSettings.fogEndDistance = 70f;
            RenderSettings.fogColor = BackgroundColor;
        }

        private static void CreateMoonLight()
        {
            var go = new GameObject("Moon Light");
            go.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = MoonColor;
            light.intensity = 0.9f;
            light.shadows = LightShadows.None; // blob shadows instead of shadow maps
        }

        private static void CreateCamera(Transform target)
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
            // Min keeps the south wall at the screen bottom (see TopDownFollowCamera geometry); max keeps the far wall in view.
            so.FindProperty("lookZRange").vector2Value = new Vector2(-2.4f, 3f);
            so.ApplyModifiedPropertiesWithoutUndo();
            go.transform.SetPositionAndRotation(target.position + new Vector3(0f, 34f, -20f), Quaternion.Euler(60f, 0f, 0f));
        }

        private static void CreateHud(PanelSettings panelSettings, FloatingJoystick joystick)
        {
            var go = new GameObject("HUD");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
            if (doc.visualTreeAsset == null) throw new System.InvalidOperationException($"UXML missing: {HudUxmlPath}");
            SetRef(go.AddComponent<GameHudController>(), "joystick", joystick);
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
