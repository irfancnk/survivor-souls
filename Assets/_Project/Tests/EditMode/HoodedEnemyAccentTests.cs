using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor;
using SurvivorSouls.Editor.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-145: the hooded Meshy enemies read apart. The builder output (run the Game scene builder first): Pyre Thrower,
    /// Hexbound Shaman and The Butcher of Ashgrave use the rim shader with their own rim colour, the two zombies have a baked
    /// hand glow, every other roster material stays plain Simple Lit, and nothing adds a material (draw call) to a body.
    /// </summary>
    public sealed class HoodedEnemyAccentTests
    {
        private static readonly string[] Hooded = { "PyreThrower", "HexboundShaman", "AshgraveButcher" };

        private static Material LoadMaterial(string id) => AssetDatabase.LoadAssetAtPath<Material>(EnemyRosterBuilder.Find(id).MaterialPath);

        private static float Hue(Color c)
        {
            Color.RGBToHSV(new Color(c.r, c.g, c.b) / Mathf.Max(c.r, Mathf.Max(c.g, c.b)), out float h, out _, out _);
            return h;
        }

        [Test]
        public void HoodedTypes_UseTheRimShader_WithTheirOwnRim()
        {
            foreach (string id in Hooded)
            {
                var spec = EnemyRosterBuilder.Find(id);
                var mat = LoadMaterial(id);
                Assert.IsNotNull(mat, id);
                Assert.AreEqual(EnemyRosterBuilder.RimShader, mat.shader.name, id);
                Assert.AreEqual(spec.Rim, mat.GetColor("_RimColor"), id + " rim colour = the builder spec");
                Assert.AreEqual(spec.RimPower, mat.GetFloat("_RimPower"), 1e-4f, id);
                Assert.IsTrue(mat.IsKeywordEnabled("_NORMALMAP"), id + ": the Simple Lit keywords carry over");
                Assert.IsNotNull(mat.GetTexture("_BaseMap"), id);
            }
        }

        [Test]
        public void RimColours_AreFarApartInHue()
        {
            var hues = Hooded.Select(id => Hue(EnemyRosterBuilder.Find(id).Rim)).ToArray();
            for (int a = 0; a < hues.Length; a++)
            for (int b = a + 1; b < hues.Length; b++)
            {
                float d = Mathf.Abs(hues[a] - hues[b]);
                d = Mathf.Min(d, 1f - d);
                Assert.Greater(d, 0.05f, $"{Hooded[a]} vs {Hooded[b]}: rim hues too close");
            }
        }

        [Test]
        public void OtherRosterMaterials_StayPlainSimpleLit()
        {
            foreach (var spec in EnemyRosterBuilder.Roster.Where(s => !Hooded.Contains(s.Id)))
            {
                Assert.AreEqual(0f, spec.Rim.a, spec.Id);
                Assert.AreEqual(EditorAssetUtil.SimpleLit, LoadMaterial(spec.Id).shader.name, spec.Id);
            }
        }

        [TestCase("PyreThrower", true)]
        [TestCase("HexboundShaman", true)]
        [TestCase("AshgraveButcher", false)]
        public void HandGlow_IsBakedIntoTheEmissionMap(string id, bool glows)
        {
            var mat = LoadMaterial(id);
            Assert.AreEqual(glows, mat.IsKeywordEnabled("_EMISSION"), id);
            Assert.AreEqual(glows, mat.GetTexture("_EmissionMap") != null, id);
            if (glows) Assert.AreEqual(Color.white, mat.GetColor("_EmissionColor"), id);
        }

        [Test]
        public void ShamanGlow_IsOnOneHand_PyreThrowerOnBoth()
        {
            Assert.AreEqual("LeftHand + RightHand", GlowHands("PyreThrower", false));
            string shaman = GlowHands("HexboundShaman", true);
            Assert.That(HandGlowBaker.HandBones, Does.Contain(shaman), "the staff hand only");
        }

        private static string GlowHands(string id, bool staffOnly)
        {
            var spec = EnemyRosterBuilder.Find(id);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{spec.ArtDir}/SK_{spec.Id}.fbx");
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var glow = HandGlowBaker.VertexGlow(skin, staffOnly, out string hands);
            int lit = glow.Count(g => g > 0.5f);
            Assert.Greater(lit, 20, id + ": hand vertices glow");
            Assert.Less(lit, glow.Length / 3, id + ": only the hands glow, not the body");
            return hands;
        }

        [Test]
        public void Rasterize_FillsTheTriangle_AndGrowsTheIsland()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                uv = new[] { new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f), new Vector2(0.25f, 0.75f) },
                triangles = new[] { 0, 1, 2 },
            };
            try
            {
                var mask = HandGlowBaker.Rasterize(mesh, new[] { 1f, 1f, 1f }, 64);
                Assert.AreEqual(1f, mask[24 * 64 + 24], 1e-4f, "inside");
                Assert.AreEqual(1f, mask[14 * 64 + 20], 1e-4f, "2 px below the island: grown over the seam");
                Assert.AreEqual(0f, mask[4 * 64 + 4], "far outside");
                Assert.AreEqual(0f, mask[60 * 64 + 60], "far outside");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void RimShader_HasTheForwardPasses_AndNoInspector()
        {
            var shader = Shader.Find(EnemyRosterBuilder.RimShader);
            Assert.IsNotNull(shader);
            Assert.IsTrue(shader.isSupported, "compiles on this platform");
            var mat = new Material(shader);
            try
            {
                var passes = Enumerable.Range(0, mat.passCount).Select(mat.GetPassName).ToArray();
                Assert.That(passes, Is.EquivalentTo(new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals" }));
            }
            finally { Object.DestroyImmediate(mat); }
        }

        [Test]
        public void HoodedPrefabs_KeepOneMaterialPerBody()
        {
            foreach (string id in Hooded)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterBuilder.Find(id).PrefabPath);
                var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.AreEqual(1, renderers.Length, id);
                Assert.AreEqual(1, renderers[0].sharedMaterials.Length, id + ": no extra material = no extra draw call");
                Assert.AreSame(LoadMaterial(id), renderers[0].sharedMaterial, id);
                Assert.AreEqual(ShadowCastingMode.Off, renderers[0].shadowCastingMode, id + ": no realtime shadows");
            }
        }
    }
}
