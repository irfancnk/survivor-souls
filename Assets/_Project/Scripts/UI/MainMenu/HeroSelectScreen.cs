using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Heroes;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>What a hero card's button does now.</summary>
    public enum HeroCardState
    {
        /// <summary>The picked hero: the button only says so.</summary>
        Current,
        /// <summary>Unlocked (owned, free or Test Mode), not picked: tap picks it.</summary>
        Select,
        /// <summary>Locked and the wallet covers the price: first tap asks to confirm, second tap buys and picks.</summary>
        Buy,
        /// <summary>Locked and too expensive: tap says how much is missing.</summary>
        Poor,
    }

    /// <summary>
    /// Hero page (KAN-153, first slice of the KAN-100 roster), the HeroSelect.uxml tree inside the main menu. One card per
    /// <see cref="HeroRoster"/> hero: portrait, name, tagline, strengths (green) and weaknesses (red) against the Knight,
    /// price chip and one button (<see cref="HeroCardState"/>). Buying is a two-tap confirm (the price is high); a buy
    /// spends the price through <see cref="HeroProgress.TryBuy"/> and picks the hero. Test Mode (KAN-144): every hero is
    /// free and can be picked, nothing is bought or charged, a note says so. Pills follow both wallets.
    /// Plain class, owned by <see cref="MainMenuController"/>; tests build it on the MainMenu.uxml tree.
    /// </summary>
    public sealed class HeroSelectScreen
    {
        public const string ScreenName = "hs-screen";
        public const string CardPrefix = "hs-card-";
        public const string ActionPrefix = "hs-action-";
        private const string HiddenClass = "ss-hidden";
        private const string ScreenClosedClass = "hs-screen--closed";
        private const string SelectedClass = "hs-card--selected";
        private const string LockedClass = "hs-card--locked";
        private const string PillPopClass = "hs-pill--pop";
        private static readonly string[] ActionClasses = { "hs-action--current", "", "hs-action--buy", "hs-action--poor" };
        private const string ConfirmClass = "hs-action--confirm";
        private const long HideAfterMs = 260;
        private const long PopMs = 140;
        private const long ToastMs = 2600;
        /// <summary>Milliseconds a "confirm" button waits for the second tap.</summary>
        public const long ConfirmMs = 3000;

        private sealed class Card
        {
            public HeroDefinition Hero;
            public VisualElement Root, Badge, Price, PriceGem;
            public Label PriceValue;
            public Button Action;
        }

        private readonly HeroRoster _roster;
        private readonly VisualElement _screen, _soulsPill, _shardsPill;
        private readonly ScrollView _list;
        private readonly Label _souls, _shards, _testNote, _toast;
        private readonly List<Card> _cards = new List<Card>();
        private IVisualElementScheduledItem _screenHide, _toastHide, _confirmReset;
        private HeroDefinition _confirming;

        public bool IsOpen { get; private set; }
        public VisualElement Page => _screen;
        public HeroRoster Roster => _roster;
        /// <summary>The hero the next run starts with.</summary>
        public HeroDefinition Current => HeroProgress.Current(_roster);
        /// <summary>Hero whose buy button waits for the confirm tap (null = none).</summary>
        public HeroDefinition Confirming => _confirming;
        public string ToastText => _toast.text;
        public bool ToastVisible => !_toast.ClassListContains(HiddenClass);
        public bool TestNoteVisible => !_testNote.ClassListContains(HiddenClass);

        /// <summary>A hero was picked (by select or by buy).</summary>
        public event Action<HeroDefinition> Picked;
        /// <summary>A hero was bought (the menu plays feedback).</summary>
        public event Action<HeroDefinition> Bought;

        /// <param name="root">Tree that holds HeroSelect.uxml (the main menu root).</param>
        public HeroSelectScreen(VisualElement root, HeroRoster roster)
        {
            _roster = roster;
            _screen = root.Q(ScreenName);
            if (_screen == null) throw new InvalidOperationException($"Heroes: '{ScreenName}' missing in the menu UXML");
            // The instanced template fills the screen; only the page itself may take taps.
            if (_screen.parent != null && _screen.parent != root) _screen.parent.pickingMode = PickingMode.Ignore;
            _list = _screen.Q<ScrollView>("hs-list");
            _souls = _screen.Q<Label>("hs-souls");
            _shards = _screen.Q<Label>("hs-shards");
            _soulsPill = _screen.Q("hs-souls-pill");
            _shardsPill = _screen.Q("hs-shards-pill");
            _testNote = _screen.Q<Label>("hs-test-note");
            _toast = _screen.Q<Label>("hs-toast");
            _screen.Q<Button>("hs-back").clicked += () => Back();
            BuildCards();
        }

        public void Open()
        {
            CancelConfirm();
            Refresh();
            _screenHide?.Pause();
            IsOpen = true;
            _screen.RemoveFromClassList(HiddenClass);
            _screen.schedule.Execute(() => _screen.RemoveFromClassList(ScreenClosedClass));
            _list.scrollOffset = Vector2.zero;
        }

        public void Close()
        {
            if (!IsOpen) return;
            CancelConfirm();
            IsOpen = false;
            _screen.AddToClassList(ScreenClosedClass);
            _screenHide = _screen.schedule.Execute(() => _screen.AddToClassList(HiddenClass)).StartingIn(HideAfterMs);
        }

        /// <summary>Back button / Esc: a waiting confirm first, then the page. False when nothing was open.</summary>
        public bool Back()
        {
            if (!IsOpen) return false;
            if (_confirming != null)
            {
                CancelConfirm();
                return true;
            }
            Close();
            return true;
        }

        /// <summary>Button state of <paramref name="hero"/> now.</summary>
        public HeroCardState StateOf(HeroDefinition hero)
        {
            if (hero == Current) return HeroCardState.Current;
            if (HeroProgress.IsUnlocked(hero)) return HeroCardState.Select;
            return HeroProgress.Check(hero) == HeroBuyCheck.Ok ? HeroCardState.Buy : HeroCardState.Poor;
        }

        public HeroCardState StateOf(string id) => StateOf(_roster.Find(id));

        /// <summary>Card root of the hero with <paramref name="id"/> (tests, screenshots); null if unknown.</summary>
        public VisualElement CardOf(string id) => Find(id)?.Root;

        /// <summary>Button of the hero card (tests, screenshots); null if unknown.</summary>
        public Button ActionOf(string id) => Find(id)?.Action;

        /// <summary>Price chip text of the hero card ("1,000", "OWNED", "FREE: TEST MODE"); null if unknown.</summary>
        public string PriceTextOf(string id) => Find(id)?.PriceValue.text;

        /// <summary>The card button (tests and dev screenshots press it too). Returns the state the press acted on.</summary>
        public HeroCardState Press(string id)
        {
            var hero = _roster.Find(id);
            if (hero == null) return HeroCardState.Current;
            var state = StateOf(hero);
            switch (state)
            {
                case HeroCardState.Select:
                    CancelConfirm();
                    if (HeroProgress.Select(_roster, hero))
                    {
                        ShowToast($"{hero.DisplayName} walks with you.");
                        Picked?.Invoke(hero);
                    }
                    break;
                case HeroCardState.Buy:
                    if (_confirming != hero)
                    {
                        Confirm(hero);
                        break;
                    }
                    CancelConfirm();
                    if (HeroProgress.TryBuy(hero) == HeroBuyCheck.Ok)
                    {
                        HeroProgress.Select(_roster, hero);
                        Pop(hero.Currency == HeroCurrency.Souls ? _soulsPill : _shardsPill);
                        ShowToast($"{hero.DisplayName} joins you.");
                        Bought?.Invoke(hero);
                        Picked?.Invoke(hero);
                    }
                    break;
                case HeroCardState.Poor:
                    CancelConfirm();
                    int missing = hero.Price - HeroProgress.Balance(hero.Currency);
                    ShowToast($"Need {PlayerHealthHud.FormatCount(missing)} more {CurrencyName(hero.Currency)}.");
                    break;
            }
            Refresh();
            return state;
        }

        /// <summary>Reads wallets, Test Mode and the pick again; updates every card (also on open and after each press).</summary>
        public void Refresh()
        {
            _souls.text = PlayerHealthHud.FormatCount(SoulWallet.Balance);
            _shards.text = PlayerHealthHud.FormatCount(GemWallet.Balance);
            _testNote.EnableInClassList(HiddenClass, !TestMode.IsOn);
            foreach (var card in _cards) RefreshCard(card);
        }

        private void RefreshCard(Card card)
        {
            var hero = card.Hero;
            var state = StateOf(hero);
            bool owned = HeroProgress.IsOwned(hero);
            card.Root.EnableInClassList(SelectedClass, state == HeroCardState.Current);
            card.Root.EnableInClassList(LockedClass, !owned && !TestMode.IsOn);
            card.Badge.EnableInClassList(HiddenClass, state != HeroCardState.Current);
            bool free = !owned && TestMode.IsOn;
            card.PriceValue.text = owned ? (hero.IsFree ? "FREE" : "OWNED") : free ? "FREE: TEST MODE" : PlayerHealthHud.FormatCount(hero.Price);
            card.PriceGem.EnableInClassList(HiddenClass, owned || free);
            card.PriceGem.EnableInClassList("hs-price__gem--souls", hero.Currency == HeroCurrency.Souls);
            card.Price.EnableInClassList("hs-price--free", free);
            for (int i = 0; i < ActionClasses.Length; i++)
                if (ActionClasses[i].Length > 0) card.Action.EnableInClassList(ActionClasses[i], (int)state == i);
            bool confirming = state == HeroCardState.Buy && _confirming == hero;
            card.Action.EnableInClassList(ConfirmClass, confirming);
            card.Action.text = state switch
            {
                HeroCardState.Current => "SELECTED",
                HeroCardState.Select => "SELECT",
                HeroCardState.Buy => confirming ? $"CONFIRM {PlayerHealthHud.FormatCount(hero.Price)}?" : "BUY",
                _ => $"NEED {PlayerHealthHud.FormatCount(hero.Price)}",
            };
        }

        private void Confirm(HeroDefinition hero)
        {
            _confirming = hero;
            _confirmReset?.Pause();
            _confirmReset = _screen.schedule.Execute(() =>
            {
                _confirming = null;
                Refresh();
            }).StartingIn(ConfirmMs);
        }

        private void CancelConfirm()
        {
            _confirmReset?.Pause();
            _confirming = null;
        }

        private Card Find(string id)
        {
            foreach (var card in _cards)
                if (card.Hero.Id == id) return card;
            return null;
        }

        private void BuildCards()
        {
            var root = _screen.Q("hs-cards");
            root.Clear();
            _cards.Clear();
            if (_roster == null) return;
            foreach (var hero in _roster.Heroes)
            {
                if (hero == null) continue;
                var card = new Card { Hero = hero, Root = new VisualElement { name = CardPrefix + hero.Id } };
                card.Root.AddToClassList("hs-card");
                var portrait = Child(card.Root, "hs-portrait");
                if (hero.Portrait != null) portrait.style.backgroundImage = new StyleBackground(hero.Portrait);

                var info = Child(card.Root, "hs-info");
                var head = Child(info, "hs-head");
                Text(head, "hs-name", hero.DisplayName).style.color = hero.Accent;
                Text(head, "hs-tagline", hero.Tagline);
                var traits = Child(info, "hs-traits");
                foreach (var line in hero.Pros) Trait(traits, line, true);
                foreach (var line in hero.Cons) Trait(traits, line, false);

                var row = Child(info, "hs-action-row");
                card.Price = Child(row, "hs-price");
                card.PriceGem = Child(card.Price, "hs-price__gem");
                card.PriceValue = Text(card.Price, "hs-price__value", "");
                card.Action = new Button { name = ActionPrefix + hero.Id, text = "" };
                card.Action.AddToClassList("hs-action");
                string id = hero.Id;
                card.Action.clicked += () => Press(id);
                row.Add(card.Action);

                card.Badge = Text(card.Root, "hs-badge", "IN USE");
                root.Add(card.Root);
                _cards.Add(card);
            }
            Refresh();
        }

        private static void Trait(VisualElement parent, string text, bool pro)
        {
            var row = Child(parent, "hs-trait");
            row.AddToClassList(pro ? "hs-trait--pro" : "hs-trait--con");
            Child(row, "hs-trait__mark");
            Text(row, "hs-trait__text", text);
        }

        private static string CurrencyName(HeroCurrency currency) => currency == HeroCurrency.Souls ? "Souls" : "Ember Shards";

        private void ShowToast(string message)
        {
            _toast.text = message;
            _toast.RemoveFromClassList(HiddenClass);
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.AddToClassList(HiddenClass)).StartingIn(ToastMs);
        }

        private static void Pop(VisualElement element)
        {
            if (element == null) return;
            element.AddToClassList(PillPopClass);
            element.schedule.Execute(() => element.RemoveFromClassList(PillPopClass)).StartingIn(PopMs);
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
    }
}
