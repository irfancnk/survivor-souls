using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.FX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Arena
{
    /// <summary>
    /// Builds the first arena: a long ruined courtyard (Archero-style room, portrait).
    /// Stone floor, low broken walls, pillars, rubble, six braziers (warm light), ground mist.
    /// Performance rules (mobile, 60 FPS):
    /// - Static geometry is merged per material into a few meshes, split into Z chunks so each chunk
    ///   gets its own nearest 4 torch lights (URP forward per-object light limit is 4).
    /// - No realtime shadows. No colliders on decor; separate invisible box colliders block movement.
    /// Layout constants below define the playfield; the camera and spawn read them.
    /// </summary>
    public static class ArenaBuilder
    {
        public const float HalfWidth = 5f;     // playable X: -5..5
        public const float HalfLength = 13f;   // playable Z: -13..13
        public static readonly Vector3 PlayerSpawn = new Vector3(0f, 0f, -8f);

        private const string MeshDir = "Assets/_Project/Art/Arena";
        private const float WallLine = 5.6f;   // wall centre X
        private const float EndLine = 14.1f;   // wall centre Z
        private static readonly float[] ChunkEdges = { -5f, 5f }; // 3 chunks along Z
        private static readonly Color TorchColor = new Color(1f, 0.6f, 0.3f);

        private sealed class Chunked
        {
            public readonly MeshBuilder[] Parts = { new MeshBuilder(), new MeshBuilder(), new MeshBuilder() };
            public MeshBuilder At(float z) => Parts[z < ChunkEdges[0] ? 0 : z < ChunkEdges[1] ? 1 : 2];
        }

        public static void Build(Transform parent, GameArtKit kit)
        {
            var rng = new System.Random(1337); // fixed seed: same layout every rebuild
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);

            var floor = new Chunked();
            var walls = new Chunked();
            var props = new Chunked();
            var colliders = new List<(Vector3 centre, Vector3 size, Quaternion rot)>();

            BuildFloor(floor);
            BuildWalls(walls, colliders, rng);
            BuildPillars(walls, colliders, rng);
            BuildRubble(walls, rng);
            var braziers = BuildBraziers(props);
            foreach (var b in braziers) colliders.Add((b + Vector3.up, new Vector3(0.8f, 2f, 0.8f), Quaternion.identity));

            Emit(root, "Floor", floor, kit.Floor);
            Emit(root, "Walls", walls, kit.Wall);
            Emit(root, "Props", props, kit.Palette);
            BuildOuterGround(root, kit);
            BuildRuneDecal(root, kit);
            foreach (var p in braziers) BuildFire(root, p, kit);
            BuildMist(root, kit);
            BuildColliders(root, colliders);
        }

        // ------------------------------------------------------------ Geometry

        private static MeshBuilder.UvMapper StoneUv(float tile) => (p, n) =>
        {
            var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z) / tile;
            return a.x >= a.z ? new Vector2(p.z, p.y) / tile : new Vector2(p.x, p.y) / tile;
        };

        private static void BuildFloor(Chunked floor)
        {
            // Floor slabs per chunk; slightly wider than the walls so no gaps show.
            float[] z = { -EndLine - 1f, ChunkEdges[0], ChunkEdges[1], EndLine + 1f };
            for (int i = 0; i < 3; i++)
            {
                float len = z[i + 1] - z[i];
                floor.Parts[i].Box(new Vector3(0f, -0.1f, (z[i] + z[i + 1]) * 0.5f), new Vector3((WallLine + 1f) * 2f, 0.2f, len),
                    Quaternion.identity, StoneUv(4f), 0);
            }
        }

        private static void BuildWalls(Chunked walls, List<(Vector3, Vector3, Quaternion)> colliders, System.Random rng)
        {
            var uv = StoneUv(2f);
            // Side walls (X = +-WallLine): broken segments, low enough to read the arena from above.
            foreach (float side in new[] { -1f, 1f })
            {
                float z = -EndLine;
                while (z < EndLine)
                {
                    float len = Range(rng, 1.4f, 3.2f);
                    float zEnd = Mathf.Min(z + len, EndLine);
                    bool gap = rng.NextDouble() < 0.18;
                    float h = gap ? Range(rng, 0.15f, 0.35f) : Range(rng, 0.6f, 1.5f);
                    var centre = new Vector3(side * (WallLine + Range(rng, -0.08f, 0.08f)), h * 0.5f, (z + zEnd) * 0.5f);
                    var size = new Vector3(0.8f, h, zEnd - z - 0.04f);
                    var rot = Quaternion.Euler(0f, Range(rng, -1.5f, 1.5f), 0f);
                    walls.At(centre.z).Box(centre, size, rot, uv, 0);
                    if (!gap && h > 1f) AddCapStones(walls, centre, size, rot, rng); // ragged top
                    z = zEnd;
                }
            }

            // Far wall (north, top of screen): taller, frames the arena. Near wall (south): very low.
            foreach (float end in new[] { -1f, 1f })
            {
                bool far = end > 0f;
                float x = -WallLine;
                while (x < WallLine)
                {
                    float len = Range(rng, 1.3f, 2.6f);
                    float xEnd = Mathf.Min(x + len, WallLine);
                    float h = far ? Range(rng, 1.4f, 2.6f) : Range(rng, 0.25f, 0.55f);
                    if (far && Mathf.Abs((x + xEnd) * 0.5f) < 1.2f) h = 0.3f; // broken gap at the centre
                    var centre = new Vector3((x + xEnd) * 0.5f, h * 0.5f, end * EndLine);
                    var size = new Vector3(xEnd - x - 0.04f, h, 0.8f);
                    walls.At(centre.z).Box(centre, size, Quaternion.identity, uv, 0);
                    x = xEnd;
                }
            }

            // One collider per side: the playable area is a clean box.
            colliders.Add((new Vector3(-WallLine, 1.5f, 0f), new Vector3(0.8f + 2f * (WallLine - HalfWidth - 0.4f), 3f, EndLine * 2f + 1f), Quaternion.identity));
            colliders.Add((new Vector3(WallLine, 1.5f, 0f), new Vector3(0.8f + 2f * (WallLine - HalfWidth - 0.4f), 3f, EndLine * 2f + 1f), Quaternion.identity));
            colliders.Add((new Vector3(0f, 1.5f, -EndLine), new Vector3(WallLine * 2f + 1f, 3f, 0.8f + 2f * (EndLine - HalfLength - 0.4f)), Quaternion.identity));
            colliders.Add((new Vector3(0f, 1.5f, EndLine), new Vector3(WallLine * 2f + 1f, 3f, 0.8f + 2f * (EndLine - HalfLength - 0.4f)), Quaternion.identity));
        }

        private static void AddCapStones(Chunked walls, Vector3 centre, Vector3 size, Quaternion rot, System.Random rng)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(size.z / 0.9f));
            for (int i = 0; i < count; i++)
            {
                if (rng.NextDouble() < 0.35) continue;
                float t = (i + 0.5f) / count - 0.5f;
                var pos = centre + rot * new Vector3(Range(rng, -0.05f, 0.05f), size.y * 0.5f + 0.1f, t * size.z);
                walls.At(pos.z).Box(pos, new Vector3(0.7f, 0.2f, Range(rng, 0.5f, 0.85f)),
                    rot * Quaternion.Euler(Range(rng, -4f, 4f), Range(rng, -6f, 6f), Range(rng, -4f, 4f)), StoneUv(2f), 0);
            }
        }

        private static void BuildPillars(Chunked walls, List<(Vector3, Vector3, Quaternion)> colliders, System.Random rng)
        {
            var uv = StoneUv(2f);
            var spots = new List<(Vector3 pos, float h)>
            {
                (new Vector3(-WallLine, 0f, -EndLine), 1.2f), (new Vector3(WallLine, 0f, -EndLine), 0.9f),
                (new Vector3(-WallLine, 0f, EndLine), 4.2f), (new Vector3(WallLine, 0f, EndLine), 3.6f),
                (new Vector3(-1.6f, 0f, EndLine), 4.6f), (new Vector3(1.6f, 0f, EndLine), 4.4f),
            };
            foreach (float z in new[] { -7f, 0f, 7f })
            {
                spots.Add((new Vector3(-WallLine, 0f, z), Range(rng, 1.8f, 3.2f)));
                spots.Add((new Vector3(WallLine, 0f, z), Range(rng, 1.6f, 3f)));
            }

            foreach (var (pos, h) in spots)
            {
                var rot = Quaternion.Euler(0f, Range(rng, -6f, 6f), 0f);
                walls.At(pos.z).Box(pos + new Vector3(0f, 0.12f, 0f), new Vector3(1.25f, 0.24f, 1.25f), rot, uv, 0);      // plinth
                walls.At(pos.z).Box(pos + new Vector3(0f, h * 0.5f, 0f), new Vector3(0.95f, h, 0.95f), rot, uv, 0);       // shaft
                if (h > 2.5f)
                    walls.At(pos.z).Box(pos + new Vector3(0f, h + 0.14f, 0f), new Vector3(1.2f, 0.28f, 1.2f), rot, uv, 0); // capital
                else
                    walls.At(pos.z).Box(pos + new Vector3(Range(rng, -0.1f, 0.1f), h + 0.12f, 0f), new Vector3(0.8f, 0.3f, 0.7f),
                        rot * Quaternion.Euler(Range(rng, -12f, 12f), 0f, Range(rng, -15f, 15f)), uv, 0);                  // broken top
            }

            // Lintel across the far gate pillars (broken arch over the north gap).
            walls.At(EndLine).Box(new Vector3(0f, 4.75f, EndLine), new Vector3(4.2f, 0.45f, 0.9f), Quaternion.Euler(0f, 0f, -3f), uv, 0);
        }

        private static void BuildRubble(Chunked walls, System.Random rng)
        {
            var uv = StoneUv(1.5f);
            for (int i = 0; i < 26; i++)
            {
                // Along the walls, inside the play edge, so they never block the middle.
                bool sideWall = rng.NextDouble() < 0.75;
                Vector3 pos = sideWall
                    ? new Vector3((rng.NextDouble() < 0.5 ? -1f : 1f) * Range(rng, 4.6f, 5.1f), 0f, Range(rng, -13f, 13f))
                    : new Vector3(Range(rng, -5f, 5f), 0f, (rng.NextDouble() < 0.5 ? -1f : 1f) * Range(rng, 13.0f, 13.6f));
                float s = Range(rng, 0.15f, 0.45f);
                pos.y = s * 0.3f;
                walls.At(pos.z).Box(pos, new Vector3(s * Range(rng, 1f, 1.8f), s, s * Range(rng, 0.8f, 1.4f)),
                    Quaternion.Euler(Range(rng, -15f, 15f), Range(rng, 0f, 360f), Range(rng, -15f, 15f)), uv, 0);
            }
        }

        private static List<Vector3> BuildBraziers(Chunked props)
        {
            var spots = new List<Vector3>();
            foreach (float z in new[] { -9.5f, 0f, 9.5f })
            foreach (float side in new[] { -1f, 1f })
                spots.Add(new Vector3(side * 4.4f, 0f, z));

            foreach (var p in spots)
            {
                var mb = props.At(p.z);
                mb.Lathe(p, Quaternion.identity, new[] { new Vector2(0f, 0.32f), new Vector2(0.12f, 0.3f), new Vector2(0.18f, 0.16f), new Vector2(0.75f, 0.12f), new Vector2(0.82f, 0.18f) },
                    PaletteAtlas.For(Swatch.Stone), 0, 10, capBottom: false, capTop: true);
                mb.Lathe(p + Vector3.up * 0.8f, Quaternion.identity, new[] { new Vector2(0f, 0.16f), new Vector2(0.12f, 0.34f), new Vector2(0.22f, 0.42f), new Vector2(0.26f, 0.44f) },
                    PaletteAtlas.For(Swatch.Gunmetal), 0, 12);
                mb.Lathe(p + Vector3.up * 0.98f, Quaternion.identity, new[] { new Vector2(0f, 0.38f), new Vector2(0.04f, 0.3f), new Vector2(0.08f, 0.12f) },
                    PaletteAtlas.For(Swatch.Coal), 0, 10, capTop: true);
            }
            return spots;
        }

        // ------------------------------------------------------------ Scene objects

        private static void Emit(Transform root, string name, Chunked chunked, Material mat)
        {
            for (int i = 0; i < chunked.Parts.Length; i++)
            {
                var mb = chunked.Parts[i];
                if (mb.VertexCount == 0) continue;
                var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/Arena_{name}_{i}.asset");
                mb.WriteTo(mesh);
                EditorUtility.SetDirty(mesh);
                var go = new GameObject($"{name}_{i}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        private static void BuildOuterGround(Transform root, GameArtKit kit)
        {
            var go = new GameObject("OuterGround");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, -0.12f, 0f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(60f, 80f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            SetupRenderer(go.AddComponent<MeshRenderer>(), kit.OuterGround);
            // Quad UVs are 0..1; scale the material tiling instead of the mesh.
            kit.OuterGround.SetTextureScale("_BaseMap", new Vector2(20f, 26f));
            EditorUtility.SetDirty(kit.OuterGround);
        }

        private static void BuildRuneDecal(Transform root, GameArtKit kit)
        {
            var go = new GameObject("RuneCircle");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.01f, 1.5f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(6.5f, 6.5f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            SetupRenderer(go.AddComponent<MeshRenderer>(), kit.RuneDecal);
        }

        private static void BuildFire(Transform root, Vector3 brazier, GameArtKit kit)
        {
            var go = new GameObject("Brazier Fire");
            go.transform.SetParent(root, false);
            go.transform.position = brazier + Vector3.up * 1.05f;

            var flames = Particles("Flames", go.transform, kit.Flame);
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.22f), new Color(1f, 0.38f, 0.1f));
            main.maxParticles = 30;
            var emission = flames.emission;
            emission.rateOverTime = 26f;
            var shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.18f;
            var col = flames.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(new[] { (new Color(1f, 0.9f, 0.6f), 0f), (new Color(1f, 0.5f, 0.15f), 0.4f), (new Color(0.6f, 0.12f, 0.04f), 1f) },
                new[] { (0f, 0f), (0.85f, 0.12f), (0.5f, 0.5f), (0f, 1f) });
            var size = flames.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var embers = Particles("Embers", go.transform, kit.Ember);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.35f), new Color(1f, 0.45f, 0.12f));
            main.maxParticles = 16;
            emission = embers.emission;
            emission.rateOverTime = 7f;
            shape = embers.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.2f;
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.6f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            col = embers.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.1f), (0.8f, 0.7f), (0f, 1f) });

            var lightGo = new GameObject("Torch Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = TorchColor;
            light.shadows = LightShadows.None;
            lightGo.AddComponent<LightFlicker>().Configure(intensity: 9f, intensityAmp: 1.2f, range: 10f, rangeAmp: 0.6f);
            light.intensity = 9f;
            light.range = 10f;
        }

        private static void BuildMist(Transform root, GameArtKit kit)
        {
            var ps = Particles("Ground Mist", root, kit.Mist);
            ps.transform.position = new Vector3(0f, 0.4f, 0f);
            ps.transform.rotation = Quaternion.identity;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.66f, 0.74f, 0.12f), new Color(0.7f, 0.68f, 0.7f, 0.2f));
            main.maxParticles = 28;
            var emission = ps.emission;
            emission.rateOverTime = 2.2f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(HalfWidth * 2.4f, 0.4f, HalfLength * 2.2f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.3f), (1f, 0.7f), (0f, 1f) });
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            // Lie flat: camera-facing billboards would cut into the floor with a hard edge.
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        private static void BuildColliders(Transform root, List<(Vector3 centre, Vector3 size, Quaternion rot)> colliders)
        {
            var parent = new GameObject("Colliders").transform;
            parent.SetParent(root, false);
            for (int i = 0; i < colliders.Count; i++)
            {
                var go = new GameObject($"Block_{i}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(colliders[i].centre, colliders[i].rot);
                go.AddComponent<BoxCollider>().size = colliders[i].size;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
            // Floor collider so the CharacterController stands on y = 0.
            var floor = new GameObject("Floor Collider");
            floor.transform.SetParent(parent, false);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(WallLine * 2f + 2f, 1f, EndLine * 2f + 2f);
        }

        // ------------------------------------------------------------ Helpers

        private static void SetupRenderer(Renderer r, Material mat)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static ParticleSystem Particles(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along +Z; point it up
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.loop = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static Gradient Gradient((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(colors.Select(k => new GradientColorKey(k.c, k.t)).ToArray(), alphas.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
            return g;
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
