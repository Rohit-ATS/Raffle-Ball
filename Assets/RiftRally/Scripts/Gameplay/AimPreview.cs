using UnityEngine;
using RiftRally.Core;
using RiftRally.Modifiers;
using RiftRally.Presentation;
using RiftRally.Setup;

namespace RiftRally.Gameplay
{
    /// <summary>
    /// Draws where the stone is going to go.
    ///
    /// This is the single most important readability feature in the game: with hand tracking
    /// the player cannot feel the throw, so they need to see it. Three things make the preview
    /// trustworthy rather than decorative:
    ///
    /// 1. The path is produced by stepping the stone's own motion model - the same
    ///    constant-deceleration friction, the same wall reflections - so the drawn line is a
    ///    genuine prediction rather than a straight arrow.
    /// 2. The active rink modifier is applied during the prediction. A cross current that
    ///    bends every real shot but not the preview would be worse than drawing nothing,
    ///    because the player would learn to distrust the line.
    /// 3. The predicted resting place is marked with a ring. Power, not direction, is what
    ///    players misjudge, and a stop marker turns an abstract pull distance into "my stone
    ///    lands there".
    /// </summary>
    public class AimPreview : MonoBehaviour
    {
        [Header("References")]
        public Rink Playfield;
        public LineRenderer Path;
        public Transform PowerPip;

        [Tooltip("Marks where the stone is predicted to stop.")]
        public Transform StopRing;

        [Tooltip("Small markers placed where the path is predicted to hit a wall.")]
        public Transform[] BounceMarkers;

        [Tooltip("Modifier director, so the live modifier can bend the prediction.")]
        public ModifierDirector Modifiers;

        [Header("Prediction")]
        [Tooltip("Simulation step. Smaller is smoother but costs more points.")]
        public float StepTime = 0.030f;

        [Tooltip("Maximum number of points in the predicted path.")]
        public int MaxPoints = 96;

        [Tooltip("How many wall bounces to predict.")]
        public int MaxBounces = 2;

        [Header("Look")]
        public float IdleHintLength = 0.18f;

        private Vector3[] _points;
        private int _bounceCount;
        private readonly Vector3[] _bouncePoints = new Vector3[4];
        private Vector3 _stopPoint;
        private bool _haveStopPoint;

        private Material _pathMaterial;
        private Material _stopMaterial;
        private float _shown;

        private void Awake()
        {
            _points = new Vector3[Mathf.Max(8, MaxPoints)];
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();
            if (Modifiers == null) Modifiers = FindFirstObjectByType<ModifierDirector>();

            if (Path != null) _pathMaterial = Path.material;
            if (StopRing != null)
            {
                var renderer = StopRing.GetComponent<Renderer>();
                if (renderer != null) _stopMaterial = renderer.material;
            }

            Hide();
        }

        public void Hide()
        {
            if (Path != null) Path.enabled = false;
            if (PowerPip != null) PowerPip.gameObject.SetActive(false);
            if (StopRing != null) StopRing.gameObject.SetActive(false);
            HideBounceMarkers(0);
            _shown = 0f;
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

            // Breathing alpha, so the hint reads as a prompt rather than as a live aim line.
            float breathe = 0.12f + Mathf.Abs(Mathf.Sin(Time.time * 1.8f)) * 0.14f;
            // Scaled towards black rather than towards transparent: the line material is
            // additive, so alpha alone would leave the hint at full brightness.
            Color c = new Color(breathe, breathe, breathe, breathe);
            Path.startColor = c;
            Path.endColor = new Color(0f, 0f, 0f, 0f);
            Path.widthMultiplier = 0.005f;

            if (PowerPip != null) PowerPip.gameObject.SetActive(false);
            if (StopRing != null) StopRing.gameObject.SetActive(false);
            HideBounceMarkers(0);
        }

        /// <summary>Shows the full predicted path for a pending throw.</summary>
        /// <param name="origin">Current stone position.</param>
        /// <param name="direction">Launch direction (world, horizontal).</param>
        /// <param name="speed">Launch speed in m/s.</param>
        /// <param name="power01">Normalised pull strength, for colour and the power pip.</param>
        /// <param name="teamColor">Owning player's colour.</param>
        public void Show(Vector3 origin, Vector3 direction, float speed, float power01,
                         Color teamColor)
        {
            if (Path == null || Playfield == null) return;

            int count = Predict(origin, direction, speed);
            if (count < 2) { Hide(); return; }

            _shown = Easing.Damp(_shown, 1f, 18f, Time.deltaTime);

            Path.enabled = true;
            Path.positionCount = count;
            for (int i = 0; i < count; i++) Path.SetPosition(i, _points[i]);

            // Hot at the stone, cooling along the line: the eye follows the gradient out to
            // where the stone will end up.
            Color hot = Color.Lerp(teamColor, Color.white, 0.45f);
            float head = Mathf.Lerp(0.40f, 1f, power01) * _shown;
            Path.startColor = new Color(hot.r * head, hot.g * head, hot.b * head, head);
            Path.endColor = new Color(0f, 0f, 0f, 0f);
            Path.widthMultiplier = Mathf.Lerp(0.0045f, 0.011f, power01);

            if (_pathMaterial != null && _pathMaterial.HasProperty("_EmissionColor"))
                _pathMaterial.SetColor("_EmissionColor", hot * Mathf.Lerp(1.2f, 3.2f, power01));

            UpdatePowerPip(power01, teamColor);
            UpdateStopRing(power01, teamColor);
            ShowBounceMarkers(teamColor);
        }

