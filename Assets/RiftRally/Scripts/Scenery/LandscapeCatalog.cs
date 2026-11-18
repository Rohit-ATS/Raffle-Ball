using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Scenery
{
    /// <summary>
    /// Every environment the game can put the table in.
    ///
    /// Two rules shaped these eight. First, each one has to be recognisable in a quarter of a
    /// second from any direction - a different sky colour is not enough, so each biome also
    /// changes its silhouette (what shape the trees are, how tall the ridges stand, whether
    /// there is water) and what moves through it. Second, the interface palette is chosen
    /// against the sky rather than alongside it: the bright biomes get darker, more opaque
    /// panels and the dusk biomes get lighter text. That is why the UI colours sit in the same
    /// literal as the sky colours instead of in a separate theme file, where the two would
    /// drift apart the first time a sky was retuned.
    /// </summary>
    public static class LandscapeCatalog
    {
        private static Landscape[] _all;

        public static Landscape[] All => _all ??= Build();

        public static int Count => All.Length;

        /// <summary>
        /// The landscape at an index, wrapping in both directions. The options panel steps
        /// the index up and down, so a negative index has to walk backwards off the start of
        /// the list rather than throw.
        /// </summary>
        public static Landscape At(int index)
        {
            Landscape[] all = All;
            int i = ((index % all.Length) + all.Length) % all.Length;
            return all[i];
        }

        /// <summary>Finds a landscape by name, or null when there is no such biome.</summary>
        public static Landscape ByName(string name)
        {
            int index = IndexOf(name);
            return index < 0 ? null : All[index];
        }

        public static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;

            Landscape[] all = All;
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static Landscape[] Build() => new[]
        {
            // ---------------------------------------------------------------- 1
            new Landscape
            {
                Name = "Alpine Dawn",
                Tagline = "FIRST LIGHT ON THE SNOW LINE",
                SkyZenith = new Color(0.09f, 0.17f, 0.40f),
                SkyHorizon = new Color(0.96f, 0.62f, 0.46f),
                SkyHaze = new Color(1.00f, 0.80f, 0.64f),
                SunColor = new Color(1.00f, 0.84f, 0.66f),
                SunIntensity = 1.15f,
                SunAngles = new Vector2(12f, 18f),
                SunSize = 7f,
                AmbientSky = new Color(0.45f, 0.50f, 0.66f),
                AmbientGround = new Color(0.34f, 0.32f, 0.36f),
                FogColor = new Color(0.82f, 0.72f, 0.72f),
                FogDensity = 0.0085f,
                GroundLow = new Color(0.30f, 0.33f, 0.31f),
                GroundMid = new Color(0.62f, 0.64f, 0.68f),
                GroundHigh = new Color(0.94f, 0.95f, 0.98f),
                HillHeight = 11f,
                HillRoughness = 1.35f,
                RidgeColor = new Color(0.52f, 0.54f, 0.66f),
                RidgeHeight = 26f,
                RockColor = new Color(0.40f, 0.40f, 0.44f),
                RockCount = 30,
                Trees = TreeKind.Conifer,
                TreeCount = 95,
                TreeScale = 1.15f,
                TrunkColor = new Color(0.20f, 0.15f, 0.12f),
                FoliageNear = new Color(0.10f, 0.26f, 0.21f),
                FoliageFar = new Color(0.08f, 0.19f, 0.20f),
                GrassCount = 90,
                GrassColor = new Color(0.46f, 0.50f, 0.44f),
                BushCount = 34,
                FlowerCount = 70,
                FlowerColor = new Color(0.88f, 0.92f, 1.00f),
                ReedCount = 0,
                LogCount = 5,
                Clouds = true,
                CloudCount = 12,
                CloudColor = new Color(1.00f, 0.86f, 0.80f, 0.80f),
                Motes = AirMote.Snow,
                MoteColor = new Color(0.95f, 0.97f, 1.00f, 0.75f),
                Birds = BirdStyle.Soaring,
                FlockCount = 2,
                BirdsPerFlock = 4,
                BirdColor = new Color(0.14f, 0.12f, 0.12f),
                BirdSpan = 1.6f,
                BirdHeight = 20f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(1.00f, 0.78f, 0.54f),
                    Secondary = new Color(0.78f, 0.84f, 0.95f),
                    Accent = new Color(0.52f, 0.88f, 1.00f),
                    Text = new Color(0.97f, 0.98f, 1.00f),
                    TextDim = new Color(0.74f, 0.80f, 0.90f),
                    Panel = new Color(0.05f, 0.08f, 0.16f, 0.80f),
                    PanelEdge = new Color(0.58f, 0.76f, 0.96f),
                    Warn = new Color(1.00f, 0.52f, 0.42f)
                }
            },

            // ---------------------------------------------------------------- 2
            new Landscape
            {
                Name = "Amber Woods",
                Tagline = "LATE AUTUMN, LEAVES STILL FALLING",
                SkyZenith = new Color(0.22f, 0.32f, 0.50f),
                SkyHorizon = new Color(0.92f, 0.72f, 0.44f),
                SkyHaze = new Color(0.98f, 0.84f, 0.60f),
                SunColor = new Color(1.00f, 0.88f, 0.68f),
                SunIntensity = 1.05f,
                SunAngles = new Vector2(24f, -36f),
                SunSize = 6f,
                AmbientSky = new Color(0.50f, 0.46f, 0.42f),
                AmbientGround = new Color(0.32f, 0.24f, 0.16f),
                FogColor = new Color(0.80f, 0.70f, 0.54f),
                FogDensity = 0.011f,
                GroundLow = new Color(0.30f, 0.24f, 0.14f),
                GroundMid = new Color(0.44f, 0.33f, 0.17f),
                GroundHigh = new Color(0.56f, 0.46f, 0.28f),
                HillHeight = 6.5f,
                HillRoughness = 0.85f,
                RidgeColor = new Color(0.44f, 0.40f, 0.42f),
                RidgeHeight = 15f,
                RockColor = new Color(0.38f, 0.34f, 0.30f),
                RockCount = 18,
                Trees = TreeKind.Broadleaf,
                TreeCount = 130,
                TreeScale = 1.2f,
                TrunkColor = new Color(0.26f, 0.19f, 0.14f),
                FoliageNear = new Color(0.78f, 0.36f, 0.12f),
                FoliageFar = new Color(0.56f, 0.26f, 0.12f),
                GrassCount = 170,
                GrassColor = new Color(0.52f, 0.40f, 0.18f),
                BushCount = 74,
                FlowerCount = 44,
                FlowerColor = new Color(0.95f, 0.84f, 0.44f),
                ReedCount = 0,
                LogCount = 8,
                Clouds = true,
                CloudCount = 16,
                CloudColor = new Color(1.00f, 0.92f, 0.80f, 0.78f),
                Motes = AirMote.Leaves,
                MoteColor = new Color(0.92f, 0.48f, 0.16f, 0.90f),
                Birds = BirdStyle.Darting,
                FlockCount = 3,
                BirdsPerFlock = 7,
                BirdColor = new Color(0.16f, 0.13f, 0.11f),
                BirdSpan = 0.55f,
                BirdHeight = 9f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(1.00f, 0.82f, 0.40f),
                    Secondary = new Color(0.92f, 0.84f, 0.70f),
                    Accent = new Color(0.40f, 0.92f, 0.82f),
                    Text = new Color(1.00f, 0.97f, 0.90f),
                    TextDim = new Color(0.84f, 0.76f, 0.62f),
                    Panel = new Color(0.10f, 0.06f, 0.03f, 0.82f),
                    PanelEdge = new Color(0.92f, 0.68f, 0.34f),
                    Warn = new Color(1.00f, 0.44f, 0.34f)
                }
            },

            // ---------------------------------------------------------------- 3
            new Landscape
            {
                Name = "Red Mesa",
                Tagline = "HIGH DESERT, NOTHING MOVING BUT THE AIR",
                SkyZenith = new Color(0.20f, 0.40f, 0.68f),
                SkyHorizon = new Color(0.94f, 0.80f, 0.62f),
                SkyHaze = new Color(0.98f, 0.86f, 0.70f),
                SunColor = new Color(1.00f, 0.96f, 0.86f),
                SunIntensity = 1.35f,
                SunAngles = new Vector2(58f, 52f),
                SunSize = 4.5f,
                AmbientSky = new Color(0.62f, 0.58f, 0.52f),
                AmbientGround = new Color(0.44f, 0.26f, 0.18f),
                FogColor = new Color(0.86f, 0.74f, 0.60f),
                FogDensity = 0.0055f,
                GroundLow = new Color(0.52f, 0.28f, 0.17f),
                GroundMid = new Color(0.68f, 0.38f, 0.22f),
                GroundHigh = new Color(0.82f, 0.56f, 0.36f),
                HillHeight = 9f,
                HillRoughness = 1.8f,
                RidgeColor = new Color(0.62f, 0.36f, 0.26f),
                RidgeHeight = 22f,
                RockColor = new Color(0.60f, 0.32f, 0.20f),
                RockCount = 46,
                Trees = TreeKind.Cactus,
                TreeCount = 42,
                TreeScale = 1.05f,
                TrunkColor = new Color(0.26f, 0.38f, 0.22f),
                FoliageNear = new Color(0.30f, 0.46f, 0.26f),
                FoliageFar = new Color(0.24f, 0.38f, 0.24f),
                GrassCount = 110,
                GrassColor = new Color(0.66f, 0.56f, 0.30f),
                BushCount = 26,
                FlowerCount = 22,
                FlowerColor = new Color(0.92f, 0.40f, 0.52f),
                ReedCount = 0,
                LogCount = 2,
                Clouds = true,
                CloudCount = 7,
                CloudColor = new Color(1.00f, 0.98f, 0.94f, 0.70f),
                Motes = AirMote.Dust,
                MoteColor = new Color(0.86f, 0.70f, 0.50f, 0.50f),
                Birds = BirdStyle.Soaring,
                FlockCount = 2,
                BirdsPerFlock = 3,
                BirdColor = new Color(0.17f, 0.13f, 0.12f),
                BirdSpan = 1.8f,
                BirdHeight = 24f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(1.00f, 0.90f, 0.56f),
                    Secondary = new Color(0.96f, 0.88f, 0.76f),
                    Accent = new Color(0.42f, 0.86f, 1.00f),
                    Text = new Color(1.00f, 0.98f, 0.94f),
                    TextDim = new Color(0.88f, 0.80f, 0.70f),
                    Panel = new Color(0.12f, 0.05f, 0.03f, 0.86f),
                    PanelEdge = new Color(1.00f, 0.62f, 0.32f),
                    Warn = new Color(1.00f, 0.40f, 0.30f)
                }
            },

            // ---------------------------------------------------------------- 4
            new Landscape
            {
                Name = "Coral Lagoon",
                Tagline = "WARM SHALLOWS AND A STEADY TRADE WIND",
                SkyZenith = new Color(0.10f, 0.46f, 0.76f),
                SkyHorizon = new Color(0.72f, 0.92f, 0.96f),
                SkyHaze = new Color(0.86f, 0.97f, 0.98f),
                SunColor = new Color(1.00f, 0.97f, 0.90f),
                SunIntensity = 1.30f,
                SunAngles = new Vector2(52f, -24f),
                SunSize = 4.5f,
                AmbientSky = new Color(0.58f, 0.72f, 0.80f),
                AmbientGround = new Color(0.56f, 0.52f, 0.44f),
                FogColor = new Color(0.78f, 0.92f, 0.95f),
                FogDensity = 0.0045f,
                GroundLow = new Color(0.78f, 0.72f, 0.56f),
                GroundMid = new Color(0.86f, 0.82f, 0.66f),
                GroundHigh = new Color(0.40f, 0.52f, 0.30f),
                HillHeight = 4.5f,
                HillRoughness = 0.7f,
                RidgeColor = new Color(0.34f, 0.52f, 0.46f),
                RidgeHeight = 12f,
                RockColor = new Color(0.56f, 0.54f, 0.50f),
                RockCount = 20,
                Trees = TreeKind.Palm,
                TreeCount = 48,
                TreeScale = 1.3f,
                TrunkColor = new Color(0.46f, 0.36f, 0.24f),
                FoliageNear = new Color(0.22f, 0.56f, 0.28f),
                FoliageFar = new Color(0.16f, 0.44f, 0.30f),
                GrassCount = 130,
                GrassColor = new Color(0.44f, 0.60f, 0.32f),
                BushCount = 42,
                FlowerCount = 34,
                FlowerColor = new Color(1.00f, 0.46f, 0.56f),
                ReedCount = 40,
                LogCount = 3,
                HasWater = true,
                WaterLevel = -0.35f,
                WaterShallow = new Color(0.32f, 0.86f, 0.84f, 0.72f),
                WaterDeep = new Color(0.02f, 0.30f, 0.52f, 0.92f),
                Clouds = true,
                CloudCount = 11,
                CloudColor = new Color(1.00f, 1.00f, 1.00f, 0.86f),
                Motes = AirMote.Pollen,
                MoteColor = new Color(1.00f, 0.98f, 0.86f, 0.45f),
                Birds = BirdStyle.Skimming,
                FlockCount = 3,
                BirdsPerFlock = 5,
                BirdColor = new Color(0.96f, 0.96f, 0.98f),
                BirdSpan = 1.1f,
                BirdHeight = 7f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(1.00f, 0.52f, 0.42f),
                    Secondary = new Color(0.92f, 0.98f, 1.00f),
                    Accent = new Color(1.00f, 0.84f, 0.30f),
                    Text = new Color(1.00f, 1.00f, 1.00f),
                    TextDim = new Color(0.84f, 0.92f, 0.96f),
                    Panel = new Color(0.02f, 0.14f, 0.22f, 0.88f),
                    PanelEdge = new Color(0.36f, 0.92f, 0.92f),
                    Warn = new Color(1.00f, 0.44f, 0.38f)
                }
            },

            // ---------------------------------------------------------------- 5
            new Landscape
            {
                Name = "Highland Moor",
                Tagline = "HEATHER, LOW CLOUD, AND A LONG VIEW",
                SkyZenith = new Color(0.34f, 0.38f, 0.46f),
                SkyHorizon = new Color(0.72f, 0.74f, 0.78f),
                SkyHaze = new Color(0.82f, 0.82f, 0.84f),
                SunColor = new Color(0.92f, 0.92f, 0.96f),
                SunIntensity = 0.85f,
                SunAngles = new Vector2(34f, 112f),
                SunSize = 0f,                       // overcast: no visible disc
                AmbientSky = new Color(0.56f, 0.58f, 0.62f),
                AmbientGround = new Color(0.28f, 0.28f, 0.26f),
                FogColor = new Color(0.74f, 0.76f, 0.80f),
                FogDensity = 0.016f,
                GroundLow = new Color(0.20f, 0.26f, 0.18f),
                GroundMid = new Color(0.34f, 0.30f, 0.34f),
                GroundHigh = new Color(0.46f, 0.40f, 0.44f),
                HillHeight = 8f,
                HillRoughness = 0.9f,
                RidgeColor = new Color(0.46f, 0.48f, 0.54f),
                RidgeHeight = 18f,
                RockColor = new Color(0.38f, 0.38f, 0.36f),
                RockCount = 40,
                Trees = TreeKind.DeadWood,
                TreeCount = 34,
                TreeScale = 1.0f,
                TrunkColor = new Color(0.22f, 0.20f, 0.18f),
                FoliageNear = new Color(0.30f, 0.28f, 0.24f),
                FoliageFar = new Color(0.24f, 0.24f, 0.22f),
                GrassCount = 200,
                GrassColor = new Color(0.42f, 0.30f, 0.42f),
                BushCount = 92,
                FlowerCount = 130,
                FlowerColor = new Color(0.70f, 0.46f, 0.76f),
                ReedCount = 0,
                LogCount = 6,
                Clouds = true,
                CloudCount = 20,
                CloudColor = new Color(0.86f, 0.86f, 0.88f, 0.90f),
                Motes = AirMote.None,
                Birds = BirdStyle.Migrating,
                FlockCount = 2,
                BirdsPerFlock = 9,
                BirdColor = new Color(0.20f, 0.20f, 0.22f),
                BirdSpan = 1.3f,
                BirdHeight = 17f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(0.86f, 0.60f, 0.96f),
                    Secondary = new Color(0.86f, 0.88f, 0.92f),
                    Accent = new Color(0.68f, 1.00f, 0.52f),
                    Text = new Color(1.00f, 1.00f, 1.00f),
                    TextDim = new Color(0.82f, 0.84f, 0.88f),
                    Panel = new Color(0.08f, 0.08f, 0.12f, 0.86f),
                    PanelEdge = new Color(0.72f, 0.62f, 0.86f),
                    Warn = new Color(1.00f, 0.56f, 0.46f)
                }
            },

            // ---------------------------------------------------------------- 6
            new Landscape
            {
                Name = "Boreal Dusk",
                Tagline = "DARK PINES UNDER AN AURORA",
                SkyZenith = new Color(0.04f, 0.05f, 0.14f),
                SkyHorizon = new Color(0.18f, 0.22f, 0.44f),
                SkyHaze = new Color(0.26f, 0.44f, 0.52f),
                SunColor = new Color(0.56f, 0.70f, 0.96f),
                SunIntensity = 0.55f,
                SunAngles = new Vector2(8f, -140f),
                SunSize = 3.0f,
                AmbientSky = new Color(0.20f, 0.28f, 0.44f),
                AmbientGround = new Color(0.14f, 0.16f, 0.24f),
                FogColor = new Color(0.14f, 0.18f, 0.32f),
                FogDensity = 0.013f,
                GroundLow = new Color(0.16f, 0.20f, 0.28f),
                GroundMid = new Color(0.44f, 0.50f, 0.62f),
                GroundHigh = new Color(0.72f, 0.78f, 0.90f),
                HillHeight = 9.5f,
                HillRoughness = 1.2f,
                RidgeColor = new Color(0.18f, 0.22f, 0.34f),
                RidgeHeight = 22f,
                RockColor = new Color(0.26f, 0.28f, 0.34f),
                RockCount = 24,
                Trees = TreeKind.Conifer,
                TreeCount = 150,
                TreeScale = 1.25f,
                TrunkColor = new Color(0.12f, 0.12f, 0.16f),
                FoliageNear = new Color(0.06f, 0.16f, 0.18f),
                FoliageFar = new Color(0.05f, 0.11f, 0.16f),
                GrassCount = 70,
                GrassColor = new Color(0.34f, 0.40f, 0.48f),
                BushCount = 48,
                FlowerCount = 0,
                FlowerColor = new Color(0.60f, 0.80f, 0.90f),
                ReedCount = 0,
                LogCount = 7,
                Clouds = false,
                Aurora = true,
                AuroraColor = new Color(0.34f, 1.00f, 0.68f, 0.26f),
                Motes = AirMote.Snow,
                MoteColor = new Color(0.82f, 0.92f, 1.00f, 0.70f),
                Birds = BirdStyle.Darting,
                FlockCount = 2,
                BirdsPerFlock = 5,
                BirdColor = new Color(0.08f, 0.08f, 0.10f),
                BirdSpan = 0.9f,
                BirdHeight = 12f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(0.46f, 1.00f, 0.72f),
                    Secondary = new Color(0.70f, 0.82f, 1.00f),
                    Accent = new Color(1.00f, 0.52f, 0.88f),
                    Text = new Color(0.92f, 0.97f, 1.00f),
                    TextDim = new Color(0.64f, 0.74f, 0.92f),
                    Panel = new Color(0.02f, 0.03f, 0.09f, 0.74f),
                    PanelEdge = new Color(0.36f, 0.86f, 0.74f),
                    Warn = new Color(1.00f, 0.48f, 0.52f)
                }
            },

            // ---------------------------------------------------------------- 7
            new Landscape
            {
                Name = "Savanna Dusk",
                Tagline = "GOLD GRASS AND A SUN ON THE DECK",
                SkyZenith = new Color(0.24f, 0.22f, 0.44f),
                SkyHorizon = new Color(1.00f, 0.56f, 0.24f),
                SkyHaze = new Color(1.00f, 0.74f, 0.38f),
                SunColor = new Color(1.00f, 0.70f, 0.42f),
                SunIntensity = 1.0f,
                SunAngles = new Vector2(6f, 4f),
                SunSize = 11f,
                AmbientSky = new Color(0.56f, 0.42f, 0.38f),
                AmbientGround = new Color(0.38f, 0.26f, 0.16f),
                FogColor = new Color(0.86f, 0.56f, 0.32f),
                FogDensity = 0.010f,
                GroundLow = new Color(0.40f, 0.30f, 0.14f),
                GroundMid = new Color(0.62f, 0.48f, 0.20f),
                GroundHigh = new Color(0.74f, 0.60f, 0.30f),
                HillHeight = 4f,
                HillRoughness = 0.6f,
                RidgeColor = new Color(0.36f, 0.26f, 0.30f),
                RidgeHeight = 14f,
                RockColor = new Color(0.44f, 0.36f, 0.30f),
                RockCount = 22,
                Trees = TreeKind.Broadleaf,
                TreeCount = 38,
                TreeScale = 1.6f,
                TrunkColor = new Color(0.22f, 0.17f, 0.12f),
                FoliageNear = new Color(0.26f, 0.32f, 0.16f),
                FoliageFar = new Color(0.20f, 0.25f, 0.15f),
                GrassCount = 230,
                GrassColor = new Color(0.72f, 0.58f, 0.24f),
                BushCount = 30,
                FlowerCount = 18,
                FlowerColor = new Color(0.96f, 0.80f, 0.34f),
                ReedCount = 0,
                LogCount = 3,
                Clouds = true,
                CloudCount = 9,
                CloudColor = new Color(1.00f, 0.72f, 0.52f, 0.78f),
                Motes = AirMote.Dust,
                MoteColor = new Color(1.00f, 0.80f, 0.52f, 0.45f),
                Birds = BirdStyle.Migrating,
                FlockCount = 2,
                BirdsPerFlock = 11,
                BirdColor = new Color(0.10f, 0.08f, 0.10f),
                BirdSpan = 1.5f,
                BirdHeight = 19f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(1.00f, 0.86f, 0.52f),
                    Secondary = new Color(0.98f, 0.86f, 0.74f),
                    Accent = new Color(0.46f, 0.92f, 1.00f),
                    Text = new Color(1.00f, 0.98f, 0.92f),
                    TextDim = new Color(0.90f, 0.78f, 0.66f),
                    Panel = new Color(0.10f, 0.05f, 0.06f, 0.84f),
                    PanelEdge = new Color(1.00f, 0.70f, 0.38f),
                    Warn = new Color(1.00f, 0.42f, 0.36f)
                }
            },

            // ---------------------------------------------------------------- 8
            new Landscape
            {
                Name = "Blossom Valley",
                Tagline = "SPRING, AND THE PETALS ARE GOING OVER",
                SkyZenith = new Color(0.22f, 0.46f, 0.78f),
                SkyHorizon = new Color(0.86f, 0.88f, 0.96f),
                SkyHaze = new Color(0.96f, 0.90f, 0.94f),
                SunColor = new Color(1.00f, 0.96f, 0.92f),
                SunIntensity = 1.15f,
                SunAngles = new Vector2(44f, -70f),
                SunSize = 4.5f,
                AmbientSky = new Color(0.58f, 0.66f, 0.80f),
                AmbientGround = new Color(0.34f, 0.40f, 0.28f),
                FogColor = new Color(0.90f, 0.86f, 0.92f),
                FogDensity = 0.0065f,
                GroundLow = new Color(0.26f, 0.42f, 0.20f),
                GroundMid = new Color(0.38f, 0.56f, 0.26f),
                GroundHigh = new Color(0.52f, 0.64f, 0.38f),
                HillHeight = 6f,
                HillRoughness = 0.75f,
                RidgeColor = new Color(0.48f, 0.56f, 0.66f),
                RidgeHeight = 17f,
                RockColor = new Color(0.46f, 0.46f, 0.44f),
                RockCount = 16,
                Trees = TreeKind.Blossom,
                TreeCount = 110,
                TreeScale = 1.1f,
                TrunkColor = new Color(0.28f, 0.22f, 0.20f),
                FoliageNear = new Color(1.00f, 0.74f, 0.84f),
                FoliageFar = new Color(0.92f, 0.60f, 0.76f),
                GrassCount = 210,
                GrassColor = new Color(0.40f, 0.60f, 0.26f),
                BushCount = 62,
                FlowerCount = 150,
                FlowerColor = new Color(0.98f, 0.74f, 0.86f),
                ReedCount = 30,
                LogCount = 4,
                HasWater = true,
                WaterLevel = -1.1f,
                WaterShallow = new Color(0.44f, 0.72f, 0.74f, 0.70f),
                WaterDeep = new Color(0.10f, 0.34f, 0.44f, 0.88f),
                Clouds = true,
                CloudCount = 13,
                CloudColor = new Color(1.00f, 0.98f, 1.00f, 0.84f),
                Motes = AirMote.Petals,
                MoteColor = new Color(1.00f, 0.76f, 0.86f, 0.85f),
                Birds = BirdStyle.Skimming,
                FlockCount = 3,
                BirdsPerFlock = 6,
                BirdColor = new Color(0.14f, 0.14f, 0.18f),
                BirdSpan = 0.7f,
                BirdHeight = 10f,
                Ui = new Theme.Palette
                {
                    Primary = new Color(1.00f, 0.60f, 0.76f),
                    Secondary = new Color(0.92f, 0.94f, 1.00f),
                    Accent = new Color(0.56f, 1.00f, 0.66f),
                    Text = new Color(1.00f, 1.00f, 1.00f),
                    TextDim = new Color(0.86f, 0.88f, 0.94f),
                    Panel = new Color(0.08f, 0.06f, 0.14f, 0.84f),
                    PanelEdge = new Color(1.00f, 0.72f, 0.84f),
                    Warn = new Color(1.00f, 0.50f, 0.44f)
                }
            }
        };
    }
}
