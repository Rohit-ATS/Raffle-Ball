using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Gameplay
{
    public enum LauncherState
    {
        Inactive,   // not this player's turn
        Ready,      // a stone is docked and waiting to be grabbed
        Aiming,     // stone is pinched and being pulled back
        Thrown      // stone released, waiting for the round to move on
    }

    /// <summary>
    /// Turns hand-tracked pinches into throws.
    ///
    /// The throw is a slingshot: pinch the docked stone, pull it back, release. This was
    /// chosen over a velocity-based flick on purpose. Hand-tracking velocity is noisy and
    /// a mis-read spike would fire a stone off the rink, whereas a pull-back offset is a
    /// stable, readable quantity the player can see and correct before letting go.
    /// </summary>
    public class StoneLauncher : MonoBehaviour
    {
        [Header("References")]
        public Rink Playfield;
        public GameObject StonePrefab;
        public AimPreview Preview;

        [Header("Feel")]
        [Tooltip("How quickly the held stone chases the pinch point. Lower = smoother, laggier.")]
        public float FollowSharpness = 22f;

        [Tooltip("Minimum pull before a release counts as a throw rather than a cancel.")]
        public float MinimumPull = 0.05f;

        public LauncherState State { get; private set; } = LauncherState.Inactive;
        public PlayerSide CurrentPlayer { get; private set; } = PlayerSide.One;
        public Stone ActiveStone { get; private set; }

        /// <summary>Raised the moment a stone is released. Arguments: stone, launch speed.</summary>
        public event System.Action<Stone, float> StoneThrown;

        /// <summary>Raised when a stone is first grabbed, for tutorial and audio cues.</summary>
        public event System.Action StoneGrabbed;

        private Vector3 _anchor;
        private Vector3 _heldTarget;
        private HandSide _grabHand;
        private bool _subscribed;

        private void Start()
        {
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();
            TrySubscribe();
        }

        private void OnEnable() => TrySubscribe();

        private void OnDisable()
        {
            if (HandInput.Instance != null && _subscribed)
            {
                HandInput.Instance.PinchStarted -= OnPinchStarted;
                HandInput.Instance.PinchEnded -= OnPinchEnded;
                _subscribed = false;
            }
        }

        private void TrySubscribe()
        {
            if (_subscribed || HandInput.Instance == null) return;
            HandInput.Instance.PinchStarted += OnPinchStarted;
            HandInput.Instance.PinchEnded += OnPinchEnded;
            _subscribed = true;
        }

        // ---------------------------------------------------------------- turn control

        /// <summary>Docks a fresh stone for the given player and waits for a grab.</summary>
        public void BeginTurn(PlayerSide player)
        {
            CurrentPlayer = player;
            _anchor = Playfield.LaunchPointWorld(player);

            SpawnDockedStone(player);
            State = LauncherState.Ready;
        }

        /// <summary>
        /// Spawns and immediately launches a stone on behalf of the AI. Routed through the
        /// launcher so that AI and human throws create stones the exact same way.
        /// </summary>
        public Stone ThrowForAi(PlayerSide player, Vector3 direction, float speed)
        {
            CurrentPlayer = player;
            _anchor = Playfield.LaunchPointWorld(player);

            SpawnDockedStone(player);
            if (ActiveStone == null) return null;

            direction.y = 0f;
            ActiveStone.Launch(direction.normalized * speed, 0f);
            State = LauncherState.Thrown;
            StoneThrown?.Invoke(ActiveStone, speed);
            return ActiveStone;
        }

        /// <summary>Stops accepting input, e.g. between rounds or during a cut-scene.</summary>
        public void EndTurn()
        {
            State = LauncherState.Inactive;
            ActiveStone = null;
            if (Preview != null) Preview.Hide();
        }

        private void SpawnDockedStone(PlayerSide player)
        {
            if (StonePrefab == null)
            {
                Debug.LogError("StoneLauncher has no StonePrefab assigned.");
                return;
            }

            GameObject go = Instantiate(StonePrefab, _anchor, Quaternion.identity);
            go.name = $"Stone_{player}_{Time.frameCount}";

            // The source may be an inactive template parked off-rink, so copies are woken up
            // explicitly rather than relying on the prefab's own active state.
            if (!go.activeSelf) go.SetActive(true);

            ActiveStone = go.GetComponent<Stone>();
            if (ActiveStone == null)
            {
                Debug.LogError("StonePrefab is missing a Stone component.");
                Destroy(go);
                return;
            }

            ActiveStone.Initialise(player);
            ActiveStone.MoveTo(_anchor);
            _heldTarget = _anchor;
        }

        // ---------------------------------------------------------------- input

        private void OnPinchStarted(HandSide side)
        {
            if (State != LauncherState.Ready || ActiveStone == null) return;

            var sample = HandInput.Instance.Get(side);
            if (!sample.IsTracked) return;

            // Only grab if the pinch happened near the docked stone.
            float reach = Vector3.Distance(sample.PinchPoint, ActiveStone.transform.position);
            if (reach > GameConfig.GrabRadius) return;

            _grabHand = side;
            _heldTarget = ActiveStone.transform.position;
            ActiveStone.SetState(StoneState.Held);
            State = LauncherState.Aiming;
            StoneGrabbed?.Invoke();
        }

        private void OnPinchEnded(HandSide side)
        {
            if (State != LauncherState.Aiming || side != _grabHand || ActiveStone == null) return;

            Vector3 offset = PlanarOffsetFromAnchor(ActiveStone.transform.position);
            float pull = offset.magnitude;

            if (pull < MinimumPull)
            {
                // Treated as a cancel: put the stone back and let the player try again.
                ActiveStone.SetState(StoneState.Docked);
                ActiveStone.MoveTo(_anchor);
                _heldTarget = _anchor;
                State = LauncherState.Ready;
                if (Preview != null) Preview.Hide();
                return;
            }

            float t = Mathf.Clamp01(pull / GameConfig.MaxPullDistance);
            float speed = Mathf.Lerp(GameConfig.MinLaunchSpeed, GameConfig.MaxLaunchSpeed, t);
            Vector3 direction = (-offset).normalized;

            // A touch of spin, signed by how far off-centre the pull was, so throws feel alive.
            Vector3 localOffset = Playfield.Surface.InverseTransformDirection(offset);
            float spin = Mathf.Clamp(-localOffset.x * 9f, -4.5f, 4.5f);

            ActiveStone.Launch(direction * speed, spin);
            State = LauncherState.Thrown;
            if (Preview != null) Preview.Hide();

            StoneThrown?.Invoke(ActiveStone, speed);
        }

        private void Update()
        {
            TrySubscribe();

            if (State == LauncherState.Aiming && ActiveStone != null)
            {
                UpdateAiming();
            }
            else if (State == LauncherState.Ready && ActiveStone != null && Preview != null)
            {
                Preview.ShowIdleHint(ActiveStone.transform.position, Playfield.DownRink);
            }
        }

        private void UpdateAiming()
        {
            var sample = HandInput.Instance.Get(_grabHand);
            if (!sample.IsTracked)
            {
                // Lost tracking mid-pull: dock the stone instead of firing something random.
                ActiveStone.SetState(StoneState.Docked);
                ActiveStone.MoveTo(_anchor);
                State = LauncherState.Ready;
                if (Preview != null) Preview.Hide();
                return;
            }

            Vector3 desired = ClampToPullZone(sample.PinchPoint);
            _heldTarget = Vector3.Lerp(_heldTarget, desired,
                                       1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            ActiveStone.MoveTo(_heldTarget);

            Vector3 offset = PlanarOffsetFromAnchor(_heldTarget);
            float pull = offset.magnitude;
            float t = Mathf.Clamp01(pull / GameConfig.MaxPullDistance);

            if (Preview != null)
            {
                Vector3 dir = pull > 0.0025f ? (-offset).normalized : Playfield.DownRink;
                float speed = Mathf.Lerp(GameConfig.MinLaunchSpeed, GameConfig.MaxLaunchSpeed, t);
                Preview.Show(ActiveStone.transform.position, dir, speed, t,
                             CurrentPlayer.ToColor());
            }
        }

        /// <summary>
        /// Keeps the held stone inside a slingshot zone: behind the launch anchor, within the
        /// rink walls, and no further back than the maximum pull.
        /// </summary>
        private Vector3 ClampToPullZone(Vector3 worldPoint)
        {
            Vector3 local = Playfield.Surface.InverseTransformPoint(worldPoint);
            Vector3 anchorLocal = Playfield.Surface.InverseTransformPoint(_anchor);

            float halfW = GameConfig.RinkWidth * 0.5f - GameConfig.StoneRadius;
            local.x = Mathf.Clamp(local.x, -halfW, halfW);

            // Never in front of the anchor: this is a pull-back, not a push.
            local.z = Mathf.Min(local.z, anchorLocal.z);

            Vector3 offset = new Vector3(local.x - anchorLocal.x, 0f, local.z - anchorLocal.z);
            if (offset.magnitude > GameConfig.MaxPullDistance)
                offset = offset.normalized * GameConfig.MaxPullDistance;

            local.x = anchorLocal.x + offset.x;
            local.z = anchorLocal.z + offset.z;
            local.y = anchorLocal.y;

            return Playfield.Surface.TransformPoint(local);
        }

        private Vector3 PlanarOffsetFromAnchor(Vector3 worldPoint)
        {
            Vector3 offset = worldPoint - _anchor;
            Vector3 up = Playfield.Surface.up;
            return offset - Vector3.Project(offset, up);
        }
    }
}
