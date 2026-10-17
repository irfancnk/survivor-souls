using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// Every number a skill can change (KAN-19). Each consumer owns the base value in its natural unit and asks
    /// <see cref="PlayerStats.Modify"/> for the final one: Damage (per bolt), AttackSpeed (shots per second),
    /// MoveSpeed (m/s), MaxHealth (HP), CritChance (0..1), CritDamage (damage multiplier on a crit), Multishot (extra
    /// angled bolts of Spread Shot, KAN-83: the main bolt always flies straight), Pierce / Ricochet (extra enemies), BurnDps (fire damage per second), LifeSteal (share of bolt damage healed),
    /// KillBurst (burst damage on kill, as a share of bolt damage), PickupRadius (m), BurnDuration (s, burn length;
    /// base from the burn effect), BurstRadius (m, soul burst reach; base from the burst effect), ChestChance /
    /// ChestEliteChance (0..1 Reliquary chest drop per normal / elite kill, KAN-72), ChestChoices (extra cards on a chest's
    /// upgrade screen; base from the drop config), RareChestEvery (every Nth skill chest is Rare; 0 = never), DoubleShot
    /// (extra bolts fired one after another down the same line, KAN-83), SpreadDamage (damage share of each angled Spread
    /// Shot bolt; base from the shooter), ChainDamage (damage share of each follow-up Double Shot bolt; base 1), BurnSpread
    /// (foes a bolt-lit burning enemy sets on fire per burn tick, KAN-77), BurnDeathBlast (blast damage on a burning enemy's
    /// death, as a share of bolt damage, KAN-77), WardDuration (seconds a Hallowed Shield ward blocks all damage, KAN-93),
    /// WardCooldown (seconds between two wards; base from the effect, levels subtract), BurnSlow (0..1 move speed share a
    /// burning foe loses, KAN-134), IgniteStun (seconds a foe stops when a bolt sets it on fire, KAN-134). Append new ids at
    /// the end: assets store them as numbers.
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
        BurnDuration,
        BurstRadius,
        ChestChance,
        ChestEliteChance,
        ChestChoices,
        RareChestEvery,
        DoubleShot,
        SpreadDamage,
        ChainDamage,
        BurnSpread,
        BurnDeathBlast,
        WardDuration,
        WardCooldown,
        BurnSlow,
        IgniteStun,
        /// <summary>KAN-91 Freeze Arrow: every Nth bolt hit is a frost hit (N, 0 = no frost).</summary>
        FreezeEvery,
        /// <summary>KAN-91 Freeze Arrow: seconds a frost hit freezes a foe (bosses and elites get a share).</summary>
        FreezeDuration,
        /// <summary>KAN-123 Holy Tomes: books orbiting the Knight (0 = none).</summary>
        TomeCount,
        /// <summary>KAN-123 Holy Tomes: orbit radius in metres.</summary>
        TomeRadius,
        /// <summary>KAN-123 Holy Tomes: seconds per full turn of the orbit (lower = faster).</summary>
        TomeSpin,
        /// <summary>KAN-123 Holy Tomes: hit damage as a share of the bolt damage (0.6 = 60 %).</summary>
        TomeDamage,
        /// <summary>KAN-123 Holy Tomes: knockback speed of a tome hit in m/s (0 = none).</summary>
        TomeKnockback,
        /// <summary>KAN-123 Holy Tomes: holy flash splash radius in metres round a tome hit (0 = none).</summary>
        TomeFlash,
        /// <summary>KAN-220 Piercing Bolts level 7: flat change of the damage share a bolt loses per pierce (-0.18 = 33 % to 15 %).</summary>
        PierceFalloff,
        /// <summary>KAN-220 Ricochet level 7: flat change of the damage share a bolt loses per bounce (-0.15 = 30 % to 15 %).</summary>
        BounceFalloff,

        // ---- KAN-246 Ember Boons (append only) ----
        /// <summary>Venom Bolts: poison damage per second per stack.</summary>
        PoisonDps,
        /// <summary>Venom Bolts: poison length in seconds.</summary>
        PoisonDuration,
        /// <summary>Venom Bolts: most poison stacks on one foe.</summary>
        PoisonStacks,
        /// <summary>Storm Touch: foes one weapon hit arcs to (0 = none).</summary>
        ArcTargets,
        /// <summary>Storm Touch: arc damage as a share of the bolt damage.</summary>
        ArcDamage,
        /// <summary>Repelling Bolts: every Nth weapon hit knocks the foe back (0 = never).</summary>
        KnockEvery,
        /// <summary>Repelling Bolts: knockback speed in m/s.</summary>
        KnockForce,
        /// <summary>Stormshock: zombies one chain strikes, the struck one included (0 = no chain).</summary>
        ShockTargets,
        /// <summary>Stormshock: chain damage per zombie as a share of the bolt damage.</summary>
        ShockDamage,
        /// <summary>Front Arrow +1: extra parallel forward bolts per volley.</summary>
        FrontLanes,
        /// <summary>Front Arrow +1: damage share of every forward-lane bolt (base 1, levels subtract less).</summary>
        FrontDamage,
        /// <summary>Bouncy Wall: wall rebounds per bolt.</summary>
        WallBounces,
        /// <summary>Bouncy Wall: damage share a bolt keeps after a rebound (base 0, levels add).</summary>
        WallDamage,
        /// <summary>Rear Arrow +1: bolts fired behind the hero per volley.</summary>
        RearArrows,
        /// <summary>Rear Arrow +1: damage share of a rear bolt.</summary>
        RearDamage,
        /// <summary>Ember Wheel: firebrands orbiting the hero.</summary>
        WheelCount,
        /// <summary>Ember Wheel: seconds per turn (lower = faster).</summary>
        WheelSpin,
        /// <summary>Ember Wheel: burn damage per second a contact sets.</summary>
        WheelBurn,
        /// <summary>Rime Blades: ice blades orbiting the hero.</summary>
        RimeCount,
        /// <summary>Rime Blades: contact slow length in seconds.</summary>
        RimeSlow,
        /// <summary>Storm Satellites: charged orbs orbiting the hero.</summary>
        SatelliteCount,
        /// <summary>Storm Satellites: seconds between two discharges (base from the effect, levels subtract).</summary>
        SatelliteInterval,
        /// <summary>Storm Satellites: secondary arcs per discharge.</summary>
        SatelliteArc,
        /// <summary>Reaper Scythes: scythes on the wide orbit.</summary>
        ScytheCount,
        /// <summary>Reaper Scythes: damage per sweep as a share of the bolt damage.</summary>
        ScytheDamage,
        /// <summary>Reaper Scythes: size multiplier add (0.2 = +20 %).</summary>
        ScytheSize,
        /// <summary>Sentinel Halo: shield fragments orbiting the hero.</summary>
        HaloCount,
        /// <summary>Sentinel Halo: block reach add per fragment in metres (angular coverage).</summary>
        HaloReach,
        /// <summary>Second Dawn: health share restored by the revive (0 = no revive).</summary>
        ReviveHealth,
        /// <summary>Second Dawn: shockwave radius add in metres.</summary>
        ReviveRadius,
        /// <summary>Eclipse Crown: dark pulse damage as a share of the bolt damage (0 = none).</summary>
        EclipsePulse,
        /// <summary>Eclipse Crown: pulse radius add in metres.</summary>
        EclipseRadius,
        /// <summary>Eclipse Crown: formation cooldown change in seconds (levels subtract).</summary>
        EclipseCooldown,
        /// <summary>Grave Choir: chance (0..1) that a weapon kill raises a spectral archer.</summary>
        ChoirChance,
        /// <summary>Grave Choir: archer lifetime add in seconds.</summary>
        ChoirDuration,
        /// <summary>Grave Choir: archer bolt damage as a share of the bolt damage.</summary>
        ChoirDamage,
        /// <summary>Last Oath: weapon damage bonus while the surge runs (0 = no surge).</summary>
        OathDamage,
        /// <summary>Last Oath: surge cooldown change in seconds (levels subtract).</summary>
        OathCooldown,
        /// <summary>Stolen Moment: echo volley damage share (0 = no echo).</summary>
        EchoDamage,
        /// <summary>Stolen Moment: change of the volleys needed for an echo (levels subtract).</summary>
        EchoVolleys,
        /// <summary>Sovereign's Fall: sword impact damage as a share of the bolt damage (0 = none).</summary>
        SwordDamage,
        /// <summary>Sovereign's Fall: impact radius add in metres.</summary>
        SwordRadius,
        /// <summary>Ashen Heart: healing of health pickups (Percent: 0.3 = +30 %).</summary>
        PickupHeal,
        /// <summary>Ashen Heart: health pickup drop rate (Percent: 0.15 = +15 %).</summary>
        PickupDrop,
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
    /// Permanent sources (KAN-98 soul tree bonuses, set before the run starts) also count in a second total:
    /// <see cref="Modify(StatId, float, bool)"/> with <c>permanentOnly</c> reads the Knight as he enters the run, the baseline
    /// the horde scaling measures against (KAN-92), so meta upgrades stay a real edge.
    /// </summary>
    public sealed class PlayerStats : MonoBehaviour
    {
        public static readonly int StatCount = Enum.GetValues(typeof(StatId)).Length;

        private sealed class Source
        {
            public UnityEngine.Object Owner;
            public StatModifier[] Modifiers;
            public int Stacks;
            public bool Permanent;
        }

        private readonly List<Source> _sources = new List<Source>();
        private readonly float[] _flat = new float[StatCount];
        private readonly float[] _percent = new float[StatCount];
        private readonly float[] _permanentFlat = new float[StatCount];
        private readonly float[] _permanentPercent = new float[StatCount];

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

        /// <summary>
        /// Same as <see cref="Modify(StatId, float)"/>; <paramref name="permanentOnly"/> counts only the permanent sources
        /// (the Knight at run start, before any in-run skill).
        /// </summary>
        public float Modify(StatId id, float baseValue, bool permanentOnly)
        {
            if (!permanentOnly) return Modify(id, baseValue);
            int i = (int)id;
            return (baseValue + _permanentFlat[i]) * Mathf.Max(0f, 1f + _permanentPercent[i]);
        }

        /// <summary>Integer stats (extra bolts, pierces, bounces): rounded, never below 0.</summary>
        public int ModifyInt(StatId id, int baseValue) => Mathf.Max(0, Mathf.RoundToInt(Modify(id, baseValue)));

        public int ModifyInt(StatId id, int baseValue, bool permanentOnly) =>
            Mathf.Max(0, Mathf.RoundToInt(Modify(id, baseValue, permanentOnly)));

        /// <summary>Null-safe read for consumers that also work without a stat sheet (tests, editor previews).</summary>
        public static float Eval(PlayerStats stats, StatId id, float baseValue) =>
            stats != null ? stats.Modify(id, baseValue) : baseValue;

        public static float Eval(PlayerStats stats, StatId id, float baseValue, bool permanentOnly) =>
            stats != null ? stats.Modify(id, baseValue, permanentOnly) : baseValue;

        /// <summary>
        /// Sets <paramref name="owner"/>'s modifiers, each applied <paramref name="stacks"/> times. Replaces what the owner
        /// set before; 0 stacks removes it. The array is kept by reference (skill assets are not edited at runtime).
        /// <paramref name="permanent"/>: a bonus the Knight brings into every run (soul tree, KAN-98), see
        /// <see cref="Modify(StatId, float, bool)"/>.
        /// </summary>
        public void SetSource(UnityEngine.Object owner, StatModifier[] modifiers, int stacks, bool permanent = false)
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
                _sources[index].Permanent = permanent;
            }
            else
            {
                _sources.Add(new Source { Owner = owner, Modifiers = modifiers, Stacks = stacks, Permanent = permanent });
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
            Array.Clear(_permanentFlat, 0, StatCount);
            Array.Clear(_permanentPercent, 0, StatCount);
            foreach (var s in _sources)
            foreach (var m in s.Modifiers)
            {
                int i = (int)m.stat;
                if (i < 0 || i >= StatCount) continue;
                float v = m.value * s.Stacks;
                if (m.kind == ModifierKind.Flat) _flat[i] += v;
                else _percent[i] += v;
                if (!s.Permanent) continue;
                if (m.kind == ModifierKind.Flat) _permanentFlat[i] += v;
                else _permanentPercent[i] += v;
            }
            Version++;
            Changed?.Invoke();
        }
    }
}
