using SurvivorSouls.Core;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Economy
{
    /// <summary>Debug menus for the persistent Souls wallet (KAN-96). Work in edit and play mode (PlayerPrefs).</summary>
    public static class WalletDebugMenu
    {
        [MenuItem("Survivor Souls/Debug/Wallet: Add 1000 Souls")]
        private static void Add1000() => Debug.Log($"[SoulWallet] +1000 -> {SoulWallet.Earn(1000)} souls");

        [MenuItem("Survivor Souls/Debug/Wallet: Clear")]
        private static void Clear()
        {
            SoulWallet.Clear();
            Debug.Log("[SoulWallet] cleared -> 0 souls");
        }

        [MenuItem("Survivor Souls/Debug/Wallet: Print")]
        private static void Print() => Debug.Log($"[SoulWallet] {SoulWallet.Balance} souls held");
    }
}
