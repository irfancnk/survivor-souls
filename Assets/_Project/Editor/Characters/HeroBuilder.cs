using System.Collections.Generic;
using System.IO;
using System.Linq;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Heroes;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Characters
{
    /// <summary>
    /// Hero roster (KAN-153, first slice of KAN-100): the hero data assets, the Shadow Ranger model prefab and the player
    /// wiring.
    /// <list type="bullet">
    /// <item>Data (<c>Data/Heroes/</c>): <see cref="HeroRoster"/> + one <see cref="HeroDefinition"/> per hero, made once with
    /// the first balance guesses and never overwritten (Inspector tuning stays); only the model and portrait links are
    /// refreshed. The Knight is the free reference hero (empty profile). The Shadow Ranger costs
    /// <see cref="RangerPrice"/> Ember Shards (the 1,200 pack covers it).</item>
    /// <item>Model (<c>Prefabs/Hero_ShadowRanger_Model.prefab</c>): "Model" root with the KAN-156 import of
    /// <c>SK_ShadowRanger.fbx</c> as "Body" (Generic, Mesh LODs), URP materials (body BaseColor + Normal, untextured parts
    /// from the Blender colours in Docs/ENEMY_ROSTER.md section 7), Animator <c>AC_ShadowRanger</c> on the rig node
    /// (<see cref="ClipRoot"/>: where the clip paths start; always animates) (Idle / Walking /
    /// Running blend on Speed, Shoot on bool Shooting with motion time ShootTime, Death on trigger Die), a
    /// <see cref="BowShootDriver"/> and the <c>Bow_Muzzle</c> socket on the bow hand.</item>
    /// <item>Shot (KAN-227): the Ranger's arrow (<see cref="CombatBuilder.ArrowPrefabPath"/>) and bow twang clips
    /// (<see cref="SfxBuilder.BowTwangName"/>) on his definition; refreshed like the model link.</item>
    /// <item><see cref="AddTo"/>: <see cref="HeroLoadout"/> on the Game scene player (one line in GameSceneBuilder).</item>
    /// </list>
    /// Tri budget: the Ranger is 7.8k tris LOD0 (body decimated to 6k in Tools/Meshy/build_shadow_ranger.py, KAN-227),
    /// inside the 5-8k hero budget (STATE.md DECISIONS).
    /// </summary>
    public static class HeroBuilder
    {
        public const string DataDir = "Assets/_Project/Data/Heroes";
        public const string RosterPath = DataDir + "/HeroRoster.asset";
        public const string KnightId = "knight";
        public const string RangerId = "shadow_ranger";
        public const int RangerPrice = 1000;
        /// <summary>
        /// Aim turn per hero (KAN-231): a critically damped spring on the yaw (max deg/s, smooth-damp time s). Its peak
        /// speed grows with the angle: a 180 deg retarget starts fast, a small correction is short and soft. To the 15 deg
        /// fire window: Ranger 180 deg ~0.23 s / 30 deg ~0.09 s; Knight (heavier) 180 deg ~0.31 s / 30 deg ~0.11 s.
        /// Builder-owned (written on every build). KAN-229 had the Ranger at 360 / 0.16 (~0.6 s): far too slow.
        /// </summary>
        public const float RangerTurnSpeed = 1200f, RangerTurnSmoothing = 0.09f;
        public const float KnightTurnSpeed = 840f, KnightTurnSmoothing = 0.12f;
        public const string RangerModelPath = "Assets/_Project/Prefabs/Hero_ShadowRanger_Model.prefab";
        private const string RangerArt = HeroPetArtImporter.HeroesArt + "/ShadowRanger";
        private const string GeneratedDir = "Assets/_Project/Art/Characters/HeroBuilds/ShadowRanger";
        public const string RangerControllerPath = GeneratedDir + "/AC_ShadowRanger.controller";
        private const string PortraitDir = "Assets/_Project/Art/UI/Heroes";
        private const float WalkSpeed = 1.6f, RunSpeed = 5f; // same thresholds as the Knight (MeshyCharacterImporter)
        private const string MuzzleBone = "LeftHand"; // the bow hand: arrows leave the bow

        /// <summary>Ranger profile against the Knight: fast and fragile.</summary>
        public static readonly StatModifier[] RangerProfile =
        {
            new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, 0.30f),
            new StatModifier(StatId.MoveSpeed, ModifierKind.Percent, 0.18f),
            new StatModifier(StatId.MaxHealth, ModifierKind.Percent, -0.25f),
            new StatModifier(StatId.Damage, ModifierKind.Percent, -0.20f),
        };

        /// <summary>Roster with both heroes; missing assets are made, existing ones keep their numbers.</summary>
        [MenuItem("Survivor Souls/Characters/Build Hero Roster")]
        public static HeroRoster LoadOrCreateRoster()
        {
            EditorAssetUtil.EnsureFolder(DataDir);
            var knight = LoadOrCreateHero(KnightId, "The Knight", HeroCurrency.Free, 0, System.Array.Empty<StatModifier>(),
                "Steel, crossbow, iron will.", new[] { "Full health", "Heavy bolts" }, new[] { "Slow reload", "Heavy step" },
                new Color(0.78f, 0.8f, 0.86f));
            var ranger = LoadOrCreateHero(RangerId, "The Shadow Ranger", HeroCurrency.EmberShards, RangerPrice, RangerProfile,
                "A bow from the hollow woods.", new[] { "+30% attack speed", "+18% move speed" },
                new[] { "-25% max health", "-20% arrow damage" }, new Color(0.55f, 0.78f, 0.62f));
            var model = BuildRangerModel();
            if (ranger.ModelPrefab != model)
            {
                ranger.SetModel(model);
                EditorUtility.SetDirty(ranger);
            }
            SetTurn(ranger, RangerTurnSpeed, RangerTurnSmoothing); // KAN-231 angle-aware spring turn
            SetTurn(knight, KnightTurnSpeed, KnightTurnSmoothing);
            var arrowAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CombatBuilder.ArrowPrefabPath);
            var arrow = arrowAsset != null ? arrowAsset.GetComponent<Bolt>() : null;
            var twangs = LoadTwangs();
            if (arrow != null && (ranger.Projectile != arrow || !ranger.ShotClips.SequenceEqual(twangs)))
            {
                ranger.SetShot(arrow, twangs); // KAN-227
                EditorUtility.SetDirty(ranger);
            }

            var roster = AssetDatabase.LoadAssetAtPath<HeroRoster>(RosterPath);
            if (roster == null)
            {
                roster = HeroRoster.Create(knight, ranger);
                AssetDatabase.CreateAsset(roster, RosterPath);
            }
            else if (!roster.Contains(knight) || !roster.Contains(ranger))
            {
                var list = roster.Heroes.Where(h => h != null).ToList();
                if (!list.Contains(knight)) list.Insert(0, knight);
                if (!list.Contains(ranger)) list.Add(ranger);
                roster.SetHeroes(list.ToArray());
                EditorUtility.SetDirty(roster);
            }
            AssetDatabase.SaveAssets();
            return roster;
        }

        private static void SetTurn(HeroDefinition hero, float speed, float smoothing)
        {
            if (Mathf.Approximately(hero.AimTurnSpeed, speed) && Mathf.Approximately(hero.AimTurnSmoothing, smoothing)) return;
            hero.SetAimTurn(speed, smoothing);
            EditorUtility.SetDirty(hero);
        }

        /// <summary>The Ranger's bow twang clips (SfxBuilder writes them; missing files are skipped).</summary>
        private static AudioClip[] LoadTwangs() =>
            Enumerable.Range(1, 3)
                .Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxBuilder.Dir}/{SfxBuilder.BowTwangName}_{i:00}.wav"))
                .Where(c => c != null).ToArray();

        /// <summary>Game scene: the hero loadout on the player, before the soul tree applies (KAN-153).</summary>
        public static HeroLoadout AddTo(GameObject player, PlayerStats stats)
        {
            var loadout = player.AddComponent<HeroLoadout>();
            var so = new SerializedObject(loadout);
            so.FindProperty("roster").objectReferenceValue = LoadOrCreateRoster();
            so.FindProperty("stats").objectReferenceValue = stats;
            so.ApplyModifiedPropertiesWithoutUndo();
            return loadout;
        }

        private static HeroDefinition LoadOrCreateHero(string id, string name, HeroCurrency currency, int price, StatModifier[] profile,
            string tagline, string[] pros, string[] cons, Color accent)
        {
            string path = $"{DataDir}/Hero_{id}.asset";
            var portrait = ImportPortrait($"{PortraitDir}/T_Hero_{(id == RangerId ? "ShadowRanger" : "Knight")}_Portrait.png");
            var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>(path);
            if (hero == null)
            {
                hero = HeroDefinition.Create(id, name, currency, price, profile);
                hero.Describe(tagline, pros, cons, accent, portrait);
                AssetDatabase.CreateAsset(hero, path);
            }
            else if (hero.Portrait != portrait && portrait != null)
            {
                hero.Describe(hero.Tagline, hero.Pros, hero.Cons, hero.Accent, portrait);
                EditorUtility.SetDirty(hero);
            }
            return hero;
        }

        /// <summary>UI portrait import: no mips, clamp, small (one per hero card).</summary>
        private static Texture2D ImportPortrait(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && (importer.mipmapEnabled || importer.maxTextureSize != 512 || importer.wrapMode != TextureWrapMode.Clamp
                    || importer.npotScale != TextureImporterNPOTScale.None))
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 512;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogWarning($"[HeroBuilder] Portrait missing: {path}");
            return texture;
        }

        // ------------------------------------------------------------ Shadow Ranger model

        /// <summary>The Ranger "Model" prefab (null when the art is missing).</summary>
        public static GameObject BuildRangerModel()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{RangerArt}/SK_ShadowRanger.fbx");
            if (fbx == null)
            {
                Debug.LogWarning("[HeroBuilder] SK_ShadowRanger.fbx missing: no Ranger model");
                return null;
            }
            EditorAssetUtil.EnsureFolder(GeneratedDir);
            var clips = LoadClips();
            var materials = BuildMaterials();

            var root = new GameObject(HeroLoadout.ModelName);
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                int tris = 0;
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials = MaterialsFor(r, materials);
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    if (r is SkinnedMeshRenderer smr)
                    {
                        smr.quality = SkinQuality.Bone4;
                        if (smr.sharedMesh != null) tris += TriCount(smr.sharedMesh);
                    }
                }

                // KAN-227: the clip files hold the armature alone, so their curve paths start below the rig node
                // ("Hips/..."), while SK_ puts the rig under the FBX root ("ShadowRanger_Rig/Hips/..."). On the FBX root
                // the Animator bound 0 of 280 curves and the Ranger stood in the rest pose (KAN-153 to KAN-227).
                var rig = ClipRoot(body.transform, clips.Values);
                var animator = rig.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = BuildController(clips);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // the renderers are not under the rig node
                var driver = new SerializedObject(root.AddComponent<BowShootDriver>());
                driver.FindProperty("animator").objectReferenceValue = animator;
                driver.ApplyModifiedPropertiesWithoutUndo();

                var hand = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == MuzzleBone);
                if (hand == null) throw new System.InvalidOperationException($"[HeroBuilder] Ranger rig has no '{MuzzleBone}' bone");
                var muzzle = new GameObject(HeroLoadout.MuzzleName).transform;
                muzzle.SetParent(hand, false);

                var bounds = new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);
                EditorAssetUtil.EnsureFolder("Assets/_Project/Prefabs");
                StableAssetWriter.Remember(RangerModelPath);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, RangerModelPath);
                Debug.Log($"[HeroBuilder] Shadow Ranger model: {tris} tris LOD0 (hero budget 5-8k), bounds {bounds.size}, " +
                          $"clips {string.Join(" ", clips.Select(kv => $"{kv.Key}={kv.Value.length:F2}s"))} -> {RangerModelPath}");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The transform the clip curve paths start from: the first one (breadth first under <paramref name="body"/>)
        /// where every path of every clip resolves. Throws when none does (the Animator would bind nothing).
        /// </summary>
        public static Transform ClipRoot(Transform body, IEnumerable<AnimationClip> clips)
        {
            var paths = clips.SelectMany(AnimationUtility.GetCurveBindings).Select(b => b.path).Where(p => p.Length > 0)
                .Distinct().ToArray();
            var queue = new Queue<Transform>();
            queue.Enqueue(body);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (paths.All(p => t.Find(p) != null)) return t;
                foreach (Transform child in t) queue.Enqueue(child);
            }
            throw new System.InvalidOperationException($"[HeroBuilder] no transform under '{body.name}' matches the clip paths (first '{paths.FirstOrDefault()}')");
        }

        private static int TriCount(Mesh mesh)
        {
            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)mesh.GetIndexCount(i) / 3;
            return tris;
        }

        private static Dictionary<string, AnimationClip> LoadClips()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var name in new[] { "Idle", "Walking", "Running", "Shoot", "Death" })
            {
                string path = $"{RangerArt}/A_ShadowRanger_{name}.fbx";
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) throw new System.InvalidOperationException($"[HeroBuilder] clip missing: {path}");
                clips[name] = clip;
            }
            return clips;
        }

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            StableAssetWriter.Remember(RangerControllerPath); // new fileIDs every rebuild: keep the old file when nothing changed (KAN-157)
            AssetDatabase.DeleteAsset(RangerControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(RangerControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger); // no hit clip yet: kept so PlayHit never warns
            ctrl.AddParameter("Shooting", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("ShootTime", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips["Walking"], WalkSpeed);
            tree.AddChild(clips["Running"], RunSpeed);

            var sm = ctrl.layers[0].stateMachine;
            sm.defaultState = loco;
            var shoot = sm.AddState("Shoot");
            shoot.motion = clips["Shoot"];
            shoot.timeParameterActive = true; // BowShootDriver sets the clip time from the shooter clock
            shoot.timeParameter = "ShootTime";
            var toShoot = loco.AddTransition(shoot);
            toShoot.hasExitTime = false;
            toShoot.duration = 0.12f;
            toShoot.AddCondition(AnimatorConditionMode.If, 0f, "Shooting");
            var back = shoot.AddTransition(loco);
            back.hasExitTime = false;
            back.duration = 0.15f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0f, "Shooting");

            var death = sm.AddState("Death");
            death.motion = clips["Death"];
            var toDeath = sm.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            toDeath.duration = 0.1f;
            toDeath.canTransitionToSelf = false;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        // Material slots per mesh object, in the Blender slot order (ShadowRanger.blend).
        private static readonly Dictionary<string, string[]> Slots = new Dictionary<string, string[]>
        {
            { "ShadowRanger_Body", new[] { "Body" } },
            { "ShadowRanger_Mask", new[] { "Cloth" } },
            { "ShadowRanger_Bow", new[] { "Bow", "Grip" } },
            { "ShadowRanger_String", new[] { "String" } },
            { "ShadowRanger_Arrow", new[] { "Arrow", "Steel", "Bow" } },
        };

        private static Material[] MaterialsFor(Renderer r, Dictionary<string, Material> materials)
        {
            int count = r is SkinnedMeshRenderer s && s.sharedMesh != null ? s.sharedMesh.subMeshCount
                : r.GetComponent<MeshFilter>()?.sharedMesh != null ? r.GetComponent<MeshFilter>().sharedMesh.subMeshCount : 1;
            var names = Slots.TryGetValue(r.name, out var n) ? n : new[] { "Cloth" };
            if (!Slots.ContainsKey(r.name)) Debug.LogWarning($"[HeroBuilder] Unknown Ranger mesh '{r.name}': cloth material");
            var result = new Material[Mathf.Max(1, count)];
            for (int i = 0; i < result.Length; i++) result[i] = materials[names[Mathf.Min(i, names.Length - 1)]];
            return result;
        }

        private static Dictionary<string, Material> BuildMaterials()
        {
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangerArt}/T_ShadowRanger_BaseColor.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{RangerArt}/T_ShadowRanger_Normal.png");
            var body = EditorAssetUtil.LoadOrCreateMaterial($"{GeneratedDir}/M_ShadowRanger_Body.mat", "Universal Render Pipeline/Lit");
            body.SetTexture("_BaseMap", albedo);
            body.SetColor("_BaseColor", Color.white);
            body.SetTexture("_BumpMap", normal);
            body.SetFloat("_BumpScale", 1f);
            if (normal != null) body.EnableKeyword("_NORMALMAP");
            body.SetFloat("_Metallic", 0f);
            body.SetFloat("_Smoothness", 0.3f);
            body.SetFloat("_EnvironmentReflections", 0f); // no reflection probes in the arena
            body.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorAssetUtil.ValidateLikeInspector(body);
            return new Dictionary<string, Material>
            {
                { "Body", body },
                // Untextured parts: colours from Docs/ENEMY_ROSTER.md section 7 (Blender build).
                { "Bow", Plain("Bow", new Color(0.030f, 0.026f, 0.024f), 0.62f, 0f) },
                { "Grip", Plain("Grip", new Color(0.11f, 0.065f, 0.035f), 0.3f, 0f) },
                { "String", Plain("String", new Color(0.55f, 0.52f, 0.45f), 0.25f, 0f) },
                { "Cloth", Plain("Cloth", new Color(0.022f, 0.021f, 0.022f), 0.15f, 0f) },
                { "Arrow", Plain("Arrow", new Color(0.06f, 0.05f, 0.045f), 0.3f, 0f) },
                { "Steel", Plain("Steel", new Color(0.35f, 0.36f, 0.38f), 0.6f, 0.9f) },
            };
        }

        private static Material Plain(string name, Color color, float smoothness, float metallic)
        {
            var m = EditorAssetUtil.LoadOrCreateMaterial($"{GeneratedDir}/M_ShadowRanger_{name}.mat", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorAssetUtil.ValidateLikeInspector(m);
            return m;
        }
    }
}
