using SurvivorSouls.Gameplay.Boss;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Shared ground warnings of the ranged zombies (KAN-105): the same red <see cref="LineTelegraph"/> lanes (aim lines) and
    /// <see cref="GroundTelegraph"/> circles (landing spots) as the boss, built once by the builder and rented per attack, so a
    /// pooled enemy carries no quads of its own. A rented warning belongs to the renter until <see cref="Release"/>; a released
    /// one still plays its hit flash and is lent again only once it is hidden. None free = <c>null</c>: the attack waits (it
    /// never fires without its warning).
    /// On the "Enemy Shots" object (RangedEnemyBuilder).
    /// </summary>
    public sealed class EnemyTelegraphs : MonoBehaviour
    {
        /// <summary>The scene's warning pool (ranged attacks find it here). Set while enabled.</summary>
        public static EnemyTelegraphs Current { get; private set; }

        [SerializeField] private LineTelegraph[] lines = new LineTelegraph[0];
        [SerializeField] private GroundTelegraph[] circles = new GroundTelegraph[0];

        private bool[] _lineRented;
        private bool[] _circleRented;

        public int LineCapacity => lines.Length;
        public int CircleCapacity => circles.Length;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(LineTelegraph[] laneWarnings, GroundTelegraph[] circleWarnings)
        {
            lines = laneWarnings ?? new LineTelegraph[0];
            circles = circleWarnings ?? new GroundTelegraph[0];
            Allocate();
        }

        private void Awake() => Allocate();

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        private void Allocate()
        {
            if (_lineRented == null || _lineRented.Length != lines.Length) _lineRented = new bool[lines.Length];
            if (_circleRented == null || _circleRented.Length != circles.Length) _circleRented = new bool[circles.Length];
        }

        /// <summary>A free, hidden lane warning; null when every one is in use.</summary>
        public LineTelegraph RentLine()
        {
            Allocate();
            for (int i = 0; i < lines.Length; i++)
            {
                if (_lineRented[i] || lines[i] == null || lines[i].IsShowing) continue;
                _lineRented[i] = true;
                return lines[i];
            }
            return null;
        }

        /// <summary>A free, hidden circle warning; null when every one is in use.</summary>
        public GroundTelegraph RentCircle()
        {
            Allocate();
            for (int i = 0; i < circles.Length; i++)
            {
                if (_circleRented[i] || circles[i] == null || circles[i].IsShowing) continue;
                _circleRented[i] = true;
                return circles[i];
            }
            return null;
        }

        /// <summary>Free circle warnings right now (an attack with several landing spots checks before it starts).</summary>
        public int FreeCircles()
        {
            Allocate();
            int n = 0;
            for (int i = 0; i < circles.Length; i++)
                if (!_circleRented[i] && circles[i] != null && !circles[i].IsShowing) n++;
            return n;
        }

        /// <summary>Gives a lane warning back (its flash, if any, keeps playing).</summary>
        public void Release(LineTelegraph line)
        {
            if (line == null) return;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i] == line) _lineRented[i] = false;
        }

        /// <summary>Gives a circle warning back (its flash, if any, keeps playing).</summary>
        public void Release(GroundTelegraph circle)
        {
            if (circle == null) return;
            for (int i = 0; i < circles.Length; i++)
                if (circles[i] == circle) _circleRented[i] = false;
        }

        /// <summary>Lane warnings rented right now (tests).</summary>
        public int RentedLines()
        {
            Allocate();
            int n = 0;
            foreach (bool r in _lineRented) if (r) n++;
            return n;
        }

        /// <summary>Circle warnings rented right now (tests).</summary>
        public int RentedCircles()
        {
            Allocate();
            int n = 0;
            foreach (bool r in _circleRented) if (r) n++;
            return n;
        }
    }
}
