using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// Every number a skill can change (KAN-19). Each consumer owns the base value in its natural unit and asks
    /// <see cref="PlayerStats.Modify"/> for the final one: Damage (per bolt), AttackSpeed (shots per second),
    /// MoveSpeed (m/s), MaxHealth (HP), CritChance (0..1), CritDamage (damage multiplier on a crit), Multishot / Pierce /
    /// Ricochet (extra bolts / enemies), BurnDps (fire damage per second), LifeSteal (share of bolt damage healed),
    /// KillBurst (burst damage on kill, as a share of bolt damage), PickupRadius (m). Append new ids at the end:
    /// assets store them as numbers.
    /// </summary>
    public enum StatId
    {
        Damage,
        AttackSpeed,
        MoveSpeed,
        MaxHealth,
        CritChance,
        CritDamage,
        Multishot,
        Pierce,
        Ricochet,
        BurnDps,
        LifeSteal,
        KillBurst,
        PickupRadius,
    }

    /// <summary>Flat adds to the base value; Percent scales it (0.2 = +20 %). Percents from all sources add up.</summary>
    public enum ModifierKind { Flat, Percent }

    /// <summary>One stat change granted per skill stack.</summary>
    [Serializable]
    public struct StatModifier
    {
        public StatId stat;
        public ModifierKind kind;
        public float value;

        public StatModifier(StatId stat, ModifierKind kind, float value)
        {
            this.stat = stat;
            this.kind = kind;
            this.value = value;
        }
    }

    /// <summary>
    /// The player's stat sheet (KAN-19): the one place skills write to and combat, movement, health and pickup code read
    /// from, so a new stat skill needs no combat code change. Sources (usually a <see cref="SkillDefinition"/>) set their
    /// modifiers times a stack count; <see cref="Modify"/> returns <c>(base + flat) * (1 + percent)</c>. Totals are cached
    /// per stat and rebuilt only when a source changes, so reads are two array lookups with no allocation.
    /// </summary>
    public sealed class PlayerStats : MonoBehaviour
    {
        public static readonly int StatCount = Enum.GetValues(typeof(StatId)).Length;

        private sealed class Source
        {
            public UnityEngine.Object Owner;
            public StatModifier[] Modifiers;
            public int Stacks;
        }

        private readonly List<Source> _sources = new List<Source>();
        private readonly float[] _flat = new float[StatCount];
        private readonly float[] _percent = new float[StatCount];

        /// <summary>Raised after any source changed (health re-reads its max, HUD refreshes).</summary>
        public event Action Changed;

        /// <summary>Bumped on every change: cheap "did anything change" check for pollers.</summary>
        public int Version { get; private set; }

        public float Flat(StatId id) => _flat[(int)id];
        public float Percent(StatId id) => _percent[(int)id];

        /// <summary>Final value of <paramref name="id"/> for a consumer whose own base value is <paramref name="baseValue"/>.</summary>
        public float Modify(StatId id, float baseValue)
        {
            int i = (int)id;
            return (baseValue + _flat[i]) * Mathf.Max(0f, 1f + _percent[i]);
        }

        /// <summary>Integer stats (extra bolts, pierces, bounces): rounded, never below 0.</summary>
        public int ModifyInt(StatId id, int baseValue) => Mathf.Max(0, Mathf.RoundToInt(Modify(id, baseValue)));

        /// <summary>Null-safe read for consumers that also work without a stat sheet (tests, editor previews).</summary>
        public static float Eval(PlayerStats stats, StatId id, float baseValue) =>
            stats != null ? stats.Modify(id, baseValue) : baseValue;

        /// <summary>
        /// Sets <paramref name="owner"/>'s modifiers, each applied <paramref name="stacks"/> times. Replaces what the owner
        /// set before; 0 stacks removes it. The array is kept by reference (skill assets are not edited at runtime).
        /// </summary>
        public void SetSource(UnityEngine.Object owner, StatModifier[] modifiers, int stacks)
        {
            int index = IndexOf(owner);
            if (stacks <= 0 || modifiers == null || modifiers.Length == 0)
            {
                if (index < 0) return;
                _sources.RemoveAt(index);
            }
            else if (index >= 0)
            {
                _sources[index].Modifiers = modifiers;
                _sources[index].Stacks = stacks;
            }
            else
            {
                _sources.Add(new Source { Owner = owner, Modifiers = modifiers, Stacks = stacks });
            }
            Rebuild();
        }

        public void RemoveSource(UnityEngine.Object owner) => SetSource(owner, null, 0);

        /// <summary>Drops every source (start of a run).</summary>
        public void Clear()
        {
            if (_sources.Count == 0) return;
            _sources.Clear();
            Rebuild();
        }

        private int IndexOf(UnityEngine.Object owner)
        {
            for (int i = 0; i < _sources.Count; i++)
                if (ReferenceEquals(_sources[i].Owner, owner)) return i;
            return -1;
        }

        private void Rebuild()
        {
            Array.Clear(_flat, 0, StatCount);
            Array.Clear(_percent, 0, StatCount);
            foreach (var s in _sources)
            foreach (var m in s.Modifiers)
            {
                int i = (int)m.stat;
                if (i < 0 || i >= StatCount) continue;
                if (m.kind == ModifierKind.Flat) _flat[i] += m.value * s.Stacks;
                else _percent[i] += m.value * s.Stacks;
            }
            Version++;
            Changed?.Invoke();
        }
    }
}
