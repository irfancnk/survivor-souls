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
    /// Soul tree page (KAN-98), the SoulTree.uxml tree inside the main menu. One row per tier of the <see cref="SoulTree"/>
    /// (roots on top), joined by a faint ember line; one card per node: icon, name, effect now (or per level when not
    /// owned), level pips, and the next cost / MAX / the missing prerequisite. Tapping a card opens the detail sheet
    /// (description, NOW, NEXT, COST); its KINDLE button buys the next level through <see cref="SoulTree.TryBuy"/> and the
    /// sheet stays open, so several levels can be bought in a row. The Souls pill in the header follows the wallet.
    /// <see cref="Back"/> closes the sheet first, then the page. Plain class, owned by <see cref="MainMenuController"/>;
    /// tests build it on the MainMenu.uxml tree.
    /// </summary>
    public sealed class SoulTreeScreen
    {
        public const string ScreenName = "st-screen";
        public const string NodePrefix = "st-node-";
        private const string ListName = "st-list";
        private const string BackName = "st-back";
        private const string SoulsName = "st-souls";
        private const string VeilName = "st-veil";
        private const string DetailName = "st-detail";
        private const string BuyName = "st-buy";
        private const string DetailBackName = "st-detail-back";
        private const string HiddenClass = "ss-hidden";
        private const string ScreenClosedClass = "st-screen--closed";
        private const string VeilClosedClass = "st-veil--closed";
        private const string DetailClosedClass = "st-detail--closed";
        private const string DetailLockedClass = "st-detail--locked";
        private const string SelectedClass = "st-node--selected";
        private const string LockedClass = "st-node--locked";
        private const string MaxClass = "st-node--max";
        private const string PoorClass = "st-node--poor";
        private const string BoughtClass = "st-node--bought";
        private const string PipOnClass = "st-pip--on";
        private const string SpentClass = "st-souls--spent";
        /// <summary>Milliseconds before a closed part is hidden: a little longer than the USS transitions.</summary>
        private const long HideAfterMs = 260;
        private const long PopMs = 140;

        private sealed class Card
        {
            public SoulTreeNode Node;
            public Button Root;
            public Label Effect, Price;
            public VisualElement[] Pips;
        }

        private readonly SoulTree _tree;
        private readonly VisualElement _screen, _veil, _detail, _detailIcon;
        private readonly ScrollView _list;
        private readonly Label _souls, _name, _level, _desc, _now, _next, _cost, _note;
        private readonly Button _buy;
        private readonly List<Card> _cards = new List<Card>();
        private IVisualElementScheduledItem _screenHide, _veilHide, _detailHide;

        public bool IsOpen { get; private set; }
        public bool IsDetailOpen { get; private set; }
        /// <summary>Node of the open (or last opened) detail sheet.</summary>
        public SoulTreeNode Selected { get; private set; }
        /// <summary>KINDLE is on in the detail sheet.</summary>
        public bool CanBuy => _buy != null && _buy.enabledSelf;
        /// <summary>Text of the KINDLE button (tests).</summary>
        public string BuyText => _buy.text;
        public VisualElement Page => _screen;
        public SoulTree Tree => _tree;

        /// <summary>A level was bought on this page (the menu plays feedback).</summary>
        public event Action<SoulTreeNode, int> Bought;

        /// <param name="root">Tree that holds SoulTree.uxml (the main menu root).</param>
        public SoulTreeScreen(VisualElement root, SoulTree tree)
        {
            _tree = tree;
            _screen = root.Q(ScreenName);
            if (_screen == null) throw new InvalidOperationException($"SoulTree: '{ScreenName}' missing in the menu UXML");
            // The instanced template fills the screen; only the page itself may take taps.
            if (_screen.parent != null && _screen.parent != root) _screen.parent.pickingMode = PickingMode.Ignore;

            _list = _screen.Q<ScrollView>(ListName);
            _souls = _screen.Q<Label>(SoulsName);
            _veil = _screen.Q(VeilName);
            _detail = _screen.Q(DetailName);
            _detailIcon = _screen.Q("st-detail-icon");
            _name = _screen.Q<Label>("st-detail-name");
            _level = _screen.Q<Label>("st-detail-level");
            _desc = _screen.Q<Label>("st-detail-desc");
            _now = _screen.Q<Label>("st-detail-now");
            _next = _screen.Q<Label>("st-detail-next");
            _cost = _screen.Q<Label>("st-detail-cost");
            _note = _screen.Q<Label>("st-detail-note");
            _buy = _screen.Q<Button>(BuyName);

            _screen.Q<Button>(BackName).clicked += () => Back();
            _screen.Q<Button>(DetailBackName).clicked += CloseDetail;
            _veil.RegisterCallback<PointerDownEvent>(_ => CloseDetail());
            _buy.clicked += OnBuy;

            BuildCards();
        }

        /// <summary>Shows the page with fresh levels and balance.</summary>
        public void Open()
        {
            Refresh();
            _screenHide?.Pause();
            IsOpen = true;
            Show(_screen, ScreenClosedClass);
            _list.scrollOffset = Vector2.zero;
        }

        /// <summary>Hides the page (and the sheet).</summary>
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

        /// <summary>Opens the detail sheet of the node with <paramref name="id"/> (opens the page first). False if unknown.</summary>
        public bool OpenDetail(string id)
        {
            var node = _tree != null ? _tree.Find(id) : null;
            if (node == null) return false;
            if (!IsOpen) Open();
            Selected = node;
            foreach (var card in _cards) card.Root.EnableInClassList(SelectedClass, card.Node == node);
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
            foreach (var card in _cards) card.Root.RemoveFromClassList(SelectedClass);
            _veilHide = Hide(_veil, VeilClosedClass);
            _detailHide = Hide(_detail, DetailClosedClass);
        }

        /// <summary>Card of the node with <paramref name="id"/> (tests, screenshots); null if not in the tree.</summary>
        public Button CardOf(string id)
        {
            foreach (var card in _cards)
                if (card.Node.Id == id) return card.Root;
            return null;
        }

        /// <summary>Buys the next level of the open node (KINDLE). Returns the check result.</summary>
        public SoulTreeBuyCheck BuySelected()
        {
            if (_tree == null || Selected == null) return SoulTreeBuyCheck.Unknown;
            var node = Selected;
            var result = _tree.TryBuy(node);
            if (result != SoulTreeBuyCheck.Ok) return result;
            Refresh();
            var card = Find(node);
            if (card != null) Pop(card.Root, BoughtClass);
            Pop(_souls.parent, SpentClass);
            Bought?.Invoke(node, SoulTreeProgress.Level(node));
            return result;
        }

        /// <summary>Reads levels, lock states and the wallet again (also on every <see cref="Open"/> and buy).</summary>
        public void Refresh()
        {
            int balance = SoulWallet.Balance;
            _souls.text = PlayerHealthHud.FormatCount(balance);
            foreach (var card in _cards)
            {
                var node = card.Node;
                int level = SoulTreeProgress.Level(node);
                bool max = level >= node.MaxLevel;
                bool locked = !max && !SoulTree.IsUnlocked(node);
                int cost = SoulTree.NextCost(node);
                card.Root.EnableInClassList(MaxClass, max);
                card.Root.EnableInClassList(LockedClass, locked);
                card.Root.EnableInClassList(PoorClass, !max && !locked && balance < cost);
                card.Effect.text = level > 0 ? node.EffectText(level) : node.EffectText(1) + " / LV";
                card.Price.text = max ? "MAX" : locked ? RequirementText(node) : PlayerHealthHud.FormatCount(cost);
                for (int i = 0; i < card.Pips.Length; i++) card.Pips[i].EnableInClassList(PipOnClass, i < level);
            }
            if (IsDetailOpen || Selected != null) RefreshDetail();
        }

        /// <summary>"Needs Kindled Might II" for the first prerequisite not met; empty when none.</summary>
        public static string RequirementText(SoulTreeNode node)
        {
            var missing = SoulTree.MissingRequirement(node);
            return missing.node != null ? $"Needs {missing.node.DisplayName} {ChamberTransitionHud.Roman(Mathf.Max(1, missing.level))}" : "";
        }

        private void RefreshDetail()
        {
            var node = Selected;
            if (node == null) return;
            int level = SoulTreeProgress.Level(node);
            bool max = level >= node.MaxLevel;
            var check = _tree.Check(node);
            _detail.style.borderTopColor = node.Accent;
            _detail.EnableInClassList(DetailLockedClass, check == SoulTreeBuyCheck.Locked);
            _detailIcon.style.backgroundImage = node.Icon != null ? new StyleBackground(node.Icon) : new StyleBackground(StyleKeyword.None);
            _name.text = node.DisplayName;
            _level.text = $"LEVEL {level} / {node.MaxLevel}";
            _desc.text = node.Description;
            _now.text = level > 0 ? node.EffectText(level) : "Not kindled";
            _next.text = max ? "Fully kindled" : node.EffectText(level + 1);
            _cost.text = max ? "-" : $"{PlayerHealthHud.FormatCount(SoulTree.NextCost(node))} Souls";

            string note = check == SoulTreeBuyCheck.Locked ? RequirementText(node) + " to kindle this."
                : check == SoulTreeBuyCheck.NotEnoughSouls ? $"You hold {PlayerHealthHud.FormatCount(SoulWallet.Balance)} Souls. Fight on to earn more."
                : null;
            _note.text = note ?? "";
            _note.EnableInClassList(HiddenClass, note == null);
            _buy.SetEnabled(check == SoulTreeBuyCheck.Ok);
            _buy.text = max ? "MAX LEVEL"
                : check == SoulTreeBuyCheck.Locked ? "LOCKED"
                : check == SoulTreeBuyCheck.NotEnoughSouls ? "NOT ENOUGH SOULS"
                : level == 0 ? "KINDLE" : "KINDLE " + ChamberTransitionHud.Roman(level + 1);
        }

        private void OnBuy()
        {
            if (!IsDetailOpen) return;
            BuySelected();
        }

        private Card Find(SoulTreeNode node)
        {
            foreach (var card in _cards)
                if (card.Node == node) return card;
            return null;
        }

        private void BuildCards()
        {
            var content = _list.contentContainer;
            content.Clear();
            _cards.Clear();
            if (_tree == null) return;

            for (int tier = 0; tier <= _tree.MaxTier; tier++)
            {
                VisualElement row = null;
                foreach (var node in _tree.Nodes)
                {
                    if (node == null || node.Tier != tier) continue;
                    if (row == null)
                    {
                        if (content.childCount > 0) Child(content, "st-link");
                        row = Child(content, "st-tier");
                    }
                    row.Add(BuildCard(node));
                }
            }
            Refresh();
        }

        private Button BuildCard(SoulTreeNode node)
        {
            var card = new Card { Node = node, Root = new Button { name = NodePrefix + node.Id, text = "" } };
            card.Root.AddToClassList("st-node");
            card.Root.style.borderTopColor = node.Accent;
            string id = node.Id;
            card.Root.clicked += () => OpenDetail(id);

            var icon = Child(card.Root, "st-icon");
            if (node.Icon != null) icon.style.backgroundImage = new StyleBackground(node.Icon);
            icon.style.borderTopColor = icon.style.borderBottomColor = icon.style.borderLeftColor = icon.style.borderRightColor =
                new Color(node.Accent.r, node.Accent.g, node.Accent.b, 0.7f);
            Text(card.Root, "st-node__name", node.DisplayName);
            card.Effect = Text(card.Root, "st-node__effect", "");
            var pips = Child(card.Root, "st-pips");
            card.Pips = new VisualElement[node.MaxLevel];
            for (int i = 0; i < card.Pips.Length; i++) card.Pips[i] = Child(pips, "st-pip");
            var cost = Child(card.Root, "st-node__cost");
            Child(cost, "st-gem");
            card.Price = Text(cost, "st-node__price", "");
            _cards.Add(card);
            return card.Root;
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
