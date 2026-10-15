using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Run counters (KAN-51): enemies killed and souls collected (XP value of every absorbed wisp, merged drops counted
    /// at their full value) since the run started. Lives in the Game scene, so a scene reload starts at 0; the portal
    /// carries the numbers to the next Chamber in the <see cref="RunSnapshot"/> (<see cref="ChamberTransition.Capture"/>
    /// / <see cref="ChamberTransition.Apply"/>). A new run (menu PLAY, RETRY) carries nothing, so it starts at 0 again.
    /// Shown on the death screen (<c>PlayerHealthHud</c>) and the KAN-47 victory screen.
    /// Run end (KAN-96): the souls are banked into the persistent <see cref="SoulWallet"/> once per run, when the Knight
    /// dies, when the Realm is cleared, or when the player leaves the run to the menu (<see cref="BankToWallet"/>). A portal
    /// to the next Chamber is not a run end: the numbers ride in the snapshot and are banked later.
    /// The first run that ends this way also grants the one-time first-run Ember Shard bonus (KAN-97, <see cref="GemWallet"/>).
    /// </summary>
    public sealed class RunCounters : MonoBehaviour
    {
        [SerializeField] private SoulWispField souls;
        [Tooltip("Death banks the run's souls (KAN-96). Null = only the other run ends bank.")]
        [SerializeField] private PlayerHealth health;
        [Tooltip("Realm cleared banks the run's souls (KAN-96). Null = only the other run ends bank.")]
        [SerializeField] private ChamberTransition transition;
        [Tooltip("First-run Ember Shard bonus amount (KAN-97). Null = no bonus.")]
        [SerializeField] private GemRewardConfig gems;

        public int Kills { get; private set; }
        public int Souls { get; private set; }
        /// <summary>Souls this component put in the wallet (0 until the run ends here).</summary>
        public int BankedSouls { get; private set; }
        /// <summary>First-run bonus shards granted at this run's end (0 when it was not the first run, KAN-97).</summary>
        public int BonusGems { get; private set; }

        /// <summary>The run's souls went into the wallet: amount banked, new wallet balance.</summary>
        public event Action<int, int> Banked;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(SoulWispField soulField)
        {
            if (isActiveAndEnabled && souls != null) souls.Collected -= OnCollected;
            souls = soulField;
            if (isActiveAndEnabled && souls != null) souls.Collected += OnCollected;
        }

        /// <summary>Tests: the bonus amounts (null = no first-run bonus).</summary>
        public void SetupGems(GemRewardConfig gemRewards) => gems = gemRewards;

        /// <summary>Tests: the run ends that bank on their own (death, Realm cleared).</summary>
        public void SetupRunEnds(PlayerHealth playerHealth, ChamberTransition chamberTransition)
        {
            if (isActiveAndEnabled) UnhookRunEnds();
            health = playerHealth;
            transition = chamberTransition;
            if (isActiveAndEnabled) HookRunEnds();
        }

        private void OnEnable()
        {
            Enemy.Damaged += OnDamaged;
            if (souls != null) souls.Collected += OnCollected;
            HookRunEnds();
        }

        private void OnDisable()
        {
            Enemy.Damaged -= OnDamaged;
            if (souls != null) souls.Collected -= OnCollected;
            UnhookRunEnds();
        }

        private void HookRunEnds()
        {
            if (health != null) health.OnDeath += OnRunEnded;
            if (transition != null) transition.RealmCleared += OnRealmCleared;
        }

        private void UnhookRunEnds()
        {
            if (health != null) health.OnDeath -= OnRunEnded;
            if (transition != null) transition.RealmCleared -= OnRealmCleared;
        }

        private void OnRunEnded() => BankToWallet();
        private void OnRealmCleared(RunSnapshot run) => BankToWallet();

        /// <summary>
        /// Run end: adds <see cref="Souls"/> to the <see cref="SoulWallet"/> once per run (<see cref="RunState.TryMarkSoulsBanked"/>).
        /// Safe to call from every run-end path; later calls return 0 and change nothing. Returns the souls banked.
        /// The first run ever also gets the first-run shard bonus (<see cref="BonusGems"/>).
        /// </summary>
        public int BankToWallet()
        {
            if (!RunState.TryMarkSoulsBanked()) return 0;
            BankedSouls = Souls;
            int balance = SoulWallet.Earn(Souls);
            BonusGems = 0;
            if (gems != null && GemWallet.TryGrantBonus(GemBonus.FirstRun, gems.FirstRunBonus)) BonusGems = gems.FirstRunBonus;
            Banked?.Invoke(Souls, balance);
            return Souls;
        }

        private void OnDamaged(Enemy enemy, float deducted, bool killed)
        {
            if (killed) AddKill();
        }

        private void OnCollected(Vector3 at, int value) => AddSouls(value);

        public void AddKill() => Kills++;

        public void AddSouls(int value)
        {
            if (value > 0) Souls += value;
        }

        /// <summary>Puts carried numbers back after a portal (negative values clamp to 0).</summary>
        public void Restore(int kills, int soulCount)
        {
            Kills = Mathf.Max(0, kills);
            Souls = Mathf.Max(0, soulCount);
        }
    }
}
