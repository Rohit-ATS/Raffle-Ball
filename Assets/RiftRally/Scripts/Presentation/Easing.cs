using UnityEngine;

namespace RiftRally.Presentation
{
    /// <summary>
    /// Easing curves and frame-rate independent smoothing.
    ///
    /// Everything that moves in the HUD goes through these rather than a linear lerp. Linear
    /// motion is the single most recognisable sign of unfinished work: it starts and stops
    /// abruptly, and in VR, where the player's head is also moving, it reads as a glitch
    /// rather than an animation.
    /// </summary>
    public static class Easing
    {
        /// <summary>Slow in, slow out. The default for almost everything.</summary>
        public static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Fast out of the gate, settling at the end. Good for things appearing.</summary>
        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return t * t * t + 1f;
        }

        /// <summary>Accelerating. Good for things leaving.</summary>
        public static float InCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        /// <summary>Overshoots and comes back. Used for score pops and banners.</summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return t * t * ((overshoot + 1f) * t + overshoot) + 1f;
        }

        /// <summary>Springy settle. Used when a stone docks and when a ring is struck.</summary>
        public static float OutElastic(float t, float period = 0.35f)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            float s = period / 4f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - s) * (2f * Mathf.PI) / period) + 1f;
        }

        /// <summary>A 0-1-0 pulse, for flashes and highlights.</summary>
        public static float Pulse(float t)
        {
            t = Mathf.Clamp01(t);
            return Mathf.Sin(t * Mathf.PI);
        }

        /// <summary>
        /// Exponential smoothing that behaves identically at 72, 90 and 120 Hz.
        ///
        /// A plain <c>Lerp(a, b, 0.1f)</c> in Update is a frame-rate dependent bug: the same
        /// code chases twice as fast at double the frame rate, so the game feels different on
        /// a Quest 2 than on a Quest 3. This is the fix, and it is used everywhere.
        /// </summary>
        public static float Damp(float current, float target, float sharpness, float deltaTime)
            => Mathf.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));

        public static Vector3 Damp(Vector3 current, Vector3 target, float sharpness,
                                   float deltaTime)
            => Vector3.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));

        public static Color Damp(Color current, Color target, float sharpness, float deltaTime)
            => Color.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));

        public static Quaternion Damp(Quaternion current, Quaternion target, float sharpness,
                                      float deltaTime)
            => Quaternion.Slerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));

        /// <summary>
        /// A critically damped spring. Used for the held stone, where a plain damped chase
        /// either lags behind the hand or arrives with a visible snap; a spring arrives fast
        /// and stops dead with no overshoot.
        /// </summary>
        public static Vector3 Spring(Vector3 current, ref Vector3 velocity, Vector3 target,
                                     float frequency, float deltaTime)
        {
            if (deltaTime <= 0f) return current;

            float omega = 2f * Mathf.PI * Mathf.Max(0.0001f, frequency);
            float exp = Mathf.Exp(-omega * deltaTime);

            Vector3 change = current - target;
            Vector3 temp = (velocity + omega * change) * deltaTime;

            velocity = (velocity - omega * temp) * exp;
            return target + (change + temp) * exp;
        }
    }
}
