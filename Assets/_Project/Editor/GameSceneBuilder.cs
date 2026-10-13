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
    /// Builds the Game scene from code: art kit (textures, materials), Realm 1 look (snowy shore, sea, sky),
    /// Archer prefab, streamed world, camera,
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

        [MenuItem("Survivor Souls/Rebuild Game Scene")]
        public static void Build()
        {
            var kit = GameArtKit.Build();
            var realm = RealmArt.Build(kit); // Realm 1 look: values in Art/World/Realm1_Look.asset
            // Main character: the Meshy model when its source GLB exists, else the procedural placeholder.
            var archerPrefab = ArcherModelBuilder.BuildPrefab(kit.Palette, kit.BlobShadow, kit.Quad);
            if (MeshyCharacterImporter.SourceExists) archerPrefab = MeshyCharacterImporter.BuildPrefab(kit.BlobShadow, kit.Quad);
            CombatBuilder.Build(kit);
            var panelSettings = CreatePanelSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment(realm);
            new GameObject("Game").AddComponent<GameSceneController>();
            CreateSunLight(realm.Look);

            var joystick = new GameObject("Input").AddComponent<FloatingJoystick>();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(archerPrefab);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // centre of the spawn tile
            SetRef(player.GetComponent<PlayerMover>(), "joystick", joystick);
            var world = WorldBuilder.Build(kit, realm, player.transform, player.GetComponent<PlayerMover>());
            var combat = CombatBuilder.Load();
            var shooter = CreateCombat(player, combat.bolt, combat.enemy, world);
            CombatBuilder.CreateFx(kit, shooter);
            CreateAudio(SfxBuilder.Build(), shooter, player.GetComponent<PlayerMover>());

            var camera = CreateCamera(player.transform, realm.Look);
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

        /// <summary>Cold overcast coast: cool trilight ambient, overcast sky, haze that hides the far edge.</summary>
        private static void SetupEnvironment(RealmArt realm)
        {
            var look = realm.Look;
            RenderSettings.skybox = realm.Sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.ambientSky;
            RenderSettings.ambientEquatorColor = look.ambientEquator;
            RenderSettings.ambientGroundColor = look.ambientGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = look.fogStart;
            RenderSettings.fogEndDistance = look.fogEnd;
            RenderSettings.fogColor = look.fogColor;
        }

        private static void CreateSunLight(RealmLook look)
        {
            var go = new GameObject("Sun Light");
            go.transform.rotation = Quaternion.Euler(50f, -35f, 0f); // soft overcast key, slightly from the front-left
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = look.sunColor;
            light.intensity = look.sunIntensity;
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

        /// <summary>Procedural sound set (shots, impacts, deaths, snow footsteps, wind + surf ambience) on one audio object.</summary>
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

        private static Camera CreateCamera(Transform target, RealmLook look)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = look.fogColor;
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
