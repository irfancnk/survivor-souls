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
    /// </summary>
    public static class SoulWallet
    {
        public const string KeyBalance = "wallet.souls";
        public const string KeyCheck = "wallet.souls.check";

        /// <summary>New balance after every earn, spend or clear.</summary>
        public static event Action<int> Changed;

        /// <summary>The last read found a balance that did not match its checksum (reset to 0). Cleared by <see cref="Clear"/>.</summary>
        public static bool TamperDetected { get; private set; }

        /// <summary>Souls held now (0 when nothing is saved or the save was tampered with).</summary>
        public static int Balance
        {
            get
            {
                if (!PlayerPrefs.HasKey(KeyBalance)) return 0;
                int value = PlayerPrefs.GetInt(KeyBalance, 0);
                if (value >= 0 && PlayerPrefs.GetInt(KeyCheck, 0) == Checksum(value)) return value;
                TamperDetected = true;
                Debug.LogWarning($"[SoulWallet] Saved balance {value} fails its checksum: reset to 0.");
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

        /// <summary>Balance to 0 (debug, tests).</summary>
        public static void Clear()
        {
            TamperDetected = false;
            Write(0);
            Changed?.Invoke(0);
        }

        /// <summary>
        /// Checksum of a balance: FNV-1a over the value bytes with a fixed salt. Stable across runs and platforms (no
        /// <c>GetHashCode</c>). Public for tests.
        /// </summary>
        public static int Checksum(int value)
        {
            unchecked
            {
                const uint salt = 0x5EED50F1u;
                uint hash = 2166136261u;
                uint v = (uint)value ^ salt;
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (v >> (i * 8)) & 0xFFu;
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        private static void Write(int value)
        {
            PlayerPrefs.SetInt(KeyBalance, value);
            PlayerPrefs.SetInt(KeyCheck, Checksum(value));
            PlayerPrefs.Save();
        }
    }
}
