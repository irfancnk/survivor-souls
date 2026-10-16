using System.Collections;
using System.Globalization;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build screenshot helper for the special melee effects (KAN-132), next to <see cref="DevScreenshot"/>:
    /// <c>-ss-melee-freeze &lt;event&gt;[,&lt;seconds&gt;]</c> stops time that many scaled seconds (default 0.1) after the first
    /// such event, so the capture (<c>-ss-delay</c>, realtime) shows the effect mid-flight. Events: <c>windup</c> (any
    /// special attack starts its wind-up), <c>burst</c> (a Plague Bloater bursts), <c>leap</c> (a Ghoul takes off),
    /// <c>land</c> (a Ghoul lands), <c>slam</c> (a Grave Brute slams). Example with
    /// <c>-ss-enemy GraveBrute,1</c>: <c>-ss-melee-freeze slam,0.3</c> (ring half way out, dust on the front, crack fresh).
    /// Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class SpecialMeleeShotArgs : MonoBehaviour
    {
        private string _event;
        private float _after;
        private bool _armed = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-melee-freeze");
            if (string.IsNullOrEmpty(spec)) return;
            string[] parts = spec.Split(',');
            var go = new GameObject(nameof(SpecialMeleeShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<SpecialMeleeShotArgs>();
            args._event = parts[0];
            args._after = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 0.1f;
        }

        private void OnEnable()
        {
            SpecialAttack.WindUpStarted += OnWindUp;
            SpecialAttack.Struck += OnStruck;
            BloaterBurst.Burst += OnBurst;
            GhoulLeap.Landed += OnLanded;
        }

        private void OnDisable()
        {
            SpecialAttack.WindUpStarted -= OnWindUp;
            SpecialAttack.Struck -= OnStruck;
            BloaterBurst.Burst -= OnBurst;
            GhoulLeap.Landed -= OnLanded;
        }

        private void OnWindUp(SpecialAttack attack) => Fire("windup", attack.name);
        private void OnStruck(SpecialAttack attack)
        {
            if (attack is GhoulLeap) Fire("leap", attack.name);
            else if (attack is BruteSlam) Fire("slam", attack.name);
        }
        private void OnBurst(BloaterBurst bloater, Vector3 centre, float dealt, bool byKill) => Fire("burst", bloater.name);
        private void OnLanded(GhoulLeap ghoul, Vector3 at, bool hit) => Fire("land", ghoul.name);

        private void Fire(string ev, string who)
        {
            if (!_armed || ev != _event) return;
            _armed = false;
            Debug.Log($"[SpecialMeleeShotArgs] {ev} by {who} at t={Time.time:F2}: freezing in {_after:F2} s");
            StartCoroutine(Freeze());
        }

        private IEnumerator Freeze()
        {
            if (_after > 0f) yield return new WaitForSeconds(_after);
            Time.timeScale = 0f;
            Debug.Log($"[SpecialMeleeShotArgs] frozen at t={Time.time:F2}");
        }

        private static string GetArg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
