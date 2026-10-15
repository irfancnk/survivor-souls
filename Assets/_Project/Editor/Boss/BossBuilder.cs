using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Editor.Enemies;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Editor.Boss
{
    /// <summary>
    /// Boss fight of the Game scene (KAN-58): the "Boss" object with <see cref="BossFight"/> and its red ground telegraph
    /// (ring + growing disc, two flat quads, procedural textures), the Realm 1 boss asset (<see cref="BossData"/>, never
    /// overwritten: only an empty body slot is filled) and the boss Chamber rules (<see cref="OpenCondition.KillBoss"/>).
    /// The body prefab is built by <see cref="CombatBuilder"/>. Healing hearts (KAN-60) live on the same object
    /// (<see cref="BossHeartsBuilder"/>). KAN-61: the lane telegraph of the fan and the charge (<see cref="LineTelegraph"/>,
    /// 9 lanes of two flat quads) and the fan bolts (<see cref="BossProjectiles"/>, one script-driven particle system).
    /// Called by GameSceneBuilder.
    /// KAN-62: <see cref="BossReward"/> (guaranteed Rare chest or skill pick after the victory beat) via <see cref="CreateReward"/>.
    /// KAN-108: The Butcher of Ashgrave asset (<see cref="ButcherDataPath"/>, attack list, never overwritten) is the Realm 1
    /// boss of the first boss Chamber; the Warden asset stays as the old reference fight. KAN-112: the boss order is the
    /// <see cref="BossRotation"/> asset (<see cref="RotationPath"/>): Butcher Chamber 5, Rotting Colossus Chamber 10.
    /// Debug menus (Play mode): "Survivor Souls/Debug/Boss: Kill", "Boss: Half Health" (= phase 2), "Boss: Health 25 %",
    /// "Boss: Health 10 %", "Boss: Fan", "Boss: Charge", "Boss: Jump To Boss Chamber" (any Chamber; reloads the scene).
    /// KAN-116: "Boss: Nova", "Boss: Spiral", "Boss: Rain" (a boss without that type gets the sample entry first); bolt pool 64.
    /// KAN-119: the boulder pool of the lobbed attack ("Boss/Boulders": <see cref="BoulderCount"/> lumpy stone bodies, one
    /// shared mesh + the Realm snow-cap props material, a capsule collider each for the rubble); menus "Boss: Lobbed",
    /// "Boss: Summon", "Boss: Teleport" (same sample rule). KAN-121: menus "Boss: Shockwave", "Boss: Homing", "Boss: Beams"
    /// (same sample rule; the rings use the shared circle pool, the orbs the bolt pool, the beams the lane telegraph).
    /// KAN-109: The Pyre Lord asset (<see cref="PyreLordDataPath"/>, never overwritten) sits in the fight's library: not in
    /// Realm 1's rotation, picked by the dev flag <c>-ss-boss-type PyreLord</c> or the menu "Boss: Next Fight Pyre Lord";
    /// a Realm 2 boss once KAN-57 lands. Its slam and meteors leave burning patches (shared ground hazard pool, fire look).
    /// KAN-110: The Rotting Colossus asset (<see cref="ColossusDataPath"/>, never overwritten): Realm 1's second boss since
    /// KAN-112 (rotation); also <c>-ss-boss-type RottingColossus</c> or the menu "Boss: Next Fight Rotting Colossus".
    /// KAN-111: The Bone Matriarch asset (<see cref="MatriarchDataPath"/>, never overwritten) joins the library the same way:
    /// <c>-ss-boss-type BoneMatriarch</c> or the menu "Boss: Next Fight Bone Matriarch". Her body is tuned by
    /// <see cref="ConfigurePrefab"/> (called by <see cref="EnemyRosterBuilder"/>): no contact damage, a 6-9 m hold band and a
    /// <see cref="HoverBob"/> on the model.
    /// </summary>
    public static class BossBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Boss";
        public const string BossDataPath = DataDir + "/Boss_Realm1_Warden.asset";
        public const string ButcherDataPath = DataDir + "/Boss_Realm1_Butcher.asset";
        public const string PyreLordDataPath = DataDir + "/Boss_PyreLord.asset";
        public const string ColossusDataPath = DataDir + "/Boss_RottingColossus.asset";
        public const string MatriarchDataPath = DataDir + "/Boss_BoneMatriarch.asset";
        public const string MatriarchId = "BoneMatriarch";
        /// <summary>KAN-112: Realm 1 boss order (Butcher Chamber 5, Rotting Colossus Chamber 10). Never overwritten.</summary>
        public const string RotationPath = DataDir + "/BossRotation_Realm1.asset";
        /// <summary>KAN-111: the Matriarch keeps this far from the Knight (m): hard to reach, never melee.</summary>
        public const float MatriarchHoldMin = 6f, MatriarchHoldMax = 9f;
        public const string BossRulesPath = "Assets/_Project/Data/Chamber/ChamberRules_Boss.asset";
        private const string ArtDir = "Assets/_Project/Art/Boss";
        private const string RingTexturePath = ArtDir + "/T_TelegraphRing.png";
        private const string DiscTexturePath = ArtDir + "/T_TelegraphDisc.png";
        private const string LaneTexturePath = ArtDir + "/T_TelegraphLane.png";
        private const string LaneFillTexturePath = ArtDir + "/T_TelegraphLaneFill.png";
        private const string BoltTexturePath = ArtDir + "/T_BossBolt.png";
        private const int LaneTextureWidth = 64;
        private const int LaneTextureHeight = 256;
        /// <summary>How far (v) a lane chevron's sides trail behind its tip.</summary>
        public const float LaneChevronSweep = 0.06f;

        /// <summary>Lane fill size in pixels (tests sample <see cref="LaneFillPixel"/> on this grid).</summary>
        public static Vector2Int LaneTextureSize => new Vector2Int(LaneTextureWidth, LaneTextureHeight);

        /// <summary>v of chevron <paramref name="i"/>'s tip (0 = attacker, 1 = far end); bands span v 0.70 .. 0.94.</summary>
        public static float LaneChevronTip(int i) => 0.76f + i * 0.09f;
        private const int BoltTextureSize = 64;
        private const int BoltCapacity = 64; // KAN-116: a 2-arm spiral keeps ~40 bolts in the air
        private const int TextureSize = 256;
        /// <summary>KAN-119: boulders in the pool (a 3-boulder volley plus rubble of the last throws, 8 s each).</summary>
        public const int BoulderCount = 6;
        private const string BoulderMeshPath = ArtDir + "/Mesh_BossBoulder.asset";
        private const string BoulderMaterialPath = "Assets/_Project/Art/Materials/Realm1/M_SnowProps.mat";

        public static BossFight Create(GameArtKit kit, PlayerHealth player, SurvivalTimer timer, EnemySpawner spawner, SoulWispField souls)
        {
            var data = LoadOrCreateData();
            var butcher = LoadOrCreateButcher();
            var pyreLord = LoadOrCreatePyreLord();
            var colossus = LoadOrCreateColossus();
            var matriarch = LoadOrCreateMatriarch();
            var rules = LoadOrCreateRules();
            var root = new GameObject("Boss");
            var telegraph = CreateTelegraph(kit, root.transform);
            var lines = CreateLanes(kit, root.transform);
            var bolts = CreateBolts(root.transform, player);
            var boulders = CreateBoulders(kit, root.transform, player);
            var fight = root.AddComponent<BossFight>();
            var so = new SerializedObject(fight);
            so.FindProperty("data").objectReferenceValue = data;
            so.FindProperty("bossRules").objectReferenceValue = rules;
            so.FindProperty("timer").objectReferenceValue = timer;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("souls").objectReferenceValue = souls;
            so.FindProperty("telegraph").objectReferenceValue = telegraph;
            so.FindProperty("lines").objectReferenceValue = lines;
            so.FindProperty("projectiles").objectReferenceValue = bolts;
            so.FindProperty("boulders").objectReferenceValue = boulders;
            so.FindProperty("rotation").objectReferenceValue = LoadOrCreateRotation(butcher, colossus); // KAN-112: Realm 1 boss Chambers
            var library = so.FindProperty("library");
            library.arraySize = 2;
            library.GetArrayElementAtIndex(0).objectReferenceValue = pyreLord; // KAN-109: -ss-boss-type PyreLord, Realm 2 later
            library.GetArrayElementAtIndex(1).objectReferenceValue = matriarch; // KAN-111: -ss-boss-type BoneMatriarch
            so.ApplyModifiedPropertiesWithoutUndo();
            BossHeartsBuilder.Create(kit, root, fight, player); // healing hearts during the fight (KAN-60)
            return fight;
        }

        /// <summary>The guaranteed reward pick after the kill (KAN-62), on the "Boss" object.</summary>
        public static BossReward CreateReward(BossFight fight, ChestDropper chests, SkillManager skills, LevelUpFlow flow, XpManager xp)
        {
            var reward = fight.gameObject.AddComponent<BossReward>();
            var so = new SerializedObject(reward);
            so.FindProperty("fight").objectReferenceValue = fight;
            so.FindProperty("chests").objectReferenceValue = chests;
            so.FindProperty("skills").objectReferenceValue = skills;
            so.FindProperty("flow").objectReferenceValue = flow;
            so.FindProperty("xp").objectReferenceValue = xp;
            so.ApplyModifiedPropertiesWithoutUndo();
            return reward;
        }

        /// <summary>One red circle warning (ring + growing disc) under <paramref name="parent"/>. Also used by the ranged zombies (KAN-105).</summary>
        public static GroundTelegraph CreateTelegraph(GameArtKit kit, Transform parent, string name = "Telegraph")
        {
            var ringTex = TextureFactory.Write(RingTexturePath, TextureSize, TextureSize, RingPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: TextureSize);
            var discTex = TextureFactory.Write(DiscTexturePath, TextureSize, TextureSize, DiscPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: TextureSize);
            // Alpha blend, not additive: a dark red reads on white snow, additive red would vanish there.
            var ringMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_TelegraphRing.mat", ringTex, additive: false);
            var discMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_TelegraphDisc.mat", discTex, additive: false);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var fill = Flat("Fill", go.transform, kit.Quad, discMat);
            var ring = Flat("Ring", go.transform, kit.Quad, ringMat);
            ring.sortingOrder = 1; // ring over the disc
            var telegraph = go.AddComponent<GroundTelegraph>();
            var so = new SerializedObject(telegraph);
            so.FindProperty("ring").objectReferenceValue = ring;
            so.FindProperty("fill").objectReferenceValue = fill;
            so.ApplyModifiedPropertiesWithoutUndo();
            return telegraph;
        }

        /// <summary>
        /// Lane warnings (KAN-61): <paramref name="lanes"/> lanes (default <see cref="LineTelegraph.MaxLanes"/>), each an edge
        /// quad + a fill quad. Also the aim lines of the ranged zombies (KAN-105).
        /// </summary>
        public static LineTelegraph CreateLanes(GameArtKit kit, Transform parent, int lanes = LineTelegraph.MaxLanes, string name = "Lane Telegraph")
        {
            var edgeTex = TextureFactory.Write(LaneTexturePath, LaneTextureWidth, LaneTextureHeight, LanePixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: LaneTextureHeight);
            var fillTex = TextureFactory.Write(LaneFillTexturePath, LaneTextureWidth, LaneTextureHeight, LaneFillPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: LaneTextureHeight);
            var edgeMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_TelegraphLane.mat", edgeTex, additive: false);
            var fillMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_TelegraphLaneFill.mat", fillTex, additive: false);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            lanes = Mathf.Clamp(lanes, 1, LineTelegraph.MaxLanes);
            var edges = new Renderer[lanes];
            var fills = new Renderer[lanes];
            for (int i = 0; i < lanes; i++)
            {
                fills[i] = Flat("Fill " + i, go.transform, kit.Quad, fillMat);
                edges[i] = Flat("Edge " + i, go.transform, kit.Quad, edgeMat);
                edges[i].sortingOrder = 1; // edge over the fill
            }
            var lines = go.AddComponent<LineTelegraph>();
            var so = new SerializedObject(lines);
            SetArray(so.FindProperty("edges"), edges);
            SetArray(so.FindProperty("fills"), fills);
            so.ApplyModifiedPropertiesWithoutUndo();
            return lines;
        }

        private static void SetArray(SerializedProperty prop, Object[] items)
        {
            prop.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        /// <summary>Fan bolts (KAN-61): one particle system the script fills with SetParticles (head + 2 ghosts per bolt).</summary>
        private static BossProjectiles CreateBolts(Transform parent, PlayerHealth player)
        {
            var tex = TextureFactory.Write(BoltTexturePath, BoltTextureSize, BoltTextureSize, BoltPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: BoltTextureSize);
            // Alpha blend with a dark rim: an additive ember would vanish on the pale arena stone and the snow.
            var mat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_BossBolt.mat", tex, additive: false);
            var go = new GameObject("Bolts");
            go.transform.SetParent(parent, false);
            var ps = PropKit.Particles("Bolt Sprites", go.transform, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.maxParticles = BoltCapacity * 3;
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
            var bolts = go.AddComponent<BossProjectiles>();
            var so = new SerializedObject(bolts);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("bodies").objectReferenceValue = ps;
            so.FindProperty("capacity").intValue = BoltCapacity;
            so.ApplyModifiedPropertiesWithoutUndo();
            return bolts;
        }

        /// <summary>
        /// KAN-119: the lobbed attack's boulders. One unit-radius lumpy stone mesh (scaled to the rubble radius at the throw),
        /// <see cref="BoulderCount"/> bodies, each with a capsule collider (off while flying) so rubble blocks the Knight.
        /// </summary>
        private static BossBoulders CreateBoulders(GameArtKit kit, Transform parent, PlayerHealth player)
        {
            var mb = new MeshBuilder();
            ShoreKit.Boulder(mb, new Vector3(0f, -0.25f, 0f), new System.Random(119), 1f);
            var mesh = EditorAssetUtil.LoadOrCreateMesh(BoulderMeshPath);
            mb.WriteTo(mesh);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BoulderMaterialPath);
            if (mat == null) mat = kit.Palette;
            var go = new GameObject("Boulders");
            go.transform.SetParent(parent, false);
            var bodies = new Transform[BoulderCount];
            for (int i = 0; i < BoulderCount; i++)
            {
                var body = new GameObject("Boulder " + i);
                body.transform.SetParent(go.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                PropKit.SetupRenderer(body.AddComponent<MeshRenderer>(), mat);
                var solid = body.AddComponent<CapsuleCollider>();
                solid.radius = 1f;
                solid.height = 3f;
                solid.center = new Vector3(0f, 0.5f, 0f);
                solid.enabled = false;
                body.SetActive(false);
                bodies[i] = body.transform;
            }
            var boulders = go.AddComponent<BossBoulders>();
            var so = new SerializedObject(boulders);
            so.FindProperty("player").objectReferenceValue = player;
            SetArray(so.FindProperty("bodies"), bodies);
            so.ApplyModifiedPropertiesWithoutUndo();
            return boulders;
        }

        private static MeshRenderer Flat(string name, Transform parent, Mesh quad, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            return r;
        }

        /// <summary>Danger edge: a bold rim at the radius, a dark inner line for contrast on snow, a faint wash inside.</summary>
        private static Color RingPixel(int x, int y)
        {
            float r = Radius(x, y);
            float rim = 1f - TextureFactory.SmoothStep(0.03f, 0.07f, Mathf.Abs(r - 0.92f));
            float inner = (1f - TextureFactory.SmoothStep(0.01f, 0.03f, Mathf.Abs(r - 0.84f))) * 0.6f;
            float wash = r < 0.9f ? 0.14f : 0f;
            float a = Mathf.Clamp01(Mathf.Max(rim, Mathf.Max(inner, wash))) * (1f - TextureFactory.SmoothStep(0.97f, 1f, r));
            float shade = rim >= inner ? 1f : 0.45f; // inner line darker
            return new Color(shade, shade, shade, a);
        }

        /// <summary>Filled disc, slightly stronger towards the edge (reads as a pressure wave), soft outer edge.</summary>
        private static Color DiscPixel(int x, int y)
        {
            float r = Radius(x, y);
            float a = Mathf.Lerp(0.7f, 1f, r * r) * (1f - TextureFactory.SmoothStep(0.9f, 0.99f, r));
            return new Color(1f, 1f, 1f, a);
        }

        /// <summary>Lane edge: bold rims along both sides, a faint wash between, a rounded far end and a soft start.</summary>
        private static Color LanePixel(int x, int y)
        {
            float u = Mathf.Abs((x + 0.5f) / LaneTextureWidth * 2f - 1f); // 0 centre .. 1 side
            float v = (y + 0.5f) / LaneTextureHeight;                     // 0 boss .. 1 far end
            float rim = 1f - TextureFactory.SmoothStep(0.05f, 0.12f, Mathf.Abs(u - 0.86f));
            float wash = u < 0.84f ? 0.16f : 0f;
            float end = 1f - TextureFactory.SmoothStep(0.93f, 1f, v);
            float start = TextureFactory.SmoothStep(0f, 0.06f, v);
            float a = Mathf.Clamp01(Mathf.Max(rim, wash)) * end * start * (1f - TextureFactory.SmoothStep(0.96f, 1f, u));
            return new Color(1f, 1f, 1f, a);
        }

        /// <summary>
        /// Lane fill: solid with soft sides and three chevron bands near the tip (reads as direction). The quad has v 0 at
        /// the attacker and v 1 at the far end, so each band's centre sits further along v than its sides: the arrow tips
        /// point along the attack (KAN-166). Public for the EditMode test.
        /// </summary>
        public static Color LaneFillPixel(int x, int y)
        {
            float u = Mathf.Abs((x + 0.5f) / LaneTextureWidth * 2f - 1f);
            float v = (y + 0.5f) / LaneTextureHeight;
            float body = 0.72f * (1f - TextureFactory.SmoothStep(0.78f, 0.9f, u));
            float chevron = 0f;
            for (int i = 0; i < 3; i++)
            {
                float c = LaneChevronTip(i) - u * LaneChevronSweep; // sides trail behind the tip
                chevron = Mathf.Max(chevron, 1f - TextureFactory.SmoothStep(0.008f, 0.02f, Mathf.Abs(v - c)));
            }
            float a = Mathf.Clamp01(body + chevron * 0.28f) * (1f - TextureFactory.SmoothStep(0.95f, 1f, v));
            return new Color(1f, 1f, 1f, a);
        }

        /// <summary>Boss bolt: white-hot core, ember orange, crimson shell, dark rim (reads on snow), soft outer glow.</summary>
        private static Color BoltPixel(int x, int y)
        {
            float u = (x + 0.5f) / BoltTextureSize * 2f - 1f, v = (y + 0.5f) / BoltTextureSize * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            var core = new Color(1f, 0.93f, 0.7f);
            var ember = new Color(1f, 0.45f, 0.1f);
            var crimson = new Color(0.62f, 0.05f, 0.03f);
            var rim = new Color(0.16f, 0.01f, 0.01f);
            Color c = r < 0.22f ? core
                : r < 0.45f ? Color.Lerp(core, ember, (r - 0.22f) / 0.23f)
                : r < 0.68f ? Color.Lerp(ember, crimson, (r - 0.45f) / 0.23f)
                : Color.Lerp(crimson, rim, Mathf.Clamp01((r - 0.68f) / 0.12f));
            float a = r < 0.8f ? 1f : 0.35f * (1f - TextureFactory.SmoothStep(0.8f, 1f, r));
            if (r >= 0.78f && r < 0.8f) a = 1f;
            c.a = a;
            return c;
        }

        private static float Radius(int x, int y)
        {
            float u = (x + 0.5f) / TextureSize * 2f - 1f, v = (y + 0.5f) / TextureSize * 2f - 1f;
            return Mathf.Sqrt(u * u + v * v);
        }

        private static BossData LoadOrCreateData()
        {
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(CombatBuilder.BossPrefabPath)?.GetComponent<Enemy>();
            if (body == null) throw new System.InvalidOperationException($"Boss body missing: {CombatBuilder.BossPrefabPath}");
            var data = AssetDatabase.LoadAssetAtPath<BossData>(BossDataPath);
            if (data != null)
            {
                if (data.Prefab == null)
                {
                    var so = new SerializedObject(data);
                    so.FindProperty("prefab").objectReferenceValue = body;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(data);
                }
                // New fields (KAN-61 fan, charge, phase 2) get their defaults written into the YAML; set values stay.
                AssetDatabase.ForceReserializeAssets(new[] { BossDataPath });
                return data;
            }
            EditorAssetUtil.EnsureFolder(DataDir);
            // KAN-62 balance: 7200 HP x Chamber 5 health (x1.6) = 11520: a 10-pick Knight (~290 dps) shooting 55 % of the
            // time wins in ~73 s (BossIntroTests balance check); slam 22 % of max HP, 1 s warning; 2.4 s intro, 1.8 s beat.
            data = BossData.Create(body, health: 7200f, fanBolts: 5, charge: true, introTime: 2.4f, victoryBeat: 1.8f);
            AssetDatabase.CreateAsset(data, BossDataPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>
        /// The Butcher of Ashgrave (KAN-108, Docs/ENEMY_ROSTER.md 3.12): 4.2 m roster body, 8000 HP (Chamber 1 terms).
        /// Phase 1: cleaver chop (lane 6 x 1.6 m) and cleaver spin (r 3 m, 2.5 s, follows slowly) up close, a charge dash
        /// farther away (1 s stun when it hits a pillar or the wall). Phase 2 (50 %): roar, then chain hook (lane 10 m,
        /// drags the Knight 4 m) and a triple charge replace the single dash. Never overwritten: edit the asset.
        /// </summary>
        public static BossAttack[] ButcherAttacks() => new[]
        {
            BossAttack.Lane("Cleaver Chop", BossPhases.Both, 0f, 5.5f, length: 6f, width: 1.6f, telegraph: 0.9f, damage: 0.22f,
                recover: 0.8f, cooldown: 2.6f, priority: 1, weight: 2f, clip: "Chop"),
            BossAttack.Spin("Cleaver Spin", BossPhases.Both, 4.5f, radius: 3f, telegraph: 0.9f, duration: 2.5f, speed: 1.6f,
                interval: 0.6f, damage: 0.08f, recover: 1f, cooldown: 3.2f, priority: 1, clip: "Spin"),
            BossAttack.Charge("Charge", BossPhases.Phase1, 4.5f, 13f, telegraph: 0.9f, speed: 15f, distance: 11f, width: 2.6f,
                damage: 0.2f, recover: 0.8f, cooldown: 3.4f, stun: 1f, clip: "Charge"),
            BossAttack.Lane("Chain Hook", BossPhases.Phase2, 5f, 10f, length: 10f, width: 1f, telegraph: 0.8f, damage: 0.1f,
                recover: 0.7f, cooldown: 2.4f, pull: 4f, clip: "Chop"),
            BossAttack.Charge("Triple Charge", BossPhases.Phase2, 4.5f, 14f, telegraph: 0.75f, speed: 16f, distance: 10f,
                width: 2.6f, damage: 0.18f, recover: 1f, cooldown: 3.6f, dashes: 3, stun: 1f, weight: 2f, clip: "Charge"),
        };

        private static BossData LoadOrCreateButcher()
        {
            var body = EnemyRosterBuilder.LoadPrefab("AshgraveButcher");
            if (body == null) throw new System.InvalidOperationException("Boss body missing: Enemy_AshgraveButcher (EnemyRosterBuilder)");
            var data = AssetDatabase.LoadAssetAtPath<BossData>(ButcherDataPath);
            if (data != null)
            {
                if (data.Prefab == null)
                {
                    var so = new SerializedObject(data);
                    so.FindProperty("prefab").objectReferenceValue = body;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(data);
                }
                AssetDatabase.ForceReserializeAssets(new[] { ButcherDataPath });
                return data;
            }
            EditorAssetUtil.EnsureFolder(DataDir);
            // 8000 HP x Chamber 5 health (x1.6): a 10-pick Knight kills it in ~80 s (BossIntroTests balance check).
            data = BossData.CreateWithAttacks(body, ButcherAttacks(), health: 8000f, firstAttackDelay: 1.5f, introTime: 2.4f,
                victoryBeat: 1.8f, roarClip: "Roar", phaseRoar: 1.2f, displayName: "THE BUTCHER OF ASHGRAVE",
                title: "Headsman of the Ashen Gallows");
            AssetDatabase.CreateAsset(data, ButcherDataPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>
        /// The Pyre Lord (KAN-109, Docs/ENEMY_ROSTER.md 3.13): 4.5 m roster body with baked lava glow, 8000 HP (Chamber 1
        /// terms; doc 9000, but then a no-pick Knight needs over 4 min), a fire caster. Phase 1: fireball fan (7 bolts over
        /// 70 deg) at range, fire nova (ring of 16 with two gaps of two bolts) at mid range, flame slam up close (circle r 4 m round the body, leaves a burning
        /// patch r 2.2 m for 3 s). Phase 2 (50 %): roar, then the fire spiral (2 arms, 3 s) and the meteor rain (6 circles,
        /// each leaves a small burning patch) join. Never overwritten: edit the asset.
        /// </summary>
        public static BossAttack[] PyreLordAttacks()
        {
            var nova = BossAttack.Sample(BossFight.Move.Nova);
            nova.gaps = 2; // two doors of two missing bolts: a clear way through at any distance
            nova.clip = "CastNova";
            var spiral = BossAttack.Sample(BossFight.Move.Spiral);
            spiral.clip = "CastNova";
            var rain = BossAttack.Sample(BossFight.Move.Rain).WithBurn(radius: 1f, life: 2f, dpsShare: 0.05f);
            rain.clip = "CastFan";
            return new[]
            {
                BossAttack.Slam("Flame Slam", BossPhases.Both, range: 3.5f, radius: 4f, reach: 0f, telegraph: 1.1f, damage: 0.2f,
                    recover: 0.9f, cooldown: 3.4f, priority: 1, clip: "Slam").WithBurn(radius: 2.2f, life: 3f, dpsShare: 0.05f),
                BossAttack.Fan("Fireball Fan", BossPhases.Both, maxRange: 14f, bolts: 7, spread: 70f, telegraph: 0.9f, speed: 9f,
                    distance: 15f, laneWidth: 0.6f, hitRadius: 0.6f, damage: 0.12f, recover: 0.6f, cooldown: 2.6f, weight: 2f,
                    clip: "CastFan"),
                nova,
                spiral,
                rain,
            };
        }

        private static BossData LoadOrCreatePyreLord()
        {
            var body = EnemyRosterBuilder.LoadPrefab("PyreLord");
            if (body == null) throw new System.InvalidOperationException("Boss body missing: Enemy_PyreLord (EnemyRosterBuilder)");
            var data = AssetDatabase.LoadAssetAtPath<BossData>(PyreLordDataPath);
            if (data != null)
            {
                if (data.Prefab == null)
                {
                    var so = new SerializedObject(data);
                    so.FindProperty("prefab").objectReferenceValue = body;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(data);
                }
                AssetDatabase.ForceReserializeAssets(new[] { PyreLordDataPath });
                return data;
            }
            EditorAssetUtil.EnsureFolder(DataDir);
            // 8000 HP x Chamber 5 health (x1.6): a 10-pick Knight kills it in ~73 s, no picks ~233 s (BossIntroTests balance check).
            data = BossData.CreateWithAttacks(body, PyreLordAttacks(), health: 8000f, firstAttackDelay: 1.5f, introTime: 2.4f,
                victoryBeat: 1.8f, roarClip: "Roar", phaseRoar: 1.2f, displayName: "THE PYRE LORD",
                title: "Ember King of the Cinder Throne");
            AssetDatabase.CreateAsset(data, PyreLordDataPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>
        /// The Rotting Colossus (KAN-110, Docs/ENEMY_ROSTER.md 3.14): 5.0 m roster body (the biggest), 8200 HP (Chamber 1
        /// terms; doc 11000, but then a no-pick Knight needs over 4 min, so it gets the most HP the balance rule allows), the
        /// slowest boss (1.2 m/s). Phase 1: double slam up close (two shockwave rings, warning circle r 5.5 m), grab at mid
        /// range (1.2 s lane 7 x 1.4 m; a hit deals 20 % and drags the Knight in to the body), corpse boulder at range
        /// (landing circle r 2.5 m, rubble r 0.9 m for 8 s that blocks the ground and boss bolts). Phase 2 (50 %): roar, then
        /// the roar summons 4 Frenzied Ghouls (only the missing ones come) and the boulders come 3 in a row. Never
        /// overwritten: edit the asset.
        /// </summary>
        public static BossAttack[] ColossusAttacks()
        {
            var slam = BossAttack.Sample(BossFight.Move.Shockwave);
            slam.priority = 1; // up close the slam goes first
            slam.clip = "Slam";
            var boulder = BossAttack.Sample(BossFight.Move.Lobbed);
            boulder.minRange = 5f; // a thrower at range; up close it slams or grabs
            boulder.phases = BossPhases.Phase1;
            boulder.clip = "Throw";
            var volley = boulder;
            volley.label = "Boulder Volley";
            volley.phases = BossPhases.Phase2;
            volley.count = 3;
            volley.interval = 0.7f;
            volley.cooldown = 3.6f;
            volley.weight = 1.5f;
            var summon = BossAttack.Sample(BossFight.Move.Summon);
            summon.clip = "Roar";
            return new[]
            {
                slam,
                BossAttack.Lane("Grab", BossPhases.Both, 2.5f, 7f, length: 7f, width: 1.4f, telegraph: 1.2f, damage: 0.2f,
                    recover: 0.9f, cooldown: 2.8f, pull: 7f, weight: 1.5f, clip: "Grab"),
                boulder,
                volley,
                summon,
            };
        }

        private static BossData LoadOrCreateColossus()
        {
            var body = EnemyRosterBuilder.LoadPrefab("RottingColossus");
            if (body == null) throw new System.InvalidOperationException("Boss body missing: Enemy_RottingColossus (EnemyRosterBuilder)");
            var data = AssetDatabase.LoadAssetAtPath<BossData>(ColossusDataPath);
            if (data != null)
            {
                if (data.Prefab == null)
                {
                    var so = new SerializedObject(data);
                    so.FindProperty("prefab").objectReferenceValue = body;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(data);
                }
                AssetDatabase.ForceReserializeAssets(new[] { ColossusDataPath });
                return data;
            }
            EditorAssetUtil.EnsureFolder(DataDir);
            // 8200 HP x Chamber 5 health (x1.6): a 10-pick Knight kills it in ~75 s, no picks ~239 s (BossIntroTests balance check).
            data = BossData.CreateWithAttacks(body, ColossusAttacks(), health: 8200f, firstAttackDelay: 1.5f, introTime: 2.6f,
                victoryBeat: 1.8f, roarClip: "Roar", phaseRoar: 1.4f, displayName: "THE ROTTING COLOSSUS",
                title: "Carrion Giant of the Barrow Fields");
            var zoom = new SerializedObject(data);
            zoom.FindProperty("introZoom").floatValue = 1.5f; // the 5 m body: pull out further than the 4.2-4.5 m bosses
            zoom.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(data, ColossusDataPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>
        /// The Bone Matriarch (KAN-111, Docs/ENEMY_ROSTER.md 3.15): 4.0 m roster body, 7000 HP (doc; the least of all bosses),
        /// floats and never melees (<see cref="ConfigurePrefab"/>: no contact damage, holds 6-9 m from the Knight, hover bob).
        /// Phase 1: Homing Skulls (3 slow orbs, turn 55 deg/s, 3.6 m/s, 14.4 m = 4 s life, a pillar or the wall stops them),
        /// Soul Ring (24 orbs with 3 gaps of two missing orbs), Raise the Dead (4 Graveyard Laborers rise round her; only the
        /// missing ones come; the Bone Archer is cut, KAN-128). Phase 2 (50 %) adds Cross Beams (4 lanes 11 m from the body,
        /// line warnings, then they turn 45 deg/s for 4 s; the Knight starts in a gap and can walk with it) and a timed
        /// Teleport every 8 s (warning circle where she lands, 7.5 m from the Knight = inside her hold band). Clips: CastOrbs,
        /// CastRing, Summon, Roar. Never overwritten: edit the asset.
        /// </summary>
        public static BossAttack[] MatriarchAttacks()
        {
            var skulls = BossAttack.Sample(BossFight.Move.Homing);
            skulls.length = skulls.speed * 4f; // die after 4 s (or on a pillar / the wall)
            skulls.weight = 1.5f;
            skulls.clip = "CastOrbs";
            var beams = BossAttack.Sample(BossFight.Move.Beams);
            beams.length = 11f; // reaches past her 9 m hold band
            beams.weight = 1.2f;
            beams.clip = "CastRing";
            var teleport = BossAttack.Sample(BossFight.Move.Teleport).Every(8f);
            teleport.length = (MatriarchHoldMin + MatriarchHoldMax) * 0.5f; // lands inside her band: no walk after
            teleport.cooldown = 1f;
            teleport.clip = "CastOrbs";
            return new[]
            {
                skulls,
                BossAttack.Nova("Soul Ring", BossPhases.Both, 14f, bolts: 24, gaps: 3, warnRadius: 3f, telegraph: 0.9f, speed: 6f,
                    distance: 14f, hitRadius: 0.5f, damage: 0.14f, recover: 0.8f, cooldown: 2.8f, clip: "CastRing"),
                BossAttack.Summon("Raise the Dead", BossPhases.Both, 0f, 30f, "GraveyardLaborer", count: 4, ring: 2.6f,
                    telegraph: 1.2f, recover: 0.8f, cooldown: 4f, weight: 0.7f, clip: "Summon"),
                beams,
                teleport,
            };
        }

        private static BossData LoadOrCreateMatriarch()
        {
            var body = EnemyRosterBuilder.LoadPrefab(MatriarchId);
            if (body == null) throw new System.InvalidOperationException("Boss body missing: Enemy_BoneMatriarch (EnemyRosterBuilder)");
            var data = AssetDatabase.LoadAssetAtPath<BossData>(MatriarchDataPath);
            if (data != null)
            {
                if (data.Prefab == null)
                {
                    var so = new SerializedObject(data);
                    so.FindProperty("prefab").objectReferenceValue = body;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(data);
                }
                AssetDatabase.ForceReserializeAssets(new[] { MatriarchDataPath });
                return data;
            }
            EditorAssetUtil.EnsureFolder(DataDir);
            // 7000 HP x Chamber 5 health (x1.6): a 10-pick Knight kills it in ~64 s, no picks ~204 s (BossIntroTests balance check).
            data = BossData.CreateWithAttacks(body, MatriarchAttacks(), health: 7000f, firstAttackDelay: 1.5f, introTime: 2.6f,
                victoryBeat: 1.8f, roarClip: "Roar", phaseRoar: 1.4f, displayName: "THE BONE MATRIARCH",
                title: "Lich Queen of the Ossuary");
            AssetDatabase.CreateAsset(data, MatriarchDataPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>
        /// KAN-111: the Bone Matriarch body before <see cref="EnemyRosterBuilder"/> saves the prefab: no contact damage (she
        /// never melees), a 6-9 m hold band (backs off when the Knight closes in) and a <see cref="HoverBob"/> on the model
        /// (0.3 m lift, 0.12 m bob every 2.6 s; the root and blob shadow stay on the ground). Her Walking clip already plays
        /// slow (glide; EnemyRosterBuilder.MinMoveRate). No-op for other types.
        /// </summary>
        public static void ConfigurePrefab(GameObject root, Enemy enemy, string id)
        {
            if (id != MatriarchId) return;
            var so = new SerializedObject(enemy);
            so.FindProperty("damagePerSecond").floatValue = 0f;
            so.FindProperty("holdMin").floatValue = MatriarchHoldMin;
            so.FindProperty("holdMax").floatValue = MatriarchHoldMax;
            so.ApplyModifiedPropertiesWithoutUndo();
            var animator = root.GetComponentInChildren<Animator>();
            var model = animator != null && animator.transform.parent != null && animator.transform.parent != root.transform
                ? animator.transform.parent
                : throw new System.InvalidOperationException("BoneMatriarch: model node above the rig missing");
            model.gameObject.AddComponent<HoverBob>().Configure(0.3f, 0.12f, 2.6f);
        }

        /// <summary>
        /// KAN-112: Realm 1 boss rotation asset: the 1st boss Chamber (5) fights the Butcher, the 2nd (10, the Realm's
        /// end) the Rotting Colossus, the heavier fight with the most HP. Pyre Lord and Bone Matriarch stay in the library
        /// for later Realms (KAN-57). An existing asset is kept as it is (designer edits); only empty slots are filled.
        /// </summary>
        public static BossRotation LoadOrCreateRotation(BossData first, BossData second)
        {
            var rotation = AssetDatabase.LoadAssetAtPath<BossRotation>(RotationPath);
            if (rotation != null)
            {
                var list = rotation.Bosses;
                bool dirty = false;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] == null) { list[i] = i == 0 ? first : second; dirty = true; }
                if (dirty) EditorUtility.SetDirty(rotation);
                return rotation;
            }
            rotation = BossRotation.Create(new[] { first, second });
            AssetDatabase.CreateAsset(rotation, RotationPath);
            AssetDatabase.SaveAssets();
            return rotation;
        }

        private static ChamberRules LoadOrCreateRules()
        {
            var rules = AssetDatabase.LoadAssetAtPath<ChamberRules>(BossRulesPath);
            if (rules != null) return rules;
            rules = ChamberRules.Create(600f, new int[0], 0f, 2f, OpenCondition.KillBoss);
            AssetDatabase.CreateAsset(rules, BossRulesPath);
            AssetDatabase.SaveAssets();
            return rules;
        }

        [MenuItem("Survivor Souls/Debug/Boss: Kill")]
        private static void Kill()
        {
            var boss = BossFight.Active != null ? BossFight.Active.Boss : null;
            if (boss != null && boss.IsAlive) boss.TakeDamage(boss.Health + 1f, Vector3.zero);
        }

        [MenuItem("Survivor Souls/Debug/Boss: Half Health")]
        private static void Half() => SetHealth(0.5f);

        [MenuItem("Survivor Souls/Debug/Boss: Health 25 %")]
        private static void Quarter() => SetHealth(0.25f);

        [MenuItem("Survivor Souls/Debug/Boss: Health 10 %")]
        private static void Tenth() => SetHealth(0.1f);

        /// <summary>Lowers the boss to <paramref name="share"/> of max health (as damage: phase 2 and the HUD react).</summary>
        private static void SetHealth(float share)
        {
            var boss = BossFight.Active != null ? BossFight.Active.Boss : null;
            if (boss != null && boss.Health > boss.MaxHealth * share) boss.TakeDamage(boss.Health - boss.MaxHealth * share, Vector3.zero);
        }

        /// <summary>Reloads the Game scene as the next boss Chamber of this run (the first one after the last).</summary>
        [MenuItem("Survivor Souls/Debug/Boss: Jump To Boss Chamber")]
        private static void JumpToBoss()
        {
            var setup = Object.FindAnyObjectByType<ChamberSetup>();
            var difficulty = setup != null ? setup.Difficulty : null;
            int chamber = difficulty != null ? difficulty.NextBossChamber(RunState.Chamber) : 5;
            RunState.JumpToChamber(chamber);
            GamePause.Clear();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            Debug.Log($"[BossBuilder] Jump to boss Chamber {chamber}");
        }

        [MenuItem("Survivor Souls/Debug/Boss: Jump To Boss Chamber", true)]
        private static bool InGameScene() => Application.isPlaying && Object.FindAnyObjectByType<ChamberSetup>() != null;

        [MenuItem("Survivor Souls/Debug/Boss: Fan")]
        private static void Fan() => BossFight.Active.ForceAttack(BossFight.Move.Fan);

        [MenuItem("Survivor Souls/Debug/Boss: Charge")]
        private static void Charge() => BossFight.Active.ForceAttack(BossFight.Move.Charge);

        [MenuItem("Survivor Souls/Debug/Boss: Slam")]
        private static void Slam() => BossFight.Active.ForceAttack(BossFight.Move.Slam);

        [MenuItem("Survivor Souls/Debug/Boss: Lane (Chop / Hook)")]
        private static void Lane() => BossFight.Active.ForceAttack(BossFight.Move.Lane);

        [MenuItem("Survivor Souls/Debug/Boss: Spin")]
        private static void Spin() => BossFight.Active.ForceAttack(BossFight.Move.Spin);

        [MenuItem("Survivor Souls/Debug/Boss: Nova")]
        private static void Nova() => ForceOrSample(BossFight.Move.Nova);

        [MenuItem("Survivor Souls/Debug/Boss: Spiral")]
        private static void Spiral() => ForceOrSample(BossFight.Move.Spiral);

        [MenuItem("Survivor Souls/Debug/Boss: Rain")]
        private static void Rain() => ForceOrSample(BossFight.Move.Rain);

        [MenuItem("Survivor Souls/Debug/Boss: Lobbed")]
        private static void Lobbed() => ForceOrSample(BossFight.Move.Lobbed);

        [MenuItem("Survivor Souls/Debug/Boss: Summon")]
        private static void Summon() => ForceOrSample(BossFight.Move.Summon);

        [MenuItem("Survivor Souls/Debug/Boss: Teleport")]
        private static void TeleportMenu() => ForceOrSample(BossFight.Move.Teleport);

        [MenuItem("Survivor Souls/Debug/Boss: Shockwave")]
        private static void Shockwave() => ForceOrSample(BossFight.Move.Shockwave);

        [MenuItem("Survivor Souls/Debug/Boss: Homing")]
        private static void Homing() => ForceOrSample(BossFight.Move.Homing);

        [MenuItem("Survivor Souls/Debug/Boss: Beams")]
        private static void Beams() => ForceOrSample(BossFight.Move.Beams);

        /// <summary>KAN-116: starts the boss's <paramref name="move"/> attack; a boss without one gets the sample entry first.</summary>
        private static void ForceOrSample(BossFight.Move move)
        {
            var fight = BossFight.Active;
            if (fight.ForceAttack(move)) return;
            foreach (var a in fight.Attacks)
                if (a.type == move) return; // it has one: busy right now, try again
            fight.ForceAttack(fight.AddAttack(BossAttack.Sample(move)));
        }

        /// <summary>KAN-109: the next boss Chamber (this run, after a jump or a portal) gets The Pyre Lord. Play mode only.</summary>
        [MenuItem("Survivor Souls/Debug/Boss: Next Fight Pyre Lord")]
        private static void NextPyreLord()
        {
            BossFight.ForcedBoss = BossFight.ForcedBoss == "PyreLord" ? null : "PyreLord";
            Debug.Log($"[BossBuilder] forced boss: {BossFight.ForcedBoss ?? "none (Realm list)"}");
        }

        /// <summary>KAN-110: the next boss Chamber (this run, after a jump or a portal) gets The Rotting Colossus. Play mode only.</summary>
        [MenuItem("Survivor Souls/Debug/Boss: Next Fight Rotting Colossus")]
        private static void NextColossus()
        {
            BossFight.ForcedBoss = BossFight.ForcedBoss == "RottingColossus" ? null : "RottingColossus";
            Debug.Log($"[BossBuilder] forced boss: {BossFight.ForcedBoss ?? "none (Realm list)"}");
        }

        /// <summary>KAN-111: the next boss Chamber (this run, after a jump or a portal) gets The Bone Matriarch. Play mode only.</summary>
        [MenuItem("Survivor Souls/Debug/Boss: Next Fight Bone Matriarch")]
        private static void NextMatriarch()
        {
            BossFight.ForcedBoss = BossFight.ForcedBoss == MatriarchId ? null : MatriarchId;
            Debug.Log($"[BossBuilder] forced boss: {BossFight.ForcedBoss ?? "none (Realm list)"}");
        }

        [MenuItem("Survivor Souls/Debug/Boss: Print Attacks")]
        private static void PrintAttacks()
        {
            var fight = BossFight.Active;
            var sb = new System.Text.StringBuilder($"[BossBuilder] {fight.Data.DisplayName} phase {fight.Phase}:\n");
            for (int i = 0; i < fight.Attacks.Length; i++)
            {
                var a = fight.Attacks[i];
                sb.AppendLine($"  {i} {a} {a.type} {a.phases} prio {a.priority} w {a.weight} range {a.minRange}-{a.maxRange} " +
                              $"warn {a.Telegraph} dmg {a.damage} clip {a.clip}");
            }
            Debug.Log(sb.ToString());
        }

        [MenuItem("Survivor Souls/Debug/Boss: Kill", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Slam", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Lane (Chop / Hook)", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Spin", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Nova", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Spiral", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Rain", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Lobbed", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Summon", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Teleport", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Shockwave", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Homing", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Beams", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Print Attacks", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Half Health", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Health 25 %", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Health 10 %", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Fan", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Charge", true)]
        private static bool BossAlive() =>
            Application.isPlaying && BossFight.Active != null && BossFight.Active.Boss != null && BossFight.Active.Boss.IsAlive;
    }
}