        private void UpdatePowerPip(float power01, Color teamColor)
        {
            if (PowerPip == null) return;

            PowerPip.gameObject.SetActive(true);
            PowerPip.position = _haveStopPoint ? _stopPoint : _points[Mathf.Max(0, Path.positionCount - 1)];

            float s = Mathf.Lerp(0.012f, 0.03f, power01);
            // A touch of pulse at full power tells the player they have run out of pull.
            float overdrive = power01 > 0.97f ? 1f + Mathf.Sin(Time.time * 14f) * 0.12f : 1f;
            PowerPip.localScale = new Vector3(s * overdrive, 0.002f, s * overdrive);
        }

        private void UpdateStopRing(float power01, Color teamColor)
        {
            if (StopRing == null) return;

            if (!_haveStopPoint)
            {
                StopRing.gameObject.SetActive(false);
                return;
            }

            StopRing.gameObject.SetActive(true);
            StopRing.position = _stopPoint;
            StopRing.rotation = Quaternion.Euler(0f, Time.time * 24f, 0f);

            float grow = Easing.OutBack(Mathf.Clamp01(_shown));
            StopRing.localScale = Vector3.one * grow;

            if (_stopMaterial == null) return;

            // The ring turns the target colour when the predicted stop lands in the house,
            // which is the clearest possible "this shot scores" signal.
            float distance = Playfield.DistanceToTarget(_stopPoint);
            bool scoring = distance <= GameConfig.RingRadii[0];
            Color c = scoring ? GameConfig.TargetColor : teamColor;

            ProceduralArt.SetGlowFade(_stopMaterial, c, 0.8f * _shown, scoring ? 3.4f : 1.6f);
        }

        private void ShowBounceMarkers(Color teamColor)
        {
            if (BounceMarkers == null) return;

            for (int i = 0; i < BounceMarkers.Length; i++)
            {
                var marker = BounceMarkers[i];
                if (marker == null) continue;

                bool used = i < _bounceCount;
                marker.gameObject.SetActive(used);
                if (!used) continue;

                marker.position = _bouncePoints[i];
                float pop = 1f + Mathf.Sin(Time.time * 8f + i) * 0.1f;
                marker.localScale = Vector3.one * pop;
            }
        }

        private void HideBounceMarkers(int from)
        {
            if (BounceMarkers == null) return;
            for (int i = from; i < BounceMarkers.Length; i++)
                if (BounceMarkers[i] != null) BounceMarkers[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// Steps the stone's own motion model forward: constant-deceleration friction, the
        /// live modifier's influence, and reflections off the side and far walls. Returns how
        /// many points were written, and records the bounce points and the resting place.
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
            const float y = 0.004f;

            // The live current, expressed in rink-local terms, so the preview bends the same
            // way the stone will.
            var current = Modifiers != null ? Modifiers.Current as CurrentModifier : null;
            bool hasCurrent = current != null && current.IsActive;
            float currentSign = 0f;
            if (hasCurrent)
            {
                Vector3 flowLocal = surface.InverseTransformDirection(current.FlowDirection);
                currentSign = Mathf.Sign(flowLocal.x) * current.Strength;
            }

            _bounceCount = 0;
            _haveStopPoint = false;

            int n = 0;
            _points[n++] = surface.TransformPoint(new Vector3(pos.x, y, pos.y));

            for (int step = 1; step < _points.Length; step++)
            {
                float sp = vel.magnitude;
                if (sp <= 0.02f)
                {
                    _stopPoint = surface.TransformPoint(new Vector3(pos.x, y, pos.y));
                    _haveStopPoint = true;
                    break;
                }

                if (hasCurrent && Mathf.Abs(pos.y - current.BandCenterZ) <= current.BandHalfDepth)
                {
                    vel.x += currentSign * StepTime;
                }

                pos += vel * StepTime;

                // Friction, matching Stone.FixedUpdate.
                float newSpeed = Mathf.Max(0f, vel.magnitude - GameConfig.IceFriction * StepTime);
                vel = vel.sqrMagnitude > 0.000001f ? vel.normalized * newSpeed : Vector2.zero;

                // Reflect off the long side walls.
                if (Mathf.Abs(pos.x) > halfW)
                {
                    if (_bounceCount >= MaxBounces) break;
                    pos.x = Mathf.Sign(pos.x) * halfW;
                    vel.x = -vel.x * 0.88f;
                    RecordBounce(surface, pos, y);
                }

                // Far wall: the stone bounces back off the end board.
                if (pos.y > halfL)
                {
                    if (_bounceCount >= MaxBounces) break;
                    pos.y = halfL;
                    vel.y = -vel.y * 0.72f;
                    RecordBounce(surface, pos, y);
                }

                // Behind the near edge: it has left play, stop drawing.
                if (pos.y < -halfL) break;

                _points[n++] = surface.TransformPoint(new Vector3(pos.x, y, pos.y));
                if (n >= _points.Length) break;
            }

            if (!_haveStopPoint && n > 0)
            {
                _stopPoint = _points[n - 1];
                _haveStopPoint = true;
            }

            HideBounceMarkers(_bounceCount);
            return n;
        }

        private void RecordBounce(Transform surface, Vector2 pos, float y)
        {
            if (_bounceCount >= _bouncePoints.Length) { _bounceCount++; return; }
            _bouncePoints[_bounceCount] = surface.TransformPoint(new Vector3(pos.x, y, pos.y));
            _bounceCount++;
        }
    }
}
