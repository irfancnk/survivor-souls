using SurvivorSouls.Editor.Combat;
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
    /// Debug menus (Play mode): "Survivor Souls/Debug/Boss: Kill", "Boss: Half Health" (= phase 2), "Boss: Health 25 %",
    /// "Boss: Health 10 %", "Boss: Fan", "Boss: Charge", "Boss: Jump To Boss Chamber" (any Chamber; reloads the scene).
    /// </summary>
    public static class BossBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Boss";
        public const string BossDataPath = DataDir + "/Boss_Realm1_Warden.asset";
        public const string BossRulesPath = "Assets/_Project/Data/Chamber/ChamberRules_Boss.asset";
        private const string ArtDir = "Assets/_Project/Art/Boss";
        private const string RingTexturePath = ArtDir + "/T_TelegraphRing.png";
        private const string DiscTexturePath = ArtDir + "/T_TelegraphDisc.png";
        private const string LaneTexturePath = ArtDir + "/T_TelegraphLane.png";
        private const string LaneFillTexturePath = ArtDir + "/T_TelegraphLaneFill.png";
        private const string BoltTexturePath = ArtDir + "/T_BossBolt.png";
        private const int LaneTextureWidth = 64;
        private const int LaneTextureHeight = 256;
        private const int BoltTextureSize = 64;
        private const int BoltCapacity = 32;
        private const int TextureSize = 256;

        public static BossFight Create(GameArtKit kit, PlayerHealth player, SurvivalTimer timer, EnemySpawner spawner, SoulWispField souls)
        {
            var data = LoadOrCreateData();
            var rules = LoadOrCreateRules();
            var root = new GameObject("Boss");
            var telegraph = CreateTelegraph(kit, root.transform);
            var lines = CreateLanes(kit, root.transform);
            var bolts = CreateBolts(root.transform, player);
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

        /// <summary>Lane fill: solid with soft sides and three chevron bands near the tip (reads as direction).</summary>
        private static Color LaneFillPixel(int x, int y)
        {
            float u = Mathf.Abs((x + 0.5f) / LaneTextureWidth * 2f - 1f);
            float v = (y + 0.5f) / LaneTextureHeight;
            float body = 0.72f * (1f - TextureFactory.SmoothStep(0.78f, 0.9f, u));
            float chevron = 0f;
            for (int i = 0; i < 3; i++)
            {
                float c = 0.7f + i * 0.09f + u * 0.06f; // arrow tips point to the far end
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

        [MenuItem("Survivor Souls/Debug/Boss: Kill", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Half Health", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Health 25 %", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Health 10 %", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Fan", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Charge", true)]
        private static bool BossAlive() =>
            Application.isPlaying && BossFight.Active != null && BossFight.Active.Boss != null && BossFight.Active.Boss.IsAlive;
    }
}
