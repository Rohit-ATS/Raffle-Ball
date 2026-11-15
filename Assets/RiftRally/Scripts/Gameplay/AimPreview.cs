using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Gameplay
{
    /// <summary>
    /// Draws where the stone is going to go.
    ///
    /// This is the single most important readability feature in the game: with hand
    /// tracking the player cannot feel the throw, so they need to see it. The path is
    /// predicted by stepping the same friction model the stone uses and reflecting off
    /// the side walls, so what is drawn matches what happens.
    /// </summary>
    public class AimPreview : MonoBehaviour
    {
        [Header("References")]
        public Rink Playfield;
        public LineRenderer Path;
        public Transform PowerPip;

        [Header("Prediction")]
        [Tooltip("Simulation step. Smaller is smoother but costs more points.")]
        public float StepTime = 0.045f;

        [Tooltip("Maximum number of points in the predicted path.")]
        public int MaxPoints = 48;

        [Tooltip("How many wall bounces to predict.")]
        public int MaxBounces = 2;

        [Header("Look")]
        public float IdleHintLength = 0.18f;
        public Gradient PowerGradient;

        private Vector3[] _points;

        private void Awake()
        {
            _points = new Vector3[MaxPoints];
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();
            Hide();
        }

        public void Hide()
        {
            if (Path != null) Path.enabled = false;
            if (PowerPip != null) PowerPip.gameObject.SetActive(false);
        }

        /// <summary>A soft nudge shown while the stone is docked, hinting "throw that way".</summary>
        public void ShowIdleHint(Vector3 origin, Vector3 forward)
        {
            if (Path == null || Playfield == null) return;

            Path.enabled = true;
            Path.positionCount = 2;
            float y = Playfield.SurfaceY + 0.004f;

            Vector3 a = new Vector3(origin.x, y, origin.z);
            Vector3 b = a + forward.normalized * IdleHintLength;
            Path.SetPosition(0, a);
            Path.SetPosition(1, b);

            Color c = new Color(1f, 1f, 1f, 0.22f);
            Path.startColor = c;
            Path.endColor = new Color(1f, 1f, 1f, 0f);
            Path.widthMultiplier = 0.006f;

            if (PowerPip != null) PowerPip.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows the full predicted path for a pending throw.
        /// </summary>
        /// <param name="origin">Current stone position.</param>
        /// <param name="direction">Launch direction (world, horizontal).</param>
        /// <param name="speed">Launch speed in m/s.</param>
        /// <param name="power01">Normalised pull strength, for colour and the power pip.</param>
        /// <param name="teamColor">Owning player's colour.</param>
        public void Show(Vector3 origin, Vector3 direction, float speed, float power01, Color teamColor)
        {
            if (Path == null || Playfield == null) return;

            int count = Predict(origin, direction, speed);
            if (count < 2) { Hide(); return; }

            Path.enabled = true;
            Path.positionCount = count;
            for (int i = 0; i < count; i++) Path.SetPosition(i, _points[i]);

            Color hot = Color.Lerp(teamColor, Color.white, 0.35f);
            Path.startColor = new Color(hot.r, hot.g, hot.b, Mathf.Lerp(0.45f, 1f, power01));
            Path.endColor = new Color(teamColor.r, teamColor.g, teamColor.b, 0.05f);
            Path.widthMultiplier = Mathf.Lerp(0.005f, 0.011f, power01);

            if (PowerPip != null)
            {
                PowerPip.gameObject.SetActive(true);
                PowerPip.position = _points[count - 1];
                float s = Mathf.Lerp(0.012f, 0.03f, power01);
                PowerPip.localScale = new Vector3(s, 0.002f, s);
            }
        }

        /// <summary>
        /// Steps the stone's own motion model forward: constant-deceleration friction with
        /// reflections off the side walls. Returns how many points were written.
        /// </summary>
        private int Predict(Vector3 origin, Vector3 direction, float speed)
        {
            Transform surface = Playfield.Surface;
            Vector3 local = surface.InverseTransformPoint(origin);
            Vector3 localDir = surface.InverseTransformDirection(direction);

            Vector2 pos = new Vector2(local.x, local.z);
            Vector2 vel = new Vector2(localDir.x, localDir.z).normalized * speed;

            float halfW = GameConfig.RinkWidth * 0.5f - GameConfig.StoneRadius;
            float halfL = GameConfig.RinkLength * 0.5f - GameConfig.StoneRadius;
            float y = 0.004f;

            int bounces = 0;
            int n = 0;
            _points[n++] = surface.TransformPoint(new Vector3(pos.x, y, pos.y));

            for (int step = 1; step < MaxPoints; step++)
            {
                float sp = vel.magnitude;
                if (sp <= 0.02f) break;

                pos += vel * StepTime;

                // Friction, matching Stone.FixedUpdate.
                float newSpeed = Mathf.Max(0f, sp - GameConfig.IceFriction * StepTime);
                vel = vel.normalized * newSpeed;

                // Reflect off the long side walls.
                if (Mathf.Abs(pos.x) > halfW)
                {
                    if (bounces++ >= MaxBounces) break;
                    pos.x = Mathf.Sign(pos.x) * halfW;
                    vel.x = -vel.x * 0.88f;
                }

                // Far wall: the stone would bounce back off the end board.
                if (pos.y > halfL)
                {
                    if (bounces++ >= MaxBounces) break;
                    pos.y = halfL;
                    vel.y = -vel.y * 0.72f;
                }

                // Behind the near edge: it has left play, stop drawing.
                if (pos.y < -halfL) break;

                _points[n++] = surface.TransformPoint(new Vector3(pos.x, y, pos.y));
                if (n >= MaxPoints) break;
            }

            return n;
        }
    }
}
