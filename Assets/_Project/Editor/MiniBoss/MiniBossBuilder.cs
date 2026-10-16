using SurvivorSouls.Editor.Boss;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.MiniBosses;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.MiniBoss
{
    /// <summary>
    /// Mini boss of the Game scene (KAN-23): the Realm 1 data asset (<see cref="DataPath"/>, made once, never overwritten),
    /// the "MiniBoss" object with <see cref="MiniBossDirector"/>, its own crimson ground warning (the boss telegraph look,
    /// <see cref="BossBuilder.CreateTelegraph"/>) and the ground aura (<see cref="MiniBossAura"/>: T_Ring + T_SoftDot quads,
    /// alpha blend). KAN-212: the special attack (<see cref="MiniBossAttack"/>) with its own red circle + one lane
    /// (<see cref="BossBuilder.CreateTelegraph"/>, <see cref="BossBuilder.CreateLanes"/>) and the scene's follow camera
    /// for the shake. Called by GameSceneBuilder. Debug menus (Play mode): "Survivor Souls/Debug/Mini Boss: Spawn Now",
    /// "Mini Boss: Kill".
    /// </summary>
    public static class MiniBossBuilder
    {
        private const string DataDir = "Assets/_Project/Data/MiniBoss";
        public const string DataPath = DataDir + "/MiniBoss_Realm1_Gravewarden.asset";
        private const string ArtDir = "Assets/_Project/Art/MiniBoss";
        private const string RingTexturePath = "Assets/_Project/Art/Textures/Game/T_Ring.png"; // made by GameArtKit

        public static MiniBossDirector Create(GameArtKit kit, EnemySpawner spawner, PlayerHealth player, ChestDropper chests,
            SkillManager skills, LevelUpFlow flow, XpManager xp, SoulWispField souls)
        {
            var data = LoadOrCreateData();
            var go = new GameObject("MiniBoss");
            var warning = BossBuilder.CreateTelegraph(kit, go.transform, "Warning");
            var aura = CreateAura(kit, go.transform);
            var attack = CreateAttack(kit, go);
            var director = go.AddComponent<MiniBossDirector>();
            var so = new SerializedObject(director);
            so.FindProperty("data").objectReferenceValue = data;
            so.FindProperty("spawner").objectReferenceValue = spawner;
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("chests").objectReferenceValue = chests;
            so.FindProperty("skills").objectReferenceValue = skills;
            so.FindProperty("flow").objectReferenceValue = flow;
            so.FindProperty("xp").objectReferenceValue = xp;
            so.FindProperty("souls").objectReferenceValue = souls;
            so.FindProperty("warning").objectReferenceValue = warning;
            so.FindProperty("aura").objectReferenceValue = aura;
            so.FindProperty("attack").objectReferenceValue = attack;
            so.ApplyModifiedPropertiesWithoutUndo();
            return director;
        }

        /// <summary>KAN-212: the attack loop with its own warnings (attack red, apart from the crimson rise warning).</summary>
        private static MiniBossAttack CreateAttack(GameArtKit kit, GameObject root)
        {
            var slam = BossBuilder.CreateTelegraph(kit, root.transform, "Slam Warning");
            var lane = BossBuilder.CreateLanes(kit, root.transform, 1, "Charge Lane");
            var attack = root.AddComponent<MiniBossAttack>();
            var so = new SerializedObject(attack);
            so.FindProperty("slamWarning").objectReferenceValue = slam;
            so.FindProperty("chargeLane").objectReferenceValue = lane;
            // The camera rig is built before the mini boss (GameSceneBuilder); found here so the builder call stays as is.
            so.FindProperty("followCamera").objectReferenceValue = Object.FindAnyObjectByType<TopDownFollowCamera>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return attack;
        }

        private static MiniBossData LoadOrCreateData()
        {
            var data = AssetDatabase.LoadAssetAtPath<MiniBossData>(DataPath);
            if (data != null) return data;
            EditorAssetUtil.EnsureFolder(DataDir);
            data = MiniBossData.Create(); // first guesses: Grave Brute x4 health, x1.35 size, 20 s in, Light chest
            data.name = "MiniBoss_Realm1_Gravewarden";
            AssetDatabase.CreateAsset(data, DataPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        private static MiniBossAura CreateAura(GameArtKit kit, Transform parent)
        {
            EditorAssetUtil.EnsureFolder(ArtDir);
            var ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath);
            if (ringTex == null) throw new System.InvalidOperationException($"Missing {RingTexturePath} (GameArtKit builds it).");
            // Alpha blend, not additive: additive glows vanish on Realm 1 snow.
            var ringMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_MiniBossAuraRing.mat", ringTex, additive: false);
            var discMat = EditorAssetUtil.ParticleMaterial(ArtDir + "/M_FX_MiniBossAuraDisc.mat", kit.SoftDot, additive: false);

            var go = new GameObject("Aura");
            go.transform.SetParent(parent, false);
            var disc = Flat("Disc", go.transform, kit.Quad, discMat);
            var ring = Flat("Ring", go.transform, kit.Quad, ringMat);
            ring.sortingOrder = 1;
            var aura = go.AddComponent<MiniBossAura>();
            var so = new SerializedObject(aura);
            so.FindProperty("ring").objectReferenceValue = ring;
            so.FindProperty("disc").objectReferenceValue = disc;
            so.ApplyModifiedPropertiesWithoutUndo();
            return aura;
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

        [MenuItem("Survivor Souls/Debug/Mini Boss: Spawn Now")]
        private static void SpawnNow()
        {
            var director = Object.FindAnyObjectByType<MiniBossDirector>();
            if (director == null || !director.Arm(0f)) Debug.Log("[MiniBoss] No director, or the mini boss of this Chamber already rose.");
        }

        [MenuItem("Survivor Souls/Debug/Mini Boss: Kill")]
        private static void Kill()
        {
            var director = Object.FindAnyObjectByType<MiniBossDirector>();
            if (director != null && director.Boss != null) director.Boss.TakeDamage(1e9f, Vector3.forward);
        }
    }
}
