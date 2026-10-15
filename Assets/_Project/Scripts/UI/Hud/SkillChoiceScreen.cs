using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Level-up skill pick (KAN-19), the <see cref="LevelUpChoice"/> that <see cref="LevelUpFlow"/> opens while the game is
    /// paused: "LEVEL N" header and up to 3 rune cards ("Ember Boons") rolled by <see cref="SkillManager.Roll"/>. Each card:
    /// rarity, icon, name, text of the level this pick gives, level pips (owned + the one this pick adds) and a badge:
    /// NEW, or "LV N → N+1" for an owned skill (KAN-50). Cards rise in one
    /// after another; taps count only after <see cref="armDelay"/> (a thumb still on the stick must not pick by accident).
    /// A pick grants the skill at once, pulses the card, fades the rest and hands control back. Keys 1-3 pick on desktop.
    /// Unscaled time. Nothing to offer (every skill maxed): closes at once. Looks: skills* / skill-card* in GameHud.uss.
    /// Tier (KAN-37): colour class per <see cref="SkillRarity"/>, tier name + 1-4 gems from <see cref="SkillRarityStyles"/>
    /// (readable without colour), <see cref="CardRevealed"/> when a card lands (GameAudio plays the tier sound), and for
    /// shine / burst tiers (Legendary) a light band sweeping the card and a ring + sparks out of the icon on landing.
    /// Chest upgrade mode (KAN-73, <see cref="PresentUpgrade"/>): same cards, gold "RELIQUARY" band (class skills--chest,
    /// Rare chest: skills--chest-rare), owned skills from <see cref="SkillManager.RollUpgrades"/> only, badge
    /// "LV N → N+k" and the text of level N+k (k = the chest's levels, capped at max), a pick calls
    /// <see cref="SkillManager.Upgrade(SkillDefinition, int)"/> and raises <see cref="Upgraded"/>.
    /// Re-roll (KAN-67, level-up only, hidden on the chest screen): button under the cards with the re-rolls left this run
    /// (<see cref="SkillManager.RerollsLeft"/>, kept in <see cref="RunState"/>). A tap spends one, rolls a new offer without
    /// the cards on screen (<see cref="SkillManager.Roll"/> with exclude), flips the cards shut and open again on the new
    /// skills (tier reveal plays again) and raises <see cref="Rerolled"/> (whoosh) + a light haptic. Taps count after
    /// <see cref="armDelay"/>, one per <see cref="rerollCooldown"/>, never during a flip. Disabled at 0 left, or with a hint
    /// when no other skill could be offered. Key R re-rolls on desktop.
    /// Slot-machine roll (KAN-159): on open and after each re-roll, every card's icon + name scroll top to bottom through
    /// the skills that could be offered now (<see cref="SkillManager.CollectOfferable"/>; chest: owned upgradable skills)
    /// and stop left to right at <see cref="reelStops"/> (0.6 / 0.8 / 1.0 s) on the skill rolled BEFORE the roll starts
    /// (the reel is only a show: result, odds and pity are untouched). Rim colour, tier, gems, text, pips and badge stay
    /// hidden until the card stops; the stop plays a small bounce, <see cref="ReelStopped"/> (clunk), a light haptic and the
    /// tier reveal. <see cref="ReelTicked"/> (soft tick, rate limited) as skills pass. A tap anywhere (or a pick key) during
    /// the roll stops all reels at once and picks nothing; picks and re-rolls arm after the last stop + <see cref="armDelay"/>.
    /// Off with the "Fast Skill Reveal" setting (<see cref="GameSettings.FastSkillReveal"/>): cards land as before.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillChoiceScreen : LevelUpChoice
    {
        private const string ScreenName = "skill-choice";
        private const string TitleName = "skill-choice-title";
        private const string SubName = "skill-choice-sub";
        private const string RerollName = "skill-reroll";
        private const string RerollTextName = "skill-reroll-text";
        private const string RerollCountName = "skill-reroll-count";
        public const string RerollText = "RE-ROLL";
        public const string RerollNoneText = "NO OTHER BOONS";
        private const string ChestClass = "skills--chest";
        private const string RareChestClass = "skills--chest-rare";
        public const string BoonSubtitle = "CHOOSE AN EMBER BOON";
        private const string CardPrefix = "skill-card-";
        private const string HiddenClass = "hud-hidden";
        private const string PipClass = "skill-pip";
        private const string PipOnClass = "skill-pip--on";
        private const string PipNextClass = "skill-pip--next";
        private const string BadgeUpClass = "skill-card__badge--up";
        private const string MarkClass = "skill-mark";
        private const string SparkClass = "skill-spark";
        private const string RollingClass = "skill-card--rolling";
        private const string ReelClass = "skill-card__reel";
        private const string ReelNameClass = "skill-card__reel-name";
        private static readonly string[] RarityClasses = { "skill-card--common", "skill-card--rare", "skill-card--epic", "skill-card--legendary" };
        public const int MaxCards = 3;
        private const int MaxMarks = 4;
        private const int Sparks = 8;
        /// <summary>Card animation progress (0..1) at which it counts as landed: reveal sound + burst.</summary>
        private const float RevealAt = 0.6f;

        [SerializeField] private SkillManager skills;
        [Tooltip("Tier names, gems, reveal sounds, shine / burst (KAN-37). Null = built-in defaults.")]
        [SerializeField] private SkillRarityStyles styles;
        [SerializeField, Range(1, MaxCards)] private int choices = MaxCards;
        [Tooltip("Unscaled seconds after opening before a tap can pick.")]
        [SerializeField, Min(0f)] private float armDelay = 0.4f;
        [SerializeField, Min(0.01f)] private float veilIn = 0.16f;
        [SerializeField, Min(0.01f)] private float cardIn = 0.26f;
        [SerializeField, Min(0f)] private float cardStagger = 0.08f;
        [SerializeField, Min(0.01f)] private float pickOut = 0.38f;
        [SerializeField] private float cardRise = 140f; // panel px
        [Tooltip("Unscaled seconds for one shine sweep across a Legendary card (plus a short rest).")]
        [SerializeField, Min(0.1f)] private float shinePeriod = 2f;
        [SerializeField, Min(0.05f)] private float burstTime = 0.55f;
        [Tooltip("Re-roll (KAN-67): unscaled seconds the old cards take to flip shut.")]
        [SerializeField, Min(0.01f)] private float flipOut = 0.15f;
        [Tooltip("Re-roll: unscaled seconds a new card takes to flip open.")]
        [SerializeField, Min(0.01f)] private float flipIn = 0.2f;
        [SerializeField, Min(0f)] private float flipStagger = 0.05f;
        [Tooltip("Re-roll: a second tap within this many unscaled seconds is ignored (one tap = one re-roll).")]
        [SerializeField, Min(0f)] private float rerollCooldown = 0.3f;
        [Header("Slot-machine roll (KAN-159)")]
        [Tooltip("Cards roll through skills before they stop on the offer. Off = cards land at once (also the Fast Skill Reveal setting).")]
        [SerializeField] private bool reel = true;
        [Tooltip("Unscaled seconds after the roll starts at which card 1, 2, 3 stop.")]
        [SerializeField] private float[] reelStops = { 0.6f, 0.8f, 1f };
        [Tooltip("Skills per second passing a card at full speed.")]
        [SerializeField, Min(1f)] private float reelSpeed = 16f;
        [Tooltip("Unscaled seconds of slow-down before each stop.")]
        [SerializeField, Min(0f)] private float reelDecel = 0.3f;
        [Tooltip("Unscaled seconds of the landing bounce after a stop.")]
        [SerializeField, Min(0.01f)] private float reelBounce = 0.16f;
        [Tooltip("Landing bounce: icon overshoot in panel px.")]
        [SerializeField] private float reelBouncePx = 26f;
        [Tooltip("Icon height stretch at full reel speed (motion blur feel).")]
        [SerializeField, Min(0f)] private float reelStretch = 0.45f;
        [Tooltip("At most one tick sound per this many unscaled seconds, however many reels pass a skill.")]
        [SerializeField, Min(0f)] private float reelTickGap = 0.05f;
        /// <summary>Distance (panel px) the reel moves an item down: the icon plate's inner height.</summary>
        private const float ReelSlot = 188f;

        private sealed class Card
        {
            public Button Root;
            public Label Rarity, Name, Desc, Badge;
            public VisualElement Icon, Pips, Marks, Shine, Burst;
            public readonly VisualElement[] SparkDots = new VisualElement[Sparks];
            public SkillRarityStyles.Style Style;
            public bool Revealed;
            public float RevealAge;
            // KAN-159 reel
            public VisualElement Reel;
            public Label ReelName;
            public int RarityIndex;
            public bool Stopped = true;
            public int StopItem, ReelSeed, ReelAvoid, ShownItem = -1, NameItem = -1;
            public float StoppedAt, RevealTime;
        }

        /// <summary>The player picked a skill (already granted or upgraded).</summary>
        public event Action<SkillDefinition> Picked;
        /// <summary>Chest upgrade picked (already applied): skill, old level, new level. Sound, burst, telemetry.</summary>
        public event Action<SkillDefinition, int, int> Upgraded;
        /// <summary>A card landed on screen (once per card per offer): card index, its skill. GameAudio plays the tier sound.</summary>
        public event Action<int, SkillDefinition> CardRevealed;
        /// <summary>A re-roll was spent (KAN-67): re-rolls left this run. GameAudio plays the whoosh.</summary>
        public event Action<int> Rerolled;
        /// <summary>A skill passed on a rolling reel (KAN-159), rate limited to one per <see cref="reelTickGap"/>. Soft tick.</summary>
        public event Action ReelTicked;
        /// <summary>A card's reel stopped on its skill (KAN-159): card index, skill. Clunk; the tier reveal follows at once.</summary>
        public event Action<int, SkillDefinition> ReelStopped;

        private readonly List<SkillDefinition> _options = new List<SkillDefinition>(MaxCards);
        private readonly List<SkillDefinition> _next = new List<SkillDefinition>(MaxCards);
        private readonly Card[] _cards = new Card[MaxCards];
        private VisualElement _screen;
        private Label _title, _sub;
        private Button _reroll;
        private Label _rerollText, _rerollCount;
        private int _level;
        private bool _rerollPossible;
        private int _shownLeft = -1;
        private float _rerollAt;
        private float _flip = -1f;
        private bool _flipDeal;
        private float _dealAt;
        private float _armAt;
        private bool _upgradeMode;
        private int _levels = 1;
        private Action _onDone;
        private float _age = -1f;
        private float _pickAge;
        private int _picked = -1;
        private readonly List<SkillDefinition> _reelPool = new List<SkillDefinition>(32);
        private readonly Dictionary<SkillDefinition, string> _upperNames = new Dictionary<SkillDefinition, string>();
        private float _rollAt = -1f;
        private float _skipAfter = float.PositiveInfinity;
        private float _lastTickAt = float.NegativeInfinity;
        private int _reelsLeft;

        public bool IsShowing => _age >= 0f;
        /// <summary>Open as a chest upgrade screen (KAN-73), not a level-up pick.</summary>
        public bool IsUpgrade => IsShowing && _upgradeMode;
        public string Title => _title?.text;
        public string Subtitle => _sub?.text;
        public bool IsArmed => _age >= armDelay && _age >= _armAt && _picked < 0 && _flip < 0f && _reelsLeft == 0;
        public IReadOnlyList<SkillDefinition> Options => _options;
        /// <summary>Re-rolls left this run (KAN-67).</summary>
        public int RerollsLeft => skills != null ? skills.RerollsLeft : 0;
        /// <summary>The re-roll button is shown: open as a level-up pick (not the chest screen).</summary>
        public bool RerollShown => IsShowing && !_upgradeMode && _reroll != null;
        /// <summary>A tap on re-roll would work now: level-up pick, armed, no flip, one left, another skill could be offered.</summary>
        public bool CanReroll => IsShowing && !_upgradeMode && _picked < 0 && _flip < 0f && _age >= armDelay && _rerollPossible && RerollsLeft > 0
                                 && _reelsLeft == 0 && _age >= _armAt;
        /// <summary>The cards are flipping to a new offer (old ones shutting).</summary>
        public bool IsFlipping => _flip >= 0f;
        /// <summary>Re-roll button label and count as shown (tests, screenshots).</summary>
        public string RerollLabel => _rerollText?.text;
        public string RerollCountLabel => _rerollCount?.text;
        public bool RerollEnabled => _reroll != null && _reroll.enabledSelf;
        public SkillRarityStyles Styles => styles;
        /// <summary>At least one card's reel is still rolling (KAN-159): picks and re-rolls wait.</summary>
        public bool IsRolling => _age >= 0f && _reelsLeft > 0;
        /// <summary>The roll is on for the next open (component switch and not the Fast Skill Reveal setting).</summary>
        public bool ReelOn => reel && !GameSettings.FastSkillReveal;
        /// <summary>Skills the reels roll through this offer (tests: never a maxed one).</summary>
        public IReadOnlyList<SkillDefinition> ReelPool => _reelPool;
        /// <summary>Seconds after the roll start at which card <paramref name="index"/> stops (no skip).</summary>
        public float ReelStopTime(int index) =>
            reelStops != null && reelStops.Length > 0 ? Mathf.Max(0.05f, reelStops[Mathf.Min(index, reelStops.Length - 1)]) : 0.6f + 0.2f * index;
        /// <summary>Skill card <paramref name="index"/> shows now (the reel item while it rolls, the offer once stopped). Tests.</summary>
        public SkillDefinition ShownSkill(int index) =>
            index < 0 || index >= _options.Count || _cards[index] == null ? null
            : _cards[index].Stopped ? _options[index] : ReelSkill(_cards[index], index, _cards[index].ShownItem);
        /// <summary>Card <paramref name="index"/>'s reel has stopped (always true without the roll).</summary>
        public bool IsStopped(int index) => index < 0 || index >= MaxCards || _cards[index] == null || _cards[index].Stopped;

        /// <summary>Tier name, gems, sound and flags for <paramref name="rarity"/> (asset entry or built-in default).</summary>
        public SkillRarityStyles.Style StyleOf(SkillRarity rarity) => styles != null ? styles.Of(rarity) : DefaultStyles[(int)rarity];

        private static readonly SkillRarityStyles.Style[] DefaultStyles = SkillRarityStyles.Defaults();

        /// <summary>Wiring for code-built objects and tests.</summary>
        /// <remarks>Code-built screens have no slot-machine roll unless <paramref name="slotRoll"/> (KAN-159); the scene's has it.</remarks>
        public void Setup(SkillManager manager, float armSeconds = 0.4f, SkillRarityStyles rarityStyles = null, bool slotRoll = false)
        {
            skills = manager;
            armDelay = Mathf.Max(0f, armSeconds);
            styles = rarityStyles;
            reel = slotRoll;
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root?.Q(ScreenName);
            _title = root?.Q<Label>(TitleName);
            _sub = root?.Q<Label>(SubName);
            _reroll = root?.Q<Button>(RerollName);
            _rerollText = root?.Q<Label>(RerollTextName);
            _rerollCount = root?.Q<Label>(RerollCountName);
            if (_reroll != null) _reroll.clicked += OnRerollClicked;
            _screen?.RegisterCallback<PointerDownEvent>(OnScreenPointerDown, TrickleDown.TrickleDown); // tap stops the reels
            for (int i = 0; i < MaxCards; i++)
            {
                var button = root?.Q<Button>(CardPrefix + i);
                if (button == null) { _cards[i] = null; continue; }
                int index = i;
                button.clicked += () => Choose(index);
                _cards[i] = new Card
                {
                    Root = button,
                    Rarity = button.Q<Label>(className: "skill-card__rarity"),
                    Name = button.Q<Label>(className: "skill-card__name"),
                    Desc = button.Q<Label>(className: "skill-card__desc"),
                    Badge = button.Q<Label>(className: "skill-card__badge"),
                    Icon = button.Q(className: "skill-card__icon"),
                    Pips = button.Q(className: "skill-card__pips"),
                    Marks = button.Q(className: "skill-card__marks"),
                    Shine = button.Q(className: "skill-card__shine"),
                    Burst = button.Q(className: "skill-card__burst"),
                };
                BuildDecor(_cards[i]);
            }
        }

        /// <summary>Tier gems, burst sparks and the reel's second icon: made once per card, shown / moved in Fill and Animate.</summary>
        private static void BuildDecor(Card card)
        {
            card.Reel = card.Icon?.parent?.Q(className: ReelClass); // re-enable: reuse the reel parts made before
            card.ReelName = card.Root.Q<Label>(className: ReelNameClass);
            if (card.Icon?.parent != null && card.Reel == null)
            {
                card.Reel = new VisualElement { pickingMode = PickingMode.Ignore };
                card.Reel.AddToClassList("skill-card__icon");
                card.Reel.AddToClassList(ReelClass); // absolute, shown only while the card rolls (KAN-159)
                card.Icon.parent.Insert(card.Icon.parent.IndexOf(card.Icon) + 1, card.Reel);
            }
            if (card.Name != null && card.ReelName == null)
            {
                // Over the real name (which keeps the card's layout while hidden): one line, so the card never resizes.
                // A sibling, not a child: a Label with children loses its text measure (height 0).
                card.ReelName = new Label { pickingMode = PickingMode.Ignore };
                card.ReelName.AddToClassList(ReelNameClass);
                card.Root.Insert(card.Root.IndexOf(card.Name) + 1, card.ReelName);
            }
            if (card.Marks != null)
                while (card.Marks.childCount < MaxMarks)
                {
                    var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                    mark.AddToClassList(MarkClass);
                    card.Marks.Add(mark);
                }
            var plate = card.Burst?.parent;
            if (plate == null) return;
            for (int k = 0; k < Sparks; k++)
            {
                var spark = new VisualElement { pickingMode = PickingMode.Ignore };
                spark.AddToClassList(SparkClass);
                plate.Insert(plate.IndexOf(card.Burst) + 1, spark); // behind the icon
                card.SparkDots[k] = spark;
            }
        }

        public override void Present(int level, Action onDone)
        {
            _onDone = onDone;
            _picked = -1;
            _pickAge = 0f;
            _upgradeMode = false;
            _levels = 1;
            _level = level;
            if (skills == null || skills.Roll(choices, level, _options) == 0)
            {
                Close(); // nothing left to offer: the level still counts, the run goes on
                return;
            }
            Open($"LEVEL {level}", BoonSubtitle, false, false);
        }

        /// <summary>
        /// Chest upgrade choice (KAN-73): up to <see cref="UpgradeOffer.Choices"/> owned skills that can level up. False
        /// (screen stays closed, <paramref name="onDone"/> not called) when none can.
        /// </summary>
        public override bool PresentUpgrade(UpgradeOffer offer, Action onDone)
        {
            if (skills == null || skills.RollUpgrades(Mathf.Min(offer.Choices, MaxCards), _options) == 0) return false;
            _onDone = onDone;
            _picked = -1;
            _pickAge = 0f;
            _upgradeMode = true;
            _levels = Mathf.Max(1, offer.Levels);
            Open(offer.Rare ? "RARE RELIQUARY" : "RELIQUARY", UpgradeSubtitle(_levels), true, offer.Rare);
            return true;
        }

        /// <summary>Chest screen subtitle: "UPGRADE A SKILL", or "UPGRADE A SKILL +N LEVELS" for a Rare chest.</summary>
        public static string UpgradeSubtitle(int levels) => levels > 1 ? $"UPGRADE A SKILL +{levels} LEVELS" : "UPGRADE A SKILL";

        private void Open(string title, string subtitle, bool chest, bool rare)
        {
            _age = 0f;
            _flip = -1f;
            _flipDeal = false;
            _dealAt = 0f;
            _armAt = 0f;
            _rerollAt = float.NegativeInfinity;
            if (_title != null) _title.text = title;
            if (_sub != null) _sub.text = subtitle;
            for (int i = 0; i < MaxCards; i++) Fill(_cards[i], i < _options.Count ? _options[i] : null);
            RefreshReroll();
            StartRoll();
            if (_screen == null) return;
            _screen.EnableInClassList(ChestClass, chest);
            _screen.EnableInClassList(RareChestClass, chest && rare);
            _screen.style.opacity = 0f;
            _screen.RemoveFromClassList(HiddenClass);
            Animate();
        }

        /// <summary>Picks card <paramref name="index"/>. False when not open, not armed yet, already picked or no such card.</summary>
        public bool Choose(int index)
        {
            if (IsRolling)
            {
                SkipRoll(); // the first tap stops the reels; picking arms after armDelay
                return false;
            }
            if (!IsArmed || index < 0 || index >= _options.Count) return false;
            var skill = _options[index];
            int old = skills.GetLevel(skill);
            if (_upgradeMode ? !skills.Upgrade(skill, _levels) : !skills.Grant(skill)) return false;
            _picked = index;
            _pickAge = 0f;
            Picked?.Invoke(skill);
            if (_upgradeMode) Upgraded?.Invoke(skill, old, skills.GetLevel(skill));
            return true;
        }

        private void OnRerollClicked() => Reroll();

        /// <summary>
        /// Spends one re-roll (KAN-67): new offer without the cards on screen, cards flip to it. False when not possible now
        /// (<see cref="CanReroll"/>) or within <see cref="rerollCooldown"/> of the last one (double tap).
        /// </summary>
        public bool Reroll()
        {
            if (!CanReroll || _age - _rerollAt < rerollCooldown) return false;
            if (skills.Roll(choices, _level, _next, _options) == 0) return false;
            if (!RunState.TryUseReroll(skills.Progression != null ? skills.Progression.RerollsPerRun : 0)) return false;
            _rerollAt = _age;
            _flip = 0f;
            Haptics.Light();
            Rerolled?.Invoke(RerollsLeft);
            RefreshRerollCount();
            return true;
        }

        /// <summary>Old cards shut: show the new offer and flip it open (tier reveal again).</summary>
        private void SwapOffer()
        {
            _flip = -1f;
            _options.Clear();
            _options.AddRange(_next);
            _flipDeal = true;
            _dealAt = _age;
            _armAt = _age + flipIn * 0.5f;
            for (int i = 0; i < MaxCards; i++) Fill(_cards[i], i < _options.Count ? _options[i] : null);
            RefreshReroll();
            StartRoll(); // the new cards roll too (KAN-159)
        }

        // ------------------------------------------------------------ Slot-machine roll (KAN-159)

        /// <summary>Starts the reels on the offer in <see cref="_options"/> (already rolled), or leaves the cards still when off.</summary>
        private void StartRoll()
        {
            _rollAt = -1f;
            _reelsLeft = 0;
            _skipAfter = float.PositiveInfinity;
            if (!ReelOn || _options.Count == 0) return;
            if (skills != null)
            {
                if (_upgradeMode) skills.CollectUpgradable(_reelPool);
                else skills.CollectOfferable(_level, _reelPool);
            }
            else _reelPool.Clear();
            foreach (var s in _options)
                if (s != null && !_reelPool.Contains(s)) _reelPool.Add(s); // forced dev offers: the reel still lands on them
            _rollAt = _age;
            float last = 0f;
            for (int i = 0; i < _options.Count && i < MaxCards; i++)
            {
                var card = _cards[i];
                float stop = ReelStopTime(i);
                if (card == null) continue; // no panel (logic-only tests): nothing to roll
                last = Mathf.Max(last, stop);
                _reelsLeft++;
                card.Stopped = false;
                card.StopItem = SkillReel.StopItem(stop, reelDecel, reelSpeed);
                card.ReelSeed = UnityEngine.Random.Range(0, _reelPool.Count);
                card.ReelAvoid = _reelPool.IndexOf(_options[i]);
                card.ShownItem = -1;
                card.NameItem = -1;
                card.Root.AddToClassList(RollingClass);
                for (int r = 0; r < RarityClasses.Length; r++) card.Root.EnableInClassList(RarityClasses[r], false); // neutral rim
            }
            _armAt = Mathf.Max(_armAt, _rollAt + last + armDelay);
            _lastTickAt = float.NegativeInfinity;
        }

        /// <summary>Stops every rolling reel now (tap during the roll). Picks arm <see cref="armDelay"/> later. False when none rolls.</summary>
        public bool SkipRoll()
        {
            if (!IsRolling) return false;
            _skipAfter = Mathf.Max(0f, _age - _rollAt);
            _armAt = _age + armDelay;
            UpdateReels(); // land them this frame, so the tap feels instant
            return true;
        }

        private void OnScreenPointerDown(PointerDownEvent e)
        {
            if (IsRolling) SkipRoll();
        }

        /// <summary>Skill on reel item <paramref name="item"/> of card <paramref name="index"/>: the offer on the stop item.</summary>
        private SkillDefinition ReelSkill(Card card, int index, int item)
        {
            if (item >= card.StopItem || _reelPool.Count == 0) return _options[index];
            int k = SkillReel.PoolIndex(item, card.StopItem, card.ReelSeed, _reelPool.Count, card.ReelAvoid);
            return k >= 0 ? _reelPool[k] : _options[index];
        }

        private string UpperName(SkillDefinition skill)
        {
            if (skill == null) return string.Empty;
            if (!_upperNames.TryGetValue(skill, out string name))
            {
                name = skill.DisplayName.ToUpperInvariant(); // once per skill: no allocation while the reels roll
                _upperNames[skill] = name;
            }
            return name;
        }

        /// <summary>Moves the rolling reels, lands the ones whose stop time has come (bounce, clunk, haptic, tier colour).</summary>
        private void UpdateReels()
        {
            if (_rollAt < 0f) return;
            float rt = _age - _rollAt;
            bool passed = false;
            for (int i = 0; i < _options.Count && i < MaxCards; i++)
            {
                var card = _cards[i];
                float stop = Mathf.Min(ReelStopTime(i), _skipAfter);
                if (card == null) continue;
                if (card.Stopped)
                {
                    AnimateLanding(card, rt);
                    continue;
                }
                if (rt >= stop)
                {
                    Land(card, i, rt);
                    continue;
                }
                float p = SkillReel.Position(rt, ReelStopTime(i), card.StopItem, reelDecel);
                int item = Mathf.FloorToInt(p);
                float frac = p - item;
                if (item != card.ShownItem)
                {
                    if (card.ShownItem >= 0) passed = true;
                    card.ShownItem = item;
                    var now = ReelSkill(card, i, item);
                    var next = ReelSkill(card, i, item + 1);
                    if (card.Icon != null) card.Icon.style.backgroundImage = now.Icon != null ? new StyleBackground(now.Icon) : StyleKeyword.None;
                    if (card.Reel != null) card.Reel.style.backgroundImage = next.Icon != null ? new StyleBackground(next.Icon) : StyleKeyword.None;
                }
                float stretch = 1f + reelStretch * SkillReel.Speed01(rt, ReelStopTime(i), reelDecel);
                // Top to bottom: the current skill slides down and out, the next one comes in from above.
                if (card.Icon != null)
                {
                    card.Icon.style.translate = new Translate(0f, frac * ReelSlot);
                    card.Icon.style.scale = new Scale(new Vector3(1f, stretch, 1f));
                }
                if (card.Reel != null)
                {
                    card.Reel.style.translate = new Translate(0f, (frac - 1f) * ReelSlot);
                    card.Reel.style.scale = new Scale(new Vector3(1f, stretch, 1f));
                }
                if (card.ReelName != null && card.Name != null)
                {
                    // The name of the icon nearer the plate centre, sliding down with it.
                    int nameItem = frac < 0.5f ? item : item + 1;
                    float offset = frac < 0.5f ? frac : frac - 1f;
                    if (nameItem != card.NameItem)
                    {
                        card.NameItem = nameItem;
                        card.ReelName.text = UpperName(ReelSkill(card, i, nameItem));
                    }
                    // Absolute in the card: follow the hidden real name's slot (top is measured inside the top border).
                    card.ReelName.style.top = card.Name.layout.y - card.Root.resolvedStyle.borderTopWidth;
                    card.ReelName.style.translate = new Translate(0f, offset * 44f);
                    card.ReelName.style.opacity = 1f - 1.4f * Mathf.Abs(offset);
                }
            }
            if (passed && _age - _lastTickAt >= reelTickGap)
            {
                _lastTickAt = _age;
                ReelTicked?.Invoke();
            }
        }

        /// <summary>Card <paramref name="index"/> lands on its offer: real icon, name, tier rim; clunk + haptic; reveal follows.</summary>
        private void Land(Card card, int index, float rt)
        {
            card.Stopped = true;
            card.StoppedAt = rt;
            card.ShownItem = card.StopItem;
            _reelsLeft = Mathf.Max(0, _reelsLeft - 1);
            var skill = _options[index];
            card.Root.RemoveFromClassList(RollingClass);
            for (int r = 0; r < RarityClasses.Length; r++) card.Root.EnableInClassList(RarityClasses[r], r == card.RarityIndex);
            if (card.Icon != null)
            {
                card.Icon.style.backgroundImage = skill.Icon != null ? new StyleBackground(skill.Icon) : StyleKeyword.None;
                card.Icon.style.scale = StyleKeyword.Null;
            }
            AnimateLanding(card, rt);
            Haptics.Light();
            ReelStopped?.Invoke(index, skill);
        }

        /// <summary>Landing bounce: the icon overshoots down a little and settles (top-to-bottom reel direction).</summary>
        private void AnimateLanding(Card card, float rt)
        {
            float k = (rt - card.StoppedAt) / reelBounce;
            if (card.Icon == null || k > 2f) return; // settled: nothing to move

            float y = k < 1f ? SkillReel.Bounce(k) * reelBouncePx : 0f;
            card.Icon.style.translate = new Translate(0f, y);
        }

        /// <summary>Puts a card back to its still look (no reel): used by Fill before a new offer.</summary>
        private static void ResetReel(Card card)
        {
            card.Stopped = true;
            card.ShownItem = -1;
            card.Root.RemoveFromClassList(RollingClass);
            if (card.Icon != null)
            {
                card.Icon.style.translate = StyleKeyword.Null;
                card.Icon.style.scale = StyleKeyword.Null;
            }
        }

        /// <summary>Shows / hides the button, checks if another skill could be offered, sets text, count and enabled.</summary>
        private void RefreshReroll()
        {
            _rerollPossible = !_upgradeMode && skills != null && skills.OfferableCount(_level) > _options.Count;
            if (_reroll == null) return;
            _reroll.style.display = _upgradeMode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_rerollText != null) _rerollText.text = _rerollPossible ? RerollText : RerollNoneText;
            RefreshRerollCount();
        }

        private void RefreshRerollCount()
        {
            if (_reroll == null) return;
            int left = RerollsLeft;
            _shownLeft = left;
            if (_rerollCount != null) _rerollCount.text = $"x{left}";
            _reroll.SetEnabled(_rerollPossible && left > 0);
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advances the open/pick animation. Public for tests (Update calls it with unscaled time).</summary>
        public void Tick(float dt)
        {
            if (_age < 0f) return;
            _age += dt;
            if (_flip >= 0f)
            {
                _flip += dt;
                if (_flip >= flipOut) SwapOffer();
            }
            if (_reroll != null && !_upgradeMode && RerollsLeft != _shownLeft) RefreshRerollCount(); // debug / bonus re-rolls
            if (_picked < 0) ReadKeys();
            UpdateReels();
            if (_picked >= 0)
            {
                _pickAge += dt;
                if (_pickAge >= pickOut)
                {
                    Close();
                    return;
                }
            }
            Animate();
        }

        private void ReadKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) Choose(0);
            else if (kb.digit2Key.wasPressedThisFrame) Choose(1);
            else if (kb.digit3Key.wasPressedThisFrame) Choose(2);
            else if (kb.rKey.wasPressedThisFrame) Reroll();
        }

        private void Close()
        {
            _age = -1f;
            _screen?.AddToClassList(HiddenClass);
            var done = _onDone;
            _onDone = null;
            done?.Invoke();
        }

        private void Fill(Card card, SkillDefinition skill)
        {
            if (card == null) return;
            card.Root.style.display = skill != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (skill == null) return;
            int r = (int)skill.Rarity;
            ResetReel(card);
            card.RarityIndex = r;
            for (int i = 0; i < RarityClasses.Length; i++) card.Root.EnableInClassList(RarityClasses[i], i == r);
            card.Style = StyleOf(skill.Rarity);
            card.Revealed = false;
            card.RevealAge = 0f;
            if (card.Rarity != null) card.Rarity.text = card.Style.label;
            if (card.Marks != null)
                for (int i = 0; i < card.Marks.childCount; i++)
                    card.Marks[i].style.display = i < card.Style.marks ? DisplayStyle.Flex : DisplayStyle.None;
            if (card.Burst != null) card.Burst.style.opacity = 0f;
            foreach (var spark in card.SparkDots)
                if (spark != null) spark.style.opacity = 0f;
            if (card.Name != null) card.Name.text = skill.DisplayName.ToUpperInvariant();
            int owned = skills.GetLevel(skill);
            int gain = _upgradeMode ? Mathf.Max(1, Mathf.Min(_levels, skill.MaxLevel - owned)) : 1;
            if (card.Desc != null) card.Desc.text = skill.DescriptionAt(owned + gain);
            if (card.Icon != null) card.Icon.style.backgroundImage = skill.Icon != null ? new StyleBackground(skill.Icon) : StyleKeyword.None;
            if (card.Badge != null)
            {
                card.Badge.text = BadgeText(owned, gain);
                card.Badge.EnableInClassList(BadgeUpClass, owned > 0);
            }
            if (card.Pips == null) return;
            while (card.Pips.childCount < skill.MaxLevel)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList(PipClass);
                card.Pips.Add(pip);
            }
            for (int i = 0; i < card.Pips.childCount; i++)
            {
                var pip = card.Pips[i];
                pip.style.display = i < skill.MaxLevel ? DisplayStyle.Flex : DisplayStyle.None;
                pip.EnableInClassList(PipOnClass, i < owned);
                pip.EnableInClassList(PipNextClass, i >= owned && i < owned + gain);
            }
        }

        /// <summary>Card corner badge (KAN-50): "NEW" for a skill not owned, "LV N → N+1" for an upgrade.</summary>
        public static string BadgeText(int ownedLevel) => BadgeText(ownedLevel, 1);

        /// <summary>Badge for a pick that adds <paramref name="gain"/> levels: "LV N → N+gain" (chest), "NEW" when not owned.</summary>
        public static string BadgeText(int ownedLevel, int gain) =>
            ownedLevel > 0 ? $"LV {ownedLevel} \u2192 {ownedLevel + Mathf.Max(1, gain)}" : "NEW";

        private void Animate()
        {
            if (_screen == null) return;
            float veil = Mathf.Clamp01(_age / veilIn);
            float outK = _picked >= 0 ? Mathf.Clamp01(_pickAge / pickOut) : 0f;
            float fade = outK < 0.55f ? 1f : 1f - (outK - 0.55f) / 0.45f; // whole screen fades over the last part
            _screen.style.opacity = veil * fade;

            // Re-roll flip (KAN-67): old cards shut along X, new ones open with a small overshoot; no rise.
            float shut = _flip >= 0f ? Mathf.Cos(Mathf.Clamp01(_flip / flipOut) * Mathf.PI * 0.5f) : 1f;
            for (int i = 0; i < _options.Count && i < MaxCards; i++)
            {
                var card = _cards[i];
                if (card == null) continue;
                float t = Mathf.Clamp01((_age - CardStart(i)) / CardLength);
                float rise = _flipDeal ? 1f : EaseOutBack(t);
                float opacity = _flipDeal ? 1f : t;
                float scale = 0.9f + 0.1f * rise;
                if (_rollAt >= 0f && card.Stopped && _picked < 0) // landing pulse on the reel stop (KAN-159)
                    scale *= 1f + 0.05f * SkillReel.Bounce((_age - _rollAt - card.StoppedAt) / reelBounce);
                float flipX = (_flipDeal ? Mathf.Max(0f, EaseOutBack(t)) : 1f) * shut;
                if (_picked >= 0)
                {
                    if (i == _picked) scale *= 1f + 0.09f * Mathf.Sin(Mathf.Min(1f, outK * 1.6f) * Mathf.PI);
                    else
                    {
                        opacity *= 1f - Mathf.Min(1f, outK * 2.2f);
                        rise -= outK * 0.6f;
                    }
                }
                card.Root.style.opacity = opacity;
                card.Root.style.translate = new Translate(0f, (1f - rise) * cardRise);
                card.Root.style.scale = new Scale(new Vector3(scale * flipX, scale, 1f));
                AnimateTier(card, i, t);
            }
        }

        /// <summary>Age at which card <paramref name="index"/> starts landing: rise-in on open, flip-open after a re-roll.</summary>
        private float CardStart(int index) =>
            _flipDeal ? _dealAt + index * flipStagger : veilIn * 0.5f + index * cardStagger;

        private float CardLength => _flipDeal ? flipIn : cardIn;

        /// <summary>Reveal event once the card has landed, Legendary shine sweep and the ring + sparks burst.</summary>
        private void AnimateTier(Card card, int index, float t)
        {
            // With the roll (KAN-159) a card reveals its tier when its reel stops, not when it lands.
            if (!card.Revealed && (_rollAt >= 0f ? card.Stopped : t >= RevealAt))
            {
                card.Revealed = true;
                card.RevealAge = 0f;
                card.RevealTime = _age;
                CardRevealed?.Invoke(index, _options[index]);
            }
            var style = card.Style;
            if (style == null) return;
            if (card.Revealed) card.RevealAge = _age - (_rollAt >= 0f ? card.RevealTime : CardStart(index) + CardLength * RevealAt);

            if (style.shine && card.Shine != null)
            {
                // Sweep left -> right over 75 % of the period, rest for the remainder; first sweep starts as the card lands.
                float phase = card.Revealed ? Mathf.Repeat(card.RevealAge / shinePeriod, 1f) / 0.75f : 2f;
                float x = phase < 1f ? Mathf.Lerp(-160f, 420f, phase * phase * (3f - 2f * phase)) : -220f;
                card.Shine.style.translate = new Translate(x, 0f);
            }

            if (!style.burst || card.Burst == null) return;
            float b = card.Revealed ? Mathf.Clamp01(card.RevealAge / burstTime) : 0f;
            bool live = card.Revealed && b < 1f;
            float ease = 1f - (1f - b) * (1f - b);
            card.Burst.style.opacity = live ? 0.9f * (1f - b) : 0f;
            float ring = 1f + 0.85f * ease;
            card.Burst.style.scale = new Scale(new Vector3(ring, ring, 1f));
            for (int k = 0; k < Sparks; k++)
            {
                var spark = card.SparkDots[k];
                if (spark == null) continue;
                spark.style.opacity = live ? 1f - b : 0f;
                if (!live) continue;
                float angle = (k + 0.5f) * (Mathf.PI * 2f / Sparks);
                float dist = 70f + 150f * ease;
                spark.style.translate = new Translate(Mathf.Cos(angle) * dist, -Mathf.Sin(angle) * dist);
                float size = 1f - 0.6f * b;
                spark.style.scale = new Scale(new Vector3(size, size, 1f));
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
