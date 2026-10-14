using System;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Guaranteed reward pick after a boss kill (KAN-62). When the victory beat ends (<see cref="BossFight.VictoryFinished"/>)
    /// a Rare Reliquary chest lands where the boss fell (<see cref="BossData.RewardChoices"/> cards, +<see cref="BossData.RewardUpgrades"/>
    /// levels; it never fades and ignores the ground cap). Walking into it opens the usual upgrade screen (<see cref="ChestRewards"/>).
    /// When no owned skill can level up, a chest would have nothing to offer, so the Knight gets a bonus skill pick instead
    /// (<see cref="LevelUpFlow.EnqueueBonusPick"/>). Once per fight. On the "Boss" object (BossBuilder).
    /// </summary>
    public sealed class BossReward : MonoBehaviour
    {
        [SerializeField] private BossFight fight;
        [SerializeField] private ChestDropper chests;
        [SerializeField] private SkillManager skills;
        [SerializeField] private LevelUpFlow flow;
        [SerializeField] private XpManager xp;

        /// <summary>The reward chest landed.</summary>
        public event Action<ReliquaryChest> ChestDropped;
        /// <summary>No chest possible: a bonus skill pick was queued (level).</summary>
        public event Action<int> PickQueued;

        private Action<Vector3> _onVictory;

        /// <summary>The reward chest of this fight (null before the reward or when a pick was given).</summary>
        public ReliquaryChest Chest { get; private set; }
        public bool Given { get; private set; }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossFight bossFight, ChestDropper chestDropper, SkillManager skillManager, LevelUpFlow levelUpFlow,
            XpManager xpManager)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled) OnDisable();
            fight = bossFight;
            chests = chestDropper;
            skills = skillManager;
            flow = levelUpFlow;
            xp = xpManager;
            if (wasEnabled) OnEnable();
        }

        private void OnEnable()
        {
            _onVictory ??= Give;
            if (fight != null) fight.VictoryFinished += _onVictory;
        }

        private void OnDisable()
        {
            if (fight != null) fight.VictoryFinished -= _onVictory;
        }

        /// <summary>Gives the reward at <paramref name="at"/> (once). Public for tests and dev tools.</summary>
        public void Give(Vector3 at)
        {
            if (Given) return;
            Given = true;
            var data = fight != null ? fight.Data : null;
            int choices = data != null ? data.RewardChoices : 3;
            int upgrades = data != null ? data.RewardUpgrades : 2;
            bool upgradable = skills == null || skills.HasUpgradableSkill();
            if (chests != null && upgradable)
            {
                Chest = chests.DropReward(at, ChestTier.Rare, choices, upgrades);
                if (Chest != null)
                {
                    ChestDropped?.Invoke(Chest);
                    return;
                }
            }
            int level = xp != null ? xp.Level : 1;
            if (flow != null && flow.EnqueueBonusPick(level)) PickQueued?.Invoke(level);
        }
    }
}
