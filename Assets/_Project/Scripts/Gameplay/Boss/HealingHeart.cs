using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// One ember heart of the boss fight (KAN-60). Pooled and driven by <see cref="BossHearts"/> (one Update for all).
    /// Life: <b>Appearing</b> (a red-gold ground glow grows for <see cref="HeartRules.AppearTime"/>, no pickup yet), then
    /// <b>Floating</b> (the heart pops up, floats, sways and beats like a heart, lub-dub; in the last
    /// <see cref="HeartRules.WarningTime"/> seconds it blinks faster and faster, then shrinks away), or <b>Collected</b>
    /// (a short healing burst: the heart swells and fades, the glow flares wide). Colour by one MaterialPropertyBlock
    /// (heart emission, glow tint): no allocation per frame. Look built by the editor HeartBuilder.
    /// </summary>
    public sealed class HealingHeart : MonoBehaviour
    {
        public enum State
        {
            Off = 0,
            Appearing = 1,
            Floating = 2,
            Collected = 3,
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private const float PopTime = 0.3f;
        private const float VanishTime = 0.3f;
        private const float BurstTime = 0.45f;
        private const float BeatPeriod = 1.1f;

        [Tooltip("Floats, sways (yaw) and bobs.")]
        [SerializeField] private Transform pivot;
        [Tooltip("Heart mesh (beats by scale, glows by emission).")]
        [SerializeField] private Renderer heart;
        [Tooltip("Flat ground glow quad.")]
        [SerializeField] private Renderer glow;
        [SerializeField] private Color emberColor = new Color(1f, 0.36f, 0.1f, 1f);
        [SerializeField] private Color glowColor = new Color(1f, 0.28f, 0.12f, 0.85f);
        [SerializeField, Min(0f)] private float floatHeight = 0.95f;
        [SerializeField, Min(0.1f)] private float glowSize = 2.2f;

        private MaterialPropertyBlock _mpb;
        private float _blinkPhase;
        private float _swayPhase;
        private Vector3 _heartScale = Vector3.one;

        public State Current { get; private set; }
        /// <summary>Seconds in the current state.</summary>
        public float Age { get; private set; }
        public float AppearTime { get; private set; }
        public float Lifetime { get; private set; }
        public float WarningTime { get; private set; }
        /// <summary>Floating seconds left before it fades (full lifetime while it is still appearing).</summary>
        public float TimeLeft => Current == State.Floating ? Mathf.Max(0f, Lifetime - Age) : Current == State.Appearing ? Lifetime : 0f;
        /// <summary>On the ground (glowing or floating): counts against the cap.</summary>
        public bool IsOnGround => Current == State.Appearing || Current == State.Floating;
        /// <summary>The Knight can take it (floating, not just glowing).</summary>
        public bool CanPickUp => Current == State.Floating;
        public bool IsWarning => Current == State.Floating && TimeLeft <= WarningTime;
        /// <summary>Glow / emission strength 0..1 this frame (tests read the blink).</summary>
        public float Brightness { get; private set; }
        /// <summary>Heart scale factor this frame (0 = not visible).</summary>
        public float Size { get; private set; }

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(Transform pivotTransform, Renderer heartRenderer, Renderer glowRenderer)
        {
            pivot = pivotTransform;
            heart = heartRenderer;
            glow = glowRenderer;
        }

        private void Awake()
        {
            if (heart != null) _heartScale = heart.transform.localScale;
        }

        /// <summary>Starts the ground glow at <paramref name="position"/>; the heart appears after <paramref name="appearTime"/>.</summary>
        public void Spawn(Vector3 position, float appearTime, float lifetime, float warningTime)
        {
            transform.SetPositionAndRotation(position, Quaternion.identity);
            AppearTime = Mathf.Max(0f, appearTime);
            Lifetime = Mathf.Max(0.01f, lifetime);
            WarningTime = Mathf.Clamp(warningTime, 0f, Lifetime);
            _blinkPhase = 0f;
            _swayPhase = Random.Range(0f, Mathf.PI * 2f);
            Enter(AppearTime > 0f ? State.Appearing : State.Floating);
            gameObject.SetActive(true);
            Tick(0f);
        }

        /// <summary>Back to the pool.</summary>
        public void Despawn()
        {
            Current = State.Off;
            gameObject.SetActive(false);
        }

        /// <summary>Picked up: the healing burst starts. False when it cannot be taken now.</summary>
        public bool BeginCollect()
        {
            if (!CanPickUp) return false;
            Enter(State.Collected);
            return true;
        }

        /// <summary>
        /// Advances the heart by <paramref name="dt"/> scaled seconds (pauses with the level-up cards). True when it is done:
        /// faded unpicked (Floating) or the burst ended (Collected). The owner then pools it.
        /// </summary>
        public bool Tick(float dt)
        {
            switch (Current)
            {
                case State.Appearing:
                    Age += dt;
                    if (Age >= AppearTime)
                    {
                        float extra = Age - AppearTime;
                        Enter(State.Floating);
                        return TickFloating(extra);
                    }
                    ShowAppearing(Age / Mathf.Max(0.01f, AppearTime));
                    return false;
                case State.Floating:
                    return TickFloating(dt);
                case State.Collected:
                    Age += dt;
                    ShowBurst(Mathf.Clamp01(Age / BurstTime));
                    return Age >= BurstTime;
                default:
                    return false;
            }
        }

        private bool TickFloating(float dt)
        {
            Age += dt;
            float left = Lifetime - Age;
            _swayPhase += dt * 1.6f;

            // Pop in, beat, shrink away at the very end.
            float s = Age < PopTime ? Overshoot(Age / PopTime) : 1f;
            if (left < VanishTime) s *= Mathf.Max(0f, left) / VanishTime;
            s *= 1f + Beat(Age);
            Size = s;

            // Calm glow, then a blink that speeds up from 2 to 8 Hz through the warning window.
            float b;
            if (WarningTime > 0f && left <= WarningTime)
            {
                float k = 1f - Mathf.Max(0f, left) / WarningTime;
                _blinkPhase += dt * Mathf.Lerp(2f, 8f, k) * Mathf.PI * 2f;
                b = 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Cos(_blinkPhase));
            }
            else
            {
                b = 0.75f + 0.25f * Beat(Age) / 0.14f;
            }
            Brightness = b;

            if (pivot != null)
            {
                float rise = Age < PopTime ? Mathf.Lerp(0.2f, 1f, Overshoot(Age / PopTime)) : 1f;
                pivot.localPosition = new Vector3(0f, floatHeight * rise + Mathf.Sin(_swayPhase * 0.8f) * 0.1f, 0f);
                pivot.localRotation = Quaternion.Euler(0f, Mathf.Sin(_swayPhase) * 22f, 0f);
            }
            SetHeart(s, b);
            SetGlow(glowSize, b * 0.85f);
            return left <= 0f;
        }

        private void ShowAppearing(float k)
        {
            Size = 0f;
            Brightness = k;
            SetHeart(0f, 0f);
            // The glow gathers: grows from half size with a quickening pulse.
            float pulse = 0.85f + 0.15f * Mathf.Sin(k * k * 14f);
            SetGlow(glowSize * Mathf.Lerp(0.5f, 1f, k), Mathf.SmoothStep(0f, 1f, k) * pulse);
        }

        private void ShowBurst(float k)
        {
            float fade = 1f - k;
            Size = Mathf.Lerp(1.15f, 1.9f, Mathf.Sqrt(k)) * fade;
            Brightness = fade;
            if (pivot != null) pivot.localPosition = new Vector3(0f, floatHeight + k * 0.6f, 0f);
            SetHeart(Size, 1f + fade);
            SetGlow(glowSize * Mathf.Lerp(1f, 2.4f, Mathf.Sqrt(k)), fade);
        }

        private void Enter(State state)
        {
            Current = state;
            Age = 0f;
        }

        private void SetHeart(float scale, float emission)
        {
            if (heart == null) return;
            bool show = scale > 0.001f;
            if (heart.enabled != show) heart.enabled = show;
            if (!show) return;
            heart.transform.localScale = _heartScale * scale;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetColor(EmissionColorId, emberColor * (0.6f + 0.9f * emission));
            heart.SetPropertyBlock(_mpb);
        }

        private void SetGlow(float size, float alpha)
        {
            if (glow == null) return;
            bool show = alpha > 0.001f;
            if (glow.enabled != show) glow.enabled = show;
            if (!show) return;
            glow.transform.localScale = new Vector3(size, size, 1f);
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            var c = glowColor;
            c.a *= Mathf.Clamp01(alpha);
            _mpb.SetColor(BaseColorId, c);
            glow.SetPropertyBlock(_mpb);
        }

        /// <summary>Heartbeat scale bump: lub, then a softer dub 0.2 s later, once per <see cref="BeatPeriod"/>.</summary>
        private static float Beat(float t)
        {
            float p = Mathf.Repeat(t, BeatPeriod);
            return Bump(p, 0f) * 0.14f + Bump(p, 0.2f) * 0.08f;
        }

        private static float Bump(float p, float at)
        {
            float x = (p - at) / 0.12f;
            return x < 0f || x > 1f ? 0f : Mathf.Sin(x * Mathf.PI);
        }

        private static float Overshoot(float t)
        {
            t = Mathf.Clamp01(t);
            const float k = 1.70158f;
            float u = t - 1f;
            return 1f + (k + 1f) * u * u * u + k * u * u;
        }
    }
}
