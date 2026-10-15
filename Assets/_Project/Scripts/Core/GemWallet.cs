using System;
using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>One-time Ember Shard bonuses (KAN-97). Bit flags: the granted set is saved as one checked int.</summary>
    [Flags]
    public enum GemBonus
    {
        None = 0,
        /// <summary>First time the main menu opens.</summary>
        Welcome = 1,
        /// <summary>First run that ends (death, Realm cleared or leave to the menu).</summary>
        FirstRun = 2,
    }

    /// <summary>
    /// Persistent Ember Shards balance (gems, premium currency; KAN-97, second slice of the KAN-89 economy). Same save
    /// format and anti-tamper rule as <see cref="SoulWallet"/>: PlayerPrefs value + checksum (own salt), saved on every
    /// change, a hand-edited balance resets to 0 and sets <see cref="TamperDetected"/>.
    /// Sources: one-time bonuses (<see cref="TryGrantBonus"/>: welcome, first run) and gem rewards from Rare Reliquary
    /// chests (<c>ChestRewards</c>); amounts and chances live in <see cref="GemRewardConfig"/>. The store (KAN-99) adds
    /// purchases and spends.
    /// The granted bonus set is a checked int too. A tampered set reads as "all granted", so a hand edit can never give a
    /// bonus again.
    /// </summary>
    public static class GemWallet
    {
        public const string KeyBalance = "wallet.gems";
        public const string KeyCheck = "wallet.gems.check";
        public const string KeyBonus = "wallet.gems.bonus";
        public const string KeyBonusCheck = "wallet.gems.bonus.check";
        private const uint Salt = 0xE3B5A11Du;
        private const uint BonusSalt = 0xB0A05EEDu;
        private const int AllBonuses = (int)(GemBonus.Welcome | GemBonus.FirstRun);
        private static readonly CheckedPrefInt Store = new CheckedPrefInt(KeyBalance, Salt);
        private static readonly CheckedPrefInt BonusStore = new CheckedPrefInt(KeyBonus, BonusSalt);

        /// <summary>Every PlayerPrefs key of this wallet (tests save and restore them).</summary>
        public static readonly string[] Keys = { KeyBalance, KeyCheck, KeyBonus, KeyBonusCheck };

        /// <summary>New balance after every earn, spend or clear.</summary>
        public static event Action<int> Changed;

        /// <summary>A bonus was granted: which one, amount, new balance.</summary>
        public static event Action<GemBonus, int, int> BonusGranted;

        /// <summary>The last read found a balance or bonus set that failed its checksum. Cleared by <see cref="Clear"/>.</summary>
        public static bool TamperDetected { get; private set; }

        /// <summary>Ember Shards held now (0 when nothing is saved or the save was tampered with).</summary>
        public static int Balance
        {
            get
            {
                int value = Store.Read(0, out bool tampered);
                if (!tampered) return value;
                TamperDetected = true;
                Debug.LogWarning($"[GemWallet] Saved balance {PlayerPrefs.GetInt(KeyBalance, 0)} fails its checksum: reset to 0.");
                Store.Write(0);
                return 0;
            }
        }

        /// <summary>Bonuses already granted (all of them when the saved set was tampered with).</summary>
        public static GemBonus Granted
        {
            get
            {
                int value = BonusStore.Read(0, out bool tampered);
                if (!tampered) return (GemBonus)(value & AllBonuses);
                TamperDetected = true;
                Debug.LogWarning("[GemWallet] Saved bonus set fails its checksum: every bonus counts as granted.");
                BonusStore.Write(AllBonuses);
                return (GemBonus)AllBonuses;
            }
        }

        public static bool HasBonus(GemBonus bonus) => bonus != GemBonus.None && (Granted & bonus) == bonus;

        public static bool CanAfford(int cost) => cost <= 0 || Balance >= cost;

        /// <summary>Adds <paramref name="amount"/> (ignored when not positive; the total stops at int.MaxValue). Returns the new balance.</summary>
        public static int Earn(int amount)
        {
            int balance = Balance;
            if (amount <= 0) return balance;
            long sum = (long)balance + amount;
            balance = sum > int.MaxValue ? int.MaxValue : (int)sum;
            Store.Write(balance);
            Changed?.Invoke(balance);
            return balance;
        }

        /// <summary>Takes <paramref name="cost"/> when the balance covers it. False (and no change) when it does not.</summary>
        public static bool TrySpend(int cost)
        {
            if (cost < 0) return false;
            int balance = Balance;
            if (balance < cost) return false;
            if (cost == 0) return true;
            balance -= cost;
            Store.Write(balance);
            Changed?.Invoke(balance);
            return true;
        }

        /// <summary>
        /// Grants a one-time bonus of <paramref name="amount"/> shards. True only the first time for that bonus. A bonus
        /// with a non-positive amount is not granted and not marked (a later config with an amount can still grant it).
        /// </summary>
        public static bool TryGrantBonus(GemBonus bonus, int amount)
        {
            if (amount <= 0 || bonus == GemBonus.None || ((int)bonus & ~AllBonuses) != 0) return false;
            var granted = Granted;
            if ((granted & bonus) != 0) return false;
            BonusStore.Write((int)(granted | bonus));
            int balance = Earn(amount);
            BonusGranted?.Invoke(bonus, amount, balance);
            return true;
        }

        /// <summary>Balance to 0 (debug, tests). Keeps the granted bonuses.</summary>
        public static void Clear()
        {
            TamperDetected = false;
            Store.Write(0);
            Changed?.Invoke(0);
        }

        /// <summary>Balance to 0 and no bonus granted (debug, dev screenshots, tests): the next menu gives the welcome bonus again.</summary>
        public static void ResetAll()
        {
            BonusStore.Write(0);
            Clear();
        }

        /// <summary>Checksum of a balance (<see cref="CheckedPrefInt.Checksum"/> with the gems salt). Public for tests.</summary>
        public static int Checksum(int value) => CheckedPrefInt.Checksum(value, Salt);
    }
}
