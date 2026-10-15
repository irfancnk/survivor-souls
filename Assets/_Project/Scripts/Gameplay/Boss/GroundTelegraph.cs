using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Red ground warning of a boss attack (KAN-58): a ring at the full danger radius and a disc that grows from the
    /// centre to the ring while the attack winds up (<see cref="SetProgress"/>), so the player reads both where and when.
    /// The hit flashes the disc bright, then it fades. Two flat quads (alpha blend, dark red reads on snow and rock),
    /// colour through one MaterialPropertyBlock, no allocation per frame. One per boss; reused for every attack.
    /// </summary>
    public sealed class GroundTelegraph : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer ring;
        [SerializeField] private Renderer fill;
        [SerializeField] private Color ringColor = new Color(0.62f, 0.02f, 0.01f, 1f);
        [SerializeField] private Color fillColor = new Color(0.5f, 0.02f, 0.01f, 0.78f);
        [SerializeField] private Color flashColor = new Color(1f, 0.42f, 0.2f, 0.85f);
        [Tooltip("Seconds the hit flash takes to fade.")]
        [SerializeField, Min(0.05f)] private float flashTime = 0.3f;
        [Tooltip("Height above the ground (no z-fight with the floor).")]
        [SerializeField, Min(0f)] private float lift = 0.05f;

        private MaterialPropertyBlock _mpb;
        private float _radius = 1f;
        private float _flash = -1f;

        /// <summary>A warning or its hit flash is on screen.</summary>
        public bool IsShowing { get; private set; }
        /// <summary>0 = just shown, 1 = the attack lands.</summary>
        public float Progress { get; private set; }
        public float Radius => _radius;
        public Vector3 Centre => transform.position;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(Renderer ringRenderer, Renderer fillRenderer)
        {
            ring = ringRenderer;
            fill = fillRenderer;
            Hide();
        }

        private void Awake() => Hide();

        /// <summary>Shows the warning at <paramref name="centre"/> (ground) with <paramref name="radius"/> m, progress 0.</summary>
        public void Show(Vector3 centre, float radius)
        {
            _radius = Mathf.Max(0.1f, radius);
            transform.position = new Vector3(centre.x, centre.y + lift, centre.z);
            if (ring != null) ring.transform.localScale = Vector3.one * (_radius * 2f);
            _flash = -1f;
            IsShowing = true;
            SetVisible(true);
            SetProgress(0f);
        }

        /// <summary>Moves a showing warning (the Butcher's spin circle follows the boss, KAN-108). Keeps size and progress.</summary>
        public void MoveTo(Vector3 centre)
        {
            if (!IsShowing) return;
            transform.position = new Vector3(centre.x, centre.y + lift, centre.z);
        }

        /// <summary>Wind-up 0..1: the disc grows to the ring and darkens; the ring pulses faster near the end.</summary>
        public void SetProgress(float progress)
        {
            if (!IsShowing || _flash >= 0f) return;
            Progress = Mathf.Clamp01(progress);
            float eased = 1f - (1f - Progress) * (1f - Progress);
            if (fill != null) fill.transform.localScale = Vector3.one * (Mathf.Max(0.02f, eased) * _radius * 2f);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, Progress));
            var r = ringColor;
            r.a *= pulse;
            Paint(ring, r);
            var f = fillColor;
            f.a *= Mathf.Lerp(0.55f, 1f, Progress);
            Paint(fill, f);
        }

        /// <summary>The attack landed: bright flash over the full circle, then fade out and hide.</summary>
        public void Flash()
        {
            if (!IsShowing) return;
            Progress = 1f;
            _flash = 0f;
            if (fill != null) fill.transform.localScale = Vector3.one * (_radius * 2f);
        }

        /// <summary>Hides at once (attack cancelled, boss died).</summary>
        public void Hide()
        {
            IsShowing = false;
            _flash = -1f;
            Progress = 0f;
            SetVisible(false);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Fades the hit flash (public for tests).</summary>
        public void Tick(float dt)
        {
            if (_flash < 0f) return;
            _flash += dt;
            float k = Mathf.Clamp01(_flash / flashTime);
            var f = Color.Lerp(flashColor, new Color(flashColor.r, flashColor.g, flashColor.b, 0f), k);
            Paint(fill, f);
            var r = ringColor;
            r.a *= 1f - k;
            Paint(ring, r);
            if (k >= 1f) Hide();
        }

        private void SetVisible(bool on)
        {
            if (ring != null && ring.enabled != on) ring.enabled = on;
            if (fill != null && fill.enabled != on) fill.enabled = on;
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
