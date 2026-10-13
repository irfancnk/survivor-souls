using System;
using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    public enum PortalState
    {
        Locked = 0,
        Opening = 1,
        Open = 2,
    }

    /// <summary>
    /// Exit portal in the gate arch of the exit tile (KAN-39). Small state machine:
    /// <list type="bullet">
    /// <item><b>Locked</b>: sealed dark film with glowing red cracks and a lock rune, a few slow embers; a blocker collider
    /// fills the arch (cannot be entered).</item>
    /// <item><b>Opening</b> (<see cref="ChamberRules.OpenSequence"/> s, scaled time, so it waits during a level-up pause):
    /// runes light one by one, cracks spread, the film bursts into a swirling portal (burst particles).</item>
    /// <item><b>Open</b>: swirling portal, light beam, ground glow, motes pulled in, ambient hum. Blocker off. A player that
    /// reaches the trigger point raises <see cref="PlayerEntered"/> once (KAN-40 loads the next Chamber).</item>
    /// </list>
    /// Opens when <see cref="SurvivalTimer.Active"/> finishes. The exit tile is pooled by the world streamer: a portal that
    /// is enabled after the timer finished is Open at once (no sequence replay far from the player).
    /// Visuals: one quad with the Portal shader (per-frame values in one MaterialPropertyBlock), two additive glow quads,
    /// small particle systems, no light, no grab pass. No allocation per frame.
    /// </summary>
    public sealed class PortalGate : MonoBehaviour
    {
        private static readonly int OpenId = Shader.PropertyToID("_Open");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        [Header("Parts")]
        [SerializeField] private Renderer surface;
        [SerializeField] private Collider blocker;
        [SerializeField] private Renderer beam;
        [SerializeField] private Renderer groundGlow;
        [Tooltip("Slow red embers while locked.")]
        [SerializeField] private ParticleSystem lockedFx;
        [Tooltip("Motes pulled into the centre while open.")]
        [SerializeField] private ParticleSystem openFx;
        [Tooltip("One-shot burst when the film breaks.")]
        [SerializeField] private ParticleSystem burstFx;
        [SerializeField] private AudioSource hum;

        [Header("Trigger")]
        [Tooltip("Local point (arch centre on the ground) the player must reach.")]
        [SerializeField] private Vector3 triggerPoint;
        [SerializeField, Min(0.1f)] private float triggerRadius = 1f;

        [Header("Feel")]
        [Tooltip("Opening progress at which the film bursts.")]
        [SerializeField, Range(0f, 1f)] private float burstAt = 0.65f;
        [SerializeField, Min(0)] private int burstCount = 48;
        [SerializeField, Range(0f, 1f)] private float humVolume = 0.55f;

        private MaterialPropertyBlock _mpb;
        private float _progress;
        private bool _burst;
        private bool _entered;
        private float _drawnOpen = -1f;

        /// <summary>The player walked into an open portal. Raised once per portal opening.</summary>
        public static event Action<PortalGate> PlayerEntered;

        public event Action<PortalState> StateChanged;

        public PortalState State { get; private set; } = PortalState.Locked;
        /// <summary>0 = sealed, 1 = fully open.</summary>
        public float Progress => _progress;
        /// <summary>Whether the arch blocks walking (locked / opening).</summary>
        public bool Blocks => blocker != null && blocker.enabled;
        public Vector3 TriggerPosition => transform.TransformPoint(triggerPoint);

        /// <summary>Tests and tools: wires the parts without the builder.</summary>
        public void Setup(Renderer surfaceRenderer, Collider blockerCollider, Vector3 localTrigger, float radius)
        {
            surface = surfaceRenderer;
            blocker = blockerCollider;
            triggerPoint = localTrigger;
            triggerRadius = radius;
            Sync();
        }

        private void OnEnable() => Sync();

        /// <summary>Matches the state to the live timer: Open at once if the timer already finished, else Locked.</summary>
        private void Sync()
        {
            var timer = SurvivalTimer.Active;
            _entered = false;
            _burst = false;
            if (timer != null && timer.IsFinished) SetState(PortalState.Open, 1f);
            else SetState(PortalState.Locked, 0f);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One frame of the state machine with <paramref name="dt"/> scaled seconds (public for tests).</summary>
        public void Tick(float dt)
        {
            var timer = SurvivalTimer.Active;
            switch (State)
            {
                case PortalState.Locked:
                    if (timer != null && timer.IsFinished) BeginOpening();
                    break;
                case PortalState.Opening:
                    float length = timer != null ? timer.OpenSequence : 2f;
                    _progress = Mathf.Min(1f, _progress + dt / Mathf.Max(0.01f, length));
                    if (!_burst && _progress >= burstAt)
                    {
                        _burst = true;
                        if (burstFx != null) burstFx.Emit(burstCount);
                        if (lockedFx != null) lockedFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                        if (openFx != null) openFx.Play(true);
                    }
                    if (_progress >= 1f) SetState(PortalState.Open, 1f);
                    break;
                case PortalState.Open:
                    if (!_entered && timer != null && timer.Player != null && Inside(timer.Player.position))
                    {
                        _entered = true;
                        PlayerEntered?.Invoke(this);
                    }
                    break;
            }
            Apply();
        }

        /// <summary>Starts the opening sequence (no-op unless locked).</summary>
        public void BeginOpening()
        {
            if (State != PortalState.Locked) return;
            SetState(PortalState.Opening, 0f);
        }

        private bool Inside(Vector3 position)
        {
            Vector3 d = position - TriggerPosition;
            d.y = 0f;
            return d.sqrMagnitude <= triggerRadius * triggerRadius;
        }

        private void SetState(PortalState state, float progress)
        {
            bool changed = state != State;
            State = state;
            _progress = progress;
            if (blocker != null) blocker.enabled = state != PortalState.Open;
            switch (state)
            {
                case PortalState.Locked:
                    PlayFx(lockedFx, true);
                    PlayFx(openFx, false);
                    if (hum != null) hum.Stop();
                    break;
                case PortalState.Opening:
                    if (hum != null && hum.clip != null) hum.Play();
                    break;
                case PortalState.Open:
                    _burst = true;
                    PlayFx(lockedFx, false);
                    PlayFx(openFx, true);
                    if (hum != null && hum.clip != null && !hum.isPlaying) hum.Play();
                    break;
            }
            _drawnOpen = -1f;
            Apply();
            if (changed) StateChanged?.Invoke(state);
        }

        private static void PlayFx(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on && !ps.isPlaying) ps.Play(true);
            else if (!on && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>Writes the visual state: surface open amount, beam + ground glow fade-in, hum volume.</summary>
        private void Apply()
        {
            float open = _progress;
            if (hum != null) hum.volume = humVolume * GameSettings.SfxVolume * Mathf.Clamp01((open - 0.3f) / 0.7f);
            if (Mathf.Approximately(open, _drawnOpen)) return;
            _drawnOpen = open;
            _mpb ??= new MaterialPropertyBlock();
            if (surface != null)
            {
                surface.GetPropertyBlock(_mpb);
                _mpb.SetFloat(OpenId, open);
                surface.SetPropertyBlock(_mpb);
            }
            float glow = Mathf.Clamp01((open - burstAt) / (1f - burstAt));
            SetAlpha(beam, glow);
            SetAlpha(groundGlow, Mathf.Max(0.15f, glow)); // a faint red pool even when locked
        }

        private void SetAlpha(Renderer r, float alpha)
        {
            if (r == null) return;
            bool show = alpha > 0.001f;
            if (r.enabled != show) r.enabled = show;
            if (!show) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(AlphaId, alpha);
            _mpb.SetFloat(OpenId, _progress);
            r.SetPropertyBlock(_mpb);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = State == PortalState.Open ? Color.cyan : Color.red;
            Gizmos.DrawWireSphere(TriggerPosition, triggerRadius);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PlayerEntered = null;
    }
}
