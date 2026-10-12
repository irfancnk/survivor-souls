using UnityEngine;

namespace SurvivorSouls.Gameplay.Characters
{
    /// <summary>
    /// Bone references for a humanoid, for any rest pose (procedural T-ish pose or an imported Meshy rig).
    /// Animation code poses bones with <see cref="SetRotation"/>: rotations are given in CHARACTER space
    /// (this transform's axes: +X right, +Y up, +Z forward) as an offset from the rest pose, so the same
    /// animation works on rigs whose bones have arbitrary local axes.
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

        /// <summary>Hips rest position (parent space), captured on Awake, so bobbing is relative.</summary>
        public Vector3 HipsRestPosition { get; private set; }

        private Transform[] _bones;
        private Quaternion[] _restLocal;
        private Quaternion[] _restCharacter; // bone rotation relative to this transform at rest

        private void Awake()
        {
            HipsRestPosition = hips.localPosition;
            _bones = new[] { hips, spine, chest, neck, head, upperArmL, foreArmL, handL, upperArmR, foreArmR, handR, thighL, shinL, footL, thighR, shinR, footR };
            _restLocal = new Quaternion[_bones.Length];
            _restCharacter = new Quaternion[_bones.Length];
            var inv = Quaternion.Inverse(transform.rotation);
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                _restLocal[i] = _bones[i].localRotation;
                _restCharacter[i] = inv * _bones[i].rotation;
            }
        }

        /// <summary>
        /// Rotates <paramref name="bone"/> by <paramref name="characterSpaceDelta"/> relative to its rest pose,
        /// on top of whatever its parents do. Deltas compose down a chain like local rotations.
        /// </summary>
        public void SetRotation(Transform bone, Quaternion characterSpaceDelta)
        {
            int i = System.Array.IndexOf(_bones, bone);
            if (i < 0 || bone == null) return;
            var rest = _restCharacter[i];
            bone.localRotation = _restLocal[i] * (Quaternion.Inverse(rest) * characterSpaceDelta * rest);
        }
    }
}
