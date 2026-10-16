using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Chests
{
    /// <summary>
    /// Reliquary chest drops of the Game scene (KAN-72): the drop rules asset (<see cref="ChestDropConfig"/>, made once,
    /// never overwritten), the light chest prefab (procedural wooden chest on the palette material + gold light beam +
    /// ground glow; the lid is its own part hinged at the back so it can swing open, KAN-73) and the "Chests" scene object
    /// with <see cref="ChestDropper"/> (drops, walk-in pickup) and <see cref="ChestRewards"/> (upgrade queue, fallback).
    /// Placeholder look until KAN-23 brings the real chest (it replaces the body, not the dropper). Called by GameSceneBuilder.
    /// Debug menu (Play mode): "Survivor Souls/Debug/Chest: Drop Now", "Chest: Print Drop Statistics".
    /// </summary>
    public static class ChestBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Chests";
        public const string ConfigPath = DataDir + "/ChestDropConfig.asset";
        private const string ArtDir = "Assets/_Project/Art/Chests";
        private const string BeamTexturePath = ArtDir + "/T_ChestBeam.png";
        private const string PrefabDir = "Assets/_Project/Prefabs/Chests";
        public const string PrefabPath = PrefabDir + "/Chest_Reliquary.prefab";
        private const float BeamHeight = 4.2f;
        private const float BeamWidth = 1.2f;
        /// <summary>Lid hinge in body space: top of the box, back edge (+Z; the lock plate faces -Z).</summary>
        private static readonly Vector3 Hinge = new Vector3(0f, 0.34f, 0.21f);

        public static ChestDropper Create(GameArtKit kit, PlayerStats stats, PlayerHealth player, SkillManager skills,
            LevelUpFlow flow, SoulWispField souls)
        {
            var config = LoadOrCreateConfig();
            var prefab = BuildPrefab(kit);
            var go = new GameObject("Chests");
            var dropper = go.AddComponent<ChestDropper>();
            var so = new SerializedObject(dropper);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("stats").objectReferenceValue = stats;
            so.FindProperty("chestPrefab").objectReferenceValue = prefab;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("skills").objectReferenceValue = skills;
            so.ApplyModifiedPropertiesWithoutUndo();

            var rewards = go.AddComponent<ChestRewards>();
            so = new SerializedObject(rewards);
            so.FindProperty("dropper").objectReferenceValue = dropper;
            so.FindProperty("flow").objectReferenceValue = flow;
            so.FindProperty("skills").objectReferenceValue = skills;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("souls").objectReferenceValue = souls;
            so.ApplyModifiedPropertiesWithoutUndo();
            return dropper;
        }

        private static ChestDropConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ChestDropConfig>(ConfigPath);
            if (config != null) return config;
            EditorAssetUtil.EnsureFolder(DataDir);
            config = ChestDropConfig.Create(pity: 60f); // pity 60 s (KAN-113: 2 minute Chambers), 2 on the ground, 40 s life, 5 s warning
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        public static ReliquaryChest BuildPrefab(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(PrefabDir);
            var beamTex = TextureFactory.Write(BeamTexturePath, 64, 128, BeamPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: 128);
            // Alpha blend, not additive: additive gold vanishes on white snow (same choice as the boss telegraph).
            var beamMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_ChestBeam.mat", beamTex, additive: false);
            var glowMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_ChestGlow.mat", kit.SoftDot, additive: false);
            var bodyMesh = EditorAssetUtil.LoadOrCreateMesh(ArtDir + "/Mesh_ChestBody.asset");
            ChestMesh().WriteTo(bodyMesh);
            var lidMesh = EditorAssetUtil.LoadOrCreateMesh(ArtDir + "/Mesh_ChestLid.asset");
            LidMesh().WriteTo(lidMesh);
            var beamMesh = EditorAssetUtil.LoadOrCreateMesh(ArtDir + "/Mesh_ChestBeam.asset");
            WriteBeam(beamMesh);

            var root = new GameObject("Chest_Reliquary");
            try
            {
                var body = Part("Body", root.transform, bodyMesh, kit.Palette);
                var lid = Part("Lid", body.transform, lidMesh, kit.Palette);
                lid.transform.localPosition = Hinge;
                var beam = Part("Beam", root.transform, beamMesh, beamMat);
                var glow = Part("Glow", root.transform, kit.Quad, glowMat);
                glow.transform.localPosition = Vector3.up * 0.03f;
                glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                glow.transform.localScale = Vector3.one * 2.6f;
                var chest = root.AddComponent<ReliquaryChest>();
                chest.Setup(body.transform, beam, glow, lid.transform);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return prefab.GetComponent<ReliquaryChest>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static MeshRenderer Part(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>
        /// Small wooden reliquary, about 0.64 x 0.56 x 0.44 m: box body, iron straps and rims, a bright lock plate at the
        /// front (-Z), a glowing gold floor inside (seen when the lid opens). The round lid is <see cref="LidMesh"/>.
        /// Palette atlas: batches with the props.
        /// </summary>
        private static MeshBuilder ChestMesh()
        {
            var mb = new MeshBuilder();
            var wood = PropKit.Pal(Swatch.Wood);
            var iron = PropKit.Pal(Swatch.Gunmetal);
            var bright = PropKit.Pal(Swatch.BrightMetal);
            var id = Quaternion.identity;
            mb.Box(new Vector3(0f, 0.18f, 0f), new Vector3(0.62f, 0.32f, 0.42f), id, wood, 0);
            mb.Box(new Vector3(0f, 0.035f, 0f), new Vector3(0.66f, 0.07f, 0.46f), id, iron, 0); // foot rim
            mb.Box(new Vector3(0f, 0.335f, 0f), new Vector3(0.66f, 0.04f, 0.46f), id, iron, 0); // lid seam
            mb.Box(new Vector3(0f, 0.36f, 0f), new Vector3(0.54f, 0.01f, 0.34f), id, bright, 0); // treasure (shows when open)
            foreach (float x in new[] { -0.2f, 0.2f })
                mb.Box(new Vector3(x, 0.18f, 0f), new Vector3(0.06f, 0.33f, 0.44f), id, iron, 0);
            mb.Box(new Vector3(0f, 0.28f, -0.225f), new Vector3(0.12f, 0.14f, 0.03f), id, bright, 0); // lock plate
            return mb;
        }

        /// <summary>Round lid (half-buried cylinder) with two iron straps, in hinge space (pivot at <see cref="Hinge"/>).</summary>
        private static MeshBuilder LidMesh()
        {
            var mb = new MeshBuilder();
            var wood = PropKit.Pal(Swatch.Wood);
            var iron = PropKit.Pal(Swatch.Gunmetal);
            var c = new Vector3(0f, 0.34f, 0f) - Hinge; // cylinder axis relative to the hinge
            mb.Tube(new[] { c + new Vector3(-0.31f, 0f, 0f), c + new Vector3(0.31f, 0f, 0f) }, _ => 0.21f, wood, 0, sides: 10);
            foreach (float x in new[] { -0.2f, 0.2f })
                mb.Tube(new[] { c + new Vector3(x - 0.03f, 0f, 0f), c + new Vector3(x + 0.03f, 0f, 0f) }, _ => 0.225f, iron, 0, sides: 10);
            return mb;
        }

        /// <summary>Two crossed vertical quads (X seen from above): reads as a light column from any camera yaw.</summary>
        private static void WriteBeam(Mesh mesh)
        {
            float h = BeamWidth * 0.5f;
            var v = new[]
            {
                new Vector3(-h, 0f, 0f), new Vector3(h, 0f, 0f), new Vector3(h, BeamHeight, 0f), new Vector3(-h, BeamHeight, 0f),
                new Vector3(0f, 0f, -h), new Vector3(0f, 0f, h), new Vector3(0f, BeamHeight, h), new Vector3(0f, BeamHeight, -h),
            };
            var uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            // Both faces of each quad (particle material culls back faces).
            var tris = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 4, 5, 6, 4, 6, 7 };
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
        }

        /// <summary>Light column: bright core, soft sides, quick fade in at the ground, long fade to the top.</summary>
        private static Color BeamPixel(int x, int y)
        {
            float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 128f;
            float across = Mathf.Exp(-u * u * 3f) * (1f - TextureFactory.SmoothStep(0.85f, 1f, Mathf.Abs(u)));
            float along = TextureFactory.SmoothStep(0f, 0.06f, v) * Mathf.Pow(1f - v, 1.1f);
            float core = Mathf.Exp(-u * u * 40f);
            float g = Mathf.Lerp(0.85f, 1f, core);
            return new Color(g, g, g, Mathf.Clamp01(across * along * 1.5f));
        }

        [MenuItem("Survivor Souls/Debug/Chest: Drop Now")]
        private static void DropNow()
        {
            var dropper = Object.FindAnyObjectByType<ChestDropper>();
            var player = Object.FindAnyObjectByType<SurvivorSouls.Gameplay.Player.PlayerHealth>();
            if (dropper == null || player == null) return;
            var at = player.transform.position + player.transform.forward * 2.5f;
            if (dropper.DropNow(at) == null) Debug.Log("[Chests] Ground full (cap reached).");
        }

        [MenuItem("Survivor Souls/Debug/Chest: Drop Light")]
        private static void DropLight() => DropTier(ChestTier.Light);

        [MenuItem("Survivor Souls/Debug/Chest: Drop Rare")]
        private static void DropRare() => DropTier(ChestTier.Rare);

        /// <summary>A chest of <paramref name="tier"/> 2.5 m ahead of the Knight (KAN-75), counted as a skill chest.</summary>
        public static ReliquaryChest DropTier(ChestTier tier) => SurvivorSouls.Dev.DevCheats.DropChest(tier); // KAN-87: shared with the touch panel

        [MenuItem("Survivor Souls/Debug/Chest: Open Upgrade Screen")]
        private static void OpenUpgradeScreen()
        {
            var flow = Object.FindAnyObjectByType<LevelUpFlow>();
            if (flow == null || !flow.EnqueueUpgrade(new UpgradeOffer(3, 1, false))) Debug.Log("[Chests] Upgrade queue full.");
        }

        [MenuItem("Survivor Souls/Debug/Chest: Print Drop Statistics")]
        private static void PrintStatistics()
        {
            var dropper = Object.FindAnyObjectByType<ChestDropper>();
            if (dropper == null) return;
            // 1000 kills, 10 % elites, a kill every 0.6 s (busy horde): current skill level of the live run.
            Debug.Log($"[Chests] chance {dropper.Chance:P1} / elite {dropper.EliteChance:P1}: " +
                      dropper.Simulate(1000, 0.1f, 0.6f, System.Environment.TickCount));
        }

        [MenuItem("Survivor Souls/Debug/Chest: Print Drop Statistics Per Level")]
        private static void PrintPerLevel() => Debug.Log(DropTable());

        /// <summary>
        /// Drop statistics per Reliquary Hunter level (KAN-75): 1000 kills each, 10 % elites, a kill every 0.6 s, shipped
        /// config and skill asset. Edit or Play mode (no live run needed).
        /// </summary>
        public static string DropTable(int kills = 1000, float eliteShare = 0.1f, float secondsPerKill = 0.6f)
        {
            var config = AssetDatabase.LoadAssetAtPath<ChestDropConfig>(ConfigPath);
            var hunter = AssetDatabase.LoadAssetAtPath<SkillDefinition>(HunterPath);
            if (config == null || hunter == null) return $"[Chests] Missing {ConfigPath} or {HunterPath}";
            var rows = ChestDropper.SimulatePerLevel(config, hunter, kills, eliteShare, secondsPerKill, 75);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Chests] Reliquary Hunter drops per level: {kills} kills, {eliteShare:P0} elite, a kill every " +
                          $"{secondsPerKill:0.##} s, pity {config.PitySeconds:0} s, chests collected at once.");
            for (int i = 0; i < rows.Length; i++) sb.AppendLine($"L{i + 1}: {rows[i]}");
            return sb.ToString();
        }

        private const string HunterPath = "Assets/_Project/Data/Skills/Skill_ReliquaryHunter.asset";

        [MenuItem("Survivor Souls/Debug/Chest: Drop Now", true)]
        [MenuItem("Survivor Souls/Debug/Chest: Drop Light", true)]
        [MenuItem("Survivor Souls/Debug/Chest: Drop Rare", true)]
        [MenuItem("Survivor Souls/Debug/Chest: Open Upgrade Screen", true)]
        [MenuItem("Survivor Souls/Debug/Chest: Print Drop Statistics", true)]
        private static bool InPlayMode() => Application.isPlaying;
    }
}
