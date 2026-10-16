using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// KAN-163: boss hit damage with the phase 2 factor (<see cref="BossData.DamageFor(float, float, int)"/>) and boss
    /// telemetry. Every boss hit on the Knight raises <see cref="KnightHit"/> and tracks
    /// <see cref="GameTelemetry.BossHitTaken"/> (attack label, share %, health lost). Direct hits carry the attack label;
    /// boss bolts carry <see cref="BoltLabel"/> and boulders <see cref="BoulderLabel"/> (they land after the attack ended).
    /// The end of the fight tracks <see cref="GameTelemetry.BossFightResult"/> once: "win" on the kill, "death" when the
    /// Knight dies while fighting, with the fight seconds (<see cref="FightTime"/>, scaled, after the intro). No allocation per hit.
    /// </summary>
    public sealed partial class BossFight
    {
        public const string BoltLabel = "Bolt";
        public const string BoulderLabel = "Boulder";
        public const string ResultWin = "win";
        public const string ResultDeath = "death";

        // Copies made at type init: a string literal in a method body is interned when that method is first compiled, which
        // would allocate on the first boss hit of the run (no-GC-per-frame tests).
        private static readonly string HitEvent = GameTelemetry.BossHitTaken;
        private static readonly string ResultEvent = GameTelemetry.BossFightResult;
        private static readonly string BoltName = BoltLabel;
        private static readonly string BoulderName = BoulderLabel;

        /// <summary>A boss hit the Knight: attack label, health lost (above 0).</summary>
        public event Action<string, float> KnightHit;

        /// <summary>The fight ended: "win" or "death", fight seconds. Once per fight.</summary>
        public event Action<string, float> FightEnded;

        private bool _fightStarted;
        private bool _resultTracked;
        private BossProjectiles _trackedBolts;
        private BossBoulders _trackedBoulders;
        private PlayerHealth _trackedKnight;
        private Action<Vector3, float> _onBoltHit, _onBoulderLanded;
        private Action _onKnightDeath;

        /// <summary>Damage now of a hit worth <paramref name="share"/> of max health: phase 2 factor, then the cap.</summary>
        private float DamageNow(float share) => data.DamageFor(share, player != null ? player.Max : 0f, Phase);

        /// <summary>Deals a direct hit of the running attack to the Knight (caller checked the Knight is alive and inside).</summary>
        private float HitKnight(float share)
        {
            float dealt = player.TakeDamage(DamageNow(share));
            ReportHit(_attack.label, dealt);
            return dealt;
        }

        private void ReportHit(string label, float dealt)
        {
            if (dealt <= 0f) return;
            KnightHit?.Invoke(label, dealt);
            float max = player != null ? player.Max : 0f;
            int sharePercent = max > 0f ? Mathf.RoundToInt(dealt / max * 100f) : 0;
            Vector3 at = player != null ? player.transform.position : Vector3.zero;
            GameTelemetry.Track(new TelemetryEvent(HitEvent, label, sharePercent, Mathf.RoundToInt(dealt),
                data != null ? data.DisplayName : null, Time.timeSinceLevelLoad, at));
        }

        private void OnStageTelemetry(Stage stage)
        {
            switch (stage)
            {
                case Stage.Waiting:
                    _fightStarted = false;
                    _resultTracked = false;
                    break;
                case Stage.Fighting:
                    if (!_fightStarted) StartFightTelemetry();
                    break;
                case Stage.Defeated:
                    TrackResult(ResultWin);
                    StopFightTelemetry();
                    break;
            }
        }

        private void StartFightTelemetry()
        {
            _fightStarted = true;
            _resultTracked = false;
            _onBoltHit ??= (_, dealt) => ReportHit(BoltName, dealt);
            _onBoulderLanded ??= (_, dealt) => ReportHit(BoulderName, dealt);
            _onKnightDeath ??= OnKnightDeath;
            StopFightTelemetry();
            _trackedBolts = projectiles;
            _trackedBoulders = Boulders;
            _trackedKnight = player;
            if (_trackedBolts != null) _trackedBolts.HitPlayer += _onBoltHit;
            if (_trackedBoulders != null) _trackedBoulders.Landed += _onBoulderLanded;
            if (_trackedKnight != null) _trackedKnight.OnDeath += _onKnightDeath;
        }

        private void StopFightTelemetry()
        {
            if (_trackedBolts != null) _trackedBolts.HitPlayer -= _onBoltHit;
            if (_trackedBoulders != null) _trackedBoulders.Landed -= _onBoulderLanded;
            if (_trackedKnight != null) _trackedKnight.OnDeath -= _onKnightDeath;
            _trackedBolts = null;
            _trackedBoulders = null;
            _trackedKnight = null;
        }

        private void OnKnightDeath()
        {
            if (Current != Stage.Fighting) return;
            TrackResult(ResultDeath);
            StopFightTelemetry();
        }

        private void TrackResult(string result)
        {
            if (_resultTracked || !_fightStarted) return;
            _resultTracked = true;
            float seconds = FightTime;
            FightEnded?.Invoke(result, seconds);
            Vector3 at = player != null ? player.transform.position : Vector3.zero;
            GameTelemetry.Track(new TelemetryEvent(ResultEvent, data != null ? data.DisplayName : null, Phase,
                Mathf.RoundToInt(seconds), result, Time.timeSinceLevelLoad, at));
        }
    }
}
