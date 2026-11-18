using UnityEngine;
using RiftRally.Setup;

namespace RiftRally.Presentation
{
    /// <summary>
    /// A small world-space text label with a fade, a drop shadow and an optional backing
    /// plate.
    ///
    /// Built on the legacy <see cref="TextMesh"/> on purpose: it needs no fonts, atlases,
    /// canvases or UI packages shipped with the project, so the entire HUD can be created in
    /// code at runtime and still render identically in the Editor and on a headset. In VR the
    /// HUD is physical signage sitting in the room anyway, so a canvas would buy nothing.
    ///
    /// The shadow and the plate are not decoration. Once the game started putting the table
    /// in a different landscape every match, the one background a label could no longer count
    /// on was a dark one: the same light grey text that read cleanly against the old blue void
    /// sat on bright desert sand and on a near-white overcast sky and disappeared into both.
    /// A dark offset copy of the glyphs gives every letter its own local contrast whatever is
    /// behind it, which is the cheapest fix available and works at any distance. The plate is
    /// the heavier option, for blocks of text that have to be read rather than glanced at.
    /// </summary>
    public class TextPanel : MonoBehaviour
    {
        private TextMesh _mesh;
        private MeshRenderer _renderer;
        private TextMesh _shadowMesh;
        private Color _color = Color.white;
        private float _alpha = 1f;
        private float _fadeTarget = 1f;
        private float _fadeSpeed = 6f;

        private Transform _plate;
        private Material _plateMaterial;
        private Color _plateColor;
        private Vector2 _platePadding;
        private bool _plateDirty;

        /// <summary>Opacity of the shadow copy relative to the label.</summary>
        private const float ShadowStrength = 0.72f;

        /// <summary>Creates a label parented to <paramref name="parent"/>.</summary>
        public static TextPanel Create(
            Transform parent,
            string name,
            Vector3 localPosition,
            float characterHeight,
            TextAnchor anchor = TextAnchor.MiddleCenter,
            FontStyle style = FontStyle.Normal,
            bool shadow = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;

            var panel = go.AddComponent<TextPanel>();
            panel.Init(characterHeight, anchor, style, shadow);
            return panel;
        }

        private void Init(float characterHeight, TextAnchor anchor, FontStyle style, bool shadow)
        {
            _mesh = gameObject.AddComponent<TextMesh>();
            _renderer = GetComponent<MeshRenderer>();

            Typography.Apply(_mesh, characterHeight, anchor, style);

            Material fontMaterial = Typography.MaterialFor(_mesh.font);
            if (_renderer != null && fontMaterial != null)
                _renderer.sharedMaterial = fontMaterial;

            if (shadow) BuildShadow(characterHeight, anchor, style);

            SetText("");
        }

        /// <summary>
        /// A dark copy of the label, offset behind it.
        ///
        /// Offset along local Z as well as down and right, so the shadow is genuinely behind
        /// the glyphs in depth. Offsetting only in the plane leaves two coplanar text meshes
        /// fighting over the same pixels, and which one wins varies with the viewing angle -
        /// the label flickers as the player moves their head, which is far worse than having
        /// no shadow at all.
        /// </summary>
        private void BuildShadow(float characterHeight, TextAnchor anchor, FontStyle style)
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(transform, false);

            float offset = characterHeight * 0.075f;
            go.transform.localPosition = new Vector3(offset, -offset, 0.0015f);

            _shadowMesh = go.AddComponent<TextMesh>();
            Typography.Apply(_shadowMesh, characterHeight, anchor, style);

