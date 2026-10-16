using System.Globalization;
using SurvivorSouls.Gameplay.Boss;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build helper for the boss aggression model (KAN-164.2), next to <see cref="DevScreenshot"/>:
    /// <c>-ss-boss-enrage &lt;freeze after s&gt;</c> logs the enrage (<c>[BossPhaseShotArgs] enraged ...</c>), every combo
    /// partner start and, with a value above 0, freezes time (<c>Time.timeScale</c> 0) that many unscaled seconds after the
    /// enrage, so the aura is on screen for the shot. Put the boss under its enrage share with the usual args, e.g.
    /// <c>-ss-chamber 5 -ss-boss 0.2 -ss-boss-enrage 0.8</c>. Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class BossPhaseShotArgs : MonoBehaviour
    {
        private float _freezeAfter;
        private float _freezeAt = -1f;
        private BossFight _fight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-boss-enrage");
            if (string.IsNullOrEmpty(spec)) return;
            var go = new GameObject(nameof(BossPhaseShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<BossPhaseShotArgs>();
            args._freezeAfter = float.TryParse(spec, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
            SceneManager.sceneLoaded += args.OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var fight = FindAnyObjectByType<BossFight>();
            if (fight == null) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _fight = fight;
            fight.Enraged += OnEnraged;
            fight.ComboStarted += OnCombo;
            Debug.Log($"[BossPhaseShotArgs] armed, freeze {_freezeAfter:0.00} s after the enrage");
        }

        private void OnEnraged()
        {
            var body = _fight.Boss;
            Debug.Log($"[BossPhaseShotArgs] enraged {_fight.Data.DisplayName} hp={(body != null ? body.Health : 0f):F0}/"
                      + $"{(body != null ? body.MaxHealth : 0f):F0} phase={_fight.Phase} alone={_fight.Alone}");
            if (_freezeAfter > 0f) _freezeAt = Time.unscaledTime + _freezeAfter;
        }

        private void OnCombo(int opener, int partner)
        {
            var list = _fight.Attacks;
            if (opener < 0 || opener >= list.Length || partner < 0 || partner >= list.Length) return;
            Debug.Log($"[BossPhaseShotArgs] combo {list[opener].label} -> {list[partner].label} (enraged={_fight.IsEnraged}, "
                      + $"combos={_fight.CombosStarted})");
        }

        private void Update()
        {
            if (_freezeAt < 0f || Time.unscaledTime < _freezeAt) return;
            _freezeAt = -1f;
            Time.timeScale = 0f;
            Debug.Log("[BossPhaseShotArgs] froze time after the enrage");
        }

        private static string GetArg(string name)
        {
            var args = DevArgs.All; // KAN-179: command line or SS_ARGS (iOS)
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
