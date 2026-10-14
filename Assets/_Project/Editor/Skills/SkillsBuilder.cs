using System;
using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Builds the skill system (KAN-19): rune icons, the 12 skill assets, the level table, the three behaviour-skill effect
    /// prefabs (burn embers, life steal motes, soul burst ring + sparks) and the scene <see cref="SkillManager"/>.
    /// Skill and table assets are created only when missing: designer edits are never overwritten (missing icon / effect
    /// links are filled in). Icons and effect prefabs are rebuilt every run. Called by GameSceneBuilder.
    /// Tiers (KAN-37): Common = plain stat boosts, Rare = crit / pierce / ricochet, Epic = life steal / multishot,
    /// Legendary = burning bolts / soul burst. <see cref="BuildStyles"/> makes the per-tier style asset (labels, gems, sounds).
    /// </summary>
    public static class SkillsBuilder
    {
        public const string DataDir = "Assets/_Project/Data/Skills";
        public const string ProgressionPath = DataDir + "/SkillProgression_Run.asset";
        public const string StylesPath = DataDir + "/SkillRarityStyles.asset";
        private const string IconDir = "Assets/_Project/Art/Skills/Icons";
        private const string FxDir = "Assets/_Project/Art/Skills";
        private const string PrefabDir = "Assets/_Project/Prefabs/Skills";
        private const string RingTexturePath = "Assets/_Project/Art/Textures/Game/T_Ring.png"; // made by GameArtKit

        private enum Fx { None, Burn, LifeSteal, KillBurst }

        private sealed class Spec
        {
            public string Id, Name, File;
            public SkillRarity Rarity;
            public float Weight = 1f;
            /// <summary>Level table (KAN-71): row N = total effect at level N. Row 1 is also the card text and stacking fallback.</summary>
            public SkillLevelData[] Levels;
            public Func<Vector2, float> Glyph;
            public Fx Effect;

            public string Text => Levels[0].description;
            public StatModifier[] Mods => Levels[0].modifiers;
            public int Max => Levels.Length;
        }

        private static StatModifier Pct(StatId s, float v) => new StatModifier(s, ModifierKind.Percent, v);
        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);
        private static SkillLevelData L(string text, params StatModifier[] mods) => new SkillLevelData(text, mods);

        // Base numbers for reference: bolt 100 dmg / 1 shot per s, no crit / x2 crit damage, zombie 150 HP (Shambler 100),
        // player 400 HP at 5 m/s, pickup 2.6 m, burn 3 s, soul burst 2.6 m.
        // Level tables (KAN-71, balance sheet in Confluence SS > 01 Product & Game Design): each row is the TOTAL at that
        // level. Common = one stat, shrinking steps, up to 6 levels (5 where the stat saturates: move, pickup). Rare / Epic
        // = 3-5 levels, the last level adds a second stat. Legendary = 4 levels, level 3 adds reach (burn length, burst
        // radius). Level 1 keeps the KAN-19 numbers, so the first pick feels the same as before.
        private static readonly Spec[] Specs =
        {
            new Spec { Id = "sharpened_bolts", File = "SharpenedBolts", Name = "Sharpened Bolts", Rarity = SkillRarity.Common,
                Glyph = SkillIconPainter.Damage, Levels = new[]
                {
                    L("+20% bolt damage", Pct(StatId.Damage, 0.2f)),
                    L("+36% bolt damage", Pct(StatId.Damage, 0.36f)),
                    L("+52% bolt damage", Pct(StatId.Damage, 0.52f)),
                    L("+66% bolt damage", Pct(StatId.Damage, 0.66f)),
                    L("+80% bolt damage", Pct(StatId.Damage, 0.8f)),
                    L("+95% bolt damage", Pct(StatId.Damage, 0.95f)),
                } },
            new Spec { Id = "quick_draw", File = "QuickDraw", Name = "Quick Draw", Rarity = SkillRarity.Common,
                Glyph = SkillIconPainter.AttackSpeed, Levels = new[]
                {
                    L("+15% attack speed", Pct(StatId.AttackSpeed, 0.15f)),
                    L("+28% attack speed", Pct(StatId.AttackSpeed, 0.28f)),
                    L("+40% attack speed", Pct(StatId.AttackSpeed, 0.4f)),
                    L("+51% attack speed", Pct(StatId.AttackSpeed, 0.51f)),
                    L("+62% attack speed", Pct(StatId.AttackSpeed, 0.62f)),
                    L("+72% attack speed", Pct(StatId.AttackSpeed, 0.72f)),
                } },
            new Spec { Id = "fleet_foot", File = "FleetFoot", Name = "Fleet Foot", Rarity = SkillRarity.Common,
                Glyph = SkillIconPainter.MoveSpeed, Levels = new[]
                {
                    L("+10% move speed", Pct(StatId.MoveSpeed, 0.1f)),
                    L("+18% move speed", Pct(StatId.MoveSpeed, 0.18f)),
                    L("+24% move speed", Pct(StatId.MoveSpeed, 0.24f)),
                    L("+29% move speed", Pct(StatId.MoveSpeed, 0.29f)),
                    L("+33% move speed", Pct(StatId.MoveSpeed, 0.33f)),
                } },
            new Spec { Id = "vigor", File = "Vigor", Name = "Vigor", Rarity = SkillRarity.Common,
                Glyph = SkillIconPainter.MaxHealth, Levels = new[]
                {
                    L("+80 max health, healed at once", Add(StatId.MaxHealth, 80f)),
                    L("+150 max health, healed at once", Add(StatId.MaxHealth, 150f)),
                    L("+215 max health, healed at once", Add(StatId.MaxHealth, 215f)),
                    L("+275 max health, healed at once", Add(StatId.MaxHealth, 275f)),
                    L("+330 max health, healed at once", Add(StatId.MaxHealth, 330f)),
                    L("+380 max health, healed at once", Add(StatId.MaxHealth, 380f)),
                } },
            new Spec { Id = "soul_siphon", File = "SoulSiphon", Name = "Soul Siphon", Rarity = SkillRarity.Common, Weight = 0.7f,
                Glyph = SkillIconPainter.PickupRadius, Levels = new[]
                {
                    L("+40% soul pickup range", Pct(StatId.PickupRadius, 0.4f)),
                    L("+70% soul pickup range", Pct(StatId.PickupRadius, 0.7f)),
                    L("+95% soul pickup range", Pct(StatId.PickupRadius, 0.95f)),
                    L("+115% soul pickup range", Pct(StatId.PickupRadius, 1.15f)),
                    L("+130% soul pickup range", Pct(StatId.PickupRadius, 1.3f)),
                } },
            new Spec { Id = "keen_eye", File = "KeenEye", Name = "Keen Eye", Rarity = SkillRarity.Rare,
                Glyph = SkillIconPainter.Crit, Levels = new[]
                {
                    L("+10% crit chance, +25% crit damage", Add(StatId.CritChance, 0.1f), Add(StatId.CritDamage, 0.25f)),
                    L("+15% crit chance, +40% crit damage", Add(StatId.CritChance, 0.15f), Add(StatId.CritDamage, 0.4f)),
                    L("+20% crit chance, +55% crit damage", Add(StatId.CritChance, 0.2f), Add(StatId.CritDamage, 0.55f)),
                    L("+25% crit chance, +70% crit damage", Add(StatId.CritChance, 0.25f), Add(StatId.CritDamage, 0.7f)),
                    L("+30% crit chance, +90% crit damage", Add(StatId.CritChance, 0.3f), Add(StatId.CritDamage, 0.9f)),
                } },
            new Spec { Id = "piercing_bolts", File = "PiercingBolts", Name = "Piercing Bolts", Rarity = SkillRarity.Rare,
                Glyph = SkillIconPainter.Pierce, Levels = new[]
                {
                    L("Bolts pass through +1 enemy", Add(StatId.Pierce, 1f)),
                    L("Bolts pass through +2 enemies", Add(StatId.Pierce, 2f)),
                    L("Pierce +2 enemies, +10% damage", Add(StatId.Pierce, 2f), Pct(StatId.Damage, 0.1f)),
                    L("Pierce +3 enemies, +15% damage", Add(StatId.Pierce, 3f), Pct(StatId.Damage, 0.15f)),
                } },
            new Spec { Id = "ricochet", File = "Ricochet", Name = "Ricochet", Rarity = SkillRarity.Rare,
                Glyph = SkillIconPainter.Ricochet, Levels = new[]
                {
                    L("Bolts bounce to +1 nearby enemy", Add(StatId.Ricochet, 1f)),
                    L("Bolts bounce to +2 nearby enemies", Add(StatId.Ricochet, 2f)),
                    L("Bolts bounce to +3 nearby enemies", Add(StatId.Ricochet, 3f)),
                    L("Bounce to +3 enemies, +15% damage", Add(StatId.Ricochet, 3f), Pct(StatId.Damage, 0.15f)),
                } },
            new Spec { Id = "ember_bolts", File = "EmberBolts", Name = "Ember Bolts", Rarity = SkillRarity.Legendary,
                Glyph = SkillIconPainter.Burn, Effect = Fx.Burn, Levels = new[]
                {
                    L("Hits burn: 25 fire damage/s for 3 s", Add(StatId.BurnDps, 25f)),
                    L("Hits burn: 40 fire damage/s for 3 s", Add(StatId.BurnDps, 40f)),
                    L("Hits burn: 55 fire damage/s for 4 s", Add(StatId.BurnDps, 55f), Add(StatId.BurnDuration, 1f)),
                    L("Hits burn: 75 fire damage/s for 5 s", Add(StatId.BurnDps, 75f), Add(StatId.BurnDuration, 2f)),
                } },
            new Spec { Id = "blood_covenant", File = "BloodCovenant", Name = "Blood Covenant", Rarity = SkillRarity.Epic,
                Glyph = SkillIconPainter.LifeSteal, Effect = Fx.LifeSteal, Levels = new[]
                {
                    L("Heal 5% of bolt damage dealt", Add(StatId.LifeSteal, 0.05f)),
                    L("Heal 7% of bolt damage dealt", Add(StatId.LifeSteal, 0.07f)),
                    L("Heal 9% of bolt damage dealt", Add(StatId.LifeSteal, 0.09f)),
                    L("Heal 12% of damage, +60 max HP", Add(StatId.LifeSteal, 0.12f), Add(StatId.MaxHealth, 60f)),
                } },
            new Spec { Id = "multishot", File = "Multishot", Name = "Multishot", Rarity = SkillRarity.Epic,
                Glyph = SkillIconPainter.Multishot, Levels = new[]
                {
                    L("+1 bolt per volley, -15% damage", Add(StatId.Multishot, 1f), Pct(StatId.Damage, -0.15f)),
                    L("+2 bolts per volley, -25% damage", Add(StatId.Multishot, 2f), Pct(StatId.Damage, -0.25f)),
                    L("+2 bolts per volley, -10% damage", Add(StatId.Multishot, 2f), Pct(StatId.Damage, -0.1f)),
                } },
            new Spec { Id = "soul_burst", File = "SoulBurst", Name = "Soul Burst", Rarity = SkillRarity.Legendary,
                Glyph = SkillIconPainter.KillBurst, Effect = Fx.KillBurst, Levels = new[]
                {
                    L("Kills burst: 50% damage in 2.6 m", Add(StatId.KillBurst, 0.5f)),
                    L("Kills burst: 70% damage in 2.6 m", Add(StatId.KillBurst, 0.7f)),
                    L("Kills burst: 90% damage in 3.2 m", Add(StatId.KillBurst, 0.9f), Add(StatId.BurstRadius, 0.6f)),
                    L("Kills burst: 120% damage in 3.6 m", Add(StatId.KillBurst, 1.2f), Add(StatId.BurstRadius, 1f)),
                } },
        };

        /// <summary>Builds the assets and adds the SkillManager (plus PlayerStats on the player when missing).</summary>
        public static SkillManager Create(GameArtKit kit, GameObject host, GameObject player, AutoShooter shooter, PlayerHealth health)
        {
            var progression = BuildAssets(kit);
            if (!player.TryGetComponent<PlayerStats>(out var stats)) stats = player.AddComponent<PlayerStats>();
            var manager = host.AddComponent<SkillManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("progression").objectReferenceValue = progression;
            so.FindProperty("stats").objectReferenceValue = stats;
            so.FindProperty("shooter").objectReferenceValue = shooter;
            so.FindProperty("health").objectReferenceValue = health;
            so.FindProperty("effectRoot").objectReferenceValue = player.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        [MenuItem("Survivor Souls/Rebuild Skill Assets")]
        private static void BuildAssetsMenu()
        {
            BuildAssets(GameArtKit.Build());
            BuildStyles(Audio.SfxBuilder.Build().RarityReveals);
        }

        /// <summary>
        /// Per-tier style asset (KAN-37). Created with the built-in table when missing; an existing asset keeps its labels,
        /// gems, volumes and flags, only empty reveal sound slots are filled with <paramref name="reveals"/>.
        /// </summary>
        public static SkillRarityStyles BuildStyles(AudioClip[] reveals)
        {
            EditorAssetUtil.EnsureFolder(DataDir);
            var styles = AssetDatabase.LoadAssetAtPath<SkillRarityStyles>(StylesPath);
            if (styles == null)
            {
                styles = ScriptableObject.CreateInstance<SkillRarityStyles>();
                styles.SetReveals(reveals);
                AssetDatabase.CreateAsset(styles, StylesPath);
            }
            else
            {
                var so = new SerializedObject(styles);
                var list = so.FindProperty("styles");
                for (int i = 0; i < list.arraySize && reveals != null && i < reveals.Length; i++)
                {
                    var clip = list.GetArrayElementAtIndex(i).FindPropertyRelative("reveal");
                    if (clip.objectReferenceValue == null) clip.objectReferenceValue = reveals[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            return styles;
        }

        public static SkillProgression BuildAssets(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(DataDir);
            EditorAssetUtil.EnsureFolder(IconDir);
            EditorAssetUtil.EnsureFolder(PrefabDir);
            var effects = BuildEffects(kit);
            var byId = new Dictionary<string, SkillDefinition>();
            foreach (var spec in Specs)
            {
                var icon = SkillIconPainter.Write($"{IconDir}/T_Skill_{spec.File}.png", spec.Glyph);
                effects.TryGetValue(spec.Effect, out var effect);
                byId[spec.Id] = LoadOrCreate(spec, icon, effect);
            }
            var table = BuildProgression(byId);
            AssetDatabase.SaveAssets();
            return table;
        }

        private static SkillDefinition LoadOrCreate(Spec spec, Texture2D icon, SkillEffect effect)
        {
            string path = $"{DataDir}/Skill_{spec.File}.asset";
            var skill = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);
            if (skill == null)
            {
                skill = ScriptableObject.CreateInstance<SkillDefinition>();
                skill.Configure(spec.Id, spec.Name, spec.Text, spec.Rarity, spec.Max, spec.Mods);
                skill.SetLevels(spec.Levels);
                skill.SetPresentation(icon, effect, spec.Weight);
                AssetDatabase.CreateAsset(skill, path);
                return skill;
            }
            // Existing asset: keep designer numbers, only repair missing links and give a table-less skill its table.
            if (!skill.HasLevelTable) FillLevels(skill, spec);
            var so = new SerializedObject(skill);
            var iconProp = so.FindProperty("icon");
            if (iconProp.objectReferenceValue == null) iconProp.objectReferenceValue = icon;
            var fxProp = so.FindProperty("effectPrefab");
            if (fxProp.objectReferenceValue == null && effect != null) fxProp.objectReferenceValue = effect;
            so.ApplyModifiedPropertiesWithoutUndo();
            return skill;
        }

        /// <summary>
        /// One-time move of a KAN-19 asset (old stacking, empty table) to its level table (KAN-71). The fallback fields
        /// (card text, modifiers, max stack) are set to row 1 / the table length so every view of the skill agrees. An
        /// asset that already has a table is never touched: tune it in the Inspector.
        /// </summary>
        private static void FillLevels(SkillDefinition skill, Spec spec)
        {
            skill.Configure(skill.Id, skill.DisplayName, spec.Text, skill.Rarity, spec.Max, spec.Mods);
            skill.SetLevels(spec.Levels);
            EditorUtility.SetDirty(skill);
        }

        /// <summary>
        /// Run pacing (XP curve: levels 2-4 come every few kills). Stat skills first, behaviour skills from level 4, epic
        /// game-changers from level 6. Level 4 always offers Ember Bolts (the first Legendary), level 6 Multishot.
        /// Tier odds (KAN-37) grow with the level: 70/27/3/0 at L2, 55/30/12/3 at L4, 45/32/16/7 at L6, 38/32/20/10 from L10.
        /// Pity: at least one Rare+ card in every 3 offers.
        /// </summary>
        private static SkillProgression BuildProgression(Dictionary<string, SkillDefinition> s)
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            if (table != null) return table;
            table = ScriptableObject.CreateInstance<SkillProgression>();
            table.Configure(
                new[]
                {
                    new SkillProgression.Tier
                    {
                        fromLevel = 2, common = 70f, rare = 27f, epic = 3f, legendary = 0f,
                        unlocks = new[] { s["sharpened_bolts"], s["quick_draw"], s["fleet_foot"], s["vigor"], s["soul_siphon"], s["keen_eye"], s["piercing_bolts"] },
                    },
                    new SkillProgression.Tier
                    {
                        fromLevel = 4, common = 55f, rare = 30f, epic = 12f, legendary = 3f,
                        unlocks = new[] { s["ricochet"], s["ember_bolts"], s["blood_covenant"] },
                    },
                    new SkillProgression.Tier
                    {
                        fromLevel = 6, common = 45f, rare = 32f, epic = 16f, legendary = 7f,
                        unlocks = new[] { s["multishot"], s["soul_burst"] },
                    },
                    new SkillProgression.Tier
                    {
                        fromLevel = 10, common = 38f, rare = 32f, epic = 20f, legendary = 10f,
                        unlocks = new SkillDefinition[0],
                    },
                },
                new[]
                {
                    new SkillProgression.Guarantee { level = 4, skill = s["ember_bolts"] },
                    new SkillProgression.Guarantee { level = 6, skill = s["multishot"] },
                });
            AssetDatabase.CreateAsset(table, ProgressionPath);
            return table;
        }

        // ------------------------------------------------------------ Effect prefabs

        private static Dictionary<Fx, SkillEffect> BuildEffects(GameArtKit kit)
        {
            EditorAssetUtil.EnsureFolder(FxDir);
            var ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(RingTexturePath);
            if (ringTex == null) throw new InvalidOperationException($"Missing {RingTexturePath} (GameArtKit builds it).");
            var ringMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_SoulBurstRing.mat", ringTex, additive: true);
            var glowMat = EditorAssetUtil.ParticleMaterial(FxDir + "/M_FX_SkillGlow.mat", kit.SoftDot, additive: true);

            return new Dictionary<Fx, SkillEffect>
            {
                [Fx.Burn] = SavePrefab<BurnOnHitEffect>("FX_Skill_EmberBolts", (root, fx) =>
                {
                    var embers = Particles(root, "Embers", glowMat, 260, new Vector2(0.45f, 0.85f), new Vector2(0.06f, 0.13f),
                        new Color(1f, 0.62f, 0.2f), new Color(1f, 0.32f, 0.08f));
                    Drag(embers, 1.5f);
                    Set(fx, "embers", embers);
                }),
                [Fx.LifeSteal] = SavePrefab<LifeStealEffect>("FX_Skill_BloodCovenant", (root, fx) =>
                {
                    var motes = Particles(root, "Blood Motes", glowMat, 40, new Vector2(0.45f, 0.7f), new Vector2(0.1f, 0.18f),
                        new Color(1f, 0.25f, 0.3f), new Color(0.85f, 0.08f, 0.15f));
                    Drag(motes, 3f);
                    Set(fx, "motes", motes);
                }),
                [Fx.KillBurst] = SavePrefab<KillBurstEffect>("FX_Skill_SoulBurst", (root, fx) =>
                {
                    var ring = Particles(root, "Burst Ring", ringMat, 24, new Vector2(0.38f, 0.38f), new Vector2(1f, 1f),
                        new Color(0.72f, 0.9f, 1f, 0.95f), new Color(0.72f, 0.9f, 1f, 0.95f));
                    ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    var size = ring.sizeOverLifetime;
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.25f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f)));
                    var sparks = Particles(root, "Burst Sparks", glowMat, 320, new Vector2(0.3f, 0.5f), new Vector2(0.07f, 0.12f),
                        new Color(0.8f, 0.94f, 1f), new Color(0.55f, 0.78f, 1f));
                    var sr = sparks.GetComponent<ParticleSystemRenderer>();
                    sr.renderMode = ParticleSystemRenderMode.Stretch;
                    sr.velocityScale = 0.06f;
                    sr.lengthScale = 1.4f;
                    Drag(sparks, 5f);
                    var main = sparks.main;
                    main.gravityModifier = 0.6f;
                    Set(fx, "ring", ring);
                    Set(fx, "sparks", sparks);
                }),
            };
        }

        private static SkillEffect SavePrefab<T>(string name, Action<GameObject, T> build) where T : SkillEffect
        {
            var root = new GameObject(name);
            try
            {
                var fx = root.AddComponent<T>();
                build(root, fx);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
                return prefab.GetComponent<T>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Script-fed world-space particles (Emit only): fade out, shrink, no lights, shadows or probes.</summary>
        private static ParticleSystem Particles(GameObject parent, string name, Material mat, int max, Vector2 life, Vector2 size,
            Color a, Color b)
        {
            var ps = PropKit.Particles(name, parent.transform, mat);
            var main = ps.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = true;
            main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.1f), (0.7f, 0.55f), (0f, 1f) });
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        private static void Drag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
        }

        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
