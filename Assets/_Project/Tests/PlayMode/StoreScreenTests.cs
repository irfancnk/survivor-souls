using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Meta;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-99 acceptance in play mode: the store page on the real MainMenu.uxml (pack cards with amount, bonus, badge and
    /// price; offer cards poor until the shards cover them; tabs swap the shards list and the Items placeholder; the pack
    /// sheet shows PROCESSING while the provider works, a purchase adds shards, closes the sheet and toasts; a cancel keeps
    /// the sheet with a note and adds nothing; an offer trades shards for Souls; Back closes the sheet, then the page),
    /// the menu's STORE button (opens the page; hidden without a catalog) and the MainMenu scene wired to the shipped
    /// catalog. Wallets are kept by <see cref="WalletPrefsGuard"/>.
    /// </summary>
    public sealed class StoreScreenTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Economy/StoreCatalog.asset";
        private readonly List<Object> _objects = new List<Object>();
        private StoreCatalog _catalog;
        private MockPurchaseProvider _provider;

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        [SetUp]
        public void SetUp()
        {
            SoulWallet.Clear();
            GemWallet.Clear();
            _catalog = Track(StoreCatalog.Create(
                new[]
                {
                    new GemPack("test.shards.100", "Ember Pinch", 100, 0, 99),
                    new GemPack("test.shards.550", "Ember Pouch", 500, 50, 499, "POPULAR"),
                },
                new[]
                {
                    new ShardOffer("test-bundle", "Soul Bundle", "Souls in a cloth.", ShardOfferKind.SoulBundle, 120, 2500),
                    new ShardOffer("test-reliquary", "Ashen Reliquary", "Never the same.", ShardOfferKind.SoulReliquary, 60, 800, 2400),
                }));
            _provider = new MockPurchaseProvider();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            SoulWallet.Clear();
            GemWallet.Clear();
        }

        private VisualElement MenuTree(out GameObject go)
        {
            go = Track(new GameObject("Menu"));
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/MainMenu/MainMenuPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MainMenu/MainMenu.uxml");
            return doc.rootVisualElement;
        }

        private static void Press(Button button)
        {
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = button;
            button.SendEvent(e);
        }

        private static string TextOf(VisualElement card, string className) => card.Q<Label>(className: className).text;

        [UnityTest]
        public IEnumerator Page_PackSheet_ProcessingThenGrant_CancelKeepsSheet_OfferTrades_TabsAndBack()
        {
            var root = MenuTree(out _);
            yield return null;
            var page = new StoreScreen(root, new ShardStore(_catalog, _provider, () => 0f));
            var screen = root.Q(StoreScreen.ScreenName);
            Assert.That(screen.ClassListContains("ss-hidden"), Is.True, "closed until STORE");
            Assert.That(screen.parent.pickingMode, Is.EqualTo(PickingMode.Ignore), "the full-screen host never eats menu taps");

            page.Open();
            yield return null;
            yield return null;
            Assert.That(page.IsOpen, Is.True);
            Assert.That(page.Tab, Is.EqualTo(StoreScreen.ShardsTab));
            Assert.That(screen.ClassListContains("sp-screen--closed"), Is.False, "fades in");
            Assert.That(root.Q<Label>("sp-sandbox").ClassListContains("ss-hidden"), Is.False, "sandbox note with the mock");

            // Cards.
            var pouch = page.CardOf("test.shards.550");
            Assert.That(screen.Query(className: "sp-pack").ToList().Count, Is.EqualTo(2));
            Assert.That(TextOf(pouch, "sp-pack__amount"), Is.EqualTo("550"));
            Assert.That(TextOf(pouch, "sp-pack__bonus"), Is.EqualTo("+50 BONUS"));
            Assert.That(TextOf(pouch, "sp-pack__price"), Is.EqualTo("$4.99"));
            Assert.That(TextOf(pouch, "sp-badge"), Is.EqualTo("POPULAR"));
            Assert.That(pouch.Query(className: "sp-shard").ToList().Count, Is.EqualTo(2), "second pack: two diamonds");
            Assert.That(page.CardOf("test.shards.100").Q(className: "sp-badge"), Is.Null, "no badge");
            var bundle = page.CardOf("test-bundle");
            Assert.That(TextOf(bundle, "sp-offer__reward"), Is.EqualTo("2,500 Souls"));
            Assert.That(TextOf(bundle, "sp-offer__price"), Is.EqualTo("120"));
            Assert.That(bundle.ClassListContains("sp-offer--poor"), Is.True, "no shards yet");
            Assert.That(TextOf(page.CardOf("test-reliquary"), "sp-offer__reward"), Is.EqualTo("800 - 2,400 Souls"));

            // Pack sheet: PROCESSING while the provider works, then the shards arrive, the sheet closes and a toast shows.
            _provider.AutoComplete = false;
            var bought = 0;
            page.Bought += () => bought++;
            Assert.That(page.OpenDetail("test.shards.550"), Is.True);
            yield return null;
            Assert.That(page.IsDetailOpen, Is.True);
            Assert.That(pouch.ClassListContains("sp-pack--selected"), Is.True);
            Assert.That(root.Q<Label>("sp-detail-get").text, Is.EqualTo("550 Ember Shards"));
            Assert.That(root.Q<Label>("sp-detail-extra").text, Is.EqualTo("+50 included"));
            Assert.That(root.Q<Label>("sp-detail-price").text, Is.EqualTo("$4.99"));
            Assert.That(page.BuyText, Is.EqualTo("BUY $4.99"));
            Assert.That(page.CanBuy, Is.True);
            Press(root.Q<Button>("sp-buy"));
            yield return null;
            Assert.That(page.BuyText, Is.EqualTo("PROCESSING..."));
            Assert.That(page.CanBuy, Is.False, "no second tap while busy");
            Assert.That(GemWallet.Balance, Is.Zero);
            _provider.CompletePending(PurchaseOutcome.Purchased);
            yield return null;
            Assert.That(GemWallet.Balance, Is.EqualTo(550));
            Assert.That(root.Q<Label>("sp-shards").text, Is.EqualTo("550"));
            Assert.That(page.IsDetailOpen, Is.False, "success closes the sheet");
            Assert.That(page.ToastVisible, Is.True);
            StringAssert.Contains("+550 Ember Shards", page.ToastText);
            Assert.That(bought, Is.EqualTo(1));
            Assert.That(bundle.ClassListContains("sp-offer--poor"), Is.False, "shards now cover the bundle");

            // Cancel: sheet stays, note says nothing was charged, balance unchanged.
            _provider.AutoComplete = true;
            _provider.NextOutcome = PurchaseOutcome.Cancelled;
            page.OpenDetail("test.shards.100");
            Assert.That(root.Q("sp-row-extra").ClassListContains("ss-hidden"), Is.True, "no bonus row");
            Assert.That(page.BuySelected(), Is.EqualTo(StoreCheck.Ok));
            Assert.That(page.IsDetailOpen, Is.True);
            Assert.That(root.Q<Label>("sp-detail-note").text, Does.Contain("cancelled"));
            Assert.That(GemWallet.Balance, Is.EqualTo(550));
            Assert.That(page.BuyText, Is.EqualTo("BUY $0.99"), "free to try again");

            // Offline provider: button off and says so.
            _provider.IsReady = false;
            page.Refresh();
            Assert.That(page.BuyText, Is.EqualTo("STORE OFFLINE"));
            Assert.That(page.CanBuy, Is.False);
            _provider.IsReady = true;

            // Offer: trade shards for Souls.
            page.OpenDetail("test-bundle");
            Assert.That(page.SelectedOffer.Id, Is.EqualTo("test-bundle"));
            Assert.That(page.SelectedPack, Is.Null);
            Assert.That(root.Q<Label>("sp-detail-price").text, Is.EqualTo("120 Ember Shards"));
            Assert.That(root.Q<Label>("sp-detail-extra").text, Is.EqualTo("550 Ember Shards"), "YOU HOLD row");
            Assert.That(page.BuyText, Is.EqualTo("TRADE 120 SHARDS"));
            Press(root.Q<Button>("sp-buy"));
            yield return null;
            Assert.That(GemWallet.Balance, Is.EqualTo(430));
            Assert.That(SoulWallet.Balance, Is.EqualTo(2500));
            Assert.That(root.Q<Label>("sp-souls").text, Is.EqualTo("2,500"));
            StringAssert.Contains("+2,500 Souls", page.ToastText);
            Assert.That(page.IsDetailOpen, Is.False);

            // Not enough shards for an offer: sheet says so.
            GemWallet.Clear();
            page.OpenDetail("test-reliquary");
            Assert.That(page.BuyText, Is.EqualTo("NOT ENOUGH SHARDS"));
            Assert.That(page.CanBuy, Is.False);
            Assert.That(page.BuySelected(), Is.EqualTo(StoreCheck.NotEnoughShards));
            Assert.That(SoulWallet.Balance, Is.EqualTo(2500));
            Assert.That(page.OpenDetail("nope"), Is.False);

            // Tabs: Items shows the placeholder and closes the sheet.
            Press(root.Q<Button>("sp-tab-items"));
            yield return null;
            Assert.That(page.Tab, Is.EqualTo(StoreScreen.ItemsTab));
            Assert.That(page.IsDetailOpen, Is.False);
            Assert.That(root.Q("sp-items").ClassListContains("ss-hidden"), Is.False);
            Assert.That(root.Q("sp-list").ClassListContains("ss-hidden"), Is.True);
            Assert.That(root.Q<Button>("sp-tab-items").ClassListContains("sp-tab--active"), Is.True);
            Press(root.Q<Button>("sp-tab-shards"));
            Assert.That(page.Tab, Is.EqualTo(StoreScreen.ShardsTab));
            Assert.That(root.Q("sp-items").ClassListContains("ss-hidden"), Is.True);

            // Back: sheet first, then the page.
            page.OpenDetail("test-bundle");
            Assert.That(page.Back(), Is.True);
            Assert.That(page.IsDetailOpen, Is.False);
            Assert.That(page.IsOpen, Is.True);
            Assert.That(page.Back(), Is.True);
            Assert.That(page.IsOpen, Is.False);
            Assert.That(page.Back(), Is.False);
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(screen.ClassListContains("ss-hidden"), Is.True, "hidden after the fade");
        }

        [UnityTest]
        public IEnumerator Menu_StoreButton_OpensThePage_BuyThroughTheMenuProvider_HiddenWithoutACatalog()
        {
            MenuTree(out var go);
            go.SetActive(false);
            var menu = go.AddComponent<MainMenuController>();
            menu.Setup(null, null, null, _catalog);
            menu.StoreProvider = _provider;
            go.SetActive(true);
            yield return null;
            Assert.That(menu.StorePage, Is.Not.Null);
            Assert.That(menu.StorePage.Store.Provider, Is.SameAs(_provider));
            var root = go.GetComponent<UIDocument>().rootVisualElement;
            var button = root.Q<Button>("btn-store");
            Assert.That(button.ClassListContains("ss-hidden"), Is.False);
            Press(button);
            yield return null;
            Assert.That(menu.StorePage.IsOpen, Is.True);
            menu.StorePage.OpenDetail("test.shards.100");
            menu.StorePage.BuySelected();
            Assert.That(GemWallet.Balance, Is.EqualTo(100));
            Assert.That(root.Q<Label>("lbl-wallet-gems").text, Is.EqualTo("100"), "menu pill follows the wallet");
            Assert.That(menu.Back(), Is.True);
            Assert.That(menu.StorePage.IsOpen, Is.False);

            MenuTree(out var bare);
            bare.SetActive(false);
            var plain = bare.AddComponent<MainMenuController>();
            plain.Setup(null);
            bare.SetActive(true);
            yield return null;
            Assert.That(plain.StorePage, Is.Null);
            Assert.That(plain.StoreProvider, Is.InstanceOf<MockPurchaseProvider>(), "sandbox by default");
            Assert.That(bare.GetComponent<UIDocument>().rootVisualElement.Q<Button>("btn-store").ClassListContains("ss-hidden"), Is.True);
        }

        [Test]
        public void Scene_WiresTheShippedCatalog()
        {
            Assert.That(UnityEditor.AssetDatabase.LoadAssetAtPath<StoreCatalog>(CatalogPath), Is.Not.Null);
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(CatalogPath);
            StringAssert.Contains($"storeCatalog: {{fileID: 11400000, guid: {guid}",
                System.IO.File.ReadAllText("Assets/_Project/Scenes/MainMenu.unity"), "MainMenu controller wired to the catalog");
        }
    }
}
