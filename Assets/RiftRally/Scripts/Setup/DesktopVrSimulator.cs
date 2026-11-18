using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Setup
{
    /// <summary>
    /// Plays the VR game on a desktop by simulating the headset and the hands, rather than
    /// substituting a flat mouse control scheme for them.
    ///
    /// The distinction matters. A mouse-on-a-plane fallback is a second, parallel way to play,
    /// and testing it proves nothing about the headset build: it does not exercise reaching
    /// for a stone in three dimensions, the grab radius, the pull-back geometry, the pinch
    /// hysteresis, the hand visuals, or pressing a physical button in space. This instead
    /// feeds <see cref="HandInput"/> exactly the kind of data an <c>XRHandSubsystem</c> would
    /// - two world-space pinch points with a pinch state - so every one of those paths runs
    /// for real, and the only thing being faked is where the joints come from.
    ///
    /// It is also what makes the throw testable without hardware: a test can place a hand,
    /// close it, drag it back and open it, and assert that a stone leaves at the right speed.
    ///
    /// Controls
    ///   Right mouse drag  look around        W A S D  move      Q / E  down / up
    ///   Hold SHIFT        move the left hand with the mouse
    ///   Hold SPACE        move the right hand with the mouse
    ///   Arrow keys / IJKL move the right hand without a mouse
    ///   Mouse wheel       push the held hand further away or pull it closer
    ///   Left mouse / CTRL pinch
    ///   R  recentre       H or F1  hide this help
    /// </summary>
    public class DesktopVrSimulator : MonoBehaviour
    {
        public static DesktopVrSimulator Instance { get; private set; }

        [Header("Head")]
        [Tooltip("Transform moved as the head. Defaults to the main camera.")]
        public Transform Head;

        public float MoveSpeed = 1.1f;
        public float LookSensitivity = 0.12f;

        [Header("Hands")]
        [Tooltip("How fast the mouse moves a hand, in metres per pixel.")]
        public float HandSensitivity = 0.0016f;

        [Tooltip("How far the wheel pushes a hand per notch, in metres.")]
        public float HandDepthStep = 0.03f;

        [Tooltip("Hands cannot be pushed closer than this to the head.")]
        public float MinHandDistance = 0.18f;

        [Tooltip("Hands cannot be pushed further than this from the head.")]
        public float MaxHandDistance = 1.1f;

        [Header("Help")]
        public bool ShowHelp = true;

        /// <summary>A simulated hand, in world space, in the same terms a real one arrives in.</summary>
        public struct SimHand
        {
            public bool Tracked;
            public bool Pinching;
            public Vector3 Position;
        }

        private readonly SimHand[] _hands = new SimHand[2];
        private Vector3 _headStart;
        private Quaternion _headStartRotation;
        private Vector3 _headPosition;
        private Quaternion _headRotation = Quaternion.identity;
        private float _yaw;
        private float _pitch;
        private bool _started;
        private bool _loggedPose;

        /// <summary>True when the simulator is driving the hands rather than a headset.</summary>
        public static bool Active => Instance != null && Instance.isActiveAndEnabled;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // Hands are pulled by HandInput rather than pushed, so the simulator never has to
            // care about script execution order.
            HandInput.SimulatedHandSource = side =>
            {
                SimHand hand = _hands[(int)side];
                return (hand.Tracked, hand.Pinching, hand.Position);
            };
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            HandInput.SimulatedHandSource = null;
        }

        private void Start()
        {
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            if (Head == null) return;

            // Without this the view starts wherever the XR rig's camera happened to sit -
            // usually at floor height facing into empty space - and the player's first
            // impression of the game is an empty sky.
            FrameRink();

            _headStart = _headPosition;
            _headStartRotation = _headRotation;

            PlaceHandsInFrontOfHead();
            _started = true;
        }

        /// <summary>
        /// Puts the head where a seated player's would be: behind the near end of the rink,
        /// a little above the table, looking down the ice at the house.
        /// </summary>
        public void FrameRink()
        {
            if (Head == null) return;

            var rink = Gameplay.Rink.Instance;
            if (rink == null) return;

            Vector3 eye = rink.RinkToWorld(0f, -GameConfig.RinkLength * 0.5f - 0.30f, 0.34f);
            Vector3 lookAt = rink.RinkToWorld(0f, GameConfig.RinkLength * 0.18f, 0f);

            _headPosition = eye;
            _headRotation = Quaternion.LookRotation((lookAt - eye).normalized, Vector3.up);

            Vector3 euler = _headRotation.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;

            ApplyHeadPose();
        }

        /// <summary>
        /// Writes the simulated head pose onto the camera.
        ///
        /// Done in LateUpdate as well as on demand, because an XR rig keeps re-applying its
        /// own camera offset every frame even with no headset connected. Setting the pose
        /// once is silently undone; owning it last each frame is what actually works.
        /// </summary>
        private void ApplyHeadPose()
        {
            if (Head == null) return;
            Head.SetPositionAndRotation(_headPosition, _headRotation);
        }

        private void LateUpdate()
        {
            if (!_started) return;
            ApplyHeadPose();

            if (_loggedPose) return;
            _loggedPose = true;

            var rink = Gameplay.Rink.Instance;
            Debug.Log($"[RiftRally] Simulator head at {_headPosition} facing " +
                      $"{_headRotation.eulerAngles}; rink surface at " +
                      $"{(rink != null ? rink.Surface.position.ToString() : "<none>")}; " +
                      $"stones={(rink != null ? rink.Stones.Count : -1)}");
        }

        /// <summary>
        /// Puts both hands where a seated player's hands would naturally rest: in front of the
        /// chest, slightly apart, at about the height of the table.
        /// </summary>
        public void PlaceHandsInFrontOfHead()
        {
            if (Head == null) return;

            Vector3 forward = Flatten(_headRotation * Vector3.forward);
            Vector3 right = Flatten(_headRotation * Vector3.right);
            Vector3 origin = _headPosition + forward * 0.42f - Vector3.up * 0.25f;

            SetHand(HandSide.Left, origin - right * 0.14f, false);
            SetHand(HandSide.Right, origin + right * 0.14f, false);
        }

        /// <summary>
        /// Places a hand directly. The keyboard and mouse drive this, and so does a test,
        /// which is what lets the whole throw be exercised with no hardware and no input.
        /// </summary>
        public void SetHand(HandSide side, Vector3 worldPosition, bool pinching)
        {
            _hands[(int)side] = new SimHand
            {
                Tracked = true,
                Pinching = pinching,
                Position = worldPosition
            };
        }

        public void SetPinch(HandSide side, bool pinching)
        {
            SimHand hand = _hands[(int)side];
            hand.Pinching = pinching;
            hand.Tracked = true;
            _hands[(int)side] = hand;
        }

        /// <summary>Hides a hand, as if it had left the tracking volume.</summary>
        public void SetHandTracked(HandSide side, bool tracked)
        {
            SimHand hand = _hands[(int)side];
            hand.Tracked = tracked;
            _hands[(int)side] = hand;
        }

        public SimHand GetHand(HandSide side) => _hands[(int)side];

        private void Update()
        {
            if (!_started || Head == null || !DesktopInput.Available) return;

            bool movingLeft = DesktopInput.Held(SimKey.LeftHand);
            bool movingRight = DesktopInput.Held(SimKey.RightHand) || HandKeyboardMoving;

            if (movingLeft || movingRight) MoveHand(movingLeft ? HandSide.Left : HandSide.Right);
            else UpdateHead();

            // The pinch is the left mouse button, and it applies to whichever hand is being
            // moved - or to the right hand, which is the one most people throw with.
            bool pinching = PointerInput.PrimaryHeld || PointerInput.KeyboardPinchHeld;
            if (movingLeft) SetPinch(HandSide.Left, pinching);
            else if (movingRight) SetPinch(HandSide.Right, pinching);
            else SetPinch(HandSide.Right, pinching);

            if (DesktopInput.PressedThisFrame(SimKey.Reset)) Recentre();
            if (DesktopInput.PressedThisFrame(SimKey.ToggleHelp)) ShowHelp = !ShowHelp;
        }

        private static bool HandKeyboardMoving =>
            DesktopInput.Held(SimKey.HandForward)
            || DesktopInput.Held(SimKey.HandBack)
            || DesktopInput.Held(SimKey.HandLeft)
            || DesktopInput.Held(SimKey.HandRight);

        private void UpdateHead()
        {
            float dt = Time.unscaledDeltaTime;

            if (DesktopInput.LookHeld)
            {
                Vector2 delta = DesktopInput.MouseDelta;
                _yaw += delta.x * LookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * LookSensitivity, -85f, 85f);
                _headRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 move = Vector3.zero;
            Vector3 forward = Flatten(_headRotation * Vector3.forward);
            Vector3 right = Flatten(_headRotation * Vector3.right);

            if (DesktopInput.Held(SimKey.Forward)) move += forward;
            if (DesktopInput.Held(SimKey.Back)) move -= forward;
            if (DesktopInput.Held(SimKey.Right)) move += right;
            if (DesktopInput.Held(SimKey.Left)) move -= right;
            if (DesktopInput.Held(SimKey.Up)) move += Vector3.up;
            if (DesktopInput.Held(SimKey.Down)) move -= Vector3.up;

            if (move.sqrMagnitude < 0.0001f) return;

            Vector3 step = move.normalized * (MoveSpeed * dt);
            _headPosition += step;

            // The hands come along with the head, exactly as they would on a real body.
            for (int i = 0; i < 2; i++)
            {
                SimHand hand = _hands[i];
                hand.Position += step;
                _hands[i] = hand;
            }
        }

        /// <summary>
        /// Moves a hand in the plane facing the player, with the wheel controlling depth.
        /// Working in the head's own axes is what makes this feel like reaching rather than
        /// like dragging an object around a map.
        /// </summary>
        private void MoveHand(HandSide side)
        {
            SimHand hand = _hands[(int)side];
            if (!hand.Tracked)
            {
                hand.Tracked = true;
                hand.Position = _headPosition
                                + Flatten(_headRotation * Vector3.forward) * 0.42f
                                - Vector3.up * 0.25f;
            }

            Vector2 delta = DesktopInput.MouseDelta;
            Vector3 right = _headRotation * Vector3.right;
            Vector3 up = _headRotation * Vector3.up;

            hand.Position += right * (delta.x * HandSensitivity) + up * (delta.y * HandSensitivity);

            // Arrow keys (or IJKL) provide a complete mouse-free fallback. Movement is in
            // the player's view plane so the same controls work after looking around.
            Vector3 keyboardMove = Vector3.zero;
            if (DesktopInput.Held(SimKey.HandLeft)) keyboardMove -= right;
            if (DesktopInput.Held(SimKey.HandRight)) keyboardMove += right;
            if (DesktopInput.Held(SimKey.HandForward)) keyboardMove += up;
            if (DesktopInput.Held(SimKey.HandBack)) keyboardMove -= up;
            if (keyboardMove.sqrMagnitude > 0.0001f)
                hand.Position += keyboardMove.normalized * (HandSensitivity * 650f * Time.unscaledDeltaTime);

            float scroll = DesktopInput.Scroll;
            if (Mathf.Abs(scroll) > 0.001f)
                hand.Position += Flatten(_headRotation * Vector3.forward) * (scroll * HandDepthStep);

            // Keep the hand within arm's reach so it cannot be lost behind the player.
            Vector3 fromHead = hand.Position - _headPosition;
            float distance = fromHead.magnitude;
            if (distance > 0.0001f)
            {
                float clamped = Mathf.Clamp(distance, MinHandDistance, MaxHandDistance);
                hand.Position = _headPosition + fromHead / distance * clamped;
            }

            _hands[(int)side] = hand;
        }

        public void Recentre()
        {
            if (Head == null) return;

            FrameRink();
            _headStart = _headPosition;
            _headStartRotation = _headRotation;

            Vector3 euler = _headStartRotation.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;

            PlaceHandsInFrontOfHead();
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
        }

        private void OnGUI()
        {
            if (!ShowHelp || !_started) return;

            const float width = 330f;
            var area = new Rect(12f, 12f, width, 166f);

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Box(area, GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, width - 20f, area.height - 16f));
            GUILayout.Label("DESKTOP VR SIMULATOR");
            GUILayout.Label("Right-drag: look    W A S D: move    Q/E: down/up");
            GUILayout.Label("Hold SHIFT: move LEFT hand with mouse");
            GUILayout.Label("Hold SPACE: move RIGHT hand with mouse");
            GUILayout.Label("Arrow keys / IJKL: move RIGHT hand (keyboard)");
            GUILayout.Label("Mouse wheel: reach further / closer");
            GUILayout.Label("Left mouse / CTRL: pinch (grab, pull back, release)");
            GUILayout.Label("R: recentre     H: hide this");
            GUILayout.EndArea();
        }
    }
}
