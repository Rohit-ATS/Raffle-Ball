using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace RiftRally.Core
{
    public enum HandSide { Left = 0, Right = 1 }

    /// <summary>A single frame of tracked-hand state, already converted to world space.</summary>
    public struct HandSample
    {
        public bool IsTracked;
        public bool IsPinching;
        public float PinchStrength;   // 0 = open, 1 = fully closed
        public Vector3 PinchPoint;    // filtered midpoint of thumb tip and index tip
        public Vector3 RawPinchPoint; // unfiltered, for diagnostics
        public Vector3 PalmPosition;
        public Quaternion PalmRotation;

        /// <summary>Hand velocity over a short window, in m/s.</summary>
        public Vector3 Velocity;

        /// <summary>Peak speed seen in the velocity window, for flick detection.</summary>
        public float PeakSpeed;

        /// <summary>
        /// 0-1 confidence that this hand is usable. Drops while tracking is being recovered,
        /// so gameplay can fade things out instead of snapping them.
        /// </summary>
        public float Confidence;

        /// <summary>Distance between thumb and index tips, in metres.</summary>
        public float PinchGap;

        /// <summary>Rough hand scale (wrist to middle-finger tip), used to normalise pinch.</summary>
        public float HandScale;
    }

    /// <summary>
    /// Single source of truth for hand input. Reads Unity's XRHandSubsystem (Quest hand
    /// tracking) and turns it into something a game can actually be played with.
    ///
    /// Three things here matter more than anything else for how the game feels:
    ///
    /// 1. Joint positions are one-euro filtered, so a held stone does not vibrate while the
    ///    hand is still, yet a fast pull is not dragged behind the hand.
    /// 2. The pinch gap is normalised by the hand's own size before being compared to a
    ///    threshold, so the same gesture works for small and large hands instead of being
    ///    tuned for whoever tested it last.
    /// 3. Brief tracking dropouts - which happen constantly when a hand turns edge-on to the
    ///    headset - are held through a grace window rather than reported as "hand gone". A
    ///    dropped frame used to release the stone mid-pull; now it does nothing.
    ///
    /// When no hand subsystem is running (plain Editor play mode with no headset) it falls
    /// back to a mouse driver, which keeps the whole game loop testable on the desktop.
    /// </summary>
    public class HandInput : MonoBehaviour
    {
        public static HandInput Instance { get; private set; }

        [Tooltip("Transform that hand joint poses are relative to. Normally the XR Origin.")]
        public Transform TrackingSpace;

        [Tooltip("Drive the right hand with the mouse when no XR hand tracking is present.")]
        public bool AllowDesktopFallback = true;

        [Tooltip("Plane height used by the desktop mouse fallback.")]
        public float DesktopPlaneHeight = GameConfig.RinkHeight;

        [Header("Filtering")]
        [Tooltip("Cutoff in Hz while the hand is still. Lower is steadier and laggier.")]
        public float FilterMinCutoff = OneEuroFilter.DefaultMinCutoff;

        [Tooltip("How quickly the filter opens up as the hand speeds up.")]
        public float FilterBeta = OneEuroFilter.DefaultBeta;

        [Tooltip("Seconds of lost tracking to ride out before declaring the hand gone.")]
        public float TrackingGracePeriod = 0.22f;

        private readonly List<XRHandSubsystem> _subsystems = new();
        private XRHandSubsystem _subsystem;

        private readonly HandSample[] _samples = new HandSample[2];
        private readonly bool[] _pinchLatched = new bool[2];
        private readonly bool[] _wasPinching = new bool[2];
        private readonly float[] _lastTrackedTime = new float[2];
        private readonly float[] _handScale = new float[2];

        private readonly OneEuroFilterVector3[] _pinchFilters = new OneEuroFilterVector3[2];
        private readonly OneEuroFilterVector3[] _palmFilters = new OneEuroFilterVector3[2];
        private readonly VelocityEstimator[] _velocity = new VelocityEstimator[2];

        public bool UsingRealHands => _subsystem != null && _subsystem.running;

        public HandSample Left => _samples[(int)HandSide.Left];
        public HandSample Right => _samples[(int)HandSide.Right];
        public HandSample Get(HandSide side) => _samples[(int)side];

        /// <summary>Fires on the frame a pinch closes.</summary>
        public event System.Action<HandSide> PinchStarted;

        /// <summary>Fires on the frame a pinch opens again.</summary>
        public event System.Action<HandSide> PinchEnded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            for (int i = 0; i < 2; i++)
            {
                _pinchFilters[i] = new OneEuroFilterVector3(FilterMinCutoff, FilterBeta);
                _palmFilters[i] = new OneEuroFilterVector3(FilterMinCutoff * 0.8f, FilterBeta);
                _velocity[i] = new VelocityEstimator(8);
                _handScale[i] = 0.18f;
                _lastTrackedTime[i] = -999f;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            EnsureSubsystem();

            if (UsingRealHands)
            {
                SampleRealHand(HandSide.Left, _subsystem.leftHand);
                SampleRealHand(HandSide.Right, _subsystem.rightHand);
            }
            else if (AllowDesktopFallback)
            {
                SampleDesktopFallback();
            }
            else
            {
                _samples[0] = default;
                _samples[1] = default;
            }

            DispatchPinchEvents(HandSide.Left);
            DispatchPinchEvents(HandSide.Right);
        }

        private void EnsureSubsystem()
        {
            if (_subsystem != null && _subsystem.running) return;

            _subsystems.Clear();
            SubsystemManager.GetSubsystems(_subsystems);
            _subsystem = null;
            foreach (var s in _subsystems)
            {
                if (s != null && s.running) { _subsystem = s; break; }
            }
        }

        private void SampleRealHand(HandSide side, XRHand hand)
        {
            int i = (int)side;
            float now = Time.time;
            var sample = new HandSample { HandScale = _handScale[i] };

            bool haveThumb = false, haveIndex = false, havePalm = false;
            Pose thumb = default, index = default, palm = default;

            if (hand.isTracked)
            {
                haveThumb = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out thumb);
                haveIndex = hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out index);
                havePalm = hand.GetJoint(XRHandJointID.Palm).TryGetPose(out palm);
                UpdateHandScale(side, hand);
            }

            if (!hand.isTracked || !haveThumb || !haveIndex)
            {
                // Ride out brief dropouts: keep reporting the last good sample at falling
                // confidence, and only let go once the grace window expires.
                float age = now - _lastTrackedTime[i];
                if (age <= TrackingGracePeriod && _samples[i].IsTracked)
                {
                    var held = _samples[i];
                    held.Confidence = Mathf.Clamp01(1f - age / Mathf.Max(0.0001f, TrackingGracePeriod));
                    held.Velocity = Vector3.zero;
                    _samples[i] = held;
                    return;
                }

                _pinchLatched[i] = false;
                _pinchFilters[i].Reset();
                _palmFilters[i].Reset();
                _velocity[i].Reset();
                _samples[i] = sample;
                return;
            }

            _lastTrackedTime[i] = now;

            Vector3 thumbPos = ToWorldPoint(thumb.position);
            Vector3 indexPos = ToWorldPoint(index.position);
            Vector3 rawPinch = (thumbPos + indexPos) * 0.5f;
            float gap = Vector3.Distance(thumbPos, indexPos);

            Vector3 filteredPinch = _pinchFilters[i].Filter(rawPinch, Time.deltaTime);
            _velocity[i].Add(filteredPinch, now);

            // Normalise the gap by hand size so one threshold fits every player.
            float scaleRatio = _handScale[i] / 0.18f;
            float enter = GameConfig.PinchEnterDistance * scaleRatio;
            float exit = GameConfig.PinchExitDistance * scaleRatio;

            sample.IsTracked = true;
            sample.Confidence = 1f;
            sample.RawPinchPoint = rawPinch;
            sample.PinchPoint = filteredPinch;
            sample.PinchGap = gap;
            sample.PinchStrength = Mathf.Clamp01(Mathf.InverseLerp(exit, enter, gap));
            sample.Velocity = _velocity[i].Velocity;
            sample.PeakSpeed = _velocity[i].PeakSpeed;

            if (havePalm)
            {
                sample.PalmPosition = _palmFilters[i].Filter(ToWorldPoint(palm.position),
                                                             Time.deltaTime);
                sample.PalmRotation = ToWorldRotation(palm.rotation);
            }
            else
            {
                sample.PalmPosition = filteredPinch;
                sample.PalmRotation = Quaternion.identity;
            }

            // Hysteresis: close below the enter gap, only release past the wider exit gap.
            if (_pinchLatched[i]) _pinchLatched[i] = gap < exit;
            else _pinchLatched[i] = gap < enter;

            sample.IsPinching = _pinchLatched[i];
            _samples[i] = sample;
        }

        /// <summary>
        /// Measures the hand from wrist to middle-finger tip and tracks it slowly. Slowly
        /// because a single bad frame must not change the pinch thresholds mid-grab.
        /// </summary>
        private void UpdateHandScale(HandSide side, XRHand hand)
        {
            if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist)) return;
            if (!hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose tip)) return;

            float measured = Vector3.Distance(wrist.position, tip.position);
            if (measured < 0.08f || measured > 0.32f) return;   // implausible, ignore

            int i = (int)side;
            _handScale[i] = Mathf.Lerp(_handScale[i], measured, 0.04f);
        }

        private void SampleDesktopFallback()
        {
            _samples[(int)HandSide.Left] = default;

            var cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(PointerInput.Position);
            var plane = new Plane(Vector3.up, new Vector3(0f, DesktopPlaneHeight, 0f));

            Vector3 point = cam.transform.position + cam.transform.forward * 0.6f;
            if (plane.Raycast(ray, out float enter)) point = ray.GetPoint(enter);

            bool held = PointerInput.PrimaryHeld;
            int i = (int)HandSide.Right;
            _pinchLatched[i] = held;

            Vector3 pinch = point + Vector3.up * 0.02f;
            _velocity[i].Add(pinch, Time.time);

            _samples[i] = new HandSample
            {
                IsTracked = true,
                IsPinching = held,
                Confidence = 1f,
                PinchStrength = held ? 1f : 0f,
                PinchPoint = pinch,
                RawPinchPoint = pinch,
                PalmPosition = point + Vector3.up * 0.05f,
                PalmRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up),
                Velocity = _velocity[i].Velocity,
                PeakSpeed = _velocity[i].PeakSpeed,
                PinchGap = held ? 0.01f : 0.06f,
                HandScale = 0.18f
            };
        }

        private void DispatchPinchEvents(HandSide side)
        {
            int i = (int)side;
            bool now = _samples[i].IsTracked && _samples[i].IsPinching;
            if (now && !_wasPinching[i]) PinchStarted?.Invoke(side);
            else if (!now && _wasPinching[i]) PinchEnded?.Invoke(side);
            _wasPinching[i] = now;
        }

        private Vector3 ToWorldPoint(Vector3 local)
            => TrackingSpace != null ? TrackingSpace.TransformPoint(local) : local;

        private Quaternion ToWorldRotation(Quaternion local)
            => TrackingSpace != null ? TrackingSpace.rotation * local : local;

        /// <summary>
        /// Picks the hand best suited to throwing this frame: a pinching hand wins, and the
        /// right hand breaks ties. Returns false when neither hand is tracked.
        /// </summary>
        public bool TryGetActiveHand(out HandSide side, out HandSample sample)
        {
            var right = _samples[(int)HandSide.Right];
            var left = _samples[(int)HandSide.Left];

            if (right.IsTracked && right.IsPinching) { side = HandSide.Right; sample = right; return true; }
            if (left.IsTracked && left.IsPinching) { side = HandSide.Left; sample = left; return true; }
            if (right.IsTracked) { side = HandSide.Right; sample = right; return true; }
            if (left.IsTracked) { side = HandSide.Left; sample = left; return true; }

            side = HandSide.Right;
            sample = default;
            return false;
        }

        /// <summary>True when either hand is pinching right now. Used by menus.</summary>
        public bool AnyPinching =>
            (Left.IsTracked && Left.IsPinching) || (Right.IsTracked && Right.IsPinching);

        /// <summary>The nearest tracked pinch point to a world position, for UI hit testing.</summary>
        public bool TryGetNearestPinchPoint(Vector3 target, out Vector3 point, out HandSide side)
        {
            float best = float.MaxValue;
            point = Vector3.zero;
            side = HandSide.Right;
            bool found = false;

            for (int i = 0; i < 2; i++)
            {
                if (!_samples[i].IsTracked) continue;
                float d = Vector3.Distance(_samples[i].PinchPoint, target);
                if (d < best)
                {
                    best = d;
                    point = _samples[i].PinchPoint;
                    side = (HandSide)i;
                    found = true;
                }
            }
            return found;
        }
    }
}
