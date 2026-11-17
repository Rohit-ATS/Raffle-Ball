using UnityEngine;

namespace RiftRally.Presentation
{
    /// <summary>
    /// A small world-space text label with a fade.
    ///
    /// Built on the legacy <see cref="TextMesh"/> on purpose: it needs no fonts, atlases,
    /// canvases or UI packages shipped with the project, so the entire HUD can be created in
    /// code at runtime and still render identically in the Editor and on a headset. In VR the
    /// HUD is physical signage sitting on the table anyway, so a canvas would buy nothing.
    /// </summary>
    public class TextPanel : MonoBehaviour
    {
        private TextMesh _mesh;
        private MeshRenderer _renderer;
        private Color _color = Color.white;
        private float _alpha = 1f;
        private float _fadeTarget = 1f;
        private float _fadeSpeed = 6f;

        /// <summary>Creates a label parented to <paramref name="parent"/>.</summary>
        public static TextPanel Create(
            Transform parent,
            string name,
            Vector3 localPosition,
            float characterHeight,
            TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;

            var panel = go.AddComponent<TextPanel>();
            panel.Init(characterHeight, anchor);
            return panel;
        }

        private void Init(float characterHeight, TextAnchor anchor)
        {
            _mesh = gameObject.AddComponent<TextMesh>();
            _renderer = GetComponent<MeshRenderer>();

            // 0.01 world units per font unit keeps the numbers readable: a characterSize of
            // 1 with fontSize 100 gives ~1 m tall text, so scale down to centimetres.
            _mesh.fontSize = 96;
            _mesh.characterSize = characterHeight / 0.96f * 0.01f;
            _mesh.anchor = anchor;
            _mesh.alignment = TextAlignment.Center;
            _mesh.richText = false;

            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (System.Exception) { font = null; }
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (font != null)
            {
                _mesh.font = font;
                if (_renderer != null) _renderer.sharedMaterial = font.material;
            }

            SetText("");
        }

        public string Text => _mesh != null ? _mesh.text : string.Empty;

        public void SetText(string text)
        {
            if (_mesh != null) _mesh.text = text ?? string.Empty;
        }

        public void SetColor(Color color)
        {
            _color = color;
            Apply();
        }

        public void SetCharacterHeight(float metres)
        {
            if (_mesh != null) _mesh.characterSize = metres / 0.96f * 0.01f;
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

        private void Update()
        {
            if (Mathf.Approximately(_alpha, _fadeTarget)) return;
            _alpha = Mathf.MoveTowards(_alpha, _fadeTarget, _fadeSpeed * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            if (_mesh == null) return;
            _mesh.color = new Color(_color.r, _color.g, _color.b, _color.a * _alpha);
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
