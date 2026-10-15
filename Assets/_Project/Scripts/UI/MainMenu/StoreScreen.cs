using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Meta;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>
    /// Store page (KAN-99), the Store.uxml tree inside the main menu. Two tabs: EMBER SHARDS (pack cards for real money,
    /// then shard offers that turn shards into Souls) and ITEMS (placeholder until KAN-90). Tapping a card opens the detail
    /// sheet (what you get, bonus / chance, price); its button buys through <see cref="ShardStore"/>: a pack waits for the
    /// <see cref="IPurchaseProvider"/> (button says PROCESSING), an offer spends shards at once. A success closes the sheet
    /// and shows a toast; a cancel or failure keeps the sheet open with a note, and nothing is taken. The Souls and Shards
    /// pills follow both wallets. A sandbox provider shows "no real money" under the packs. <see cref="Back"/> closes the
    /// sheet first, then the page. Plain class, owned by <see cref="MainMenuController"/>; tests build it on the
    /// MainMenu.uxml tree.
    /// </summary>
    public sealed class StoreScreen
    {
        public const string ScreenName = "sp-screen";
        public const string PackPrefix = "sp-pack-";
        public const string OfferPrefix = "sp-offer-";
        public const string ShardsTab = "shards";
        public const string ItemsTab = "items";
        private const string HiddenClass = "ss-hidden";
        private const string ScreenClosedClass = "sp-screen--closed";
        private const string VeilClosedClass = "sp-veil--closed";
        private const string DetailClosedClass = "sp-detail--closed";
        private const string TabActiveClass = "sp-tab--active";
        private const string PackSelectedClass = "sp-pack--selected";
        private const string OfferSelectedClass = "sp-offer--selected";
        private const string PackBoughtClass = "sp-pack--bought";
        private const string OfferBoughtClass = "sp-offer--bought";
        private const string PoorClass = "sp-offer--poor";
        private const string PillPopClass = "sp-pill--pop";
        /// <summary>Milliseconds before a closed part is hidden: a little longer than the USS transitions.</summary>
        private const long HideAfterMs = 260;
        private const long PopMs = 140;
        private const long ToastMs = 2600;

        private readonly ShardStore _store;
        private readonly VisualElement _screen, _veil, _detail, _detailIcon, _items, _extraRow, _soulsPill, _shardsPill;
        private readonly ScrollView _list;
        private readonly Label _souls, _shards, _name, _kind, _desc, _get, _extraKey, _extra, _priceKey, _price, _note, _toast, _sandbox;
        private readonly Button _buy, _tabShards, _tabItems;
        private readonly Dictionary<GemPack, Button> _packCards = new Dictionary<GemPack, Button>();
        private readonly Dictionary<ShardOffer, Button> _offerCards = new Dictionary<ShardOffer, Button>();
        private IVisualElementScheduledItem _screenHide, _veilHide, _detailHide, _toastHide;
        private string _lastNote;

        public bool IsOpen { get; private set; }
        public bool IsDetailOpen { get; private set; }
        /// <summary>"shards" or "items".</summary>
        public string Tab { get; private set; } = ShardsTab;
        /// <summary>Pack of the open (or last opened) sheet; null when an offer is selected.</summary>
        public GemPack SelectedPack { get; private set; }
        /// <summary>Offer of the open (or last opened) sheet; null when a pack is selected.</summary>
        public ShardOffer SelectedOffer { get; private set; }
        /// <summary>The sheet's buy button is on.</summary>
        public bool CanBuy => _buy != null && _buy.enabledSelf;
        public string BuyText => _buy.text;
        /// <summary>Text of the last result toast (tests, screenshots).</summary>
        public string ToastText => _toast.text;
        public bool ToastVisible => !_toast.ClassListContains(HiddenClass);
        public VisualElement Page => _screen;
        public ShardStore Store => _store;

        /// <summary>Shards arrived from a pack, or Souls from an offer (the menu plays feedback).</summary>
        public event Action Bought;

        /// <param name="root">Tree that holds Store.uxml (the main menu root).</param>
        public StoreScreen(VisualElement root, ShardStore store)
        {
            _store = store;
            _screen = root.Q(ScreenName);
            if (_screen == null) throw new InvalidOperationException($"Store: '{ScreenName}' missing in the menu UXML");
            // The instanced template fills the screen; only the page itself may take taps.
            if (_screen.parent != null && _screen.parent != root) _screen.parent.pickingMode = PickingMode.Ignore;

            _list = _screen.Q<ScrollView>("sp-list");
            _items = _screen.Q("sp-items");
            _souls = _screen.Q<Label>("sp-souls");
            _shards = _screen.Q<Label>("sp-shards");
            _soulsPill = _screen.Q("sp-souls-pill");
            _shardsPill = _screen.Q("sp-shards-pill");
            _sandbox = _screen.Q<Label>("sp-sandbox");
            _toast = _screen.Q<Label>("sp-toast");
            _tabShards = _screen.Q<Button>("sp-tab-shards");
            _tabItems = _screen.Q<Button>("sp-tab-items");
            _veil = _screen.Q("sp-veil");
            _detail = _screen.Q("sp-detail");
            _detailIcon = _screen.Q("sp-detail-icon");
            _name = _screen.Q<Label>("sp-detail-name");
            _kind = _screen.Q<Label>("sp-detail-kind");
            _desc = _screen.Q<Label>("sp-detail-desc");
            _get = _screen.Q<Label>("sp-detail-get");
            _extraRow = _screen.Q("sp-row-extra");
            _extraKey = _screen.Q<Label>("sp-detail-extra-key");
            _extra = _screen.Q<Label>("sp-detail-extra");
            _priceKey = _screen.Q<Label>("sp-detail-price-key");
            _price = _screen.Q<Label>("sp-detail-price");
            _note = _screen.Q<Label>("sp-detail-note");
            _buy = _screen.Q<Button>("sp-buy");

            _screen.Q<Button>("sp-back").clicked += () => Back();
            _screen.Q<Button>("sp-detail-back").clicked += CloseDetail;
            _veil.RegisterCallback<PointerDownEvent>(_ => CloseDetail());
            _tabShards.clicked += () => ShowTab(ShardsTab);
            _tabItems.clicked += () => ShowTab(ItemsTab);
            _buy.clicked += OnBuy;

            BuildCards();
        }

        /// <summary>Shows the page on the shards tab with fresh balances.</summary>
        public void Open()
        {
            Refresh();
            ShowTab(ShardsTab);
            _screenHide?.Pause();
            IsOpen = true;
            Show(_screen, ScreenClosedClass);
            _list.scrollOffset = Vector2.zero;
        }

        public void Close()
        {
            if (!IsOpen) return;
            CloseDetail();
            IsOpen = false;
            _screenHide = Hide(_screen, ScreenClosedClass);
        }

        /// <summary>Back button / Esc: closes the sheet, else the page. False when nothing was open.</summary>
        public bool Back()
        {
            if (IsDetailOpen)
            {
                CloseDetail();
                return true;
            }
            if (!IsOpen) return false;
            Close();
            return true;
        }

        /// <summary>"shards" or "items". Unknown names are ignored.</summary>
        public void ShowTab(string tab)
        {
            if (tab != ShardsTab && tab != ItemsTab) return;
            Tab = tab;
            bool shards = tab == ShardsTab;
            _tabShards.EnableInClassList(TabActiveClass, shards);
            _tabItems.EnableInClassList(TabActiveClass, !shards);
            _list.EnableInClassList(HiddenClass, !shards);
            _items.EnableInClassList(HiddenClass, shards);
            if (!shards) CloseDetail();
        }

        /// <summary>Opens the sheet of the pack or offer with <paramref name="id"/> (opens the page first). False if unknown.</summary>
        public bool OpenDetail(string id)
        {
            var catalog = _store?.Catalog;
            var pack = catalog != null ? catalog.FindPack(id) : null;
            var offer = pack == null && catalog != null ? catalog.FindOffer(id) : null;
            if (pack == null && offer == null) return false;
            if (!IsOpen) Open();
            if (Tab != ShardsTab) ShowTab(ShardsTab);
            SelectedPack = pack;
            SelectedOffer = offer;
            _lastNote = null;
            foreach (var kv in _packCards) kv.Value.EnableInClassList(PackSelectedClass, kv.Key == pack);
            foreach (var kv in _offerCards) kv.Value.EnableInClassList(OfferSelectedClass, kv.Key == offer);
            RefreshDetail();
            _veilHide?.Pause();
            _detailHide?.Pause();
            IsDetailOpen = true;
            Show(_veil, VeilClosedClass);
            Show(_detail, DetailClosedClass);
            return true;
        }

        public void CloseDetail()
        {
            if (!IsDetailOpen) return;
            IsDetailOpen = false;
            foreach (var card in _packCards.Values) card.RemoveFromClassList(PackSelectedClass);
            foreach (var card in _offerCards.Values) card.RemoveFromClassList(OfferSelectedClass);
            _veilHide = Hide(_veil, VeilClosedClass);
            _detailHide = Hide(_detail, DetailClosedClass);
        }

        /// <summary>Card of the pack or offer with <paramref name="id"/> (tests, screenshots); null if not in the catalog.</summary>
        public Button CardOf(string id)
        {
            foreach (var kv in _packCards)
                if (kv.Key.Id == id) return kv.Value;
            foreach (var kv in _offerCards)
                if (kv.Key.Id == id) return kv.Value;
            return null;
        }

        /// <summary>Buys what the open sheet shows (the sheet's button). Returns the check.</summary>
        public StoreCheck BuySelected()
        {
            if (_store == null) return StoreCheck.Unknown;
            if (SelectedPack != null)
            {
                var pack = SelectedPack;
                _lastNote = null;
                var check = _store.BuyPack(pack, (result, granted) => OnPackDone(pack, result, granted));
                RefreshDetail(); // PROCESSING while the provider is busy
                return check;
            }
            if (SelectedOffer != null)
            {
                var offer = SelectedOffer;
                var check = _store.BuyOffer(offer, out int souls);
                if (check == StoreCheck.Ok)
                {
                    Refresh();
                    if (_offerCards.TryGetValue(offer, out var card)) Pop(card, OfferBoughtClass);
                    Pop(_soulsPill, PillPopClass);
                    Pop(_shardsPill, PillPopClass);
                    CloseDetail();
                    ShowToast($"+{PlayerHealthHud.FormatCount(souls)} Souls from the {offer.Title}.");
                    Bought?.Invoke();
                }
                else RefreshDetail();
                return check;
            }
            return StoreCheck.Unknown;
        }

        /// <summary>Reads both wallets again and updates pills, offer states and the sheet (also on every open and buy).</summary>
        public void Refresh()
        {
            _souls.text = PlayerHealthHud.FormatCount(SoulWallet.Balance);
            int shards = GemWallet.Balance;
            _shards.text = PlayerHealthHud.FormatCount(shards);
            foreach (var kv in _offerCards) kv.Value.EnableInClassList(PoorClass, shards < kv.Key.Cost);
            var provider = _store?.Provider;
            _sandbox.EnableInClassList(HiddenClass, provider == null || !provider.IsSandbox);
            if (IsDetailOpen) RefreshDetail();
        }

        private void OnPackDone(GemPack pack, PurchaseResult result, int granted)
        {
            Refresh();
            if (granted > 0)
            {
                if (_packCards.TryGetValue(pack, out var card)) Pop(card, PackBoughtClass);
                Pop(_shardsPill, PillPopClass);
                if (SelectedPack == pack) CloseDetail();
                ShowToast($"+{PlayerHealthHud.FormatCount(granted)} Ember Shards. The flame thanks you.");
                Bought?.Invoke();
                return;
            }
            _lastNote = result.Outcome == PurchaseOutcome.Cancelled ? "Purchase cancelled. Nothing was charged."
                : result.Outcome == PurchaseOutcome.Failed ? "Purchase failed. Nothing was charged. Try again later."
                : "Purchase not delivered. Contact support if you were charged.";
            if (SelectedPack == pack) RefreshDetail();
        }

        private void RefreshDetail()
        {
            if (SelectedPack != null) RefreshPackDetail(SelectedPack);
            else if (SelectedOffer != null) RefreshOfferDetail(SelectedOffer);
        }

        private void RefreshPackDetail(GemPack pack)
        {
            var check = _store.CheckPack(pack);
            _name.text = pack.Title;
            _kind.text = "SHARD PACK";
            _desc.text = "Ember Shards, the rare currency of the realm.";
            _get.text = $"{PlayerHealthHud.FormatCount(pack.TotalShards)} Ember Shards";
            _extraRow.EnableInClassList(HiddenClass, pack.Bonus <= 0);
            _extraKey.text = "BONUS";
            _extra.text = $"+{PlayerHealthHud.FormatCount(pack.Bonus)} included";
            _priceKey.text = "PRICE";
            _price.text = pack.PriceText;
            SetIcon(true, PackTier(pack), ShardOfferKind.SoulBundle);
            string note = _lastNote
                ?? (check == StoreCheck.NotReady ? "The store cannot be reached now. Try again later." : null)
                ?? (_store.Provider != null && _store.Provider.IsSandbox ? "Sandbox: no real money is charged." : null);
            SetNote(note);
            _buy.SetEnabled(check == StoreCheck.Ok);
            _buy.text = check == StoreCheck.Busy ? "PROCESSING..." : check == StoreCheck.NotReady ? "STORE OFFLINE" : "BUY " + pack.PriceText;
        }

        private void RefreshOfferDetail(ShardOffer offer)
        {
            var check = _store.CheckOffer(offer);
            _name.text = offer.Title;
            _kind.text = offer.Kind == ShardOfferKind.SoulReliquary ? "RELIQUARY" : "SOUL BUNDLE";
            _desc.text = offer.Description;
            _get.text = offer.RewardText;
            _extraRow.RemoveFromClassList(HiddenClass);
            _extraKey.text = "YOU HOLD";
            _extra.text = $"{PlayerHealthHud.FormatCount(GemWallet.Balance)} Ember Shards";
            _priceKey.text = "COST";
            _price.text = $"{PlayerHealthHud.FormatCount(offer.Cost)} Ember Shards";
            SetIcon(false, offer.Cost >= 300 ? 1 : 0, offer.Kind);
            SetNote(check == StoreCheck.NotEnoughShards ? "Not enough Ember Shards. Buy a pack above." : null);
            _buy.SetEnabled(check == StoreCheck.Ok);
            _buy.text = check == StoreCheck.NotEnoughShards ? "NOT ENOUGH SHARDS" : $"TRADE {PlayerHealthHud.FormatCount(offer.Cost)} SHARDS";
        }

        private void SetNote(string note)
        {
            _note.text = note ?? "";
            _note.EnableInClassList(HiddenClass, string.IsNullOrEmpty(note));
        }

        private void SetIcon(bool pack, int tier, ShardOfferKind kind)
        {
            _detailIcon.Clear();
            if (pack) _detailIcon.Add(ShardCluster(Mathf.Min(tier + 1, 3)));
            else _detailIcon.Add(OfferGlyph(kind, tier > 0));
        }

        private void OnBuy()
        {
            if (!IsDetailOpen) return;
            BuySelected();
        }

        /// <summary>0 for the first pack in the catalog, 1 for the next, ...: more diamonds on bigger packs.</summary>
        private int PackTier(GemPack pack)
        {
            var packs = _store?.Catalog?.Packs;
            if (packs == null) return 0;
            for (int i = 0; i < packs.Count; i++)
                if (packs[i] == pack) return i;
            return 0;
        }

        private void BuildCards()
        {
            var packsRoot = _screen.Q("sp-packs");
            var offersRoot = _screen.Q("sp-offers");
            packsRoot.Clear();
            offersRoot.Clear();
            _packCards.Clear();
            _offerCards.Clear();
            var catalog = _store?.Catalog;
            if (catalog == null) return;

            for (int i = 0; i < catalog.Packs.Count; i++)
            {
                var pack = catalog.Packs[i];
                if (pack == null) continue;
                var card = new Button { name = PackPrefix + pack.Id, text = "" };
                card.AddToClassList("sp-pack");
                string id = pack.Id;
                card.clicked += () => OpenDetail(id);
                if (!string.IsNullOrEmpty(pack.Badge)) Text(card, "sp-badge", pack.Badge);
                card.Add(ShardCluster(Mathf.Min(i + 1, 5)));
                Text(card, "sp-pack__amount", PlayerHealthHud.FormatCount(pack.TotalShards));
                Text(card, "sp-pack__bonus", pack.Bonus > 0 ? $"+{PlayerHealthHud.FormatCount(pack.Bonus)} BONUS" : "");
                Text(card, "sp-pack__price", pack.PriceText);
                packsRoot.Add(card);
                _packCards[pack] = card;
            }

            foreach (var offer in catalog.Offers)
            {
                if (offer == null) continue;
                var card = new Button { name = OfferPrefix + offer.Id, text = "" };
                card.AddToClassList("sp-offer");
                string id = offer.Id;
                card.clicked += () => OpenDetail(id);
                var icon = Child(card, "sp-icon");
                icon.Add(OfferGlyph(offer.Kind, offer.Cost >= 300));
                var texts = Child(card, "sp-offer__texts");
                Text(texts, "sp-offer__name", offer.Title);
                Text(texts, "sp-offer__reward", offer.RewardText);
                var cost = Child(card, "sp-offer__cost");
                Child(cost, "sp-offer__gem");
                Text(cost, "sp-offer__price", PlayerHealthHud.FormatCount(offer.Cost));
                offersRoot.Add(card);
                _offerCards[offer] = card;
            }
            Refresh();
        }

        private static VisualElement ShardCluster(int count)
        {
            var cluster = new VisualElement { pickingMode = PickingMode.Ignore };
            cluster.AddToClassList("sp-shard-cluster");
            for (int i = 0; i < count; i++) Child(cluster, "sp-shard");
            return cluster;
        }

        private static VisualElement OfferGlyph(ShardOfferKind kind, bool big)
        {
            if (kind == ShardOfferKind.SoulReliquary)
            {
                var chest = new VisualElement { pickingMode = PickingMode.Ignore };
                chest.AddToClassList("sp-glyph-chest");
                Child(chest, "sp-glyph-chest__lock");
                return chest;
            }
            var soul = new VisualElement { pickingMode = PickingMode.Ignore };
            soul.AddToClassList("sp-glyph-soul");
            soul.EnableInClassList("sp-glyph-soul--big", big);
            return soul;
        }

        private void ShowToast(string message)
        {
            _toast.text = message;
            _toast.RemoveFromClassList(HiddenClass);
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.AddToClassList(HiddenClass)).StartingIn(ToastMs);
        }

        private static VisualElement Child(VisualElement parent, string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            parent.Add(element);
            return element;
        }

        private static Label Text(VisualElement parent, string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        /// <summary>Class on now, off after a short beat: the USS scale transition plays out and back.</summary>
        private static void Pop(VisualElement element, string className)
        {
            if (element == null) return;
            element.AddToClassList(className);
            element.schedule.Execute(() => element.RemoveFromClassList(className)).StartingIn(PopMs);
        }

        /// <summary>display on now, the closed class off next frame so the USS transition plays.</summary>
        private static void Show(VisualElement element, string closedClass)
        {
            element.RemoveFromClassList(HiddenClass);
            element.schedule.Execute(() => element.RemoveFromClassList(closedClass));
        }

        /// <summary>Closed class on now (transition out), display none after it.</summary>
        private static IVisualElementScheduledItem Hide(VisualElement element, string closedClass)
        {
            element.AddToClassList(closedClass);
            return element.schedule.Execute(() => element.AddToClassList(HiddenClass)).StartingIn(HideAfterMs);
        }
    }
}
