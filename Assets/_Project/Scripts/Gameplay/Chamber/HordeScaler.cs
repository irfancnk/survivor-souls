using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Live horde scaling (KAN-92). Measures the Knight's power index from <see cref="PlayerStats"/> when the stats change
    /// (pick, chest upgrade, Chamber restore; no work per frame), reads the Chamber clock progress from the
    /// <see cref="SurvivalTimer"/>, and gives the spawner the dynamic health factor of the <see cref="HordeScaling"/>
    /// asset every frame (two floats, no allocation), plus the spawn pressure factors (KAN-113: max alive / burst size up,
    /// intervals down with the power index). Zombies already on the field keep the health they spawned with.
    /// Boss Chambers: no clock, progress 0. <see cref="PowerOverride"/> / <see cref="ProgressOverride"/> are debug hooks
    /// (dev screenshots, tests). On the "Chamber" object (ChamberBuilder).
    /// The power index is measured against the Knight with his permanent bonuses (KAN-98 soul tree,
    /// <see cref="PlayerStats.Modify(StatId, float, bool)"/>), not the bare Knight: meta upgrades make the Knight
    /// stronger than the horde; only in-run skills raise zombie health.
    /// </summary>
    public sealed class HordeScaler : MonoBehaviour
    {
        [SerializeField] private HordeScaling scaling;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private PlayerStats stats;
        [SerializeField] private SurvivalTimer timer;

        private PowerSnapshot _baseline;
        private bool _dirty = true;

        /// <summary>Power index now (1 = the Knight at run start: bare + permanent bonuses). Updated when the stats change.</summary>
        public float Power { get; private set; } = 1f;
        /// <summary>Chamber clock progress used now (0 = start, 1 = portal open).</summary>
        public float Progress { get; private set; }
        /// <summary>Dynamic health factor given to the spawner now.</summary>
        public float Multiplier { get; private set; } = 1f;
        /// <summary>Spawn pressure (KAN-113): max alive and burst size factor given to the spawner now.</summary>
        public float CapFactor { get; private set; } = 1f;
        /// <summary>Spawn pressure (KAN-113): group and burst interval factor given to the spawner now.</summary>
        public float IntervalFactor { get; private set; } = 1f;
        /// <summary>Debug: power index to use instead of the measured one (0 = off).</summary>
        public float PowerOverride { get; set; }
        /// <summary>Debug: clock progress to use instead of the timer (below 0 = off).</summary>
        public float ProgressOverride { get; set; } = -1f;
        public HordeScaling Scaling => scaling;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(HordeScaling rules, EnemySpawner enemySpawner, PlayerStats playerStats, SurvivalTimer survivalTimer)
        {
            if (isActiveAndEnabled && stats != null) stats.Changed -= MarkDirty;
            scaling = rules;
            spawner = enemySpawner;
            stats = playerStats;
            timer = survivalTimer;
            if (isActiveAndEnabled && stats != null) stats.Changed += MarkDirty;
            _dirty = true;
            Refresh();
        }

        private void OnEnable()
        {
            if (stats != null) stats.Changed += MarkDirty;
            _dirty = true;
            Refresh();
        }

        private void OnDisable()
        {
            if (stats != null) stats.Changed -= MarkDirty;
        }

        private void Update() => Refresh();

        private void MarkDirty() => _dirty = true;

        /// <summary>Measures (when the stats changed) and pushes the factor to the spawner. Public for tests (Update calls it).</summary>
        public void Refresh()
        {
            if (_dirty)
            {
                _dirty = false;
                _baseline = PowerSnapshot.Measure(stats, PowerBase.Default, permanentOnly: true);
                var now = PowerSnapshot.Measure(stats, PowerBase.Default);
                Power = scaling != null ? scaling.PowerOf(now, _baseline) : HordeScaling.PowerIndex(now, _baseline);
            }
            Progress = ProgressOverride >= 0f ? Mathf.Clamp01(ProgressOverride) : ClockProgress();
            float power = PowerOverride > 0f ? PowerOverride : Power;
            Multiplier = scaling != null ? scaling.DynamicMultiplier(power, Progress) : 1f;
            CapFactor = scaling != null ? scaling.CapFactor(power) : 1f;
            IntervalFactor = scaling != null ? scaling.IntervalFactor(power) : 1f;
            if (spawner == null) return;
            spawner.SetHordeScale(scaling, Multiplier);
            spawner.SetHordePressure(CapFactor, IntervalFactor);
        }

        private float ClockProgress()
        {
            if (timer == null || !timer.UsesTimer || timer.Duration <= 0f) return 0f;
            return Mathf.Clamp01(1f - timer.Remaining / timer.Duration);
        }
    }
}
