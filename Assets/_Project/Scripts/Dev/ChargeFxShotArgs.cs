using System;
using System.Globalization;
using SurvivorSouls.Gameplay.Boss;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build screenshot helper for the boss charge visuals (KAN-80), next to <see cref="DevScreenshot"/>:
    /// <c>-ss-charge-freeze &lt;phase&gt;[,&lt;seconds&gt;]</c> stops time that many scaled seconds (default 0.2) after
    /// <see cref="BossChargeFx"/> first enters the phase (<c>windup</c>, <c>dash</c>, <c>punish</c>), so the capture
    /// (<c>-ss-delay</c>, realtime) shows the lean, the trail or the dizzy embers. Use with
    /// <c>-ss-boss 0.4 -ss-boss-attack charge</c>. Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class ChargeFxShotArgs : MonoBehaviour
    {
        private BossChargeFx.Phase _phase;
        private float _after;
        private float _left = -1f;
        private bool _done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-charge-freeze");
            if (string.IsNullOrEmpty(spec)) return;
            string[] parts = spec.Split(',');
            BossChargeFx.Phase phase;
            switch (parts[0])
            {
                case "windup": phase = BossChargeFx.Phase.WindUp; break;
                case "dash": phase = BossChargeFx.Phase.Dash; break;
                case "punish": phase = BossChargeFx.Phase.Punish; break;
                default:
                    Debug.LogWarning($"[ChargeFxShotArgs] unknown phase '{parts[0]}' (windup | dash | punish)");
                    return;
            }
            var go = new GameObject(nameof(ChargeFxShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<ChargeFxShotArgs>();
            args._phase = phase;
            args._after = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 0.2f;
        }

        private void Update()
        {
            if (_done) return;
            var fx = BossChargeFx.Active;
            if (_left < 0f)
            {
                if (fx == null || fx.Current != _phase) return;
                _left = _after;
            }
            _left -= Time.deltaTime;
            if (_left > 0f) return;
            Time.timeScale = 0f;
            _done = true;
            Debug.Log($"[ChargeFxShotArgs] frozen in {_phase}: lean {(fx != null ? fx.Lean : 0f):F1} deg, trail {(fx != null ? fx.TrailEmitted : 0)}, " +
                $"scrape {(fx != null ? fx.ScrapeEmitted : 0)}, skid {(fx != null ? fx.SkidEmitted : 0)}, embers {(fx != null ? fx.EmbersEmitted : 0)}, t={Time.time:F2}");
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
