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
    /// <c>-ss-hp-fx &lt;event&gt;@&lt;lead&gt;[,...]</c> runs health events that many seconds before the capture
    /// (<c>-ss-delay</c>, realtime after the Game scene loads), so a shot catches the effect mid-flight. Events:
    /// <c>hit:&lt;share&gt;</c> deals that share of max health (works with -ss-immortal), <c>heal:&lt;share&gt;</c> heals it,
    /// <c>max:&lt;amount&gt;</c> raises max health by a flat amount (a stat source, like Vigor), <c>shield:&lt;share&gt;</c> sets the
    /// shield to that share of max health, <c>hits:&lt;share&gt;</c> deals that share split over 1 s of contact pulses (horde).
    /// Example: <c>-ss-hp-fx hit:0.18@0.12</c> (chunk + number + trail). Dev builds only; no effect without the arg.
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
                float lead = at.Length > 1 ? float.Parse(at[1], CultureInfo.InvariantCulture) : 0.5f;
                StartCoroutine(Run(kv[0], value, Mathf.Max(0f, _delay - lead)));
            }
        }

        private IEnumerator Run(string kind, float value, float wait)
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
        }

        private static void Hit(PlayerHealth health, float amount)
        {
            bool invulnerable = health.Invulnerable; // -ss-immortal: let the scripted hit through, nothing else
            health.Invulnerable = false;
            health.TakeDamage(amount);
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
