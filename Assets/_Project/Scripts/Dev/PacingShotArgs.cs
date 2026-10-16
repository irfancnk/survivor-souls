using System.Globalization;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build pacing measure (KAN-113), next to <see cref="DevScreenshot"/>: <c>-ss-pacing &lt;log every s&gt;[,&lt;card&gt;]</c>
    /// picks a level-up card by itself (index <c>card</c>, default a seeded random one; the slot-machine roll is skipped)
    /// and logs one <c>[Pacing]</c> line every <c>log every</c> game seconds and when the Chamber clock runs out: game time,
    /// Hunter level, total XP, kills, alive / max alive, power index, spawn pressure factors and the zombie health factor.
    /// Use with <c>-ss-immortal</c> to measure a whole 2 minute Chamber, e.g.
    /// <c>-ss-scene Game -ss-immortal -ss-pacing 10 -ss-delay 122 -ss-screenshot shot.png</c>. Compare the log with the
    /// <see cref="ChamberPacing"/> model (balance report). Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class PacingShotArgs : MonoBehaviour
    {
        private float _every = 10f;
        private int _card = -1;
        private float _next;
        private bool _clockDone;
        private System.Random _rng = new System.Random(113);
        private SkillChoiceScreen _screen;
        private XpManager _xp;
        private RunCounters _counters;
        private EnemySpawner _spawner;
        private HordeScaler _scaler;
        private SurvivalTimer _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-pacing");
            if (string.IsNullOrEmpty(spec)) return;
            var go = new GameObject(nameof(PacingShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<PacingShotArgs>();
            string[] parts = spec.Split(',');
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float every)) args._every = Mathf.Max(1f, every);
            if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int card)) args._card = card;
            SceneManager.sceneLoaded += args.OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _screen = FindAnyObjectByType<SkillChoiceScreen>();
            _xp = FindAnyObjectByType<XpManager>();
            _counters = FindAnyObjectByType<RunCounters>();
            _spawner = FindAnyObjectByType<EnemySpawner>();
            _scaler = FindAnyObjectByType<HordeScaler>();
            _timer = FindAnyObjectByType<SurvivalTimer>();
            _next = _every;
            _clockDone = false;
            Debug.Log($"[Pacing] scene {scene.name}: every {_every:0} s, card {(_card < 0 ? "random" : _card.ToString(CultureInfo.InvariantCulture))}");
        }

        private void Update()
        {
            if (_screen != null && _screen.IsShowing)
            {
                if (_screen.IsRolling) _screen.SkipRoll();
                if (_screen.IsArmed && _screen.Options.Count > 0)
                {
                    int index = _card >= 0 ? Mathf.Min(_card, _screen.Options.Count - 1) : _rng.Next(_screen.Options.Count);
                    var skill = _screen.Options[index];
                    if (_screen.Choose(index)) Debug.Log($"[Pacing] pick L{(_xp != null ? _xp.Level : 0)}: {skill?.Id}");
                }
            }
            if (_spawner == null) return;
            float t = Time.timeSinceLevelLoad;
            bool clockOut = !_clockDone && _timer != null && _timer.UsesTimer && _timer.Remaining <= 0f;
            if (t < _next && !clockOut) return;
            if (clockOut) _clockDone = true;
            _next = t + _every;
            int phase = _spawner.Waves != null ? _spawner.Waves.PhaseAt(_spawner.RunTime) : -1;
            int maxAlive = phase >= 0 ? _spawner.MaxAliveOf(_spawner.Waves.GetPhase(phase)) : 0;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[Pacing] t={0:0.0}{1} level={2} totalXp={3} kills={4} alive={5}/{6} spawned={7} power={8:0.00} cap={9:0.00} interval={10:0.00} health={11:0.00}",
                t, clockOut ? " (clock out)" : "", _xp != null ? _xp.Level : 0, _xp != null ? _xp.TotalXp : 0,
                _counters != null ? _counters.Kills : 0, _spawner.InUse, maxAlive, _spawner.Stats.Spawned,
                _scaler != null ? _scaler.Power : 1f, _spawner.PressureCapFactor, _spawner.PressureIntervalFactor,
                _spawner.SpawnHealthMultiplier));
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
