using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Heroes;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>What a hero's button does now.</summary>
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
    /// Hero page (KAN-153, 3D stage KAN-244), the HeroSelect.uxml tree inside the main menu. One hero at a time: the
    /// <see cref="HeroStage"/> renders him alive (idle, variants, pick taunt, drag to turn) full screen behind the page;
    /// over him float his name (accent colour), tagline, strengths (green up mark) and weaknesses (red down mark) against
    /// the Knight, with no cards or boxes, then price and one button (<see cref="HeroCardState"/>). Arrows, a swipe from
    /// the outer <see cref="EdgeShare"/> of the screen, or <see cref="Step"/> (keyboard) change the viewed hero; a drag
    /// anywhere else turns him. Buying is a two-tap confirm (the price is high); a buy spends the price through
    /// <see cref="HeroProgress.TryBuy"/> and picks the hero. Test Mode (KAN-144): every hero is free and can be picked,
    /// nothing is bought or charged, a note says so. Pills follow both wallets. Without a stage (tests, code-built menus)
    /// the page works the same with a plain dark background.
    /// Plain class, owned by <see cref="MainMenuController"/>; tests build it on the MainMenu.uxml tree.
    /// </summary>
    public sealed class HeroSelectScreen
    {
        public const string ScreenName = "hs-screen";
        public const string ActionName = "hs-action";
        public const string PrevName = "hs-prev";
        public const string NextName = "hs-next";
        public const string StageName = "hs-stage";
        public const string NameName = "hs-name";
        private const string HiddenClass = "ss-hidden";
        private const string ScreenClosedClass = "hs-screen--closed";
        private const string PillPopClass = "hs-pill--pop";
        private const string InfoSnapClass = "hs-info--snap";
        private const string InfoOutLeftClass = "hs-info--out-left";
        private const string InfoOutRightClass = "hs-info--out-right";
        private static readonly string[] ActionClasses = { "hs-action--current", "", "hs-action--buy", "hs-action--poor" };
        private const string ConfirmClass = "hs-action--confirm";
        private const long HideAfterMs = 260;
        private const long PopMs = 140;
        private const long ToastMs = 2600;
        /// <summary>Milliseconds a "confirm" button waits for the second tap.</summary>
        public const long ConfirmMs = 3000;
        /// <summary>Share of the screen width at each side where a horizontal swipe changes the hero (the rest turns him).</summary>
        public const float EdgeShare = 0.15f;
        /// <summary>Panel pixels a swipe must travel to change the hero.</summary>
        public const float SwipePixels = 70f;
        /// <summary>Seconds after the page opens before the picked hero taunts (the page has faded in).</summary>
        public const float OpenTauntDelay = 0.35f;

        private readonly HeroRoster _roster;
        private readonly HeroStage _stage;
        private readonly VisualElement _screen, _safe, _stageView, _soulsPill, _shardsPill, _info, _dots, _pros, _cons;
        private readonly VisualElement _price, _priceGem, _lock;
        private readonly Label _souls, _shards, _testNote, _toast, _name, _tagline, _badge, _priceValue;
        private readonly Button _action, _prev, _next;
        private readonly List<HeroDefinition> _heroes = new List<HeroDefinition>();
        private IVisualElementScheduledItem _screenHide, _toastHide, _confirmReset, _infoIn;
        private HeroDefinition _confirming, _viewed;
        private int _pointer = -1;
        private bool _swipe;
        private float _downX, _lastX;

        public bool IsOpen { get; private set; }
        public VisualElement Page => _screen;
        /// <summary>The element the safe-area padding goes on (the stage itself stays full screen).</summary>
        public VisualElement SafeRoot => _safe;
        public HeroRoster Roster => _roster;
        /// <summary>The 3D stage (null when the menu has none).</summary>
        public HeroStage Stage => _stage;
        /// <summary>The hero the next run starts with.</summary>
        public HeroDefinition Current => HeroProgress.Current(_roster);
        /// <summary>The hero on show now (name, button and stage).</summary>
        public HeroDefinition Viewed => _viewed;
        /// <summary>Hero whose buy button waits for the confirm tap (null = none).</summary>
        public HeroDefinition Confirming => _confirming;
        /// <summary>The one action button (it acts on <see cref="Viewed"/>).</summary>
        public Button ActionButton => _action;
        public string ToastText => _toast.text;
        public bool ToastVisible => !_toast.ClassListContains(HiddenClass);
        public bool TestNoteVisible => !_testNote.ClassListContains(HiddenClass);
        /// <summary>Name shown now (tests).</summary>
        public string NameText => _name.text;
        public string TaglineText => _tagline.text;
        /// <summary>Price text shown now ("1,000", "OWNED", "FREE: TEST MODE").</summary>
        public string PriceText => _priceValue.text;
        /// <summary>True when the lock mark shows next to the price.</summary>
        public bool LockVisible => !_lock.ClassListContains(HiddenClass);
        /// <summary>The strength / weakness lines shown now.</summary>
        public IEnumerable<string> ProLines => Lines(_pros);
        public IEnumerable<string> ConLines => Lines(_cons);

        /// <summary>A hero was picked (by select or by buy).</summary>
        public event Action<HeroDefinition> Picked;
        /// <summary>A hero was bought (the menu plays feedback).</summary>
        public event Action<HeroDefinition> Bought;

        /// <param name="root">Tree that holds HeroSelect.uxml (the main menu root).</param>
        /// <param name="stage">3D stage, or null for a flat page.</param>
        public HeroSelectScreen(VisualElement root, HeroRoster roster, HeroStage stage = null)
        {
            _roster = roster;
            _stage = stage;
            _screen = root.Q(ScreenName);
            if (_screen == null) throw new InvalidOperationException($"Heroes: '{ScreenName}' missing in the menu UXML");
            // The instanced template fills the screen; only the page itself may take taps.
            if (_screen.parent != null && _screen.parent != root) _screen.parent.pickingMode = PickingMode.Ignore;
            _safe = _screen.Q("hs-safe");
            _stageView = _screen.Q(StageName);
            _souls = _screen.Q<Label>("hs-souls");
            _shards = _screen.Q<Label>("hs-shards");
            _soulsPill = _screen.Q("hs-souls-pill");
            _shardsPill = _screen.Q("hs-shards-pill");
            _testNote = _screen.Q<Label>("hs-test-note");
            _toast = _screen.Q<Label>("hs-toast");
            _info = _screen.Q("hs-info");
            _dots = _screen.Q("hs-dots");
            _badge = _screen.Q<Label>("hs-badge");
            _name = _screen.Q<Label>(NameName);
            _tagline = _screen.Q<Label>("hs-tagline");
            _pros = _screen.Q("hs-pros");
            _cons = _screen.Q("hs-cons");
            _price = _screen.Q("hs-price");
            _priceGem = _screen.Q("hs-price-gem");
            _priceValue = _screen.Q<Label>("hs-price-value");
            _lock = _screen.Q("hs-lock");
            _action = _screen.Q<Button>(ActionName);
            _prev = _screen.Q<Button>(PrevName);
            _next = _screen.Q<Button>(NextName);
            _screen.Q<Button>("hs-back").clicked += () => Back();
            _action.clicked += () =>
            {
                if (_viewed != null) Press(_viewed.Id);
            };
            _prev.clicked += () => Step(-1);
            _next.clicked += () => Step(1);
            if (_roster != null)
                foreach (var hero in _roster.Heroes)
                    if (hero != null) _heroes.Add(hero);
            BuildDots();
            BindStageInput();
            if (_stage != null) _stage.TargetChanged += ShowTarget;
            _viewed = Current;
            Refresh();
        }

        public void Open()
        {
            CancelConfirm();
            _screenHide?.Pause();
            IsOpen = true;
            _viewed = Current;
            if (_stage != null)
            {
                _stage.Open(_roster);
                ShowTarget(_stage.Target);
            }
            Refresh();
            ShowOnStage(0);
            _stage?.TauntOnce(OpenTauntDelay); // the "initial pick" moment
            _screen.RemoveFromClassList(HiddenClass);
            _screen.schedule.Execute(() => _screen.RemoveFromClassList(ScreenClosedClass));
        }

        public void Close()
        {
            if (!IsOpen) return;
            CancelConfirm();
            EndPointer();
            IsOpen = false;
            _screen.AddToClassList(ScreenClosedClass);
            _screenHide = _screen.schedule.Execute(() =>
            {
                _screen.AddToClassList(HiddenClass);
                _stage?.Close();
                _stageView.style.backgroundImage = StyleKeyword.None;
            }).StartingIn(HideAfterMs);
        }

        /// <summary>Back button / Esc: a waiting confirm first, then the page. False when nothing was open.</summary>
        public bool Back()
        {
            if (!IsOpen) return false;
            if (_confirming != null)
            {
                CancelConfirm();
                Refresh();
                return true;
            }
            Close();
            return true;
        }

        /// <summary>Shows the next (+1) or previous (-1) hero of the roster (wraps). Arrows, swipes and the keyboard.</summary>
        public void Step(int direction)
        {
            if (_heroes.Count < 2 || direction == 0) return;
            int i = Mathf.Max(0, _heroes.IndexOf(_viewed));
            int next = ((i + Math.Sign(direction)) % _heroes.Count + _heroes.Count) % _heroes.Count;
            ViewHero(_heroes[next], Math.Sign(direction));
        }

        /// <summary>Shows the hero with <paramref name="id"/> (no slide). False if unknown.</summary>
        public bool View(string id)
        {
            var hero = _roster != null ? _roster.Find(id) : null;
            if (hero == null) return false;
            ViewHero(hero, 0);
            return true;
        }

        private void ViewHero(HeroDefinition hero, int direction)
        {
            if (hero == _viewed && direction == 0) return;
            CancelConfirm();
            _viewed = hero;
            Refresh();
            ShowOnStage(direction);
            SlideInfo(direction);
        }

        /// <summary>Button state of <paramref name="hero"/> now.</summary>
        public HeroCardState StateOf(HeroDefinition hero)
        {
            if (hero == Current) return HeroCardState.Current;
            if (HeroProgress.IsUnlocked(hero)) return HeroCardState.Select;
            return HeroProgress.Check(hero) == HeroBuyCheck.Ok ? HeroCardState.Buy : HeroCardState.Poor;
        }

        public HeroCardState StateOf(string id) => StateOf(_roster.Find(id));

        /// <summary>Shows the hero and returns the action button (tests, screenshots); null if unknown.</summary>
        public Button ActionOf(string id) => View(id) ? _action : null;

        /// <summary>Price text of the hero ("1,000", "OWNED", "FREE", "FREE: TEST MODE"); null if unknown.</summary>
        public string PriceTextOf(string id)
        {
            var hero = _roster?.Find(id);
            if (hero == null) return null;
            bool owned = HeroProgress.IsOwned(hero);
            bool free = !owned && TestMode.IsOn;
            return owned ? (hero.IsFree ? "FREE" : "OWNED") : free ? "FREE: TEST MODE" : PlayerHealthHud.FormatCount(hero.Price);
        }

        /// <summary>The button of hero <paramref name="id"/> (shows him first). Returns the state the press acted on.</summary>
        public HeroCardState Press(string id)
        {
            var hero = _roster.Find(id);
            if (hero == null) return HeroCardState.Current;
            if (hero != _viewed) ViewHero(hero, 0);
            var state = StateOf(hero);
            switch (state)
            {
                case HeroCardState.Select:
                    CancelConfirm();
                    if (HeroProgress.Select(_roster, hero))
                    {
                        ShowToast($"{hero.DisplayName} walks with you.");
                        Picked?.Invoke(hero);
                        _stage?.Taunt();
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
                        _stage?.Taunt();
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

        /// <summary>Reads wallets, Test Mode and the pick again; updates the hero text, button and stage light.</summary>
        public void Refresh()
        {
            _souls.text = PlayerHealthHud.FormatCount(SoulWallet.Balance);
            _shards.text = PlayerHealthHud.FormatCount(GemWallet.Balance);
            _testNote.EnableInClassList(HiddenClass, !TestMode.IsOn);
            bool many = _heroes.Count > 1;
            _prev.EnableInClassList(HiddenClass, !many);
            _next.EnableInClassList(HiddenClass, !many);
            if (_viewed == null || !_heroes.Contains(_viewed)) _viewed = Current;
            RefreshDots();
            var hero = _viewed;
            if (hero == null) return;

            var state = StateOf(hero);
            bool owned = HeroProgress.IsOwned(hero);
            bool free = !owned && TestMode.IsOn;
            _name.text = hero.DisplayName;
            _name.style.color = hero.Accent;
            _tagline.text = hero.Tagline;
            _badge.EnableInClassList(HiddenClass, state != HeroCardState.Current);
            FillTraits(_pros, hero.Pros, true);
            FillTraits(_cons, hero.Cons, false);
            _priceValue.text = PriceTextOf(hero.Id);
            _priceGem.EnableInClassList(HiddenClass, owned || free);
            _priceGem.EnableInClassList("hs-price__gem--souls", hero.Currency == HeroCurrency.Souls);
            _price.EnableInClassList("hs-price--free", free);
            _lock.EnableInClassList(HiddenClass, !Locked(hero));
            for (int i = 0; i < ActionClasses.Length; i++)
                if (ActionClasses[i].Length > 0) _action.EnableInClassList(ActionClasses[i], (int)state == i);
            bool confirming = state == HeroCardState.Buy && _confirming == hero;
            _action.EnableInClassList(ConfirmClass, confirming);
            _action.text = state switch
            {
                HeroCardState.Current => "SELECTED",
                HeroCardState.Select => "SELECT",
                HeroCardState.Buy => confirming ? $"CONFIRM {PlayerHealthHud.FormatCount(hero.Price)}?" : "BUY",
                _ => $"NEED {PlayerHealthHud.FormatCount(hero.Price)}",
            };
            if (IsOpen && _stage != null && _stage.Shown == hero) _stage.Show(hero, 0, Locked(hero)); // light follows a buy / Test Mode
        }

        private static bool Locked(HeroDefinition hero) => !HeroProgress.IsUnlocked(hero);

        private void ShowOnStage(int direction)
        {
            if (_stage == null || !IsOpen || _viewed == null) return;
            _stage.Show(_viewed, direction, Locked(_viewed));
        }

        private void ShowTarget(RenderTexture target)
        {
            if (_stageView == null) return;
            _stageView.style.backgroundImage = target != null ? new StyleBackground(Background.FromRenderTexture(target)) : StyleKeyword.None;
        }

        // ------------------------------------------------------------ Stage input

        private void BindStageInput()
        {
            if (_stageView == null) return;
            _stageView.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_pointer >= 0) return;
                _pointer = e.pointerId;
                _downX = _lastX = e.position.x;
                float width = _stageView.layout.width;
                _swipe = width > 0f && (e.position.x < width * EdgeShare || e.position.x > width * (1f - EdgeShare));
                _stageView.CapturePointer(e.pointerId);
                if (!_swipe) _stage?.BeginDrag();
            });
            _stageView.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != _pointer) return;
                float dx = e.position.x - _lastX;
                _lastX = e.position.x;
                if (!_swipe) _stage?.Drag(dx);
            });
            _stageView.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != _pointer) return;
                float travel = e.position.x - _downX;
                bool swipe = _swipe;
                EndPointer();
                if (swipe && Mathf.Abs(travel) >= SwipePixels) Step(travel < 0f ? 1 : -1); // swipe left = next hero
            });
            _stageView.RegisterCallback<PointerCaptureOutEvent>(_ => EndPointer());
        }

        private void EndPointer()
        {
            if (_pointer < 0) return;
            if (_stageView.HasPointerCapture(_pointer)) _stageView.ReleasePointer(_pointer);
            _pointer = -1;
            if (!_swipe) _stage?.EndDrag();
            _swipe = false;
        }

        // ------------------------------------------------------------ Text

        private void BuildDots()
        {
            _dots.Clear();
            foreach (var _ in _heroes)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("hs-dot");
                _dots.Add(dot);
            }
            _dots.EnableInClassList(HiddenClass, _heroes.Count < 2);
        }

        private void RefreshDots()
        {
            for (int i = 0; i < _dots.childCount && i < _heroes.Count; i++)
                _dots[i].EnableInClassList("hs-dot--on", _heroes[i] == _viewed);
        }

        private static void FillTraits(VisualElement column, string[] lines, bool pro)
        {
            lines ??= Array.Empty<string>();
            while (column.childCount > lines.Length) column.RemoveAt(column.childCount - 1);
            while (column.childCount < lines.Length)
            {
                var row = Child(column, "hs-trait");
                Child(row, "hs-trait__mark");
                Text(row, "hs-trait__text", "");
            }
            for (int i = 0; i < lines.Length; i++)
            {
                var row = column[i];
                row.EnableInClassList("hs-trait--pro", pro);
                row.EnableInClassList("hs-trait--con", !pro);
                row.Q<Label>(className: "hs-trait__text").text = lines[i];
            }
        }

        private static IEnumerable<string> Lines(VisualElement column)
        {
            foreach (var l in column.Query<Label>(className: "hs-trait__text").ToList()) yield return l.text;
        }

        /// <summary>The text slides out the way the old hero leaves and comes back in (0.28 s USS transition).</summary>
        private void SlideInfo(int direction)
        {
            if (direction == 0 || !IsOpen) return;
            _infoIn?.Pause();
            // Jump out at once (snap: no transition), then ease back in from the side the new hero comes from.
            _info.AddToClassList(InfoSnapClass);
            _info.AddToClassList(direction > 0 ? InfoOutRightClass : InfoOutLeftClass);
            _infoIn = _info.schedule.Execute(() =>
            {
                _info.RemoveFromClassList(InfoSnapClass);
                _info.RemoveFromClassList(InfoOutRightClass);
                _info.RemoveFromClassList(InfoOutLeftClass);
            }).StartingIn(50);
        }

        private static string CurrencyName(HeroCurrency currency) => currency == HeroCurrency.Souls ? "Souls" : "Ember Shards";

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
