using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Plague Bloater (KAN-106, Docs/ENEMY_ROSTER.md 3.5): a slow suicide bomber.
    /// <list type="bullet">
    /// <item>Swell: within <see cref="Settings.triggerRange"/> of the Knight it stops, plays its Burst clip and swells for
    /// <see cref="Settings.swell"/> s while a green circle (<see cref="Settings.radius"/>) fills round it.</item>
    /// <item>Burst: the Knight inside the circle loses <see cref="Settings.damageShare"/> of its max health; every other
    /// zombie inside (not bosses) loses <see cref="Settings.enemyShare"/> of its own max health (a crowd tool: chains into
    /// other Bloaters); a toxic cloud (<see cref="Settings.cloud"/>, <see cref="GroundHazards"/>) stays. The Bloater dies by
    /// its own burst (<see cref="Enemy.Expire"/>: no soul wisp, no kill).</item>
    /// <item>A kill by the Knight (bolt, burn, another burst) bursts it too: the circle shows round the corpse and the burst
    /// comes <see cref="Settings.deathFuse"/> s after the warning first showed (a kill mid-swell keeps the time already shown).
    /// With no free warning the corpse still bursts on the zombies and leaves the cloud, but never hits the Knight unwarned.</item>
    /// </list>
    /// </summary>
    public sealed class BloaterBurst : SpecialAttack
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Distance (m) to the Knight at which the swell starts.")]
            [Min(0.5f)] public float triggerRange;
            [Tooltip("Seconds of swell (warning) before the burst.")]
            [Min(MinTelegraph)] public float swell;
            [Tooltip("Burst radius (m) = the warning circle.")]
            [Min(0.5f)] public float radius;
            [Tooltip("Damage to the Knight inside the circle, share of its max health.")]
            [Range(0f, 1f)] public float damageShare;
            [Tooltip("Damage to other zombies inside the circle, share of their own max health (bosses are safe).")]
            [Range(0f, 1f)] public float enemyShare;
            [Tooltip("Killed by the Knight: seconds from the first warning to the burst.")]
            [Min(MinTelegraph)] public float deathFuse;
            [Tooltip("Body scale at the end of the swell (1 = no swell).")]
            [Min(1f)] public float swellScale;
            [Tooltip("Toxic cloud left on the ground (radius 0 = none).")]
            public HazardSpec cloud;
            [Tooltip("Seconds before the first swell can start after a spawn (a fresh Bloater at the Knight's feet waits).")]
            [Min(0f)] public float spawnCooldown;
        }

        /// <summary>Warning ring / fill / flash: toxic green, so the burst reads apart from the red shots and slams.</summary>
        public static readonly Color RingTint = new Color(0.36f, 0.62f, 0.05f, 1f);
        public static readonly Color FillTint = new Color(0.3f, 0.5f, 0.04f, 0.72f);
        public static readonly Color FlashTint = new Color(0.78f, 1f, 0.35f, 0.85f);

        [SerializeField] private Settings settings;

        private GroundTelegraph _circle;
        private bool _burst;
        private bool _warned;    // the circle showed: the burst may hit the Knight
        private float _fuse = -1f; // seconds the warning has shown since the kill (-1 = not killed yet)
        private float _scale = 1f;

        /// <summary>A Bloater burst: where, Knight damage dealt, killed by the Knight (false = its own swell).</summary>
        public static event Action<BloaterBurst, Vector3, float, bool> Burst;

        public Settings Config => settings;
        /// <summary>Burst already (alive swell or corpse).</summary>
        public bool HasBurst => _burst;
        /// <summary>The warning circle of the swell / death fuse (null when none).</summary>
        public GroundTelegraph Circle => _circle;
        protected override float SpawnCooldown => settings.spawnCooldown;

        /// <summary>Tests and tools: sets the attack without the builder.</summary>
        public void Configure(in Settings value) => settings = value;

        protected override void OnEnable()
        {
            base.OnEnable();
            _burst = false;
            _warned = false;
            _fuse = -1f;
        }

        protected override bool WantsToStart(PlayerHealth knight) =>
            Planar(knight.transform.position, transform.position) <= settings.triggerRange;

        protected override bool Begin(PlayerHealth knight)
        {
            if (!ShowCircle()) return false;
            _scale = Enemy.Scale;
            Enemy.PlayAction("Burst");
            return true;
        }

        private bool ShowCircle()
        {
            var pool = EnemyTelegraphs.Current;
            _circle = pool != null ? pool.RentCircle() : null;
            if (_circle == null) return false;
            _circle.Show(transform.position, settings.radius);
            _circle.Tint(RingTint, FillTint, FlashTint);
            _warned = true;
            return true;
        }

        protected override void Step(PlayerHealth knight, float dt)
        {
            float p = settings.swell > 0f ? Mathf.Clamp01(_time / settings.swell) : 1f;
            if (_circle != null) _circle.SetProgress(p);
            float s = _scale * Mathf.Lerp(1f, settings.swellScale, p * p);
            transform.localScale = new Vector3(s, s, s);
            if (_time >= settings.swell) Explode(byKill: false);
        }

        /// <summary>Killed: the corpse bursts after the fuse (warning first). Already burst: nothing.</summary>
        protected override void OnDeadTick(PlayerHealth knight, float dt)
        {
            if (_burst) return;
            if (_fuse < 0f)
            {
                _fuse = _phase == Phase.WindUp ? _time : 0f; // a kill mid-swell keeps the warning time already shown
                if (_circle == null) ShowCircle();
                _phase = Phase.Strike;
            }
            _fuse += dt;
            if (_circle != null) _circle.SetProgress(Mathf.Clamp01(_fuse / Mathf.Max(0.01f, settings.deathFuse)));
            if (_fuse >= settings.deathFuse) Explode(byKill: true);
        }

        /// <summary>
        /// The burst: Knight (only when warned), other zombies, cloud, flash. The swell ends in the Bloater's own death
        /// (<see cref="Enemy.Expire"/>).
        /// </summary>
        private void Explode(bool byKill)
        {
            _burst = true;
            Vector3 centre = transform.position;
            if (_circle != null) _circle.Flash();
            _circle = ReleaseCircle(_circle, hide: false); // the flash fades out on its own

            var knight = Knight();
            float dealt = 0f;
            if (_warned && knight != null && !knight.IsDead && Planar(knight.transform.position, centre) <= settings.radius)
                dealt = Hurt(knight, settings.damageShare);

            if (settings.enemyShare > 0f)
            {
                var self = Enemy;
                var active = Enemy.Active;
                for (int i = active.Count - 1; i >= 0; i--) // a kill removes that enemy from the list: walk it backwards
                {
                    if (i >= active.Count) continue;
                    var e = active[i];
                    if (e == null || e == self || e.IsBoss || !e.IsAlive) continue;
                    if (Planar(e.transform.position, centre) > settings.radius + e.HitRadius) continue;
                    Vector3 away = e.transform.position - centre;
                    e.TakeDamage(settings.enemyShare * e.MaxHealth, away, DamageKind.Burst);
                }
            }

            var hazards = GroundHazards.Current;
            if (hazards != null && settings.cloud.IsValid) hazards.Spawn(centre, settings.cloud);
            RaiseStruck();
            Burst?.Invoke(this, centre, dealt, byKill);
            if (!byKill) Enemy.Expire();
            _phase = Phase.Recover;
        }

        public override void Cancel()
        {
            _circle = ReleaseCircle(_circle, hide: true);
            if (_phase == Phase.WindUp && Enemy.IsAlive) transform.localScale = new Vector3(_scale, _scale, _scale);
            base.Cancel();
        }
    }
}
