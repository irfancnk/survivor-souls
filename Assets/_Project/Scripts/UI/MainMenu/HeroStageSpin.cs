using System;
using UnityEngine;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>Numbers of the hero stage turn (KAN-244). Serialized on <see cref="HeroStage"/>.</summary>
    [Serializable]
    public sealed class HeroStageSpinFeel
    {
        [Tooltip("Hero turn per panel pixel of horizontal drag (deg). The panel is 1080 px wide.")]
        [Min(0.01f)] public float degreesPerPixel = 0.4f;
        [Tooltip("Spin friction after a flick (1/s, exponential). 6 = the spin is under 1 % of its speed after 0.8 s.")]
        [Min(0.5f)] public float friction = 6f;
        [Tooltip("Flick speed below this (deg/s) stops the spin.")]
        [Min(0f)] public float stopSpeed = 6f;
        [Tooltip("Highest flick speed (deg/s): a wild swipe does not blur the hero.")]
        [Min(1f)] public float maxSpeed = 900f;
        [Tooltip("Seconds without a touch before the hero turns back to the front.")]
        [Min(0f)] public float returnDelay = 2f;
        [Tooltip("Seconds of the ease back to the front.")]
        [Min(0.05f)] public float returnTime = 0.6f;
    }

    /// <summary>
    /// Hero stage turn (KAN-244): a horizontal drag turns the hero about the up axis, a release with speed keeps it spinning
    /// with friction, and <see cref="HeroStageSpinFeel.returnDelay"/> after the last touch (spin stopped) the hero eases back
    /// to the front (yaw 0, the short way round). Plain class: <see cref="HeroStage"/> feeds it, EditMode tests drive it.
    /// No allocation.
    /// </summary>
    public sealed class HeroStageSpin
    {
        private const int VelocitySamples = 4;
        private readonly HeroStageSpinFeel _feel;
        private readonly float[] _sampleDeg = new float[VelocitySamples];
        private readonly float[] _sampleDt = new float[VelocitySamples];
        private int _sample;
        private float _idle, _returnFrom, _returnT = -1f, _pendingDeg;

        public HeroStageSpin(HeroStageSpinFeel feel) => _feel = feel ?? new HeroStageSpinFeel();

        /// <summary>Hero yaw now (deg, -180..180]. 0 = facing the camera.</summary>
        public float Yaw { get; private set; }
        /// <summary>Spin speed now (deg/s).</summary>
        public float Velocity { get; private set; }
        public bool Dragging { get; private set; }
        /// <summary>True while easing back to the front.</summary>
        public bool Returning => _returnT >= 0f;
        /// <summary>True when the hero is at rest (no drag, no spin, no return).</summary>
        public bool AtRest => !Dragging && Velocity == 0f && !Returning;

        public void Snap(float yaw)
        {
            Yaw = Wrap(yaw);
            Velocity = 0f;
            Dragging = false;
            _returnT = -1f;
            _idle = 0f;
            _pendingDeg = 0f;
        }

        public void BeginDrag()
        {
            Dragging = true;
            Velocity = 0f;
            _returnT = -1f;
            _idle = 0f;
            _pendingDeg = 0f;
            Array.Clear(_sampleDeg, 0, VelocitySamples);
            Array.Clear(_sampleDt, 0, VelocitySamples);
        }

        /// <summary>Drag moved by <paramref name="panelPixels"/> (+ = right: the hero turns to his left, like a turntable).</summary>
        public void Drag(float panelPixels)
        {
            if (!Dragging) BeginDrag();
            float deg = -panelPixels * _feel.degreesPerPixel;
            Yaw = Wrap(Yaw + deg);
            _pendingDeg += deg;
        }

        /// <summary>Finger up: the speed of the last few frames becomes the flick.</summary>
        public void EndDrag()
        {
            if (!Dragging) return;
            Dragging = false;
            float deg = 0f, dt = 0f;
            for (int i = 0; i < VelocitySamples; i++)
            {
                deg += _sampleDeg[i];
                dt += _sampleDt[i];
            }
            Velocity = dt > 1e-4f ? Mathf.Clamp(deg / dt, -_feel.maxSpeed, _feel.maxSpeed) : 0f;
            if (Mathf.Abs(Velocity) < _feel.stopSpeed) Velocity = 0f;
            _idle = 0f;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (Dragging)
            {
                // One velocity sample per frame: what the finger moved this frame.
                _sampleDeg[_sample] = _pendingDeg;
                _sampleDt[_sample] = dt;
                _sample = (_sample + 1) % VelocitySamples;
                _pendingDeg = 0f;
                return;
            }
            if (Velocity != 0f)
            {
                Yaw = Wrap(Yaw + Velocity * dt);
                Velocity *= Mathf.Exp(-_feel.friction * dt);
                if (Mathf.Abs(Velocity) < _feel.stopSpeed) Velocity = 0f;
                _idle = 0f;
                return;
            }
            if (_returnT >= 0f)
            {
                _returnT += dt / _feel.returnTime;
                float k = Mathf.Clamp01(_returnT);
                k = k * k * (3f - 2f * k);
                Yaw = Wrap(_returnFrom * (1f - k));
                if (_returnT >= 1f)
                {
                    Yaw = 0f;
                    _returnT = -1f;
                }
                return;
            }
            if (Yaw == 0f) return;
            _idle += dt;
            if (_idle < _feel.returnDelay) return;
            _returnFrom = Yaw; // Wrap keeps it in -180..180, so the ease takes the short way
            _returnT = 0f;
        }

        public static float Wrap(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            else if (deg <= -180f) deg += 360f;
            return deg;
        }
    }
}
