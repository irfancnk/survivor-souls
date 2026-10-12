using SurvivorSouls.Gameplay.Characters;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Small procedural recoil when the crossbow fires: the upper body (spine + chest) kicks back a few degrees
    /// and settles. Arms and crossbow are children of the chest, so the whole hold moves together (hands stay on).
    /// Runs in LateUpdate, on top of the Animator pose, so it works with every clip. Listens to
    /// <see cref="AutoShooter.Fired"/> on the same object.
    /// </summary>
    [RequireComponent(typeof(AutoShooter))]
    public sealed class CrossbowRecoil : MonoBehaviour
    {
        [SerializeField] private HumanoidRig rig;
        [Tooltip("Upper-body pitch back at the peak (degrees).")]
        [SerializeField, Range(0f, 20f)] private float kickDegrees = 7f;
        [Tooltip("Small twist toward the right shoulder at the peak (degrees).")]
        [SerializeField, Range(0f, 10f)] private float twistDegrees = 2.5f;
        [SerializeField, Min(0.005f)] private float attack = 0.04f;
        [SerializeField, Min(0.02f)] private float recover = 0.11f;

        private AutoShooter _shooter;
        private float _time = float.MaxValue;

        private void Awake() => _shooter = GetComponent<AutoShooter>();
        private void OnEnable() => _shooter.Fired += OnFired;
        private void OnDisable() => _shooter.Fired -= OnFired;

        private void OnFired(Vector3 muzzle, Vector3 direction) => _time = 0f;

        private void LateUpdate()
        {
            if (rig == null || _time > attack + recover * 6f) return;
            _time += Time.deltaTime;
            // Fast rise, exponential settle with a tiny overshoot.
            float k = _time < attack
                ? Mathf.SmoothStep(0f, 1f, _time / attack)
                : Mathf.Exp(-(_time - attack) / recover) * Mathf.Cos((_time - attack) / recover * 1.2f);

            Vector3 right = transform.right, up = transform.up;
            var kick = Quaternion.AngleAxis(-kickDegrees * k, right) * Quaternion.AngleAxis(twistDegrees * k, up);
            var half = Quaternion.Slerp(Quaternion.identity, kick, 0.4f);
            if (rig.spine != null) rig.spine.rotation = half * rig.spine.rotation;
            if (rig.chest != null) rig.chest.rotation = Quaternion.Slerp(Quaternion.identity, kick, 0.6f) * rig.chest.rotation;
        }
    }
}
