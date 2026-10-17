using System.Collections.Generic;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Skill Guide Book seed (KAN-64): type and "How it works" text per skill id. <see cref="SkillsBuilder"/> writes it into
    /// a skill asset only while the asset's own field is empty (kind <see cref="SkillKind.Unset"/>, empty guide text), so
    /// Inspector edits stay. A new skill without an entry here still shows in the guide (no type, no rules text).
    /// </summary>
    public static class SkillGuideSeed
    {
        private readonly struct Entry
        {
            public readonly SkillKind Kind;
            public readonly string Text;

            public Entry(SkillKind kind, string text)
            {
                Kind = kind;
                Text = text;
            }
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
        {
            ["sharpened_bolts"] = new Entry(SkillKind.Attack,
                "Raises the damage of every bolt. Skills that deal a share of bolt damage (side bolts, bursts, tomes) grow with it."),
            ["quick_draw"] = new Entry(SkillKind.Attack,
                "Shortens the reload between shots, so every bolt skill fires more often."),
            ["keen_eye"] = new Entry(SkillKind.Attack,
                "Each bolt can crit. A crit hit deals extra damage on top of the normal hit."),
            ["multishot"] = new Entry(SkillKind.Attack,
                "Side bolts fan out next to the main bolt and deal a share of its damage. Strong against crowds."),
            ["double_shot"] = new Entry(SkillKind.Attack,
                "Extra bolts follow the main bolt down the same line. Strong against one tough foe or a boss."),
            ["piercing_bolts"] = new Entry(SkillKind.Attack,
                "A bolt flies on through the foes in its line. Each foe it passes takes a share off its damage; level 7 halves the loss."),
            ["ricochet"] = new Entry(SkillKind.Attack,
                "After a hit the bolt jumps to the next foe close by. Each bounce deals less damage; level 7 halves the loss."),
            ["ember_bolts"] = new Entry(SkillKind.Attack,
                "Hits set foes on fire: damage over time and a slow. Higher levels spread the fire and make burning foes blast when they die."),
            ["soul_burst"] = new Entry(SkillKind.Attack,
                "A kill bursts into soul fire that hits the foes around the body. Kills from a burst can trigger another burst."),
            ["holy_tomes"] = new Entry(SkillKind.Attack,
                "Holy books circle you and hit every foe they touch for a share of bolt damage. Later levels add books, knockback and a holy flash."),
            ["vigor"] = new Entry(SkillKind.Defence,
                "Raises max health. The added health is healed at once when you take a level."),
            ["blood_covenant"] = new Entry(SkillKind.Defence,
                "Heals you by a share of the bolt damage you deal. From level 4 it also raises max health."),
            ["temporary_shield"] = new Entry(SkillKind.Defence,
                "A ward makes you immune to all damage for a few seconds, then recharges. Higher levels hold longer and recharge faster."),
            ["fleet_foot"] = new Entry(SkillKind.Utility,
                "You run faster: easier to dodge boss attacks and to reach souls."),
            ["soul_siphon"] = new Entry(SkillKind.Utility,
                "Souls and XP are drawn in from further away, so you collect them without a detour."),
            ["freeze_arrow"] = new Entry(SkillKind.Utility,
                "Every few bolt hits freeze the foe in place. Bosses and elites thaw sooner."),
            ["reliquary_hunter"] = new Entry(SkillKind.Utility,
                "Kills can drop Reliquary chests (elites more often), and chests level up your skills. Offered only while another skill can still level up."),
        };

        /// <summary>Kind of <paramref name="skillId"/> in the seed; <see cref="SkillKind.Unset"/> when it has no entry.</summary>
        public static SkillKind KindOf(string skillId) =>
            skillId != null && Entries.TryGetValue(skillId, out var e) ? e.Kind : SkillKind.Unset;

        /// <summary>Rules text of <paramref name="skillId"/> in the seed; empty when it has no entry.</summary>
        public static string TextOf(string skillId) =>
            skillId != null && Entries.TryGetValue(skillId, out var e) ? e.Text : "";

        /// <summary>Fills an empty kind / guide text of <paramref name="skill"/> from the seed. True when it changed the asset.</summary>
        public static bool Fill(SkillDefinition skill)
        {
            if (skill == null || !Entries.TryGetValue(skill.Id ?? "", out var e)) return false;
            var kind = skill.Kind != SkillKind.Unset ? skill.Kind : e.Kind;
            string text = string.IsNullOrEmpty(skill.GuideText) ? e.Text : skill.GuideText;
            if (kind == skill.Kind && text == skill.GuideText) return false;
            skill.SetGuide(kind, text);
            EditorUtility.SetDirty(skill);
            return true;
        }
    }
}
