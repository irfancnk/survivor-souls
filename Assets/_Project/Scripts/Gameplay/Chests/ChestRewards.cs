using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chests
{
    /// <summary>
    /// What an opened Reliquary chest gives (KAN-73). Listens to <see cref="ChestDropper.Opened"/> and queues the chest's
    /// <see cref="UpgradeOffer"/> on the <see cref="LevelUpFlow"/> (one modal at a time, level-up first, cap
    /// <see cref="LevelUpFlow.UpgradeQueueCap"/>). The upgrade itself is <see cref="SkillManager.Upgrade(SkillDefinition, int)"/>
    /// from the card screen: no effect code here. Fallback reward instead of the screen when no owned skill can level up
    /// (now, or when the queued chest's turn comes) or the queue is full: heal <see cref="ChestDropConfig.FallbackHeal"/> of
    /// max health and pop <see cref="ChestDropConfig.FallbackXp"/> XP as soul wisps out of the chest. Light haptic when the
    /// Knight walks into a chest.
    /// </summary>
    public sealed class ChestRewards : MonoBehaviour
    {
        [SerializeField] private ChestDropper dropper;
        [SerializeField] private LevelUpFlow flow;
        [SerializeField] private SkillManager skills;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private SoulWispField souls;

        /// <summary>A chest's offer went to the upgrade queue.</summary>
        public event Action<UpgradeOffer> Queued;
        /// <summary>Fallback reward given at this point (heal + wisps).</summary>
        public event Action<Vector3> FallbackGiven;

        private Action<ReliquaryChest> _onOpened, _onOpenStarted;
        private Action<UpgradeOffer> _onEmpty;

        public int QueuedCount { get; private set; }
        public int FallbackCount { get; private set; }

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(ChestDropper chestDropper, LevelUpFlow levelUpFlow, SkillManager skillManager, PlayerHealth knight,
            SoulWispField soulField)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled) OnDisable();
            dropper = chestDropper;
            flow = levelUpFlow;
            skills = skillManager;
            player = knight;
            souls = soulField;
            if (wasEnabled) OnEnable();
        }

        private void OnEnable()
        {
            _onOpened ??= OnOpened;
            _onOpenStarted ??= OnOpenStarted;
            _onEmpty ??= OnUpgradeEmpty;
            if (dropper != null)
            {
                dropper.Opened += _onOpened;
                dropper.OpenStarted += _onOpenStarted;
            }
            if (flow != null) flow.UpgradeEmpty += _onEmpty;
        }

        private void OnDisable()
        {
            if (dropper != null)
            {
                dropper.Opened -= _onOpened;
                dropper.OpenStarted -= _onOpenStarted;
            }
            if (flow != null) flow.UpgradeEmpty -= _onEmpty;
        }

        private static void OnOpenStarted(ReliquaryChest chest) => Haptics.Light();

        private void OnOpened(ReliquaryChest chest)
        {
            var offer = new UpgradeOffer(chest.Choices, chest.Upgrades, chest.Tier == ChestTier.Rare);
            if (skills != null && flow != null && skills.HasUpgradableSkill() && flow.EnqueueUpgrade(offer))
            {
                QueuedCount++;
                Queued?.Invoke(offer);
                return;
            }
            GiveFallback(chest.transform.position);
        }

        private void OnUpgradeEmpty(UpgradeOffer offer) =>
            GiveFallback(player != null ? player.transform.position : transform.position);

        /// <summary>Heal share of max health + XP wisps around <paramref name="at"/>. Nothing for a dead Knight.</summary>
        public void GiveFallback(Vector3 at)
        {
            if (player != null && player.IsDead) return;
            var config = dropper != null ? dropper.Config : null;
            if (config == null) return;
            if (player != null && config.FallbackHeal > 0f) player.Heal(player.Max * config.FallbackHeal);
            if (souls != null && config.FallbackXp > 0)
            {
                int count = Mathf.Min(config.FallbackWisps, config.FallbackXp);
                int each = config.FallbackXp / count;
                int rest = config.FallbackXp - each * count;
                for (int i = 0; i < count; i++)
                {
                    float a = i * (Mathf.PI * 2f / count);
                    souls.Spawn(at + new Vector3(Mathf.Cos(a) * 0.6f, 1f, Mathf.Sin(a) * 0.6f), each + (i == 0 ? rest : 0));
                }
            }
            FallbackCount++;
            FallbackGiven?.Invoke(at);
        }
    }
}
