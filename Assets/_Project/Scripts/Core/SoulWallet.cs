using System;
using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Persistent Souls balance (KAN-96, first slice of the KAN-89 economy). Souls collected in a run are banked here once
    /// when the run ends (<c>RunCounters.BankToWallet</c>); the soul skill tree and the store spend them later (KAN-98, KAN-99).
    /// Stored in PlayerPrefs like <see cref="GameSettings"/> and <c>RunProgress</c>, saved on every change (rare: a few
    /// writes per run), so it survives app restarts.
    /// Basic anti-tamper: a checksum key sits next to the balance. A balance that does not match its checksum (hand-edited
    /// prefs) is reset to 0 on the next read and <see cref="TamperDetected"/> turns true. This stops casual edits only; real
    /// protection needs a server (out of scope, see KAN-89).
    /// Pending souls (KAN-101): when the app goes to the background mid-run, the run's unbanked souls are saved as a
    /// pending amount (own key + checksum). iOS can kill a background app, so the next menu start adds a pending amount
    /// to the balance once (<see cref="RestorePending"/>). A normal run end clears it first, so nothing is counted twice.
    /// </summary>
    public static class SoulWallet
    {
        public const string KeyBalance = "wallet.souls";
        public const string KeyCheck = "wallet.souls.check";
        public const string KeyPending = "wallet.souls.pending";
        public const string KeyPendingCheck = "wallet.souls.pending.check";
        private const uint Salt = 0x5EED50F1u;
        private const uint PendingSalt = 0x5EED50F2u;
        private static readonly CheckedPrefInt Store = new CheckedPrefInt(KeyBalance, Salt);
        private static readonly CheckedPrefInt PendingStore = new CheckedPrefInt(KeyPending, PendingSalt);

        /// <summary>New balance after every earn, spend or clear.</summary>
        public static event Action<int> Changed;

        /// <summary>The last read found a balance that did not match its checksum (reset to 0). Cleared by <see cref="Clear"/>.</summary>
        public static bool TamperDetected { get; private set; }

        /// <summary>Souls held now (0 when nothing is saved or the save was tampered with).</summary>
        public static int Balance
        {
            get
            {
                int value = Store.Read(0, out bool tampered);
                if (!tampered) return value;
                TamperDetected = true;
                Debug.LogWarning($"[SoulWallet] Saved balance {PlayerPrefs.GetInt(KeyBalance, 0)} fails its checksum: reset to 0.");
                Write(0);
                return 0;
            }
        }

        public static bool CanAfford(int cost) => cost <= 0 || Balance >= cost;

        /// <summary>Adds <paramref name="amount"/> (ignored when not positive; the total stops at int.MaxValue). Returns the new balance.</summary>
        public static int Earn(int amount)
        {
            int balance = Balance;
            if (amount <= 0) return balance;
            long sum = (long)balance + amount;
            balance = sum > int.MaxValue ? int.MaxValue : (int)sum;
            Write(balance);
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
            Write(balance);
            Changed?.Invoke(balance);
            return true;
        }

        /// <summary>Balance to 0 and no pending souls (debug, tests).</summary>
        public static void Clear()
        {
            TamperDetected = false;
            ClearPending();
            Write(0);
            Changed?.Invoke(0);
        }

        /// <summary>
        /// Souls of a run that was not banked when the app last went to the background (KAN-101). 0 when none is saved; a
        /// pending amount that fails its checksum is deleted and reads 0.
        /// </summary>
        public static int PendingSouls
        {
            get
            {
                int value = PendingStore.Read(0, out bool tampered);
                if (!tampered) return value;
                Debug.LogWarning($"[SoulWallet] Pending souls {PlayerPrefs.GetInt(KeyPending, 0)} fail their checksum: dropped.");
                ClearPending();
                return 0;
            }
        }

        /// <summary>
        /// Saves the run's unbanked souls as the pending amount (replaces the last one). Not positive = no pending amount.
        /// Called when the app goes to the background mid-run (<c>RunCounters</c>).
        /// </summary>
        public static void SavePending(int amount)
        {
            if (amount <= 0) ClearPending();
            else PendingStore.Write(amount);
        }

        /// <summary>No pending amount any more (a run end banked the souls, or the pending amount was restored).</summary>
        public static void ClearPending()
        {
            if (!PlayerPrefs.HasKey(KeyPending) && !PlayerPrefs.HasKey(KeyPendingCheck)) return;
            PlayerPrefs.DeleteKey(KeyPending);
            PlayerPrefs.DeleteKey(KeyPendingCheck);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// App start with no run going (main menu): adds the pending amount to the balance once and deletes it. The delete
        /// comes first, so a crash between the two steps loses the amount instead of counting it twice. Returns the souls added.
        /// </summary>
        public static int RestorePending()
        {
            int amount = PendingSouls;
            ClearPending();
            if (amount <= 0) return 0;
            Earn(amount);
            Debug.Log($"[SoulWallet] Restored {amount} pending souls from an unfinished run.");
            return amount;
        }

        /// <summary>Checksum of a balance (<see cref="CheckedPrefInt.Checksum"/> with the Souls salt). Public for tests.</summary>
        public static int Checksum(int value) => CheckedPrefInt.Checksum(value, Salt);

        private static void Write(int value) => Store.Write(value);
    }
}
