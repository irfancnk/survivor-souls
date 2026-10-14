using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Boss
{
    /// <summary>
    /// Boss fight of the Game scene (KAN-58): the "Boss" object with <see cref="BossFight"/> and its red ground telegraph
    /// (ring + growing disc, two flat quads, procedural textures), the Realm 1 boss asset (<see cref="BossData"/>, never
    /// overwritten: only an empty body slot is filled) and the boss Chamber rules (<see cref="OpenCondition.KillBoss"/>).
    /// The body prefab is built by <see cref="CombatBuilder"/>. Healing hearts (KAN-60) live on the same object
    /// (<see cref="BossHeartsBuilder"/>). Called by GameSceneBuilder.
    /// Debug menus (Play mode): "Survivor Souls/Debug/Boss: Kill" and "Boss: Half Health".
    /// </summary>
    public static class BossBuilder
    {
        private const string DataDir = "Assets/_Project/Data/Boss";
        public const string BossDataPath = DataDir + "/Boss_Realm1_Warden.asset";
        public const string BossRulesPath = "Assets/_Project/Data/Chamber/ChamberRules_Boss.asset";
        private const string ArtDir = "Assets/_Project/Art/Boss";
        private const string RingTexturePath = ArtDir + "/T_TelegraphRing.png";
        private const string DiscTexturePath = ArtDir + "/T_TelegraphDisc.png";
        private const int TextureSize = 256;

        public static BossFight Create(GameArtKit kit, PlayerHealth player, SurvivalTimer timer, EnemySpawner spawner, SoulWispField souls)
        {
            var data = LoadOrCreateData();
            var rules = LoadOrCreateRules();
            var root = new GameObject("Boss");
            var telegraph = CreateTelegraph(kit, root.transform);
            var fight = root.AddComponent<BossFight>();
            var so = new SerializedObject(fight);
            so.FindProperty("data").objectReferenceValue = data;
            so.FindProperty("bossRules").objectReferenceValue = rules;
            so.FindProperty("timer").objectReferenceValue = timer;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("souls").objectReferenceValue = souls;
            so.FindProperty("telegraph").objectReferenceValue = telegraph;
            so.ApplyModifiedPropertiesWithoutUndo();
            BossHeartsBuilder.Create(kit, root, fight, player); // healing hearts during the fight (KAN-60)
            return fight;
        }

        private static GroundTelegraph CreateTelegraph(GameArtKit kit, Transform parent)
        {
            var ringTex = TextureFactory.Write(RingTexturePath, TextureSize, TextureSize, RingPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: TextureSize);
            var discTex = TextureFactory.Write(DiscTexturePath, TextureSize, TextureSize, DiscPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: TextureSize);
            // Alpha blend, not additive: a dark red reads on white snow, additive red would vanish there.
            var ringMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_TelegraphRing.mat", ringTex, additive: false);
            var discMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_TelegraphDisc.mat", discTex, additive: false);

            var go = new GameObject("Telegraph");
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
                return data;
            }
            EditorAssetUtil.EnsureFolder(DataDir);
            // First guesses (KAN-62 balances): 3200 HP x Chamber 5 health (x1.6) ~ 5100; slam 22 % of max HP, 1 s warning.
            data = BossData.Create(body);
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
        private static void Half()
        {
            var boss = BossFight.Active != null ? BossFight.Active.Boss : null;
            if (boss != null && boss.Health > boss.MaxHealth * 0.5f) boss.TakeDamage(boss.Health - boss.MaxHealth * 0.5f, Vector3.zero);
        }

        [MenuItem("Survivor Souls/Debug/Boss: Kill", true)]
        [MenuItem("Survivor Souls/Debug/Boss: Half Health", true)]
        private static bool BossAlive() =>
            Application.isPlaying && BossFight.Active != null && BossFight.Active.Boss != null && BossFight.Active.Boss.IsAlive;
    }
}
