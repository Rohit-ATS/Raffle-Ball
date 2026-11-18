using UnityEngine;

namespace RiftRally.Core
{
    /// <summary>
    /// The colours the interface draws itself with, as a single mutable set.
    ///
    /// The game's signage used to read its colours straight out of <see cref="GameConfig"/>,
    /// which is a set of <c>const</c>s - correct while there was one environment, and wrong
    /// the moment the world behind the text changes every match. Cool grey body text is
    /// perfectly legible against a dusk sky and nearly invisible against bright desert sand,
    /// so the palette has to be able to move with the landscape.
    ///
    /// It lives here rather than in the scenery for two reasons: the HUD must not take a
    /// dependency on the world builder to know what colour to be, and the defaults below are
    /// the colours the game shipped with, so anything built without a landscape - a test, a
    /// headless match - looks exactly as it did before.
    ///
    /// The two player colours are deliberately *not* themed. Blue and orange are how a player
    /// tells whose stone is whose; re-tinting them per biome would trade a small amount of
    /// visual harmony for a real loss of gameplay information.
    /// </summary>
    public static class Theme
    {
        /// <summary>A full interface palette, normally supplied by the active landscape.</summary>
        public struct Palette
        {
            /// <summary>Titles, round banners, anything that should pull the eye.</summary>
            public Color Primary;

            /// <summary>Supporting lines under a title.</summary>
            public Color Secondary;

            /// <summary>Highlights: the house, the active button, the aim path.</summary>
            public Color Accent;

            /// <summary>Body text, at full strength.</summary>
            public Color Text;

            /// <summary>Body text that is present but not being read right now.</summary>
            public Color TextDim;

            /// <summary>Fill behind text, so a bright sky cannot wash it out.</summary>
            public Color Panel;

            /// <summary>The thin lit edge of a panel, which is what gives it a shape.</summary>
            public Color PanelEdge;

            /// <summary>Warnings and failed connections.</summary>
            public Color Warn;
        }

        /// <summary>The palette in use. Replaced whenever a landscape is installed.</summary>
        public static Palette Current { get; private set; } = Default();

        /// <summary>Raised after <see cref="Current"/> changes, so live UI can re-tint.</summary>
        public static event System.Action<Palette> Changed;

        public static Palette Default() => new Palette
        {
            Primary = new Color(0.55f, 0.95f, 0.70f),
            Secondary = new Color(0.72f, 0.78f, 0.86f),
            Accent = new Color(0.55f, 0.95f, 0.70f),
            Text = new Color(0.94f, 0.96f, 1.00f),
            TextDim = new Color(0.72f, 0.78f, 0.86f),
            Panel = new Color(0.04f, 0.06f, 0.10f, 0.78f),
            PanelEdge = new Color(0.45f, 0.62f, 0.78f),
            Warn = new Color(1.00f, 0.50f, 0.35f)
        };

        public static void Install(Palette palette)
        {
            Current = palette;
            Changed?.Invoke(palette);
        }

        /// <summary>Puts the shipped palette back. Used when no landscape is built.</summary>
        public static void Reset() => Install(Default());

        // Short accessors, so call sites read like the old GameConfig ones did.
        public static Color Primary => Current.Primary;
        public static Color Secondary => Current.Secondary;
        public static Color Accent => Current.Accent;
        public static Color Text => Current.Text;
        public static Color TextDim => Current.TextDim;
        public static Color Panel => Current.Panel;
        public static Color PanelEdge => Current.PanelEdge;
        public static Color Warn => Current.Warn;

        public static Color PlayerOne => GameConfig.PlayerOneColor;
        public static Color PlayerTwo => GameConfig.PlayerTwoColor;

        /// <summary>The same colour at a different opacity, for fades and dimmed states.</summary>
        public static Color At(Color color, float alpha)
            => new Color(color.r, color.g, color.b, alpha);
    }
}
