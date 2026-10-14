using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Boss banner (KAN-62): a dark band across the lower middle of the screen. Intro: the boss name and title fade in while
    /// the camera pulls out (<see cref="BossFight.IntroStarted"/>) and fade out when the fight starts. Kill: "GREAT FOE FELLED"
    /// in bright gold with the soul reward under it (<see cref="BossFight.Defeated"/>), held for <see cref="victoryHold"/> s.
    /// The text swells slowly while shown. Unscaled time (slow motion and level-up pauses do not stall it). Style writes only
    /// while it animates: no allocation per frame. Element names are constants; keep them in sync with GameHud.uxml.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BossBannerHud : MonoBehaviour
    {
        public const string VictoryText = "GREAT FOE FELLED";

        private const string RootName = "boss-banner";
        private const string TextName = "boss-banner-text";
        private const string SubName = "boss-banner-sub";
        private const string HiddenClass = "hud-hidden";
        private const string VictoryClass = "boss-banner--victory";

        [SerializeField] private BossFight fight;
        [SerializeField, Min(0.05f)] private float fadeIn = 0.35f;
        [SerializeField, Min(0.05f)] private float fadeOut = 0.5f;
        [Tooltip("Real seconds the victory banner stays before it fades.")]
        [SerializeField, Min(0f)] private float victoryHold = 2.2f;
        [Tooltip("Text scale at the start and at the end of the hold (slow swell).")]
        [SerializeField] private Vector2 swell = new Vector2(0.94f, 1.04f);

        private UIDocument _document;
        private VisualElement _root;
        private Label _text, _sub;
        private float _age = -1f;     // seconds since shown (-1 = hidden)
        private float _hold;          // seconds to hold before fading (infinity while the intro runs)
        private float _fadeStart = -1f;

        /// <summary>The banner is on screen.</summary>
        public bool IsShown => _age >= 0f;
        /// <summary>Showing the victory line (else the intro name).</summary>
        public bool IsVictory { get; private set; }
        /// <summary>Text on the band now.</summary>
        public string Text => _text != null ? _text.text : "";
        /// <summary>Line under the text now.</summary>
        public string Sub => _sub != null ? _sub.text : "";
        /// <summary>Opacity as drawn.</summary>
        public float Alpha { get; private set; }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossFight bossFight)
        {
            Unsubscribe();
            fight = bossFight;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _root = root.Q(RootName);
            _text = root.Q<Label>(TextName);
            _sub = root.Q<Label>(SubName);
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (fight == null) return;
            fight.IntroStarted += OnIntroStarted;
            fight.IntroEnded += OnIntroEnded;
            fight.Defeated += OnDefeated;
        }

        private void Unsubscribe()
        {
            if (fight == null) return;
            fight.IntroStarted -= OnIntroStarted;
            fight.IntroEnded -= OnIntroEnded;
            fight.Defeated -= OnDefeated;
        }

        private void OnIntroStarted(Enemy boss)
        {
            var data = fight.Data;
            Show(data != null ? data.DisplayName : boss.name, data != null ? data.Title.ToUpperInvariant() : "", false,
                float.PositiveInfinity);
        }

        private void OnIntroEnded()
        {
            if (IsShown && !IsVictory) BeginFade();
        }

        private void OnDefeated()
        {
            var data = fight.Data;
            string sub = data != null && data.RewardXp > 0 ? $"+{data.RewardXp} SOULS" : "";
            Show(VictoryText, sub, true, victoryHold);
        }

        private void Show(string text, string sub, bool victory, float hold)
        {
            if (_root == null) return;
            if (_text != null) _text.text = text;
            if (_sub != null)
            {
                _sub.text = sub;
                _sub.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            IsVictory = victory;
            if (victory) _root.AddToClassList(VictoryClass);
            else _root.RemoveFromClassList(VictoryClass);
            _root.RemoveFromClassList(HiddenClass);
            _age = 0f;
            _hold = hold;
            _fadeStart = -1f;
            Draw(0f, swell.x);
        }

        private void BeginFade()
        {
            if (_fadeStart < 0f) _fadeStart = Mathf.Max(_age, fadeIn);
        }

        private void LateUpdate() => Step(Time.unscaledDeltaTime);

        /// <summary>Advances the banner by <paramref name="dt"/> real seconds. Public for tests (LateUpdate calls it).</summary>
        public void Step(float dt)
        {
            if (_age < 0f || _root == null) return;
            _age += dt;
            if (_fadeStart < 0f && _age >= fadeIn + _hold) _fadeStart = fadeIn + _hold; // on schedule, even after a long frame

            float alpha = Mathf.Clamp01(_age / fadeIn);
            if (_fadeStart >= 0f)
            {
                float k = (_age - _fadeStart) / fadeOut;
                if (k >= 1f)
                {
                    _age = -1f;
                    Alpha = 0f;
                    _root.AddToClassList(HiddenClass);
                    return;
                }
                alpha *= 1f - k;
            }
            float span = float.IsInfinity(_hold) ? 3f : fadeIn + _hold + fadeOut;
            Draw(alpha, Mathf.Lerp(swell.x, swell.y, Mathf.Clamp01(_age / span)));
        }

        private void Draw(float alpha, float scale)
        {
            Alpha = alpha;
            _root.style.opacity = alpha;
            if (_text != null) _text.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }
    }
}
