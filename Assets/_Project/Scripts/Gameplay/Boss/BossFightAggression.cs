using System;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// KAN-164.2 aggression model of a boss fight (numbers on <see cref="BossData"/>; defaults = off, the old fight):
    /// <list type="bullet">
    /// <item><b>Alone</b> (no summon of this boss alive, <see cref="SummonsAlive"/> 0): cooldowns x
    /// <see cref="BossData.AloneCooldownScale"/>, so a boss with an empty arena pushes hard. While summons live the normal
    /// cooldown holds (the KAN-164.1 Great Summon ease-off plugs in here).</item>
    /// <item><b>Combos</b> (phase 2, alone): when an opener of <see cref="BossData.Combos"/> ends, a roll against
    /// <see cref="BossData.ComboChance"/> chains its partner after <see cref="BossData.ComboGap"/> s instead of the cooldown.
    /// The partner must be fair (<see cref="BossCombo.IsFair"/>), on in this phase, in range and usable; else the normal pick
    /// runs. It warns as usual (never under 0.6 s).</item>
    /// <item><b>Enrage</b> (phase 3, never before phase 2) at <see cref="BossData.Phase3At"/> health: cooldowns x
    /// <see cref="BossData.EnrageCooldownScale"/>, bolts / orbs / shockwave rings x <see cref="BossData.EnrageBoltSpeed"/>, walk
    /// x <see cref="BossData.EnrageWalk"/>, the higher combo chance, a roar (<see cref="BossData.EnrageRoar"/>) and
    /// <see cref="Enraged"/> (aura, sound: <see cref="BossEnrageFx"/>). <see cref="Phase"/> stays 2: phase 2 attacks, damage and
    /// wind-ups hold; <see cref="IsEnraged"/> is the third step.</item>
    /// </list>
    /// No allocation per frame or per attack.
    /// </summary>
    public sealed partial class BossFight
    {
        /// <summary>KAN-164.2: the boss enraged (phase 3). Once per fight.</summary>
        public event Action Enraged;
        /// <summary>KAN-164.2: a combo partner started (opener index, partner index into <see cref="Attacks"/>).</summary>
        public event Action<int, int> ComboStarted;

        private bool _enraged;
        private bool _roarRecover;   // the recovery running is a roar (phase 2 / enrage): it opens no combo
        private int _comboNext = -1; // partner to start at the next pick (-1 = none)
        private int _comboOpener = -1;
        private int _combos;

        /// <summary>KAN-164.2: the boss is enraged (phase 3: under <see cref="BossData.Phase3At"/> health, after phase 2).</summary>
        public bool IsEnraged => _enraged;
        /// <summary>KAN-164.2: no summon of this boss is alive (full aggression).</summary>
        public bool Alone => SummonsAlive == 0;
        /// <summary>KAN-164.2: combo partners started this fight.</summary>
        public int CombosStarted => _combos;
        /// <summary>KAN-164.2: attack index chained for the next pick (-1 = none).</summary>
        public int ComboPending => _comboNext;
        /// <summary>KAN-164.2: chase seconds left before the next pick (tests, dev).</summary>
        public float CooldownLeft => _cooldown;

        /// <summary>Speed of the running attack's bolts, orbs and rings (faster while enraged).</summary>
        private float BoltSpeed => data.BoltSpeedFor(_attack.speed, _enraged);

        private void ResetAggression()
        {
            _enraged = false;
            _roarRecover = false;
            _comboNext = -1;
            _comboOpener = -1;
            _combos = 0;
        }

        /// <summary>
        /// Chase time after an attack worth <paramref name="baseCooldown"/> s: the phase 2 factor, then the aggression factor.
        /// An opener of a fair combo may chain its partner (then the gap, if shorter).
        /// </summary>
        private float NextCooldown(float baseCooldown)
        {
            _comboNext = -1;
            bool alone = SummonsAlive == 0;
            float cooldown = data.CooldownFor(baseCooldown, Phase) * data.AggressionScale(alone, _enraged);
            bool roar = _roarRecover;
            _roarRecover = false;
            if (roar || !alone || _index < 0 || player == null || player.IsDead) return cooldown;
            float chance = data.ComboChanceFor(Phase, _enraged);
            if (chance <= 0f) return cooldown;
            int partner = ComboPartner(_index);
            if (partner < 0 || (chance < 1f && UnityEngine.Random.value >= chance)) return cooldown;
            _comboNext = partner;
            _comboOpener = _index;
            return Mathf.Min(cooldown, data.ComboGap);
        }

        /// <summary>First fair partner of opener <paramref name="index"/> that is on in this phase and usable; -1 = none.</summary>
        private int ComboPartner(int index)
        {
            var combos = data.Combos;
            for (int i = 0; i < combos.Length; i++)
            {
                var c = combos[i];
                if (c.first != index || !BossCombo.IsFair(_attacks, c)) continue;
                if (_attacks[c.second].InPhase(Phase) && CanUse(c.second)) return c.second;
            }
            return -1;
        }

        /// <summary>Starts the chained partner, if it still fits (alone, in phase, in range, usable). True = it began.</summary>
        private bool TryCombo(float distance)
        {
            int next = _comboNext;
            _comboNext = -1;
            if (next < 0 || next >= _attacks.Length || SummonsAlive > 0) return false;
            var a = _attacks[next];
            if (!a.InPhase(Phase) || !a.InRange(distance) || !CanUse(next) || !Begin(next)) return false;
            _combos++;
            ComboStarted?.Invoke(_comboOpener, next);
            return true;
        }

        /// <summary>The boss stands and roars for <paramref name="seconds"/> (phase 2 and the enrage); the next attack waits.</summary>
        private void BeginRoar(float seconds)
        {
            FaceKnight();
            _boss.PlayAction(data.RoarClip);
            BeginRecover(seconds, RoarCooldown, RecoverPose.Keep);
            _roarRecover = true;
        }

        /// <summary>Enrage once the boss (in phase 2) falls to <see cref="BossData.Phase3At"/> health.</summary>
        private void CheckEnrage(Enemy enemy)
        {
            if (_enraged || Phase < 2 || data.Phase3At <= 0f || enemy.Health > enemy.MaxHealth * data.Phase3At) return;
            _enraged = true;
            if (_boss != null) _boss.SpeedBoost = data.Phase2Speed * data.EnrageWalk;
            if (data.EnrageRoar > 0f && Current == Stage.Fighting && CurrentAttack == Attack.Chase && player != null && !player.IsDead)
                BeginRoar(data.EnrageRoar);
            Enraged?.Invoke();
        }
    }
}
