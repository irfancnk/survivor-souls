using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>
    /// Skill Guide Book (KAN-64), the SkillGuide.uxml tree inside the main menu ("GRIMOIRE"). List page: one card per skill
    /// of the <see cref="SkillProgression"/> run table (<see cref="SkillGuideText.Collect"/>: a new skill in the table shows
    /// up with no UI change), 2 columns, sorted by tier; type and tier filter chips. A card shows the icon in its tier
    /// colour, name, tier, card text, type and max level with pips. A tap opens the detail page: big icon, tier, type,
    /// one row per level (card text + the stat lines of <see cref="SkillGuideText.EffectsAt"/>, changed numbers in gold),
    /// "How it works" (<see cref="SkillDefinition.GuideText"/>) and "How to find it". Arrows or a horizontal swipe move to
    /// the next / previous card of the filtered list. Back / Esc: detail first, then the page.
    /// <see cref="HideUnfound"/> (off for now) shows skills never taken (<see cref="SkillDiscovery"/>) as "???".
    /// Cards are built once and only shown / hidden by the filters. Plain class, owned by <see cref="MainMenuController"/>;
    /// tests build it on the MainMenu.uxml tree.
    /// </summary>
    public sealed class SkillGuideScreen
    {
        public const string ScreenName = "sg-screen";
        public const string CardPrefix = "sg-card-";
        public const string KindChipPrefix = "sg-kind-";
        public const string TierChipPrefix = "sg-tier-";
        private const string HiddenClass = "ss-hidden";
        private const string ScreenClosedClass = "sg-screen--closed";
        private const string ChipOnClass = "sg-chip--on";
        private const string UnknownClass = "sg-card--unknown";
        private const string ChangedClass = "sg-effect--changed";
        private const long HideAfterMs = 260;
        /// <summary>Horizontal drag in panel pixels that counts as a swipe on the detail page.</summary>
        public const float SwipePixels = 140f;
        private static readonly string[] TierClasses = { "sg-tier--common", "sg-tier--rare", "sg-tier--epic", "sg-tier--legendary" };
        private static readonly SkillKind[] KindChips = { SkillKind.Attack, SkillKind.Defence, SkillKind.Utility };
        private static readonly SkillRarity[] TierChips = { SkillRarity.Common, SkillRarity.Rare, SkillRarity.Epic, SkillRarity.Legendary };

        private sealed class Card
        {
            public SkillDefinition Skill;
            public Button Root;
            public VisualElement Icon;
            public Label Name, Desc;
        }

        private readonly SkillProgression _table;
        private readonly SkillRarityStyles _styles;
        private readonly VisualElement _screen, _listPage, _detail, _detailIcon, _rows, _howBlock;
        private readonly ScrollView _list, _detailScroll;
        private readonly Label _count, _empty, _detailName, _detailTier, _detailKind, _detailMax, _detailDesc, _how, _get, _pos;
        private readonly List<SkillDefinition> _skills;
        private readonly List<SkillDefinition> _visible = new List<SkillDefinition>();
        private readonly List<Card> _cards = new List<Card>();
        private readonly List<Button> _kindButtons = new List<Button>();
        private readonly List<Button> _tierButtons = new List<Button>();
        private readonly List<SkillGuideEffect> _effects = new List<SkillGuideEffect>();
        private IVisualElementScheduledItem _screenHide;
        private SkillKind? _kindFilter;
        private SkillRarity? _tierFilter;
        private bool _hideUnfound;
        private Vector2 _swipeStart;
        private bool _swiping;

        public bool IsOpen { get; private set; }
        public VisualElement Page => _screen;
        /// <summary>Every skill of the run table, sorted (tier, unlock level, name).</summary>
        public IReadOnlyList<SkillDefinition> Skills => _skills;
        /// <summary>Skills the filters let through, in list order (arrows and swipes walk this list).</summary>
        public IReadOnlyList<SkillDefinition> VisibleSkills => _visible;
        /// <summary>Skill on the detail page; null while the list shows.</summary>
        public SkillDefinition DetailSkill { get; private set; }
        public bool DetailOpen => DetailSkill != null;
        public SkillKind? KindFilter => _kindFilter;
        public SkillRarity? TierFilter => _tierFilter;
        /// <summary>Level rows on the detail page (tests).</summary>
        public int LevelRowCount => _rows.childCount;
        public string HowItWorksText => _how.text;
        public bool HowItWorksVisible => !_howBlock.ClassListContains(HiddenClass);
        public string HowToGetText => _get.text;

        /// <summary>
        /// Skills never taken in a run show as "???" and cannot be opened (KAN-64 prepared option, off by default: every
        /// skill is visible until the design decides).
        /// </summary>
        public bool HideUnfound
        {
            get => _hideUnfound;
            set
            {
                _hideUnfound = value;
                foreach (var card in _cards) RefreshCard(card);
            }
        }

        /// <summary>A card was opened (the menu plays feedback).</summary>
        public event Action<SkillDefinition> Opened;

        /// <param name="root">Tree that holds SkillGuide.uxml (the main menu root).</param>
        public SkillGuideScreen(VisualElement root, SkillProgression table, SkillRarityStyles styles = null)
        {
            _table = table;
            _styles = styles;
            _screen = root.Q(ScreenName);
            if (_screen == null) throw new InvalidOperationException($"Skill guide: '{ScreenName}' missing in the menu UXML");
            // The instanced template fills the screen; only the page itself may take taps.
            if (_screen.parent != null && _screen.parent != root) _screen.parent.pickingMode = PickingMode.Ignore;
            _listPage = _screen.Q("sg-list-page");
            _list = _screen.Q<ScrollView>("sg-list");
            _count = _screen.Q<Label>("sg-count");
            _empty = _screen.Q<Label>("sg-empty");
            _detail = _screen.Q("sg-detail");
            _detailIcon = _screen.Q("sg-detail-icon");
            _detailName = _screen.Q<Label>("sg-detail-name");
            _detailTier = _screen.Q<Label>("sg-detail-tier");
            _detailKind = _screen.Q<Label>("sg-detail-kind");
            _detailMax = _screen.Q<Label>("sg-detail-max");
            _detailDesc = _screen.Q<Label>("sg-detail-desc");
            _detailScroll = _screen.Q<ScrollView>("sg-detail-scroll");
            _rows = _screen.Q("sg-rows");
            _howBlock = _screen.Q("sg-how-block");
            _how = _screen.Q<Label>("sg-how");
            _get = _screen.Q<Label>("sg-get");
            _pos = _screen.Q<Label>("sg-pos");
            _screen.Q<Button>("sg-back").clicked += () => Back();
            _screen.Q<Button>("sg-prev").clicked += () => Previous();
            _screen.Q<Button>("sg-next").clicked += () => Next();
            // Swipe: trickle down so the drag is seen before the scroll view takes the pointer.
            _detail.RegisterCallback<PointerDownEvent>(e => { _swipeStart = e.position; _swiping = true; }, TrickleDown.TrickleDown);
            _detail.RegisterCallback<PointerUpEvent>(e => EndSwipe(e.position), TrickleDown.TrickleDown);
            _detail.RegisterCallback<PointerCancelEvent>(_ => _swiping = false, TrickleDown.TrickleDown);

            _skills = SkillGuideText.Collect(table);
            BuildChips();
            BuildCards();
            ApplyFilters();
        }

        public void Open()
        {
            _screenHide?.Pause();
            IsOpen = true;
            CloseDetail();
            foreach (var card in _cards) RefreshCard(card); // found flags may have changed in a run
            _screen.RemoveFromClassList(HiddenClass);
            _screen.schedule.Execute(() => _screen.RemoveFromClassList(ScreenClosedClass));
            _list.scrollOffset = Vector2.zero;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _screen.AddToClassList(ScreenClosedClass);
            _screenHide = _screen.schedule.Execute(() => _screen.AddToClassList(HiddenClass)).StartingIn(HideAfterMs);
        }

        /// <summary>Back button / Esc: the detail page first, then the guide. False when nothing was open.</summary>
        public bool Back()
        {
            if (!IsOpen) return false;
            if (DetailOpen)
            {
                CloseDetail();
                return true;
            }
            Close();
            return true;
        }

        /// <summary>Card button of the skill with <paramref name="id"/> (tests, screenshots); null if unknown.</summary>
        public Button CardOf(string id) => Find(id)?.Root;

        /// <summary>True when the card of <paramref name="id"/> shows (passes the filters).</summary>
        public bool CardVisible(string id)
        {
            var card = Find(id);
            return card != null && !card.Root.ClassListContains(HiddenClass);
        }

        /// <summary>Type filter (null = all). Tests and the chips call it.</summary>
        public void SetKindFilter(SkillKind? kind)
        {
            _kindFilter = kind;
            ApplyFilters();
        }

        /// <summary>Tier filter (null = all). Tests and the chips call it.</summary>
        public void SetTierFilter(SkillRarity? tier)
        {
            _tierFilter = tier;
            ApplyFilters();
        }

        /// <summary>Opens the detail page of <paramref name="id"/> (card tap). False when unknown or not found yet while hidden.</summary>
        public bool ShowDetail(string id)
        {
            var card = Find(id);
            if (card == null || IsUnknown(card.Skill)) return false;
            ShowDetail(card.Skill);
            Opened?.Invoke(card.Skill);
            return true;
        }

        /// <summary>Next skill of the filtered list (wraps). False while the list shows.</summary>
        public bool Next() => Step(1);

        /// <summary>Previous skill of the filtered list (wraps). False while the list shows.</summary>
        public bool Previous() => Step(-1);

        /// <summary>A horizontal drag of <paramref name="dx"/> panel pixels on the detail page (tests call it too).</summary>
        public bool Swipe(float dx)
        {
            if (!DetailOpen || Mathf.Abs(dx) < SwipePixels) return false;
            return dx < 0f ? Next() : Previous();
        }

        /// <summary>Scrolls the page that shows (list or detail) to <paramref name="y"/> panel pixels (dev screenshots).</summary>
        public void ScrollTo(float y)
        {
            var view = DetailOpen ? _detailScroll : _list;
            view.scrollOffset = new Vector2(0f, Mathf.Max(0f, y));
        }

        /// <summary>Text of level row <paramref name="level"/> on the detail page: card text, then the stat lines (tests).</summary>
        public string LevelRowText(int level)
        {
            if (level < 1 || level > _rows.childCount) return null;
            var row = _rows[level - 1];
            var parts = new List<string>();
            row.Query<Label>().ForEach(l => parts.Add(l.text));
            return string.Join(" ", parts);
        }

        /// <summary>Changed-number chips of level row <paramref name="level"/> (tests).</summary>
        public int ChangedEffectsAt(int level)
        {
            if (level < 1 || level > _rows.childCount) return 0;
            int n = 0;
            _rows[level - 1].Query(className: ChangedClass).ForEach(_ => n++);
            return n;
        }

        private void EndSwipe(Vector2 end)
        {
            if (!_swiping) return;
            _swiping = false;
            var delta = end - _swipeStart;
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.5f) Swipe(delta.x);
        }

        private bool Step(int dir)
        {
            if (!DetailOpen || _visible.Count == 0) return false;
            int i = _visible.IndexOf(DetailSkill);
            for (int n = 0; n < _visible.Count; n++)
            {
                i = (i + dir + _visible.Count) % _visible.Count;
                if (!IsUnknown(_visible[i])) break;
            }
            if (_visible[i] == DetailSkill) return false;
            ShowDetail(_visible[i]);
            return true;
        }

        private bool IsUnknown(SkillDefinition skill) => _hideUnfound && !SkillDiscovery.IsFound(skill.Id);

        private void ShowDetail(SkillDefinition skill)
        {
            DetailSkill = skill;
            _listPage.AddToClassList(HiddenClass);
            _detail.RemoveFromClassList(HiddenClass);
            SetTier(_detail, skill.Rarity);
            if (skill.Icon != null) _detailIcon.style.backgroundImage = new StyleBackground(skill.Icon);
            else _detailIcon.style.backgroundImage = StyleKeyword.None;
            _detailName.text = skill.DisplayName;
            _detailTier.text = TierLabel(skill.Rarity);
            string kind = SkillGuideText.KindName(skill.Kind);
            _detailKind.text = kind;
            _detailKind.EnableInClassList(HiddenClass, kind.Length == 0);
            _detailMax.text = $"MAX LV {skill.MaxLevel}";
            _detailDesc.text = skill.Description;

            _rows.Clear();
            for (int level = 1; level <= skill.MaxLevel; level++) _rows.Add(BuildRow(skill, level));
            _how.text = skill.GuideText ?? "";
            _howBlock.EnableInClassList(HiddenClass, string.IsNullOrEmpty(skill.GuideText));
            _get.text = string.Join("\n", SkillGuideText.HowToGet(skill, _table));
            int index = _visible.IndexOf(skill);
            _pos.text = index >= 0 ? $"{index + 1} / {_visible.Count}" : "";
            _detailScroll.scrollOffset = Vector2.zero;
        }

        private void CloseDetail()
        {
            DetailSkill = null;
            _detail.AddToClassList(HiddenClass);
            _listPage.RemoveFromClassList(HiddenClass);
        }

        private VisualElement BuildRow(SkillDefinition skill, int level)
        {
            var row = Child(null, "sg-row");
            Text(row, "sg-row__level", $"LV {level}");
            var body = Child(row, "sg-row__body");
            Text(body, "sg-row__text", skill.DescriptionAt(level));
            var effects = Child(body, "sg-effects");
            SkillGuideText.EffectsAt(skill, level, _effects);
            foreach (var e in _effects)
            {
                var chip = Child(effects, "sg-effect");
                chip.EnableInClassList(ChangedClass, e.Changed);
                Text(chip, "sg-effect__label", e.Label);
                Text(chip, "sg-effect__value", e.ValueText);
            }
            return row;
        }

        private void BuildChips()
        {
            var kinds = _screen.Q("sg-kind-chips");
            kinds.Clear();
            _kindButtons.Clear();
            _kindButtons.Add(Chip(kinds, KindChipPrefix + "all", "ALL", () => SetKindFilter(null)));
            foreach (var k in KindChips)
            {
                var kind = k;
                _kindButtons.Add(Chip(kinds, KindChipPrefix + kind.ToString().ToLowerInvariant(), SkillGuideText.KindName(kind),
                    () => SetKindFilter(kind)));
            }
            var tiers = _screen.Q("sg-tier-chips");
            tiers.Clear();
            _tierButtons.Clear();
            _tierButtons.Add(Chip(tiers, TierChipPrefix + "all", "ALL", () => SetTierFilter(null)));
            foreach (var t in TierChips)
            {
                var tier = t;
                _tierButtons.Add(Chip(tiers, TierChipPrefix + tier.ToString().ToLowerInvariant(), TierLabel(tier),
                    () => SetTierFilter(tier)));
            }
        }

        private static Button Chip(VisualElement parent, string name, string text, Action onClick)
        {
            var chip = new Button(onClick) { name = name, text = text };
            chip.AddToClassList("sg-chip");
            parent.Add(chip);
            return chip;
        }

        private void BuildCards()
        {
            var grid = _screen.Q("sg-grid");
            grid.Clear();
            _cards.Clear();
            foreach (var skill in _skills)
            {
                var card = new Card { Skill = skill, Root = new Button { name = CardPrefix + skill.Id, text = "" } };
                card.Root.AddToClassList("sg-card");
                SetTier(card.Root, skill.Rarity);
                string id = skill.Id;
                card.Root.clicked += () => ShowDetail(id);

                var top = Child(card.Root, "sg-card__top");
                card.Icon = Child(top, "sg-icon");
                if (skill.Icon != null) card.Icon.style.backgroundImage = new StyleBackground(skill.Icon);
                var head = Child(top, "sg-card__head");
                card.Name = Text(head, "sg-card__name", skill.DisplayName);
                Text(head, "sg-card__tier", TierLabel(skill.Rarity));
                card.Desc = Text(card.Root, "sg-card__desc", skill.Description);

                var foot = Child(card.Root, "sg-card__foot");
                Text(foot, "sg-card__kind", SkillGuideText.KindName(skill.Kind));
                Text(foot, "sg-card__max", $"MAX LV {skill.MaxLevel}");
                var pips = Child(card.Root, "sg-pips");
                for (int i = 0; i < skill.MaxLevel; i++) Child(pips, "sg-pip");

                grid.Add(card.Root);
                _cards.Add(card);
                RefreshCard(card);
            }
        }

        private void RefreshCard(Card card)
        {
            bool unknown = IsUnknown(card.Skill);
            card.Root.EnableInClassList(UnknownClass, unknown);
            card.Name.text = unknown ? "???" : card.Skill.DisplayName;
            card.Desc.text = unknown ? "Not yet found. Take it in a run." : card.Skill.Description;
        }

        private void ApplyFilters()
        {
            _visible.Clear();
            foreach (var card in _cards)
            {
                var s = card.Skill;
                bool show = (_kindFilter == null || s.Kind == _kindFilter) && (_tierFilter == null || s.Rarity == _tierFilter);
                card.Root.EnableInClassList(HiddenClass, !show);
                if (show) _visible.Add(s);
            }
            for (int i = 0; i < _kindButtons.Count; i++)
                _kindButtons[i].EnableInClassList(ChipOnClass, i == 0 ? _kindFilter == null : _kindFilter == KindChips[i - 1]);
            for (int i = 0; i < _tierButtons.Count; i++)
                _tierButtons[i].EnableInClassList(ChipOnClass, i == 0 ? _tierFilter == null : _tierFilter == TierChips[i - 1]);
            _empty.EnableInClassList(HiddenClass, _visible.Count > 0);
            _count.text = _visible.Count == _skills.Count ? $"{_skills.Count} SKILLS" : $"{_visible.Count} / {_skills.Count} SKILLS";
        }

        private string TierLabel(SkillRarity rarity) =>
            _styles != null ? _styles.Of(rarity).label : SkillRarityStyles.Defaults()[Mathf.Clamp((int)rarity, 0, 3)].label;

        private static void SetTier(VisualElement element, SkillRarity rarity)
        {
            for (int i = 0; i < TierClasses.Length; i++) element.EnableInClassList(TierClasses[i], (int)rarity == i);
        }

        private Card Find(string id)
        {
            foreach (var card in _cards)
                if (card.Skill.Id == id) return card;
            return null;
        }

        private static VisualElement Child(VisualElement parent, string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            parent?.Add(element);
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
