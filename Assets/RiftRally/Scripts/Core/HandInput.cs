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
        public Vector3 PinchPoint;    // midpoint of thumb tip and index tip
        public Vector3 PalmPosition;
        public Quaternion PalmRotation;
    }

    /// <summary>
    /// Single source of truth for hand input. Reads Unity's XRHandSubsystem (Quest hand
    /// tracking) and derives a pinch gesture with hysteresis so grabs do not flicker.
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

        private readonly List<XRHandSubsystem> _subsystems = new();
        private XRHandSubsystem _subsystem;
        private readonly HandSample[] _samples = new HandSample[2];
        private readonly bool[] _pinchLatched = new bool[2];
        private readonly bool[] _wasPinching = new bool[2];

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
            var sample = new HandSample();

            if (!hand.isTracked)
            {
                _pinchLatched[i] = false;
                _samples[i] = sample;
                return;
            }

            bool haveThumb = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb);
            bool haveIndex = hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index);
            bool havePalm = hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm);

            if (!haveThumb || !haveIndex)
            {
                _pinchLatched[i] = false;
                _samples[i] = sample;
                return;
            }

            Vector3 thumbPos = ToWorldPoint(thumb.position);
            Vector3 indexPos = ToWorldPoint(index.position);
            float gap = Vector3.Distance(thumbPos, indexPos);

            sample.IsTracked = true;
            sample.PinchPoint = (thumbPos + indexPos) * 0.5f;
            sample.PinchStrength = Mathf.Clamp01(
                Mathf.InverseLerp(GameConfig.PinchExitDistance, GameConfig.PinchEnterDistance, gap));

            if (havePalm)
            {
                sample.PalmPosition = ToWorldPoint(palm.position);
                sample.PalmRotation = ToWorldRotation(palm.rotation);
            }
            else
            {
                sample.PalmPosition = sample.PinchPoint;
                sample.PalmRotation = Quaternion.identity;
            }

            // Hysteresis: close below the enter gap, only release past the wider exit gap.
            if (_pinchLatched[i]) _pinchLatched[i] = gap < GameConfig.PinchExitDistance;
            else _pinchLatched[i] = gap < GameConfig.PinchEnterDistance;

            sample.IsPinching = _pinchLatched[i];
            _samples[i] = sample;
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

            _samples[i] = new HandSample
            {
                IsTracked = true,
                IsPinching = held,
                PinchStrength = held ? 1f : 0f,
                PinchPoint = point + Vector3.up * 0.02f,
                PalmPosition = point + Vector3.up * 0.05f,
                PalmRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up)
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
    }
}
