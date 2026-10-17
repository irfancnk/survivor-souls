using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// KAN-246 Ember Boons: the 33-skill catalog of <c>Docs/skills/ember-boons.json</c>. 14 catalog skills are the shipped
    /// ones (new art, some a new tier or name: see the Specs comments in SkillsBuilder.cs), 19 are new (<see cref="BoonSpecs"/>,
    /// 3 levels each from the JSON <c>upgrade_direction</c>; numbers are first proposals for playtesting). Art: the 512x683
    /// T_Boon_* portraits in <see cref="BoonDir"/> (<see cref="Catalog"/> maps skill id to art; mind the trap: id "multishot"
    /// is Spread Shot, id "double_shot" is Multishot). <see cref="ApplyCatalog"/> moves the shipped assets once (art, tier,
    /// the burn-only Ember Bolts table, the Eclipse Crown gate) and never touches a value a designer changed since.
    /// New skills join the run table by tier: Rare from level 4, Epic from 6, Legendary from 10 (<see cref="BoonUnlocks"/>).
    /// </summary>
    public static partial class SkillsBuilder
    {
        public const string BoonDir = "Assets/_Project/Art/Skills/Boons";

        /// <summary>
        /// Batch rebuild of the skill assets only (no scene): <c>Unity -batchmode -quit -executeMethod
        /// SurvivorSouls.Editor.Skills.SkillsBuilder.BuildFromCommandLine</c>. The Game scene links the progression table and
        /// the skill assets by reference, so new skills need no scene rebuild.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            BuildAssets(Procedural.GameArtKit.Build());
            AssetDatabase.SaveAssets();
            Debug.Log("[SkillsBuilder] BuildFromCommandLine done");
        }

        private static IEnumerable<Spec> AllSpecs()
        {
            foreach (var s in Specs) yield return s;
            foreach (var s in BoonSpecs) yield return s;
        }

        /// <summary>Ember Bolts, burn only (KAN-246): 7 levels (KAN-220), damage and length grow.</summary>
        private static SkillLevelData[] EmberBoltsLevels => new[]
        {
            L("Bolts set foes ablaze, 25/s", Add(StatId.BurnDps, 25f)),
            L("Fire 40/s for 3 s", Add(StatId.BurnDps, 40f)),
            L("Fire 55/s for 4 s", Add(StatId.BurnDps, 55f), Add(StatId.BurnDuration, 1f)),
            L("Fire 75/s for 5 s", Add(StatId.BurnDps, 75f), Add(StatId.BurnDuration, 2f)),
            L("Fire 90/s for 5 s", Add(StatId.BurnDps, 90f), Add(StatId.BurnDuration, 2f)),
            L("Fire 105/s for 6 s", Add(StatId.BurnDps, 105f), Add(StatId.BurnDuration, 3f)),
            L("Fire 130/s for 6 s", Add(StatId.BurnDps, 130f), Add(StatId.BurnDuration, 3f)),
        };

        /// <summary>Catalog id → art file, tier now, tier before KAN-246 (only an asset still on the old tier moves).</summary>
        private static readonly (string id, string art, SkillRarity rarity, SkillRarity before)[] Catalog =
        {
            ("sharpened_bolts", "SharpenedBolts", SkillRarity.Common, SkillRarity.Common),
            ("quick_draw", "QuickDraw", SkillRarity.Common, SkillRarity.Common),
            ("vigor", "Vigor", SkillRarity.Common, SkillRarity.Common),
            ("fleet_foot", "FleetFoot", SkillRarity.Common, SkillRarity.Common),
            ("keen_eye", "KeenEye", SkillRarity.Rare, SkillRarity.Rare),
            ("piercing_bolts", "PiercingBolts", SkillRarity.Rare, SkillRarity.Rare),
            ("ember_bolts", "EmberBolts", SkillRarity.Rare, SkillRarity.Legendary),
            ("freeze_arrow", "FrostBolts", SkillRarity.Rare, SkillRarity.Rare),
            ("multishot", "SpreadShot", SkillRarity.Epic, SkillRarity.Rare),         // Spread Shot (id kept from KAN-83)
            ("double_shot", "Multishot", SkillRarity.Legendary, SkillRarity.Epic),   // Multishot (was Double Shot)
            ("soul_burst", "SoulBurst", SkillRarity.Epic, SkillRarity.Legendary),
            ("temporary_shield", "HallowedShield", SkillRarity.Epic, SkillRarity.Epic),
            ("holy_tomes", "HolyTomes", SkillRarity.Epic, SkillRarity.Rare),
            ("ricochet", "Ricochet", SkillRarity.Legendary, SkillRarity.Rare),
            ("venom_bolts", "VenomBolts", SkillRarity.Rare, SkillRarity.Rare),
            ("storm_touch", "StormTouch", SkillRarity.Rare, SkillRarity.Rare),
            ("ashen_heart", "AshenHeart", SkillRarity.Rare, SkillRarity.Rare),
            ("repelling_bolts", "RepellingBolts", SkillRarity.Rare, SkillRarity.Rare),
            ("stormshock", "Stormshock", SkillRarity.Epic, SkillRarity.Epic),
            ("front_arrow", "FrontArrow", SkillRarity.Epic, SkillRarity.Epic),
            ("bouncy_wall", "BouncyWall", SkillRarity.Epic, SkillRarity.Epic),
            ("rear_arrow", "RearArrow", SkillRarity.Epic, SkillRarity.Epic),
            ("ember_wheel", "EmberWheel", SkillRarity.Epic, SkillRarity.Epic),
            ("rime_blades", "RimeBlades", SkillRarity.Epic, SkillRarity.Epic),
            ("storm_satellites", "StormSatellites", SkillRarity.Epic, SkillRarity.Epic),
            ("reaper_scythes", "ReaperScythes", SkillRarity.Epic, SkillRarity.Epic),
            ("sentinel_halo", "SentinelHalo", SkillRarity.Epic, SkillRarity.Epic),
            ("second_dawn", "SecondDawn", SkillRarity.Legendary, SkillRarity.Legendary),
            ("eclipse_crown", "EclipseCrown", SkillRarity.Legendary, SkillRarity.Legendary),
            ("grave_choir", "GraveChoir", SkillRarity.Legendary, SkillRarity.Legendary),
            ("last_oath", "LastOath", SkillRarity.Legendary, SkillRarity.Legendary),
            ("stolen_moment", "StolenMoment", SkillRarity.Legendary, SkillRarity.Legendary),
            ("sovereigns_fall", "SovereignsFall", SkillRarity.Legendary, SkillRarity.Legendary),
        };

        /// <summary>The orbital skills: Eclipse Crown is offered only while one of them is owned.</summary>
        private static readonly string[] OrbitalIds =
            { "holy_tomes", "ember_wheel", "rime_blades", "storm_satellites", "reaper_scythes", "sentinel_halo" };

        /// <summary>New skills join the run table by tier, once (Rare level 4, Epic level 6, Legendary level 10).</summary>
        private static readonly (string id, int fromLevel)[] BoonUnlocks =
        {
            ("venom_bolts", 4), ("storm_touch", 4), ("ashen_heart", 4), ("repelling_bolts", 4),
            ("stormshock", 6), ("front_arrow", 6), ("bouncy_wall", 6), ("rear_arrow", 6), ("ember_wheel", 6),
            ("rime_blades", 6), ("storm_satellites", 6), ("reaper_scythes", 6), ("sentinel_halo", 6),
            ("second_dawn", 10), ("eclipse_crown", 10), ("grave_choir", 10), ("last_oath", 10), ("stolen_moment", 10),
            ("sovereigns_fall", 10),
        };

        // Card texts stay short (~35 chars, KAN-219 rule). Numbers: the JSON proposals where it has them (Stormshock), else
        // first values sized against the shipped skills (bolt 100 dmg / 1 shot per s, Ember Bolts L1 25/s for 3 s).
        private static readonly Spec[] BoonSpecs =
        {
            // ---- Rare
            new Spec { Id = "venom_bolts", File = "VenomBolts", Name = "Venom Bolts", Rarity = SkillRarity.Rare, Effect = Fx.Venom,
                Levels = new[]
                {
                    L("Poison 12/s for 5 s, 2 stacks", Add(StatId.PoisonDps, 12f), Add(StatId.PoisonDuration, 5f), Add(StatId.PoisonStacks, 2f)),
                    L("Poison 18/s for 5 s, 2 stacks", Add(StatId.PoisonDps, 18f), Add(StatId.PoisonDuration, 5f), Add(StatId.PoisonStacks, 2f)),
                    L("Poison 18/s for 6 s, 3 stacks", Add(StatId.PoisonDps, 18f), Add(StatId.PoisonDuration, 6f), Add(StatId.PoisonStacks, 3f)),
                } },
            new Spec { Id = "storm_touch", File = "StormTouch", Name = "Storm Touch", Rarity = SkillRarity.Rare, Effect = Fx.StormTouch,
                Levels = new[]
                {
                    L("Hits arc to 1 foe, 35% damage", Add(StatId.ArcTargets, 1f), Add(StatId.ArcDamage, 0.35f)),
                    L("Hits arc to 1 foe, 55% damage", Add(StatId.ArcTargets, 1f), Add(StatId.ArcDamage, 0.55f)),
                    L("Hits arc to 2 foes, 55% damage", Add(StatId.ArcTargets, 2f), Add(StatId.ArcDamage, 0.55f)),
                } },
            // Health pickups are the boss fight hearts and the chest fallback heal: a niche card, half weight.
            new Spec { Id = "ashen_heart", File = "AshenHeart", Name = "Ashen Heart", Rarity = SkillRarity.Rare, Weight = 0.5f,
                Levels = new[]
                {
                    L("Health pickups heal 30% more", Pct(StatId.PickupHeal, 0.3f)),
                    L("+30% heal, hearts drop sooner", Pct(StatId.PickupHeal, 0.3f), Pct(StatId.PickupDrop, 0.15f)),
                    L("+60% heal, hearts drop sooner", Pct(StatId.PickupHeal, 0.6f), Pct(StatId.PickupDrop, 0.15f)),
                } },
            new Spec { Id = "repelling_bolts", File = "RepellingBolts", Name = "Repelling Bolts", Rarity = SkillRarity.Rare,
                Effect = Fx.Repel, Levels = new[]
                {
                    L("Every 4th hit knocks a foe back", Add(StatId.KnockEvery, 4f), Add(StatId.KnockForce, 6f)),
                    L("Every 3rd hit knocks a foe back", Add(StatId.KnockEvery, 3f), Add(StatId.KnockForce, 6f)),
                    L("Every 3rd hit, harder knockback", Add(StatId.KnockEvery, 3f), Add(StatId.KnockForce, 8f)),
                } },
            // ---- Epic
            new Spec { Id = "stormshock", File = "Stormshock", Name = "Stormshock", Rarity = SkillRarity.Epic, Effect = Fx.Stormshock,
                Levels = new[]
                {
                    L("4th hit: lightning to 3, 60% dmg", Add(StatId.ShockTargets, 3f), Add(StatId.ShockDamage, 0.6f)),
                    L("Lightning to 4 foes, 70% damage", Add(StatId.ShockTargets, 4f), Add(StatId.ShockDamage, 0.7f)),
                    L("Lightning to 5 foes, 80% damage", Add(StatId.ShockTargets, 5f), Add(StatId.ShockDamage, 0.8f)),
                } },
            new Spec { Id = "front_arrow", File = "FrontArrow", Name = "Front Arrow +1", Rarity = SkillRarity.Epic, Levels = new[]
                {
                    L("+1 forward bolt, 75% damage each", Add(StatId.FrontLanes, 1f), Add(StatId.FrontDamage, -0.25f)),
                    L("+1 forward bolt, 85% damage each", Add(StatId.FrontLanes, 1f), Add(StatId.FrontDamage, -0.15f)),
                    L("+1 forward bolt, 95% damage each", Add(StatId.FrontLanes, 1f), Add(StatId.FrontDamage, -0.05f)),
                } },
            new Spec { Id = "bouncy_wall", File = "BouncyWall", Name = "Bouncy Wall", Rarity = SkillRarity.Epic, Effect = Fx.BouncyWall,
                Levels = new[]
                {
                    L("Bolts rebound off walls, 60% dmg", Add(StatId.WallBounces, 1f)),
                    L("Bolts rebound off walls, 80% dmg", Add(StatId.WallBounces, 1f), Add(StatId.WallDamage, 0.2f)),
                    L("2 wall rebounds, 80% damage", Add(StatId.WallBounces, 2f), Add(StatId.WallDamage, 0.2f)),
                } },
            new Spec { Id = "rear_arrow", File = "RearArrow", Name = "Rear Arrow +1", Rarity = SkillRarity.Epic, Levels = new[]
                {
                    L("+1 bolt behind you, 60% damage", Add(StatId.RearArrows, 1f), Add(StatId.RearDamage, 0.6f)),
                    L("+1 bolt behind you, 80% damage", Add(StatId.RearArrows, 1f), Add(StatId.RearDamage, 0.8f)),
                    L("+1 bolt behind you, full damage", Add(StatId.RearArrows, 1f), Add(StatId.RearDamage, 1f)),
                } },
            new Spec { Id = "ember_wheel", File = "EmberWheel", Name = "Ember Wheel", Rarity = SkillRarity.Epic, Effect = Fx.EmberWheel,
                Levels = new[]
                {
                    L("2 firebrands orbit, burn 20/s", Add(StatId.WheelCount, 2f), Add(StatId.WheelSpin, 2.2f), Add(StatId.WheelBurn, 20f)),
                    L("2 faster firebrands, burn 20/s", Add(StatId.WheelCount, 2f), Add(StatId.WheelSpin, 1.6f), Add(StatId.WheelBurn, 20f)),
                    L("2 fast firebrands, burn 35/s", Add(StatId.WheelCount, 2f), Add(StatId.WheelSpin, 1.6f), Add(StatId.WheelBurn, 35f)),
                } },
            new Spec { Id = "rime_blades", File = "RimeBlades", Name = "Rime Blades", Rarity = SkillRarity.Epic, Effect = Fx.RimeBlades,
                Levels = new[]
                {
                    L("2 ice blades orbit, slow 1.2 s", Add(StatId.RimeCount, 2f), Add(StatId.RimeSlow, 1.2f)),
                    L("2 ice blades orbit, slow 2 s", Add(StatId.RimeCount, 2f), Add(StatId.RimeSlow, 2f)),
                    L("3 ice blades orbit, slow 2 s", Add(StatId.RimeCount, 3f), Add(StatId.RimeSlow, 2f)),
                } },
            new Spec { Id = "storm_satellites", File = "StormSatellites", Name = "Storm Satellites", Rarity = SkillRarity.Epic,
                Effect = Fx.Satellites, Levels = new[]
                {
                    L("2 storm orbs zap every 1.6 s", Add(StatId.SatelliteCount, 2f)),
                    L("2 storm orbs zap every 1.1 s", Add(StatId.SatelliteCount, 2f), Add(StatId.SatelliteInterval, -0.5f)),
                    L("Zaps arc on to 1 more foe", Add(StatId.SatelliteCount, 2f), Add(StatId.SatelliteInterval, -0.5f),
                        Add(StatId.SatelliteArc, 1f)),
                } },
            new Spec { Id = "reaper_scythes", File = "ReaperScythes", Name = "Reaper Scythes", Rarity = SkillRarity.Epic,
                Effect = Fx.Scythes, Levels = new[]
                {
                    L("2 scythes sweep wide, 120% dmg", Add(StatId.ScytheCount, 2f), Add(StatId.ScytheDamage, 1.2f)),
                    L("2 scythes sweep wide, 160% dmg", Add(StatId.ScytheCount, 2f), Add(StatId.ScytheDamage, 1.6f)),
                    L("Bigger scythes, 160% damage", Add(StatId.ScytheCount, 2f), Add(StatId.ScytheDamage, 1.6f), Add(StatId.ScytheSize, 0.25f)),
                } },
            new Spec { Id = "sentinel_halo", File = "SentinelHalo", Name = "Sentinel Halo", Rarity = SkillRarity.Epic, Effect = Fx.Halo,
                Levels = new[]
                {
                    L("2 shields orbit, block shots", Add(StatId.HaloCount, 2f)),
                    L("2 shields, wider block", Add(StatId.HaloCount, 2f), Add(StatId.HaloReach, 0.3f)),
                    L("3 shields, wider block", Add(StatId.HaloCount, 3f), Add(StatId.HaloReach, 0.3f)),
                } },
            // ---- Legendary
            new Spec { Id = "second_dawn", File = "SecondDawn", Name = "Second Dawn", Rarity = SkillRarity.Legendary,
                Effect = Fx.SecondDawn, Levels = new[]
                {
                    L("Once a run: revive with 40% HP", Add(StatId.ReviveHealth, 0.4f)),
                    L("Once a run: revive with 60% HP", Add(StatId.ReviveHealth, 0.6f)),
                    L("Revive 60% HP, wider shockwave", Add(StatId.ReviveHealth, 0.6f), Add(StatId.ReviveRadius, 1.5f)),
                } },
            new Spec { Id = "eclipse_crown", File = "EclipseCrown", Name = "Eclipse Crown", Rarity = SkillRarity.Legendary,
                Effect = Fx.Eclipse, Levels = new[]
                {
                    L("Orbiters align, pulse 150% dmg", Add(StatId.EclipsePulse, 1.5f)),
                    L("Eclipse pulse reaches further", Add(StatId.EclipsePulse, 1.5f), Add(StatId.EclipseRadius, 1.5f)),
                    L("Eclipse every 10 s", Add(StatId.EclipsePulse, 1.5f), Add(StatId.EclipseRadius, 1.5f), Add(StatId.EclipseCooldown, -4f)),
                } },
            new Spec { Id = "grave_choir", File = "GraveChoir", Name = "Grave Choir", Rarity = SkillRarity.Legendary,
                Effect = Fx.GraveChoir, Levels = new[]
                {
                    L("Kills may raise a ghost archer", Add(StatId.ChoirChance, 0.15f), Add(StatId.ChoirDamage, 0.5f)),
                    L("Ghost archers stay 9 s", Add(StatId.ChoirChance, 0.15f), Add(StatId.ChoirDamage, 0.5f), Add(StatId.ChoirDuration, 3f)),
                    L("Ghost archers hit for 80%", Add(StatId.ChoirChance, 0.15f), Add(StatId.ChoirDamage, 0.8f), Add(StatId.ChoirDuration, 3f)),
                } },
            new Spec { Id = "last_oath", File = "LastOath", Name = "Last Oath", Rarity = SkillRarity.Legendary, Effect = Fx.LastOath,
                Levels = new[]
                {
                    L("Under 30% HP: +60% damage surge", Add(StatId.OathDamage, 0.6f)),
                    L("Under 30% HP: +90% damage surge", Add(StatId.OathDamage, 0.9f)),
                    L("Surge returns sooner (30 s)", Add(StatId.OathDamage, 0.9f), Add(StatId.OathCooldown, -15f)),
                } },
            new Spec { Id = "stolen_moment", File = "StolenMoment", Name = "Stolen Moment", Rarity = SkillRarity.Legendary,
                Effect = Fx.StolenMoment, Levels = new[]
                {
                    L("8th volley: an echo fires 3 times", Add(StatId.EchoDamage, 0.6f)),
                    L("Echo volleys deal 80% damage", Add(StatId.EchoDamage, 0.8f)),
                    L("6th volley: an echo fires 3 times", Add(StatId.EchoDamage, 0.8f), Add(StatId.EchoVolleys, -2f)),
                } },
            new Spec { Id = "sovereigns_fall", File = "SovereignsFall", Name = "Sovereign's Fall", Rarity = SkillRarity.Legendary,
                Effect = Fx.SovereignsFall, Levels = new[]
                {
                    L("6 crits drop a sword: 400% dmg", Add(StatId.SwordDamage, 4f)),
                    L("Falling sword deals 600% damage", Add(StatId.SwordDamage, 6f)),
                    L("Falling sword hits a wider area", Add(StatId.SwordDamage, 6f), Add(StatId.SwordRadius, 1f)),
                } },
        };

        /// <summary>The T_Boon_* portrait of <paramref name="skillId"/>, null when the catalog has none (Soul Siphon, ...).</summary>
        private static Texture2D BoonArt(string skillId)
        {
            foreach (var c in Catalog)
                if (c.id == skillId) return AssetDatabase.LoadAssetAtPath<Texture2D>($"{BoonDir}/T_Boon_{c.art}.png");
            return null;
        }

        /// <summary>
        /// KAN-246 one-time moves of the shipped assets (idempotent: a second build changes nothing):
        /// <list type="bullet">
        /// <item>icon: a missing icon or the old painted glyph (Art/Skills/Icons) becomes the T_Boon portrait;</item>
        /// <item>tier: an asset still on its pre-KAN-246 tier moves to the catalog tier (a designer's own tier stays);</item>
        /// <item>Ember Bolts: a table with the old extras (slow, ignite stun, spread, death blast) becomes the burn-only table;</item>
        /// <item>Eclipse Crown: an empty offer gate gets the orbital skills.</item>
        /// </list>
        /// </summary>
        private static void ApplyCatalog(Dictionary<string, SkillDefinition> byId)
        {
            foreach (var c in Catalog)
            {
                if (!byId.TryGetValue(c.id, out var skill) || skill == null) continue;
                var so = new SerializedObject(skill);
                bool changed = false;
                var art = BoonArt(c.id);
                var icon = so.FindProperty("icon");
                var current = icon.objectReferenceValue;
                if (art != null && current != art &&
                    (current == null || AssetDatabase.GetAssetPath(current).StartsWith(IconDir, StringComparison.Ordinal)))
                {
                    icon.objectReferenceValue = art;
                    changed = true;
                }
                var rarity = so.FindProperty("rarity");
                if (c.rarity != c.before && rarity.intValue == (int)c.before)
                {
                    rarity.intValue = (int)c.rarity;
                    changed = true;
                }
                if (!changed) continue;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(skill);
                Debug.Log($"[SkillsBuilder] KAN-246 catalog: {c.id} art / tier ({skill.Rarity})");
            }

            if (byId.TryGetValue("ember_bolts", out var ember) && ember != null && HasEmberExtras(ember))
            {
                var levels = EmberBoltsLevels;
                ember.Configure(ember.Id, ember.DisplayName, levels[0].description, ember.Rarity, levels.Length, levels[0].modifiers);
                ember.SetLevels(levels);
                EditorUtility.SetDirty(ember);
                Debug.Log("[SkillsBuilder] KAN-246: Ember Bolts burns only (slow, stun, spread, blast removed)");
            }

            if (byId.TryGetValue("eclipse_crown", out var eclipse) && eclipse != null &&
                (eclipse.RequiresAnyOf == null || eclipse.RequiresAnyOf.Length == 0))
            {
                var gate = new List<SkillDefinition>();
                foreach (var id in OrbitalIds)
                    if (byId.TryGetValue(id, out var orbital) && orbital != null) gate.Add(orbital);
                eclipse.SetRequiresAnyOf(gate.ToArray());
                EditorUtility.SetDirty(eclipse);
            }
        }

        private static bool HasEmberExtras(SkillDefinition ember)
        {
            if (ember.Levels == null) return false;
            foreach (var row in ember.Levels)
            {
                if (row.modifiers == null) continue;
                foreach (var m in row.modifiers)
                    if (m.stat == StatId.BurnSpread || m.stat == StatId.BurnDeathBlast || m.stat == StatId.BurnSlow ||
                        m.stat == StatId.IgniteStun) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ Effect prefabs (KAN-246)

        private static readonly Color Gold = new Color(1f, 0.85f, 0.45f), Ember = new Color(1f, 0.5f, 0.15f);

        private static void AddBoonEffects(Dictionary<Fx, SkillEffect> built, Procedural.GameArtKit kit)
        {
            var art = EmberBoonsArt.Build(FxDir, kit.SoftDot);

            built[Fx.Venom] = SavePrefab<VenomBoltsEffect>("FX_Skill_VenomBolts", (root, fx) =>
                Set(fx, "fx", Particles(root, "Venom", art.Glow, 400, new Vector2(0.4f, 0.8f), new Vector2(0.08f, 0.16f),
                    new Color(0.5f, 1f, 0.3f), new Color(0.3f, 0.8f, 0.2f))));

            built[Fx.StormTouch] = SavePrefab<StormTouchEffect>("FX_Skill_StormTouch", (root, fx) =>
            {
                Set(fx, "fx", Particles(root, "Sparks", art.Glow, 200, new Vector2(0.15f, 0.3f), new Vector2(0.06f, 0.1f), Gold, Gold));
                Set(fx, "arcs", Arcs(root, art.Arc, 12, 0.07f));
            });

            built[Fx.Repel] = SavePrefab<RepellingBoltsEffect>("FX_Skill_RepellingBolts", (root, fx) =>
                Set(fx, "fx", Particles(root, "Puff", art.Glow, 120, new Vector2(0.15f, 0.3f), new Vector2(0.08f, 0.14f),
                    Color.white, new Color(0.8f, 0.9f, 1f))));

            built[Fx.Stormshock] = SavePrefab<StormshockEffect>("FX_Skill_Stormshock", (root, fx) =>
            {
                Set(fx, "fx", Particles(root, "Sparks", art.Glow, 240, new Vector2(0.15f, 0.3f), new Vector2(0.06f, 0.1f), Gold, Gold));
                Set(fx, "arcs", Arcs(root, art.Arc, 16, 0.09f));
            });

            built[Fx.BouncyWall] = SavePrefab<BouncyWallEffect>("FX_Skill_BouncyWall", (root, fx) =>
                Set(fx, "fx", Particles(root, "Rebound", art.Glow, 160, new Vector2(0.12f, 0.25f), new Vector2(0.05f, 0.09f), Gold, Ember)));

            built[Fx.EmberWheel] = SavePrefab<EmberWheelEffect>("FX_Skill_EmberWheel", (root, fx) =>
                Orbit(root, fx, art.Shard, art.Fire, 1.3f, Particles(root, "Embers", art.Glow, 300, new Vector2(0.25f, 0.45f),
                    new Vector2(0.08f, 0.16f), Ember, new Color(1f, 0.3f, 0.08f)), new Color(1f, 0.45f, 0.12f, 0.9f)));

            built[Fx.RimeBlades] = SavePrefab<RimeBladesEffect>("FX_Skill_RimeBlades", (root, fx) =>
                Orbit(root, fx, art.Blade, art.Ice, 1.2f, Particles(root, "Frost", art.Glow, 300, new Vector2(0.25f, 0.45f),
                    new Vector2(0.08f, 0.14f), new Color(0.6f, 0.85f, 1f), new Color(0.45f, 0.7f, 1f)), new Color(0.55f, 0.8f, 1f, 0.85f)));

            built[Fx.Satellites] = SavePrefab<StormSatellitesEffect>("FX_Skill_StormSatellites", (root, fx) =>
            {
                Orbit(root, fx, art.Orb, art.Storm, 0.38f, Particles(root, "Static", art.Glow, 260, new Vector2(0.2f, 0.4f),
                    new Vector2(0.06f, 0.12f), new Color(0.75f, 0.9f, 1f), Color.white), new Color(0.7f, 0.85f, 1f, 0.85f));
                Set(fx, "arcs", Arcs(root, art.Arc, 12, 0.08f));
            });

            built[Fx.Scythes] = SavePrefab<ReaperScythesEffect>("FX_Skill_ReaperScythes", (root, fx) =>
                Orbit(root, fx, art.Scythe, art.Reaper, 1.25f, Particles(root, "Wisps", art.Glow, 300, new Vector2(0.3f, 0.5f),
                    new Vector2(0.1f, 0.2f), new Color(0.75f, 0.6f, 1f), new Color(0.5f, 0.35f, 0.8f)), new Color(0.7f, 0.55f, 1f, 0.75f)));

            built[Fx.Halo] = SavePrefab<SentinelHaloEffect>("FX_Skill_SentinelHalo", (root, fx) =>
                Orbit(root, fx, art.Plate, art.Halo, 1.25f, Particles(root, "Glint", art.Glow, 200, new Vector2(0.2f, 0.35f),
                    new Vector2(0.08f, 0.14f), Gold, Color.white), new Color(1f, 0.85f, 0.5f, 0.7f)));

            built[Fx.Eclipse] = SavePrefab<EclipseCrownEffect>("FX_Skill_EclipseCrown", (root, fx) =>
                Set(fx, "fx", Particles(root, "Eclipse", art.Glow, 500, new Vector2(0.3f, 0.6f), new Vector2(0.2f, 0.4f),
                    new Color(0.45f, 0.15f, 0.7f), new Color(0.25f, 0.05f, 0.4f))));

            built[Fx.SecondDawn] = SavePrefab<SecondDawnEffect>("FX_Skill_SecondDawn", (root, fx) =>
                Set(fx, "fx", Particles(root, "Dawn", art.Glow, 400, new Vector2(0.4f, 1f), new Vector2(0.15f, 0.3f), Gold, Ember)));

            built[Fx.LastOath] = SavePrefab<LastOathEffect>("FX_Skill_LastOath", (root, fx) =>
                Set(fx, "fx", Particles(root, "Oath", art.Glow, 240, new Vector2(0.3f, 0.6f), new Vector2(0.08f, 0.16f),
                    new Color(1f, 0.35f, 0.2f), Gold)));

            built[Fx.GraveChoir] = SavePrefab<GraveChoirEffect>("FX_Skill_GraveChoir", (root, fx) =>
            {
                Set(fx, "fx", Particles(root, "Souls", art.Glow, 240, new Vector2(0.4f, 0.8f), new Vector2(0.1f, 0.2f),
                    new Color(0.55f, 0.85f, 1f), new Color(0.4f, 0.7f, 1f)));
                Set(fx, "archerTemplate", Ghost(root, "Ghost Archer", art, art.GhostArcher, 0.85f));
            });

            built[Fx.StolenMoment] = SavePrefab<StolenMomentEffect>("FX_Skill_StolenMoment", (root, fx) =>
            {
                Set(fx, "fx", Particles(root, "Ripple", art.Glow, 200, new Vector2(0.3f, 0.5f), new Vector2(0.15f, 0.3f),
                    new Color(0.6f, 0.85f, 1f), Color.white));
                var echo = Ghost(root, "Echo", art, art.GhostEcho, 1f);
                echo.gameObject.SetActive(false);
                Set(fx, "echoBody", echo);
            });

            built[Fx.SovereignsFall] = SavePrefab<SovereignsFallEffect>("FX_Skill_SovereignsFall", (root, fx) =>
            {
                Set(fx, "fx", Particles(root, "Impact", art.Glow, 300, new Vector2(0.2f, 0.45f), new Vector2(0.12f, 0.25f), Gold, Color.white));
                var sword = Body(root.transform, "Sword", art.Sword, art.SwordMat);
                sword.gameObject.SetActive(false);
                Set(fx, "sword", sword);
            });
        }

        /// <summary>Orbit ring bodies (OrbitRing.MaxBodies children, the level shows the first N) + its shared particle system.</summary>
        private static void Orbit(GameObject root, OrbitRing fx, Mesh mesh, Material mat, float scale, ParticleSystem ps, Color trail)
        {
            var bodies = new Transform[OrbitRing.MaxBodies];
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i] = Body(root.transform, $"Body {i}", mesh, mat);
                bodies[i].gameObject.SetActive(false);
            }
            var so = new SerializedObject(fx);
            var list = so.FindProperty("bodies");
            list.arraySize = bodies.Length;
            for (int i = 0; i < bodies.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            so.FindProperty("bodyScale").floatValue = scale;
            so.FindProperty("fx").objectReferenceValue = ps;
            so.FindProperty("trailColour").colorValue = trail;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform Body(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return go.transform;
        }

        /// <summary>Ghost figure (capsule body + head), translucent: Grave Choir archer, Stolen Moment echo. Feet at the root.</summary>
        private static Transform Ghost(GameObject root, string name, EmberBoonsArt.Result art, Material mat, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var body = Body(go.transform, "Body", art.Ghost, mat);
            body.localPosition = new Vector3(0f, 0.72f * size, 0f);
            body.localScale = new Vector3(0.5f, 0.72f, 0.42f) * size;
            var head = Body(go.transform, "Head", art.Head, mat);
            head.localPosition = new Vector3(0f, 1.6f * size, 0.02f);
            head.localScale = Vector3.one * 0.34f * size;
            return go.transform;
        }

        /// <summary>Pooled lightning lines: one template LineRenderer (ArcLines copies it at runtime).</summary>
        private static ArcLines Arcs(GameObject root, Material mat, int pool, float width)
        {
            var go = new GameObject("Arcs");
            go.transform.SetParent(root.transform, false);
            var lineGo = new GameObject("Arc");
            lineGo.transform.SetParent(go.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.sharedMaterial = mat;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 1;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.useWorldSpace = true;
            line.positionCount = ArcLines.Points;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;
            var arcs = go.AddComponent<ArcLines>();
            var so = new SerializedObject(arcs);
            so.FindProperty("template").objectReferenceValue = line;
            so.FindProperty("poolSize").intValue = pool;
            so.ApplyModifiedPropertiesWithoutUndo();
            return arcs;
        }
    }
}
