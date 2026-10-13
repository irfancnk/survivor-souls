using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Props
{
    /// <summary>
    /// Imports props cleaned by <c>Tools/Blender/clean_prop.py</c> (KAN-27). Source = the Drive
    /// <c>Approved/</c> folder: one folder per prop with <c>SM_*.fbx</c> (+ _LOD1.. objects), <c>T_*_BC/_N/_MASK.png</c>
    /// and <c>SM_*.json</c> (meta + cleanup result).
    /// Per prop: name check (KAN-26), copy into <c>Art/Environment/&lt;Realm&gt;/</c>, model + ASTC texture settings,
    /// URP Lit material <c>M_*</c>, prefab <c>P_*</c> (optional box / capsule collider, static).
    /// Menu: Survivor Souls/Import Generated Props.
    /// Batch: <c>-executeMethod SurvivorSouls.Editor.Props.GeneratedPropImporter.ImportFromCommandLine [-ss-props &lt;dir&gt;]</c>
    /// </summary>
    public static class GeneratedPropImporter
    {
        public const string EnvironmentRoot = "Assets/_Project/Art/Environment";
        private const string SourceEnv = "SS_PROP_SOURCE";
        private static readonly string DefaultSource = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "survivor-souls-assets/drive/SurvivorSouls/Assets/Approved");

        public static string SourceDir => Environment.GetEnvironmentVariable(SourceEnv) is { Length: > 0 } dir ? dir : DefaultSource;

        [MenuItem("Survivor Souls/Import Generated Props")]
        public static void ImportMenu()
        {
            var report = Import(SourceDir);
            EditorUtility.DisplayDialog("Import Generated Props", report.Summary, "OK");
        }

        public static void ImportFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-ss-props");
            var report = Import(i >= 0 && i + 1 < args.Length ? args[i + 1] : SourceDir);
            EditorApplication.Exit(report.Errors.Count == 0 && report.Imported.Count > 0 ? 0 : 1);
        }

        public sealed class Report
        {
            public readonly List<string> Imported = new();
            public readonly List<string> Errors = new();
            public string Summary => $"{Imported.Count} imported, {Errors.Count} errors" +
                                     (Errors.Count > 0 ? "\n" + string.Join("\n", Errors) : "");
        }

        public static Report Import(string sourceDir)
        {
            var report = new Report();
            if (!Directory.Exists(sourceDir))
            {
                report.Errors.Add($"source folder not found: {sourceDir}");
                Debug.LogError($"[PropImporter] {report.Summary}");
                return report;
            }

            foreach (string folder in Directory.GetDirectories(sourceDir).OrderBy(f => f))
            {
                string name = Path.GetFileName(folder);
                try
                {
                    string error = PropNaming.Check(name) ?? (File.Exists(Path.Combine(folder, name + ".fbx")) ? null : "no FBX");
                    if (error != null)
                    {
                        report.Errors.Add($"{name}: {error}");
                        continue;
                    }
                    ImportProp(folder, name);
                    report.Imported.Add(name);
                }
                catch (Exception e)
                {
                    report.Errors.Add($"{name}: {e.Message}");
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[PropImporter] {sourceDir}: {report.Summary}" +
                      (report.Imported.Count > 0 ? "\n" + string.Join("\n", report.Imported) : ""));
            return report;
        }

        private static void ImportProp(string folder, string name)
        {
            string body = name.Substring(3); // SM_R1_Rock_Boulder_01 -> R1_Rock_Boulder_01
            string dir = PropNaming.RealmFolder(name);
            string meshDir = $"{dir}/Meshes", texDir = $"{dir}/Textures", matDir = $"{dir}/Materials", prefabDir = $"{dir}/Prefabs";
            foreach (var d in new[] { meshDir, texDir, matDir, prefabDir }) EditorAssetUtil.EnsureFolder(d);

            var meta = ReadMeta(Path.Combine(folder, name + ".json"));
            int texSize = meta.cleanup_result != null && meta.cleanup_result.texture_size > 0 ? meta.cleanup_result.texture_size : 512;

            // Copy (overwrite) into the project; the postprocessor below sets model import settings.
            string fbxPath = $"{meshDir}/{name}.fbx";
            File.Copy(Path.Combine(folder, name + ".fbx"), fbxPath, true);
            File.Copy(Path.Combine(folder, name + ".json"), $"{meshDir}/{name}.json", true);
            var tex = new Dictionary<string, string>();
            foreach (var suffix in new[] { "BC", "N", "MASK" })
            {
                string file = $"T_{body}_{suffix}.png";
                if (!File.Exists(Path.Combine(folder, file))) continue;
                File.Copy(Path.Combine(folder, file), $"{texDir}/{file}", true);
                tex[suffix] = $"{texDir}/{file}";
            }
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);

            var baseMap = tex.TryGetValue("BC", out var bc) ? ConfigureTexture(bc, TextureImporterType.Default, true, texSize) : null;
            var normal = tex.TryGetValue("N", out var n) ? ConfigureTexture(n, TextureImporterType.NormalMap, false, texSize) : null;
            var mask = tex.TryGetValue("MASK", out var m) ? ConfigureTexture(m, TextureImporterType.Default, false, texSize) : null;
            var material = LitMaterial($"{matDir}/M_{body}.mat", baseMap, normal, mask);

            BuildPrefab(fbxPath, $"{prefabDir}/P_{body}.prefab", material, meta.collider);
        }

        private static Texture2D ConfigureTexture(string path, TextureImporterType type, bool srgb, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = platform, overridden = true, maxTextureSize = maxSize,
                    format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50,
                });
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material LitMaterial(string path, Texture2D baseMap, Texture2D normal, Texture2D mask)
        {
            var mat = EditorAssetUtil.LoadOrCreateMaterial(path, "Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            SetKeyword(mat, "_NORMALMAP", normal != null);
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetFloat("_Metallic", 1f);                 // map is the value when set
            mat.SetFloat("_Smoothness", 0.6f);             // scales map alpha: generated textures are often too glossy
            mat.SetFloat("_SmoothnessTextureChannel", 0f); // smoothness in metallic alpha
            SetKeyword(mat, "_METALLICSPECGLOSSMAP", mask != null);
            mat.SetFloat("_EnvironmentReflections", 0f);   // no reflection probes in the world
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildPrefab(string fbxPath, string prefabPath, Material material, string collider)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new InvalidOperationException($"model not imported: {fbxPath}");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = Path.GetFileNameWithoutExtension(prefabPath);
            try
            {
                var renderers = go.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length == 0) throw new InvalidOperationException("no mesh renderer in FBX");
                foreach (var r in renderers)
                {
                    r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // no realtime shadows in game
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                var bounds = renderers[0].bounds; // LOD0, instance at the origin
                switch (collider)
                {
                    case "none":
                        break;
                    case "capsule":
                        var cap = go.AddComponent<CapsuleCollider>();
                        cap.center = bounds.center;
                        cap.height = bounds.size.y;
                        cap.radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
                        break;
                    default:
                        var box = go.AddComponent<BoxCollider>();
                        box.center = bounds.center;
                        box.size = bounds.size;
                        break;
                }
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void SetKeyword(Material mat, string keyword, bool on)
        {
            if (on) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }

        // ------------------------------------------------------------ meta.json (subset)

        [Serializable]
        private sealed class Meta
        {
            public string collider = "box";
            public CleanupResult cleanup_result;
        }

        [Serializable]
        private sealed class CleanupResult
        {
            public int texture_size;
        }

        private static Meta ReadMeta(string path)
        {
            var meta = File.Exists(path) ? JsonUtility.FromJson<Meta>(File.ReadAllText(path)) : null;
            meta ??= new Meta();
            if (string.IsNullOrEmpty(meta.collider)) meta.collider = "box";
            return meta;
        }

        // ------------------------------------------------------------ model import settings

        private sealed class Postprocessor : AssetPostprocessor
        {
            private void OnPreprocessModel()
            {
                if (!assetPath.StartsWith(EnvironmentRoot + "/", StringComparison.Ordinal)) return;
                var importer = (ModelImporter)assetImporter;
                importer.globalScale = 1f;
                importer.useFileScale = true;       // Blender export applies scale: 1 unit = 1 m
                importer.bakeAxisConversion = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importBlendShapes = false;
                importer.isReadable = false;
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.addCollider = false;
                importer.generateSecondaryUV = false;
            }
        }
    }

    /// <summary>
    /// Prop names from KAN-26 (proposed): <c>SM_&lt;Realm&gt;_&lt;Category&gt;_&lt;Name&gt;_&lt;Variant&gt;</c>,
    /// e.g. <c>SM_R1_Rock_Boulder_01</c>. Realm = R1, R2, ... or Gen (shared).
    /// </summary>
    public static class PropNaming
    {
        public static readonly string[] Categories =
        {
            "Rock", "Cliff", "Ice", "Snow", "Tree", "Plant", "Wreck", "Pier", "Ruin", "Prop", "Light",
            "Grave", "Wall", "Door", "Terrain", "Water", "Sky", "Decor",
        };

        private static readonly Regex Pattern =
            new(@"^SM_(?<realm>R[0-9]+|Gen)_(?<cat>[A-Z][A-Za-z]*)_(?<name>[A-Z][A-Za-z0-9]*)_(?<variant>[0-9]{2})$");

        /// <summary>Null when the name is valid, else the reason.</summary>
        public static string Check(string name)
        {
            var m = Pattern.Match(name);
            if (!m.Success) return "name does not match SM_<Realm>_<Category>_<Name>_<NN> (KAN-26)";
            if (!Categories.Contains(m.Groups["cat"].Value)) return $"unknown category '{m.Groups["cat"].Value}'";
            return null;
        }

        /// <summary>R1 -> Environment/R1_SnowyShore, Gen -> Environment/Shared, other realms -> Environment/Rn.</summary>
        public static string RealmFolder(string name)
        {
            string realm = Pattern.Match(name).Groups["realm"].Value;
            string folder = realm switch { "R1" => "R1_SnowyShore", "Gen" => "Shared", _ => realm };
            return $"{GeneratedPropImporter.EnvironmentRoot}/{folder}";
        }
    }
}
