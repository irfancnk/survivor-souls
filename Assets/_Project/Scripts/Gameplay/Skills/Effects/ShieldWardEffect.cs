using System;
using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Hallowed Shield" (KAN-93, Epic): on a cooldown a ward rises round the Knight for <see cref="Duration"/> seconds
    /// (<see cref="StatId.WardDuration"/>). While it is up, <see cref="Player.PlayerHealth.Warded"/> blocks every hit (the
    /// shield pool is not touched). The next ward comes <see cref="Cooldown"/> seconds after the last one ended
    /// (<see cref="cooldown"/> + <see cref="StatId.WardCooldown"/>, levels subtract); the first one <see cref="firstDelay"/>
    /// after the pick (or after a Chamber loads). Scaled time: pause and the level-up screen freeze both clocks.
    /// Look: one sphere with the WardBubble shader (pop in, breathe, gold flash on a blocked hit, flicker in the last
    /// <see cref="warnTime"/> seconds, swell + fade out) and motes on one shared particle system. One MPB write per frame
    /// while visible, renderer off otherwise, no alloc. Static events drive the sounds (GameAudio); light haptic on a raise.
    /// </summary>
    public sealed class ShieldWardEffect : SkillEffect, ISkillCooldown
    {
        [SerializeField, Min(1f)] private float cooldown = 20f;
        [SerializeField, Min(0f)] private float firstDelay = 1f;
        [SerializeField, Min(0f)] private float warnTime = 0.9f;
        [SerializeField, Min(0.05f)] private float blockPulseInterval = 0.2f;

        [Header("Look")]
        [SerializeField] private Renderer bubble;
        [SerializeField] private ParticleSystem motes;
        [SerializeField, Min(0.1f)] private float bubbleScale = 2.2f;
        [SerializeField, Min(0.01f)] private float popTime = 0.18f;
        [SerializeField, Min(0.01f)] private float fadeOutTime = 0.25f;
        [SerializeField, Min(0)] private int raiseMotes = 18;
        [SerializeField, Min(0)] private int breakMotes = 26;

        /// <summary>A ward rose (sound, haptic).</summary>
        public static event Action WardRaised;
        /// <summary>A ward ran out (sound).</summary>
        public static event Action WardEnded;
        /// <summary>A ward blocked a hit (at most one per <see cref="blockPulseInterval"/>).</summary>
        public static event Action WardBlocked;

        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");

        private MaterialPropertyBlock _mpb;
        private Action<float> _onBlocked;
        private bool _started;
        private float _cooldownLeft;
        private float _activeLeft;
        private float _shown;     // seconds since the ward rose (pop)
        private float _fade;      // fade-out left, seconds
        private float _flash;
        private float _lastBlock = float.NegativeInfinity;

        /// <summary>Seconds a ward lasts at the current level.</summary>
        public float Duration => Mathf.Max(0.1f, PlayerStats.Eval(Context?.Stats, StatId.WardDuration, 0f));
        /// <summary>Seconds between the end of one ward and the start of the next, at the current level.</summary>
        public float Cooldown => Mathf.Max(1f, PlayerStats.Eval(Context?.Stats, StatId.WardCooldown, cooldown));
        public bool IsActive { get; private set; }
        /// <summary>Ward seconds left (0 when down).</summary>
        public float ActiveLeft => IsActive ? _activeLeft : 0f;
        /// <summary>Seconds to the next ward (0 while up).</summary>
        public float CooldownLeft => IsActive ? 0f : Mathf.Max(0f, _cooldownLeft);
        /// <summary>True in the last <see cref="warnTime"/> seconds of a ward (bubble flickers).</summary>
        public bool Warning => IsActive && _activeLeft <= warnTime;
        public int RaisedCount { get; private set; }
        /// <summary>Damage the wards blocked this run.</summary>
        public float BlockedDamage { get; private set; }
        public Renderer Bubble => bubble;

        /// <summary>HUD slot shade (KAN-192): <see cref="CooldownLeft"/> over <see cref="Cooldown"/> (the 1 s first delay shows as a sliver).</summary>
        public float CooldownRemaining01 => IsActive ? 0f : Mathf.Clamp01(CooldownLeft / Cooldown);
        /// <summary>HUD slot gold ring (KAN-192): the ward is up.</summary>
        public bool CooldownEffectActive => IsActive;

        protected override void Subscribe()
        {
            if (!_started)
            {
                _started = true;
                _cooldownLeft = firstDelay;
            }
            _onBlocked ??= OnBlocked;
            if (Context.Health != null) Context.Health.OnWardBlocked += _onBlocked;
        }

        protected override void Unsubscribe()
        {
            if (Context.Health != null)
            {
                Context.Health.OnWardBlocked -= _onBlocked;
                if (IsActive) Context.Health.Warded = false;
            }
            IsActive = false;
            _fade = 0f;
            if (bubble != null) bubble.enabled = false;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the ward clocks and the look by <paramref name="dt"/> scaled seconds. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (Context == null) return;
            var health = Context.Health;
            if (health != null && health.IsDead)
            {
                if (IsActive) Drop(silent: true);
                UpdateLook(dt);
                return;
            }
            if (IsActive)
            {
                _activeLeft -= dt;
                if (_activeLeft <= 0f) Drop(silent: false);
            }
            else
            {
                _cooldownLeft -= dt;
                if (_cooldownLeft <= 0f) Raise();
            }
            UpdateLook(dt);
        }

        private void Raise()
        {
            IsActive = true;
            _activeLeft = Duration;
            _shown = 0f;
            _fade = 0f;
            _flash = 0.6f;
            RaisedCount++;
            if (Context.Health != null) Context.Health.Warded = true;
            EmitMotes(raiseMotes, outward: false);
            Haptics.Light();
            WardRaised?.Invoke();
        }

        private void Drop(bool silent)
        {
            IsActive = false;
            _cooldownLeft = Cooldown;
            _fade = fadeOutTime;
            if (Context.Health != null) Context.Health.Warded = false;
            if (silent) return;
            EmitMotes(breakMotes, outward: true);
            WardEnded?.Invoke();
        }

        private void OnBlocked(float amount)
        {
            if (!IsActive) return;
            BlockedDamage += amount;
            float now = Time.time;
            if (now - _lastBlock < blockPulseInterval) return;
            _lastBlock = now;
            _flash = 1f;
            WardBlocked?.Invoke();
        }

        private void UpdateLook(float dt)
        {
            if (bubble == null) return;
            _flash = Mathf.Max(0f, _flash - dt * 5f);
            float alpha, scale;
            if (IsActive)
            {
                _shown += dt;
                float t = Mathf.Clamp01(_shown / popTime);
                alpha = t;
                scale = EaseOutBack(t) * (1f + 0.015f * Mathf.Sin(_shown * 3f));
                if (_activeLeft <= warnTime && warnTime > 0f)
                {
                    // Flicker faster as the end nears: 6 Hz -> 14 Hz.
                    float k = 1f - Mathf.Clamp01(_activeLeft / warnTime);
                    float hz = Mathf.Lerp(6f, 14f, k);
                    alpha *= Mathf.Sin(_shown * hz * Mathf.PI * 2f) > 0f ? 1f : 0.3f;
                }
            }
            else if (_fade > 0f)
            {
                _fade = Mathf.Max(0f, _fade - dt);
                float t = _fade / fadeOutTime;
                alpha = t;
                scale = 1f + 0.15f * (1f - t);
            }
            else
            {
                if (bubble.enabled) bubble.enabled = false;
                return;
            }
            if (!bubble.enabled) bubble.enabled = true;
            bubble.transform.localScale = Vector3.one * (bubbleScale * Mathf.Max(0.01f, scale));
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetFloat(AlphaId, alpha);
            _mpb.SetFloat(FlashId, _flash);
            bubble.SetPropertyBlock(_mpb);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private void EmitMotes(int count, bool outward)
        {
            if (motes == null || count <= 0) return;
            Vector3 centre = bubble != null ? bubble.transform.position : transform.position + Vector3.up;
            float r = bubbleScale * 0.5f;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f + UnityEngine.Random.Range(-0.15f, 0.15f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (outward)
                {
                    // Ward breaks: shards fly off the shell.
                    Vector3 d = (dir + Vector3.up * UnityEngine.Random.Range(-0.6f, 0.9f)).normalized;
                    p.position = centre + d * r;
                    p.velocity = d * UnityEngine.Random.Range(2.5f, 4.5f);
                }
                else
                {
                    // Ward rises: motes climb from a ring at the feet.
                    p.position = new Vector3(centre.x, transform.position.y + 0.05f, centre.z) + dir * r * 0.9f;
                    p.velocity = Vector3.up * UnityEngine.Random.Range(1.8f, 3.2f) - dir * 0.4f;
                }
                motes.Emit(p, 1);
            }
        }
    }
}
