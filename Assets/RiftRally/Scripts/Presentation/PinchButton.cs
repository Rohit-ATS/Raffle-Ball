using System;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Setup;

namespace RiftRally.Presentation
{
    /// <summary>
    /// A button you press by moving your hand into it and pinching.
    ///
    /// Direct touch, not a laser pointer. Pointing a ray at a distant panel is accurate but
    /// it feels like operating a remote control, and with hand tracking the ray jitters
    /// around exactly when the player is trying to be precise. A button placed within arm's
    /// reach that you physically push into is both steadier and more satisfying, and it reuses
    /// the pinch the whole game is already built around, so there is only one gesture to learn.
    ///
    /// The press has three stages - hover, pinch-down, release - and each one is visible,
    /// because a button that gives no feedback until it fires leaves the player unsure whether
    /// the game saw them at all.
    /// </summary>
    public class PinchButton : MonoBehaviour
    {
        [Header("Shape")]
        public float Width = 0.17f;
        public float Height = 0.045f;

        [Tooltip("How far from the surface a hand starts to register as hovering.")]
        public float HoverRange = 0.055f;

        [Header("Look")]
        public Color Tint = new Color(0.22f, 0.62f, 0.85f);

        /// <summary>Raised on release, while still inside the button.</summary>
        public event Action Pressed;

        public string Label
        {
            get => _label != null ? _label.Text : string.Empty;
            set { if (_label != null) _label.SetText(value); }
        }

        public bool Interactable { get; set; } = true;

        private TextPanel _label;
        private Transform _plate;
        private Material _plateMaterial;
        private float _hover;
        private float _press;
        private bool _armed;
        private HandSide _armedHand;
        private Vector3 _plateBaseScale;

        /// <summary>Builds a button under <paramref name="parent"/> in its local space.</summary>
        public static PinchButton Create(Transform parent, string label, Vector3 localPosition,
                                         float width, float height, Color tint,
                                         Action onPressed = null)
        {
            var go = new GameObject($"Button_{label}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var button = go.AddComponent<PinchButton>();
            button.Width = width;
            button.Height = height;
            button.Tint = tint;
            button.Build(label);

            if (onPressed != null) button.Pressed += onPressed;
            return button;
        }

        private void Build(string label)
        {
            _plateMaterial = ProceduralArt.MakeGlowMaterial(Tint, 1.1f);

            var plate = ProceduralArt.Box(transform, "Plate", Vector3.zero,
                                          new Vector3(Width, Height, 0.006f),
                                          _plateMaterial, false);
            _plate = plate.transform;
            _plateBaseScale = _plate.localScale;

            // The label sits slightly in front so it is never z-fighting with the plate.
            _label = TextPanel.Create(transform, "Label", new Vector3(0f, 0f, -0.005f),
                                      Height * 0.52f);
            _label.Show(label, Color.white);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;   // menus must stay responsive in slow motion
            var hands = HandInput.Instance;

            bool hovering = false;
            float pinchStrength = 0f;
            HandSide hand = HandSide.Right;

            if (hands != null && Interactable)
            {
                for (int i = 0; i < 2; i++)
                {
                    HandSample sample = hands.Get((HandSide)i);
                    if (!sample.IsTracked) continue;

                    if (!Contains(sample.PinchPoint)) continue;

                    hovering = true;
                    hand = (HandSide)i;
                    pinchStrength = sample.PinchStrength;

                    if (sample.IsPinching && !_armed)
                    {
                        _armed = true;
                        _armedHand = (HandSide)i;
                    }
                    else if (!sample.IsPinching && _armed && _armedHand == (HandSide)i)
                    {
                        // Fires on release rather than on contact, so a player who reaches
                        // into the wrong button can pull away without triggering it.
                        _armed = false;
                        Pressed?.Invoke();
                    }
                    break;
                }
            }

            if (!hovering && _armed)
            {
                // Hand left the button while pinched: treat it as a cancel.
                _armed = false;
            }

            _hover = Easing.Damp(_hover, hovering ? 1f : 0f, 14f, dt);
            _press = Easing.Damp(_press, _armed ? 1f : pinchStrength * (hovering ? 0.6f : 0f),
                                 20f, dt);

            Apply();
        }

        private void Apply()
        {
            if (_plate == null) return;

            // Grows slightly on hover and sinks when pressed: two separate, readable signals.
            float grow = 1f + _hover * 0.06f;
            float depth = 1f - _press * 0.45f;
            _plate.localScale = new Vector3(_plateBaseScale.x * grow,
                                            _plateBaseScale.y * grow,
                                            _plateBaseScale.z * depth);
            _plate.localPosition = new Vector3(0f, 0f, _press * 0.004f);

            float alpha = Interactable ? Mathf.Lerp(0.42f, 0.95f, _hover) : 0.18f;
            float emission = Interactable ? 0.9f + _hover * 1.6f + _press * 2.2f : 0.3f;

            Color tint = Interactable ? Tint : Color.Lerp(Tint, Color.gray, 0.7f);

            ProceduralArt.SetGlowFade(_plateMaterial, tint, alpha, emission);

            if (_label != null)
                _label.SetColor(Interactable
                    ? Color.Lerp(new Color(0.88f, 0.93f, 1f), Color.white, _hover)
                    : new Color(0.6f, 0.65f, 0.7f));
        }

        /// <summary>True when a world point is inside the button's slab.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= Width * 0.5f
                && Mathf.Abs(local.y) <= Height * 0.5f
                && Mathf.Abs(local.z) <= HoverRange;
        }
    }
}
