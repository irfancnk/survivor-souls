using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurvivorSouls.Core;
using SurvivorSouls.FX;
using SurvivorSouls.Gameplay;
using SurvivorSouls.UI.Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace SurvivorSouls.Editor
{
    /// <summary>
    /// Builds the Game scene (P0 arena: hero vs. a growing zombie horde) and its prefabs from code.
    /// Re-run after edits: menu "Survivor Souls/Rebuild Game Scene", or batchmode
    /// <c>-executeMethod SurvivorSouls.Editor.GameSceneBuilder.BuildFromCommandLine [-ss-preview &lt;png&gt;]</c>.
    /// -ss-preview renders one still of the scene with a staged horde, for checking art without a player build.
    /// Hand edits to the generated scene and prefabs are overwritten on rebuild.
    /// </summary>
    public static class GameSceneBuilder
    {
        private const string Root = "Assets/_Project";
        private const string ScenePath = Root + "/Scenes/" + SceneNames.Game + ".unity";
        private const string UxmlPath = Root + "/UI/Game/GameHud.uxml";
        private const string ThemePath = Root + "/UI/Theme/SurvivorSoulsTheme.tss";
        private const string PanelSettingsPath = Root + "/UI/Game/GameHudPanelSettings.asset";
        private const string MaterialsDir = Root + "/Art/Materials";
        private const string SoftDotPath = Root + "/Art/Textures/T_SoftDot.png"; // made by MainMenuSceneBuilder
        private const string FlameMaterialPath = MaterialsDir + "/M_Menu_Flame.mat"; // made by MainMenuSceneBuilder
        private const string ZombieDir = Root + "/Art/Characters/Enemies/Zombie";
        private const string ZombieModelPath = ZombieDir + "/SK_Zombie.fbx";
        private const string ZombieRunPath = ZombieDir + "/A_Zombie_Run.fbx";
        private const string ZombieTexturePath = ZombieDir + "/T_Zombie_BaseColor.jpg"; // extracted from the Meshy FBX
        private const string ZombieControllerPath = ZombieDir + "/AC_Zombie.controller";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string ZombiePrefabPath = PrefabsDir + "/Enemies/P_Zombie.prefab";
        private const string SwordPrefabPath = PrefabsDir + "/Projectiles/P_ThrownSword.prefab";

        private const float ArenaRadius = 14f;
        /// <summary>Yaw that turns the Meshy model to face +Z (Unity forward).</summary>
        private const float ZombieModelYaw = 0f;

        private static readonly Color DuskColor = new Color(0.07f, 0.08f, 0.115f);
        private static readonly Color AmbientColor = new Color(0.3f, 0.31f, 0.38f);
        private static readonly Color MoonColor = new Color(0.62f, 0.72f, 0.95f);
        private static readonly Color FireLightColor = new Color(1f, 0.55f, 0.25f);
        private static readonly Color EmberColor = new Color(1f, 0.55f, 0.18f);

        [MenuItem("Survivor Souls/Rebuild Game Scene")]
        public static void Build()
        {
            var m = CreateMaterials();
            var zombiePrefab = CreateZombiePrefab(m);
            var swordPrefab = CreateSwordPrefab(m);
            var panelSettings = CreatePanelSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment();
            CreateMoonLight();
            CreateArena(m);
            var joystick = new GameObject("Input").AddComponent<VirtualJoystick>();
            var player = CreatePlayer(m, joystick, swordPrefab);
            CreateCamera(player.transform);

            var spawner = new GameObject("Enemy Spawner").AddComponent<EnemySpawner>();
            spawner.Configure(zombiePrefab, player, ArenaRadius);

            CreateHud(panelSettings, player, joystick, spawner);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuild(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameSceneBuilder] Built {ScenePath}");
        }

        /// <summary>Batchmode entry point. Exit code 0 on success.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                string preview = GetArg("-ss-preview");
                if (!string.IsNullOrEmpty(preview)) RenderPreview(preview);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // ---------------------------------------------------------------- Assets

        private sealed class GameMaterials
        {
            public Material Ground, Stone, Steel, Cloth, Ember, Shadow, Trail, Flame, Zombie;
        }

        private static GameMaterials CreateMaterials()
        {
            EnsureFolder(MaterialsDir);
            var softDot = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftDotPath);
            if (softDot == null) throw new System.InvalidOperationException($"Missing {SoftDotPath}. Rebuild the main menu scene first.");

            return new GameMaterials
            {
                Ground = LitMaterial("M_Game_Ground", new Color(0.2f, 0.2f, 0.21f), 0.05f),
                Stone = LitMaterial("M_Game_Stone", new Color(0.34f, 0.34f, 0.35f), 0.1f),
                Steel = LitMaterial("M_Game_Steel", new Color(0.6f, 0.6f, 0.64f), 0.7f),
                Cloth = LitMaterial("M_Game_Cloth", new Color(0.36f, 0.08f, 0.06f), 0.05f),
                Ember = UnlitMaterial("M_Game_Ember", new Color(1f, 0.62f, 0.25f)),
                Shadow = TransparentUnlitMaterial("M_Game_BlobShadow", softDot, new Color(0f, 0f, 0f, 0.6f), additive: false),
                Trail = TransparentUnlitMaterial("M_Game_Trail", softDot, EmberColor, additive: true),
                Flame = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterialPath),
                Zombie = TexturedMaterial("M_Zombie", ZombieTexturePath),
            };
        }

        private static Material LitMaterial(string name, Color color, float smoothness)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Simple Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material TexturedMaterial(string name, string texturePath)
        {
            // Meshy's embedded FBX material does not pick up its packed texture in URP; bind it explicitly.
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (importer == null) throw new System.InvalidOperationException($"Texture missing: {texturePath}");
            if (importer.maxTextureSize != 1024)
            {
                importer.maxTextureSize = 1024; // 2k source; 1k is plenty at this camera distance on phones
                importer.SaveAndReimport();
            }
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Simple Lit");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material UnlitMaterial(string name, Color color)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material TransparentUnlitMaterial(string name, Texture2D texture, Color color, bool additive)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Particles/Unlit");
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", color);
            // Same keyword/blend setup the URP material inspector writes.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material LoadOrCreateMaterial(string name, string shaderName)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader not found: {shaderName}");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static ZombieEnemy CreateZombiePrefab(GameMaterials m)
        {
            EnsureLoopingImport(ZombieRunPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ZombieModelPath);
            if (model == null) throw new System.InvalidOperationException($"Model missing: {ZombieModelPath}");
            var run = LoadClip(ZombieRunPath);

            AssetDatabase.DeleteAsset(ZombieControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(ZombieControllerPath, run);

            var root = new GameObject("Zombie");
            root.AddComponent<Health>();
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.transform.SetParent(root.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0f, ZombieModelYaw, 0f);

            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            foreach (var r in visual.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = Enumerable.Repeat(m.Zombie, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            BlobShadow(root.transform, m.Shadow, 1.1f);
            root.AddComponent<ZombieEnemy>();

            EnsureFolder(Path.GetDirectoryName(ZombiePrefabPath).Replace('\\', '/'));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ZombiePrefabPath);
            Object.DestroyImmediate(root);

            Debug.Log($"[GameSceneBuilder] Zombie prefab: {ZombiePrefabPath}, clip '{run.name}' {run.length:0.00}s loop={run.isLooping}");
            return prefab.GetComponent<ZombieEnemy>();
        }

        private static Projectile CreateSwordPrefab(GameMaterials m)
        {
            var root = new GameObject("Thrown Sword");
            var spinner = new GameObject("Spinner").transform;
            spinner.SetParent(root.transform, false);
            // Flat spinning blade, like a thrown cleaver: reads well from the high camera.
            Box("Blade", spinner, m.Ember, new Vector3(0f, 0f, 0.12f), new Vector3(0.1f, 0.03f, 0.75f));
            Box("Guard", spinner, m.Steel, new Vector3(0f, 0f, -0.27f), new Vector3(0.32f, 0.05f, 0.06f));
            Box("Grip", spinner, m.Cloth, new Vector3(0f, 0f, -0.4f), new Vector3(0.05f, 0.05f, 0.2f));

            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = m.Trail;
            trail.time = 0.18f;
            trail.widthMultiplier = 0.35f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.2f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            var projectile = root.AddComponent<Projectile>();
            var so = new SerializedObject(projectile);
            so.FindProperty("spinner").objectReferenceValue = spinner;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder(Path.GetDirectoryName(SwordPrefabPath).Replace('\\', '/'));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, SwordPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Projectile>();
        }

        private static PanelSettings CreatePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1080, 1920);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0f; // portrait: lock to width, phones vary in height
            settings.sortingOrder = 0;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        // ---------------------------------------------------------------- Scene content

        private static void SetupEnvironment()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(AmbientColor);
            RenderSettings.ambientProbe = probe;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.fogColor = DuskColor;
        }

        private static void CreateCamera(Transform target)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = DuskColor;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 60f;
            go.AddComponent<AudioListener>();
            go.AddComponent<CameraFollow>().Configure(target);
        }

        private static void CreateMoonLight()
        {
            var go = new GameObject("Moon Light");
            go.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = MoonColor;
            light.intensity = 1.35f;
            light.shadows = LightShadows.None; // blob shadows instead; no shadow maps on mobile
        }

        private static void CreateArena(GameMaterials m)
        {
            var root = new GameObject("Arena").transform;
            var ground = Primitive(PrimitiveType.Plane, "Ground", root, m.Ground);
            ground.transform.localScale = new Vector3(5f, 1f, 5f);

            var rng = new System.Random(11); // fixed seed: rebuilds give the same layout

            // Broken pillar ring marks the edge of the arena.
            const int pillars = 22;
            for (int i = 0; i < pillars; i++)
            {
                float a = i / (float)pillars * Mathf.PI * 2f;
                float r = ArenaRadius + 1.2f + Range(rng, -0.3f, 0.6f);
                float h = Range(rng, 0.8f, 3.6f);
                var p = Primitive(PrimitiveType.Cube, $"Pillar_{i}", root, m.Stone);
                p.transform.position = new Vector3(Mathf.Cos(a) * r, h * 0.5f, Mathf.Sin(a) * r);
                p.transform.rotation = Quaternion.Euler(Range(rng, -6f, 6f), Range(rng, 0f, 90f), Range(rng, -6f, 6f));
                p.transform.localScale = new Vector3(Range(rng, 0.8f, 1.2f), h, Range(rng, 0.8f, 1.2f));
            }

            // Grave slabs scattered outside the ring for depth in the fog.
            for (int i = 0; i < 26; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                float r = Range(rng, ArenaRadius + 3f, ArenaRadius + 12f);
                var slab = Primitive(PrimitiveType.Cube, $"Slab_{i}", root, m.Stone);
                slab.transform.position = new Vector3(Mathf.Cos(a) * r, 0.4f, Mathf.Sin(a) * r);
                slab.transform.rotation = Quaternion.Euler(Range(rng, -12f, 12f), Range(rng, 0f, 360f), Range(rng, -10f, 10f));
                slab.transform.localScale = new Vector3(0.7f, 0.95f, 0.16f);
            }

            // Three braziers: warm pools of light on the edge.
            for (int i = 0; i < 3; i++)
            {
                float a = (i / 3f) * Mathf.PI * 2f + Mathf.PI * 0.5f;
                CreateBrazier(root, m, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ArenaRadius - 0.6f));
            }

            // Arena never moves: let Unity batch it.
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        }

        private static void CreateBrazier(Transform parent, GameMaterials m, Vector3 pos)
        {
            var root = new GameObject("Brazier").transform;
            root.SetParent(parent, false);
            root.position = pos;
            var stand = Primitive(PrimitiveType.Cylinder, "Stand", root, m.Stone);
            stand.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            stand.transform.localScale = new Vector3(0.25f, 0.5f, 0.25f);
            var bowl = Primitive(PrimitiveType.Cylinder, "Bowl", root, m.Steel);
            bowl.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            bowl.transform.localScale = new Vector3(0.8f, 0.08f, 0.8f);
            var coals = Primitive(PrimitiveType.Sphere, "Coals", root, m.Ember);
            coals.transform.localPosition = new Vector3(0f, 1.12f, 0f);
            coals.transform.localScale = new Vector3(0.6f, 0.15f, 0.6f);

            if (m.Flame != null) CreateFlames(root, m.Flame);

            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = FireLightColor;
            light.shadows = LightShadows.None;
            lightGo.AddComponent<LightFlicker>().Configure(intensity: 4f, intensityAmp: 1f, range: 8f, rangeAmp: 0.6f);
        }

        private static void CreateFlames(Transform parent, Material mat)
        {
            var go = new GameObject("Flames");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along +Z; point it up

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.22f), new Color(1f, 0.38f, 0.1f));
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 30f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.2f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static PlayerController CreatePlayer(GameMaterials m, VirtualJoystick joystick, Projectile swordPrefab)
        {
            var root = new GameObject("Player");
            root.AddComponent<Health>();
            var controller = root.AddComponent<PlayerController>();
            controller.Configure(joystick, ArenaRadius);

            // Placeholder Ashen Knight from primitives until the hero model lands.
            var body = new GameObject("Knight").transform;
            body.SetParent(root.transform, false);
            var torso = Primitive(PrimitiveType.Capsule, "Torso", body, m.Steel);
            torso.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            torso.transform.localScale = new Vector3(0.7f, 0.9f, 0.6f);
            var helm = Primitive(PrimitiveType.Cylinder, "Helm", body, m.Steel);
            helm.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            helm.transform.localScale = new Vector3(0.42f, 0.2f, 0.42f);
            Box("Visor", body, m.Ember, new Vector3(0f, 1.64f, 0.2f), new Vector3(0.26f, 0.04f, 0.04f));
            Box("Cape", body, m.Cloth, new Vector3(0f, 1.0f, -0.3f), new Vector3(0.62f, 1.1f, 0.06f));
            var lPad = Primitive(PrimitiveType.Sphere, "Pauldron_L", body, m.Steel);
            lPad.transform.localPosition = new Vector3(-0.38f, 1.35f, 0f);
            lPad.transform.localScale = Vector3.one * 0.32f;
            var rPad = Primitive(PrimitiveType.Sphere, "Pauldron_R", body, m.Steel);
            rPad.transform.localPosition = new Vector3(0.38f, 1.35f, 0f);
            rPad.transform.localScale = Vector3.one * 0.32f;
            Box("Sword", body, m.Steel, new Vector3(0.45f, 0.8f, 0.2f), new Vector3(0.06f, 0.9f, 0.03f));

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0.3f, 1.1f, 0.5f);

            // Warm ember glow around the hero: lights approaching zombies, Souls-style.
            var glow = new GameObject("Ember Glow");
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 3.2f, -0.5f); // high, so the helm does not blow out
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = FireLightColor;
            light.intensity = 3.2f;
            light.range = 9f;
            light.shadows = LightShadows.None;

            BlobShadow(root.transform, m.Shadow, 1.1f);
            root.AddComponent<PlayerAutoAttack>().Configure(swordPrefab, muzzle);
            return controller;
        }

        private static void CreateHud(PanelSettings panelSettings, PlayerController player, VirtualJoystick joystick, EnemySpawner spawner)
        {
            var go = new GameObject("Game HUD");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (doc.visualTreeAsset == null) throw new System.InvalidOperationException($"UXML missing: {UxmlPath}");
            go.AddComponent<GameHudController>().Configure(player, joystick, spawner);
        }

        // ---------------------------------------------------------------- Preview

        /// <summary>
        /// Stages a horde around the hero in the open scene, poses it from the run clip, renders the
        /// main camera at phone resolution, and writes a PNG. The scene is not saved afterwards.
        /// </summary>
        private static void RenderPreview(string pngPath)
        {
            var zombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePrefabPath);
            var swordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPrefabPath);
            var run = LoadClip(ZombieRunPath);
            var player = Object.FindAnyObjectByType<PlayerController>();
            var cam = Camera.main;

            (float angleDeg, float dist)[] staged = { (20f, 3.2f), (60f, 4.5f), (110f, 3.6f), (150f, 5.5f), (-30f, 4.8f), (-80f, 3.0f), (95f, 7.5f) };
            for (int i = 0; i < staged.Length; i++)
            {
                float a = staged[i].angleDeg * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * staged[i].dist;
                var z = (GameObject)PrefabUtility.InstantiatePrefab(zombiePrefab);
                z.transform.position = pos;
                z.transform.rotation = Quaternion.LookRotation(-pos.normalized, Vector3.up);
                var animated = z.GetComponentInChildren<Animator>().gameObject;
                run.SampleAnimation(animated, run.length * (i / (float)staged.Length));
            }
            player.transform.rotation = Quaternion.LookRotation(new Vector3(0.6f, 0f, 1f));
            var sword = (GameObject)PrefabUtility.InstantiatePrefab(swordPrefab);
            sword.transform.SetPositionAndRotation(new Vector3(1.2f, 1.1f, 2f), Quaternion.LookRotation(new Vector3(0.6f, 0f, 1f)));

            const int width = 590, height = 1278; // iPhone 14 Pro Max aspect, half res
            cam.aspect = width / (float)height;
            cam.GetComponent<CameraFollow>().SnapToTarget();
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            var smr = zombiePrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log($"[GameSceneBuilder] Preview {pngPath}; zombie mesh bounds {smr.bounds.size}, materials: {string.Join(", ", smr.sharedMaterials.Select(x => x == null ? "null" : $"{x.name}/{x.shader.name}"))}");
        }

        // ---------------------------------------------------------------- Helpers

        private static void EnsureLoopingImport(string path)
        {
            // CharacterModelImporter loops A_* clips on import; force one if this file predates it.
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.clipAnimations.Length == 0 || !importer.clipAnimations.All(c => c.loopTime))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static AnimationClip LoadClip(string path)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) throw new System.InvalidOperationException($"No animation clip in {path}");
            return clip;
        }

        private static void BlobShadow(Transform parent, Material mat, float size)
        {
            var quad = Primitive(PrimitiveType.Quad, "Blob Shadow", parent, mat);
            quad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one * size;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            Object.DestroyImmediate(go.GetComponent<Collider>()); // gameplay uses distance checks, not physics
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        private static void Box(string name, Transform parent, Material mat, Vector3 localPos, Vector3 scale)
        {
            var go = Primitive(PrimitiveType.Cube, name, parent, mat);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static void AddToBuild(string scenePath)
        {
            if (EditorBuildSettings.scenes.Any(s => s.path == scenePath)) return;
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes) { new EditorBuildSettingsScene(scenePath, true) };
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
