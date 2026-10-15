using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurvivorSouls.Core;
using SurvivorSouls.FX;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Realms;
using SurvivorSouls.UI.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SurvivorSouls.Editor
{
    /// <summary>
    /// Builds the MainMenu scene and its assets from code, so the scene is reproducible and
    /// easy to change by editing this file. Re-run after edits:
    /// menu "Survivor Souls/Rebuild Main Menu Scene", or batchmode
    /// <c>-executeMethod SurvivorSouls.Editor.MainMenuSceneBuilder.BuildFromCommandLine</c>.
    /// Hand edits to the generated scene are overwritten on rebuild.
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        private const string Root = "Assets/_Project";
        private const string ScenePath = Root + "/Scenes/" + SceneNames.MainMenu + ".unity";
        private const string UxmlPath = Root + "/UI/MainMenu/MainMenu.uxml";
        private const string UssPath = Root + "/UI/MainMenu/MainMenu.uss";
        private const string ThemePath = Root + "/UI/Theme/SurvivorSoulsTheme.tss";
        private const string PanelSettingsPath = Root + "/UI/MainMenu/MainMenuPanelSettings.asset";
        private const string MaterialsDir = Root + "/Art/Materials";
        private const string TexturesDir = Root + "/Art/Textures";
        private const string SoftDotPath = TexturesDir + "/T_SoftDot.png";
        private const string FadeVPath = TexturesDir + "/T_FadeV.png"; // referenced by MainMenu.uss
        private const string RealmsDir = Root + "/Data/Realms";
        private const string RealmCatalogPath = RealmsDir + "/RealmCatalog.asset";
        private const string Realm1ThumbPath = Root + "/Art/UI/Realms/T_Realm1_Thumb.png"; // crop of a Realm 1 dev screenshot
        private const string Realm1DifficultyPath = Root + "/Data/Chamber/ChamberDifficulty_Realm1.asset";

        // Palette: cool dusk around a warm fire. Warm, not grim.
        private static readonly Color DuskColor = new Color(0.1f, 0.125f, 0.18f);
        private static readonly Color AmbientColor = new Color(0.26f, 0.28f, 0.36f);
        private static readonly Color MoonColor = new Color(0.55f, 0.66f, 0.9f);
        private static readonly Color FireLightColor = new Color(1f, 0.55f, 0.25f);
        private static readonly Vector3 BonfirePos = Vector3.zero; // portrait: fire sits centre frame

        [MenuItem("Survivor Souls/Rebuild Main Menu Scene")]
        public static void Build()
        {
            var materials = CreateMaterials();
            CreateFadeTexture();
            AssetDatabase.ImportAsset(UssPath, ImportAssetOptions.ForceUpdate); // resolve url() to the fade texture
            var panelSettings = CreatePanelSettings();
            var realmCatalog = CreateRealms();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupEnvironment();
            CreateCamera();
            CreateMoonLight();
            CreateGround(materials);
            CreateBonfire(materials);
            CreateRuins(materials);
            CreateUi(panelSettings, realmCatalog);

            StableAssetWriter.Remember(ScenePath);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SetFirstInBuild(ScenePath);
            AssetDatabase.SaveAssets();
            StableAssetWriter.SettleAll(); // undo pure fileID churn (KAN-157)
            Debug.Log($"[MainMenuSceneBuilder] Built {ScenePath}");
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

        // ---------------------------------------------------------------- Assets

        private sealed class MenuMaterials
        {
            public Material Ground, Stone, Wood, Metal, Ruin, Flame, Ember;
        }

        private static MenuMaterials CreateMaterials()
        {
            EnsureFolder(MaterialsDir);
            var softDot = CreateSoftDotTexture();
            return new MenuMaterials
            {
                Ground = LitMaterial("M_Menu_Ground", new Color(0.13f, 0.13f, 0.14f), 0.05f),
                Stone = LitMaterial("M_Menu_Stone", new Color(0.32f, 0.31f, 0.3f), 0.1f),
                Wood = LitMaterial("M_Menu_CharredWood", new Color(0.12f, 0.08f, 0.06f), 0.05f),
                Metal = LitMaterial("M_Menu_Metal", new Color(0.55f, 0.55f, 0.58f), 0.75f),
                Ruin = LitMaterial("M_Menu_Ruin", new Color(0.2f, 0.21f, 0.24f), 0.05f),
                Flame = AdditiveParticleMaterial("M_Menu_Flame", softDot),
                Ember = AdditiveParticleMaterial("M_Menu_Ember", softDot),
            };
        }

        private static Material LitMaterial(string name, Color color, float smoothness)
        {
            // Simple Lit: Blinn-Phong, cheaper than Lit on mobile GPUs.
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Simple Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorAssetUtil.ValidateSimpleLit(mat); // validated state: opening the menu materials changes nothing (KAN-158)
            return mat;
        }

        private static Material AdditiveParticleMaterial(string name, Texture2D texture)
        {
            var mat = LoadOrCreateMaterial(name, "Universal Render Pipeline/Particles/Unlit");
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            // Transparent + additive, matching what the URP material inspector writes.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
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

        /// <summary>64x64 radial falloff used by all menu particles. Generated, so no art dependency.</summary>
        private static Texture2D CreateSoftDotTexture()
        {
            return WriteAlphaTexture(SoftDotPath, 64, 64, (u, v) =>
            {
                float dx = u * 2f - 1f;
                float dy = v * 2f - 1f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                return Mathf.Pow(1f - d, 2.2f);
            });
        }

        /// <summary>Vertical fade, opaque at the bottom. UI shades tint it (see MainMenu.uss).</summary>
        private static Texture2D CreateFadeTexture()
        {
            return WriteAlphaTexture(FadeVPath, 4, 128, (u, v) =>
            {
                float t = 1f - v; // v = 0 is the bottom row
                return t * t * (3f - 2f * t); // smoothstep
            });
        }

        /// <summary>Writes a white RGBA PNG whose alpha is alpha(u, v), u/v in [0,1]. Skips if the file exists.</summary>
        private static Texture2D WriteAlphaTexture(string path, int width, int height, System.Func<float, float, float> alpha)
        {
            EnsureFolder(TexturesDir);
            if (!File.Exists(path))
            {
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / width, (y + 0.5f) / height));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                tex.SetPixels32(pixels);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.fogColor = DuskColor;
        }

        private static void CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            // Portrait framing: fire spans ~33%-57% of screen height, between title and buttons.
            go.transform.position = new Vector3(0f, 2.1f, -5.2f);
            go.transform.LookAt(new Vector3(0f, 0.45f, 0f));

            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = DuskColor;
            cam.fieldOfView = 60f; // vertical FOV; portrait needs more height
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 60f;

            go.AddComponent<AudioListener>();
            go.AddComponent<CameraDrift>();
        }

        private static void CreateMoonLight()
        {
            var go = new GameObject("Moon Light");
            go.transform.rotation = Quaternion.Euler(28f, -140f, 0f); // back-rim on the ruins
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = MoonColor;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None; // menu: no shadow maps, save GPU
        }

        private static void CreateGround(MenuMaterials m)
        {
            var ground = Primitive(PrimitiveType.Plane, "Ground", null, m.Ground);
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
        }

        private static void CreateBonfire(MenuMaterials m)
        {
            var root = new GameObject("Bonfire").transform;
            root.position = BonfirePos;
            var rng = new System.Random(7); // fixed seed: rebuilds give the same layout

            // Stone ring.
            const int stones = 9;
            for (int i = 0; i < stones; i++)
            {
                float a = i / (float)stones * Mathf.PI * 2f;
                var s = Primitive(PrimitiveType.Sphere, $"Stone_{i}", root, m.Stone);
                s.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.62f, 0.06f, Mathf.Sin(a) * 0.62f);
                s.transform.localRotation = Quaternion.Euler(Range(rng, -20, 20), Range(rng, 0, 360), Range(rng, -20, 20));
                s.transform.localScale = new Vector3(Range(rng, 0.28f, 0.4f), Range(rng, 0.16f, 0.24f), Range(rng, 0.24f, 0.34f));
            }

            // Charred logs in a low tepee.
            const int logs = 5;
            for (int i = 0; i < logs; i++)
            {
                float a = i / (float)logs * 360f + 15f;
                var log = Primitive(PrimitiveType.Cylinder, $"Log_{i}", root, m.Wood);
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                log.transform.localPosition = dir * 0.2f + Vector3.up * 0.18f;
                log.transform.localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(-62f, 0f, 0f);
                log.transform.localScale = new Vector3(0.09f, 0.32f, 0.09f);
            }

            // Sword planted in the fire.
            var sword = new GameObject("Sword").transform;
            sword.SetParent(root, false);
            sword.localRotation = Quaternion.Euler(4f, 25f, -7f);
            Box("Blade", sword, m.Metal, new Vector3(0f, 0.55f, 0f), new Vector3(0.07f, 1.1f, 0.018f));
            Box("Guard", sword, m.Metal, new Vector3(0f, 1.12f, 0f), new Vector3(0.36f, 0.05f, 0.05f));
            Box("Grip", sword, m.Wood, new Vector3(0f, 1.3f, 0f), new Vector3(0.045f, 0.32f, 0.045f));
            Box("Pommel", sword, m.Metal, new Vector3(0f, 1.48f, 0f), new Vector3(0.08f, 0.07f, 0.08f));

            CreateFlames(root, m.Flame);
            CreateEmbers(root, m.Ember);

            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = FireLightColor;
            light.shadows = LightShadows.None;
            lightGo.AddComponent<LightFlicker>().Configure(intensity: 4f, intensityAmp: 1f, range: 9f, rangeAmp: 0.8f);
        }

        private static void CreateFlames(Transform parent, Material mat)
        {
            var ps = NewParticleSystem("Flames", parent, mat, new Vector3(0f, 0.15f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.22f), new Color(1f, 0.38f, 0.1f));
            main.maxParticles = 70;

            var emission = ps.emission;
            emission.rateOverTime = 50f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.2f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(
                new[] { (new Color(1f, 0.9f, 0.6f), 0f), (new Color(1f, 0.5f, 0.15f), 0.4f), (new Color(0.6f, 0.12f, 0.04f), 1f) },
                new[] { (0f, 0f), (0.9f, 0.12f), (0.6f, 0.5f), (0f, 1f) });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
        }

        private static void CreateEmbers(Transform parent, Material mat)
        {
            var ps = NewParticleSystem("Embers", parent, mat, new Vector3(0f, 0.3f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.35f), new Color(1f, 0.45f, 0.12f));
            main.gravityModifier = -0.04f;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 18f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.3f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.45f;
            noise.frequency = 0.6f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(
                new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0f, 0f), (1f, 0.1f), (0.8f, 0.7f), (0f, 1f) });
        }

        private static void CreateRuins(MenuMaterials m)
        {
            // Silhouettes that fade into fog. Hand-placed for composition: frame the fire, leave the left for UI.
            var root = new GameObject("Ruins").transform;
            // Portrait view is narrow (~±15° horizontal), so ruins stack in depth behind the fire.
            (Vector3 pos, Vector3 scale, float tilt)[] pillars =
            {
                (new Vector3(-2.6f, 0f, 9f), new Vector3(0.9f, 7f, 0.9f), 0f),   // arch left
                (new Vector3(2.6f, 0f, 9f), new Vector3(0.9f, 6.4f, 0.9f), 2f),  // arch right
                (new Vector3(-0.9f, 0f, 15f), new Vector3(1.1f, 10f, 1.1f), 0f),
                (new Vector3(4.6f, 0f, 13f), new Vector3(1f, 8f, 1f), -5f),
                (new Vector3(-5f, 0f, 14f), new Vector3(1.1f, 9f, 1.1f), 4f),
                (new Vector3(1.8f, 0f, 19f), new Vector3(1.2f, 12f, 1.2f), 0f),
                (new Vector3(-3.6f, 0f, 5f), new Vector3(0.8f, 2.4f, 0.8f), 14f), // broken stump
            };
            for (int i = 0; i < pillars.Length; i++)
            {
                var (pos, scale, tilt) = pillars[i];
                var p = Primitive(PrimitiveType.Cube, $"Pillar_{i}", root, m.Ruin);
                p.transform.position = pos + Vector3.up * scale.y * 0.5f;
                p.transform.rotation = Quaternion.Euler(0f, 18f * i, tilt);
                p.transform.localScale = scale;
            }

            // Cracked lintel resting on the arch pillars: frames the fire.
            var lintel = Primitive(PrimitiveType.Cube, "Lintel", root, m.Ruin);
            lintel.transform.position = new Vector3(0f, 6.7f, 9f);
            lintel.transform.rotation = Quaternion.Euler(0f, 0f, -4f);
            lintel.transform.localScale = new Vector3(6.4f, 0.75f, 1f);

            // Grave slabs around the fire for scale.
            Vector3[] slabs =
            {
                new Vector3(-1.7f, 0.4f, 2.4f),
                new Vector3(1.8f, 0.4f, 3f),
                new Vector3(-0.7f, 0.4f, 4.6f),
                new Vector3(2.9f, 0.4f, 5.4f),
            };
            for (int i = 0; i < slabs.Length; i++)
            {
                var slab = Primitive(PrimitiveType.Cube, $"Slab_{i}", root, m.Stone);
                slab.transform.position = slabs[i];
                slab.transform.rotation = Quaternion.Euler(-6f + i * 4f, -20f + i * 13f, i % 2 == 0 ? 7f : -5f);
                slab.transform.localScale = new Vector3(0.6f, 0.9f, 0.14f);
            }
        }

        private static void CreateUi(PanelSettings panelSettings, RealmCatalog realmCatalog)
        {
            var go = new GameObject("Main Menu UI");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (doc.visualTreeAsset == null) throw new System.InvalidOperationException($"UXML missing: {UxmlPath}");
            go.AddComponent<MainMenuController>().Setup(realmCatalog,
                Economy.EconomyBuilder.LoadOrCreateGemRewards(), // welcome shards (KAN-97)
                Economy.EconomyBuilder.LoadOrCreateSoulTree(), // soul tree page (KAN-98)
                Economy.EconomyBuilder.LoadOrCreateStoreCatalog()); // store page (KAN-99)
        }

        // ---------------------------------------------------------------- Realms (KAN-20)

        /// <summary>
        /// Realm assets and the catalog for the selection page. Each asset is made only when missing, so text and
        /// order edits in the Inspector stay. Realm 1 is the snowy shore (playable, Chamber count from its
        /// ChamberDifficulty); Realms 2 and 3 are "coming soon" placeholders without art (lock icon on a dark card).
        /// </summary>
        private static RealmCatalog CreateRealms()
        {
            EnsureFolder(RealmsDir);
            var thumb = ImportRealmThumbnail(Realm1ThumbPath);
            var difficulty = AssetDatabase.LoadAssetAtPath<ChamberDifficulty>(Realm1DifficultyPath);
            var list = new[]
            {
                RealmAsset(1, "Frostbitten Shore", difficulty, true, thumb,
                    "Where the cold tide gives back its dead.",
                    "A drowned coast of snow, wrecks and broken ice. Survive each Chamber until the portal wakes, then step through. The last Chamber holds the Realm's Lord.",
                    "Drowned Hollows (horde)", "Souls, XP, Ember Boons", new Color(0.62f, 0.8f, 0.94f)),
                RealmAsset(2, "Undead Burg", null, false, null,
                    "Ramparts of a fallen town.",
                    "Rooftops, ramparts and burning streets, held by the hollowed garrison.",
                    "Hollow soldiers, crossbowmen", "Souls, Iron Reliquary", new Color(0.86f, 0.45f, 0.2f)),
                RealmAsset(3, "Blighted Marsh", null, false, null,
                    "Poison mire under a rotting sky.",
                    "A sunken swamp of plague and rot. Slow ground, toxic pools, things beneath the water.",
                    "Bog crawlers, plague hollows", "Souls, Obsidian Reliquary", new Color(0.56f, 0.7f, 0.3f)),
            };

            var catalog = AssetDatabase.LoadAssetAtPath<RealmCatalog>(RealmCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RealmCatalog>();
                catalog.Configure(list);
                AssetDatabase.CreateAsset(catalog, RealmCatalogPath);
            }
            return catalog;
        }

        private static RealmDefinition RealmAsset(int id, string displayName, ChamberDifficulty difficulty, bool playable,
            Texture2D thumbnail, string tagline, string description, string enemies, string rewards, Color accent)
        {
            string path = $"{RealmsDir}/Realm_{id}.asset";
            var realm = AssetDatabase.LoadAssetAtPath<RealmDefinition>(path);
            if (realm != null) return realm;
            realm = RealmDefinition.Create(id, displayName, 10, playable, difficulty);
            realm.Describe(tagline, description, enemies, rewards, 5, accent, thumbnail);
            AssetDatabase.CreateAsset(realm, path);
            return realm;
        }

        /// <summary>UI thumbnail import: no mips, small, clamp (one per Realm card).</summary>
        private static Texture2D ImportRealmThumbnail(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                bool dirty = importer.mipmapEnabled || importer.maxTextureSize != 512 || importer.wrapMode != TextureWrapMode.Clamp
                             || importer.npotScale != TextureImporterNPOTScale.None;
                if (dirty)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.mipmapEnabled = false;
                    importer.maxTextureSize = 512;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.SaveAndReimport();
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"[MainMenuSceneBuilder] Realm thumbnail missing: {path}");
            return texture;
        }

        // ---------------------------------------------------------------- Helpers

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            Object.DestroyImmediate(go.GetComponent<Collider>()); // decor only, no physics cost
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        private static void Box(string name, Transform parent, Material mat, Vector3 localPos, Vector3 scale)
        {
            var go = Primitive(PrimitiveType.Cube, name, parent, mat);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
        }

        private static ParticleSystem NewParticleSystem(string name, Transform parent, Material mat, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along +Z; point it up

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.loop = true;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static Gradient Gradient((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(
                colors.Select(k => new GradientColorKey(k.c, k.t)).ToArray(),
                alphas.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
            return g;
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static void SetFirstInBuild(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(scenePath, true) };
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != scenePath));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
