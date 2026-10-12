using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Archero-style move hint: a small ring on the ground a short way ahead of the character, in the
    /// direction it moves. Fades in while moving, fades out when stopped. The marker is a child object
    /// with a transparent material; this script moves it and sets its alpha via a MaterialPropertyBlock.
    /// </summary>
    public sealed class DirectionMarker : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private PlayerMover mover;
        [SerializeField] private Renderer marker;
        [SerializeField, Min(0f)] private float distance = 1.7f;
        [SerializeField, Min(0f)] private float fadeSpeed = 8f;
        [SerializeField] private Color color = new Color(1f, 0.93f, 0.7f, 0.85f);

        private MaterialPropertyBlock _block;
        private float _alpha;
        private Vector3 _dir = Vector3.forward;

        private void Awake() => _block = new MaterialPropertyBlock();

        private void LateUpdate()
        {
            Vector3 v = mover.Velocity;
            bool moving = v.sqrMagnitude > 0.25f;
            if (moving) _dir = v.normalized;
            _alpha = Mathf.MoveTowards(_alpha, moving ? 1f : 0f, fadeSpeed * Time.deltaTime);

            marker.enabled = _alpha > 0.01f;
            if (!marker.enabled) return;
            marker.transform.position = transform.position + _dir * distance + Vector3.up * 0.05f;
            var c = color;
            c.a *= _alpha;
            _block.SetColor(BaseColorId, c);
            marker.SetPropertyBlock(_block);
        }
    }
}
