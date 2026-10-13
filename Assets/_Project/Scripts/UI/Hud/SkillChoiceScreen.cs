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
    /// rarity, icon, name, one-stack text, stack pips (owned + the one this pick adds) and a NEW badge. Cards rise in one
    /// after another; taps count only after <see cref="armDelay"/> (a thumb still on the stick must not pick by accident).
    /// A pick grants the skill at once, pulses the card, fades the rest and hands control back. Keys 1-3 pick on desktop.
    /// Unscaled time. Nothing to offer (every skill maxed): closes at once. Looks: skills* / skill-card* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillChoiceScreen : LevelUpChoice
    {
        private const string ScreenName = "skill-choice";
        private const string TitleName = "skill-choice-title";
        private const string CardPrefix = "skill-card-";
        private const string HiddenClass = "hud-hidden";
        private const string PipClass = "skill-pip";
        private const string PipOnClass = "skill-pip--on";
        private const string PipNextClass = "skill-pip--next";
        private static readonly string[] RarityClasses = { "skill-card--common", "skill-card--rare", "skill-card--epic" };
        private static readonly string[] RarityNames = { "COMMON", "RARE", "EPIC" };
        public const int MaxCards = 3;

        [SerializeField] private SkillManager skills;
        [SerializeField, Range(1, MaxCards)] private int choices = MaxCards;
        [Tooltip("Unscaled seconds after opening before a tap can pick.")]
        [SerializeField, Min(0f)] private float armDelay = 0.4f;
        [SerializeField, Min(0.01f)] private float veilIn = 0.16f;
        [SerializeField, Min(0.01f)] private float cardIn = 0.26f;
        [SerializeField, Min(0f)] private float cardStagger = 0.08f;
        [SerializeField, Min(0.01f)] private float pickOut = 0.38f;
        [SerializeField] private float cardRise = 140f; // panel px

        private sealed class Card
        {
            public Button Root;
            public Label Rarity, Name, Desc, Badge;
            public VisualElement Icon, Pips;
        }

        /// <summary>The player picked a skill (already granted).</summary>
        public event Action<SkillDefinition> Picked;

        private readonly List<SkillDefinition> _options = new List<SkillDefinition>(MaxCards);
        private readonly Card[] _cards = new Card[MaxCards];
        private VisualElement _screen;
        private Label _title;
        private Action _onDone;
        private float _age = -1f;
        private float _pickAge;
        private int _picked = -1;

        public bool IsShowing => _age >= 0f;
        public bool IsArmed => _age >= armDelay && _picked < 0;
        public IReadOnlyList<SkillDefinition> Options => _options;

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(SkillManager manager, float armSeconds = 0.4f)
        {
            skills = manager;
            armDelay = Mathf.Max(0f, armSeconds);
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root?.Q(ScreenName);
            _title = root?.Q<Label>(TitleName);
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
                };
            }
        }

        public override void Present(int level, Action onDone)
        {
            _onDone = onDone;
            _picked = -1;
            _pickAge = 0f;
            if (skills == null || skills.Roll(choices, level, _options) == 0)
            {
                Close(); // nothing left to offer: the level still counts, the run goes on
                return;
            }
            _age = 0f;
            if (_title != null) _title.text = $"LEVEL {level}";
            for (int i = 0; i < MaxCards; i++) Fill(_cards[i], i < _options.Count ? _options[i] : null);
            if (_screen == null) return;
            _screen.style.opacity = 0f;
            _screen.RemoveFromClassList(HiddenClass);
            Animate();
        }

        /// <summary>Picks card <paramref name="index"/>. False when not open, not armed yet, already picked or no such card.</summary>
        public bool Choose(int index)
        {
            if (!IsArmed || index < 0 || index >= _options.Count) return false;
            var skill = _options[index];
            if (!skills.Grant(skill)) return false;
            _picked = index;
            _pickAge = 0f;
            Picked?.Invoke(skill);
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
            if (card.Rarity != null) card.Rarity.text = RarityNames[r];
            if (card.Name != null) card.Name.text = skill.DisplayName.ToUpperInvariant();
            if (card.Desc != null) card.Desc.text = skill.Description;
            if (card.Icon != null) card.Icon.style.backgroundImage = skill.Icon != null ? new StyleBackground(skill.Icon) : StyleKeyword.None;
            int owned = skills.StacksOf(skill);
            if (card.Badge != null) card.Badge.EnableInClassList(HiddenClass, owned > 0);
            if (card.Pips == null) return;
            while (card.Pips.childCount < skill.MaxStack)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList(PipClass);
                card.Pips.Add(pip);
            }
            for (int i = 0; i < card.Pips.childCount; i++)
            {
                var pip = card.Pips[i];
                pip.style.display = i < skill.MaxStack ? DisplayStyle.Flex : DisplayStyle.None;
                pip.EnableInClassList(PipOnClass, i < owned);
                pip.EnableInClassList(PipNextClass, i == owned);
            }
        }

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
