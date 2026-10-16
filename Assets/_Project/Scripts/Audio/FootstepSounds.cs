using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Knight footsteps (KAN-235, KAN-81.2). Quiet soft steps instead of the removed snow steps of KAN-15.
    /// <list type="bullet">
    /// <item>Timing: a step plays when the lower foot changes (left / right foot bone heights from the
    /// <see cref="HumanoidRig"/>, small hysteresis), so the sound sits on the run clip's foot plants at any speed. A hero
    /// model without foot bones falls back to a stride timer.</item>
    /// <item>Clips: one set per <see cref="FootstepSurface"/>; even index = left foot tone (a little lower), odd = right.
    /// A step never repeats that foot's last clip. Left steps pan a hair left, right steps a hair right.</item>
    /// <item>Surface: a ray down from the Knight at each step reads <see cref="FootstepSurfaceTag"/> on the ground; untagged
    /// ground (or an empty set) uses <see cref="fallback"/> (snow).</item>
    /// <item>Level: about half of the old 0.35; walking (slow stick) quieter than a run; ducked while the fight is heavy
    /// (shots, enemy hits and kills, Knight hurt per second).</item>
    /// </list>
    /// Volume from <see cref="GameSettings.SfxVolume"/>. No GC per frame or step.
    /// </summary>
    [DefaultExecutionOrder(200)] // LateUpdate after the Animator and the aim / stance bone passes
    public sealed class FootstepSounds : MonoBehaviour
    {
        public enum Foot { Left = 0, Right = 1 }

        private const int SurfaceCount = 4;

        [Header("Clips (SfxBuilder.BuildFootsteps): even index = left foot, odd = right")]
        [SerializeField] private AudioClip[] snow;
        [SerializeField] private AudioClip[] shingle;
        [SerializeField] private AudioClip[] wood;
        [SerializeField] private AudioClip[] stone;
        [Tooltip("Surface for untagged ground (Realm 1 = snow).")]
        [SerializeField] private FootstepSurface fallback = FootstepSurface.Snow;

        [Header("Sources")]
        [Tooltip("The Knight root: speed and the ground ray.")]
        [SerializeField] private Transform body;
        [Tooltip("Optional: base run speed reference (skills that raise the speed still count as a run).")]
        [SerializeField] private PlayerMover mover;
        [Tooltip("Optional fight meter: shots.")]
        [SerializeField] private AutoShooter shooter;
        [Tooltip("Optional fight meter: Knight hurt.")]
        [SerializeField] private PlayerHealth health;

        [Header("Feet")]
        [Tooltip("Foot bones. Empty or hidden (hero swap) = found again from the active HumanoidRig under the body; none = stride timer.")]
        [SerializeField] private Transform footL;
        [SerializeField] private Transform footR;
        [Tooltip("Height gap (m) between the feet before the lower foot counts as changed (no double steps from jitter).")]
        [SerializeField, Min(0f)] private float plantHysteresis = 0.012f;
        [SerializeField, Min(0f)] private float minStepInterval = 0.14f;
        [Tooltip("Stride timer (no foot bones): metres per step.")]
        [SerializeField, Min(0.1f)] private float strideLength = 1.2f;
        [Tooltip("Below this planar speed (m/s) the Knight stands: no steps.")]
        [SerializeField, Min(0f)] private float minSpeed = 0.3f;

        [Header("Mix")]
        [Tooltip("Run step level (the KAN-15 steps were 0.35).")]
        [SerializeField, Range(0f, 1f)] private float stepVolume = 0.18f;
        [Tooltip("Level share of a slow walk (speed <= walkSpeed); full at runSpeed.")]
        [SerializeField, Range(0f, 1f)] private float walkShare = 0.55f;
        [SerializeField, Min(0f)] private float walkSpeed = 1.5f;
        [Tooltip("Used when no mover is set (else the mover's base speed).")]
        [SerializeField, Min(0.1f)] private float runSpeed = 5f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.05f;
        [SerializeField, Range(0f, 0.3f)] private float volumeJitter = 0.1f;
        [SerializeField, Range(0f, 1f)] private float footPan = 0.08f;

        [Header("Fight ducking")]
        [Tooltip("Fight events per second where the ducking starts / is full.")]
        [SerializeField, Min(0f)] private float duckStartRate = 6f;
        [SerializeField, Min(0.1f)] private float duckFullRate = 20f;
        [Tooltip("Step level at full ducking.")]
        [SerializeField, Range(0f, 1f)] private float duckGain = 0.4f;
        [Tooltip("Seconds the fight meter remembers an event (exponential).")]
        [SerializeField, Min(0.05f)] private float fightMemory = 0.5f;
        [SerializeField, Min(0.01f)] private float duckDownTime = 0.1f;
        [SerializeField, Min(0.01f)] private float duckUpTime = 0.6f;
        [SerializeField, Min(0f)] private float hurtWeight = 3f;
        [SerializeField, Min(0f)] private float followUpWeight = 0.5f;

        [Tooltip("Ground ray: starts this high above the Knight root, reaches this far below it.")]
        [SerializeField, Min(0f)] private float rayUp = 0.6f;
        [SerializeField, Min(0f)] private float rayDown = 1.2f;
        [SerializeField] private LayerMask groundMask = ~0;

        private readonly AudioSource[] _sources = new AudioSource[2];
        private readonly int[] _lastIndex = { -1, -1 };
        private readonly List<HumanoidRig> _rigs = new List<HumanoidRig>(2);
        private AudioClip[][] _sets;
        private Vector3 _lastPos;
        private bool _hasPos;
        private int _lowerFoot = -1;
        private int _nextStrideFoot;
        private float _strideLeft;
        private float _clock;
        private float _lastStep = float.NegativeInfinity;
        private float _nextRigSearch;
        private float _activity;
        private float _duck = 1f;
        private float _speed;

        /// <summary>Steps played (tests, dev log).</summary>
        public int Steps { get; private set; }
        public int LeftSteps { get; private set; }
        public int RightSteps { get; private set; }
        public Foot LastFoot { get; private set; }
        public FootstepSurface LastSurface { get; private set; }
        public AudioClip LastClip { get; private set; }
        /// <summary>Index into the surface set of the last step (-1 = none yet).</summary>
        public int LastIndex { get; private set; } = -1;
        /// <summary>Level of the last step before the SFX volume setting (step level x walk / run x duck x jitter).</summary>
        public float LastLevel { get; private set; }
        /// <summary>Current step level factor from fight ducking (1 = none, <see cref="duckGain"/> = full).</summary>
        public float Duck => _duck;
        /// <summary>Fight events per second (shots, enemy hits / kills, Knight hurt).</summary>
        public float FightRate => _activity / fightMemory;
        /// <summary>Planar speed of the body in the last <see cref="Advance"/> (m/s).</summary>
        public float Speed => _speed;
        /// <summary>True when no foot bones are known: steps come from the stride timer.</summary>
        public bool UsingStrideTimer => footL == null || footR == null;
        public Transform FootL => footL;
        public Transform FootR => footR;

        /// <summary>Wires clips, body and feet (tests; the scene step sets the serialized fields). Call before Awake (inactive object).</summary>
        public void Setup(AudioClip[] snowClips, AudioClip[] shingleClips, AudioClip[] woodClips, AudioClip[] stoneClips,
            Transform knight, Transform left = null, Transform right = null)
        {
            snow = snowClips;
            shingle = shingleClips;
            wood = woodClips;
            stone = stoneClips;
            body = knight;
            footL = left;
            footR = right;
            _sets = null;
        }

        /// <summary>The clip set of a surface (null / empty = none built).</summary>
        public AudioClip[] ClipsOf(FootstepSurface surface)
        {
            EnsureSets();
            int i = (int)surface;
            return i >= 0 && i < SurfaceCount ? _sets[i] : null;
        }

        /// <summary>Adds fight events to the ducking meter (the event handlers use it; tests and dev tools too).</summary>
        public void AddFightActivity(float events) => _activity += Mathf.Max(0f, events);

        private void Awake()
        {
            EnsureSets();
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "Footstep Left" : "Footstep Right");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.panStereo = i == 0 ? -footPan : footPan;
                _sources[i] = src;
            }
        }

        private void OnEnable()
        {
            Enemy.Damaged += OnEnemyDamaged;
            if (shooter != null)
            {
                shooter.Fired += OnFired;
                shooter.FollowUpFired += OnFollowUpFired;
            }
            if (health != null) health.OnHurt += OnHurt;
            _hasPos = false;
        }

        private void OnDisable()
        {
            Enemy.Damaged -= OnEnemyDamaged;
            if (shooter != null)
            {
                shooter.Fired -= OnFired;
                shooter.FollowUpFired -= OnFollowUpFired;
            }
            if (health != null) health.OnHurt -= OnHurt;
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        /// <summary>
        /// One frame: body speed, fight meter + ducking, foot plant (or stride timer) and the step sound. LateUpdate calls it
        /// with <see cref="Time.deltaTime"/>; tests call it by hand after moving the body and the feet. dt 0 (paused) = nothing.
        /// </summary>
        public void Advance(float dt)
        {
            if (dt <= 0f || body == null) return;
            _clock += dt;

            Vector3 pos = body.position;
            Vector3 delta = _hasPos ? pos - _lastPos : Vector3.zero;
            delta.y = 0f;
            _lastPos = pos;
            _hasPos = true;
            float moved = delta.magnitude;
            _speed = moved > 3f ? 0f : moved / dt; // a teleport (portal, Chamber start) is not a run

            _activity *= Mathf.Exp(-dt / fightMemory);
            float rate = FightRate;
            float target = Mathf.Lerp(1f, duckGain, Mathf.InverseLerp(duckStartRate, duckFullRate, rate));
            _duck = Mathf.MoveTowards(_duck, target, dt * (1f - duckGain) / (target < _duck ? duckDownTime : duckUpTime));

            if (!FeetUsable()) FindFeet();
            bool moving = _speed >= minSpeed;
            if (!UsingStrideTimer)
            {
                float d = footL.position.y - footR.position.y;
                int lower = d < -plantHysteresis ? (int)Foot.Left : d > plantHysteresis ? (int)Foot.Right : _lowerFoot;
                if (lower != _lowerFoot)
                {
                    bool first = _lowerFoot < 0;
                    _lowerFoot = lower;
                    if (!first && moving && _clock - _lastStep >= minStepInterval) Play((Foot)lower);
                }
                return;
            }

            if (!moving)
            {
                _strideLeft = strideLength * 0.3f; // the first step of a new run comes quickly
                return;
            }
            _strideLeft -= _speed * dt;
            if (_strideLeft > 0f) return;
            _strideLeft += strideLength;
            if (_clock - _lastStep < minStepInterval) return;
            Play((Foot)_nextStrideFoot);
            _nextStrideFoot = 1 - _nextStrideFoot;
        }

        /// <summary>Surface under a world point: the tag on the ground a ray down hits, else <see cref="fallback"/>.</summary>
        public FootstepSurface SurfaceUnder(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * rayUp, Vector3.down, out RaycastHit hit, rayUp + rayDown, groundMask, QueryTriggerInteraction.Ignore))
            {
                // TryGetComponent up the parents: a miss allocates nothing (GetComponent's editor fake null would).
                for (Transform t = hit.collider.transform; t != null; t = t.parent)
                    if (t.TryGetComponent(out FootstepSurfaceTag tag)) return tag.Surface;
            }
            return fallback;
        }

        private void Play(Foot foot)
        {
            FootstepSurface surface = SurfaceUnder(body.position);
            AudioClip[] clips = ClipsOf(surface);
            if (clips == null || clips.Length == 0)
            {
                surface = fallback;
                clips = ClipsOf(surface);
                if (clips == null || clips.Length == 0) return;
            }

            int f = (int)foot;
            int index = PickIndex(clips.Length, f, _lastIndex[f]);
            _lastIndex[f] = index;

            float full = mover != null && mover.BaseSpeed > 0f ? mover.BaseSpeed : runSpeed;
            float run = Mathf.InverseLerp(walkSpeed, Mathf.Max(walkSpeed + 0.01f, full), _speed);
            float level = stepVolume * Mathf.Lerp(walkShare, 1f, run) * _duck * (1f + Random.Range(-volumeJitter, volumeJitter));

            var src = _sources[f];
            if (src != null)
            {
                src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
                src.PlayOneShot(clips[index], level * GameSettings.SfxVolume);
            }

            _lastStep = _clock;
            Steps++;
            if (foot == Foot.Left) LeftSteps++;
            else RightSteps++;
            LastFoot = foot;
            LastSurface = surface;
            LastClip = clips[index];
            LastIndex = index;
            LastLevel = level;
        }

        /// <summary>
        /// A clip index of the foot's tone (even = left, odd = right; a set of 1 is shared), never that foot's last one when
        /// the foot has 2+ clips.
        /// </summary>
        public static int PickIndex(int count, int foot, int last)
        {
            if (count <= 1) return 0;
            int parity = foot & 1;
            int own = (count - parity + 1) / 2; // clips of that parity
            if (own <= 0) return Random.Range(0, count);
            int k = Random.Range(0, own);
            int index = parity + 2 * k;
            if (index == last && own > 1) index = parity + 2 * ((k + 1 + Random.Range(0, own - 1)) % own);
            return index;
        }

        private bool FeetUsable() =>
            footL != null && footR != null && footL.gameObject.activeInHierarchy && footR.gameObject.activeInHierarchy;

        /// <summary>Feet hidden or missing (hero swap): search the active rig again, at most twice a second.</summary>
        private void FindFeet()
        {
            if (_clock < _nextRigSearch) return;
            _nextRigSearch = _clock + 0.5f;
            footL = footR = null;
            _lowerFoot = -1;
            body.GetComponentsInChildren(false, _rigs);
            for (int i = 0; i < _rigs.Count; i++)
            {
                if (_rigs[i].footL == null || _rigs[i].footR == null) continue;
                footL = _rigs[i].footL;
                footR = _rigs[i].footR;
                break;
            }
            _rigs.Clear();
        }

        private void EnsureSets()
        {
            if (_sets != null) return;
            _sets = new AudioClip[SurfaceCount][];
            _sets[(int)FootstepSurface.Snow] = snow;
            _sets[(int)FootstepSurface.Shingle] = shingle;
            _sets[(int)FootstepSurface.Wood] = wood;
            _sets[(int)FootstepSurface.Stone] = stone;
        }

        private void OnEnemyDamaged(Enemy enemy, float amount, bool killed)
        {
            if (killed || enemy.LastDamageKind == DamageKind.Direct) _activity += 1f; // burn ticks are not a heavy fight
        }

        private void OnFired(Vector3 muzzle, Vector3 direction) => _activity += 1f;
        private void OnFollowUpFired(Vector3 muzzle, Vector3 direction) => _activity += followUpWeight;
        private void OnHurt() => _activity += hurtWeight;
    }
}
