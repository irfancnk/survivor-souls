using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// KAN-164.3 (KAN-239): the new projectile movers of the boss fight. Boomerang, Sine and Bounce warn with the fan's
    /// lanes (<see cref="BeginFan"/>) and fire one bolt per lane on the matching <see cref="BossProjectiles"/> mover. Wall:
    /// a row of bolts across the aim with one gap (clear width at least <see cref="MinWallGap"/>, off the Knight's line),
    /// warned by parallel lanes over every part of the row but the gap. Spikes: ground lines over a spread that burst at
    /// once after the warning (a pillar shortens a line). Each volley / cast hits the Knight once at most. No allocation
    /// per attack: fixed buffers.
    /// </summary>
    public sealed partial class BossFight
    {
        /// <summary>KAN-239: the Knight's body width on the ground (m): twice its ground radius.</summary>
        public const float KnightBodyWidth = 2f * BossBoulders.KnightRadius;
        /// <summary>KAN-239: the smallest clear gap of a wall (m): 1.5 x the Knight's body width.</summary>
        public const float MinWallGap = 1.5f * KnightBodyWidth;
        /// <summary>KAN-239: most bolts one wall fires.</summary>
        public const int MaxWallBolts = 32;
        /// <summary>KAN-239: wall bolt spacing in hit radii (below 2: no hole in the row but the gap).</summary>
        public const float WallSpacing = 1.8f;
        /// <summary>KAN-239: each side of the gap keeps at least this share of the wall width.</summary>
        public const float WallMinPart = 0.15f;
        private const float WallLaneWidth = 2.4f; // widest warning lane of a wall part (m)

        private readonly float[] _wallOffsets = new float[MaxWallBolts];
        private readonly float[] _wallLanes = new float[LineTelegraph.MaxLanes];
        private int _wallCount;
        private float _wallGapCentre, _wallGap;

        /// <summary>KAN-239: boomerang / sine / bounce bolts flew (boss ground point, bolts fired).</summary>
        public event Action<Vector3, int> MoversFired;
        /// <summary>KAN-239: a wall winds up (boss ground point, direction, gap centre: sideways offset, + = right).</summary>
        public event Action<Vector3, Vector3, float> WallWarned;
        /// <summary>KAN-239: the wall flew (boss ground point, bolts fired).</summary>
        public event Action<Vector3, int> WallFired;
        /// <summary>KAN-239: spike lines wind up (boss ground point, lines).</summary>
        public event Action<Vector3, int> SpikesWarned;
        /// <summary>KAN-239: the spikes burst (boss ground point, damage dealt; 0 = dodged). Lanes still show (<see cref="Lines"/>).</summary>
        public event Action<Vector3, float> SpikesStruck;

        /// <summary>KAN-239: gap centre of the last wall (sideways offset from the boss line, + = right).</summary>
        public float WallGapCentre => _wallGapCentre;
        /// <summary>KAN-239: clear gap width of the last wall (m).</summary>
        public float WallGap => _wallGap;
        /// <summary>KAN-239: bolts in the last wall.</summary>
        public int WallBolts => _wallCount;
        /// <summary>KAN-239: sideways offset of bolt <paramref name="i"/> of the last wall (m).</summary>
        public float WallOffset(int i) => _wallOffsets[Mathf.Clamp(i, 0, MaxWallBolts - 1)];

        /// <summary>Boomerang / Sine / Bounce: one bolt along each warned lane; neighbours bow / weave the other way.</summary>
        private void ReleaseMovers()
        {
            int count = Mathf.Min(lines.LaneCount, MaxRingBolts);
            float damage = DamageNow(_attack.damage);
            float wave = Mathf.Min(_attack.wave, _attack.width * 0.5f); // stays inside the warned lane
            float speed = BoltSpeed;
            projectiles.NewVolley(); // the whole cast hits once
            int fired = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = lines.LaneDirection(i);
                bool odd = (i & 1) == 1;
                bool ok;
                switch (_attack.type)
                {
                    case Move.Boomerang:
                        ok = projectiles.FireBoomerang(_attackOrigin, dir, speed, _attack.length, odd ? -wave : wave, damage,
                            _attack.radius);
                        break;
                    case Move.Sine:
                        ok = projectiles.FireSine(_attackOrigin, dir, speed, _attack.length, wave, _attack.wavelength,
                            odd ? Mathf.PI : 0f, damage, _attack.radius);
                        break;
                    default:
                        ok = projectiles.FireBounce(_attackOrigin, dir, speed, _attack.length, _attack.bounces, damage,
                            _attack.radius);
                        break;
                }
                if (ok) fired++;
            }
            lines.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            MoversFired?.Invoke(_attackOrigin, fired);
        }

        /// <summary>
        /// KAN-239: the gap centre of a wall (sideways offset, + = right): each side keeps <see cref="WallMinPart"/> of the
        /// wall, and the gap sits at least one gap width off the Knight's line (offset 0) when the wall is wide enough.
        /// <paramref name="t"/> 0..1 picks between the nearest and the farthest place on the side <paramref name="right"/>.
        /// </summary>
        public static float PickWallGap(float wallWidth, float hitRadius, float gapClear, float t, bool right)
        {
            float limit = Mathf.Max(0f, wallWidth * 0.5f - gapClear * 0.5f - hitRadius - WallMinPart * wallWidth);
            float away = Mathf.Min(gapClear, limit);
            float c = Mathf.Lerp(away, limit, Mathf.Clamp01(t));
            return right ? c : -c;
        }

        /// <summary>
        /// KAN-239: sideways offsets of a wall's bolts into <paramref name="buffer"/>; returns the count. Bolts run from one
        /// end of the wall to the other at most <see cref="WallSpacing"/> hit radii apart, except one gap at
        /// <paramref name="gapCentre"/>: the two bolts beside it stand <paramref name="gapClear"/> + 2 hit radii apart, so a
        /// Knight's centre inside the middle <paramref name="gapClear"/> m is never within a bolt's hit radius.
        /// </summary>
        public static int WallOffsets(float[] buffer, float wallWidth, float hitRadius, float gapClear, float gapCentre)
        {
            float r = Mathf.Max(0.05f, hitRadius);
            float half = wallWidth * 0.5f;
            float step = WallSpacing * r;
            int n = FillRow(buffer, 0, -half, gapCentre - gapClear * 0.5f - r, step);
            return FillRow(buffer, n, gapCentre + gapClear * 0.5f + r, half, step);
        }

        private static int FillRow(float[] buffer, int n, float from, float to, float step)
        {
            if (to < from) return n;
            int segments = Mathf.Max(1, Mathf.CeilToInt((to - from) / step - 0.0001f));
            for (int j = 0; j <= segments && n < buffer.Length; j++) buffer[n++] = Mathf.Lerp(from, to, j / (float)segments);
            return n;
        }

        /// <summary>
        /// KAN-239: warning lanes of a wall: parallel lanes over the danger band (bolts + hit radius) on both sides of the
        /// gap, never over the gap's clear width. Offsets into <paramref name="buffer"/>; returns the lane count.
        /// </summary>
        public static int WallLaneOffsets(float[] buffer, int capacity, float wallWidth, float hitRadius, float gapClear,
            float gapCentre, out float laneWidth)
        {
            float half = wallWidth * 0.5f;
            float aL = -half - hitRadius, bL = gapCentre - gapClear * 0.5f;
            float aR = gapCentre + gapClear * 0.5f, bR = half + hitRadius;
            float lenL = Mathf.Max(0.05f, bL - aL), lenR = Mathf.Max(0.05f, bR - aR);
            capacity = Mathf.Clamp(capacity, 2, buffer.Length);
            float w = Mathf.Min(WallLaneWidth, Mathf.Min(lenL, lenR));
            int kL, kR;
            while (true)
            {
                kL = Mathf.CeilToInt(lenL / w - 0.0001f);
                kR = Mathf.CeilToInt(lenR / w - 0.0001f);
                if (kL + kR <= capacity) break;
                w *= 1.15f; // too many lanes: wider ones (overlap inside a part, never across the gap unless forced)
            }
            laneWidth = w;
            int n = PlaceLanes(buffer, 0, aL, bL, kL, w);
            return PlaceLanes(buffer, n, aR, bR, kR, w);
        }

        private static int PlaceLanes(float[] buffer, int n, float from, float to, int k, float w)
        {
            float first = from + w * 0.5f, last = to - w * 0.5f;
            for (int j = 0; j < k; j++) buffer[n++] = k > 1 ? Mathf.Lerp(first, last, j / (float)(k - 1)) : (from + to) * 0.5f;
            return n;
        }

        private void BeginWall(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            float r = Mathf.Max(0.05f, a.radius);
            _wallGap = Mathf.Max(MinWallGap, a.gap);
            float width = Mathf.Clamp(a.width, _wallGap + 2f * r + 2f, (MaxWallBolts - 4) * WallSpacing * r);
            _wallGapCentre = PickWallGap(width, r, _wallGap, UnityEngine.Random.value, UnityEngine.Random.value < 0.5f);
            _wallCount = WallOffsets(_wallOffsets, width, r, _wallGap, _wallGapCentre);
            int lanes = WallLaneOffsets(_wallLanes, lines.Capacity, width, r, _wallGap, _wallGapCentre, out float laneWidth);
            lines.ShowRow(_attackOrigin, _attackDir, _wallLanes, lanes, a.length, laneWidth);
            for (int i = 0; i < lines.LaneCount; i++) // a lane ends where its bolts will: the ring wall or a pillar
                lines.SetLaneLength(i, FreeRun(lines.LaneStart(i), _attackDir, a.length, BoltClearRadius, BoltProbeStep));
            BeginWindUp(index);
            WallWarned?.Invoke(_attackOrigin, _attackDir, _wallGapCentre);
        }

        private void ReleaseWall()
        {
            Vector3 side = new Vector3(_attackDir.z, 0f, -_attackDir.x);
            float damage = DamageNow(_attack.damage);
            float speed = BoltSpeed;
            projectiles.NewVolley(); // the whole wall hits once
            int fired = 0;
            for (int i = 0; i < _wallCount; i++)
                if (projectiles.Fire(_attackOrigin + side * _wallOffsets[i], _attackDir, speed, _attack.length, damage,
                    _attack.radius)) fired++;
            lines.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            WallFired?.Invoke(_attackOrigin, fired);
        }

        private void BeginSpikes(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            lines.ShowFan(_attackOrigin, _attackDir, a.count, a.spread, a.length, a.width);
            for (int i = 0; i < lines.LaneCount; i++) // spikes do not burst through a pillar
                lines.SetLaneLength(i, LaneLength(_attackOrigin, lines.LaneDirection(i), a.length));
            BeginWindUp(index);
            SpikesWarned?.Invoke(_attackOrigin, lines.LaneCount);
        }

        private void StrikeSpikes()
        {
            float dealt = 0f;
            if (player != null && !player.IsDead)
            {
                Vector3 p = player.transform.position;
                for (int i = 0; i < lines.LaneCount; i++)
                {
                    if (!InLane(_attackOrigin, lines.LaneDirection(i), lines.LaneLength(i), _attack.width, p)) continue;
                    dealt = HitKnight(_attack.damage); // one hit for the whole cast
                    break;
                }
            }
            BeginRecover(_attack.recover, _attack.cooldown);
            SpikesStruck?.Invoke(_attackOrigin, dealt); // before the flash: listeners read the lanes
            lines.Flash();
        }
    }
}
