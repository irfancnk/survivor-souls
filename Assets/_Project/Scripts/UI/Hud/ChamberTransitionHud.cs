using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Portal transition on the HUD (KAN-40): the full-screen white fade drawn from <see cref="ChamberTransition.Fade"/>
    /// and the "CHAMBER II / REALM I" title card when a new Chamber starts after a portal (<see cref="ChamberTransition.Arrived"/>).
    /// The card fades in over the end of the white, holds, then fades out; all on unscaled time (the arrival is paused).
    /// Both elements ignore touches. Element names are constants below; keep them in sync with GameHud.uxml.
    /// Looks: transition-fade, chamber-card* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ChamberTransitionHud : MonoBehaviour
    {
        private const string FadeName = "transition-fade";
        private const string CardName = "chamber-card";
        private const string TitleName = "chamber-card-title";
        private const string SubName = "chamber-card-sub";
        private const string HiddenClass = "hud-hidden";

        [SerializeField] private ChamberTransition transition;
        [SerializeField, Min(0.05f)] private float cardFadeIn = 0.3f;
        [SerializeField, Min(0f)] private float cardHold = 1.5f;
        [SerializeField, Min(0.05f)] private float cardFadeOut = 0.6f;
        [Tooltip("Card rises this many px while it is shown.")]
        [SerializeField, Min(0f)] private float cardRise = 24f;

        private UIDocument _document;
        private VisualElement _fade, _card;
        private Label _title, _sub;
        private float _drawnFade = -1f;
        private float _cardAge = -1f;

        /// <summary>Text of the last title card (tests and dev tools).</summary>
        public string LastCardTitle { get; private set; }
        public bool CardVisible => _cardAge >= 0f;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _fade = root.Q(FadeName);
            _card = root.Q(CardName);
            _title = root.Q<Label>(TitleName);
            _sub = root.Q<Label>(SubName);
            if (transition != null) transition.Arrived += OnArrived;
        }

        private void OnDisable()
        {
            if (transition != null) transition.Arrived -= OnArrived;
        }

        private void OnArrived(int chamber)
        {
            LastCardTitle = CardTitle(chamber);
            if (_card == null) return;
            if (_title != null) _title.text = LastCardTitle;
            if (_sub != null) _sub.text = "REALM " + Roman(RunState.Realm);
            _card.style.opacity = 0f;
            _card.RemoveFromClassList(HiddenClass);
            _cardAge = 0f;
        }

        private void LateUpdate()
        {
            if (transition != null) DrawFade(transition.Fade);
            UpdateCard(Time.unscaledDeltaTime);
        }

        private void DrawFade(float fade)
        {
            if (_fade == null || Mathf.Approximately(fade, _drawnFade)) return;
            _drawnFade = fade;
            _fade.EnableInClassList(HiddenClass, fade <= 0f);
            _fade.style.opacity = fade;
        }

        private void UpdateCard(float dt)
        {
            if (_cardAge < 0f || _card == null) return;
            _cardAge += Mathf.Min(dt, 0.1f);
            float life = cardFadeIn + cardHold + cardFadeOut;
            if (_cardAge >= life)
            {
                _cardAge = -1f;
                _card.AddToClassList(HiddenClass);
                return;
            }
            float a = Mathf.Clamp01(_cardAge / cardFadeIn) * Mathf.Clamp01((life - _cardAge) / cardFadeOut);
            _card.style.opacity = a;
            _card.style.translate = new Translate(0f, -cardRise * (_cardAge / life));
        }

        public static string CardTitle(int chamber) => "CHAMBER " + Roman(chamber);

        /// <summary>Roman numeral for 1..3999 (Dark Souls style titles); other values as digits.</summary>
        public static string Roman(int n)
        {
            if (n < 1 || n > 3999) return n.ToString();
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
                while (n >= values[i])
                {
                    sb.Append(symbols[i]);
                    n -= values[i];
                }
            return sb.ToString();
        }
    }
}
