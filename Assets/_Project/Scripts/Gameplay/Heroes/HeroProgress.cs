using System;
using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Heroes
{
    /// <summary>Why a hero can or cannot be bought now.</summary>
    public enum HeroBuyCheck { Ok, Owned, TestModeFree, NotEnough, Unknown }

    /// <summary>
    /// Saved hero state (KAN-153): which heroes are bought and which one is picked. Ownership is one
    /// <see cref="CheckedPrefInt"/> per hero (key <c>hero.owned.&lt;id&gt;</c>, salt from the id), like the soul tree levels:
    /// a hand-edited flag reads as not owned, is written back as 0 and sets <see cref="TamperDetected"/>. The pick is a plain
    /// string (<c>hero.selected</c>): it is only honoured while the hero is unlocked, so editing it gives nothing.
    /// Unlock rule: a free hero, a bought hero, or any hero while Test Mode is on (<see cref="TestMode.IsOn"/>, KAN-144).
    /// Test Mode never writes ownership and never charges: when it goes off, a picked hero that was never bought falls back
    /// to the default hero (the saved pick stays and works again when Test Mode comes back).
    /// </summary>
    public static class HeroProgress
    {
        public const string OwnedPrefix = "hero.owned.";
        public const string KeySelected = "hero.selected";
        private const uint SaltBase = 0x4E20B0A7u;

        /// <summary>A hero was bought (price charged, ownership saved).</summary>
        public static event Action<HeroDefinition> Bought;

        /// <summary>The picked hero changed.</summary>
        public static event Action<HeroDefinition> Selected;

        /// <summary>A saved ownership flag failed its checksum (reset to 0). Cleared by <see cref="ResetAll"/>.</summary>
        public static bool TamperDetected { get; private set; }

        public static string KeyOf(HeroDefinition hero) => OwnedPrefix + hero.Id;

        /// <summary>Bought (or free). Test Mode does not count here: see <see cref="IsUnlocked"/>.</summary>
        public static bool IsOwned(HeroDefinition hero)
        {
            if (hero == null) return false;
            if (hero.IsFree) return true;
            var store = Store(hero);
            int value = store.Read(0, out bool tampered);
            if (!tampered) return value > 0;
            TamperDetected = true;
            Debug.LogWarning($"[HeroProgress] Saved ownership of '{hero.Id}' fails its checksum: reset.");
            store.Write(0);
            return false;
        }

        /// <summary>Can be picked and played now: owned, free, or Test Mode is on.</summary>
        public static bool IsUnlocked(HeroDefinition hero) => hero != null && (IsOwned(hero) || TestMode.IsOn);

        /// <summary>Wallet balance in the hero's currency (free heroes: int.MaxValue).</summary>
        public static int Balance(HeroCurrency currency) => currency switch
        {
            HeroCurrency.EmberShards => GemWallet.Balance,
            HeroCurrency.Souls => SoulWallet.Balance,
            _ => int.MaxValue,
        };

        /// <summary>Checks in order: known, owned, Test Mode (free, no buy), wallet.</summary>
        public static HeroBuyCheck Check(HeroDefinition hero)
        {
            if (hero == null) return HeroBuyCheck.Unknown;
            if (IsOwned(hero)) return HeroBuyCheck.Owned;
            if (TestMode.IsOn) return HeroBuyCheck.TestModeFree;
            return Balance(hero.Currency) >= hero.Price ? HeroBuyCheck.Ok : HeroBuyCheck.NotEnough;
        }

        /// <summary>
        /// Buys <paramref name="hero"/>: spends the price, saves ownership, raises <see cref="Bought"/>. Nothing changes when
        /// <see cref="Check"/> is not Ok (owned, Test Mode, not enough).
        /// </summary>
        public static HeroBuyCheck TryBuy(HeroDefinition hero)
        {
            var check = Check(hero);
            if (check != HeroBuyCheck.Ok) return check;
            bool paid = hero.Currency switch
            {
                HeroCurrency.EmberShards => GemWallet.TrySpend(hero.Price),
                HeroCurrency.Souls => SoulWallet.TrySpend(hero.Price),
                _ => true,
            };
            if (!paid) return HeroBuyCheck.NotEnough;
            Store(hero).Write(1);
            Debug.Log($"[HeroProgress] Bought '{hero.Id}' for {hero.Price} {hero.Currency}");
            Bought?.Invoke(hero);
            return HeroBuyCheck.Ok;
        }

        /// <summary>The hero the next run starts with: the saved pick while it is unlocked, else the roster default.</summary>
        public static HeroDefinition Current(HeroRoster roster)
        {
            if (roster == null) return null;
            var picked = roster.Find(PlayerPrefs.GetString(KeySelected, ""));
            return picked != null && IsUnlocked(picked) ? picked : roster.Default;
        }

        /// <summary>Saves <paramref name="hero"/> as the pick. False (nothing saved) when it is locked or not in the roster.</summary>
        public static bool Select(HeroRoster roster, HeroDefinition hero)
        {
            if (roster == null || !roster.Contains(hero) || !IsUnlocked(hero)) return false;
            PlayerPrefs.SetString(KeySelected, hero.Id);
            PlayerPrefs.Save();
            Selected?.Invoke(hero);
            return true;
        }

        /// <summary>Tests and debug: no hero owned, no pick saved.</summary>
        public static void ResetAll(HeroRoster roster)
        {
            TamperDetected = false;
            PlayerPrefs.DeleteKey(KeySelected);
            if (roster == null) return;
            foreach (var hero in roster.Heroes)
            {
                if (hero == null) continue;
                var store = Store(hero);
                PlayerPrefs.DeleteKey(store.Key);
                PlayerPrefs.DeleteKey(store.CheckKey);
            }
            PlayerPrefs.Save();
        }

        /// <summary>Salt of a hero: FNV-1a of its id xor a base (stable across runs and platforms).</summary>
        public static uint SaltOf(string id)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in id ?? "")
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return hash ^ SaltBase;
            }
        }

        private static CheckedPrefInt Store(HeroDefinition hero) => new CheckedPrefInt(KeyOf(hero), SaltOf(hero.Id));
    }
}
