using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Where the gate arrow points (KAN-63). No visuals of its own: the HUD draws the arrow on the screen edge
    /// (<c>UI.Hud.GateArrowHud</c>); nothing is drawn on the ground under the Knight any more.
    /// On the shore it points to the exit door of the streamed world: dim while the portal is locked, bright once the
    /// Chamber timer has finished. In the boss arena (KAN-59) it points to the arena portal instead, and only once that
    /// portal is open (<see cref="PointAt"/>): during the fight the way out is shut, so the arrow would only distract.
    /// Disabled by <c>PlayerDeath</c>: a disabled component has no target. No Update, no allocation.
    /// </summary>
    public sealed class ObjectiveArrow : MonoBehaviour
    {
        [SerializeField] private WorldStreamer world;
        [Tooltip("No target closer than this (m): the Knight is at the gate.")]
        [SerializeField, Min(0f)] private float hideDistance = 4f;

        private PortalGate _gate;
        private bool _gated;

        /// <summary>Gate point on the ground (arch centre).</summary>
        public Vector3 Target =>
            _gated ? (_gate != null ? _gate.TriggerPosition : transform.position) : world != null ? world.ExitPosition : transform.position;

        /// <summary>Ground distance from the Knight to <see cref="Target"/> (m).</summary>
        public float Distance
        {
            get
            {
                Vector3 to = Target - transform.position;
                to.y = 0f;
                return to.magnitude;
            }
        }

        /// <summary>The way out is open: arena portal open, or the shore Chamber's timer finished (no timer = open).</summary>
        public bool Open
        {
            get
            {
                if (_gated) return _gate != null && _gate.State == PortalState.Open;
                var timer = SurvivalTimer.Active;
                return timer == null || timer.IsFinished;
            }
        }

        /// <summary>Whether the HUD should point at <see cref="Target"/> this frame.</summary>
        public bool HasTarget => isActiveAndEnabled && (!_gated || Open) && (_gated || world != null) && Distance > hideDistance;

        /// <summary>Points at <paramref name="gate"/> (boss arena) and shows only while it is open. Null = back to the world exit.</summary>
        public void PointAt(PortalGate gate)
        {
            _gate = gate;
            _gated = gate != null;
        }

        /// <summary>Tests: wiring without the builder.</summary>
        public void Setup(WorldStreamer worldStreamer) => world = worldStreamer;
    }
}
