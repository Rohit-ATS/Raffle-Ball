using UnityEngine;

namespace RiftRally.Presentation
{
    /// <summary>
    /// Brief, eased time dilation on the moments that decide a round.
    ///
    /// Two rules make this safe in VR, and both are non-negotiable. First, the dilation only
    /// ever touches <see cref="Time.timeScale"/>, never the camera or the head pose - slowing
    /// or moving a player's view is a direct route to motion sickness, whereas slowing the
    /// world while the head stays live is comfortable and reads as drama. Second, it always
    /// returns to full speed on a timer, so a missed call cannot leave the game crawling.
    ///
    /// It also deliberately refuses to stack: the strongest active request wins, instead of
    /// several near-simultaneous collisions multiplying into a freeze.
    ///
    /// Dilation is applied as a multiplier on whatever time scale was already in force when
    /// this component woke, rather than by writing an absolute value. That means an automated
    /// test (or a debug fast-forward) can run the match at eight times speed and still get
    /// the real dilation behaviour layered on top, instead of having its time scale silently
    /// stamped back to one.
    /// </summary>
    public class SlowMotion : MonoBehaviour
    {
        private static SlowMotion _instance;

        [Tooltip("Slowest the game is ever allowed to run.")]
        public float FloorScale = 0.25f;

        [Tooltip("How quickly time eases into and out of the effect.")]
        public float BlendSharpness = 9f;

        private float _targetScale = 1f;
        private float _holdUntil;
        private float _baseFixedDelta;
        private float _baseTimeScale = 1f;

        /// <summary>True while the game is running slower than its normal speed.</summary>
        public static bool IsActive => _instance != null && _instance._dilation < 0.995f;

        private float _dilation = 1f;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
            _baseFixedDelta = Time.fixedDeltaTime;
            _baseTimeScale = Mathf.Max(0.01f, Time.timeScale);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;
            Time.timeScale = _baseTimeScale;
            Time.fixedDeltaTime = _baseFixedDelta;
        }

        /// <summary>
        /// Asks for time to slow to <paramref name="scale"/> for <paramref name="seconds"/>.
        /// Ignored if a stronger request is already running.
        /// </summary>
        public static void Request(float scale, float seconds)
        {
            if (_instance == null) return;
            _instance.RequestInternal(scale, seconds);
        }

        /// <summary>Drops any active dilation immediately, easing back to full speed.</summary>
        public static void Clear()
        {
            if (_instance == null) return;
            _instance._targetScale = 1f;
            _instance._holdUntil = 0f;
        }

        private void RequestInternal(float scale, float seconds)
        {
            scale = Mathf.Clamp(scale, FloorScale, 1f);

            bool stronger = scale < _targetScale;
            bool expired = Time.unscaledTime > _holdUntil;

            if (stronger || expired) _targetScale = scale;
            _holdUntil = Mathf.Max(_holdUntil, Time.unscaledTime + seconds);
        }

        private void Update()
        {
            if (Time.unscaledTime > _holdUntil) _targetScale = 1f;

            // Eased in unscaled time, or the ease itself would slow down as it took effect.
            _dilation = Easing.Damp(_dilation, _targetScale, BlendSharpness,
                                    Time.unscaledDeltaTime);
            if (_dilation > 0.999f) _dilation = 1f;

            Time.timeScale = _baseTimeScale * _dilation;

            // The physics step is scaled to match, so collisions stay just as accurate while
            // slowed instead of being integrated in coarser chunks.
            Time.fixedDeltaTime = _baseFixedDelta * Mathf.Max(0.05f, _dilation);
        }
    }
}
