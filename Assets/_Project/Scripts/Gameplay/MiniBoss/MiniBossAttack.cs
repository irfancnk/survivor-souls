using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.MiniBosses
{
    /// <summary>
    /// The mini boss's own telegraphed attack (KAN-212 = KAN-23.2), so it plays differently from a big zombie of its type.
    /// <see cref="MiniBossDirector"/> calls <see cref="Begin"/> at the rise and <see cref="Stop"/> when the mini boss is gone.
    /// Loop: Idle (<see cref="MiniBossData.FirstAttackDelay"/>, then <see cref="MiniBossData.AttackCooldown"/>; the Knight
    /// within <see cref="MiniBossData.AttackRange"/> m) -> WindUp (the mini boss halts, plays <see cref="MiniBossData.AttackClip"/>
    /// and its warning fills for <see cref="MiniBossData.AttackWindUp"/> s, never under <see cref="MiniBossData.MinWindUp"/>)
    /// -> Strike -> Recover (<see cref="MiniBossData.AttackRecover"/> s standing: the punish window) -> Idle.
    /// <list type="bullet">
    /// <item>Slam: a red circle (<see cref="slamWarning"/>) of <see cref="MiniBossData.SlamRadius"/> m round the mini boss
    /// fills; at the hit it flashes and a Knight inside the circle loses <see cref="MiniBossData.AttackDamage"/> of its max
    /// health; camera shake.</item>
    /// <item>Charge: a red lane (<see cref="chargeLane"/>) towards the Knight (direction locked at the start, cut by walls and
    /// pillars) fills; then the mini boss dashes along it in <see cref="MiniBossData.ChargeTime"/> s. A Knight inside the lane
    /// (half its width) when the body passes is hit once; small shake at the stop.</item>
    /// </list>
    /// The type's own attacks (<see cref="SpecialAttack"/>: the Grave Brute's ring slam, KAN-106) are switched off while this
    /// attack runs and never start one together with it. Own telegraph instances (never the shared pool, so a crowded
    /// screen cannot block it). No allocation per frame. On the "MiniBoss" object (MiniBossBuilder).
    /// </summary>
    public sealed class MiniBossAttack : MonoBehaviour
    {
        public enum Phase : byte { Off, Idle, WindUp, Strike, Recover }

        private const float ChargeStep = 0.25f;  // wall probe step (m) of the lane cut
        private const float MinCharge = 1.5f;    // a shorter free lane = no charge this time
        private const float RetryDelay = 0.4f;   // busy type attack / no room: look again soon
        private const float TurnSpeed = 720f;    // degrees per second while it winds up

        [Tooltip("Red circle of the slam (own instance).")]
        [SerializeField] private GroundTelegraph slamWarning;
        [Tooltip("Red lane of the charge (own instance, one lane).")]
        [SerializeField] private LineTelegraph chargeLane;
        [SerializeField] private TopDownFollowCamera followCamera;
        [SerializeField, Min(0f)] private float slamShake = 0.2f;
        [SerializeField, Min(0f)] private float slamHitShake = 0.3f;
        [SerializeField, Min(0f)] private float chargeStopShake = 0.12f;
        [SerializeField, Min(0.05f)] private float shakeTime = 0.3f;

        /// <summary>The wind-up started: kind, centre (slam) or lane start (charge).</summary>
        public event Action<MiniBossAttackKind, Vector3> WindUpStarted;
        /// <summary>The attack struck (slam hit, charge take-off): kind, where.</summary>
        public event Action<MiniBossAttackKind, Vector3> Struck;
        /// <summary>The attack hit the Knight: damage dealt.</summary>
        public event Action<float> HitKnight;

        private static readonly string StandPose = Enemy.StandState; // static copy: no literal interning in the hit path

        private readonly List<SpecialAttack> _typeAttacks = new List<SpecialAttack>(2);
        private MiniBossData _data;
        private Enemy _boss;
        private PlayerHealth _knight;
        private MiniBossAttackKind _kind;
        private float _time;
        private float _cooldown;
        private Vector3 _origin;
        private Vector3 _dir = Vector3.forward;
        private float _length;
        private float _groundY;
        private bool _hit;
        private bool _typeOff;

        public Phase State { get; private set; } = Phase.Off;
        public MiniBossAttackKind Kind => _kind;
        /// <summary>Seconds left before the next attack may start (Idle).</summary>
        public float Cooldown => _cooldown;
        /// <summary>Attacks started since <see cref="Begin"/>.</summary>
        public int Attacks { get; private set; }
        /// <summary>The current / last attack hit the Knight.</summary>
        public bool HitThisAttack => _hit;
        /// <summary>Slam centre or charge lane start of the current / last attack.</summary>
        public Vector3 Origin => _origin;
        /// <summary>Charge direction (locked at the wind-up start).</summary>
        public Vector3 Direction => _dir;
        /// <summary>Charge length (m) after the wall cut.</summary>
        public float ChargeLength => _length;
        /// <summary>Wind-up 0..1 (1 from the hit on, 0 while idle).</summary>
        public float Progress => State == Phase.WindUp ? Mathf.Clamp01(_time / _data.AttackWindUp) : State == Phase.Idle || State == Phase.Off ? 0f : 1f;
        public GroundTelegraph SlamWarning => slamWarning;
        public LineTelegraph ChargeLane => chargeLane;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(GroundTelegraph slam, LineTelegraph lane, TopDownFollowCamera cameraRig = null)
        {
            slamWarning = slam;
            chargeLane = lane;
            followCamera = cameraRig;
        }

        /// <summary>
        /// Starts the attack loop on the risen <paramref name="boss"/> with <paramref name="data"/>'s attack against
        /// <paramref name="knight"/>. No effect with <see cref="MiniBossAttackKind.None"/> or a missing warning.
        /// </summary>
        public void Begin(Enemy boss, MiniBossData data, PlayerHealth knight)
        {
            Stop();
            if (boss == null || data == null || knight == null) return;
            _kind = data.Attack;
            if (_kind == MiniBossAttackKind.Slam && slamWarning == null) return;
            if (_kind == MiniBossAttackKind.Charge && chargeLane == null) return;
            if (_kind == MiniBossAttackKind.None) return;
            _boss = boss;
            _data = data;
            _knight = knight;
            boss.GetComponents(_typeAttacks);
            _cooldown = data.FirstAttackDelay;
            Attacks = 0;
            _hit = false;
            State = Phase.Idle;
        }

        /// <summary>Ends the loop: hides the warning, gives the type's attacks back, the body walks again.</summary>
        public void Stop()
        {
            Cancel();
            State = Phase.Off;
            _boss = null;
            _data = null;
            _knight = null;
            _typeAttacks.Clear();
        }

        /// <summary>Starts the attack now (dev shots, tests). False when off, busy or there is no room.</summary>
        public bool ForceAttack()
        {
            if (State != Phase.Idle || !Ready()) return false;
            return TryStart();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f || State == Phase.Off) return;
            if (_boss == null || !_boss.IsAlive || !_boss.gameObject.activeSelf)
            {
                Stop();
                return;
            }
            if (_knight == null || _knight.IsDead)
            {
                Cancel();
                return;
            }
            if (_boss.IsFrozenSolid) return; // KAN-91: frozen = the attack pauses
            if (State == Phase.Idle)
            {
                if (_cooldown > 0f) _cooldown -= dt;
                if (_cooldown <= 0f && Ready()) TryStart();
                return;
            }
            _time += dt;
            switch (State)
            {
                case Phase.WindUp:
                    WindUp(dt);
                    break;
                case Phase.Strike:
                    Dash();
                    break;
                case Phase.Recover:
                    if (_time >= _data.AttackRecover) End();
                    break;
            }
        }

        /// <summary>The Knight is in range and no type attack of the body is busy.</summary>
        private bool Ready()
        {
            if (SpecialAttack.Planar(_knight.transform.position, _boss.transform.position) > _data.AttackRange) return false;
            for (int i = 0; i < _typeAttacks.Count; i++)
            {
                var a = _typeAttacks[i];
                if (a != null && a.enabled && a.CurrentPhase != SpecialAttack.Phase.Idle)
                {
                    _cooldown = RetryDelay;
                    return false;
                }
            }
            return true;
        }

        private bool TryStart()
        {
            Vector3 at = _boss.transform.position;
            Vector3 to = _knight.transform.position - at;
            to.y = 0f;
            _dir = to.sqrMagnitude > 0.0001f ? to.normalized : _boss.transform.forward;
            _dir.y = 0f;
            if (_dir.sqrMagnitude < 0.0001f) _dir = Vector3.forward;
            _dir.Normalize();
            _origin = at;
            _groundY = at.y;
            if (_kind == MiniBossAttackKind.Charge)
            {
                _length = BossFight.FreeRun(at, _dir, _data.ChargeLength, Mathf.Max(0.3f, _boss.HitRadius * 0.6f), ChargeStep);
                if (Enemy.LandMaxZ < float.PositiveInfinity && _dir.z > 0.001f)
                    _length = Mathf.Min(_length, Mathf.Max(0f, (Enemy.LandMaxZ - at.z) / _dir.z));
                if (_length < MinCharge)
                {
                    _cooldown = RetryDelay;
                    return false;
                }
                chargeLane.ShowLine(at, _dir, _length, _data.ChargeWidth);
            }
            else slamWarning.Show(at, _data.SlamRadius);
            SetTypeAttacks(false);
            _boss.Halted = true;
            _boss.PlayAction(_data.AttackClip);
            _boss.transform.rotation = Quaternion.LookRotation(_dir);
            _hit = false;
            _time = 0f;
            Attacks++;
            State = Phase.WindUp;
            WindUpStarted?.Invoke(_kind, _origin);
            return true;
        }

        private void WindUp(float dt)
        {
            float windUp = _data.AttackWindUp;
            float t = _time / windUp;
            if (_kind == MiniBossAttackKind.Charge)
            {
                chargeLane.SetProgress(t, windUp - _time);
                _boss.transform.rotation = Quaternion.RotateTowards(_boss.transform.rotation, Quaternion.LookRotation(_dir), TurnSpeed * dt);
            }
            else
            {
                slamWarning.SetProgress(t);
                slamWarning.MoveTo(_boss.transform.position); // the halted body can still be nudged by the crowd
            }
            if (_time >= windUp) Strike();
        }

        private void Strike()
        {
            _time = 0f;
            if (_kind == MiniBossAttackKind.Charge)
            {
                chargeLane.Flash();
                _origin = _boss.transform.position;
                _origin.y = _groundY;
                State = Phase.Strike;
                Struck?.Invoke(_kind, _origin);
                return;
            }
            _origin = slamWarning.Centre;
            _origin.y = _groundY;
            slamWarning.Flash();
            float dealt = 0f;
            if (SpecialAttack.Planar(_knight.transform.position, _origin) <= _data.SlamRadius) dealt = Hurt();
            Shake(dealt > 0f ? slamHitShake : slamShake);
            Struck?.Invoke(_kind, _origin);
            Recover();
        }

        /// <summary>Charge: along the lane at constant speed; the swept part of the path hits the Knight once.</summary>
        private void Dash()
        {
            float t = Mathf.Clamp01(_time / _data.ChargeTime);
            Vector3 prev = _boss.transform.position;
            prev.y = _groundY;
            Vector3 next = _origin + _dir * (_length * t);
            next.y = _groundY;
            if (!_hit && GhoulLeap.SegmentDistance(_knight.transform.position, prev, next) <= _data.ChargeWidth * 0.5f) Hurt();
            _boss.transform.position = next;
            if (t < 1f) return;
            Shake(chargeStopShake);
            Recover();
        }

        private float Hurt()
        {
            _hit = true;
            if (_knight == null || _knight.IsDead || _data.AttackDamage <= 0f) return 0f;
            float dealt = _knight.TakeDamage(_data.AttackDamage * _knight.Max, _boss.transform.position);
            HitKnight?.Invoke(dealt);
            return dealt;
        }

        private void Recover()
        {
            _time = 0f;
            State = Phase.Recover;
            if (!_boss.PlayPose(StandPose)) _boss.ResetAction();
        }

        private void End()
        {
            _time = 0f;
            _cooldown = _data.AttackCooldown;
            State = Phase.Idle;
            _boss.Halted = false;
            _boss.ResetAction();
            SetTypeAttacks(true);
        }

        /// <summary>Hides the warning (a flash still fading is left to fade), the body walks again, the type attacks come back.</summary>
        private void Cancel()
        {
            if (State == Phase.WindUp)
            {
                if (slamWarning != null) slamWarning.Hide();
                if (chargeLane != null) chargeLane.Hide();
            }
            if (State == Phase.WindUp || State == Phase.Strike || State == Phase.Recover)
            {
                if (_boss != null && _boss.IsAlive)
                {
                    _boss.Halted = false;
                    _boss.ResetAction();
                    if (State == Phase.Strike)
                    {
                        var p = _boss.transform.position;
                        _boss.transform.position = new Vector3(p.x, _groundY, p.z);
                    }
                }
                _cooldown = _data != null ? _data.AttackCooldown : 0f;
                State = Phase.Idle;
            }
            SetTypeAttacks(true);
            _time = 0f;
        }

        /// <summary>Switches the body's own special attacks off (this attack runs) or back on.</summary>
        private void SetTypeAttacks(bool on)
        {
            if (_typeOff == !on) return;
            _typeOff = !on;
            for (int i = 0; i < _typeAttacks.Count; i++)
                if (_typeAttacks[i] != null) _typeAttacks[i].enabled = on;
        }

        private void Shake(float amplitude)
        {
            if (followCamera != null && amplitude > 0f) followCamera.Shake(amplitude, shakeTime);
        }

        private void OnDisable() => Stop();
    }
}
