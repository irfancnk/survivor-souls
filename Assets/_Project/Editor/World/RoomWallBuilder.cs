using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Stone walls around the Chamber room (KAN-232): one dry-stone wall on each edge of the playable rectangle, inner face
    /// on the edge, with pillars every ~8 m and snow on the caps. The north wall stands on the beach before the water and
    /// the side walls are 1.4-1.9 m; the south wall faces the camera and stays low (0.6-0.8 m), so the Knight and the
    /// horde stay visible when they fight against it. Invisible 4 m box colliders hold the edges (walkability itself comes
    /// from <see cref="WorldStreamer.PlayableMin"/> / <see cref="WorldStreamer.PlayableMax"/>). Everything is a child of a
    /// <see cref="RoomWalls"/> object, which hides in a boss Chamber. Mobile: 2 meshes (stone, snow), 2 draw calls.
    /// </summary>
    public static class RoomWallBuilder
    {
        private const string MeshDir = "Assets/_Project/Art/World";
        private const float Thickness = 0.8f;
        private const float PillarEvery = 8f;

        public static RoomWalls Build(Transform root, Vector2 min, Vector2 max, Material stone, Material snow)
        {
            var go = new GameObject("Room Walls");
            go.transform.SetParent(root, false);
            var walls = go.AddComponent<RoomWalls>();
            var rng = new System.Random(232);
            var stoneMb = new MeshBuilder();
            var snowMb = new MeshBuilder();
            float o = Thickness * 0.5f; // wall centre line just outside the edge: the inner face is the edge
            var nw = new Vector3(min.x - o, 0f, max.y + o);
            var ne = new Vector3(max.x + o, 0f, max.y + o);
            var sw = new Vector3(min.x - o, 0f, min.y - o);
            var se = new Vector3(max.x + o, 0f, min.y - o);
            Wall(stoneMb, snowMb, nw, ne, rng, 1.5f, 1.9f, 2.5f);  // north, on the beach
            Wall(stoneMb, snowMb, sw, nw, rng, 1.4f, 1.8f, 2.3f);  // west
            Wall(stoneMb, snowMb, se, ne, rng, 1.4f, 1.8f, 2.3f);  // east
            Wall(stoneMb, snowMb, sw, se, rng, 0.6f, 0.8f, 1.1f);  // south, camera side: low
            foreach (var corner in new[] { nw, ne })
                Pillar(stoneMb, snowMb, corner, rng, 2.8f);
            foreach (var corner in new[] { sw, se })
                Pillar(stoneMb, snowMb, corner, rng, 1.3f);
            Part(go.transform, "Stone", stoneMb, stone);
            Part(go.transform, "Snow", snowMb, snow);

            float width = max.x - min.x, depth = max.y - min.y;
            float midX = (min.x + max.x) * 0.5f, midZ = (min.y + max.y) * 0.5f;
            (Vector3 c, Vector3 s)[] bounds =
            {
                (new Vector3(midX, 2f, max.y + 1f), new Vector3(width + 4f, 4f, 2f)),
                (new Vector3(midX, 2f, min.y - 1f), new Vector3(width + 4f, 4f, 2f)),
                (new Vector3(max.x + 1f, 2f, midZ), new Vector3(2f, 4f, depth + 4f)),
                (new Vector3(min.x - 1f, 2f, midZ), new Vector3(2f, 4f, depth + 4f)),
            };
            foreach (var (c, s) in bounds)
            {
                var b = new GameObject("Bound");
                b.transform.SetParent(go.transform, false);
                b.transform.position = c;
                b.AddComponent<BoxCollider>().size = s;
            }
            return walls;
        }

        /// <summary>Solid dry-stone run from a to b: blocks of random height, slight yaw jitter, snow on each block; pillars every ~8 m.</summary>
        private static void Wall(MeshBuilder stone, MeshBuilder snow, Vector3 a, Vector3 b, System.Random rng, float minH, float maxH, float pillarH)
        {
            var uv = PropKit.StoneUv(2f);
            var snowUv = PropKit.StoneUv(4f);
            var dir = (b - a).normalized;
            float length = (b - a).magnitude;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            int pillars = Mathf.Max(1, Mathf.RoundToInt(length / PillarEvery));
            for (int i = 1; i < pillars; i++) Pillar(stone, snow, a + dir * (length * i / pillars), rng, pillarH);
            float t = 0f;
            while (t < length - 0.05f)
            {
                float seg = Mathf.Min(PropKit.Range(rng, 1.2f, 2.4f), length - t);
                float h = PropKit.Range(rng, minH, maxH);
                var centre = a + dir * (t + seg * 0.5f) + Vector3.up * (h * 0.5f);
                var segRot = rot * Quaternion.Euler(0f, PropKit.Range(rng, -1.5f, 1.5f), 0f);
                stone.Box(centre, new Vector3(Thickness, h, seg + 0.06f), segRot, uv, 0);
                snow.Box(centre + Vector3.up * (h * 0.5f + 0.04f), new Vector3(Thickness * 0.92f, 0.1f, seg * PropKit.Range(rng, 0.7f, 0.95f)),
                    segRot, snowUv, 0);
                t += seg;
            }
        }

        private static void Pillar(MeshBuilder stone, MeshBuilder snow, Vector3 p, System.Random rng, float h)
        {
            PropKit.Pillar(stone, p, rng, h);
            snow.Box(p + Vector3.up * (h + 0.3f), new Vector3(0.95f, 0.12f, 0.95f), Quaternion.Euler(0f, PropKit.Range(rng, -8f, 8f), 0f),
                PropKit.StoneUv(4f), 0);
        }

        private static void Part(Transform root, string part, MeshBuilder mb, Material mat)
        {
            if (mb.VertexCount == 0) return;
            var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/Room_Walls_{part}.asset");
            mb.WriteTo(mesh);
            EditorUtility.SetDirty(mesh);
            var go = new GameObject(part);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
        }
    }
}
