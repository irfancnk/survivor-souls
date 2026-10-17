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
    /// Ember Shards (KAN-97): an opened Rare chest also rolls <see cref="GemRewardConfig.RollRareChest"/> and puts the shards
    /// straight into the <see cref="GemWallet"/> (persistent at once, not banked at run end), <see cref="GemsFound"/> pops them.
    /// </summary>
    public sealed class ChestRewards : MonoBehaviour
    {
        [SerializeField] private ChestDropper dropper;
        [SerializeField] private LevelUpFlow flow;
        [SerializeField] private SkillManager skills;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private SoulWispField souls;
        [Tooltip("Rare chest shard chance + amount (KAN-97). Null = chests give no shards.")]
        [SerializeField] private GemRewardConfig gems;

        /// <summary>A chest's offer went to the upgrade queue.</summary>
        public event Action<UpgradeOffer> Queued;
        /// <summary>Fallback reward given at this point (heal + wisps).</summary>
        public event Action<Vector3> FallbackGiven;
        /// <summary>A Rare chest gave Ember Shards: where, how many (KAN-97).</summary>
        public event Action<Vector3, int> GemsFound;

        private Action<ReliquaryChest> _onOpened, _onOpenStarted;
        private Action<UpgradeOffer> _onEmpty;

        public int QueuedCount { get; private set; }
        public int FallbackCount { get; private set; }
        /// <summary>Shards chests gave since this component woke.</summary>
        public int GemsGiven { get; private set; }

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(ChestDropper chestDropper, LevelUpFlow levelUpFlow, SkillManager skillManager, PlayerHealth knight,
            SoulWispField soulField, GemRewardConfig gemRewards = null)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled) OnDisable();
            dropper = chestDropper;
            flow = levelUpFlow;
            skills = skillManager;
            player = knight;
            souls = soulField;
            gems = gemRewards;
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
            if (chest.Tier == ChestTier.Rare) GiveChestGems(chest.transform.position, UnityEngine.Random.value, UnityEngine.Random.value);
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

        /// <summary>
        /// Rare chest shard roll (two uniform rolls in [0, 1), see <see cref="GemRewardConfig.RollRareChest"/>): earns the
        /// shards and fires <see cref="GemsFound"/>. Returns the shards given (0 on a miss or without a config).
        /// </summary>
        public int GiveChestGems(Vector3 at, float chanceRoll, float amountRoll)
        {
            int amount = gems != null ? gems.RollRareChest(chanceRoll, amountRoll) : 0;
            if (amount <= 0) return 0;
            GemWallet.Earn(amount);
            GemsGiven += amount;
            GemsFound?.Invoke(at, amount);
            return amount;
        }

        /// <summary>Heal share of max health + XP wisps around <paramref name="at"/>. Nothing for a dead Knight.</summary>
        public void GiveFallback(Vector3 at)
        {
            if (player != null && player.IsDead) return;
            var config = dropper != null ? dropper.Config : null;
            if (config == null) return;
            if (player != null && config.FallbackHeal > 0f) // KAN-246 Ashen Heart: a chest heal is a health pickup too
                player.Heal(PlayerStats.Eval(player.GetComponent<PlayerStats>(), StatId.PickupHeal, player.Max * config.FallbackHeal));
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
