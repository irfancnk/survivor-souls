using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Meta;
using UnityEngine;
using UnityEngine.TestTools;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-99 store rules: a purchased pack adds its shards + bonus once; cancel / fail / provider offline add nothing; a
    /// second callback or a repeated transaction id never grants again; one purchase at a time (busy until the provider
    /// answers); unknown items are refused; offers spend exact shards and add Souls, refuse when short with no change;
    /// reliquary roll range and steps; price and reward text; the catalog validator; the shipped catalog asset.
    /// The editor's real wallet keys are saved before each test and put back after it.
    /// </summary>
    public sealed class ShardStoreTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Economy/StoreCatalog.asset";
        private readonly Dictionary<string, int> _saved = new Dictionary<string, int>();
        private StoreCatalog _catalog;
        private MockPurchaseProvider _provider;
        private ShardStore _store;
        private GemPack _small, _pouch;
        private ShardOffer _bundle, _reliquary;
        private float _roll;

        private static IEnumerable<string> AllKeys()
        {
            foreach (var k in GemWallet.Keys) yield return k;
            yield return SoulWallet.KeyBalance;
            yield return SoulWallet.KeyCheck;
        }

        [SetUp]
        public void SetUp()
        {
            _saved.Clear();
            foreach (var k in AllKeys())
            {
                if (PlayerPrefs.HasKey(k)) _saved[k] = PlayerPrefs.GetInt(k);
                PlayerPrefs.DeleteKey(k);
            }
            GemWallet.Clear();
            SoulWallet.Clear();

            _small = new GemPack("test.shards.100", "Ember Pinch", 100, 0, 99);
            _pouch = new GemPack("test.shards.550", "Ember Pouch", 500, 50, 499, "POPULAR");
            _bundle = new ShardOffer("test-bundle", "Soul Bundle", "", ShardOfferKind.SoulBundle, 120, 2500);
            _reliquary = new ShardOffer("test-reliquary", "Ashen Reliquary", "", ShardOfferKind.SoulReliquary, 60, 800, 2400);
            _catalog = StoreCatalog.Create(new[] { _small, _pouch }, new[] { _bundle, _reliquary });
            _provider = new MockPurchaseProvider();
            _roll = 0.5f;
            _store = new ShardStore(_catalog, _provider, () => _roll);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            foreach (var k in AllKeys())
            {
                if (_saved.TryGetValue(k, out int v)) PlayerPrefs.SetInt(k, v);
                else PlayerPrefs.DeleteKey(k);
            }
            PlayerPrefs.Save();
        }

        [Test]
        public void Pack_Purchased_GrantsShardsPlusBonusOnce()
        {
            var finished = new List<int>();
            _store.PackFinished += (_, _, granted) => finished.Add(granted);
            PurchaseResult got = default;
            int grantedNow = -1;
            Assert.That(_store.BuyPack(_pouch, (r, g) => { got = r; grantedNow = g; }), Is.EqualTo(StoreCheck.Ok));
            Assert.That(got.Purchased, Is.True);
            Assert.That(got.ProductId, Is.EqualTo("test.shards.550"));
            Assert.That(got.TransactionId, Is.EqualTo("mock-1"));
            Assert.That(grantedNow, Is.EqualTo(550));
            Assert.That(GemWallet.Balance, Is.EqualTo(550), "500 + 50 bonus");
            Assert.That(finished, Is.EqualTo(new[] { 550 }));
            Assert.That(_store.Busy, Is.False);

            Assert.That(_store.BuyPack(_small), Is.EqualTo(StoreCheck.Ok));
            Assert.That(GemWallet.Balance, Is.EqualTo(650));
            Assert.That(_provider.Requests, Is.EqualTo(2));
        }

        [Test]
        public void Pack_CancelledOrFailed_GrantsNothing()
        {
            _provider.NextOutcome = PurchaseOutcome.Cancelled;
            int granted = -1;
            PurchaseResult got = default;
            Assert.That(_store.BuyPack(_pouch, (r, g) => { got = r; granted = g; }), Is.EqualTo(StoreCheck.Ok));
            Assert.That(got.Outcome, Is.EqualTo(PurchaseOutcome.Cancelled));
            Assert.That(granted, Is.Zero);
            Assert.That(GemWallet.Balance, Is.Zero);

            _provider.NextOutcome = PurchaseOutcome.Failed;
            _store.BuyPack(_pouch, (r, g) => { got = r; granted = g; });
            Assert.That(got.Outcome, Is.EqualTo(PurchaseOutcome.Failed));
            Assert.That(got.Error, Is.Not.Empty);
            Assert.That(granted, Is.Zero);
            Assert.That(GemWallet.Balance, Is.Zero);
            Assert.That(_store.Busy, Is.False, "a failure frees the store");
        }

        [Test]
        public void Pack_ProviderOffline_RefusedWithoutCallingIt()
        {
            _provider.IsReady = false;
            Assert.That(_store.CheckPack(_pouch), Is.EqualTo(StoreCheck.NotReady));
            Assert.That(_store.BuyPack(_pouch), Is.EqualTo(StoreCheck.NotReady));
            Assert.That(_provider.Requests, Is.Zero);
            Assert.That(new ShardStore(_catalog, null).CheckPack(_pouch), Is.EqualTo(StoreCheck.NotReady), "no provider");
        }

        [Test]
        public void Pack_OnePurchaseAtATime_BusyUntilTheProviderAnswers()
        {
            _provider.AutoComplete = false;
            Assert.That(_store.BuyPack(_pouch), Is.EqualTo(StoreCheck.Ok));
            Assert.That(_store.Busy, Is.True);
            Assert.That(_provider.HasPending, Is.True);
            Assert.That(_store.CheckPack(_small), Is.EqualTo(StoreCheck.Busy));
            Assert.That(_store.BuyPack(_small), Is.EqualTo(StoreCheck.Busy));
            Assert.That(_provider.Requests, Is.EqualTo(1), "the second tap never reaches the provider");
            Assert.That(GemWallet.Balance, Is.Zero, "nothing before the answer");

            Assert.That(_provider.CompletePending(PurchaseOutcome.Purchased), Is.True);
            Assert.That(_store.Busy, Is.False);
            Assert.That(GemWallet.Balance, Is.EqualTo(550));
            Assert.That(_provider.CompletePending(PurchaseOutcome.Purchased), Is.False, "nothing left to answer");
        }

        /// <summary>Provider that answers twice and repeats a transaction id across purchases.</summary>
        private sealed class DoubleProvider : IPurchaseProvider
        {
            public string Name => "Double";
            public bool IsSandbox => true;
            public bool IsReady => true;
            public string ProductOverride;

            public void Purchase(string productId, System.Action<PurchaseResult> done)
            {
                var result = new PurchaseResult(ProductOverride ?? productId, PurchaseOutcome.Purchased, "tx-same");
                done(result);
                done(result);
            }
        }

        [Test]
        public void Pack_DoubleCallbackOrRepeatedTransaction_GrantsOnce()
        {
            var store = new ShardStore(_catalog, new DoubleProvider());
            var calls = 0;
            store.BuyPack(_pouch, (_, _) => calls++);
            Assert.That(calls, Is.EqualTo(1), "second callback ignored");
            Assert.That(GemWallet.Balance, Is.EqualTo(550));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not granted"));
            int granted = -1;
            store.BuyPack(_small, (_, g) => granted = g);
            Assert.That(granted, Is.Zero, "same transaction id again");
            Assert.That(GemWallet.Balance, Is.EqualTo(550));
        }

        [Test]
        public void Pack_ResultForAnotherProduct_GrantsNothing()
        {
            var provider = new DoubleProvider { ProductOverride = "test.shards.100" };
            var store = new ShardStore(_catalog, provider);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not granted"));
            store.BuyPack(_pouch);
            Assert.That(GemWallet.Balance, Is.Zero);
        }

        [Test]
        public void UnknownItems_Refused()
        {
            var stranger = new GemPack("test.other", "Other", 10, 0, 99);
            var strangeOffer = new ShardOffer("test-other", "Other", "", ShardOfferKind.SoulBundle, 1, 1);
            Assert.That(_store.BuyPack(stranger), Is.EqualTo(StoreCheck.Unknown));
            Assert.That(_store.BuyPack(null), Is.EqualTo(StoreCheck.Unknown));
            GemWallet.Earn(100);
            Assert.That(_store.BuyOffer(strangeOffer, out int souls), Is.EqualTo(StoreCheck.Unknown));
            Assert.That(_store.BuyOffer(null, out _), Is.EqualTo(StoreCheck.Unknown));
            Assert.That(souls, Is.Zero);
            Assert.That(GemWallet.Balance, Is.EqualTo(100));
            Assert.That(_provider.Requests, Is.Zero);
        }

        [Test]
        public void Offer_SpendsExactShards_AddsSouls_RefusedWhenShort()
        {
            GemWallet.Earn(119);
            Assert.That(_store.CheckOffer(_bundle), Is.EqualTo(StoreCheck.NotEnoughShards));
            Assert.That(_store.BuyOffer(_bundle, out int souls), Is.EqualTo(StoreCheck.NotEnoughShards));
            Assert.That(souls, Is.Zero);
            Assert.That(GemWallet.Balance, Is.EqualTo(119), "no change when short");
            Assert.That(SoulWallet.Balance, Is.Zero);

            GemWallet.Earn(1);
            var bought = new List<int>();
            _store.OfferBought += (_, s) => bought.Add(s);
            Assert.That(_store.BuyOffer(_bundle, out souls), Is.EqualTo(StoreCheck.Ok));
            Assert.That(souls, Is.EqualTo(2500));
            Assert.That(GemWallet.Balance, Is.Zero, "exact spend");
            Assert.That(SoulWallet.Balance, Is.EqualTo(2500));
            Assert.That(bought, Is.EqualTo(new[] { 2500 }));
        }

        [Test]
        public void Reliquary_RollRangeAndSteps()
        {
            Assert.That(_reliquary.RollSouls(0f), Is.EqualTo(800));
            Assert.That(_reliquary.RollSouls(0.9999f), Is.EqualTo(2400), "max reachable");
            Assert.That(_reliquary.RollSouls(1f), Is.EqualTo(2400), "clamped");
            Assert.That(_reliquary.RollSouls(-1f), Is.EqualTo(800));
            for (float r = 0f; r < 1f; r += 0.013f)
            {
                int s = _reliquary.RollSouls(r);
                Assert.That(s, Is.InRange(800, 2400));
                Assert.That((s - 800) % ShardOffer.ReliquaryStep, Is.Zero);
            }
            Assert.That(_bundle.RollSouls(0.99f), Is.EqualTo(2500), "a bundle never rolls");

            GemWallet.Earn(60);
            _roll = 0.9999f;
            Assert.That(_store.BuyOffer(_reliquary, out int souls), Is.EqualTo(StoreCheck.Ok));
            Assert.That(souls, Is.EqualTo(2400));
            Assert.That(SoulWallet.Balance, Is.EqualTo(2400));
        }

        [Test]
        public void Texts_PriceAndReward()
        {
            Assert.That(_small.PriceText, Is.EqualTo("$0.99"));
            Assert.That(_pouch.PriceText, Is.EqualTo("$4.99"));
            Assert.That(GemPack.FormatUsd(4999), Is.EqualTo("$49.99"));
            Assert.That(GemPack.FormatUsd(1000), Is.EqualTo("$10.00"));
            Assert.That(_bundle.RewardText, Is.EqualTo("2,500 Souls"));
            Assert.That(_reliquary.RewardText, Is.EqualTo("800 - 2,400 Souls"));
            Assert.That(_pouch.TotalShards, Is.EqualTo(550));
        }

        [Test]
        public void Catalog_ValidateFindsEmptyAndRepeatedIds()
        {
            Assert.That(_catalog.Validate(), Is.Empty);
            var bad = StoreCatalog.Create(new[] { _small, new GemPack("", "x", 1, 0, 1) },
                new[] { new ShardOffer("test.shards.100", "dup", "", ShardOfferKind.SoulBundle, 1, 1) });
            var issues = bad.Validate();
            Object.DestroyImmediate(bad);
            Assert.That(issues, Has.Count.EqualTo(2));
            Assert.That(issues, Has.Some.Contains("without id"));
            Assert.That(issues, Has.Some.Contains("repeated id test.shards.100"));
        }

        [Test]
        public void ShippedCatalog_ValidPricedAndSane()
        {
            var shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<StoreCatalog>(CatalogPath);
            Assert.That(shipped, Is.Not.Null, CatalogPath);
            Assert.That(shipped.Validate(), Is.Empty);
            Assert.That(shipped.Packs.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(shipped.Offers.Count, Is.GreaterThanOrEqualTo(2));
            float lastPerDollar = 0f;
            int lastPrice = 0;
            foreach (var pack in shipped.Packs)
            {
                Assert.That(pack.Id, Does.StartWith(StoreCatalog.ProductPrefix));
                Assert.That(pack.PriceCents, Is.GreaterThan(lastPrice), "packs sorted by price");
                float perDollar = pack.TotalShards * 100f / pack.PriceCents;
                Assert.That(perDollar, Is.GreaterThanOrEqualTo(lastPerDollar), $"{pack.Title}: bigger packs never give less per dollar");
                lastPerDollar = perDollar;
                lastPrice = pack.PriceCents;
                Debug.Log($"[Store] {pack.Title} {pack.PriceText} = {pack.TotalShards} shards ({perDollar:F0}/$)");
            }
            foreach (var offer in shipped.Offers)
            {
                Assert.That(offer.Cost, Is.GreaterThan(0));
                Assert.That(offer.SoulsMax, Is.GreaterThanOrEqualTo(offer.SoulsMin));
                Debug.Log($"[Store] {offer.Title} {offer.Cost} shards = {offer.RewardText}");
            }
            // The welcome bonus alone buys the cheapest offer (first sink right away).
            var gems = UnityEditor.AssetDatabase.LoadAssetAtPath<GemRewardConfig>("Assets/_Project/Data/Economy/GemRewards.asset");
            int cheapest = int.MaxValue;
            foreach (var offer in shipped.Offers) cheapest = Mathf.Min(cheapest, offer.Cost);
            Assert.That(gems.WelcomeBonus + gems.FirstRunBonus, Is.GreaterThanOrEqualTo(cheapest));
        }
    }
}
