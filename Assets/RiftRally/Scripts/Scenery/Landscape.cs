using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Scenery
{
    /// <summary>Shape of the trees a landscape grows, which is most of its silhouette.</summary>
    public enum TreeKind
    {
        None,
        Conifer,     // stacked cones: alpine, boreal
        Broadleaf,   // trunk and a cluster of crowns: woodland, savanna
        Palm,        // bare trunk and arched fronds
        Birch,       // slim pale trunk, light crown
        DeadWood,    // bare angled branches
        Cactus,      // column and arms
        Blossom      // broadleaf with a petal-coloured crown
    }

    /// <summary>What drifts through the air. A tiny effect that does a lot of the work.</summary>
    public enum AirMote
    {
        None,
        Leaves,
        Snow,
        Fireflies,
        Pollen,
        Dust,
        Petals
    }

    /// <summary>
    /// How the birds fly. Birds read as alive or as decoration almost entirely on the
    /// strength of their flight path, so the path is what a landscape chooses.
    /// </summary>
    public enum BirdStyle
    {
        /// <summary>Wide slow circles, wings mostly still. Raptors over a valley.</summary>
        Soaring,

        /// <summary>A V formation crossing the sky with steady wingbeats. Geese, cranes.</summary>
        Migrating,

        /// <summary>Low fast passes close to the ground or water. Gulls, swallows.</summary>
        Skimming,

        /// <summary>Loose, quick, direction-changing. Songbirds working a treeline.</summary>
        Darting
    }

    /// <summary>
    /// One complete environment: its light, its colours, what grows in it, what flies
    /// through it, and the interface palette that stays readable against it.
    ///
    /// These are plain data objects rather than ScriptableObjects on purpose. Every other
    /// piece of this game's art is generated from code at runtime, and keeping the biomes in
    /// the same place means a new landscape is a literal in <see cref="LandscapeCatalog"/>
    /// rather than an asset that has to be created, serialised, assigned in a scene and kept
    /// in sync with the builder that reads it.
    /// </summary>
    public sealed class Landscape
    {
        public string Name = "Landscape";

        /// <summary>One line shown under the name when the environment is introduced.</summary>
        public string Tagline = "";

        // ---- sky and light ----
        public Color SkyZenith = new Color(0.16f, 0.30f, 0.56f);
        public Color SkyHorizon = new Color(0.62f, 0.74f, 0.88f);

        /// <summary>The band of haze sitting right on the horizon line.</summary>
        public Color SkyHaze = new Color(0.78f, 0.84f, 0.92f);

        public Color SunColor = new Color(1.00f, 0.95f, 0.86f);
        public float SunIntensity = 1.1f;

        /// <summary>Sun pitch and yaw in degrees. A low pitch gives long, dramatic light.</summary>
        public Vector2 SunAngles = new Vector2(42f, 28f);

        /// <summary>Drawn disc size in metres at the sky radius. Zero draws no sun.</summary>
        public float SunSize = 5.5f;

        public Color AmbientSky = new Color(0.42f, 0.50f, 0.62f);
        public Color AmbientGround = new Color(0.26f, 0.26f, 0.22f);

        public Color FogColor = new Color(0.70f, 0.78f, 0.88f);
        public float FogDensity = 0.0075f;

        // ---- terrain ----
        /// <summary>Ground colours sampled low to high: valley floor, slope, ridge top.</summary>
        public Color GroundLow = new Color(0.26f, 0.34f, 0.20f);
        public Color GroundMid = new Color(0.34f, 0.40f, 0.24f);
        public Color GroundHigh = new Color(0.50f, 0.50f, 0.46f);

        /// <summary>Peak height of the rolling hills, in metres.</summary>
        public float HillHeight = 7.0f;

        /// <summary>Higher is craggier; lower is smoother and more pastoral.</summary>
        public float HillRoughness = 1.0f;

        /// <summary>Distant ridge line standing behind the hills.</summary>
        public Color RidgeColor = new Color(0.40f, 0.46f, 0.56f);
        public float RidgeHeight = 16f;

        public Color RockColor = new Color(0.42f, 0.42f, 0.44f);
        public int RockCount = 26;

        // ---- planting ----
        public TreeKind Trees = TreeKind.Broadleaf;
        public int TreeCount = 90;
        public float TreeScale = 1f;
        public Color TrunkColor = new Color(0.24f, 0.18f, 0.13f);
        public Color FoliageNear = new Color(0.20f, 0.44f, 0.22f);
        public Color FoliageFar = new Color(0.16f, 0.34f, 0.26f);

        /// <summary>Tufts of ground cover scattered within the near ring.</summary>
        public int GrassCount = 160;
        public Color GrassColor = new Color(0.30f, 0.46f, 0.22f);

        // ---- water ----
        public bool HasWater = false;

        /// <summary>Height of the water plane. Terrain below it reads as submerged.</summary>
        public float WaterLevel = -0.6f;
        public Color WaterShallow = new Color(0.22f, 0.68f, 0.70f, 0.80f);
        public Color WaterDeep = new Color(0.05f, 0.24f, 0.38f, 0.92f);

        // ---- air ----
        public bool Clouds = true;
        public int CloudCount = 14;
        public Color CloudColor = new Color(1f, 0.98f, 0.95f, 0.85f);

        public AirMote Motes = AirMote.None;
        public Color MoteColor = Color.white;

        /// <summary>Hangs slow curtains of light in the sky. Only the night biomes use it.</summary>
        public bool Aurora = false;
        public Color AuroraColor = new Color(0.36f, 1.00f, 0.66f, 0.30f);

        // ---- birds ----
        public BirdStyle Birds = BirdStyle.Soaring;
        public int FlockCount = 2;
        public int BirdsPerFlock = 6;
        public Color BirdColor = new Color(0.12f, 0.12f, 0.14f);

        /// <summary>Wingspan in metres. Scales the whole bird.</summary>
        public float BirdSpan = 1.1f;

        /// <summary>Height above the ground the flocks fly at.</summary>
        public float BirdHeight = 14f;

        // ---- interface ----
        public Theme.Palette Ui = Theme.Default();
    }
}
