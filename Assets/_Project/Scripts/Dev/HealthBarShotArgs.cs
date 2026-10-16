using System;
using System.Collections;
using System.Globalization;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build screenshot helper for the medieval health bar (KAN-82), next to <see cref="DevScreenshot"/>:
    /// <c>-ss-hp-fx &lt;event&gt;@&lt;lead&gt;[~&lt;freeze&gt;][,...]</c> runs health events about that many seconds before the
    /// capture (<c>-ss-delay</c>, realtime after the Game scene loads; the capture itself drifts by ~1 s); with a freeze,
    /// time stops that many seconds after the event, so the shot catches the effect mid-flight (the bar runs on scaled
    /// time). Events:
    /// <c>hit:&lt;share&gt;</c> deals that share of max health (works with -ss-immortal), <c>heal:&lt;share&gt;</c> heals it,
    /// <c>max:&lt;amount&gt;</c> raises max health by a flat amount (a stat source, like Vigor), <c>shield:&lt;share&gt;</c> sets the
    /// shield to that share of max health, <c>hits:&lt;share&gt;</c> deals that share split over 1 s of contact pulses (horde),
    /// <c>blow:&lt;share&gt;:&lt;angle&gt;</c> deals that share as a discrete hit from 6 m away at that screen angle (degrees,
    /// 0 = from the top, 90 = from the right; KAN-187 arc, flash, shake, hit-stop, grunt).
    /// Example: <c>-ss-hp-fx hit:0.18@2~0.1</c> (chunk + number + trail, frozen 0.1 s after the hit). Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class HealthBarShotArgs : MonoBehaviour
    {
        private string _spec;
        private float _delay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-hp-fx");
            if (string.IsNullOrEmpty(spec) || string.IsNullOrEmpty(GetArg("-ss-screenshot"))) return;
            var go = new GameObject(nameof(HealthBarShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<HealthBarShotArgs>();
            args._spec = spec;
            args._delay = float.TryParse(GetArg("-ss-delay"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 3f;
            SceneManager.sceneLoaded += args.OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<PlayerHealth>() == null) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            foreach (string item in _spec.Split(','))
            {
                string[] at = item.Split('@');
                string[] kv = at[0].Split(':');
                if (kv.Length < 2) continue;
                float value = float.Parse(kv[1], CultureInfo.InvariantCulture);
                string[] lf = (at.Length > 1 ? at[1] : "0.5").Split('~');
                float lead = float.Parse(lf[0], CultureInfo.InvariantCulture);
                float freeze = lf.Length > 1 ? float.Parse(lf[1], CultureInfo.InvariantCulture) : -1f;
                float angle = kv.Length > 2 ? float.Parse(kv[2], CultureInfo.InvariantCulture) : 0f;
                StartCoroutine(Run(kv[0], value, Mathf.Max(0f, _delay - lead), freeze, angle));
            }
        }

        private IEnumerator Run(string kind, float value, float wait, float freeze, float angle)
        {
            yield return new WaitForSecondsRealtime(wait);
            var health = FindAnyObjectByType<PlayerHealth>();
            if (health == null) yield break;
            switch (kind)
            {
                case "hit":
                    Hit(health, health.Max * value);
                    break;
                case "hits":
                    for (int i = 0; i < 30; i++)
                    {
                        Hit(health, health.Max * value / 30f);
                        yield return new WaitForSeconds(1f / 30f);
                    }
                    break;
                case "blow": // KAN-187: discrete hit from a side (world +Z = screen up, +X = right)
                    Vector3 from = health.transform.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 6f;
                    Hit(health, health.Max * value, from);
                    break;
                case "heal":
                    health.Heal(health.Max * value);
                    break;
                case "shield":
                    health.SetShield(health.Max * value);
                    break;
                case "max":
                    var stats = health.GetComponent<PlayerStats>();
                    if (stats != null)
                        stats.SetSource(this, new[] { new StatModifier(StatId.MaxHealth, ModifierKind.Flat, value) }, 1);
                    break;
                default:
                    Debug.LogWarning($"[HealthBarShotArgs] unknown event '{kind}'");
                    break;
            }
            Debug.Log($"[HealthBarShotArgs] {kind} {value} -> {health.Current:0}/{health.Max:0} shield {health.Shield:0}");
            if (freeze < 0f) yield break;
            yield return new WaitForSecondsRealtime(freeze);
            Time.timeScale = 0f; // dev shot only: the frame holds the effect for the capture
            Debug.Log($"[HealthBarShotArgs] frozen {freeze:0.##} s after {kind}");
        }

        private static void Hit(PlayerHealth health, float amount, Vector3? from = null)
        {
            bool invulnerable = health.Invulnerable; // -ss-immortal: let the scripted hit through, nothing else
            health.Invulnerable = false;
            if (from.HasValue) health.TakeDamage(amount, from.Value);
            else health.TakeDamage(amount);
            health.Invulnerable = invulnerable;
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
