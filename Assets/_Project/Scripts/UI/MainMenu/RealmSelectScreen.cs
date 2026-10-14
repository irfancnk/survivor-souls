using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Realms;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>
    /// Realm selection page (KAN-20), the RealmSelect.uxml tree inside the main menu. One card per Realm of the
    /// <see cref="RealmCatalog"/>: thumbnail, "REALM I", name, tagline, Chamber count, best Chamber reached with a bar,
    /// and a badge (NEW / LOCKED / CLEARED / SOON). Lock states come from <see cref="RunProgress.StateOf"/> and are read
    /// again on every <see cref="Open"/>. Tapping a card opens the detail sheet (description, enemies, rewards, Chambers,
    /// best, Estus cost); its START is on only for an unlocked, playable Realm and hands the Realm to the start callback.
    /// <see cref="Back"/> closes the sheet first, then the page. Opening and closing fade and slide (USS transitions);
    /// a closed part gets display none after its transition, so it never takes taps.
    /// Plain class, owned by <see cref="MainMenuController"/>; tests build it on the MainMenu.uxml tree.
    /// </summary>
    public sealed class RealmSelectScreen
    {
        public const string ScreenName = "rs-screen";
        public const string CardPrefix = "rs-card-";
        private const string ListName = "rs-list";
        private const string BackName = "rs-back";
        private const string VeilName = "rs-veil";
        private const string DetailName = "rs-detail";
        private const string DetailThumbName = "rs-detail-thumb";
        private const string StartName = "rs-start";
        private const string DetailBackName = "rs-detail-back";
        private const string HiddenClass = "ss-hidden";
        private const string ScreenClosedClass = "rs-screen--closed";
        private const string VeilClosedClass = "rs-veil--closed";
        private const string DetailClosedClass = "rs-detail--closed";
        private const string DetailLockedClass = "rs-detail--locked";
        private const string SelectedClass = "rs-card--selected";
        private static readonly string[] StateClasses = { "rs-card--locked", "rs-card--unlocked", "rs-card--completed" };
        /// <summary>Milliseconds before a closed part is hidden: a little longer than the USS transitions.</summary>
        private const long HideAfterMs = 260;

        private sealed class Card
        {
            public RealmDefinition Realm;
            public Button Root;
            public VisualElement Fill;
            public Label Badge, Meta, Best;
        }

        private readonly RealmCatalog _catalog;
        private readonly Action<RealmDefinition> _onStart;
        private readonly VisualElement _screen, _veil, _detail, _detailThumb;
        private readonly ScrollView _list;
        private readonly Label _numeral, _name, _desc, _enemies, _rewards, _chambers, _best, _estus, _note;
        private readonly Button _start;
        private readonly List<Card> _cards = new List<Card>();
        private IVisualElementScheduledItem _screenHide, _veilHide, _detailHide;
        private Label _chamberPick; // Beta 1 only
        private int _startChamber = 1;

        public bool IsOpen { get; private set; }
        public bool IsDetailOpen { get; private set; }
        /// <summary>Realm of the open (or last opened) detail sheet.</summary>
        public RealmDefinition Selected { get; private set; }
        /// <summary>START is on in the detail sheet.</summary>
        public bool CanStart => _start != null && _start.enabledSelf;
        public VisualElement Page => _screen;
        /// <summary>Chamber START begins at: 1, or the Beta 1 picker's choice.</summary>
        public int StartChamber => BetaMode.Enabled ? _startChamber : 1;

        /// <param name="root">Tree that holds RealmSelect.uxml (the main menu root).</param>
        /// <param name="onStart">Called with the Realm when START is pressed on a startable Realm.</param>
        public RealmSelectScreen(VisualElement root, RealmCatalog catalog, Action<RealmDefinition> onStart)
        {
            _catalog = catalog;
            _onStart = onStart;
            _screen = root.Q(ScreenName);
            if (_screen == null) throw new InvalidOperationException($"RealmSelect: '{ScreenName}' missing in the menu UXML");
            // The instanced template fills the screen; only the page itself may take taps.
            if (_screen.parent != null && _screen.parent != root) _screen.parent.pickingMode = PickingMode.Ignore;

            _list = _screen.Q<ScrollView>(ListName);
            _veil = _screen.Q(VeilName);
            _detail = _screen.Q(DetailName);
            _detailThumb = _screen.Q(DetailThumbName);
            _numeral = _screen.Q<Label>("rs-detail-numeral");
            _name = _screen.Q<Label>("rs-detail-name");
            _desc = _screen.Q<Label>("rs-detail-desc");
            _enemies = _screen.Q<Label>("rs-detail-enemies");
            _rewards = _screen.Q<Label>("rs-detail-rewards");
            _chambers = _screen.Q<Label>("rs-detail-chambers");
            _best = _screen.Q<Label>("rs-detail-best");
            _estus = _screen.Q<Label>("rs-detail-estus");
            _note = _screen.Q<Label>("rs-detail-note");
            _start = _screen.Q<Button>(StartName);

            _screen.Q<Button>(BackName).clicked += () => Back();
            _screen.Q<Button>(DetailBackName).clicked += CloseDetail;
            _veil.RegisterCallback<PointerDownEvent>(_ => CloseDetail());
            _start.clicked += OnStart;
            if (BetaMode.Enabled) BuildChamberPicker();

            BuildCards();
        }

        /// <summary>Shows the page with fresh lock states and progress.</summary>
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

        /// <summary>Opens the detail sheet of the Realm with <paramref name="realmId"/> (opens the page first). False if unknown.</summary>
        public bool OpenDetail(int realmId)
        {
            var realm = _catalog != null ? _catalog.Get(realmId) : null;
            if (realm == null) return false;
            if (!IsOpen) Open();

            Selected = realm;
            var state = RunProgress.StateOf(realm.Id);
            bool locked = state == RealmState.Locked;
            _detailThumb.style.backgroundImage = realm.Thumbnail != null ? new StyleBackground(realm.Thumbnail) : new StyleBackground(StyleKeyword.None);
            _detail.EnableInClassList(DetailLockedClass, locked);
            _numeral.text = "REALM " + ChamberTransitionHud.Roman(realm.Id);
            _name.text = realm.DisplayName;
            _desc.text = realm.Description;
            _enemies.text = Or(realm.Enemies, "Unknown");
            _rewards.text = Or(realm.Rewards, "Unknown");
            _chambers.text = realm.Chambers.ToString();
            int best = Mathf.Min(RunProgress.BestChamber(realm.Id), realm.Chambers);
            _best.text = best > 0 ? $"{best} / {realm.Chambers}" : "Not yet entered";
            _estus.text = realm.EstusCost.ToString();

            string note = locked ? $"Clear Realm {ChamberTransitionHud.Roman(realm.Id - 1)} to unlock."
                : !IsPlayable(realm) ? "This Realm is still being forged. Coming soon."
                : null;
            _note.text = note ?? "";
            _note.EnableInClassList(HiddenClass, note == null);
            _start.SetEnabled(note == null);
            _start.text = locked ? "LOCKED"
                : !IsPlayable(realm) ? "COMING SOON"
                : state == RealmState.Completed ? "START AGAIN"
                : RunProgress.BestChamber(realm.Id) > 0 ? "START"
                : "ENTER";

            _startChamber = 1;
            UpdateChamberPick();
            foreach (var card in _cards) card.Root.EnableInClassList(SelectedClass, card.Realm == realm);
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

        /// <summary>Card of <paramref name="realmId"/> (tests, screenshots); null if not in the catalog.</summary>
        public Button CardOf(int realmId)
        {
            foreach (var card in _cards)
                if (card.Realm.Id == realmId) return card.Root;
            return null;
        }

        /// <summary>Reads lock states and progress again (also on every <see cref="Open"/>).</summary>
        public void Refresh()
        {
            foreach (var card in _cards)
            {
                var realm = card.Realm;
                var state = RunProgress.StateOf(realm.Id);
                for (int i = 0; i < StateClasses.Length; i++) card.Root.EnableInClassList(StateClasses[i], i == (int)state);

                card.Badge.text = BadgeText(realm, state);
                card.Badge.EnableInClassList(HiddenClass, card.Badge.text.Length == 0);
                card.Meta.text = $"{realm.Chambers} CHAMBERS";
                card.Best.text = state == RealmState.Locked ? "" : BestText(realm);
                card.Fill.style.width = Length.Percent(100f * Progress(realm, state));
            }
        }

        /// <summary>Bar fill 0..1: best Chamber reached over the Chamber count; 1 when completed.</summary>
        public static float Progress(RealmDefinition realm, RealmState state)
        {
            if (state == RealmState.Completed) return 1f;
            if (state == RealmState.Locked || realm.Chambers <= 0) return 0f;
            return Mathf.Clamp01(RunProgress.BestChamber(realm.Id) / (float)realm.Chambers);
        }

        /// <summary>Badge text: LOCKED, CLEARED, SOON (unlocked but no content), NEW (never played) or none.</summary>
        public static string BadgeText(RealmDefinition realm, RealmState state)
        {
            if (state == RealmState.Locked) return "LOCKED";
            if (state == RealmState.Completed) return "CLEARED";
            if (!IsPlayable(realm)) return "SOON";
            return RunProgress.BestChamber(realm.Id) > 0 ? "" : "NEW";
        }

        private static string BestText(RealmDefinition realm)
        {
            int best = Mathf.Min(RunProgress.BestChamber(realm.Id), realm.Chambers);
            return best > 0 ? $"BEST {best} / {realm.Chambers}" : "NOT YET ENTERED";
        }

        /// <summary>Has content to play. Beta 1: every Realm (no own content yet = Realm 1 content).</summary>
        private static bool IsPlayable(RealmDefinition realm) => realm.Playable || BetaMode.Enabled;

        /// <summary>Beta 1: "&lt; CHAMBER N &gt;" row above START, to start at any Chamber (boss Chambers 5 and 10).</summary>
        private void BuildChamberPicker()
        {
            var row = new VisualElement { name = "rs-beta-chambers" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 18;
            row.Add(StepButton("<", -1));
            _chamberPick = new Label();
            _chamberPick.style.fontSize = 44;
            _chamberPick.style.color = new Color(0.93f, 0.84f, 0.6f);
            _chamberPick.style.unityFontStyleAndWeight = FontStyle.Bold;
            _chamberPick.style.minWidth = 420;
            _chamberPick.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(_chamberPick);
            row.Add(StepButton(">", 1));
            var parent = _start.parent;
            parent.Insert(parent.IndexOf(_start), row);
            UpdateChamberPick();
        }

        private Button StepButton(string text, int step)
        {
            var b = new Button(() => StepChamber(step)) { text = text };
            b.style.width = 120;
            b.style.height = 96;
            b.style.fontSize = 48;
            b.style.color = new Color(0.93f, 0.84f, 0.6f);
            b.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            var rim = new Color(0.6f, 0.5f, 0.3f);
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = rim;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 2;
            return b;
        }

        /// <summary>Beta 1 picker: next / previous Chamber, wraps inside the Realm.</summary>
        public void StepChamber(int step)
        {
            int count = Selected != null ? Mathf.Max(1, Selected.Chambers) : 1;
            _startChamber = ((_startChamber - 1 + step) % count + count) % count + 1;
            UpdateChamberPick();
        }

        private void UpdateChamberPick()
        {
            if (_chamberPick == null) return;
            int count = Selected != null ? Selected.Chambers : 0;
            _chamberPick.text = count > 0 ? $"CHAMBER {_startChamber} / {count}" : $"CHAMBER {_startChamber}";
        }

        private static string Or(string text, string fallback) => string.IsNullOrEmpty(text) ? fallback : text;

        private void OnStart()
        {
            if (!IsDetailOpen || Selected == null || !CanStart) return;
            _onStart?.Invoke(Selected);
        }

        private void BuildCards()
        {
            var content = _list.contentContainer;
            content.Clear();
            _cards.Clear();
            if (_catalog == null) return;

            foreach (var realm in _catalog.Realms)
            {
                if (realm == null) continue;
                var card = new Card { Realm = realm, Root = new Button { name = CardPrefix + realm.Id, text = "" } };
                card.Root.AddToClassList("rs-card");
                card.Root.style.borderLeftColor = realm.Accent;
                int id = realm.Id;
                card.Root.clicked += () => OpenDetail(id);

                var thumb = Child(card.Root, "rs-card__thumb");
                if (realm.Thumbnail != null) thumb.style.backgroundImage = new StyleBackground(realm.Thumbnail);
                else Text(thumb, "rs-card__glyph", ChamberTransitionHud.Roman(realm.Id)); // no art yet: big numeral
                var lockIcon = Child(thumb, "rs-lock");
                Child(lockIcon, "rs-lock__shackle");
                Child(lockIcon, "rs-lock__body");
                card.Badge = Text(thumb, "rs-card__badge", "");

                var body = Child(card.Root, "rs-card__body");
                Text(body, "rs-card__numeral", "REALM " + ChamberTransitionHud.Roman(realm.Id));
                Text(body, "rs-card__name", realm.DisplayName);
                if (!string.IsNullOrEmpty(realm.Tagline)) Text(body, "rs-card__tagline", realm.Tagline);
                var footer = Child(body, "rs-card__footer");
                card.Meta = Text(footer, "rs-card__meta", "");
                card.Best = Text(footer, "rs-card__meta", "");
                var bar = Child(body, "rs-bar");
                card.Fill = Child(bar, "rs-bar__fill");
                card.Fill.style.backgroundColor = realm.Accent;

                content.Add(card.Root);
                _cards.Add(card);
            }
            Refresh();
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
