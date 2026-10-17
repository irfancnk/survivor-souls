using System;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Shared proc rules of the Ember Boons (KAN-246): one place that says what a hit may set off.
    /// <list type="bullet">
    /// <item><b>Weapon hits</b> are the hits the shooter reports (<see cref="AutoShooter.EnemyHit"/>): main volley bolts,
    /// their pierces, bounces and wall rebounds. Only these count for weapon-hit counters (Frost Bolts, Stormshock,
    /// Repelling Bolts, Venom, Storm Touch, Sovereign's Fall crits, Grave Choir kills).</item>
    /// <item><b>Secondary damage</b> (arcs, chain lightning, bursts, orbitals, echoes, summons, pulses) calls
    /// <see cref="Enemy.TakeDamage"/> directly or flies a bolt launched with <see cref="AutoShooter.LaunchSecondary"/>, so it
    /// never reaches <see cref="AutoShooter.EnemyHit"/>: it cannot trigger a weapon-hit skill, and nothing triggers itself.</item>
    /// <item><b>No kill chains</b>: damage done inside <see cref="NoKillChains"/> (Second Dawn's shockwave, Soul Burst's own
    /// bursts) cannot start an on-kill skill (Soul Burst). On-kill listeners check <see cref="KillsChain"/> inside the
    /// <see cref="Enemy.Damaged"/> call.</item>
    /// </list>
    /// Main thread only (all combat runs there). No allocation: the scope is a struct.
    /// </summary>
    public static class ProcGuard
    {
        private static int _noChainDepth;

        /// <summary>False while damage inside a <see cref="NoKillChains"/> scope resolves: its kills start no on-kill skill.</summary>
        public static bool KillsChain => _noChainDepth == 0;

        /// <summary>
        /// Opens a scope whose kills start no on-kill skill. Use with <c>using (ProcGuard.NoKillChains()) { ... }</c>.
        /// Scopes nest.
        /// </summary>
        public static Scope NoKillChains()
        {
            _noChainDepth++;
            return new Scope(true);
        }

        /// <summary>Resets the guard (tests, scene load). A scope left open by an exception cannot block chains for good.</summary>
        public static void Reset() => _noChainDepth = 0;

        public readonly struct Scope : IDisposable
        {
            private readonly bool _open;

            internal Scope(bool open) => _open = open;

            public void Dispose()
            {
                if (_open && _noChainDepth > 0) _noChainDepth--;
            }
        }
    }
}
