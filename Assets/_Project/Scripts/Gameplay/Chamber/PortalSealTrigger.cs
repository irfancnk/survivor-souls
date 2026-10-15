namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// "Knight touches the sealed portal" zone (KAN-49). Plain logic, no Unity objects (tests drive it by hand).
    /// Rule: fire on enter, re-arm on exit. The zone has hysteresis (enter radius, larger exit radius), so a Knight that
    /// stands on the border never flickers in and out. An entry less than <see cref="MinInterval"/> seconds after the last
    /// fire is eaten (no fire until the Knight leaves and comes back), so a quick step out and in does not spam the bubble,
    /// the thud and the haptic.
    /// </summary>
    public sealed class PortalSealTrigger
    {
        public float EnterRadius = 2.75f;
        public float ExitRadius = 3.5f;
        public float MinInterval = 1.5f;

        private bool _inside;
        private float _sinceFire = float.PositiveInfinity;

        /// <summary>The Knight is in the zone (with hysteresis).</summary>
        public bool Inside => _inside;

        /// <summary>
        /// One frame. <paramref name="sqrDistance"/> = squared ground distance Knight to zone centre; <paramref name="armed"/>
        /// = the portal is sealed and the Knight can touch it (false clears the zone). Returns true on the frame the
        /// bubble should show.
        /// </summary>
        public bool Step(float sqrDistance, float dt, bool armed)
        {
            _sinceFire += dt;
            if (!armed)
            {
                _inside = false;
                return false;
            }
            float r = _inside ? ExitRadius : EnterRadius;
            bool inside = sqrDistance <= r * r;
            bool entered = inside && !_inside;
            _inside = inside;
            if (!entered || _sinceFire < MinInterval) return false;
            _sinceFire = 0f;
            return true;
        }

        /// <summary>New portal state / pooled tile re-enabled: the Knight counts as outside, the interval is cleared.</summary>
        public void Reset()
        {
            _inside = false;
            _sinceFire = float.PositiveInfinity;
        }
    }
}
