using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Red ground warning of a boss attack (KAN-58): a ring at the full danger radius and a disc that grows from the
    /// centre to the ring while the attack winds up (<see cref="SetProgress"/>), so the player reads both where and when.
    /// The hit flashes the disc bright, then it fades. Two flat quads (alpha blend, dark red reads on snow and rock),
    /// colour through one MaterialPropertyBlock, no allocation per frame. One per boss; reused for every attack.
    /// KAN-121: <see cref="ShowWave"/> shows the ring alone, bright, as a moving shockwave front (no disc).
    /// KAN-106: <see cref="Tint"/> recolours one warning until it hides (pooled warnings go back to red).
    /// KAN-136: the look is the TelegraphCircle shader (a rune circle burnt into the ground): the block also carries the
    /// progress, the fill's share of the full circle (the crack pattern stays fixed on the ground), the turns of the glyph
    /// band (<see cref="runeTurnsPerSecond"/> while it warns, frozen on the hit), the hit flare and the wave mode.
    /// </summary>
    public sealed class GroundTelegraph : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int FillScaleId = Shader.PropertyToID("_FillScale");
        private static readonly int RuneTurnsId = Shader.PropertyToID("_RuneTurns");
        private static readonly int FlareId = Shader.PropertyToID("_Flare");
        private static readonly int WaveId = Shader.PropertyToID("_Wave");
        private static readonly int WaveRimId = Shader.PropertyToID("_WaveRim");
        private static readonly int WaveStretchId = Shader.PropertyToID("_WaveStretch");
        /// <summary>Where the ring texture's rim sits, as a share of the quad's half size (BossBuilder.RingPixel).</summary>
        public const float RimShare = 0.92f;
        /// <summary>KAN-132: the ring texture's wave band (TelegraphArt.WaveBand) runs from RimShare - inner to RimShare + outer.</summary>
        public const float WaveBandInner = 0.065f, WaveBandOuter = 0.05f;

        [SerializeField] private Renderer ring;
        [SerializeField] private Renderer fill;
        [SerializeField] private Color ringColor = new Color(0.62f, 0.02f, 0.01f, 1f);
        [SerializeField] private Color fillColor = new Color(0.5f, 0.02f, 0.01f, 0.78f);
        [SerializeField] private Color flashColor = new Color(1f, 0.42f, 0.2f, 0.85f);
        [Tooltip("Seconds the hit flash takes to fade.")]
        [SerializeField, Min(0.05f)] private float flashTime = 0.3f;
        [Tooltip("Height above the ground (no z-fight with the floor).")]
        [SerializeField, Min(0f)] private float lift = 0.05f;
        [Tooltip("KAN-136: turns per second of the glyph band while the warning shows.")]
        [SerializeField] private float runeTurnsPerSecond = 0.3f;

        private MaterialPropertyBlock _mpb;
        private Color _ring, _fill, _flashC; // colours in use: the serialized ones unless Tint set others (reset on Hide)
        private float _radius = 1f;
        private float _flash = -1f;
        private float _shownAt;     // KAN-136: Time.time of Show (glyph band turns from here)
        private float _turns;       // glyph band turns in use (frozen on the hit flash)
        private float _fillShare;   // fill radius / full radius
        private bool _wave;
        private float _waveRim = RimShare, _waveStretch = 1f; // KAN-132: band remap (shader _WaveRim / _WaveStretch)
        private Color _ringPaint;   // last ring colour (Tick repaints it as the glyph band turns)

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
            _wave = false;
            _shownAt = Time.time;
            IsShowing = true;
            SetVisible(true);
            SetProgress(0f);
        }

        /// <summary>
        /// KAN-121: a shockwave front: the ring alone with its rim at <paramref name="radius"/> m round <paramref name="centre"/>,
        /// in the hit colour, fading with <paramref name="fade"/> (0 = fresh, 1 = gone). Call every frame while the wave grows;
        /// <see cref="Hide"/> ends it. KAN-132: <paramref name="band"/> &gt; 0 = the shattered band is that many metres wide
        /// round the front at any radius (the hit band); 0 = the texture band, which scales with the radius (~0.5 m at 4 m).
        /// </summary>
        public void ShowWave(Vector3 centre, float radius, float fade, float band = 0f)
        {
            _radius = Mathf.Max(0.05f, radius);
            transform.position = new Vector3(centre.x, centre.y + lift, centre.z);
            float half = WaveQuadHalf(_radius, band, out _waveRim, out _waveStretch);
            if (ring != null)
            {
                ring.transform.localScale = Vector3.one * (half * 2f);
                if (!ring.enabled) ring.enabled = true;
            }
            if (fill != null && fill.enabled) fill.enabled = false;
            _flash = -1f;
            _wave = true;
            IsShowing = true;
            Progress = 1f;
            var c = _flashC;
            c.a = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(fade));
            Paint(ring, c);
        }

        /// <summary>
        /// KAN-132: ring quad half size (m) for a wave front at <paramref name="radius"/> with a <paramref name="band"/> m wide
        /// band, and the shader remap: <paramref name="rim"/> = the front as a share of the half size, <paramref name="stretch"/>
        /// = texture share per quad share. The quad grows past radius / RimShare when the wide band's outer part needs room.
        /// </summary>
        public static float WaveQuadHalf(float radius, float band, out float rim, out float stretch)
        {
            float half = radius / RimShare;
            rim = RimShare;
            stretch = 1f;
            if (band <= 0f) return half;
            float tex = WaveBandInner + WaveBandOuter;
            half = Mathf.Max(half, (radius + band * WaveBandOuter / tex) / 0.98f);
            rim = radius / half;
            stretch = tex * half / band;
            return half;
        }

        /// <summary>Wave band width (m) on screen for the current wave (tests): texture band share / stretch x half size.</summary>
        public float WaveBandWidth => (WaveBandInner + WaveBandOuter) / Mathf.Max(1e-4f, _waveStretch) * (_radius / Mathf.Max(1e-4f, _waveRim));

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
            _fillShare = Mathf.Max(0.02f, eased);
            _turns = (Time.time - _shownAt) * runeTurnsPerSecond;
            if (fill != null) fill.transform.localScale = Vector3.one * (_fillShare * _radius * 2f);
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, Progress));
            var r = _ring;
            r.a *= pulse;
            Paint(ring, r);
            var f = _fill;
            f.a *= Mathf.Lerp(0.55f, 1f, Progress);
            Paint(fill, f);
        }

        /// <summary>The attack landed: bright flash over the full circle, then fade out and hide.</summary>
        public void Flash()
        {
            if (!IsShowing) return;
            Progress = 1f;
            _flash = 0f;
            _fillShare = 1f;
            if (fill != null) fill.transform.localScale = Vector3.one * (_radius * 2f);
        }

        /// <summary>
        /// KAN-106: other colours for this warning until it hides (the Plague Bloater's circle is toxic green). Call after
        /// <see cref="Show"/>; <see cref="Hide"/> (also the end of the hit flash) goes back to the serialized red.
        /// </summary>
        public void Tint(Color ringTint, Color fillTint, Color flashTint)
        {
            _ring = ringTint;
            _fill = fillTint;
            _flashC = flashTint;
        }

        /// <summary>Hides at once (attack cancelled, boss died).</summary>
        public void Hide()
        {
            _ring = ringColor;
            _fill = fillColor;
            _flashC = flashColor;
            IsShowing = false;
            _flash = -1f;
            _wave = false;
            Progress = 0f;
            SetVisible(false);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Fades the hit flash (public for tests).</summary>
        public void Tick(float dt)
        {
            if (_flash < 0f)
            {
                // KAN-136: the glyph band keeps turning even when an owner stops calling SetProgress (held warnings).
                if (IsShowing && !_wave)
                {
                    _turns = (Time.time - _shownAt) * runeTurnsPerSecond;
                    Paint(ring, _ringPaint);
                }
                return;
            }
            _flash += dt;
            float k = Mathf.Clamp01(_flash / flashTime);
            var f = Color.Lerp(_flashC, new Color(_flashC.r, _flashC.g, _flashC.b, 0f), k);
            Paint(fill, f);
            var r = _ring;
            r.a *= 1f - k;
            Paint(ring, r);
            if (k >= 1f) Hide();
        }

        private void SetVisible(bool on)
        {
            if (ring != null && ring.enabled != on) ring.enabled = on;
            if (fill != null && fill.enabled != on) fill.enabled = on;
        }

        /// <summary>KAN-136: 0 = no hit flare, 1 = the hit just landed (fades with the flash).</summary>
        public float Flare => _flash < 0f ? 0f : 1f - Mathf.Clamp01(_flash / flashTime);
        /// <summary>KAN-136: turns of the glyph band now (frozen from the hit on).</summary>
        public float RuneTurns => _turns;

        /// <summary>Colour + the TelegraphCircle values (KAN-136).</summary>
        private void Paint(Renderer r, Color c)
        {
            if (r == null) return;
            if (r == ring) _ringPaint = c;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetFloat(ProgressId, Progress);
            _mpb.SetFloat(FillScaleId, _fillShare);
            _mpb.SetFloat(RuneTurnsId, _turns);
            _mpb.SetFloat(FlareId, Flare);
            _mpb.SetFloat(WaveId, _wave ? 1f : 0f);
            _mpb.SetFloat(WaveRimId, _wave ? _waveRim : RimShare);
            _mpb.SetFloat(WaveStretchId, _wave ? _waveStretch : 1f);
            r.SetPropertyBlock(_mpb);
        }
    }
}
