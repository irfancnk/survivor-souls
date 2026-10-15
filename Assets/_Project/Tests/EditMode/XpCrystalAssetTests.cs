using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Editor.Progression;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-177: the Blender soul crystal (<c>SM_XpCrystal.fbx</c>, from <c>Tools/Blender/build_xp_crystal.py</c>) imports
    /// upright (Y up), height 1 (the particle size is the crystal height), about half as wide, centred, readable for CPU
    /// mesh particles, inside the triangle budget, with the shader's UV0 data (height 0..1, facet id) and flat facets
    /// (split normals: more vertices than a smooth gem would need).
    /// </summary>
    public sealed class XpCrystalAssetTests
    {
        [Test]
        public void CrystalMesh_UprightHeightOne_Readable_InBudget_WithShaderData()
        {
            var mesh = ProgressionBuilder.LoadCrystalMesh();
            Assert.That(mesh.isReadable, "CPU mesh particles need a readable mesh");
            var b = mesh.bounds;
            Assert.That(b.size.y, Is.EqualTo(1f).Within(0.01f), "height 1, Y up");
            Assert.That(b.size.x, Is.InRange(0.4f, 0.6f), "about half as wide");
            Assert.That(b.size.z, Is.InRange(0.4f, 0.6f));
            Assert.That(b.center.magnitude, Is.LessThan(0.02f), "centred: the particle position is the crystal centre");
            Assert.That(mesh.triangles.Length / 3, Is.LessThan(400), "triangle budget");

            var uv = new List<Vector2>();
            mesh.GetUVs(0, uv);
            Assert.That(uv.Count, Is.EqualTo(mesh.vertexCount), "UV0 data on every vertex");
            float minU = 1f, maxU = 0f;
            var verts = mesh.vertices;
            for (int i = 0; i < uv.Count; i++)
            {
                minU = Mathf.Min(minU, uv[i].x);
                maxU = Mathf.Max(maxU, uv[i].x);
                Assert.That(uv[i].x, Is.EqualTo(verts[i].y + 0.5f).Within(0.02f), "u = height from the bottom tip");
            }
            Assert.That(minU, Is.LessThan(0.02f));
            Assert.That(maxU, Is.GreaterThan(0.98f));
            Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
        }
    }
}
