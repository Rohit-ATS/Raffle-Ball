using UnityEngine;

namespace RiftRally.Scenery
{
    /// <summary>
    /// Drifts an object along a slow circle. Clouds use it.
    ///
    /// A circle rather than a straight line with a wrap-around, because a cloud that
    /// disappears and reappears is the one piece of motion in a landscape a player is
    /// guaranteed to notice. A sixty-metre circle crossed over several minutes is
    /// indistinguishable from weather.
    /// </summary>
    public class CloudDrift : MonoBehaviour
    {
        public Vector3 Centre;
        public float Radius = 40f;
        public float Height = 24f;

        /// <summary>Radians per second. Values here are intentionally tiny.</summary>
        public float Speed = 0.008f;

        public float Phase;

        /// <summary>Slow swell in size, so a cloud bank is never quite static.</summary>
        public float BreatheAmount = 0.06f;

        private Vector3 _baseScale;

        private void Awake() => _baseScale = transform.localScale;

        private void Update()
        {
            float angle = Time.time * Speed + Phase;
            transform.position = Centre + new Vector3(Mathf.Cos(angle) * Radius,
                                                      Height,
                                                      Mathf.Sin(angle) * Radius);

            // Face the middle of the world, so a cloud quad is never seen edge-on.
            Vector3 toCentre = Centre - transform.position;
            toCentre.y = 0f;
            if (toCentre.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toCentre.normalized, Vector3.up);

            float breathe = 1f + Mathf.Sin(Time.time * 0.09f + Phase) * BreatheAmount;
            transform.localScale = _baseScale * breathe;
        }
    }

    /// <summary>
    /// Leans an object back and forth, as wind in a tree or in grass.
    ///
    /// Two sines of unrelated frequency are summed rather than one, because a single sine is
    /// a metronome and the eye picks it out of a hillside immediately. The phase is seeded per
    /// plant so a wood does not sway as one object, and the whole thing is rotation only - no
    /// vertex animation, no shader - so a hundred and fifty trees cost a hundred and fifty
    /// quaternion writes and nothing on the GPU.
    /// </summary>
    public class Sway : MonoBehaviour
    {
        public float Amplitude = 1.6f;
        public float Rate = 0.6f;
        public float Phase;

        /// <summary>Axis the lean happens about, in local space.</summary>
        public Vector3 Axis = Vector3.forward;

        private Quaternion _rest;

        private void Awake() => _rest = transform.localRotation;

        private void Update()
        {
            float t = Time.time * Rate + Phase;
            float lean = (Mathf.Sin(t) * 0.7f + Mathf.Sin(t * 2.37f + 1.3f) * 0.3f) * Amplitude;
            transform.localRotation = _rest * Quaternion.AngleAxis(lean, Axis);
        }
    }

    /// <summary>
    /// Scrolls a material's texture offset, and rides the surface up and down a little.
    /// Water uses it; the movement is what separates a lagoon from a sheet of glass.
    ///
    /// The material is instanced on purpose. Scrolling a shared material would animate every
    /// surface drawn with it, and the landscape shares materials aggressively to keep draw
    /// calls down.
    /// </summary>
    public class Shimmer : MonoBehaviour
    {
        public Vector2 ScrollSpeed = new Vector2(0.012f, 0.02f);
        public float BobAmplitude = 0.035f;
        public float BobRate = 0.45f;

        private Material _material;
        private float _baseY;

        private void Start()
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null) _material = renderer.material;   // instance, not shared
            _baseY = transform.localPosition.y;
        }

        private void Update()
        {
            if (_material != null)
            {
                Vector2 offset = ScrollSpeed * Time.time;
                if (_material.HasProperty("_BaseMap")) _material.SetTextureOffset("_BaseMap", offset);
                if (_material.HasProperty("_MainTex")) _material.SetTextureOffset("_MainTex", offset);
            }

            Vector3 local = transform.localPosition;
            local.y = _baseY + Mathf.Sin(Time.time * BobRate) * BobAmplitude;
            transform.localPosition = local;
        }
    }

    /// <summary>
    /// Keeps an object at a fixed offset from the player's head without rotating with it.
    ///
    /// Used for the sky, the sun and the weather. A sixty-metre world has an edge, and a
    /// player who stands up and walks half a metre in a room-scale space would otherwise see
    /// the horizon shift - the one thing that gives away that the landscape is a prop. Moving
    /// the sky with the head, and never the rotation, removes the parallax that reveals it
    /// while leaving the rink itself fixed in the room where it belongs.
    /// </summary>
    public class FollowViewer : MonoBehaviour
    {
        public Transform Viewer;

        /// <summary>Only the horizontal position is tracked when true, which is the usual case.</summary>
        public bool HorizontalOnly = true;

        private Vector3 _offset;
        private bool _ready;

        private void Start()
        {
            if (Viewer == null && Camera.main != null) Viewer = Camera.main.transform;
            if (Viewer == null) return;

            _offset = transform.position - FlatViewerPosition();
            _ready = true;
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                if (Viewer == null && Camera.main != null)
                {
                    Viewer = Camera.main.transform;
                    _offset = transform.position - FlatViewerPosition();
                    _ready = true;
                }
                return;
            }

            if (Viewer == null) return;
            transform.position = FlatViewerPosition() + _offset;
        }

        private Vector3 FlatViewerPosition()
        {
            Vector3 p = Viewer.position;
            if (HorizontalOnly) p.y = 0f;
            return p;
        }
    }
}
