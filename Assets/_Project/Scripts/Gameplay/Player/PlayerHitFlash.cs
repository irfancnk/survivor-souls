using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Brief red tint on the character's renderers on every <see cref="PlayerHealth.OnHurt"/> pulse.
    /// Tints through a MaterialPropertyBlock (_BaseColor) and clears the block when the flash ends, so the
    /// renderers return to the SRP Batcher path between hits.
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

        private PlayerHealth _health;
        private MaterialPropertyBlock _block;
        private float _flash;

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable() => _health.OnHurt += OnHurt;

        private void OnDisable()
        {
            _health.OnHurt -= OnHurt;
            if (_flash > 0f) Clear();
            _flash = 0f;
        }

        private void OnHurt() => _flash = flashTime;

        private void Update()
        {
            if (_flash <= 0f) return;
            _flash -= Time.deltaTime;
            if (_flash <= 0f)
            {
                Clear();
                return;
            }
            float k = _flash / flashTime;
            _block.SetColor(BaseColorId, Color.Lerp(Color.white, flashColor, k * k));
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(_block);
        }

        private void Clear()
        {
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(null);
        }
    }
}
