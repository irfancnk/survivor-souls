using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Characters
{
    /// <summary>
    /// KAN-156: import settings for the Meshy hero and pet art (Art/Characters/Heroes/&lt;Id&gt; and Pets/&lt;Id&gt;), so every
    /// worktree imports them the same way and their committed .meta files (GUIDs) stay put. Same rules as the enemy roster
    /// (<see cref="Enemies.EnemyRosterBuilder"/>): Generic rig, no imported materials (the prefab builders of KAN-153 / KAN-150
    /// make their own), Mesh LODs on the SK_ body; one clip per A_ file named after the file suffix, locomotion / hover /
    /// idle clips loop, action clips (Shoot, Cast) play once; textures max 1024, *_Normal as a normal map.
    /// Every character folder under the two roots is handled, so a new hero or pet only needs the SK_ / A_ / T_ names.
    /// Called by GameSceneBuilder; re-run is safe (an importer that already matches is not touched).
    /// </summary>
    public static class HeroPetArtImporter
    {
        public const string HeroesArt = "Assets/_Project/Art/Characters/Heroes";
        public const string PetsArt = "Assets/_Project/Art/Characters/Pets";
        public const int TextureSize = 1024; // 2k source; hero ~110 px, pets ~60 px tall on a phone

        /// <summary>A_ clip suffixes that loop. Anything else (Shoot, Cast) is an action and plays once.</summary>
        public static readonly string[] LoopClips = { "Idle", "Walk", "Walking", "Running", "Hover" };

        public static bool IsLoopClip(string name) => LoopClips.Contains(name);

        /// <summary>Character folders (one per hero / pet) under both roots.</summary>
        public static string[] CharacterDirs() =>
            new[] { HeroesArt, PetsArt }.Where(AssetDatabase.IsValidFolder)
                .SelectMany(AssetDatabase.GetSubFolders).OrderBy(p => p).ToArray();

        [MenuItem("Survivor Souls/Characters/Configure Hero + Pet Import")]
        public static void Configure()
        {
            var report = new System.Text.StringBuilder("[HeroPetArtImporter] heroes + pets:\n");
            foreach (var dir in CharacterDirs()) report.AppendLine(ConfigureOne(dir));
            Debug.Log(report.ToString());
        }

        private static string ConfigureOne(string dir)
        {
            string id = Path.GetFileName(dir);
            var files = AssetDatabase.FindAssets("", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetDirectoryName(p)?.Replace('\\', '/') == dir).Distinct().OrderBy(p => p).ToArray();
            var clips = new List<string>();
            int tris = 0;
            foreach (var path in files)
            {
                string file = Path.GetFileNameWithoutExtension(path);
                if (path.EndsWith(".fbx") && file == $"SK_{id}")
                {
                    ConfigureModel(path, importAnimation: false, meshLods: true);
                    tris = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Sum(m => (int)m.GetIndexCount(0) / 3);
                }
                else if (path.EndsWith(".fbx") && file.StartsWith($"A_{id}_"))
                {
                    string name = file.Substring($"A_{id}_".Length);
                    ConfigureModel(path, importAnimation: true);
                    float length = ConfigureClip(path, name);
                    clips.Add($"{name}={length:F2}s{(IsLoopClip(name) ? " loop" : "")}");
                }
                else if (file.StartsWith($"T_{id}_"))
                {
                    ConfigureTexture(path, normalMap: file.EndsWith("_Normal"));
                }
            }
            return $"  {id}: SK {tris} tris (LOD0), clips {string.Join(" ", clips)}";
        }

        private static void ConfigureModel(string path, bool importAnimation, bool meshLods = false)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Model missing: {path}");
            if (importer.animationType == ModelImporterAnimationType.Generic && importer.importAnimation == importAnimation
                && importer.materialImportMode == ModelImporterMaterialImportMode.None && importer.generateMeshLods == meshLods) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = importAnimation;
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // the prefab builder makes the material
            importer.generateMeshLods = meshLods; // the top-down camera picks a lower level
            importer.SaveAndReimport();
        }

        /// <summary>One clip per file: the first take, named after the file suffix, full range, loop by <see cref="IsLoopClip"/>.</summary>
        private static float ConfigureClip(string path, string name)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.defaultClipAnimations;
            if (defaults.Length == 0) throw new System.InvalidOperationException($"{path}: no animation take");
            bool loop = IsLoopClip(name);
            var current = importer.clipAnimations;
            var clip = current.Length == 1 ? current[0] : defaults[0];
            bool dirty = current.Length != 1 || clip.loopTime != loop || clip.name != name;
            if (dirty)
            {
                clip.name = name;
                clip.loopTime = loop;
                importer.clipAnimations = new[] { clip };
                importer.SaveAndReimport();
            }
            var asset = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            return asset != null ? asset.length : 0f;
        }

        private static void ConfigureTexture(string path, bool normalMap)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.maxTextureSize == TextureSize && importer.textureType == type) return;
            importer.textureType = type;
            importer.maxTextureSize = TextureSize;
            importer.SaveAndReimport();
        }
    }
}
