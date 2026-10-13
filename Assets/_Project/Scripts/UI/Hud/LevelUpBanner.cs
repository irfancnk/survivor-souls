using System;
using SurvivorSouls.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Placeholder level-up screen until the KAN-19 skill cards exist: a dark band with "LEVEL N" fades in over the paused
    /// game, holds, fades out, then hands control back to <see cref="LevelUpFlow"/>. No input needed (a tap here would also
    /// start the joystick). Unscaled time. Replace by pointing LevelUpFlow.choice at the skill choice screen.
    /// Looks: levelup* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LevelUpBanner : LevelUpChoice
    {
        private const string ScreenName = "levelup-screen";
        private const string TitleName = "levelup-title";
        private const string HiddenClass = "hud-hidden";

        [SerializeField, Min(0f)] private float fadeIn = 0.18f;
        [SerializeField, Min(0f)] private float hold = 1.0f;
        [SerializeField, Min(0f)] private float fadeOut = 0.25f;

        private VisualElement _screen;
        private Label _title;
        private Action _onDone;
        private float _age = -1f;

        public bool IsShowing => _age >= 0f;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root.Q(ScreenName);
            _title = root.Q<Label>(TitleName);
        }

        public override void Present(int level, Action onDone)
        {
            _onDone = onDone;
            _age = 0f;
            if (_title != null) _title.text = $"LEVEL {level}";
            if (_screen == null) return;
            _screen.style.opacity = 0f;
            _screen.RemoveFromClassList(HiddenClass);
        }

        private void Update()
        {
            if (_age < 0f) return;
            _age += Time.unscaledDeltaTime;
            float total = fadeIn + hold + fadeOut;
            if (_screen != null)
            {
                float a = fadeIn > 0f ? Mathf.Clamp01(_age / fadeIn) : 1f;
                if (_age > fadeIn + hold) a = fadeOut > 0f ? 1f - Mathf.Clamp01((_age - fadeIn - hold) / fadeOut) : 0f;
                _screen.style.opacity = a;
            }
            if (_age < total) return;
            _age = -1f;
            _screen?.AddToClassList(HiddenClass);
            var done = _onDone;
            _onDone = null;
            done?.Invoke();
        }
    }
}
