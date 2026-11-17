using UnityEngine;

namespace RiftRally.Core
{
    /// <summary>
    /// A one-euro filter: a low-pass filter whose cutoff rises with speed.
    ///
    /// This is the single highest-value piece of code for how the game feels. Raw hand-tracked
    /// joint positions jitter by several millimetres per frame, and a plain low-pass filter
    /// trades that jitter for lag - which on a throw is worse, because the stone leaves late.
    /// The one-euro filter smooths hard while the hand is still (where jitter is visible and
    /// lag is not) and barely at all while the hand is moving fast (where lag is visible and
    /// jitter is not), so a held stone sits rock steady and a quick pull still tracks the hand.
    ///
    /// Casiez, Roussel and Vogel, CHI 2012.
    /// </summary>
    public class OneEuroFilter
    {
        /// <summary>
        /// Cutoff in Hz while the hand is still. Low enough to kill tracker jitter.
        /// </summary>
        public const float DefaultMinCutoff = 1.2f;

        /// <summary>
        /// How aggressively the cutoff opens up with speed.
        ///
        /// This number is unit-dependent and that is the trap: the original paper's value of
        /// ~0.007 assumes a signal measured in pixels, where a quick movement has a
        /// derivative in the hundreds. Here the signal is metres, so a quick hand movement
        /// has a derivative of about 1, and a beta in the thousandths leaves the filter
        /// effectively fixed at its minimum cutoff - which measured as 86 mm of lag on a
        /// normal pull, enough to throw the stone late. Scaled for metres it belongs in the
        /// single digits.
        /// </summary>
        public const float DefaultBeta = 9.0f;

        /// <summary>Cutoff for the speed estimate itself.</summary>
        public const float DefaultDerivativeCutoff = 2.0f;

        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _derivativeCutoff;

        private bool _hasPrevious;
        private float _previousValue;
        private float _previousDerivative;

        /// <param name="minCutoff">Cutoff in Hz while still. Lower is smoother and laggier.</param>
        /// <param name="beta">How aggressively the cutoff opens up with speed.</param>
        /// <param name="derivativeCutoff">Cutoff for the speed estimate itself.</param>
        public OneEuroFilter(float minCutoff = DefaultMinCutoff, float beta = DefaultBeta,
                             float derivativeCutoff = DefaultDerivativeCutoff)
        {
            _minCutoff = Mathf.Max(0.0001f, minCutoff);
            _beta = beta;
            _derivativeCutoff = Mathf.Max(0.0001f, derivativeCutoff);
        }

        public void Reset()
        {
            _hasPrevious = false;
            _previousValue = 0f;
            _previousDerivative = 0f;
        }

        public float Filter(float value, float deltaTime)
        {
            if (deltaTime <= 0f) return _hasPrevious ? _previousValue : value;

            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _previousValue = value;
                _previousDerivative = 0f;
                return value;
            }

            float rate = 1f / deltaTime;

            // Filter the derivative first; a noisy speed estimate would make the adaptive
            // cutoff flap and the output would breathe.
            float rawDerivative = (value - _previousValue) * rate;
            float derivative = Lowpass(rawDerivative, _previousDerivative,
                                       Alpha(_derivativeCutoff, rate));
            _previousDerivative = derivative;

            float cutoff = _minCutoff + _beta * Mathf.Abs(derivative);
            float filtered = Lowpass(value, _previousValue, Alpha(cutoff, rate));
            _previousValue = filtered;
            return filtered;
        }

        private static float Alpha(float cutoff, float rate)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            float te = 1f / rate;
            return 1f / (1f + tau / te);
        }

        private static float Lowpass(float value, float previous, float alpha)
            => alpha * value + (1f - alpha) * previous;
    }

    /// <summary>Three one-euro filters, for positions and directions.</summary>
    public class OneEuroFilterVector3
    {
        private readonly OneEuroFilter _x;
        private readonly OneEuroFilter _y;
        private readonly OneEuroFilter _z;

        public OneEuroFilterVector3(float minCutoff = OneEuroFilter.DefaultMinCutoff,
                                    float beta = OneEuroFilter.DefaultBeta,
                                    float derivativeCutoff = OneEuroFilter.DefaultDerivativeCutoff)
        {
            _x = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            _y = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
            _z = new OneEuroFilter(minCutoff, beta, derivativeCutoff);
        }

        public void Reset()
        {
            _x.Reset();
            _y.Reset();
            _z.Reset();
        }

        public Vector3 Filter(Vector3 value, float deltaTime) => new Vector3(
            _x.Filter(value.x, deltaTime),
            _y.Filter(value.y, deltaTime),
            _z.Filter(value.z, deltaTime));
    }

    /// <summary>
    /// A short ring buffer of positions, used to estimate hand velocity over a window rather
    /// than from the last frame alone. A single-frame difference on tracked-hand data is
    /// almost pure noise; a 60-80 ms window is stable enough to throw with.
    /// </summary>
    public class VelocityEstimator
    {
        private readonly Vector3[] _positions;
        private readonly float[] _times;
        private int _count;
        private int _head;

        public VelocityEstimator(int samples = 8)
        {
            samples = Mathf.Max(2, samples);
            _positions = new Vector3[samples];
            _times = new float[samples];
        }

        public void Reset()
        {
            _count = 0;
            _head = 0;
        }

        public void Add(Vector3 position, float time)
        {
            _positions[_head] = position;
            _times[_head] = time;
            _head = (_head + 1) % _positions.Length;
            if (_count < _positions.Length) _count++;
        }

        /// <summary>
        /// Average velocity across the window. Returns zero until there are enough samples,
        /// which is deliberate: a throw released on the first tracked frame would be random.
        /// </summary>
        public Vector3 Velocity
        {
            get
            {
                if (_count < 2) return Vector3.zero;

                int newest = (_head - 1 + _positions.Length) % _positions.Length;
                int oldest = (_head - _count + _positions.Length) % _positions.Length;

                float dt = _times[newest] - _times[oldest];
                if (dt <= 0.0001f) return Vector3.zero;

                return (_positions[newest] - _positions[oldest]) / dt;
            }
        }

        /// <summary>Highest instantaneous speed in the window, for gesture detection.</summary>
        public float PeakSpeed
        {
            get
            {
                if (_count < 2) return 0f;
                float peak = 0f;
                for (int i = 1; i < _count; i++)
                {
                    int a = (_head - _count + i - 1 + _positions.Length) % _positions.Length;
                    int b = (_head - _count + i + _positions.Length) % _positions.Length;
                    float dt = _times[b] - _times[a];
                    if (dt <= 0.0001f) continue;
                    peak = Mathf.Max(peak, (_positions[b] - _positions[a]).magnitude / dt);
                }
                return peak;
            }
        }
    }
}
