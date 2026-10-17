using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// KAN-164.3 (KAN-239) spike lines look. On <see cref="BossFight.SpikesStruck"/>: stone spike clusters burst out of the
    /// ground along every warned lane (one upright billboard per <see cref="spacing"/> m, world space, one particle system,
    /// one draw call), a short camera shake and a light haptic. Built by BossMoversStep on the "Boss" object. No GC: the
    /// shards are emitted with a reused <see cref="ParticleSystem.EmitParams"/>.
    /// </summary>
    public sealed class BossSpikeFx : MonoBehaviour
    {
        [SerializeField] private BossFight fight;
        [Tooltip("Spike sprites (world space, emission off, vertical billboards).")]
        [SerializeField] private ParticleSystem shards;
        [SerializeField] private TopDownFollowCamera followCamera;
        [Tooltip("Metres between spike clusters along a lane.")]
        [SerializeField, Min(0.2f)] private float spacing = 0.75f;
        [Tooltip("Spike height as a share of the lane width (random 0.8-1.2 x).")]
        [SerializeField, Min(0.1f)] private float heightPerWidth = 1.35f;
        [SerializeField, Min(0.1f)] private float life = 0.75f;
        [SerializeField, Min(0f)] private float shake = 0.2f;
        [SerializeField, Min(0f)] private float shakeTime = 0.25f;

        private ParticleSystem.EmitParams _emit;

        /// <summary>Spike bursts played (tests).</summary>
        public int Bursts { get; private set; }
        /// <summary>Spike clusters emitted in all (tests).</summary>
        public int Emitted { get; private set; }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossFight bossFight, ParticleSystem spikes)
        {
            if (isActiveAndEnabled) Unhook();
            fight = bossFight;
            shards = spikes;
            if (isActiveAndEnabled) Hook();
        }

        private void OnEnable() => Hook();
        private void OnDisable() => Unhook();

        private void Hook()
        {
            if (fight != null) fight.SpikesStruck += OnStruck;
        }

        private void Unhook()
        {
            if (fight != null) fight.SpikesStruck -= OnStruck;
        }

        private void OnStruck(Vector3 origin, float dealt)
        {
            var lanes = fight != null ? fight.Lines : null;
            if (lanes == null) return;
            float width = Mathf.Max(0.2f, lanes.Width);
            _emit.applyShapeToPosition = false;
            _emit.velocity = Vector3.zero;
            _emit.startLifetime = life;
            for (int i = 0; i < lanes.LaneCount; i++)
            {
                Vector3 dir = lanes.LaneDirection(i);
                Vector3 side = new Vector3(dir.z, 0f, -dir.x);
                Vector3 start = lanes.LaneStart(i);
                float length = lanes.LaneLength(i);
                for (float d = 0.9f; d < length; d += spacing)
                {
                    float h = width * heightPerWidth * Random.Range(0.8f, 1.2f);
                    Vector3 at = start + dir * d + side * (Random.Range(-0.25f, 0.25f) * width);
                    at.y += h * 0.42f; // the sprite's base sits just under the ground
                    _emit.position = at;
                    _emit.startSize = h;
                    _emit.rotation = Random.Range(-8f, 8f);
                    if (shards != null) shards.Emit(_emit, 1);
                    Emitted++;
                }
            }
            Bursts++;
            if (followCamera != null) followCamera.Shake(shake, shakeTime);
            Haptics.Light();
        }
    }
}
