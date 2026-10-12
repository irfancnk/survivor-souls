using UnityEngine;

namespace SurvivorSouls.Gameplay.Characters
{
    /// <summary>
    /// Bone references for a humanoid. The rest pose is the identity rotation on every bone (arms hang down,
    /// character faces +Z), so animation code can write absolute local rotations.
    /// Filled by the editor model builder; a future imported model (e.g. Meshy) can fill the same fields.
    /// </summary>
    public sealed class HumanoidRig : MonoBehaviour
    {
        public Transform hips;
        public Transform spine;
        public Transform chest;
        public Transform neck;
        public Transform head;
        public Transform upperArmL, foreArmL, handL;
        public Transform upperArmR, foreArmR, handR;
        public Transform thighL, shinL, footL;
        public Transform thighR, shinR, footR;

        /// <summary>Hips rest position, captured on Awake, so bobbing is relative.</summary>
        public Vector3 HipsRestPosition { get; private set; }

        private void Awake() => HipsRestPosition = hips.localPosition;
    }
}
