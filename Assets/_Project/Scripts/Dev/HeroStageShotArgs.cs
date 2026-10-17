using System;
using System.Globalization;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build screenshot helper for the 3D hero stage (KAN-244), next to <see cref="DevScreenshot"/> (use with
    /// <c>-ss-heroes</c>, which opens the hero page):
    /// <list type="bullet">
    /// <item><c>-ss-hero-view &lt;id&gt;</c>: shows that hero once the page is open (no slide).</item>
    /// <item><c>-ss-hero-yaw &lt;deg&gt;</c>: holds the hero turned by that angle (as if dragged).</item>
    /// <item><c>-ss-hero-motion taunt|look|drill[,&lt;seconds&gt;]</c>: plays that motion 0.6 s after the page opened and
    /// freezes time (<c>Time.timeScale</c> 0) that many seconds into it (no seconds = no freeze).</item>
    /// </list>
    /// Example: <c>-ss-scene MainMenu -ss-heroes -ss-hero-view shadow_ranger -ss-hero-motion taunt,0.8 -ss-delay 3</c>.
    /// Dev builds only; no effect without the args.
    /// </summary>
    public sealed class HeroStageShotArgs : MonoBehaviour
    {
        private string _view;
        private float _yaw = float.NaN;
        private HeroStageMotion _motion = HeroStageMotion.Idle;
        private float _freezeAt = -1f;
        private float _openFor;
        private bool _viewed, _played, _frozen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string view = GetArg("-ss-hero-view"), yaw = GetArg("-ss-hero-yaw"), motion = GetArg("-ss-hero-motion");
            if (view == null && yaw == null && motion == null) return;
            var go = new GameObject(nameof(HeroStageShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<HeroStageShotArgs>();
            args._view = view;
            if (yaw != null && float.TryParse(yaw, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) args._yaw = y;
            if (motion != null)
            {
                string[] parts = motion.Split(',');
                args._motion = parts[0] switch
                {
                    "taunt" => HeroStageMotion.Taunt,
                    "look" => HeroStageMotion.LookAround,
                    "drill" => HeroStageMotion.Drill,
                    _ => HeroStageMotion.Idle,
                };
                if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) args._freezeAt = Mathf.Max(0f, f);
            }
        }

        private void Update()
        {
            var menu = FindAnyObjectByType<MainMenuController>();
            var page = menu != null ? menu.HeroPage : null;
            if (page == null || !page.IsOpen) return;
            _openFor += Time.unscaledDeltaTime;
            var stage = page.Stage;
            if (!_viewed)
            {
                _viewed = true;
                if (!string.IsNullOrEmpty(_view) && !page.View(_view)) Debug.LogWarning($"[HeroStageShotArgs] -ss-hero-view: no hero '{_view}'");
            }
            if (stage == null) return;
            if (!float.IsNaN(_yaw) && !_frozen) stage.Spin.Snap(_yaw);
            if (!_played && _motion != HeroStageMotion.Idle && _openFor >= 0.6f && stage.ShownLife != null)
            {
                _played = true;
                stage.ShownLife.Play(_motion);
            }
            var life = stage.ShownLife;
            if (_played && !_frozen && _freezeAt >= 0f && life != null && life.Motion == _motion && life.MotionTime >= _freezeAt)
            {
                _frozen = true;
                Time.timeScale = 0f;
                Debug.Log($"[HeroStageShotArgs] froze {life.Motion} at {life.MotionTime:0.00}s on '{stage.Shown?.Id}' yaw={stage.Spin.Yaw:0} " +
                          $"bodyYaw={life.BodyYaw:0} flare={life.Flare:0.00}");
            }
        }

        private static string GetArg(string name)
        {
            var args = DevArgs.All; // command line or SS_ARGS (iOS)
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
