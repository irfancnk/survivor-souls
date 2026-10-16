using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Marks a collider (or a parent of colliders) as a footstep surface (KAN-235). <see cref="FootstepSounds"/> casts a ray
    /// down at each step and reads this tag on the ground it hits; untagged ground uses the component's fallback surface
    /// (snow in Realm 1). Builders put it on a gate plaza, a bridge, the boss arena floor ...
    /// </summary>
    public sealed class FootstepSurfaceTag : MonoBehaviour
    {
        [SerializeField] private FootstepSurface surface = FootstepSurface.Snow;

        public FootstepSurface Surface
        {
            get => surface;
            set => surface = value;
        }
    }
}
