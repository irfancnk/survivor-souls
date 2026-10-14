using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Boss
{
    /// <summary>
    /// Round boss arena of the Game scene (KAN-59 = KAN-22.2): the inactive "Boss Arena" object with <see cref="BossArena"/>.
    /// It sits on the shore plain at <see cref="Centre"/>, south of the streamed tiles and far from the sea, so the snow
    /// ground quad, the ground collider, weather and footprints of the world serve it as well.
    /// <list type="bullet">
    /// <item>Flagstone disc (radius <see cref="Radius"/> + 0.8 m), a rune circle in the middle.</item>
    /// <item>Ring wall: low in the south (the Knight stays visible in front of it), high in the north; one box collider per
    /// segment; a stone arch with the exit portal (<see cref="PortalBuilder"/>) in the north gap.</item>
    /// <item>5 low broken pillars as partial cover, 4 braziers on the diagonals (one flickering point light each, plus the two
    /// arch sconces): with the hero light no mesh part gets more than 4 lights.</item>
    /// <item>Outside: dead pines, boulders and cliffs (reads as "a clearing in a dark wood"), dark mist and embers near the wall.</item>
    /// </list>
    /// Geometry is split in 4 quadrants (N-E, E-S, S-W, W-N), one mesh per material each, so each quadrant meets only its own
    /// brazier and sconce (URP 4 lights per object, forward). Placeholder masonry from <see cref="PropKit"/> until the KAN-17
    /// wall art lands. Meshes are assets under Art/World/BossArena_*; the object is rebuilt with the scene.
    /// </summary>
    public static class BossArenaBuilder
    {
        public static readonly Vector3 Centre = new Vector3(0f, 0f, -30f);
        public const float Radius = 16f;               // walkable: inner face of the ring wall
        private const float WallThickness = 0.9f;
        private const int WallSegments = 40;
        private const float GateHalfAngle = 9f;          // ring segments with their centre inside this angle of north are left out
        private const float GateZ = Radius + 0.6f;       // arch base (door plane) on the north axis
        private const float BrazierRadius = Radius - 1.7f;
        private const string MeshDir = "Assets/_Project/Art/World";
        private static readonly Vector3 PlayerStartLocal = new Vector3(0f, 0f, -10f);
        private static readonly Vector3 Wind = new Vector3(0.8f, 0f, -0.6f).normalized;

        // Low broken pillars: (angle from north clockwise, distance from centre, height).
        private static readonly (float angle, float dist, float height)[] PillarSpots =
        {
            (35f, 8.5f, 1.9f), (100f, 9f, 1.4f), (160f, 8.5f, 2.1f), (250f, 9f, 1.6f), (318f, 8.5f, 2.2f),
        };

        private sealed class Quadrant
        {
            public readonly MeshBuilder Floor = new MeshBuilder(), Stone = new MeshBuilder(), Props = new MeshBuilder();
        }

        /// <summary>Builds the arena in the open scene, wired to the world, spawner, Knight, camera and arrow. Inactive until a boss Chamber.</summary>
        public static BossArena Create(GameArtKit kit, RealmArt art, AudioClip portalHum, WorldStreamer world, EnemySpawner spawner,
            Transform player, TopDownFollowCamera camera, ObjectiveArrow arrow, Light sun)
        {
            var root = new GameObject("Boss Arena");
            root.transform.SetPositionAndRotation(Centre, Quaternion.identity);
            var rng = new System.Random(5959);

            var quads = new Quadrant[4];
            for (int i = 0; i < 4; i++)
            {
                quads[i] = new Quadrant();
                quads[i].Floor.Tint = new Color(1.3f, 1.27f, 1.24f, 0f); // pale swept flagstones, no snow patches: the dark Knight must read (KAN-25 rule)
                quads[i].Stone.Tint = new Color(0.8f, 0.82f, 0.88f, 0.55f);
            }

            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(root.transform, false);
            var obstacles = new List<BossArena.Pillar>();

            BuildFloor(quads);
            BuildWall(quads, colliders, rng);
            BuildPillars(quads, colliders, obstacles, rng);
            var fires = BuildBraziers(quads, colliders, obstacles);
            BuildOutside(quads, rng);

            // North arch with the exit portal. The arch faces -Z, towards the arena and the camera.
            var gate = new Vector3(0f, 0f, GateZ);
            var gateStone = Q(quads, gate).Stone;
            var gateProps = Q(quads, gate).Props;
            foreach (var flame in PropKit.DoorGate(gateStone, gateProps, gate, rng, door: false)) fires.Add((flame, 0.6f, 4f, 6f));
            foreach (float s in new[] { -1f, 1f })
                Box(colliders, gate + new Vector3(s * 2.54f, 1.5f, 0.05f), new Vector3(2.92f, 3f, 1.3f), 0f);
            Box(colliders, gate + new Vector3(0f, 1.5f, 1.3f), new Vector3(3.2f, 3f, 0.6f), 0f); // backstop behind the open portal
            var portal = PortalBuilder.Build(root.transform, gate + new Vector3(0f, 0f, -0.1f), kit, art.Glow, portalHum);

            for (int q = 0; q < 4; q++)
            {
                Part(root.transform, q, "Floor", quads[q].Floor, art.SnowFloor);
                Part(root.transform, q, "Stone", quads[q].Stone, art.SnowStone);
                Part(root.transform, q, "Props", quads[q].Props, art.SnowProps);
            }
            foreach (var (pos, scale, intensity, range) in fires) PropKit.Fire(root.transform, pos, kit, scale, intensity, range);
            RuneCircle(root.transform, kit);
            WallMist(root.transform, kit);

            var arena = root.AddComponent<BossArena>();
            var so = new SerializedObject(arena);
            so.FindProperty("radius").floatValue = Radius;
            var pillars = so.FindProperty("pillars");
            pillars.arraySize = obstacles.Count;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var el = pillars.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("centre").vector2Value = obstacles[i].centre;
                el.FindPropertyRelative("radius").floatValue = obstacles[i].radius;
            }
            so.FindProperty("playerStart").vector3Value = PlayerStartLocal;
            so.FindProperty("portal").objectReferenceValue = portal;
            so.FindProperty("world").objectReferenceValue = world;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("followCamera").objectReferenceValue = camera;
            so.FindProperty("arrow").objectReferenceValue = arrow;
            so.FindProperty("sun").objectReferenceValue = sun;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(false); // ChamberSetup turns it on in boss Chambers
            return arena;
        }

        // ------------------------------------------------------------ Parts

        private static void BuildFloor(Quadrant[] quads)
        {
            var uv = PropKit.StoneUv(4f);
            const int perQuadrant = 10;
            float r = Radius + 0.8f;
            for (int q = 0; q < 4; q++)
            for (int i = 0; i < perQuadrant; i++)
            {
                float a0 = (q * 90f + i * 90f / perQuadrant) * Mathf.Deg2Rad, a1 = (q * 90f + (i + 1) * 90f / perQuadrant) * Mathf.Deg2Rad;
                var c = Vector3.up * 0.02f;
                var p0 = c + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * r;
                var p1 = c + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * r;
                quads[q].Floor.Quad(c, p0, p1, p1, uv, 0); // fan triangle (degenerate 4th vertex)
            }
        }

        /// <summary>Ring wall: segments of cut stone, low in the south (1.1 m) rising to 3.2 m in the north, ragged caps.</summary>
        private static void BuildWall(Quadrant[] quads, Transform colliders, System.Random rng)
        {
            var uv = PropKit.StoneUv(2f);
            float mid = Radius + WallThickness * 0.5f;
            float step = 360f / WallSegments;
            float segLength = 2f * Mathf.PI * mid / WallSegments + 0.12f;
            for (int i = 0; i < WallSegments; i++)
            {
                float angle = (i + 0.5f) * step;
                float fromNorth = Mathf.DeltaAngle(0f, angle);
                if (Mathf.Abs(fromNorth) < GateHalfAngle) continue; // the arch fills the gap
                var dir = Dir(angle);
                var centre = dir * mid;
                var rot = Quaternion.Euler(0f, angle, 0f); // local z = outward, local x = along the ring
                float north = (Mathf.Cos(angle * Mathf.Deg2Rad) + 1f) * 0.5f;
                float h = Mathf.Lerp(1.1f, 3.2f, north * north) + PropKit.Range(rng, -0.2f, 0.25f);
                var stone = Q(quads, centre).Stone;
                stone.Box(centre + Vector3.up * (h * 0.5f), new Vector3(segLength, h, WallThickness), rot, uv, 0);
                if (rng.NextDouble() < 0.75)
                    stone.Box(centre + Vector3.up * (h + 0.1f), new Vector3(segLength * PropKit.Range(rng, 0.45f, 0.85f), 0.2f, WallThickness + 0.15f),
                        rot * Quaternion.Euler(PropKit.Range(rng, -3f, 3f), PropKit.Range(rng, -5f, 5f), PropKit.Range(rng, -3f, 3f)), uv, 0);
                if (i % 4 == 0) // buttress on the outside, breaks the long ring
                    stone.Box(dir * (mid + 0.7f) + Vector3.up * (h * 0.4f), new Vector3(0.7f, h * 0.8f, 0.9f), rot, uv, 0);
                Box(colliders, centre + Vector3.up * 1.5f, new Vector3(segLength, 3f, WallThickness + 0.2f), angle);
            }
        }

        private static void BuildPillars(Quadrant[] quads, Transform colliders, List<BossArena.Pillar> obstacles, System.Random rng)
        {
            foreach (var (angle, dist, height) in PillarSpots)
            {
                var p = Dir(angle) * dist;
                PropKit.Pillar(Q(quads, p).Stone, p, rng, height);
                if (rng.NextDouble() < 0.6) // fallen drum beside it
                    PropKit.Rock(Q(quads, p).Stone, p + Dir(angle + 70f) * 1.2f, rng, 0.35f);
                Box(colliders, p + Vector3.up * 1.5f, new Vector3(1.1f, 3f, 1.1f), 0f);
                obstacles.Add(new BossArena.Pillar { centre = new Vector2(p.x, p.z), radius = 0.8f });
            }
        }

        /// <summary>Four braziers on the diagonals. Returns the fires (with the arch sconces added later).</summary>
        private static List<(Vector3 pos, float scale, float intensity, float range)> BuildBraziers(Quadrant[] quads, Transform colliders,
            List<BossArena.Pillar> obstacles)
        {
            var fires = new List<(Vector3, float, float, float)>();
            foreach (float angle in new[] { 45f, 135f, 225f, 315f })
            {
                var p = Dir(angle) * BrazierRadius;
                PropKit.Brazier(Q(quads, p).Props, p);
                fires.Add((p + Vector3.up * 1.05f, 1f, 3.2f, 7.5f)); // range stops short of the next quadrant
                Box(colliders, p + Vector3.up * 0.6f, new Vector3(0.9f, 1.2f, 0.9f), 0f);
                obstacles.Add(new BossArena.Pillar { centre = new Vector2(p.x, p.z), radius = 0.6f });
            }
            return fires;
        }

        /// <summary>Dark wood around the ring: dead pines, boulders, and cliffs behind the arch.</summary>
        private static void BuildOutside(Quadrant[] quads, System.Random rng)
        {
            for (int i = 0; i < 30; i++)
            {
                float angle = i * 12f + PropKit.Range(rng, -4f, 4f);
                if (Mathf.Abs(Mathf.DeltaAngle(0f, angle)) < 20f) continue; // cliffs there
                var p = Dir(angle) * PropKit.Range(rng, Radius + 3.5f, Radius + 9f);
                ShoreKit.Pine(Q(quads, p).Props, p, rng, PropKit.Range(rng, 0.9f, 1.3f), rng.NextDouble() < 0.7, Wind);
                if (rng.NextDouble() < 0.5)
                {
                    var b = Dir(angle + 6f) * PropKit.Range(rng, Radius + 2.2f, Radius + 5f);
                    ShoreKit.Boulder(Q(quads, b).Props, b, rng, PropKit.Range(rng, 0.6f, 1.2f));
                }
            }
            foreach (float s in new[] { -1f, 1f })
                ShoreKit.Cliff(Q(quads, new Vector3(s, 0f, 1f)).Props, new Vector3(s * 6.5f, 0f, Radius + 4.2f), s * -10f, 8f, 3.6f, rng);
        }

        private static void RuneCircle(Transform root, GameArtKit kit)
        {
            var go = new GameObject("Rune Circle");
            go.transform.SetParent(root, false);
            go.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.04f, 0f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(8f, 8f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), kit.RuneDecal);
        }

        /// <summary>Dark low mist and slow embers along the inside of the wall (two particle systems for the whole ring).</summary>
        private static void WallMist(Transform root, GameArtKit kit)
        {
            var holder = new GameObject("Wall Mist").transform;
            holder.SetParent(root, false);

            var mist = PropKit.Particles("Mist", holder, kit.Mist);
            mist.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // circle shape lies flat
            var main = mist.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(-0.25f, -0.1f); // drifts slowly inward
            main.startSize = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.09f, 0.12f, 0.22f), new Color(0.16f, 0.13f, 0.16f, 0.32f));
            main.maxParticles = 40;
            var emission = mist.emission;
            emission.rateOverTime = 5f;
            var shape = mist.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Radius - 0.3f;
            shape.radiusThickness = 0f; // edge only
            var col = mist.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.3f), (1f, 0.7f), (0f, 1f) });
            mist.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            mist.transform.localPosition = new Vector3(0f, 0.25f, 0f);

            var embers = PropKit.Particles("Embers", holder, kit.Ember);
            embers.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.3f, 0.08f));
            main.maxParticles = 36;
            emission = embers.emission;
            emission.rateOverTime = 9f;
            shape = embers.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Radius - 0.5f;
            shape.radiusThickness = 0.1f;
            var vel = embers.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            vel.y = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.5f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            col = embers.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.15f), (0.7f, 0.7f), (0f, 1f) });
            embers.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        }

        // ------------------------------------------------------------ Helpers

        /// <summary>Quadrant of a local point: 0 = N..E, 1 = E..S, 2 = S..W, 3 = W..N (angle from north, clockwise).</summary>
        public static int QuadrantOf(Vector3 local)
        {
            float a = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            return Mathf.Clamp((int)(a / 90f), 0, 3);
        }

        private static Quadrant Q(Quadrant[] quads, Vector3 local) => quads[QuadrantOf(local)];

        private static Vector3 Dir(float angleFromNorth)
        {
            float a = angleFromNorth * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        private static void Box(Transform parent, Vector3 centre, Vector3 size, float yaw)
        {
            var go = new GameObject("Block");
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<BoxCollider>().size = size;
        }

        private static void Part(Transform root, int quadrant, string part, MeshBuilder mb, Material mat)
        {
            if (mb.VertexCount == 0) return;
            var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/BossArena_Q{quadrant}_{part}.asset");
            mb.WriteTo(mesh);
            EditorUtility.SetDirty(mesh);
            var go = new GameObject($"Q{quadrant} {part}");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PropKit.SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
        }
    }
}
