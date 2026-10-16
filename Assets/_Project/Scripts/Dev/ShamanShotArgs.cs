using System.Collections;
using System.Globalization;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build screenshot helper for the Hexbound Shaman feel (KAN-138), next to <see cref="DevScreenshot"/>:
    /// <c>-ss-shaman-freeze &lt;event&gt;[,&lt;seconds&gt;]</c> stops time that many scaled seconds (default 0.6) after the first
    /// such event, so the capture (<c>-ss-delay</c>, realtime) shows it. Events: <c>windup</c> (a cast starts), <c>orbs</c>
    /// (a ring leaves), <c>hex</c> (a hex lands: the embers rise off the hexed zombies), <c>raise</c>, <c>blink</c> (a blink
    /// ends). Example with <c>-ss-enemy HexboundShaman+GraveyardLaborer,1 -ss-shaman-cast hex</c>: <c>-ss-shaman-freeze hex,0.6</c>.
    /// Dev builds only; no effect without the arg. At quit it logs the Shaman casts it saw.
    /// </summary>
    public sealed class ShamanShotArgs : MonoBehaviour
    {
        private string _event;
        private float _after;
        private bool _armed = true;
        private int _windUps, _rings, _hexes, _raises, _blinks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-shaman-freeze");
            if (string.IsNullOrEmpty(spec)) return;
            string[] parts = spec.Split(',');
            var go = new GameObject(nameof(ShamanShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<ShamanShotArgs>();
            args._event = parts[0];
            args._after = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? s : 0.6f;
        }

        private void OnEnable()
        {
            HexShaman.CastStarted += OnCast;
            HexShaman.OrbsFired += OnOrbs;
            HexShaman.Hexed += OnHexed;
            HexShaman.Raised += OnRaised;
            HexShaman.Blinked += OnBlinked;
        }

        private void OnDisable()
        {
            HexShaman.CastStarted -= OnCast;
            HexShaman.OrbsFired -= OnOrbs;
            HexShaman.Hexed -= OnHexed;
            HexShaman.Raised -= OnRaised;
            HexShaman.Blinked -= OnBlinked;
        }

        private void OnDestroy() =>
            Debug.Log($"[ShamanShotArgs] wind-ups {_windUps}, rings {_rings}, hexes {_hexes}, raises {_raises}, blinks {_blinks}");

        private void OnCast(HexShaman s, HexShaman.Cast cast) { _windUps++; Fire("windup", s, cast.ToString()); }
        private void OnOrbs(HexShaman s, int n) { _rings++; Fire("orbs", s, n + " orbs"); }
        private void OnHexed(HexShaman s, int n) { _hexes++; Fire("hex", s, n + " hexed"); }
        private void OnRaised(HexShaman s, int n) { _raises++; Fire("raise", s, n + " raised"); }
        private void OnBlinked(HexShaman s, Vector3 from, Vector3 to) { _blinks++; Fire("blink", s, $"{Vector3.Distance(from, to):F1} m"); }

        private void Fire(string ev, HexShaman who, string what)
        {
            if (!_armed || ev != _event) return;
            _armed = false;
            Debug.Log($"[ShamanShotArgs] {ev} by {who.name} ({what}) at t={Time.time:F2}: freezing in {_after:F2} s");
            StartCoroutine(Freeze());
        }

        private IEnumerator Freeze()
        {
            if (_after > 0f) yield return new WaitForSeconds(_after);
            Time.timeScale = 0f;
            int hexed = 0;
            foreach (var e in Enemy.Active)
                if (e != null && e.IsHexed) hexed++;
            Debug.Log($"[ShamanShotArgs] frozen at t={Time.time:F2}: {hexed} hexed, embers so far {(HexFx.Current != null ? HexFx.Current.Motes : 0)}");
        }

        private static string GetArg(string name)
        {
            string[] args = DevArgs.All;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
