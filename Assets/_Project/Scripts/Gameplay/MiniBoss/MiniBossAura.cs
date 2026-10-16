using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.MiniBosses
{
    /// <summary>
    /// Ground aura of the mini boss (KAN-23): a dark soft disc and a slowly turning, pulsing ring on the ground under its
    /// feet, so the mini boss reads as "the special one" inside a crowd at a glance. Two flat quads (alpha blend: dark colours
    /// read on white snow, additive ones vanish), colour through one MaterialPropertyBlock, no allocation per frame. Sized by
    /// the body (<see cref="Enemy.Scale"/>). Hidden while there is no living target. One per scene ("MiniBoss/Aura").
    /// </summary>
    [DefaultExecutionOrder(100)] // after the enemy moved
    public sealed class MiniBossAura : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer ring;
        [SerializeField] private Renderer disc;
        [Tooltip("Ring pulses per second.")]
        [SerializeField, Min(0f)] private float pulseRate = 1.4f;
        [Tooltip("Ring turns (degrees per second).")]
        [SerializeField] private float spin = 40f;
        [Tooltip("Disc alpha x the aura colour alpha.")]
        [SerializeField, Range(0f, 1f)] private float discShare = 0.75f;
        [Tooltip("Height above the ground (no z-fight).")]
        [SerializeField, Min(0f)] private float lift = 0.04f;

        private MaterialPropertyBlock _mpb;
        private Enemy _target;
        private float _radius = 1f;
        private Color _color = Color.red;
        private float _angle;

        public Enemy Target => _target;
        public bool IsShowing => _target != null;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(Renderer ringRenderer, Renderer discRenderer)
        {
            ring = ringRenderer;
            disc = discRenderer;
            Stop();
        }

        private void Awake()
        {
            if (_target == null) SetVisible(false);
        }

        /// <summary>Shows the aura under <paramref name="target"/> (<paramref name="radius"/> m at size 1, <paramref name="color"/>).</summary>
        public void Follow(Enemy target, float radius, Color color)
        {
            _target = target;
            _radius = Mathf.Max(0.2f, radius);
            _color = color;
            SetVisible(target != null);
            LateUpdate();
        }

        /// <summary>Hides the aura (mini boss died).</summary>
        public void Stop()
        {
            _target = null;
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            if (!_target.IsAlive || !_target.gameObject.activeSelf) // dead or back in its pool
            {
                Stop();
                return;
            }
            var p = _target.transform.position;
            transform.position = new Vector3(p.x, p.y + lift, p.z);
            float size = _radius * 2f * _target.Scale;
            _angle = Mathf.Repeat(_angle + spin * Time.deltaTime, 360f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseRate * Mathf.PI * 2f);
            if (ring != null)
            {
                ring.transform.localScale = Vector3.one * (size * (1f + 0.06f * pulse));
                ring.transform.localRotation = Quaternion.Euler(90f, _angle, 0f);
                var c = _color;
                c.a *= 0.7f + 0.3f * pulse;
                Paint(ring, c);
            }
            if (disc != null)
            {
                disc.transform.localScale = Vector3.one * (size * 0.9f);
                var c = _color * 0.45f;
                c.a = _color.a * discShare;
                Paint(disc, c);
            }
        }

        private void Paint(Renderer r, Color c)
        {
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        private void SetVisible(bool on)
        {
            if (ring != null) ring.enabled = on;
            if (disc != null) disc.enabled = on;
        }
    }
}
