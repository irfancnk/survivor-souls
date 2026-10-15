using System.IO;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor.Characters;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-156: hero + pet art import settings as <see cref="HeroPetArtImporter"/> leaves them (it runs first here; it is a
    /// no-op when they already match). Every character folder has its SK_ body, clips and textures set the same way, so
    /// the committed .meta files do not change on a fresh worktree.
    /// </summary>
    public sealed class HeroPetArtImporterTests
    {
        private static readonly string[] Expected = { "ShadowRanger", "CinderSeraph", "GargoyleFamiliar", "LanternSprite" };

        [OneTimeSetUp]
        public void Configure() => HeroPetArtImporter.Configure();

        private static string[] Dirs => HeroPetArtImporter.CharacterDirs();

        private static string[] Files(string dir, string prefix) =>
            AssetDatabase.FindAssets("", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p).StartsWith(prefix)).Distinct().ToArray();

        [Test]
        public void CharacterDirs_HaveTheHeroAndThreePets()
        {
            Assert.That(Dirs.Select(Path.GetFileName), Is.SupersetOf(Expected));
            Assert.That(Dirs.Count(d => d.StartsWith(HeroPetArtImporter.HeroesArt)), Is.GreaterThanOrEqualTo(1));
            Assert.That(Dirs.Count(d => d.StartsWith(HeroPetArtImporter.PetsArt)), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Body_IsGenericWithLodsAndNoMaterials([ValueSource(nameof(Dirs))] string dir)
        {
            string id = Path.GetFileName(dir);
            var importer = (ModelImporter)AssetImporter.GetAtPath($"{dir}/SK_{id}.fbx");
            Assert.That(importer, Is.Not.Null, $"SK_{id}.fbx");
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.materialImportMode, Is.EqualTo(ModelImporterMaterialImportMode.None));
            Assert.That(importer.generateMeshLods, Is.True);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<Mesh>().Any(), "skinned mesh");
        }

        [Test]
        public void Clips_AreNamedAndLoopByKind([ValueSource(nameof(Dirs))] string dir)
        {
            string id = Path.GetFileName(dir);
            var paths = Files(dir, $"A_{id}_").Where(p => p.EndsWith(".fbx")).ToArray();
            Assert.That(paths, Is.Not.Empty, $"{id}: no A_ clips");
            foreach (var path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path).Substring($"A_{id}_".Length);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic), path);
                Assert.That(importer.materialImportMode, Is.EqualTo(ModelImporterMaterialImportMode.None), path);
                Assert.That(importer.clipAnimations.Length, Is.EqualTo(1), path);
                Assert.That(importer.clipAnimations[0].name, Is.EqualTo(name), path);
                Assert.That(importer.clipAnimations[0].loopTime, Is.EqualTo(HeroPetArtImporter.IsLoopClip(name)), path);
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
                Assert.That(clip, Is.Not.Null, path);
                Assert.That(clip.length, Is.GreaterThan(0.1f), path);
            }
        }

        [Test]
        public void Textures_AreMobileSizedAndNormalMapsTyped([ValueSource(nameof(Dirs))] string dir)
        {
            string id = Path.GetFileName(dir);
            var paths = Files(dir, $"T_{id}_");
            Assert.That(paths.Any(p => Path.GetFileNameWithoutExtension(p).EndsWith("_BaseColor")), $"{id}: base colour");
            foreach (var path in paths)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = Path.GetFileNameWithoutExtension(path).EndsWith("_Normal");
                Assert.That(importer.maxTextureSize, Is.EqualTo(HeroPetArtImporter.TextureSize), path);
                Assert.That(importer.textureType, Is.EqualTo(normal ? TextureImporterType.NormalMap : TextureImporterType.Default), path);
            }
        }

        [Test]
        public void LoopRule_ActionsPlayOnce()
        {
            Assert.That(HeroPetArtImporter.IsLoopClip("Running"));
            Assert.That(HeroPetArtImporter.IsLoopClip("Hover"));
            Assert.That(HeroPetArtImporter.IsLoopClip("Idle"));
            Assert.That(HeroPetArtImporter.IsLoopClip("Shoot"), Is.False);
            Assert.That(HeroPetArtImporter.IsLoopClip("Cast"), Is.False);
        }
    }
}