            var renderer = go.GetComponent<MeshRenderer>();
            Material fontMaterial = Typography.MaterialFor(_shadowMesh.font);
            if (renderer != null && fontMaterial != null)
            {
                renderer.sharedMaterial = fontMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>
        /// Puts a translucent plate behind the text, sized to whatever the text turns out to
        /// be. Used for blocks a player actually reads - the leaderboard, the options list -
        /// where a shadow alone is not enough.
        /// </summary>
        public TextPanel WithPlate(Color colour, float paddingX = 0.018f, float paddingY = 0.012f)
        {
            _plateColor = colour;
            _platePadding = new Vector2(paddingX, paddingY);

            if (_plate == null)
            {
                _plateMaterial = ProceduralArt.MakeTransparentMaterial(colour);

                var go = ProceduralArt.Box(transform, "Plate", new Vector3(0f, 0f, 0.004f),
                                           Vector3.one, _plateMaterial, false);
                _plate = go.transform;
            }

            _plateDirty = true;
            return this;
        }

        public string Text => _mesh != null ? _mesh.text : string.Empty;

        public void SetText(string text)
        {
            string value = text ?? string.Empty;
            if (_mesh != null) _mesh.text = value;
            if (_shadowMesh != null) _shadowMesh.text = value;
            _plateDirty = true;
        }

        public void SetColor(Color color)
        {
            _color = color;
            Apply();
        }

        public void SetCharacterHeight(float metres)
        {
            float size = Typography.CharacterSizeFor(metres);
            if (_mesh != null) _mesh.characterSize = size;
            if (_shadowMesh != null) _shadowMesh.characterSize = size;
            _plateDirty = true;
        }

        /// <summary>Shows text immediately, fading in.</summary>
        public void Show(string text, Color color)
        {
            SetText(text);
            _color = color;
            _fadeTarget = 1f;
            Apply();
        }

        public void Hide()
        {
            _fadeTarget = 0f;
        }

        /// <summary>Snaps the label to fully hidden without a fade.</summary>
        public void HideInstant()
        {
            _fadeTarget = 0f;
            _alpha = 0f;
            Apply();
        }

        /// <summary>How visible the label is right now, after the fade.</summary>
        public float Opacity => _alpha;

        /// <summary>Scales the whole label, plate and shadow included.</summary>
        public void SetScale(float scale) => transform.localScale = Vector3.one * scale;

        private void Update()
        {
            if (_plateDirty) ResizePlate();

            if (Mathf.Approximately(_alpha, _fadeTarget)) return;
            _alpha = Mathf.MoveTowards(_alpha, _fadeTarget, _fadeSpeed * Time.unscaledDeltaTime);
            Apply();
        }

        /// <summary>
        /// Fits the plate to the current text.
        ///
        /// The size has to come from the renderer bounds rather than from counting
        /// characters, because the font is whatever the device had and its metrics are not
        /// known in advance. Bounds are only valid once the mesh has been generated, which is
        /// why this runs on the next frame after the text changes rather than inline.
        /// </summary>
        private void ResizePlate()
        {
            _plateDirty = false;
            if (_plate == null || _renderer == null) return;

            Vector3 size = _renderer.bounds.size;
            if (size.x <= 0.0001f && size.y <= 0.0001f) return;   // nothing drawn yet

            // Bounds are in world space; the plate lives in local space, and the label may be
            // scaled, so divide the scale back out.
            Vector3 scale = transform.lossyScale;
            float width = size.x / Mathf.Max(0.0001f, scale.x);
            float height = size.y / Mathf.Max(0.0001f, scale.y);

            _plate.localScale = new Vector3(width + _platePadding.x * 2f,
                                            height + _platePadding.y * 2f,
                                            0.004f);

            // Centre the plate on the text, which is not the same as centring it on the
            // label origin: an anchored label draws off to one side of its own transform.
            Vector3 centre = transform.InverseTransformPoint(_renderer.bounds.center);
            _plate.localPosition = new Vector3(centre.x, centre.y, 0.004f);
        }

        private void Apply()
        {
            if (_mesh == null) return;

            _mesh.color = new Color(_color.r, _color.g, _color.b, _color.a * _alpha);

            if (_shadowMesh != null)
            {
                // A dark shadow under light text and a light one under dark text, decided
                // from the label colour rather than hard-coded, so a label tinted for a
                // bright biome still gets a shadow that does something.
                float luminance = _color.r * 0.299f + _color.g * 0.587f + _color.b * 0.114f;
                Color shade = luminance > 0.45f
                    ? new Color(0.02f, 0.02f, 0.05f)
                    : new Color(0.95f, 0.97f, 1f);

                _shadowMesh.color = new Color(shade.r, shade.g, shade.b,
                                              ShadowStrength * _color.a * _alpha);
            }

            if (_plateMaterial != null)
            {
                Color plate = _plateColor;
                plate.a = _plateColor.a * _alpha;
                if (_plateMaterial.HasProperty("_BaseColor")) _plateMaterial.SetColor("_BaseColor", plate);
                if (_plateMaterial.HasProperty("_Color")) _plateMaterial.SetColor("_Color", plate);
            }
        }

        /// <summary>Turns this label to face a point, keeping it upright.</summary>
        public void FaceTowards(Vector3 worldPoint)
        {
            Vector3 dir = transform.position - worldPoint;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}
