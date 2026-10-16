using System;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// KAN-164.2: a back-to-back attack pair of a boss (indices into <see cref="BossData.Attacks"/>). When the
    /// <see cref="first"/> attack ends in phase 2 with no summon alive, <see cref="BossFight"/> may start <see cref="second"/>
    /// after <see cref="BossData.ComboGap"/> s instead of the normal cooldown. The partner still warns on its own (never under
    /// <see cref="BossData.MinTelegraph"/>), so a combo is two dodgeable attacks in a row, not one wall.
    /// </summary>
    [Serializable]
    public struct BossCombo
    {
        public int first;
        public int second;

        public BossCombo(int first, int second)
        {
            this.first = first;
            this.second = second;
        }

        /// <summary>
        /// Moves that fill an area for a while (bolt spirals, meteor rain, shockwave rings, live beams). Two of them back to
        /// back leave too little safe ground, so a fair combo has at most one.
        /// </summary>
        public static bool FillsArea(BossFight.Move move) =>
            move == BossFight.Move.Spiral || move == BossFight.Move.Rain || move == BossFight.Move.Shockwave || move == BossFight.Move.Beams;

        /// <summary>
        /// Fair pair (KAN-164 "always dodgeable"): both indices valid, two different attacks, no summon (the Great Summon is its
        /// own event, KAN-164.1), no barrage side cast, the partner on in phase 2, and not two area fillers.
        /// </summary>
        public static bool IsFair(BossAttack[] list, in BossCombo combo)
        {
            if (list == null || combo.first == combo.second) return false;
            if (combo.first < 0 || combo.second < 0 || combo.first >= list.Length || combo.second >= list.Length) return false;
            var a = list[combo.first];
            var b = list[combo.second];
            if (a.type == BossFight.Move.Summon || b.type == BossFight.Move.Summon) return false;
            if (a.type == BossFight.Move.Barrage || b.type == BossFight.Move.Barrage) return false;
            if (!b.InPhase(2)) return false;
            return !(FillsArea(a.type) && FillsArea(b.type));
        }
    }
}
