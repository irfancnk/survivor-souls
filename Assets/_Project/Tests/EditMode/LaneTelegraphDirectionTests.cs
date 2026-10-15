using NUnit.Framework;
using SurvivorSouls.Editor.Boss;
using SurvivorSouls.Gameplay.Boss;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-166: the chevrons of the lane fill texture point from the attacker to the far end of the lane. Two halves:
    /// the texture (each band's centre is further along v than its sides) and the placement (<see cref="LineTelegraph"/>
    /// lays the quad so v 0 is at the attacker and v 1 at the far end, in every direction).
    /// </summary>
    public sealed class LaneTelegraphDirectionTests
    {
        /// <summary>v (0..1) of the strongest chevron pixel in column <paramref name="x"/> near the band of chevron <paramref name="i"/>.</summary>
        private static float BandPeakV(int x, int i)
        {
            var size = BossBuilder.LaneTextureSize;
            float tip = BossBuilder.LaneChevronTip(i), best = -1f, bestV = -1f;
            for (int y = 0; y < size.y; y++)
            {
                float v = (y + 0.5f) / size.y;
                if (v < tip - BossBuilder.LaneChevronSweep - 0.015f || v > tip + 0.015f) continue; // this band only (bands 0.09 apart)
                float a = BossBuilder.LaneFillPixel(x, y).a;
                if (a > best + 1e-5f) { best = a; bestV = v; }
            }
            return bestV;
        }

        [Test]
        public void FillTexture_ChevronTipsPointToTheFarEnd()
        {
            var size = BossBuilder.LaneTextureSize;
            int centre = size.x / 2, side = Mathf.RoundToInt(size.x * 0.12f); // u 0 and u ~0.76 (inside the soft sides)
            for (int i = 0; i < 3; i++)
            {
                float tipV = BandPeakV(centre, i), sideV = BandPeakV(side, i), mirrorV = BandPeakV(size.x - 1 - side, i);
                Assert.That(tipV, Is.GreaterThan(sideV + 0.02f), $"chevron {i}: centre must lead its sides towards the far end");
                Assert.That(mirrorV, Is.EqualTo(sideV).Within(1f / size.y + 1e-4f), $"chevron {i}: symmetric");
            }
        }

        [Test]
        public void FillTexture_ChevronsStayInsideTheLane()
        {
            Assert.That(BossBuilder.LaneChevronTip(0) - BossBuilder.LaneChevronSweep, Is.GreaterThan(0.5f), "bands sit near the tip");
            Assert.That(BossBuilder.LaneChevronTip(2), Is.LessThan(0.95f), "last tip before the end fade");
        }

        [Test]
        public void Placement_TextureVRunsFromTheAttackerToTheFarEnd([Values(0f, 90f, 200f, 315f)] float yaw)
        {
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var root = new GameObject("Lane Test");
            try
            {
                var edge = NewQuad("Edge", root.transform, quad);
                var fill = NewQuad("Fill", root.transform, quad);
                var lane = root.AddComponent<LineTelegraph>();
                lane.Setup(new Renderer[] { edge }, new Renderer[] { fill });
                var origin = new Vector3(3f, 0f, -2f);
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                lane.ShowLine(origin, dir, 10f, 2f);

                Vector3 nearV = Vector3.zero, farV = Vector3.zero;
                int near = 0, far = 0;
                var verts = quad.vertices;
                var uvs = quad.uv;
                for (int k = 0; k < verts.Length; k++)
                {
                    var world = edge.transform.TransformPoint(verts[k]);
                    if (uvs[k].y < 0.5f) { nearV += world; near++; }
                    else { farV += world; far++; }
                }
                nearV /= near;
                farV /= far;
                Assert.That(Vector3.Dot(nearV - origin, dir), Is.EqualTo(0f).Within(0.01f), "v 0 at the attacker");
                Assert.That(Vector3.Dot(farV - origin, dir), Is.EqualTo(10f).Within(0.01f), "v 1 at the far end");
                Assert.That(edge.transform.TransformDirection(Vector3.back).y, Is.GreaterThan(0.99f), "quad faces up (seen from the camera)");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static MeshRenderer NewQuad(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }
    }
}
