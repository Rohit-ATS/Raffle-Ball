using UnityEngine;
using RiftRally.Core;
using RiftRally.Setup;

namespace RiftRally.Scenery
{
    /// <summary>
    /// Owns the world outside the table: which landscape is up, and swapping it for another.
    ///
    /// This exists as a component rather than as a call in the bootstrap because the
    /// environment is no longer built once at launch. It rolls over between games, and the
    /// options panel can change it while a match is in progress, so something has to hold the
    /// built world and be able to tear it down cleanly. Doing that from the bootstrap would
    /// have meant the bootstrap keeping mutable scenery state for the whole session.
    ///
    /// Rebuilding is deliberately immediate rather than cross-faded. A fade would need the
    /// old and new worlds alive at once - double the triangles, double the draw calls, on the
    /// device with the least headroom - to hide a swap that happens between games anyway.
    /// </summary>
    public class LandscapeDirector : MonoBehaviour
    {
        public static LandscapeDirector Instance { get; private set; }

        [Tooltip("Fraction of the planting to place. Lowered on weaker devices.")]
        public float Density = 1f;

        /// <summary>The landscape currently built, or null before the first build.</summary>
        public Landscape Current => _built?.Landscape;

        public string CurrentName => Current != null ? Current.Name : "none";

        public LandscapeBuilder.Built BuiltWorld => _built;

        /// <summary>Raised after a new landscape finishes building.</summary>
        public event System.Action<Landscape> LandscapeChanged;

        private LandscapeBuilder.Built _built;
        private Transform _host;
        private QualityTuner.Preset? _appliedPreset;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            if (_host == null) _host = transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Builds whatever the rotation currently points at, advancing it first when
        /// auto-rotation is on. This is the call a new game makes.
        /// </summary>
        public Landscape BuildForNewGame()
        {
            return Build(LandscapeRotation.AdvanceForNewGame());
        }

        /// <summary>Builds the landscape the rotation points at, without advancing it.</summary>
        public Landscape BuildCurrent() => Build(LandscapeRotation.Current);

        /// <summary>Steps the rotation and rebuilds. Driven by the options panel.</summary>
        public Landscape Step(int delta) => Build(LandscapeRotation.Step(delta));

        public Landscape Next() => Step(1);

        public Landscape Previous() => Step(-1);

        /// <summary>
        /// Tears down the current world and builds <paramref name="landscape"/> in its place.
        /// </summary>
        public Landscape Build(Landscape landscape)
        {
            if (landscape == null) landscape = LandscapeCatalog.At(0);

            Clear();

            // A seed per landscape *and* per build, so the same biome is laid out differently
            // each time it comes round, but one build is internally consistent - the trees
            // stand where the ground actually is.
            int seed = unchecked(landscape.Name.GetHashCode()
                                 ^ (int)(Time.realtimeSinceStartup * 1000f)
                                 ^ System.DateTime.Now.Millisecond * 7919);

            _built = LandscapeBuilder.Build(_host != null ? _host : transform, landscape,
                                           seed, Density);

            Debug.Log($"[RiftRally] Landscape '{landscape.Name}': "
                      + $"{_built.PlantedCount} planted, {_built.Flocks.Length} flocks, "
                      + $"seed {seed}.");

            LandscapeChanged?.Invoke(landscape);
            return landscape;
        }

        /// <summary>Removes the built world, leaving the rink and the player untouched.</summary>
        public void Clear()
        {
            if (_built?.Root == null) { _built = null; return; }

            // Destroy rather than DestroyImmediate, except in the editor outside play mode,
            // where Destroy does nothing at all and the old world would be left standing.
            GameObject go = _built.Root.gameObject;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);

            _built = null;
        }

        /// <summary>
        /// Points the director at a transform other than its own to build under. Used by the
        /// bootstrap so the scenery is parented at the player floor rather than under a
        /// component that may be moved.
        /// </summary>
        public void SetHost(Transform host) => _host = host;

        /// <summary>
        /// Matches the landscape to a quality preset - planting density, terrain mesh
        /// resolution and texture budget - and rebuilds if a world is already up, so the
        /// change is visible straight away.
        ///
        /// All three move together, and the preset is what is compared rather than the
        /// density, because two presets can want the same amount of planting at different
        /// resolutions. Comparing density alone meant a preset change that only altered the
        /// texture budget was silently ignored until the next match.
        /// </summary>
        public void ApplyQuality(QualityTuner.Preset preset)
        {
            if (_appliedPreset == preset) return;

            ConfigureQuality(preset);
            if (_built != null) Build(_built.Landscape);
        }

        /// <summary>
        /// Sets the quality dials without rebuilding anything.
        ///
        /// This is what the bootstrap calls before the first landscape goes up. Letting the
        /// quality tuner discover the preset afterwards instead meant the opening landscape
        /// was built twice - once at the default resolution and once at the chosen one - and
        /// at four thousand pixels square the second bake is not a rounding error.
        /// </summary>
        public void ConfigureQuality(QualityTuner.Preset preset)
        {
            _appliedPreset = preset;

            Density = LandscapeBuilder.DensityFor(preset);
            LandscapeBuilder.TerrainResolution = LandscapeBuilder.TerrainResolutionFor(preset);
            NatureTextures.Budget = NatureTextures.BudgetFor(preset);
        }

        /// <summary>
        /// Puts the shipped interface palette back and clears the world. Used when the
        /// environment is switched off entirely, so the HUD does not keep a biome tint that
        /// has nothing behind it any more.
        /// </summary>
        public void Disable()
        {
            Clear();
            Theme.Reset();
        }
    }
}
