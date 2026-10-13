using System.Collections.Generic;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>One owned skill and its stack count, as carried into the next Chamber.</summary>
    public struct SkillStack
    {
        public SkillDefinition Skill;
        public int Stacks;

        public SkillStack(SkillDefinition skill, int stacks)
        {
            Skill = skill;
            Stacks = stacks;
        }
    }

    /// <summary>
    /// What the Knight takes through the portal (KAN-40): level, XP, health, owned skills and the run counters (kills,
    /// souls; KAN-51). Everything else (enemies,
    /// wisps, bolts, timer, world) is rebuilt by the scene reload. Captured and applied by <see cref="ChamberTransition"/>.
    /// </summary>
    public sealed class RunSnapshot
    {
        public int Level = 1;
        public int Xp;
        public int TotalXp;
        public float Health;
        public int Kills;
        public int Souls;
        public readonly List<SkillStack> Skills = new List<SkillStack>();
    }

    /// <summary>
    /// The current run across Chamber reloads (KAN-40): Realm, Chamber number and the <see cref="RunSnapshot"/> waiting
    /// for the next Game scene. Static, so it survives the scene load; never saved to disk (an app kill ends the run).
    /// A new run (menu PLAY, RETRY after death) calls <see cref="NewRun"/>: Chamber 1, nothing carried.
    /// Starting the Game scene without a carried snapshot (editor play, tests) is simply Chamber <see cref="Chamber"/> fresh.
    /// </summary>
    public static class RunState
    {
        public const int FirstChamber = 1;

        private static RunSnapshot _carry;

        public static int Realm { get; private set; } = 1;
        public static int Chamber { get; private set; } = FirstChamber;
        /// <summary>
        /// Random per run (KAN-46). With the Realm and Chamber it makes the world seed (<see cref="ChamberDifficulty.LayoutSeed"/>):
        /// a new layout every Chamber and every run, the same one when the same Chamber of the same run loads again.
        /// </summary>
        public static int RunSeed { get; private set; } = 1;
        /// <summary>A snapshot is waiting for the next Game scene.</summary>
        public static bool HasCarry => _carry != null;

        /// <summary>Start of a run: Chamber 1 of <paramref name="realm"/>, nothing carried. <paramref name="seed"/> 0 = random.</summary>
        public static void NewRun(int realm = 1, int seed = 0)
        {
            Realm = Mathf.Max(1, realm);
            Chamber = FirstChamber;
            RunSeed = seed != 0 ? seed : NewSeed();
            _carry = null;
        }

        /// <summary>Dev and tests: jump the current run to <paramref name="chamber"/> (nothing carried).</summary>
        public static void JumpToChamber(int chamber)
        {
            Chamber = Mathf.Max(FirstChamber, chamber);
            _carry = null;
        }

        private static int NewSeed()
        {
            int seed = (int)(System.DateTime.UtcNow.Ticks & 0x7fffffff) ^ System.Environment.TickCount;
            return seed != 0 ? seed : 1;
        }

        /// <summary>The Knight left through the portal: next Chamber, <paramref name="carry"/> waits for the new scene.</summary>
        public static void AdvanceChamber(RunSnapshot carry)
        {
            Chamber++;
            _carry = carry;
        }

        /// <summary>Hands the carried snapshot to the new scene once (null when the scene starts fresh).</summary>
        public static RunSnapshot TakeCarry()
        {
            var carry = _carry;
            _carry = null;
            return carry;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => NewRun();
    }
}
