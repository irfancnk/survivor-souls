using System;

namespace SurvivorSouls.Gameplay.Meta
{
    /// <summary>How one real-money purchase ended.</summary>
    public enum PurchaseOutcome { Purchased, Cancelled, Failed }

    /// <summary>Result of one <see cref="IPurchaseProvider.Purchase"/> call.</summary>
    public readonly struct PurchaseResult
    {
        public readonly string ProductId;
        public readonly PurchaseOutcome Outcome;
        /// <summary>Store transaction id (unique per paid purchase; empty when not purchased).</summary>
        public readonly string TransactionId;
        /// <summary>Short reason for <see cref="PurchaseOutcome.Failed"/> (empty otherwise).</summary>
        public readonly string Error;

        public PurchaseResult(string productId, PurchaseOutcome outcome, string transactionId = "", string error = "")
        {
            ProductId = productId ?? "";
            Outcome = outcome;
            TransactionId = transactionId ?? "";
            Error = error ?? "";
        }

        public bool Purchased => Outcome == PurchaseOutcome.Purchased;
    }

    /// <summary>
    /// Real-money store (KAN-99). One implementation per platform store: StoreKit on iOS and Google Play Billing on Android
    /// later (Unity IAP behind this interface); <see cref="MockPurchaseProvider"/> until then. The game only grants Ember
    /// Shards through <see cref="ShardStore"/>, never from a provider directly. Server-side receipt validation is a
    /// separate follow-up (KAN-89).
    /// </summary>
    public interface IPurchaseProvider
    {
        /// <summary>Short name for logs and the store's sandbox note.</summary>
        string Name { get; }
        /// <summary>No real money moves (mock, or a store sandbox). The store page says so.</summary>
        bool IsSandbox { get; }
        /// <summary>The provider can take a purchase now (store reached, products loaded).</summary>
        bool IsReady { get; }
        /// <summary>
        /// Starts the platform purchase flow for <paramref name="productId"/>. Calls <paramref name="done"/> once, now or
        /// later (the platform sheet may stay open for a while).
        /// </summary>
        void Purchase(string productId, Action<PurchaseResult> done);
    }

    /// <summary>
    /// Sandbox provider (KAN-99): no money, no platform sheet. Every purchase ends with <see cref="NextOutcome"/>, at once
    /// when <see cref="AutoComplete"/> is on, else when <see cref="CompletePending"/> is called (tests of the busy state).
    /// Transaction ids are "mock-1", "mock-2", ...
    /// </summary>
    public sealed class MockPurchaseProvider : IPurchaseProvider
    {
        private Action<PurchaseResult> _pendingDone;
        private string _pendingProduct;
        private int _transactions;

        public string Name => "Sandbox";
        public bool IsSandbox => true;
        public bool IsReady { get; set; } = true;
        /// <summary>Outcome of every purchase from now on.</summary>
        public PurchaseOutcome NextOutcome { get; set; } = PurchaseOutcome.Purchased;
        /// <summary>Purchases end inside <see cref="Purchase"/>. Off: they wait for <see cref="CompletePending"/>.</summary>
        public bool AutoComplete { get; set; } = true;
        /// <summary>Purchase calls so far.</summary>
        public int Requests { get; private set; }
        public bool HasPending => _pendingDone != null;

        public void Purchase(string productId, Action<PurchaseResult> done)
        {
            Requests++;
            if (!IsReady)
            {
                done?.Invoke(new PurchaseResult(productId, PurchaseOutcome.Failed, error: "Store not reachable"));
                return;
            }
            _pendingProduct = productId;
            _pendingDone = done;
            if (AutoComplete) CompletePending(NextOutcome);
        }

        /// <summary>Ends the waiting purchase with <paramref name="outcome"/>. False when none waits.</summary>
        public bool CompletePending(PurchaseOutcome outcome)
        {
            var done = _pendingDone;
            if (done == null) return false;
            _pendingDone = null;
            string tx = outcome == PurchaseOutcome.Purchased ? "mock-" + ++_transactions : "";
            string error = outcome == PurchaseOutcome.Failed ? "Sandbox failure" : "";
            done(new PurchaseResult(_pendingProduct, outcome, tx, error));
            return true;
        }
    }
}
