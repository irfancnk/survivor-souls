using System;
using System.Collections.Generic;
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
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillChoiceScreen : LevelUpChoice
    {
        private const string ScreenName = "skill-choice";
        private const string TitleName = "skill-choice-title";
        private const string SubName = "skill-choice-sub";
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

        private sealed class Card
        {
            public Button Root;
            public Label Rarity, Name, Desc, Badge;
            public VisualElement Icon, Pips, Marks, Shine, Burst;
            public readonly VisualElement[] SparkDots = new VisualElement[Sparks];
            public SkillRarityStyles.Style Style;
            public bool Revealed;
            public float RevealAge;
        }

        /// <summary>The player picked a skill (already granted or upgraded).</summary>
        public event Action<SkillDefinition> Picked;
        /// <summary>Chest upgrade picked (already applied): skill, old level, new level. Sound, burst, telemetry.</summary>
        public event Action<SkillDefinition, int, int> Upgraded;
        /// <summary>A card landed on screen (once per card per offer): card index, its skill. GameAudio plays the tier sound.</summary>
        public event Action<int, SkillDefinition> CardRevealed;

        private readonly List<SkillDefinition> _options = new List<SkillDefinition>(MaxCards);
        private readonly Card[] _cards = new Card[MaxCards];
        private VisualElement _screen;
        private Label _title, _sub;
        private bool _upgradeMode;
        private int _levels = 1;
        private Action _onDone;
        private float _age = -1f;
        private float _pickAge;
        private int _picked = -1;

        public bool IsShowing => _age >= 0f;
        /// <summary>Open as a chest upgrade screen (KAN-73), not a level-up pick.</summary>
        public bool IsUpgrade => IsShowing && _upgradeMode;
        public string Title => _title?.text;
        public string Subtitle => _sub?.text;
        public bool IsArmed => _age >= armDelay && _picked < 0;
        public IReadOnlyList<SkillDefinition> Options => _options;
        public SkillRarityStyles Styles => styles;

        /// <summary>Tier name, gems, sound and flags for <paramref name="rarity"/> (asset entry or built-in default).</summary>
        public SkillRarityStyles.Style StyleOf(SkillRarity rarity) => styles != null ? styles.Of(rarity) : DefaultStyles[(int)rarity];

        private static readonly SkillRarityStyles.Style[] DefaultStyles = SkillRarityStyles.Defaults();

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(SkillManager manager, float armSeconds = 0.4f, SkillRarityStyles rarityStyles = null)
        {
            skills = manager;
            armDelay = Mathf.Max(0f, armSeconds);
            styles = rarityStyles;
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root?.Q(ScreenName);
            _title = root?.Q<Label>(TitleName);
            _sub = root?.Q<Label>(SubName);
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

        /// <summary>Tier gems and burst sparks: made once per card, shown / moved in Fill and Animate.</summary>
        private static void BuildDecor(Card card)
        {
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
            if (_title != null) _title.text = title;
            if (_sub != null) _sub.text = subtitle;
            for (int i = 0; i < MaxCards; i++) Fill(_cards[i], i < _options.Count ? _options[i] : null);
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

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advances the open/pick animation. Public for tests (Update calls it with unscaled time).</summary>
        public void Tick(float dt)
        {
            if (_age < 0f) return;
            _age += dt;
            if (_picked < 0) ReadKeys();
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

            for (int i = 0; i < _options.Count && i < MaxCards; i++)
            {
                var card = _cards[i];
                if (card == null) continue;
                float t = Mathf.Clamp01((_age - veilIn * 0.5f - i * cardStagger) / cardIn);
                float rise = EaseOutBack(t);
                float opacity = t;
                float scale = 0.9f + 0.1f * rise;
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
                card.Root.style.scale = new Scale(new Vector3(scale, scale, 1f));
                AnimateTier(card, i, t);
            }
        }

        /// <summary>Reveal event once the card has landed, Legendary shine sweep and the ring + sparks burst.</summary>
        private void AnimateTier(Card card, int index, float t)
        {
            if (!card.Revealed && t >= RevealAt)
            {
                card.Revealed = true;
                card.RevealAge = 0f;
                CardRevealed?.Invoke(index, _options[index]);
            }
            var style = card.Style;
            if (style == null) return;
            if (card.Revealed) card.RevealAge = _age - (veilIn * 0.5f + index * cardStagger + cardIn * RevealAt);

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
