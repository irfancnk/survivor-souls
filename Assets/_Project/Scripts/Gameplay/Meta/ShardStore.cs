using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Meta
{
    /// <summary>Why a store item can or cannot be bought now.</summary>
    public enum StoreCheck { Ok, Unknown, Busy, NotReady, NotEnoughShards }

    /// <summary>
    /// Store rules (KAN-99, last slice of the KAN-89 economy). Gem packs: <see cref="BuyPack"/> starts the
    /// <see cref="IPurchaseProvider"/> flow; only a <see cref="PurchaseOutcome.Purchased"/> result adds the pack's
    /// <see cref="GemPack.TotalShards"/> to the <see cref="GemWallet"/>, once per transaction id (a provider that reports
    /// the same transaction twice, or calls back twice, grants once). One pack purchase at a time (<see cref="Busy"/>).
    /// Shard offers: <see cref="BuyOffer"/> spends shards through <see cref="GemWallet.TrySpend"/> and adds the rolled
    /// Souls to the <see cref="SoulWallet"/>; a failed spend changes nothing.
    /// Plain class: the store page owns one; tests build it with a <see cref="MockPurchaseProvider"/>.
    /// </summary>
    public sealed class ShardStore
    {
        private readonly StoreCatalog _catalog;
        private readonly IPurchaseProvider _provider;
        private readonly HashSet<string> _granted = new HashSet<string>();
        private readonly Func<float> _roll;

        /// <summary>A pack purchase ended: pack, result, shards granted (0 unless purchased the first time).</summary>
        public event Action<GemPack, PurchaseResult, int> PackFinished;
        /// <summary>A shard offer was bought: offer, Souls granted.</summary>
        public event Action<ShardOffer, int> OfferBought;

        public StoreCatalog Catalog => _catalog;
        public IPurchaseProvider Provider => _provider;
        /// <summary>A pack purchase waits for the provider.</summary>
        public bool Busy { get; private set; }

        /// <param name="roll">Uniform [0, 1) source for reliquary rolls (null = UnityEngine.Random).</param>
        public ShardStore(StoreCatalog catalog, IPurchaseProvider provider, Func<float> roll = null)
        {
            _catalog = catalog;
            _provider = provider;
            _roll = roll ?? (() => UnityEngine.Random.value);
        }

        public StoreCheck CheckPack(GemPack pack)
        {
            if (pack == null || _catalog == null || _catalog.FindPack(pack.Id) != pack) return StoreCheck.Unknown;
            if (Busy) return StoreCheck.Busy;
            if (_provider == null || !_provider.IsReady) return StoreCheck.NotReady;
            return StoreCheck.Ok;
        }

        /// <summary>
        /// Starts buying <paramref name="pack"/>. Returns the check; only <see cref="StoreCheck.Ok"/> starts the provider.
        /// <paramref name="done"/> (and <see cref="PackFinished"/>) runs once when the provider answers.
        /// </summary>
        public StoreCheck BuyPack(GemPack pack, Action<PurchaseResult, int> done = null)
        {
            var check = CheckPack(pack);
            if (check != StoreCheck.Ok) return check;
            Busy = true;
            bool answered = false;
            _provider.Purchase(pack.Id, result =>
            {
                if (answered) return; // a provider must answer once; a second call never grants again
                answered = true;
                Busy = false;
                int granted = 0;
                if (result.Purchased && result.ProductId == pack.Id && _granted.Add(result.TransactionId))
                {
                    granted = pack.TotalShards;
                    GemWallet.Earn(granted);
                }
                else if (result.Purchased)
                    Debug.LogWarning($"[ShardStore] Purchase '{result.ProductId}' tx '{result.TransactionId}' not granted (repeat or wrong product).");
                done?.Invoke(result, granted);
                PackFinished?.Invoke(pack, result, granted);
            });
            return StoreCheck.Ok;
        }

        public StoreCheck CheckOffer(ShardOffer offer)
        {
            if (offer == null || _catalog == null || _catalog.FindOffer(offer.Id) != offer) return StoreCheck.Unknown;
            return GemWallet.CanAfford(offer.Cost) ? StoreCheck.Ok : StoreCheck.NotEnoughShards;
        }

        /// <summary>Spends the offer's shards and adds its Souls. <paramref name="souls"/> = Souls added (0 unless Ok).</summary>
        public StoreCheck BuyOffer(ShardOffer offer, out int souls)
        {
            souls = 0;
            var check = CheckOffer(offer);
            if (check != StoreCheck.Ok) return check;
            if (!GemWallet.TrySpend(offer.Cost)) return StoreCheck.NotEnoughShards;
            souls = offer.RollSouls(_roll());
            SoulWallet.Earn(souls);
            OfferBought?.Invoke(offer, souls);
            return StoreCheck.Ok;
        }
    }
}
