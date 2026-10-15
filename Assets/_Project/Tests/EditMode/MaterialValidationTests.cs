using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// Builder materials are already in the state the URP inspector writes (ShaderGUI.ValidateMaterial), so opening them or a
    /// test session never changes a .mat in git. Simple Lit specular look per family (KAN-158).
    /// </summary>
    public class MaterialValidationTests
    {
        private const string TempDir = "Assets/_Project/Tests/__Kan158Temp";
        private static readonly string[] Passes = { "ShadowCaster", "DepthOnly", "DepthNormals", "MotionVectors", "Meta", "Universal2D" };

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        [Test]
        public void EveryUrpMaterial_IsAlreadyValidated()
        {
            var problems = new List<string>();
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                string shader = mat.shader.name;
                if (shader != EditorAssetUtil.SimpleLit && shader != EditorAssetUtil.Unlit && shader != EditorAssetUtil.ParticlesUnlit) continue;
                checkedCount++;
                var copy = new Material(mat);
                try
                {
                    EditorAssetUtil.ValidateLikeInspector(copy);
                    string diff = Diff(mat, copy);
                    if (diff != null) problems.Add($"{path}: {diff}");
                }
                finally { Object.DestroyImmediate(copy); }
            }
            Assert.Greater(checkedCount, 20, "expected the Simple Lit / Unlit / particle materials of the project");
            Assert.IsEmpty(problems, "URP validation would change these materials (rebuild them with the builder):\n" + string.Join("\n", problems));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void SimpleLitMaterial_IsValidated_AndSpecularMatchesFlag(bool specular, bool withMap)
        {
            var map = withMap ? new Texture2D(4, 4) : null;
            try
            {
                var mat = EditorAssetUtil.SimpleLitMaterial(TempDir + "/M_Test.mat", null, Color.white, specMap: map, smoothness: 0.3f,
                    specColor: new Color(0.2f, 0.2f, 0.2f), specular: specular);
                Assert.AreEqual(specular ? 0f : 1f, mat.GetFloat("_SpecularHighlights"));
                Assert.AreEqual(specular && !withMap, mat.IsKeywordEnabled("_SPECULAR_COLOR"));
                Assert.AreEqual(specular && withMap, mat.IsKeywordEnabled("_SPECGLOSSMAP"));
                var copy = new Material(mat);
                EditorAssetUtil.ValidateLikeInspector(copy);
                Assert.IsNull(Diff(mat, copy));
                Object.DestroyImmediate(copy);
            }
            finally { if (map != null) Object.DestroyImmediate(map); }
        }

        [Test]
        public void TransparentAndParticleMaterials_AreValidated()
        {
            var tex = new Texture2D(4, 4);
            try
            {
                var mats = new[]
                {
                    EditorAssetUtil.TransparentMaterial(TempDir + "/M_Decal.mat", EditorAssetUtil.SimpleLit, tex, new Color(0f, 0f, 0f, 0.5f)),
                    EditorAssetUtil.TransparentMaterial(TempDir + "/M_Blob.mat", EditorAssetUtil.Unlit, tex, new Color(0f, 0f, 0f, 0.5f)),
                    EditorAssetUtil.ParticleMaterial(TempDir + "/M_Add.mat", tex, additive: true),
                    EditorAssetUtil.ParticleMaterial(TempDir + "/M_Alpha.mat", tex, additive: false),
                };
                foreach (var mat in mats)
                {
                    var copy = new Material(mat);
                    EditorAssetUtil.ValidateLikeInspector(copy);
                    Assert.IsNull(Diff(mat, copy), mat.name);
                    Object.DestroyImmediate(copy);
                }
            }
            finally { Object.DestroyImmediate(tex); }
        }

        [TestCase("Assets/_Project/Art/Materials/Game/M_Palette.mat", true)]
        [TestCase("Assets/_Project/Art/Materials/Game/M_Floor.mat", true)]
        [TestCase("Assets/_Project/Art/Materials/Game/M_Wall.mat", true)]
        [TestCase("Assets/_Project/Art/Boss/M_HeartEmber.mat", true)]
        [TestCase("Assets/_Project/Art/Materials/Game/M_OuterGround.mat", false)]
        [TestCase("Assets/_Project/Art/Characters/Zombie/M_Zombie.mat", false)]
        [TestCase("Assets/_Project/Art/Characters/Zombie/M_ZombieBrute.mat", false)]
        [TestCase("Assets/_Project/Art/Characters/Enemies/PlagueBloater/M_PlagueBloater.mat", false)]
        [TestCase("Assets/_Project/Art/Characters/Bosses/AshgraveButcher/M_AshgraveButcher.mat", false)]
        public void ShippedSpecularLook_PerFamily(string path, bool specular)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(mat, path);
            Assert.AreEqual(specular ? 0f : 1f, mat.GetFloat("_SpecularHighlights"), "stone / props / heart on, ground / characters off");
            Assert.AreEqual(specular, mat.IsKeywordEnabled("_SPECULAR_COLOR") || mat.IsKeywordEnabled("_SPECGLOSSMAP"));
        }

        [Test]
        public void HealingHeart_KeepsItsGlow()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Boss/M_HeartEmber.mat");
            Assert.IsTrue(mat.IsKeywordEnabled("_EMISSION"), "validation drops _EMISSION without an emissive GI flag");
        }

        /// <summary>First difference between a material and its validated copy, or null.</summary>
        private static string Diff(Material a, Material b)
        {
            var ka = a.shaderKeywords.OrderBy(k => k).ToArray();
            var kb = b.shaderKeywords.OrderBy(k => k).ToArray();
            if (!ka.SequenceEqual(kb)) return $"keywords [{string.Join(",", ka)}] -> [{string.Join(",", kb)}]";
            if (a.renderQueue != b.renderQueue) return $"queue {a.renderQueue} -> {b.renderQueue}";
            if (a.globalIlluminationFlags != b.globalIlluminationFlags) return $"GI flags {a.globalIlluminationFlags} -> {b.globalIlluminationFlags}";
            if (a.GetTag("RenderType", false) != b.GetTag("RenderType", false)) return "RenderType tag";
            foreach (string pass in Passes)
                if (a.GetShaderPassEnabled(pass) != b.GetShaderPassEnabled(pass)) return $"pass {pass} {a.GetShaderPassEnabled(pass)} -> {b.GetShaderPassEnabled(pass)}";
            var shader = a.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        float fa = a.GetFloat(name), fb = b.GetFloat(name);
                        if (!fa.Equals(fb)) return $"{name} {fa} -> {fb}";
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        Vector4 va = a.GetVector(name), vb = b.GetVector(name);
                        if (!va.Equals(vb)) return $"{name} {va} -> {vb}";
                        break;
                }
            }
            return null;
        }
    }
}
