using SurvivorSouls.Core;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Economy
{
    /// <summary>
    /// Debug menus for the persistent wallets: Souls (KAN-96) and Ember Shards (KAN-97). Work in edit and play mode
    /// (PlayerPrefs).
    /// </summary>
    public static class WalletDebugMenu
    {
        [MenuItem("Survivor Souls/Debug/Wallet: Add 1000 Souls")]
        private static void Add1000() => Debug.Log($"[SoulWallet] +1000 -> {SoulWallet.Earn(1000)} souls");

        [MenuItem("Survivor Souls/Debug/Wallet: Add 100 Ember Shards")]
        private static void AddGems() => Debug.Log($"[GemWallet] +100 -> {GemWallet.Earn(100)} shards");

        [MenuItem("Survivor Souls/Debug/Wallet: Clear")]
        private static void Clear()
        {
            SoulWallet.Clear();
            GemWallet.Clear();
            Debug.Log("[Wallet] cleared -> 0 souls, 0 shards (bonuses stay granted)");
        }

        [MenuItem("Survivor Souls/Debug/Wallet: Reset Shard Bonuses")]
        private static void ResetBonuses()
        {
            GemWallet.ResetAll();
            Debug.Log("[GemWallet] 0 shards, welcome + first-run bonus can be granted again");
        }

        [MenuItem("Survivor Souls/Debug/Wallet: Print")]
        private static void Print() =>
            Debug.Log($"[Wallet] {SoulWallet.Balance} souls, {GemWallet.Balance} shards held, bonuses granted: {GemWallet.Granted}");
    }
}
