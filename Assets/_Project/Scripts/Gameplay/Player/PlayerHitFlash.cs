using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Tint on the character's renderers when the Knight is hurt. A discrete hit (<see cref="PlayerHealth.OnHit"/>, KAN-187:
    /// ranged shot, boss attack, telegraphed special) flashes white-hot, then red, then back. A plain
    /// <see cref="PlayerHealth.OnHurt"/> pulse (horde contact, burning ground) only gives a soft red tint, so standing in a
    /// horde does not strobe. "Reduce screen effects" (<see cref="GameSettings.ReduceScreenEffects"/>) turns both off.
    /// Tints through a MaterialPropertyBlock (_BaseColor, no new material instance) and clears the block when the flash
    /// ends, so the renderers return to the SRP Batcher path between hits.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerHitFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Body renderers to tint (not the blob shadow or ground markers).")]
        [SerializeField] private Renderer[] renderers;
        [Tooltip("_BaseColor at the flash peak. Above 1 brightens the albedo.")]
        [SerializeField, ColorUsage(false, true)] private Color flashColor = new Color(2.4f, 0.55f, 0.45f);
        [SerializeField, Min(0.02f)] private float flashTime = 0.16f;
        [Tooltip("KAN-187: white-hot start of a discrete hit flash, before it turns red.")]
        [SerializeField, ColorUsage(false, true)] private Color hotColor = new Color(3.2f, 2.9f, 2.7f);
        [Tooltip("KAN-187: seconds of a discrete hit flash (white, then red, then back).")]
        [SerializeField, Min(0.02f)] private float hitFlashTime = 0.22f;
        [Tooltip("KAN-187: strength of the contact pulse tint (share of the full red).")]
        [SerializeField, Range(0f, 1f)] private float contactStrength = 0.45f;

        private PlayerHealth _health;
        private MaterialPropertyBlock _block;
        private float _flash, _length;
        private bool _hot;
        private bool _reduceEffects; // cached: no PlayerPrefs read per hit

        /// <summary>A flash is running (tests).</summary>
        public bool Flashing => _flash > 0f;
        /// <summary>The running flash is a discrete hit flash (white-hot start), not a contact tint (tests).</summary>
        public bool HotFlash => _flash > 0f && _hot;
        /// <summary>Body renderers being tinted (tests).</summary>
        public Renderer[] Renderers => renderers;

        /// <summary>KAN-153: another hero model took the Knight's place (<c>HeroLoadout</c>, before the first hit).</summary>
        public void SetRenderers(Renderer[] body)
        {
            if (_flash > 0f) Clear();
            _flash = 0f;
            renderers = body;
        }

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            _health.OnHurt += OnHurt;
            _health.OnHit += OnHit;
            GameSettings.Changed += ReadSettings;
            ReadSettings();
        }

        private void ReadSettings() => _reduceEffects = GameSettings.ReduceScreenEffects;

        private void OnDisable()
        {
            _health.OnHurt -= OnHurt;
            _health.OnHit -= OnHit;
            GameSettings.Changed -= ReadSettings;
            if (_flash > 0f) Clear();
            _flash = 0f;
        }

        private void OnHurt()
        {
            if (_reduceEffects || _hot && _flash > 0f) return; // a hit flash is never cut by a soft tint
            _hot = false;
            _flash = _length = flashTime;
        }

        private void OnHit(PlayerHit hit)
        {
            if (_reduceEffects || hit.Dealt <= 0f) return;
            _hot = true;
            _flash = _length = hitFlashTime;
        }

        private void Update()
        {
            if (_flash <= 0f) return;
            _flash -= Time.deltaTime;
            if (_flash <= 0f)
            {
                Clear();
                return;
            }
            float k = _flash / _length; // 1 at the start, 0 at the end
            Color c;
            if (_hot)
                c = k > 0.75f ? Color.Lerp(flashColor, hotColor, (k - 0.75f) / 0.25f) : Color.Lerp(Color.white, flashColor, k / 0.75f);
            else
                c = Color.Lerp(Color.white, flashColor, k * k * contactStrength);
            _block.SetColor(BaseColorId, c);
            if (renderers == null) return;
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(_block);
        }

        private void Clear()
        {
            _hot = false;
            if (renderers == null) return;
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(null);
        }
    }
}
