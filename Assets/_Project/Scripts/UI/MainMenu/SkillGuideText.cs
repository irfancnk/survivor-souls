using System.Collections.Generic;
using System.Globalization;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>One stat line of a Skill Guide Book level row (KAN-64): the TOTAL effect at that level and the one before.</summary>
    public struct SkillGuideEffect
    {
        public StatId Stat;
        public ModifierKind Kind;
        /// <summary>Total modifier value at the level (what <see cref="PlayerStats"/> gets).</summary>
        public float Value;
        /// <summary>Total value at the level before (0 at level 1).</summary>
        public float Previous;
        public string Label;
        /// <summary>Number with its unit, e.g. "+20%", "+3", "-3 s", "60%", "every 3rd" (no "+" for stats with base 0).</summary>
        public string ValueText;

        /// <summary>The number changed from the level before (the guide highlights it).</summary>
        public bool Changed => !Mathf.Approximately(Value, Previous);
    }

    /// <summary>
    /// Text and numbers of the Skill Guide Book (KAN-64), with no UI: the skill list of the run table, the per-level stat
    /// lines read from the same <see cref="SkillDefinition"/> data that <see cref="SkillDefinition.ApplyLevel"/> writes to
    /// <see cref="PlayerStats"/> (so the guide can never drift from the game), the stat labels and units, the type name
    /// and the "how to find it" lines from the <see cref="SkillProgression"/>.
    /// </summary>
    public static class SkillGuideText
    {
        private enum Unit { Plain, Share, Count, Seconds, Metres, PerSecond, MetresPerSecond, Nth }

        private readonly struct StatText
        {
            public readonly string Label;
            public readonly Unit Unit;
            /// <summary>Read from a base of 0, so the total IS the value: shown without a sign ("Tome damage 60%").</summary>
            public readonly bool Absolute;

            public StatText(string label, Unit unit, bool absolute = false)
            {
                Label = label;
                Unit = unit;
                Absolute = absolute;
            }
        }

        // Indexed by StatId (append new stats at the end, like the enum). A missing entry falls back to the enum name.
        // "true" = the game reads the stat from a base of 0 (burn, tomes, ward, freeze, chest odds): no sign.
        private static readonly StatText[] Stats =
        {
            new StatText("Bolt damage", Unit.Plain), // Damage
            new StatText("Attack speed", Unit.Plain), // AttackSpeed
            new StatText("Move speed", Unit.Plain), // MoveSpeed
            new StatText("Max health", Unit.Plain), // MaxHealth
            new StatText("Crit chance", Unit.Share), // CritChance
            new StatText("Crit damage", Unit.Share), // CritDamage
            new StatText("Side bolts", Unit.Count), // Multishot
            new StatText("Pierce", Unit.Count), // Pierce
            new StatText("Bounces", Unit.Count), // Ricochet
            new StatText("Burn damage", Unit.PerSecond, true), // BurnDps
            new StatText("Life steal", Unit.Share, true), // LifeSteal
            new StatText("Burst damage", Unit.Share, true), // KillBurst
            new StatText("Pickup range", Unit.Metres), // PickupRadius
            new StatText("Burn time", Unit.Seconds), // BurnDuration
            new StatText("Burst radius", Unit.Metres), // BurstRadius
            new StatText("Chest drop", Unit.Share, true), // ChestChance
            new StatText("Elite chest drop", Unit.Share, true), // ChestEliteChance
            new StatText("Chest choices", Unit.Count), // ChestChoices
            new StatText("Rare chest", Unit.Nth), // RareChestEvery
            new StatText("Extra bolts", Unit.Count), // DoubleShot
            new StatText("Side bolt damage", Unit.Share), // SpreadDamage
            new StatText("Extra bolt damage", Unit.Plain), // ChainDamage
            new StatText("Fire spreads", Unit.Count), // BurnSpread
            new StatText("Death blast", Unit.Share, true), // BurnDeathBlast
            new StatText("Ward time", Unit.Seconds, true), // WardDuration
            new StatText("Ward recharge", Unit.Seconds), // WardCooldown
            new StatText("Burn slow", Unit.Share, true), // BurnSlow
            new StatText("Ignite stun", Unit.Seconds, true), // IgniteStun
            new StatText("Frost hit", Unit.Nth), // FreezeEvery
            new StatText("Freeze time", Unit.Seconds, true), // FreezeDuration
            new StatText("Tomes", Unit.Count, true), // TomeCount
            new StatText("Orbit radius", Unit.Metres, true), // TomeRadius
            new StatText("Orbit time", Unit.Seconds, true), // TomeSpin
            new StatText("Tome damage", Unit.Share, true), // TomeDamage
            new StatText("Knockback", Unit.MetresPerSecond, true), // TomeKnockback
            new StatText("Holy flash", Unit.Metres, true), // TomeFlash
            new StatText("Pierce loss", Unit.Share), // PierceFalloff
            new StatText("Bounce loss", Unit.Share), // BounceFalloff
        };

        /// <summary>
        /// Every skill of the run table (tier unlocks, then guarantees; no duplicates), sorted by tier (Common first), then
        /// by the level it unlocks at, then by name. A new skill in the table shows up in the guide with no UI change.
        /// </summary>
        public static List<SkillDefinition> Collect(SkillProgression table)
        {
            var list = new List<SkillDefinition>();
            if (table == null) return list;
            foreach (var tier in table.Tiers)
                if (tier?.unlocks != null)
                    foreach (var s in tier.unlocks)
                        if (s != null && !list.Contains(s)) list.Add(s);
            for (int level = 1; level <= 100; level++)
            {
                var g = table.GuaranteedAt(level);
                if (g != null && !list.Contains(g)) list.Add(g);
            }
            list.Sort((a, b) =>
            {
                int c = a.Rarity.CompareTo(b.Rarity);
                if (c != 0) return c;
                c = UnlockLevel(table, a).CompareTo(UnlockLevel(table, b));
                return c != 0 ? c : string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });
            return list;
        }

        /// <summary>Lowest player level whose tier unlocks <paramref name="skill"/>; int.MaxValue when no tier has it.</summary>
        public static int UnlockLevel(SkillProgression table, SkillDefinition skill)
        {
            int best = int.MaxValue;
            if (table == null || skill == null) return best;
            foreach (var tier in table.Tiers)
                if (tier?.unlocks != null && tier.fromLevel < best)
                    foreach (var s in tier.unlocks)
                        if (s == skill)
                        {
                            best = tier.fromLevel;
                            break;
                        }
            return best;
        }

        /// <summary>
        /// Stat lines of <paramref name="skill"/> at <paramref name="level"/> (1..max) into <paramref name="into"/> (cleared):
        /// one line per stat and kind, the total value at that level and the one before. A stat the level drops shows with
        /// value 0. Returns the line count.
        /// </summary>
        public static int EffectsAt(SkillDefinition skill, int level, List<SkillGuideEffect> into)
        {
            into.Clear();
            if (skill == null) return 0;
            level = Mathf.Clamp(level, 1, skill.MaxLevel);
            AddTotals(skill, level, into, current: true);
            if (level > 1) AddTotals(skill, level - 1, into, current: false);
            for (int i = 0; i < into.Count; i++)
            {
                var e = into[i];
                e.Label = LabelOf(e.Stat);
                e.ValueText = FormatValue(e.Stat, e.Kind, e.Value);
                into[i] = e;
            }
            return into.Count;
        }

        private static void AddTotals(SkillDefinition skill, int level, List<SkillGuideEffect> into, bool current)
        {
            StatModifier[] mods;
            int times;
            if (skill.HasLevelTable)
            {
                mods = skill.Levels[level - 1].modifiers;
                times = 1;
            }
            else
            {
                mods = skill.Modifiers;
                times = level;
            }
            if (mods == null) return;
            foreach (var m in mods)
            {
                int i = IndexOf(into, m.stat, m.kind);
                if (i < 0)
                {
                    into.Add(new SkillGuideEffect { Stat = m.stat, Kind = m.kind });
                    i = into.Count - 1;
                }
                var e = into[i];
                if (current) e.Value += m.value * times;
                else e.Previous += m.value * times;
                into[i] = e;
            }
        }

        private static int IndexOf(List<SkillGuideEffect> list, StatId stat, ModifierKind kind)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Stat == stat && list[i].Kind == kind) return i;
            return -1;
        }

        /// <summary>Plain label of a stat ("Bolt damage").</summary>
        public static string LabelOf(StatId stat)
        {
            int i = (int)stat;
            return i >= 0 && i < Stats.Length ? Stats[i].Label : stat.ToString();
        }

        /// <summary>
        /// Number with its unit: a Percent modifier and a share stat as "+20%", counts as "+3", times as "-3 s", ranges as
        /// "+0.6 m", burn as "40/s", "every Nth" stats as "every 3rd". Stats read from a base of 0 have no "+".
        /// </summary>
        public static string FormatValue(StatId stat, ModifierKind kind, float value)
        {
            int i = (int)stat;
            var unit = i >= 0 && i < Stats.Length ? Stats[i].Unit : Unit.Plain;
            bool sign = kind == ModifierKind.Percent || i < 0 || i >= Stats.Length || !Stats[i].Absolute;
            if (kind == ModifierKind.Percent || unit == Unit.Share) return Number(value * 100f, sign) + "%";
            switch (unit)
            {
                case Unit.Seconds: return Number(value, sign) + " s";
                case Unit.Metres: return Number(value, sign) + " m";
                case Unit.PerSecond: return Number(value, sign) + "/s";
                case Unit.MetresPerSecond: return Number(value, sign) + " m/s";
                case Unit.Nth: return "every " + Ordinal(Mathf.RoundToInt(value));
                default: return Number(value, sign);
            }
        }

        /// <summary>The number in <see cref="FormatValue"/> (tests compare it with <see cref="PlayerStats"/>).</summary>
        public static float DisplayNumber(StatId stat, ModifierKind kind, float value)
        {
            int i = (int)stat;
            var unit = i >= 0 && i < Stats.Length ? Stats[i].Unit : Unit.Plain;
            return kind == ModifierKind.Percent || unit == Unit.Share ? value * 100f : value;
        }

        private static string Number(float v, bool sign)
        {
            string text = Mathf.Abs(v).ToString("0.##", CultureInfo.InvariantCulture);
            return (v < 0f ? "-" : sign ? "+" : "") + text;
        }

        private static string Ordinal(int n)
        {
            if (n <= 1) return "hit";
            int tens = n % 100;
            string suffix = tens >= 11 && tens <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n.ToString(CultureInfo.InvariantCulture) + suffix;
        }

        /// <summary>Upper-case type name for the card tag and the filter chip; empty for <see cref="SkillKind.Unset"/>.</summary>
        public static string KindName(SkillKind kind) => kind switch
        {
            SkillKind.Attack => "ATTACK",
            SkillKind.Defence => "DEFENCE",
            SkillKind.Utility => "UTILITY",
            _ => "",
        };

        /// <summary>"How to find it" lines: level-up unlock level, guaranteed offers, the offer gate and chest upgrades.</summary>
        public static List<string> HowToGet(SkillDefinition skill, SkillProgression table)
        {
            var lines = new List<string>();
            if (skill == null) return lines;
            int unlock = UnlockLevel(table, skill);
            if (unlock != int.MaxValue) lines.Add($"Level-up offers from level {unlock}.");
            if (table != null)
                for (int level = 1; level <= 100; level++)
                    if (table.GuaranteedAt(level) == skill)
                        lines.Add($"Always offered at level {level}.");
            if (skill.RequiresUpgradableSkill) lines.Add("Offered only while another skill can still level up.");
            lines.Add(skill.UpgradeOfferWeight < 0.5f
                ? "Reliquary chests raise its level, but rarely."
                : "Reliquary chests can raise its level.");
            return lines;
        }
    }
}
