using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Meta
{
    /// <summary>One real-money Ember Shard pack (KAN-99). Bought through an <see cref="IPurchaseProvider"/>.</summary>
    [Serializable]
    public sealed class GemPack
    {
        [Tooltip("Store product id (App Store / Google Play), e.g. com.irfancnk.survivorsouls.shards.550. Unique.")]
        [SerializeField] private string id = "";
        [SerializeField] private string title = "";
        [SerializeField, Min(1)] private int shards = 100;
        [Tooltip("Extra shards on top (shown as +N BONUS).")]
        [SerializeField, Min(0)] private int bonus;
        [Tooltip("Price in US cents (499 = $4.99). The real store's localized price replaces it later.")]
        [SerializeField, Min(1)] private int priceCents = 99;
        [Tooltip("Small ribbon on the card (POPULAR, BEST VALUE). Empty = none.")]
        [SerializeField] private string badge = "";

        public string Id => id;
        public string Title => title;
        public int Shards => shards;
        public int Bonus => bonus;
        public int TotalShards => shards + bonus;
        public int PriceCents => priceCents;
        public string Badge => badge;
        /// <summary>"$4.99" (USD, invariant culture).</summary>
        public string PriceText => FormatUsd(priceCents);

        public GemPack(string id, string title, int shards, int bonus, int priceCents, string badge = "")
        {
            this.id = id ?? "";
            this.title = title ?? "";
            this.shards = Mathf.Max(1, shards);
            this.bonus = Mathf.Max(0, bonus);
            this.priceCents = Mathf.Max(1, priceCents);
            this.badge = badge ?? "";
        }

        public static string FormatUsd(int cents) =>
            "$" + (cents / 100).ToString(CultureInfo.InvariantCulture) + "." + (cents % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>What a shard offer gives.</summary>
    public enum ShardOfferKind
    {
        /// <summary>A fixed number of Souls.</summary>
        SoulBundle,
        /// <summary>A chest: a random number of Souls between the least and the most (steps of 50).</summary>
        SoulReliquary,
    }

    /// <summary>One Ember Shard spend in the store (KAN-99): pay shards, get Souls into the <c>SoulWallet</c>.</summary>
    [Serializable]
    public sealed class ShardOffer
    {
        public const int ReliquaryStep = 50;

        [SerializeField] private string id = "";
        [SerializeField] private string title = "";
        [SerializeField] private string description = "";
        [SerializeField] private ShardOfferKind kind;
        [SerializeField, Min(1)] private int cost = 100;
        [Tooltip("Souls given (bundle), or the least Souls (reliquary).")]
        [SerializeField, Min(1)] private int souls = 1000;
        [Tooltip("Most Souls a reliquary gives (inclusive). Not used by a bundle.")]
        [SerializeField, Min(1)] private int soulsMax = 1000;

        public string Id => id;
        public string Title => title;
        public string Description => description;
        public ShardOfferKind Kind => kind;
        public int Cost => cost;
        public int SoulsMin => souls;
        public int SoulsMax => kind == ShardOfferKind.SoulBundle ? souls : Mathf.Max(souls, soulsMax);

        public ShardOffer(string id, string title, string description, ShardOfferKind kind, int cost, int souls, int soulsMax = 0)
        {
            this.id = id ?? "";
            this.title = title ?? "";
            this.description = description ?? "";
            this.kind = kind;
            this.cost = Mathf.Max(1, cost);
            this.souls = Mathf.Max(1, souls);
            this.soulsMax = Mathf.Max(this.souls, soulsMax);
        }

        /// <summary>
        /// Souls for one uniform roll in [0, 1): the bundle amount, or for a reliquary min..max in steps of
        /// <see cref="ReliquaryStep"/> (both ends reachable). Pure, so tests pick the roll.
        /// </summary>
        public int RollSouls(float roll)
        {
            if (kind == ShardOfferKind.SoulBundle) return souls;
            int steps = (SoulsMax - souls) / ReliquaryStep;
            int step = Mathf.Min(steps, Mathf.FloorToInt(Mathf.Clamp01(roll) * (steps + 1)));
            return souls + step * ReliquaryStep;
        }

        /// <summary>"2,500 Souls" or "800 - 2,400 Souls".</summary>
        public string RewardText => kind == ShardOfferKind.SoulBundle
            ? $"{Count(souls)} Souls"
            : $"{Count(souls)} - {Count(SoulsMax)} Souls";

        private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Store content (KAN-99): Ember Shard packs for real money and shard offers (Souls bundles, a Souls reliquary).
    /// Asset <c>Data/Economy/StoreCatalog.asset</c>, made once by <c>EconomyBuilder</c> and never overwritten. Rules
    /// live in <see cref="ShardStore"/>; the page is <c>StoreScreen</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Store Catalog", fileName = "StoreCatalog")]
    public sealed class StoreCatalog : ScriptableObject
    {
        [SerializeField] private GemPack[] packs = Array.Empty<GemPack>();
        [SerializeField] private ShardOffer[] offers = Array.Empty<ShardOffer>();

        public IReadOnlyList<GemPack> Packs => packs;
        public IReadOnlyList<ShardOffer> Offers => offers;

        public GemPack FindPack(string id)
        {
            foreach (var p in packs)
                if (p != null && p.Id == id) return p;
            return null;
        }

        public ShardOffer FindOffer(string id)
        {
            foreach (var o in offers)
                if (o != null && o.Id == id) return o;
            return null;
        }

        /// <summary>Content errors (empty or repeated ids); empty list = valid.</summary>
        public List<string> Validate()
        {
            var issues = new List<string>();
            var ids = new HashSet<string>();
            foreach (var p in packs)
            {
                if (p == null || string.IsNullOrEmpty(p.Id)) issues.Add("pack without id");
                else if (!ids.Add(p.Id)) issues.Add("repeated id " + p.Id);
            }
            foreach (var o in offers)
            {
                if (o == null || string.IsNullOrEmpty(o.Id)) issues.Add("offer without id");
                else if (!ids.Add(o.Id)) issues.Add("repeated id " + o.Id);
            }
            return issues;
        }

        /// <summary>Code-side setup (asset builder, tests).</summary>
        public static StoreCatalog Create(GemPack[] packs, ShardOffer[] offers)
        {
            var c = CreateInstance<StoreCatalog>();
            c.packs = packs ?? Array.Empty<GemPack>();
            c.offers = offers ?? Array.Empty<ShardOffer>();
            return c;
        }

        /// <summary>
        /// Shipped content, first guesses (KAN-99). A run banks ~1,000-1,500 Souls, the whole soul tree costs ~25k.
        /// Packs follow the usual App Store tiers with a growing bonus. Offers: ~21 Souls per shard for bundles, ~27 on
        /// average for the reliquary (the gamble pays a little better).
        /// </summary>
        public static StoreCatalog CreateDefault() => Create(
            new[]
            {
                new GemPack(ProductPrefix + "100", "Ember Pinch", 100, 0, 99),
                new GemPack(ProductPrefix + "550", "Ember Pouch", 500, 50, 499, "POPULAR"),
                new GemPack(ProductPrefix + "1200", "Ember Satchel", 1000, 200, 999),
                new GemPack(ProductPrefix + "2500", "Ember Coffer", 2000, 500, 1999),
                new GemPack(ProductPrefix + "6500", "Ember Hoard", 5000, 1500, 4999, "BEST VALUE"),
            },
            new[]
            {
                new ShardOffer("reliquary", "Ashen Reliquary", "An old chest of loose souls. Never the same twice.",
                    ShardOfferKind.SoulReliquary, 60, 800, 2400),
                new ShardOffer("souls-bundle", "Soul Bundle", "Souls bound in a cloth. Enough for a kindling or two.",
                    ShardOfferKind.SoulBundle, 120, 2500),
                new ShardOffer("souls-cache", "Soul Cache", "A hoard of souls taken from the fallen.",
                    ShardOfferKind.SoulBundle, 400, 10000),
            });

        public const string ProductPrefix = "com.irfancnk.survivorsouls.shards.";
    }
}
