using UnityEngine;

namespace SurvivorSouls.FX
{
    /// <summary>
    /// Sends this transform's position to the grass shader (global <c>_SS_GrassPusher</c>), so blades
    /// bend away from the character. One pusher per scene (the player). Cost: one global vector per frame.
    /// </summary>
    public sealed class GrassPusher : MonoBehaviour
    {
        private static readonly int PusherId = Shader.PropertyToID("_SS_GrassPusher");

        [SerializeField, Min(0.1f)] private float radius = 0.75f;

        private void LateUpdate()
        {
            Vector3 p = transform.position;
            Shader.SetGlobalVector(PusherId, new Vector4(p.x, p.y, p.z, radius));
        }

        private void OnDisable() => Shader.SetGlobalVector(PusherId, Vector4.zero);
    }
}
