using UnityEngine;

namespace SurvivorSouls.Gameplay.Chests
{
    /// <summary>
    /// A Reliquary chest waiting on the ground (KAN-72): what it holds (tier, cards on the upgrade screen, levels it gives)
    /// and how long it stays. Pooled and driven by <see cref="ChestDropper"/> (one Update for every chest). Look: a small
    /// wooden chest that pops up when it lands, a gold light beam (crossed quads, alpha blend so it reads on snow) and a
    /// ground glow; in the last <see cref="ChestDropConfig.WarningTime"/> seconds the beam and glow blink faster and
    /// faster, then the chest shrinks away. Colour through one MaterialPropertyBlock, no allocation per frame.
    /// Opening it (walk in, upgrade screen) is KAN-73.
    /// </summary>
    public sealed class ReliquaryChest : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float LandTime = 0.3f;
        private const float VanishTime = 0.25f;

        [SerializeField] private Transform body;
        [SerializeField] private Renderer beam;
        [SerializeField] private Renderer glow;
        [SerializeField] private Color lightColor = new Color(1f, 0.74f, 0.2f, 0.8f);
        [SerializeField] private Color rareColor = new Color(0.45f, 0.72f, 1f, 0.85f);
        [Tooltip("Body scale of a Rare chest (a Light chest is 1).")]
        [SerializeField, Min(0.5f)] private float rareScale = 1.25f;

        private MaterialPropertyBlock _mpb;
        private float _phase;

        public ChestTier Tier { get; private set; }
        /// <summary>Cards on the upgrade screen (KAN-73).</summary>
        public int Choices { get; private set; }
        /// <summary>Levels the pick gives (Rare chest: more).</summary>
        public int Upgrades { get; private set; }
        public float Age { get; private set; }
        public float Lifetime { get; private set; }
        public float WarningTime { get; private set; }
        public float TimeLeft => Mathf.Max(0f, Lifetime - Age);
        /// <summary>In the warning window (blinking).</summary>
        public bool IsWarning => IsOnGround && TimeLeft <= WarningTime;
        public bool IsOnGround { get; private set; }
        /// <summary>Beam / glow brightness 0..1 this frame (tests read the pulse).</summary>
        public float Brightness { get; private set; }

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(Transform bodyTransform, Renderer beamRenderer, Renderer glowRenderer)
        {
            body = bodyTransform;
            beam = beamRenderer;
            glow = glowRenderer;
        }

        /// <summary>Puts the chest on the ground at <paramref name="position"/> with its contents and timer.</summary>
        public void Spawn(Vector3 position, ChestTier tier, int choices, int upgrades, float lifetime, float warningTime)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f);
            Tier = tier;
            Choices = Mathf.Max(1, choices);
            Upgrades = Mathf.Max(1, upgrades);
            Lifetime = Mathf.Max(0.01f, lifetime);
            WarningTime = Mathf.Clamp(warningTime, 0f, Lifetime);
            Age = 0f;
            _phase = 0f;
            IsOnGround = true;
            gameObject.SetActive(true);
            Tick(0f);
        }

        /// <summary>Takes the chest off the ground (expired, opened, Chamber end). The dropper returns it to the pool.</summary>
        public void Despawn()
        {
            IsOnGround = false;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Advances the timer by <paramref name="dt"/> (scaled time: pauses with the level-up cards) and updates the look.
        /// True when the lifetime is over (the dropper then expires it).
        /// </summary>
        public bool Tick(float dt)
        {
            if (!IsOnGround) return false;
            Age += dt;
            float left = TimeLeft;

            // Land: quick overshoot pop. Vanish: shrink in the last moments.
            float s = Age < LandTime ? Overshoot(Age / LandTime) : 1f;
            if (left < VanishTime) s *= left / VanishTime;
            if (body != null) body.localScale = Vector3.one * (s * (Tier == ChestTier.Rare ? rareScale : 1f));

            // Calm breathing, then a blink that speeds up from 2 to 7 Hz through the warning window.
            float b;
            if (WarningTime > 0f && left <= WarningTime)
            {
                float k = 1f - left / WarningTime;
                _phase += dt * Mathf.Lerp(2f, 7f, k) * Mathf.PI * 2f;
                b = 0.2f + 0.8f * (0.5f + 0.5f * Mathf.Cos(_phase));
            }
            else
            {
                _phase += dt * 1.1f * Mathf.PI * 2f;
                b = 0.8f + 0.2f * Mathf.Sin(_phase);
            }
            if (Age < LandTime) b *= Age / LandTime;
            Brightness = b;
            var c = Tier == ChestTier.Rare ? rareColor : lightColor;
            c.a *= b;
            Paint(beam, c);
            c.a = Mathf.Min(1f, c.a * 1.2f);
            Paint(glow, c);
            return left <= 0f;
        }

        private static float Overshoot(float t)
        {
            t = Mathf.Clamp01(t);
            const float k = 1.70158f;
            float u = t - 1f;
            return 1f + (k + 1f) * u * u * u + k * u * u;
        }

        private void Paint(Renderer r, Color c)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
