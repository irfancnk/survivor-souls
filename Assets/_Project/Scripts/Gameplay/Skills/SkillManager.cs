using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// The run's skills (KAN-19): owned skills and their levels (KAN-50), in pick order. The one place that changes a
    /// level: <see cref="Grant(SkillDefinition)"/> (level-up pick, new or +1), <see cref="Upgrade(SkillDefinition, int)"/>
    /// (chest, owned only, +N capped), <see cref="Restore"/> (Chamber carry). Each writes the skill's effect at the new
    /// level from its data to <see cref="PlayerStats"/> and spawns its effect at level 1. <see cref="Roll"/> offers
    /// distinct skills for a level: pool and rarity odds from <see cref="SkillProgression"/>, maxed and gated skills
    /// (<see cref="CanOffer"/>) left out, the
    /// level's guaranteed skill first, and the pity rule (KAN-37): after <see cref="SkillProgression.PityOffers"/> - 1 offers
    /// in a row without a Rare or better card, the next offer swaps its last Common card for a Rare+ one.
    /// Run-only: fresh in Awake (a new run reloads the scene) and on <see cref="ResetRun"/>.
    /// </summary>
    public sealed class SkillManager : MonoBehaviour
    {
        public sealed class Owned
        {
            public SkillDefinition Skill;
            /// <summary>1..<see cref="SkillDefinition.MaxLevel"/>.</summary>
            public int Level;
            public SkillEffect Effect;
        }

        /// <summary>Result of <see cref="SimulateOffers"/> (debug view of the odds table, KAN-37).</summary>
        public sealed class OfferStats
        {
            public int Level, Offers, Cards, OffersWithRarePlus, LongestDryRun, PityRescues;
            public readonly int[] CardsByRarity = new int[RarityCount];

            public float Share(SkillRarity r) => Cards > 0 ? CardsByRarity[(int)r] / (float)Cards : 0f;

            public override string ToString() =>
                $"L{Level}: {Offers} offers, {Cards} cards | Common {Share(SkillRarity.Common):P1}, Rare {Share(SkillRarity.Rare):P1}, " +
                $"Epic {Share(SkillRarity.Epic):P1}, Legendary {Share(SkillRarity.Legendary):P1} | offers with Rare+ " +
                $"{(Offers > 0 ? OffersWithRarePlus / (float)Offers : 0f):P1}, longest run without Rare+ {LongestDryRun}, pity swaps {PityRescues}";
        }

        public const int RarityCount = 4;

        [SerializeField] private SkillProgression progression;
        [SerializeField] private PlayerStats stats;
        [SerializeField] private AutoShooter shooter;
        [SerializeField] private PlayerHealth health;
        [Tooltip("Parent of spawned skill effects (the player).")]
        [SerializeField] private Transform effectRoot;
        [Tooltip("0 = random every run. Fixed seeds make rolls repeatable (tests, bug repro).")]
        [SerializeField] private int seed;

        /// <summary>Level-up pick applied (<see cref="Grant(SkillDefinition)"/> only): skill, new level.</summary>
        public event Action<SkillDefinition, int> Granted;
        /// <summary>
        /// Any level change from a pick or an upgrade (KAN-50): skill, old level (0 = new), new level. Not raised by
        /// <see cref="Restore"/> or <see cref="ResetRun"/>. HUD glow (KAN-50.5) and telemetry hook here.
        /// </summary>
        public event Action<SkillDefinition, int, int> SkillChanged;
        /// <summary>Owned list changed (grant or reset). HUD rebuilds its row.</summary>
        public event Action Changed;

        private readonly List<Owned> _owned = new List<Owned>();
        private readonly List<SkillDefinition> _pool = new List<SkillDefinition>(32);
        private readonly List<SkillDefinition> _candidates = new List<SkillDefinition>(32);
        private readonly List<float> _weights = new List<float>(32);
        private readonly List<SkillDefinition> _forced = new List<SkillDefinition>(4);
        private SkillContext _context;
        private System.Random _rng;
        private int _dryOffers;
        private bool _lastRollPity;

        public IReadOnlyList<Owned> OwnedSkills => _owned;
        public SkillProgression Progression => progression;
        public PlayerStats Stats => stats;
        /// <summary>Offers in a row without a Rare or better card (pity counter, KAN-37).</summary>
        public int OffersWithoutRarePlus => _dryOffers;
        /// <summary>True when the last <see cref="Roll"/> swapped a card because of the pity rule.</summary>
        public bool LastRollUsedPity => _lastRollPity;

        public static bool IsRarePlus(SkillRarity r) => r >= SkillRarity.Rare;

        private void Awake()
        {
            _rng = seed != 0 ? new System.Random(seed) : new System.Random(Environment.TickCount);
            ResetRun();
        }

        /// <summary>Wiring for code-built objects and tests. Resets the run.</summary>
        public void Setup(SkillProgression table, PlayerStats playerStats, AutoShooter playerShooter, PlayerHealth playerHealth,
            Transform root, int rngSeed = 0)
        {
            ResetRun();
            progression = table;
            stats = playerStats;
            shooter = playerShooter;
            health = playerHealth;
            effectRoot = root;
            seed = rngSeed;
            _context = null;
            _rng = seed != 0 ? new System.Random(seed) : new System.Random(Environment.TickCount);
        }

        /// <summary>Owned level of <paramref name="skill"/> (0 = not owned).</summary>
        public int GetLevel(SkillDefinition skill)
        {
            var o = Find(skill);
            return o != null ? o.Level : 0;
        }

        /// <summary>Owned level of the skill with this id (0 = not owned or unknown).</summary>
        public int GetLevel(string skillId) => GetLevel(FindSkill(skillId));

        /// <summary>
        /// The skill with this id: owned skills first, then every skill in the progression table. Null when unknown.
        /// For debug commands, saves and chests that name a skill by id.
        /// </summary>
        public SkillDefinition FindSkill(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            foreach (var o in _owned)
                if (o.Skill.Id == skillId) return o.Skill;
            return progression != null ? progression.FindSkill(skillId) : null;
        }

        /// <summary>Can go one level higher: new (level 0) or owned below max. Level-up offers use this.</summary>
        public bool CanTake(SkillDefinition skill) => skill != null && GetLevel(skill) < skill.MaxLevel;

        /// <summary>
        /// Level-up offers (<see cref="Roll"/>): <see cref="CanTake"/> plus the offer gate (KAN-72): a new skill marked
        /// <see cref="SkillDefinition.RequiresUpgradableSkill"/> (Reliquary Hunter) waits until another owned skill can
        /// still level up, since its chests would have nothing to upgrade. Forced offers (dev) skip the gate.
        /// </summary>
        public bool CanOffer(SkillDefinition skill) =>
            CanTake(skill) && (!skill.RequiresUpgradableSkill || GetLevel(skill) > 0 || HasUpgradableSkill(skill));

        /// <summary>True when an owned skill other than <paramref name="except"/> is below its max level.</summary>
        public bool HasUpgradableSkill(SkillDefinition except = null)
        {
            for (int i = 0; i < _owned.Count; i++)
            {
                var o = _owned[i];
                if (o.Skill != except && o.Level < o.Skill.MaxLevel) return true;
            }
            return false;
        }

        /// <summary>Owned and below max: a chest (KAN-50.4) can raise it. Never true for a skill not owned yet.</summary>
        public bool CanUpgrade(SkillDefinition skill) => skill != null && GetLevel(skill) > 0 && GetLevel(skill) < skill.MaxLevel;

        /// <summary>
        /// Level-up pick: a new skill at level 1 or an owned one 1 level up, applied at once. Raises <see cref="Granted"/>
        /// (pick sound, HUD pop) and <see cref="SkillChanged"/>. False when maxed.
        /// </summary>
        public bool Grant(SkillDefinition skill)
        {
            if (!CanTake(skill)) return false;
            var o = Raise(skill, 1);
            Granted?.Invoke(skill, o.Level);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Same as <see cref="Grant(SkillDefinition)"/> by id. False when unknown or maxed.</summary>
        public bool Grant(string skillId) => Grant(FindSkill(skillId));

        /// <summary>
        /// Chest / reward upgrade (KAN-50): raises an OWNED skill by <paramref name="levels"/>, capped at its max. Raises
        /// <see cref="SkillChanged"/> (not <see cref="Granted"/>: the upgrade screen has its own feedback). False when the
        /// skill is not owned, already maxed or <paramref name="levels"/> is below 1.
        /// </summary>
        public bool Upgrade(SkillDefinition skill, int levels = 1)
        {
            if (levels < 1 || !CanUpgrade(skill)) return false;
            Raise(skill, levels);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Same as <see cref="Upgrade(SkillDefinition, int)"/> by id.</summary>
        public bool Upgrade(string skillId, int levels = 1) => Upgrade(FindSkill(skillId), levels);

        /// <summary>
        /// Puts back skills carried into the next Chamber (KAN-40): each one at its level (capped at max), modifiers
        /// and effects applied as if granted. No <see cref="Granted"/> / <see cref="SkillChanged"/> (no pick sound, HUD pop
        /// or upgrade telemetry); one <see cref="Changed"/>.
        /// </summary>
        public void Restore(SkillDefinition skill, int level)
        {
            if (skill == null || level <= 0) return;
            var o = Find(skill) ?? Add(skill);
            o.Level = Mathf.Min(Mathf.Max(o.Level, level), skill.MaxLevel);
            Apply(o);
            Changed?.Invoke();
        }

        /// <summary>Adds <paramref name="levels"/> (capped), applies the new level and raises <see cref="SkillChanged"/>.</summary>
        private Owned Raise(SkillDefinition skill, int levels)
        {
            var o = Find(skill) ?? Add(skill);
            int old = o.Level;
            o.Level = Mathf.Min(old + levels, skill.MaxLevel);
            Apply(o);
            SkillChanged?.Invoke(skill, old, o.Level);
            return o;
        }

        private Owned Add(SkillDefinition skill)
        {
            var o = new Owned { Skill = skill };
            _owned.Add(o);
            return o;
        }

        /// <summary>Stats from the level table, effect spawned at level 1 and told the level.</summary>
        private void Apply(Owned o)
        {
            o.Skill.ApplyLevel(stats, o.Level);
            if (o.Effect == null && o.Skill.EffectPrefab != null)
            {
                o.Effect = Instantiate(o.Skill.EffectPrefab, effectRoot != null ? effectRoot : transform, false);
                o.Effect.name = o.Skill.EffectPrefab.name;
                o.Effect.Bind(Context());
            }
            if (o.Effect != null) o.Effect.SetStacks(o.Level);
        }

        /// <summary>Removes every skill, its modifiers and its effect (start of a run).</summary>
        public void ResetRun()
        {
            foreach (var o in _owned)
            {
                if (stats != null) stats.RemoveSource(o.Skill);
                if (o.Effect != null) Destroy(o.Effect.gameObject);
            }
            bool had = _owned.Count > 0;
            _owned.Clear();
            _dryOffers = 0;
            _forced.Clear();
            if (had) Changed?.Invoke();
        }

        /// <summary>
        /// Dev / debug (DevScreenshot -ss-offer, debug window): the next <see cref="Roll"/> offers these skills (the ones
        /// that can still be taken, in this order) instead of a random roll. One use. Does not touch the pity counter.
        /// </summary>
        public void ForceNextOffer(params SkillDefinition[] offer)
        {
            _forced.Clear();
            if (offer == null) return;
            foreach (var s in offer)
                if (s != null && !_forced.Contains(s)) _forced.Add(s);
        }

        /// <summary>
        /// Fills <paramref name="results"/> with up to <paramref name="count"/> distinct skills that can still be taken at
        /// <paramref name="level"/>. Weighted by rarity odds times the skill's own weight, without replacement. Zero-weight
        /// skills only fill slots nothing else can. Fewer results when the pool runs dry. No allocation.
        /// </summary>
        public int Roll(int count, int level, List<SkillDefinition> results)
        {
            results.Clear();
            _lastRollPity = false;
            if (_forced.Count > 0)
            {
                foreach (var s in _forced)
                    if (results.Count < count && CanTake(s)) results.Add(s);
                _forced.Clear();
                if (results.Count > 0) return results.Count;
            }
            if (progression == null || count <= 0) return 0;
            _pool.Clear();
            _candidates.Clear();
            progression.CollectPool(level, _pool);
            foreach (var s in _pool)
                if (CanOffer(s)) _candidates.Add(s);

            var guaranteed = progression.GuaranteedAt(level);
            if (guaranteed != null && CanOffer(guaranteed))
            {
                results.Add(guaranteed);
                _candidates.Remove(guaranteed);
            }

            _weights.Clear();
            foreach (var s in _candidates) _weights.Add(progression.RarityWeight(level, s.Rarity) * s.Weight);

            while (results.Count < count && _candidates.Count > 0)
            {
                float total = 0f;
                for (int i = 0; i < _weights.Count; i++) total += _weights[i];
                int pick;
                if (total > 0f)
                {
                    float r = (float)_rng.NextDouble() * total;
                    pick = _weights.Count - 1;
                    for (int i = 0; i < _weights.Count; i++)
                    {
                        if (_weights[i] <= 0f) continue;
                        r -= _weights[i];
                        if (r < 0f) { pick = i; break; }
                    }
                    while (_weights[pick] <= 0f) pick--; // float edge: land on the last positive weight
                }
                else
                {
                    pick = _rng.Next(_candidates.Count); // only zero-weight skills left
                }
                results.Add(_candidates[pick]);
                int last = _candidates.Count - 1;
                _candidates[pick] = _candidates[last];
                _weights[pick] = _weights[last];
                _candidates.RemoveAt(last);
                _weights.RemoveAt(last);
            }

            ApplyPity(results, guaranteed);
            return results.Count;
        }

        /// <summary>
        /// Pity: when this offer would make <see cref="SkillProgression.PityOffers"/> offers in a row with no Rare+ card,
        /// the last Common card (never the guaranteed one) becomes a Rare+ card picked by weight from what is left.
        /// Only Rare+ skills with a weight above 0 count: a tier that never offers a rarity is not forced to.
        /// </summary>
        private void ApplyPity(List<SkillDefinition> results, SkillDefinition guaranteed)
        {
            if (results.Count == 0) return;
            bool hasRarePlus = false;
            foreach (var s in results)
                if (IsRarePlus(s.Rarity)) { hasRarePlus = true; break; }
            int pity = progression.PityOffers;
            if (!hasRarePlus && pity > 0 && _dryOffers >= pity - 1)
            {
                int slot = -1;
                for (int i = results.Count - 1; i >= 0; i--)
                    if (results[i] != guaranteed) { slot = i; break; }
                float total = 0f;
                for (int i = 0; i < _candidates.Count; i++)
                    if (IsRarePlus(_candidates[i].Rarity) && _weights[i] > 0f) total += _weights[i];
                if (slot >= 0 && total > 0f)
                {
                    float r = (float)_rng.NextDouble() * total;
                    int pick = -1;
                    for (int i = 0; i < _candidates.Count; i++)
                    {
                        if (!IsRarePlus(_candidates[i].Rarity) || _weights[i] <= 0f) continue;
                        pick = i;
                        r -= _weights[i];
                        if (r < 0f) break;
                    }
                    results[slot] = _candidates[pick];
                    hasRarePlus = true;
                    _lastRollPity = true;
                }
            }
            _dryOffers = hasRarePlus ? 0 : _dryOffers + 1;
        }

        /// <summary>
        /// Debug view of the odds table (KAN-37): rolls <paramref name="offers"/> offers of <paramref name="count"/> cards at
        /// <paramref name="level"/> with the current owned skills and pity rule, then puts the pity counter back. Uses the
        /// run's random stream (so call it from debug tools, not from gameplay).
        /// </summary>
        public OfferStats SimulateOffers(int offers, int level, int count = 3)
        {
            var stats = new OfferStats { Level = level };
            int savedDry = _dryOffers;
            _forced.Clear();
            _dryOffers = 0;
            var offer = new List<SkillDefinition>(count);
            int dry = 0;
            for (int n = 0; n < offers; n++)
            {
                if (Roll(count, level, offer) == 0) break;
                stats.Offers++;
                bool rarePlus = false;
                foreach (var s in offer)
                {
                    stats.Cards++;
                    stats.CardsByRarity[(int)s.Rarity]++;
                    rarePlus |= IsRarePlus(s.Rarity);
                }
                if (_lastRollPity) stats.PityRescues++;
                if (rarePlus) { stats.OffersWithRarePlus++; dry = 0; }
                else stats.LongestDryRun = Mathf.Max(stats.LongestDryRun, ++dry);
            }
            _dryOffers = savedDry;
            return stats;
        }

        private Owned Find(SkillDefinition skill)
        {
            for (int i = 0; i < _owned.Count; i++)
                if (_owned[i].Skill == skill) return _owned[i];
            return null;
        }

        private SkillContext Context() => _context ??= new SkillContext
        {
            Stats = stats,
            Shooter = shooter,
            Health = health,
            Player = effectRoot,
        };
    }
}
