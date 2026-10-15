using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Editor.Boss;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Enemies
{
    /// <summary>
    /// KAN-105: ranged zombies. Prefab side (<see cref="ConfigurePrefab"/>, called by <see cref="EnemyRosterBuilder"/> before it
    /// saves a prefab): hold band on <see cref="Enemy"/>, the <see cref="RangedAttack"/> numbers of Docs/ENEMY_ROSTER.md
    /// sections 3.6-3.8 and a bone bow in the Bone Archer's left hand (the Meshy mesh has none). Scene side
    /// (<see cref="CreateScene"/>, called by GameSceneBuilder): the "Enemy Shots" object with the shared shot pool
    /// (<see cref="EnemyProjectiles"/>, one particle system per <see cref="ShotKind"/>), hazard pool (<see cref="GroundHazards"/>,
    /// one flat particle system per <see cref="HazardKind"/>) and warning pool (<see cref="EnemyTelegraphs"/>: boss lanes and
    /// circles). Textures are procedural (Art/EnemyFX).
    /// Debug menus (Play mode): "Survivor Souls/Debug/Ranged: Spawn Bone Archer / Pyre Thrower / Bile Spitter" (near the Knight).
    /// </summary>
    public static class RangedEnemyBuilder
    {
        public const string FxDir = "Assets/_Project/Art/EnemyFX";
        private const string BowMeshPath = FxDir + "/SM_BoneBow.asset";
        private const int ShotCapacity = 48;
        private const int HazardCapacity = 24;
        private const int LaneWarnings = 8;
        private const int CircleWarnings = 16;
        private const int LanesPerWarning = 3;
        public const int FxSize = 128;

        /// <summary>The ranged types (Docs/ENEMY_ROSTER.md 3.6-3.8): hold band (m) and attack. First guesses in Chamber 1 terms.</summary>
        public static readonly (string id, float holdMin, float holdMax, RangedAttack.Settings attack)[] Specs =
        {
            ("BoneArcher", 7f, 9f, new RangedAttack.Settings
            {
                kind = ShotKind.Arrow, lobbed = false,
                windUp = 0.8f, lockShare = 0.35f, cooldown = 2.2f, pattern = new[] { 1, 1, 3 }, spread = 30f,
                speed = 14f, range = 14f, damageShare = 0.1f, hitRadius = 0.45f, laneWidth = 0.3f, needsLineOfFire = true,
            }),
            ("PyreThrower", 6f, 10f, new RangedAttack.Settings
            {
                kind = ShotKind.FirePot, lobbed = true,
                windUp = 0.5f, cooldown = 2.6f, pattern = new[] { 1, 1, 3 }, spread = 2.6f,
                speed = 9f, range = 11f, flightTime = 1.1f, arcHeight = 3.2f, leadTime = 1.6f, maxLead = 3.5f,
                damageShare = 0.12f, hitRadius = 1.6f,
                hazard = new HazardSpec { kind = HazardKind.Fire, radius = 1.6f, life = 3f, dps = 8f },
            }),
            ("BileSpitter", 5f, 8f, new RangedAttack.Settings
            {
                kind = ShotKind.AcidGlob, lobbed = true,
                windUp = 0.7f, cooldown = 3f, pattern = new[] { 1 }, speed = 7f, range = 9f, arcHeight = 1.1f, blockable = true,
                damageShare = 0f, hitRadius = 1.4f, maxHazards = 3,
                hazard = new HazardSpec { kind = HazardKind.Acid, radius = 1.4f, life = 5f, dps = 12f, slow = 0.3f },
            }),
        };

        public static readonly string[] Ids = Specs.Select(s => s.id).ToArray();

        public static bool IsRanged(string id) => Specs.Any(s => s.id == id);

        // ------------------------------------------------------------ Prefab

        /// <summary>Hold band + <see cref="RangedAttack"/> (+ bow) on a roster prefab root before it is saved. No-op for melee types.</summary>
        public static void ConfigurePrefab(GameObject root, Enemy enemy, string id, Material palette)
        {
            int index = System.Array.FindIndex(Specs, s => s.id == id);
            if (index < 0) return;
            var spec = Specs[index];
            var so = new SerializedObject(enemy);
            so.FindProperty("holdMin").floatValue = spec.holdMin;
            so.FindProperty("holdMax").floatValue = spec.holdMax;
            so.ApplyModifiedPropertiesWithoutUndo();
            var attack = root.AddComponent<RangedAttack>();
            attack.Configure(spec.attack);
            if (id == "BoneArcher") AttachBow(root, palette);
        }

        /// <summary>
        /// Bone bow (one mesh, palette material) under the left hand bone. Scaled against the rig's centimetre scale; held
        /// upright with the belly forward in the bind pose, so the Shoot clip raises it at the Knight.
        /// </summary>
        private static void AttachBow(GameObject root, Material palette)
        {
            var hand = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LeftHand")
                       ?? throw new System.InvalidOperationException("BoneArcher: LeftHand bone missing");
            var bow = new GameObject("Bow");
            bow.transform.SetParent(hand, false);
            float s = 1f / Mathf.Max(0.0001f, hand.lossyScale.x);
            bow.transform.localScale = Vector3.one * s;
            bow.transform.rotation = root.transform.rotation;
            bow.AddComponent<MeshFilter>().sharedMesh = BuildBowMesh();
            var r = bow.AddComponent<MeshRenderer>();
            PropKit.SetupRenderer(r, palette);
        }

        /// <summary>Recurve bow 1.1 m: tapered wood-and-bone limbs bulging forward (+Z), a taut string behind. Rebuilt each run.</summary>
        private static Mesh BuildBowMesh()
        {
            var mb = new MeshBuilder();
            const float half = 0.55f;
            var limb = new List<Vector3>();
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f * 2f - 1f;                       // -1 bottom .. 1 top
                float bulge = 0.16f * (1f - t * t) - 0.03f * t * t * t * t; // recurve tips bend back
                limb.Add(new Vector3(0f, t * half, bulge));
            }
            mb.Tube(limb, t => Mathf.Lerp(0.026f, 0.012f, Mathf.Abs(t * 2f - 1f)), PaletteAtlas.For(Swatch.Bleached), 0, sides: 6);
            mb.Tube(new[] { new Vector3(0f, -0.07f, 0.15f), new Vector3(0f, 0.07f, 0.15f) }, _ => 0.032f, PaletteAtlas.For(Swatch.DarkLeather), 0, sides: 6); // grip wrap
            mb.Tube(new[] { limb[0], limb[12] }, _ => 0.005f, PaletteAtlas.For(Swatch.Fletching), 0, sides: 4, caps: false);
            var mesh = EditorAssetUtil.LoadOrCreateMesh(BowMeshPath);
            mb.WriteTo(mesh);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------ Scene

        /// <summary>"Enemy Shots": shot pool, hazard pool and warning pool for the ranged zombies.</summary>
        public static EnemyProjectiles CreateScene(GameArtKit kit, PlayerHealth player)
        {
            var root = new GameObject("Enemy Shots");
            var warnings = CreateWarnings(kit, root.transform);
            var hazards = CreateHazards(root.transform, player);
            var shots = root.AddComponent<EnemyProjectiles>();
            var looks = new[]
            {
                ShotLook(root.transform, "Arrows", "T_EnemyArrow", ArrowPixel, size: 0.2f, new Color(1f, 0.97f, 0.9f), ghosts: 0, spacing: 0f, stretch: true),
                ShotLook(root.transform, "Fire Pots", "T_FirePot", FirePotPixel, size: 0.62f, Color.white, ghosts: 3, spacing: 0.3f, stretch: false),
                ShotLook(root.transform, "Acid Globs", "T_AcidGlob", AcidGlobPixel, size: 0.5f, Color.white, ghosts: 2, spacing: 0.26f, stretch: false),
                ShotLook(root.transform, "Soul Orbs", "T_SoulOrb", ShamanBuilder.SoulOrbPixel, size: 0.6f, Color.white, ghosts: 2, spacing: 0.24f, stretch: false), // KAN-107 Shaman ring
            };
            var so = new SerializedObject(shots);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("hazards").objectReferenceValue = hazards;
            so.FindProperty("telegraphs").objectReferenceValue = warnings;
            so.FindProperty("capacity").intValue = ShotCapacity;
            var list = so.FindProperty("looks");
            list.arraySize = looks.Length;
            for (int i = 0; i < looks.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("sprites").objectReferenceValue = looks[i].sprites;
                e.FindPropertyRelative("size").floatValue = looks[i].size;
                e.FindPropertyRelative("color").colorValue = looks[i].color;
                e.FindPropertyRelative("ghosts").intValue = looks[i].ghosts;
                e.FindPropertyRelative("ghostSpacing").floatValue = looks[i].ghostSpacing;
                e.FindPropertyRelative("stretch").boolValue = looks[i].stretch;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return shots;
        }

        /// <summary>Lane + circle warnings: one of each from the boss builder, the rest copies (textures written once).</summary>
        private static EnemyTelegraphs CreateWarnings(GameArtKit kit, Transform parent)
        {
            var go = new GameObject("Warnings");
            go.transform.SetParent(parent, false);
            var lines = new LineTelegraph[LaneWarnings];
            var circles = new GroundTelegraph[CircleWarnings];
            lines[0] = BossBuilder.CreateLanes(kit, go.transform, LanesPerWarning, "Aim Lane 0");
            for (int i = 1; i < lines.Length; i++)
            {
                lines[i] = Object.Instantiate(lines[0], go.transform);
                lines[i].name = "Aim Lane " + i;
            }
            circles[0] = BossBuilder.CreateTelegraph(kit, go.transform, "Landing Circle 0");
            for (int i = 1; i < circles.Length; i++)
            {
                circles[i] = Object.Instantiate(circles[0], go.transform);
                circles[i].name = "Landing Circle " + i;
            }
            var pool = go.AddComponent<EnemyTelegraphs>();
            var so = new SerializedObject(pool);
            SetArray(so.FindProperty("lines"), lines);
            SetArray(so.FindProperty("circles"), circles);
            so.ApplyModifiedPropertiesWithoutUndo();
            return pool;
        }

        private static GroundHazards CreateHazards(Transform parent, PlayerHealth player)
        {
            var go = new GameObject("Hazards");
            go.transform.SetParent(parent, false);
            var hazards = go.AddComponent<GroundHazards>();
            var looks = new[]
            {
                (sprites: FlatSprites(go.transform, "Fire Patches", "T_FirePatch", FirePatchPixel), color: new Color(1f, 0.72f, 0.45f, 0.95f), scale: 1.18f, flicker: 0.12f),
                (sprites: FlatSprites(go.transform, "Acid Puddles", "T_AcidPuddle", AcidPuddlePixel), color: new Color(0.85f, 1f, 0.7f, 0.9f), scale: 1.12f, flicker: 0.03f),
                (sprites: FlatSprites(go.transform, "Toxic Clouds", "T_ToxicCloud", ToxicCloudPixel), color: new Color(0.9f, 1f, 0.75f, 0.85f), scale: 1.25f, flicker: 0.06f), // KAN-106 Bloater
            };
            var so = new SerializedObject(hazards);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("mover").objectReferenceValue = player.GetComponent<PlayerMover>();
            so.FindProperty("capacity").intValue = HazardCapacity;
            var list = so.FindProperty("looks");
            list.arraySize = looks.Length;
            for (int i = 0; i < looks.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("sprites").objectReferenceValue = looks[i].sprites;
                e.FindPropertyRelative("color").colorValue = looks[i].color;
                e.FindPropertyRelative("sizeScale").floatValue = looks[i].scale;
                e.FindPropertyRelative("flicker").floatValue = looks[i].flicker;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return hazards;
        }

        private static EnemyProjectiles.Look ShotLook(Transform parent, string name, string texture, System.Func<int, int, Color> pixel,
            float size, Color color, int ghosts, float spacing, bool stretch)
        {
            var ps = ScriptSprites(parent, name, texture, pixel, ShotCapacity * (ghosts + 1));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 3.2f;
                r.velocityScale = 0.02f;
                r.cameraVelocityScale = 0f;
            }
            return new EnemyProjectiles.Look { sprites = ps, size = size, color = color, ghosts = ghosts, ghostSpacing = spacing, stretch = stretch };
        }

        private static ParticleSystem FlatSprites(Transform parent, string name, string texture, System.Func<int, int, Color> pixel)
        {
            var ps = ScriptSprites(parent, name, texture, pixel, HazardCapacity);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sortMode = ParticleSystemSortMode.None;
            r.sortingOrder = -1; // under the shots and warnings
            return ps;
        }

        /// <summary>A particle system the script fills with SetParticles (emission off, never dies, alpha blend so it reads on snow).</summary>
        public static ParticleSystem ScriptSprites(Transform parent, string name, string texture, System.Func<int, int, Color> pixel, int maxParticles)
        {
            var tex = TextureFactory.Write($"{FxDir}/{texture}.png", FxSize, FxSize, pixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: FxSize);
            var mat = EditorAssetUtil.ParticleMaterial($"{FxDir}/M_FX_{texture.Substring(2)}.mat", tex, additive: false);
            var ps = PropKit.Particles(name, parent, mat);
            ps.transform.localRotation = Quaternion.identity;
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.maxParticles = maxParticles;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.Distance;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        private static void SetArray(SerializedProperty prop, Object[] items)
        {
            prop.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ------------------------------------------------------------ Textures

        private static float R(int x, int y, out float u, out float v)
        {
            u = (x + 0.5f) / FxSize * 2f - 1f;
            v = (y + 0.5f) / FxSize * 2f - 1f;
            return Mathf.Sqrt(u * u + v * v);
        }

        /// <summary>Arrow streak head (stretched along the flight): bone-white core, grey shaft tone, dark rim for snow.</summary>
        private static Color ArrowPixel(int x, int y)
        {
            float r = R(x, y, out _, out _);
            var c = r < 0.35f ? new Color(1f, 0.98f, 0.92f) : r < 0.62f ? new Color(0.78f, 0.72f, 0.62f) : new Color(0.12f, 0.08f, 0.06f);
            c.a = 1f - TextureFactory.SmoothStep(0.72f, 0.9f, r);
            return c;
        }

        /// <summary>Fire pot: clay-dark shell with a burning wick glow (hot core, ember ring, soot rim).</summary>
        private static Color FirePotPixel(int x, int y)
        {
            float r = R(x, y, out float u, out float v);
            float n = TextureFactory.Fbm((u + 1f) * 0.5f, (v + 1f) * 0.5f, 4, 3, 105);
            var core = new Color(1f, 0.9f, 0.55f);
            var ember = new Color(1f, 0.42f, 0.08f);
            var soot = new Color(0.18f, 0.06f, 0.03f);
            float k = r + (n - 0.5f) * 0.25f;
            Color c = k < 0.25f ? core : k < 0.55f ? Color.Lerp(core, ember, (k - 0.25f) / 0.3f) : Color.Lerp(ember, soot, Mathf.Clamp01((k - 0.55f) / 0.2f));
            c.a = 1f - TextureFactory.SmoothStep(0.74f, 0.92f, r);
            return c;
        }

        /// <summary>Acid glob: sickly green blob, bright highlight top left, dark olive rim.</summary>
        private static Color AcidGlobPixel(int x, int y)
        {
            float r = R(x, y, out float u, out float v);
            float hl = 1f - TextureFactory.SmoothStep(0f, 0.28f, Mathf.Sqrt((u + 0.25f) * (u + 0.25f) + (v - 0.28f) * (v - 0.28f)));
            var body = Color.Lerp(new Color(0.62f, 0.9f, 0.16f), new Color(0.2f, 0.36f, 0.04f), TextureFactory.SmoothStep(0.35f, 0.78f, r));
            var c = Color.Lerp(body, new Color(0.95f, 1f, 0.75f), hl * 0.8f);
            c.a = 1f - TextureFactory.SmoothStep(0.74f, 0.9f, r);
            return c;
        }

        /// <summary>Burning ground patch: charred centre, licking orange flame ring (noisy edge), soft outer glow.</summary>
        private static Color FirePatchPixel(int x, int y)
        {
            float r = R(x, y, out float u, out float v);
            float n = TextureFactory.Fbm((u + 1f) * 0.5f, (v + 1f) * 0.5f, 6, 4, 77);
            float edge = r + (n - 0.5f) * 0.3f;
            var char_ = new Color(0.16f, 0.06f, 0.03f);
            var flame = Color.Lerp(new Color(1f, 0.38f, 0.06f), new Color(1f, 0.85f, 0.4f), n);
            float ring = 1f - TextureFactory.SmoothStep(0.06f, 0.24f, Mathf.Abs(edge - 0.62f));
            var c = Color.Lerp(char_, flame, Mathf.Clamp01(ring + n * 0.35f));
            float a = Mathf.Max(0.78f * (1f - TextureFactory.SmoothStep(0.6f, 0.8f, edge)), ring);
            c.a = Mathf.Clamp01(a) * (1f - TextureFactory.SmoothStep(0.86f, 1f, r));
            return c;
        }

        /// <summary>Acid puddle: green pool with a dark wet rim and lighter bubbles.</summary>
        private static Color AcidPuddlePixel(int x, int y)
        {
            float r = R(x, y, out float u, out float v);
            float n = TextureFactory.Fbm((u + 1f) * 0.5f, (v + 1f) * 0.5f, 5, 3, 31);
            float edge = r + (n - 0.5f) * 0.22f;
            var pool = Color.Lerp(new Color(0.55f, 0.85f, 0.12f), new Color(0.3f, 0.55f, 0.05f), n);
            float bubbles = TextureFactory.SmoothStep(0.72f, 0.8f, TextureFactory.Fbm((u + 1f) * 2f, (v + 1f) * 2f, 8, 2, 9));
            var c = Color.Lerp(pool, new Color(0.88f, 1f, 0.6f), bubbles * 0.7f);
            float rim = 1f - TextureFactory.SmoothStep(0.03f, 0.1f, Mathf.Abs(edge - 0.76f));
            c = Color.Lerp(c, new Color(0.12f, 0.2f, 0.02f), rim * 0.8f);
            c.a = (1f - TextureFactory.SmoothStep(0.76f, 0.86f, edge)) * 0.92f;
            return c;
        }

        /// <summary>Toxic cloud (KAN-106): low murky yellow-green billows, denser clumps, soft dark-olive edge (reads on snow).</summary>
        private static Color ToxicCloudPixel(int x, int y)
        {
            float r = R(x, y, out float u, out float v);
            float n = TextureFactory.Fbm((u + 1f) * 0.5f, (v + 1f) * 0.5f, 4, 4, 211);
            float clumps = TextureFactory.Fbm((u + 1f) * 1.5f, (v + 1f) * 1.5f, 6, 3, 57);
            float edge = r + (n - 0.5f) * 0.35f;
            var murk = Color.Lerp(new Color(0.42f, 0.5f, 0.12f), new Color(0.72f, 0.82f, 0.3f), clumps);
            var c = Color.Lerp(murk, new Color(0.2f, 0.26f, 0.05f), TextureFactory.SmoothStep(0.55f, 0.85f, edge));
            float a = (1f - TextureFactory.SmoothStep(0.62f, 0.92f, edge)) * Mathf.Lerp(0.55f, 0.85f, clumps);
            c.a = Mathf.Clamp01(a) * (1f - TextureFactory.SmoothStep(0.9f, 1f, r));
            return c;
        }

        // ------------------------------------------------------------ Debug

        [MenuItem("Survivor Souls/Debug/Ranged: Spawn Bone Archer")]
        private static void SpawnArcher() => SpawnNearKnight("BoneArcher");

        [MenuItem("Survivor Souls/Debug/Ranged: Spawn Pyre Thrower")]
        private static void SpawnPyre() => SpawnNearKnight("PyreThrower");

        [MenuItem("Survivor Souls/Debug/Ranged: Spawn Bile Spitter")]
        private static void SpawnSpitter() => SpawnNearKnight("BileSpitter");

        [MenuItem("Survivor Souls/Debug/Ranged: Spawn Bone Archer", true)]
        [MenuItem("Survivor Souls/Debug/Ranged: Spawn Pyre Thrower", true)]
        [MenuItem("Survivor Souls/Debug/Ranged: Spawn Bile Spitter", true)]
        private static bool InPlay() => Application.isPlaying && Object.FindAnyObjectByType<EnemySpawner>() != null;

        private static void SpawnNearKnight(string id)
        {
            var spawner = Object.FindAnyObjectByType<EnemySpawner>();
            var prefab = spawner != null ? spawner.FindType("Enemy_" + id) : null;
            var player = EnemyProjectiles.Current != null ? EnemyProjectiles.Current.Player : null;
            if (prefab == null || player == null)
            {
                Debug.LogWarning($"[RangedEnemyBuilder] cannot spawn {id}: no spawner type or Knight");
                return;
            }
            spawner.SpawnAt(prefab, player.transform.position + new Vector3(0f, 0f, 9f));
        }
    }
}
