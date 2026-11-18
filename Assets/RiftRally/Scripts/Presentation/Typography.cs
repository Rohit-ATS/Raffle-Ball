using UnityEngine;

namespace RiftRally.Presentation
{
    /// <summary>
    /// Finds the best font available on the device, and the shared rules for using it.
    ///
    /// The game shipped drawing every label in Arial, which is what Unity hands you when you
    /// ask for a built-in font and ask for nothing else. Against a flat dark background that
    /// is merely plain. Against a dawn sky or bright sand it is actively bad: Arial at a
    /// distance has thin, evenly weighted strokes and open counters, so it loses contrast
    /// exactly where a VR panel needs it most, and it carries no sense of the game having
    /// been designed at all.
    ///
    /// No font file is shipped to fix it, because the whole project deliberately carries no
    /// binary assets. Instead the real device font is requested by name:
    /// <see cref="Font.CreateDynamicFontFromOSFont"/> walks a preference list and returns the
    /// first family installed, which on a Quest is Roboto and on a Windows desktop is a Segoe
    /// or Bahnschrift weight. All of them are grotesques with heavier stems and tighter
    /// apertures than Arial, which is precisely what reads at two metres. The built-in font
    /// stays as the last fallback, so a device with no match looks exactly as it did before
    /// rather than drawing nothing.
    /// </summary>
    public static class Typography
    {
        /// <summary>
        /// Preference order. Semibold and medium weights first: a VR label is read at a
        /// glance over moving scenery, and the heavier stem survives that where a regular
        /// weight dissolves. Condensed faces are preferred next because the scoreboard is a
        /// fixed width and a condensed face fits more of the round name into it.
        /// </summary>
        private static readonly string[] Preferred =
        {
            "Bahnschrift SemiBold",
            "Segoe UI Semibold",
            "Roboto Medium",
            "Roboto-Medium",
            "Roboto Condensed",
            "Noto Sans Medium",
            "Segoe UI",
            "Roboto",
            "DroidSans",
            "Noto Sans",
            "Verdana"
        };

        /// <summary>
        /// Glyph size the atlas is rasterised at. Kept at the value the game already used:
        /// the metres-to-character-size conversion below is calibrated against it, and
        /// changing one without the other silently rescales every label in the game.
        /// </summary>
        private const int AtlasFontSize = 96;

        private static Font _font;
        private static bool _resolved;

        /// <summary>The font every label uses. Resolved once, then cached.</summary>
        public static Font Face
        {
            get
            {
                if (_resolved) return _font;
                _resolved = true;
                _font = Resolve();
                return _font;
            }
        }

        /// <summary>Which font was actually found, for the build log.</summary>
        public static string FaceReport
        {
            get
            {
                Font face = Face;
                if (face == null) return "<none>";
                if (face.fontNames != null && face.fontNames.Length > 0) return face.fontNames[0];
                return string.IsNullOrEmpty(face.name) ? "<unnamed>" : face.name;
            }
        }

        private static Font Resolve()
        {
            // An OS font can fail in several uninteresting ways on an unfamiliar device, and
            // none of them are worth taking the game down for, so every step is guarded and
            // the fallback is always a font that is known to exist.
            try
            {
                Font dynamic = Font.CreateDynamicFontFromOSFont(Preferred, AtlasFontSize);
                if (dynamic != null && dynamic.material != null) return dynamic;
            }
            catch (System.Exception error)
            {
                Debug.LogWarning($"[RiftRally] No OS font available ({error.GetType().Name}); "
                                 + "falling back to the built-in face.");
            }

            try
            {
                Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (builtin != null) return builtin;
            }
            catch (System.Exception) { /* fall through to Arial below */ }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>
        /// Letter-spaces a short string by inserting thin gaps.
        ///
        /// <see cref="TextMesh"/> has no tracking control, and a title set solid in a
        /// grotesque looks cramped at display size. Spacing it out by hand is a hack, but it
        /// is a hack confined to titles - three or four of them in the whole game - and the
        /// alternative is shipping a text package to letter-space four words.
        /// </summary>
        public static string Track(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var builder = new System.Text.StringBuilder(text.Length * 2);
            for (int i = 0; i < text.Length; i++)
            {
                builder.Append(text[i]);
                // A word space is already wide enough; adding to it breaks the word apart.
                if (i < text.Length - 1 && text[i] != ' ' && text[i + 1] != ' ')
                    builder.Append(' ');       // thin space
            }
            return builder.ToString();
        }

        /// <summary>
        /// Converts a requested cap height in metres into a <c>TextMesh</c> character size,
        /// at <see cref="AtlasFontSize"/>. This is the calibration the game was tuned
        /// against, lifted unchanged out of the label class so there is now exactly one copy
        /// of it.
        /// </summary>
        public static float CharacterSizeFor(float heightMetres)
            => heightMetres / 0.96f * 0.01f;

        /// <summary>Applies the shared text settings to a <see cref="TextMesh"/>.</summary>
        public static void Apply(TextMesh mesh, float characterHeightMetres, TextAnchor anchor,
                                 FontStyle style = FontStyle.Normal)
        {
            if (mesh == null) return;

            Font face = Face;
            if (face != null) mesh.font = face;

            mesh.fontSize = AtlasFontSize;
            mesh.fontStyle = style;
            mesh.characterSize = CharacterSizeFor(characterHeightMetres);
            mesh.anchor = anchor;
            mesh.alignment = TextAlignment.Center;
            mesh.richText = false;

            // Tighter than the 1.0 default. Stacked HUD lines at default leading drift apart
            // into separate labels instead of reading as one block.
            mesh.lineSpacing = 0.86f;
        }

        /// <summary>The material a label should draw with, for a given font.</summary>
        public static Material MaterialFor(Font face)
            => face != null ? face.material : null;
    }
}
