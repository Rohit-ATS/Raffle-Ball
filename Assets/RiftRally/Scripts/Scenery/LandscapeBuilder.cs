using UnityEngine;
using RiftRally.Core;
using RiftRally.Setup;

namespace RiftRally.Scenery
{
    /// <summary>
    /// Builds a whole environment around the table: sky, light, ground, planting, water,
    /// weather and birds.
    ///
    /// Every landscape is generated from a seed and a <see cref="Landscape"/> rather than
    /// authored, for the same reason the rink is: the repository carries no binary art that
    /// can fall out of sync with the code, and a new biome is a literal in a catalogue rather
    /// than a scene somebody has to maintain. Generation also means the same biome is subtly
    /// different each time it comes round in the rotation, which is most of what stops eight
    /// environments feeling like eight backdrops.
    ///
    /// The budget it is written to: one material per kind of surface so the SRP batcher can
    /// do its job, no colliders anywhere, no shadow casting beyond the near planting, and a
    /// triangle count in the low tens of thousands. The rink is the thing that has to stay at
    /// 90 Hz; the scenery is only allowed to be beautiful with what is left over.
    /// </summary>
    public static class LandscapeBuilder
    {
        /// <summary>How much of the planting to actually place, per quality preset.</summary>
        public static float DensityFor(QualityTuner.Preset preset) => preset switch
        {
            QualityTuner.Preset.Crisp => 1.0f,
            QualityTuner.Preset.Balanced => 0.78f,
            _ => 0.45f
        };

        /// <summary>
        /// Grid resolution of the terrain mesh, per quality preset.
        ///
        /// This went up by a lot when the ground stopped being a flat colour. A 56-cell grid
        /// over a hundred and twenty metres is a vertex every two metres, which is enough to
        /// carry the shape of a hill and nothing like enough to carry a surface: the baked
        /// texture's grain sits on facets you can see the edges of, and the silhouette of a
        /// ridge against the sky is visibly straight between samples. 192 cells is a vertex
        /// every 65 centimetres and about seventy-four thousand triangles - which sounds like
        /// a lot until you notice it replaced a hundred thousand triangles of foliage nobody
        /// could resolve.
        /// </summary>
        public static int TerrainResolutionFor(QualityTuner.Preset preset) => preset switch
        {
            QualityTuner.Preset.Crisp => 192,
            QualityTuner.Preset.Balanced => 144,
            _ => 80
        };

        /// <summary>
        /// Resolution the next terrain is built at. Set by <see cref="QualityTuner"/>; the
        /// default is the balanced figure so a landscape built without one is still detailed.
        /// </summary>
        public static int TerrainResolution { get; set; } = 144;

        /// <summary>What a build produced, so it can be inspected, animated or torn down.</summary>
        public class Built
        {
            public Landscape Landscape;
            public Transform Root;
            public Transform Sky;
            public Light Sun;
            public int Seed;

            /// <summary>Flocks in the air, so a test can confirm the birds exist and move.</summary>
            public BirdFlock[] Flocks = System.Array.Empty<BirdFlock>();

            /// <summary>Count of planted objects, for the build log and for tests.</summary>
            public int PlantedCount;
        }

        private const float WorldExtent = 62f;
        private const float SkyRadius = 150f;
        private const float FlatRadius = 3.6f;
        private const float FlatFalloff = 5.5f;

        /// <summary>
        /// Builds <paramref name="landscape"/> under <paramref name="parent"/>, whose origin
        /// is taken as the floor the player is standing on.
        /// </summary>
        public static Built Build(Transform parent, Landscape landscape, int seed,
                                  float density = 1f)
        {
            if (landscape == null) landscape = LandscapeCatalog.At(0);

            var random = new System.Random(seed);
            density = Mathf.Clamp(density, 0.1f, 1.5f);

            var rootGo = new GameObject($"Landscape_{landscape.Name}");
            rootGo.transform.SetParent(parent, false);
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;
            Transform root = rootGo.transform;

            var built = new Built
            {
                Landscape = landscape,
                Root = root,
                Seed = seed
            };

            var noise = new SceneryArt.TerrainNoise(seed, landscape.HillRoughness);

            built.Sky = BuildSky(root, landscape);
            BuildSun(built.Sky, landscape);
            if (landscape.Aurora) BuildAurora(built.Sky, landscape, random);
            if (landscape.Clouds) BuildClouds(root, landscape, random, density);

            BuildGround(root, landscape, seed);
            BuildRidges(root, landscape, random);
            if (landscape.HasWater) BuildWater(root, landscape);

            built.PlantedCount = Plant(root, landscape, noise, random, density);
            BuildMotes(root, landscape);
            built.Flocks = BuildFlocks(root, landscape, noise, random);

            built.Sun = ApplyLighting(parent, landscape);
            ApplyCameraAndFog(landscape);
            Theme.Install(landscape.Ui);

            return built;
        }

        // ---------------------------------------------------------------- sky

        private static Transform BuildSky(Transform parent, Landscape landscape)
        {
            var skyGo = new GameObject("Sky");
            skyGo.transform.SetParent(parent, false);

            // A baked sky rather than a vertical ramp: the same horizon-to-zenith gradient up
            // one axis, with cirrus, a glow around the sun's bearing and - in the night
            // biomes - stars around the other. The dome carries both coordinates, so this is
            // a texture swap and not a geometry change.
            Texture2D sky = NatureTextures.Sky(landscape);

            // Culling the front faces is what lets a normal outward-facing dome be viewed
            // from the inside, and the Background queue keeps it behind every other surface
            // however large it is.
            Material material = ProceduralArt.MakeTexturedUnlit(
                sky, cullFront: true, transparent: false,
                renderQueue: (int)UnityEngine.Rendering.RenderQueue.Background);

            // More segments than the old dome had. A gradient needs almost none, but a sky
            // with cloud banding in it shows the facets of a coarse dome as straight edges
            // through the cloud, and the whole mesh is still under three thousand triangles.
            SceneryArt.Attach(skyGo.transform, "Dome",
                SceneryArt.BuildSkyDome(SkyRadius, 64, 20), material,
                Vector3.zero, Quaternion.identity, Vector3.one);

            // The sky travels with the player so a room-scale step cannot show its edge, but
            // never rotates with them, so the sun stays where it was put.
            skyGo.AddComponent<FollowViewer>();

            return skyGo.transform;
        }

        private static void BuildSun(Transform sky, Landscape landscape)
        {
            if (landscape.SunSize <= 0.01f) return;

            Vector3 direction = Quaternion.Euler(-landscape.SunAngles.x,
                                                 landscape.SunAngles.y, 0f) * Vector3.forward;
            Vector3 position = direction.normalized * (SkyRadius * 0.93f);

            Material disc = ProceduralArt.MakeGlowMaterial(landscape.SunColor, 2.4f);
            Material halo = ProceduralArt.MakeGlowMaterial(landscape.SunColor, 0.6f);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(sky, false);
            sunGo.transform.localPosition = position;

            // A quad faces along its own negative Z, so pointing the object outwards is what
            // turns the visible side back towards the middle of the world. Facing it inwards
            // instead puts the back of an additive, back-face-culled quad at the player, and
            // the sun simply is not there.
            sunGo.transform.localRotation = Quaternion.LookRotation(direction.normalized,
                                                                    Vector3.up);

            Mesh quad = SceneryArt.BuildQuad(1f, 1f);

            // Halo first, disc on top. Both additive, so they stack into a bright core with a
            // soft falloff without needing a gradient texture or a second pass.
            SceneryArt.Attach(sunGo.transform, "Halo", quad, halo, new Vector3(0f, 0f, 0.1f),
                Quaternion.identity, Vector3.one * landscape.SunSize * 3.4f);
            SceneryArt.Attach(sunGo.transform, "Disc", quad, disc, Vector3.zero,
                Quaternion.identity, Vector3.one * landscape.SunSize);
        }

        /// <summary>
        /// Slow vertical curtains of light. Three big additive quads, drifting out of phase,
        /// is enough to sell an aurora at this distance.
        /// </summary>
        private static void BuildAurora(Transform sky, Landscape landscape, System.Random random)
        {
            Material material = ProceduralArt.MakeGlowMaterial(landscape.AuroraColor, 1.1f);
            Mesh quad = SceneryArt.BuildQuad(1f, 1f);

            var auroraGo = new GameObject("Aurora");
            auroraGo.transform.SetParent(sky, false);

            for (int i = 0; i < 3; i++)
            {
                float yaw = -60f + i * 48f + random.Next(-14, 15);
                float radius = SkyRadius * 0.72f;

                Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var curtain = SceneryArt.Attach(auroraGo.transform, $"Curtain_{i}", quad,
                    material,
                    direction * radius + Vector3.up * (SkyRadius * 0.30f),
                    Quaternion.LookRotation(direction, Vector3.up),
                    new Vector3(70f + i * 18f, 46f, 1f));

                var sway = curtain.AddComponent<Sway>();
                sway.Amplitude = 3.4f;
                sway.Rate = 0.05f + i * 0.013f;
                sway.Phase = (float)random.NextDouble() * 6.28f;
                sway.Axis = Vector3.forward;
            }
        }

        private static void BuildClouds(Transform parent, Landscape landscape,
                                        System.Random random, float density)
        {
            int count = Mathf.Max(2, Mathf.RoundToInt(landscape.CloudCount * density));

            Material material = ProceduralArt.MakeTransparentMaterial(landscape.CloudColor);
            Mesh blob = SceneryArt.BuildBlob();

            var cloudsGo = new GameObject("Clouds");
            cloudsGo.transform.SetParent(parent, false);

            for (int i = 0; i < count; i++)
            {
                var cloud = new GameObject($"Cloud_{i}").transform;
                cloud.SetParent(cloudsGo.transform, false);

                // A cloud is three or four overlapping flattened blobs. One blob is a
                // balloon; a cluster has an outline.
                int lumps = 3 + random.Next(2);
                float width = 9f + (float)random.NextDouble() * 16f;

                for (int l = 0; l < lumps; l++)
                {
                    float lx = ((float)random.NextDouble() - 0.5f) * width;
                    float ly = ((float)random.NextDouble() - 0.5f) * width * 0.12f;
                    float size = width * (0.34f + (float)random.NextDouble() * 0.3f);

                    SceneryArt.Attach(cloud, $"Lump_{l}", blob, material,
                        new Vector3(lx, ly, (float)random.NextDouble() * 2f - 1f),
                        Quaternion.Euler(0f, random.Next(0, 360), 0f),
                        new Vector3(size, size * 0.42f, size * 0.7f));
                }

                var drift = cloud.gameObject.AddComponent<CloudDrift>();
                drift.Centre = parent.position;
                drift.Radius = 46f + (float)random.NextDouble() * 48f;
                drift.Height = 22f + (float)random.NextDouble() * 24f;
                drift.Speed = (0.004f + (float)random.NextDouble() * 0.009f)
                              * (random.Next(2) == 0 ? 1f : -1f);
                drift.Phase = (float)random.NextDouble() * 6.28f;
            }
        }

        // ---------------------------------------------------------------- ground

        private static void BuildGround(Transform parent, Landscape landscape, int seed)
        {
            // The biome's own terrain, baked as a surface: grass in the hollows, bare rock on
            // the steep faces, a bleached ridge line, metre-scale patchwork over all of it and
            // a normal map for the relief below that. At full budget this is a 4096 image over
            // a hundred-and-twenty-metre world - roughly a texel every three centimetres.
            NatureTextures.GroundSet surface = NatureTextures.Ground(
                landscape, seed, WorldExtent, FlatRadius, FlatFalloff);

            Mesh mesh = SceneryArt.BuildTerrain(landscape, seed, WorldExtent,
                                                TerrainResolution, FlatRadius, FlatFalloff);

            var ground = SceneryArt.Attach(parent, "Ground", mesh,
                GroundMaterial(surface, landscape),
                new Vector3(0f, -0.01f, 0f), Quaternion.identity, Vector3.one);

            // The one surface in the landscape that takes a shadow. The shadow distance is
            // six metres, so in practice this is the table shadow and nothing else - which is
            // the point: it is what makes the rink look like it is standing on the ground
            // rather than hovering a centimetre above a picture of it.
            var renderer = ground.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.receiveShadows = true;
        }

        /// <summary>
        /// The ground material: URP Lit rather than Unlit, wearing the baked surface and its
        /// normal map. The terrain is the one large surface where the direction of the light
        /// does real work - an unlit hillside has no form at all and reads as cut paper - and
        /// it is also the only surface in the landscape that receives a shadow.
        /// </summary>
        private static Material GroundMaterial(NatureTextures.GroundSet surface,
                                               Landscape landscape)
        {
            // The bump scale is well under one on purpose. The mesh already carries every
            // slope larger than a metre, so this map's only job is the grain below that;
            // pushed to full strength it fights the terrain's own shading and the hillside
            // starts to look like crumpled foil.
            Material material = ProceduralArt.MakeTexturedMaterial(
                surface.Albedo, Color.white, surface.Normal, 0.65f, 0.06f);
            material.name = $"RR_Ground_{landscape.Name}";
            return material;
        }

        /// <summary>
        /// A ring of distant peaks standing beyond the hills.
        ///
        /// These are what give the landscape a horizon line. The terrain mesh alone ends in a
        /// flat edge at sixty metres, and with fog on that edge reads as the world simply
        /// stopping; a ridge standing behind it reads as distance.
        /// </summary>
        private static void BuildRidges(Transform parent, Landscape landscape,
                                        System.Random random)
        {
            if (landscape.RidgeHeight <= 0.5f) return;

            var ridgeGo = new GameObject("Ridges");
            ridgeGo.transform.SetParent(parent, false);

            Mesh cone = SceneryArt.BuildCone(1f, 1f, 9);
            Texture2D stone = NatureTextures.Stone();

            // Two ranges rather than one, and the far one washed most of the way towards the
            // fog colour. This is aerial perspective, and it is the cheapest depth cue there
            // is: a single ring of peaks all the same colour reads as a cardboard cut-out
            // standing at a known distance, because nothing tells the eye it is far away
            // except its size. Two ranges with the further one paler read as miles.
            for (int range = 0; range < 2; range++)
            {
                bool far = range == 1;

                Color tint = far
                    ? Color.Lerp(landscape.RidgeColor, landscape.FogColor, 0.62f)
                    : landscape.RidgeColor;

                Material material = ProceduralArt.MakeTexturedMaterial(
                    stone, tint, null, 1f, 0.04f, new Vector2(2.5f, 2.5f));
                material.name = far ? "RR_RidgeFar" : "RR_RidgeNear";

                int count = far ? 22 : 26;
                for (int i = 0; i < count; i++)
                {
                    float angle = (i / (float)count + (float)random.NextDouble() * 0.02f
                                   + (far ? 0.5f / count : 0f)) * Mathf.PI * 2f;

                    float radius = WorldExtent * (far
                        ? 1.55f + (float)random.NextDouble() * 0.40f
                        : 0.86f + (float)random.NextDouble() * 0.30f);

                    float height = landscape.RidgeHeight * (far ? 2.1f : 1f)
                                   * (0.55f + (float)random.NextDouble() * 0.85f);
                    float width = height * (0.9f + (float)random.NextDouble() * 1.1f);

                    SceneryArt.Attach(ridgeGo.transform, $"Peak_{range}_{i}", cone, material,
                        new Vector3(Mathf.Cos(angle) * radius, -height * 0.18f,
                                    Mathf.Sin(angle) * radius),
                        Quaternion.Euler(0f, random.Next(0, 360), 0f),
                        new Vector3(width, height, width * 0.8f));
                }
            }
        }

        private static void BuildWater(Transform parent, Landscape landscape)
        {
            Material material = ProceduralArt.MakeTexturedUnlit(
                SceneryArt.MakeRippleTexture(), cullFront: false, transparent: true);

            Color tint = Color.Lerp(landscape.WaterShallow, landscape.WaterDeep, 0.45f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
            if (material.HasProperty("_BaseMap")) material.SetTextureScale("_BaseMap", Vector2.one * 18f);
            if (material.HasProperty("_MainTex")) material.SetTextureScale("_MainTex", Vector2.one * 18f);

            Mesh plane = SceneryArt.BuildQuad(WorldExtent * 2.3f, WorldExtent * 2.3f);

            var water = SceneryArt.Attach(parent, "Water", plane, material,
                new Vector3(0f, landscape.WaterLevel, 0f),
                Quaternion.Euler(90f, 0f, 0f), Vector3.one);

            water.AddComponent<Shimmer>();
        }

        // ---------------------------------------------------------------- planting

        /// <summary>
        /// Scatters trees, rocks and ground cover, and returns how many objects were placed.
        ///
        /// Everything is planted at the real terrain height rather than at zero, which is the
        /// difference between a wood and a row of trees hovering over a hillside. Nothing is
        /// planted inside the flat ring around the table: a tree within arm reach of the rink
        /// would be in the way of the throw, both visually and in the sense that a player
        /// would try to lean round it.
        /// </summary>
        private static int Plant(Transform parent, Landscape landscape,
                                 SceneryArt.TerrainNoise noise, System.Random random,
                                 float density)
        {
            var plantedGo = new GameObject("Planting");
            plantedGo.transform.SetParent(parent, false);
            Transform plant = plantedGo.transform;

            // One baked greyscale surface per kind of thing, tinted per biome. Shared
            // textures mean the SRP batcher still sees one material per surface, and the
            // tint is what makes spruce bark and palm bark different things.
            Texture2D bark = NatureTextures.Bark();
            Texture2D leaves = NatureTextures.Leaves();
            Texture2D stone = NatureTextures.Stone();
            Texture2D blades = NatureTextures.Blades();

            Material trunk = ProceduralArt.MakeTexturedMaterial(
                bark, landscape.TrunkColor, null, 1f, 0.08f, new Vector2(1f, 0.55f));
            Material foliageNear = ProceduralArt.MakeTexturedMaterial(
                leaves, landscape.FoliageNear, null, 1f, 0.06f, new Vector2(1.6f, 1.6f));
            Material foliageFar = ProceduralArt.MakeTexturedMaterial(
                leaves, landscape.FoliageFar, null, 1f, 0.06f, new Vector2(1.6f, 1.6f));
            Material rock = ProceduralArt.MakeTexturedMaterial(
                stone, landscape.RockColor, null, 1f, 0.12f, new Vector2(1.4f, 1.4f));

            // The one material in the landscape that clips rather than fills. A tuft used to
            // be two solid rectangles crossed at right angles, which at arm's length reads as
            // litter; with the blade texture clipped out of it, it reads as grass.
            Material grass = ProceduralArt.MakeCutoutMaterial(blades, landscape.GrassColor);

            var shapes = new PlantShapes
            {
                Blob = SceneryArt.BuildBlob(),
                Cone = SceneryArt.BuildCone(1f, 1f, 8),
                Trunk = SceneryArt.BuildPrism(6, 0.5f, 1f),

                // Double-sided: grass tufts and palm fronds are seen from every direction.
                Quad = SceneryArt.BuildQuad(1f, 1f, doubleSided: true)
            };

            int planted = 0;

            int treeCount = Mathf.RoundToInt(landscape.TreeCount * density);
            for (int i = 0; i < treeCount; i++)
            {
                Vector2 spot = SceneryArt.ScatterPoint(random, FlatRadius + 1.6f,
                                                       WorldExtent * 0.82f);
                float y = SceneryArt.HeightAt(noise, landscape, spot.x, spot.y,
                                              FlatRadius, FlatFalloff);

                // Nothing grows in the water.
                if (landscape.HasWater && y < landscape.WaterLevel + 0.15f) continue;

                float scale = landscape.TreeScale
                              * (0.7f + (float)random.NextDouble() * 0.65f);

                // Distance fades the foliage towards the far colour, which is a poor-man
                // aerial perspective and does more for depth than the fog alone.
                float distance = spot.magnitude / (WorldExtent * 0.82f);
                Material crown = distance > 0.45f ? foliageFar : foliageNear;

                var tree = BuildTree(plant, landscape.Trees, $"Tree_{i}",
                                     new Vector3(spot.x, y, spot.y), scale, random,
                                     shapes, trunk, crown);

                if (tree != null)
                {
                    var sway = tree.gameObject.AddComponent<Sway>();
                    sway.Amplitude = Mathf.Lerp(2.2f, 0.7f, scale / 2f);
                    sway.Rate = 0.35f + (float)random.NextDouble() * 0.45f;
                    sway.Phase = (float)random.NextDouble() * 6.28f;
                    planted++;
                }
            }

            int rockCount = Mathf.RoundToInt(landscape.RockCount * density);
            for (int i = 0; i < rockCount; i++)
            {
                Vector2 spot = SceneryArt.ScatterPoint(random, FlatRadius + 0.9f,
                                                       WorldExtent * 0.75f);
                float y = SceneryArt.HeightAt(noise, landscape, spot.x, spot.y,
                                              FlatRadius, FlatFalloff);
                float size = 0.45f + (float)random.NextDouble() * 2.4f;

                SceneryArt.Attach(plant, $"Rock_{i}", shapes.Blob, rock,
                    new Vector3(spot.x, y + size * 0.22f, spot.y),
                    Quaternion.Euler(random.Next(-18, 19), random.Next(0, 360),
                                     random.Next(-18, 19)),
                    new Vector3(size, size * 0.65f, size * 0.85f));
                planted++;
            }

            int grassCount = Mathf.RoundToInt(landscape.GrassCount * density);
            for (int i = 0; i < grassCount; i++)
            {
                // Ground cover is kept to the near ring: it is far too small to be seen
                // further out, so placing it there would cost draw calls for nothing.
                Vector2 spot = SceneryArt.ScatterPoint(random, FlatRadius + 0.4f, 16f);
                float y = SceneryArt.HeightAt(noise, landscape, spot.x, spot.y,
                                              FlatRadius, FlatFalloff);
                if (landscape.HasWater && y < landscape.WaterLevel + 0.05f) continue;

                float height = 0.28f + (float)random.NextDouble() * 0.5f;

                var tuft = new GameObject($"Tuft_{i}").transform;
                tuft.SetParent(plant, false);
                tuft.localPosition = new Vector3(spot.x, y, spot.y);
                tuft.localRotation = Quaternion.Euler(0f, random.Next(0, 360), 0f);

                // Two crossed quads: a cross section reads as a clump from every angle, and
                // it is four triangles.
                for (int blade = 0; blade < 2; blade++)
                {
                    SceneryArt.Attach(tuft, $"Blade_{blade}", shapes.Quad, grass,
                        new Vector3(0f, height * 0.5f, 0f),
                        Quaternion.Euler(0f, blade * 90f, 0f),
                        new Vector3(height * 0.8f, height, 1f));
                }

                var sway = tuft.gameObject.AddComponent<Sway>();
                sway.Amplitude = 5.5f;
                sway.Rate = 0.9f + (float)random.NextDouble() * 0.8f;
                sway.Phase = (float)random.NextDouble() * 6.28f;
                planted++;
            }

            // The layer between the grass at your feet and the trees on the hill.
            planted += PlantBushes(plant, landscape, noise, random, density, shapes,
                                   leaves);
            planted += PlantFlowers(plant, landscape, noise, random, density, shapes,
                                    blades);
            planted += PlantReeds(plant, landscape, noise, random, density, shapes, blades);
            planted += PlantLogs(plant, landscape, noise, random, density, shapes, trunk);

            return planted;
        }

        /// <summary>
        /// Low shrubs through the middle distance: two or three flattened crowns each, no
        /// trunk, at a scale between the ground cover and the trees.
        ///
        /// This is the cheapest thing in the landscape that makes it look inhabited. Trees
        /// standing in open grass is what a diagram of a wood looks like; real ground has
        /// scrub at knee and waist height, and because a bush is a crown with no trunk it
        /// costs a third of what a tree does.
        /// </summary>
        private static int PlantBushes(Transform parent, Landscape landscape,
                                       SceneryArt.TerrainNoise noise, System.Random random,
                                       float density, PlantShapes shapes, Texture2D leaves)
        {
            int count = Mathf.RoundToInt(landscape.BushCount * density);
            if (count <= 0) return 0;

            // Darker and less saturated than the canopy: a shrub sits in the shade of
            // whatever is above it, and matching the canopy exactly makes the two read as one
            // flat mass of green.
            Color tint = Color.Lerp(landscape.FoliageFar, landscape.GroundLow, 0.3f) * 0.92f;
            tint.a = 1f;
            Material material = ProceduralArt.MakeTexturedMaterial(
                leaves, tint, null, 1f, 0.05f, new Vector2(2.2f, 2.2f));
            material.name = "RR_Bush";

            var bushes = new GameObject("Bushes").transform;
            bushes.SetParent(parent, false);

            int planted = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 spot = SceneryArt.ScatterPoint(random, FlatRadius + 1.1f,
                                                       WorldExtent * 0.6f);
                float y = SceneryArt.HeightAt(noise, landscape, spot.x, spot.y,
                                              FlatRadius, FlatFalloff);
                if (landscape.HasWater && y < landscape.WaterLevel + 0.1f) continue;

                float size = 0.55f + (float)random.NextDouble() * 1.25f;

                var bush = new GameObject($"Bush_{i}").transform;
                bush.SetParent(bushes, false);
                bush.localPosition = new Vector3(spot.x, y, spot.y);
                bush.localRotation = Quaternion.Euler(0f, random.Next(0, 360), 0f);

                int lumps = 2 + random.Next(2);
                for (int l = 0; l < lumps; l++)
                {
                    float lx = ((float)random.NextDouble() - 0.5f) * size * 0.7f;
                    float lz = ((float)random.NextDouble() - 0.5f) * size * 0.7f;
                    float lump = size * (0.6f + (float)random.NextDouble() * 0.5f);

                    SceneryArt.Attach(bush, $"Lump_{l}", shapes.Blob, material,
                        new Vector3(lx, lump * 0.4f, lz), Quaternion.identity,
                        new Vector3(lump, lump * 0.72f, lump * 0.9f));
                }

                var sway = bush.gameObject.AddComponent<Sway>();
                sway.Amplitude = 1.8f;
                sway.Rate = 0.5f + (float)random.NextDouble() * 0.5f;
                sway.Phase = (float)random.NextDouble() * 6.28f;
                planted++;
            }

            return planted;
        }

        /// <summary>
        /// Flowering ground cover, kept to the near ring where it can actually be seen.
        ///
        /// Flowers are the one piece of planting whose job is colour rather than form: a few
        /// hundred specks of a colour that appears nowhere else in the biome is what turns a
        /// green hillside into a meadow in spring or a moor in heather. The biomes where
        /// nothing blooms have a count of zero and get none, which is the point of it being a
        /// per-biome number.
        /// </summary>
        private static int PlantFlowers(Transform parent, Landscape landscape,
                                        SceneryArt.TerrainNoise noise, System.Random random,
                                        float density, PlantShapes shapes, Texture2D blades)
        {
            int count = Mathf.RoundToInt(landscape.FlowerCount * density);
            if (count <= 0) return 0;

            Material material = ProceduralArt.MakeCutoutMaterial(blades, landscape.FlowerColor,
                                                                 0.4f);
            material.name = "RR_Flowers";

            var flowers = new GameObject("Flowers").transform;
            flowers.SetParent(parent, false);

            int planted = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 spot = SceneryArt.ScatterPoint(random, FlatRadius + 0.5f, 19f);
                float y = SceneryArt.HeightAt(noise, landscape, spot.x, spot.y,
                                              FlatRadius, FlatFalloff);
                if (landscape.HasWater && y < landscape.WaterLevel + 0.05f) continue;

                float height = 0.16f + (float)random.NextDouble() * 0.26f;

                var clump = new GameObject($"Flower_{i}").transform;
                clump.SetParent(flowers, false);
                clump.localPosition = new Vector3(spot.x, y, spot.y);
                clump.localRotation = Quaternion.Euler(0f, random.Next(0, 360), 0f);

                SceneryArt.Attach(clump, "Petals", shapes.Quad, material,
                    new Vector3(0f, height * 0.5f, 0f), Quaternion.identity,
                    new Vector3(height * 1.1f, height, 1f));

                var sway = clump.gameObject.AddComponent<Sway>();
                sway.Amplitude = 7f;
                sway.Rate = 1.1f + (float)random.NextDouble() * 0.9f;
                sway.Phase = (float)random.NextDouble() * 6.28f;
                planted++;
            }

            return planted;
        }

        /// <summary>
        /// Reeds standing in the shallows, placed by looking for ground just under the
        /// waterline rather than by guessing a radius.
        ///
        /// A water plane meeting bare ground in a clean line is the one part of a generated
        /// landscape that always looks generated - real water has a margin. Reeds are the
        /// cheapest margin there is, and they also hide the exact line where the plane cuts
        /// the terrain, which is where the geometry is thinnest.
        /// </summary>
        private static int PlantReeds(Transform parent, Landscape landscape,
                                      SceneryArt.TerrainNoise noise, System.Random random,
                                      float density, PlantShapes shapes, Texture2D blades)
        {
            if (!landscape.HasWater) return 0;

            int count = Mathf.RoundToInt(landscape.ReedCount * density);
            if (count <= 0) return 0;

            Color tint = Color.Lerp(landscape.GrassColor, landscape.FoliageFar, 0.45f) * 0.9f;
            tint.a = 1f;
            Material material = ProceduralArt.MakeCutoutMaterial(blades, tint, 0.42f);
            material.name = "RR_Reeds";

            var reeds = new GameObject("Reeds").transform;
            reeds.SetParent(parent, false);

            int planted = 0;

            // Rejection sampling against the real height field. Several attempts per reed,
            // because the band of ground within forty centimetres of the waterline is a small
            // fraction of the world and a single try would place most of them nowhere.
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    Vector2 spot = SceneryArt.ScatterPoint(random, FlatRadius + 1f, 34f);
                    float y = SceneryArt.HeightAt(noise, landscape, spot.x, spot.y,
                                                  FlatRadius, FlatFalloff);

                    float depth = landscape.WaterLevel - y;
                    if (depth < -0.1f || depth > 0.45f) continue;

                    float height = 0.5f + (float)random.NextDouble() * 0.9f;

                    var clump = new GameObject($"Reed_{i}").transform;
                    clump.SetParent(reeds, false);
                    clump.localPosition = new Vector3(spot.x, y, spot.y);
                    clump.localRotation = Quaternion.Euler(0f, random.Next(0, 360), 0f);

                    for (int blade = 0; blade < 2; blade++)
                    {
                        SceneryArt.Attach(clump, $"Blade_{blade}", shapes.Quad, material,
                            new Vector3(0f, height * 0.5f, 0f),
                            Quaternion.Euler(0f, blade * 90f, 0f),
                            new Vector3(height * 0.3f, height, 1f));
                    }

                    var sway = clump.gameObject.AddComponent<Sway>();
                    sway.Amplitude = 4.5f;
                    sway.Rate = 0.7f + (float)random.NextDouble() * 0.6f;
                    sway.Phase = (float)random.NextDouble() * 6.28f;
                    planted++;
                    break;
                }
            }

            return planted;
        }

        /// <summary>
        /// Fallen trunks lying on the ground, tilted to sit along the slope they are on.
        ///
        /// Half a dozen of these per biome is a disproportionate amount of the impression
        /// that the place has a history rather than having been planted a second ago. They
        /// are the same prism the trunks are built from, laid on its side.
        /// </summary>
        private static int PlantLogs(Transform parent, Landscape landscape,
                                     SceneryArt.TerrainNoise noise, System.Random random,
                                     float density, PlantShapes shapes, Material trunk)
        {
            if (landscape.Trees == TreeKind.None) return 0;

            int count = Mathf.RoundToInt(landscape.LogCount * density);
            if (count <= 0) return 0;

            var logs = new GameObject("Logs").transform;
            logs.SetParent(parent, false);

            int planted = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 spot = SceneryArt.ScatterPoint(random, FlatRadius + 1.4f, 26f);
                float y = SceneryArt.HeightAt(noise, landscape, spot.x, spot.y,
                                              FlatRadius, FlatFalloff);
                if (landscape.HasWater && y < landscape.WaterLevel + 0.1f) continue;

                float length = 1.8f + (float)random.NextDouble() * 3.4f;
                float thickness = 0.16f + (float)random.NextDouble() * 0.2f;
                float yaw = random.Next(0, 360);

                // Rolled onto its side, then tipped a few degrees so it is lying on the
                // ground rather than balanced on it.
                Quaternion rotation = Quaternion.Euler(0f, yaw, 90f)
                                      * Quaternion.Euler(random.Next(-7, 8), 0f, 0f);

                SceneryArt.Attach(logs, $"Log_{i}", shapes.Trunk, trunk,
                    new Vector3(spot.x, y + thickness * 0.9f, spot.y), rotation,
                    new Vector3(thickness * 2f, length, thickness * 2f));
                planted++;
            }

            return planted;
        }

        /// <summary>Meshes shared by every plant in one landscape, built once.</summary>
        private struct PlantShapes
        {
            public Mesh Blob;
            public Mesh Cone;
            public Mesh Trunk;
            public Mesh Quad;
        }

        private static Transform BuildTree(Transform parent, TreeKind kind, string name,
                                           Vector3 position, float scale, System.Random random,
                                           PlantShapes shapes, Material trunk, Material crown)
        {
            if (kind == TreeKind.None) return null;

            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0f, random.Next(0, 360), 0f);

            switch (kind)
            {
                case TreeKind.Conifer:
                {
                    float height = 3.4f * scale;
                    SceneryArt.Attach(root, "Trunk", shapes.Trunk, trunk, Vector3.zero,
                        Quaternion.identity, new Vector3(0.22f * scale, height * 0.42f,
                                                         0.22f * scale));

                    // Three cones of decreasing width, overlapping: the classic spruce
                    // silhouette, and the overlap hides the join.
                    for (int i = 0; i < 3; i++)
                    {
                        float t = i / 2f;
                        float width = Mathf.Lerp(1.5f, 0.6f, t) * scale;
                        float coneHeight = Mathf.Lerp(1.9f, 1.3f, t) * scale;
                        SceneryArt.Attach(root, $"Skirt_{i}", shapes.Cone, crown,
                            new Vector3(0f, height * (0.22f + t * 0.33f), 0f),
                            Quaternion.identity, new Vector3(width, coneHeight, width));
                    }
                    break;
                }

                case TreeKind.Broadleaf:
                case TreeKind.Blossom:
                {
                    float height = 2.8f * scale;
                    SceneryArt.Attach(root, "Trunk", shapes.Trunk, trunk, Vector3.zero,
                        Quaternion.identity, new Vector3(0.3f * scale, height,
                                                         0.3f * scale));

                    int lumps = 3 + random.Next(2);
                    for (int i = 0; i < lumps; i++)
                    {
                        float size = (1.5f + (float)random.NextDouble() * 0.9f) * scale;
                        float spread = 0.55f * scale;
                        SceneryArt.Attach(root, $"Crown_{i}", shapes.Blob, crown,
                            new Vector3(((float)random.NextDouble() - 0.5f) * spread * 2f,
                                        height + size * 0.22f
                                        + ((float)random.NextDouble() - 0.5f) * spread,
                                        ((float)random.NextDouble() - 0.5f) * spread * 2f),
                            Quaternion.Euler(0f, random.Next(0, 360), 0f),
                            new Vector3(size, size * 0.78f, size));
                    }
                    break;
                }

                case TreeKind.Birch:
                {
                    float height = 3.0f * scale;
                    SceneryArt.Attach(root, "Trunk", shapes.Trunk, trunk, Vector3.zero,
                        Quaternion.identity, new Vector3(0.16f * scale, height,
                                                         0.16f * scale));
                    float size = 1.5f * scale;
                    SceneryArt.Attach(root, "Crown", shapes.Blob, crown,
                        new Vector3(0f, height + size * 0.2f, 0f), Quaternion.identity,
                        new Vector3(size, size * 1.15f, size));
                    break;
                }

                case TreeKind.Palm:
                {
                    // A palm is all about the lean and the arch. The trunk is three short
                    // segments each rotated a little further over, which bends it without a
                    // skinned mesh.
                    float lean = 4f + (float)random.NextDouble() * 9f;
                    float segment = 1.5f * scale;
                    Transform previous = root;

                    for (int i = 0; i < 3; i++)
                    {
                        var piece = new GameObject($"Trunk_{i}").transform;
                        piece.SetParent(previous, false);
                        piece.localPosition = i == 0 ? Vector3.zero
                                                     : new Vector3(0f, segment, 0f);
                        piece.localRotation = Quaternion.Euler(lean, 0f, 0f);

                        SceneryArt.Attach(piece, "Segment", shapes.Trunk, trunk, Vector3.zero,
                            Quaternion.identity, new Vector3(0.22f * scale, segment,
                                                             0.22f * scale));
                        previous = piece;
                    }

                    var top = new GameObject("Fronds").transform;
                    top.SetParent(previous, false);
                    top.localPosition = new Vector3(0f, segment, 0f);

                    int fronds = 7;
                    for (int i = 0; i < fronds; i++)
                    {
                        float yaw = i / (float)fronds * 360f;
                        float droop = 24f + (float)random.NextDouble() * 26f;
                        float length = (1.7f + (float)random.NextDouble() * 0.7f) * scale;

                        var frond = new GameObject($"Frond_{i}").transform;
                        frond.SetParent(top, false);
                        frond.localRotation = Quaternion.Euler(droop, yaw, 0f);

                        SceneryArt.Attach(frond, "Blade", shapes.Quad, crown,
                            new Vector3(0f, 0f, length * 0.5f), Quaternion.Euler(90f, 0f, 0f),
                            new Vector3(0.42f * scale, length, 1f));
                    }
                    break;
                }

                case TreeKind.DeadWood:
                {
                    float height = 2.2f * scale;
                    SceneryArt.Attach(root, "Trunk", shapes.Trunk, trunk, Vector3.zero,
                        Quaternion.identity, new Vector3(0.24f * scale, height,
                                                         0.24f * scale));

                    int branches = 3 + random.Next(3);
                    for (int i = 0; i < branches; i++)
                    {
                        float yaw = random.Next(0, 360);
                        float pitch = 28f + (float)random.NextDouble() * 34f;
                        float length = (0.8f + (float)random.NextDouble() * 0.9f) * scale;

                        var branch = new GameObject($"Branch_{i}").transform;
                        branch.SetParent(root, false);
                        branch.localPosition = new Vector3(
                            0f, height * (0.5f + (float)random.NextDouble() * 0.45f), 0f);
                        branch.localRotation = Quaternion.Euler(pitch, yaw, 0f);

                        SceneryArt.Attach(branch, "Limb", shapes.Trunk, trunk, Vector3.zero,
                            Quaternion.identity, new Vector3(0.1f * scale, length,
                                                             0.1f * scale));
                    }
                    break;
                }

                case TreeKind.Cactus:
                {
                    float height = 2.3f * scale;
                    SceneryArt.Attach(root, "Column", shapes.Trunk, crown, Vector3.zero,
                        Quaternion.identity, new Vector3(0.46f * scale, height, 0.46f * scale));

                    int arms = random.Next(0, 3);
                    for (int i = 0; i < arms; i++)
                    {
                        float yaw = random.Next(0, 360);
                        float armHeight = height * (0.4f + (float)random.NextDouble() * 0.25f);
                        float length = (0.6f + (float)random.NextDouble() * 0.4f) * scale;

                        var elbow = new GameObject($"Arm_{i}").transform;
                        elbow.SetParent(root, false);
                        elbow.localPosition = new Vector3(0f, armHeight, 0f);
                        elbow.localRotation = Quaternion.Euler(72f, yaw, 0f);

                        SceneryArt.Attach(elbow, "Lower", shapes.Trunk, crown, Vector3.zero,
                            Quaternion.identity, new Vector3(0.28f * scale, length,
                                                             0.28f * scale));

                        var upper = new GameObject("Upper").transform;
                        upper.SetParent(elbow, false);
                        upper.localPosition = new Vector3(0f, length, 0f);
                        upper.localRotation = Quaternion.Euler(-72f, 0f, 0f);

                        SceneryArt.Attach(upper, "Limb", shapes.Trunk, crown, Vector3.zero,
                            Quaternion.identity, new Vector3(0.28f * scale, length * 1.2f,
                                                             0.28f * scale));
                    }
                    break;
                }
            }

            return root;
        }

        // ---------------------------------------------------------------- air

        /// <summary>
        /// Whatever drifts through the air in this biome.
        ///
        /// Emitted around the player rather than over the whole world: snow sixty metres away
        /// is invisible, and a single emitter big enough to cover the landscape would need
        /// thousands of particles to look like anything at arm length. A small volume centred
        /// on the player costs a few dozen.
        /// </summary>
        private static void BuildMotes(Transform parent, Landscape landscape)
        {
            if (landscape.Motes == AirMote.None) return;

            var go = new GameObject("AirMotes");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 2.2f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            var emission = ps.emission;
            emission.enabled = true;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14f, 6f, 14f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            // All three velocity axes have to be assigned, and in the same curve mode.
            // Unity validates this at runtime and rejects the whole module with "Particle
            // Velocity curves must all be in the same mode" if one axis is left at its
            // default single-constant - which is easy to do when each kind of weather only
            // cares about two of the three directions.
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            switch (landscape.Motes)
            {
                case AirMote.Snow:
                    main.startLifetime = 9f;
                    main.startSpeed = 0.25f;
                    main.startSize = 0.03f;
                    main.gravityModifier = 0.015f;
                    emission.rateOverTime = 14f;
                    velocity.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                    velocity.y = new ParticleSystem.MinMaxCurve(-0.5f, -0.2f);
                    velocity.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                    break;

                case AirMote.Leaves:
                case AirMote.Petals:
                    main.startLifetime = 11f;
                    main.startSpeed = 0.3f;
                    main.startSize = landscape.Motes == AirMote.Petals ? 0.035f : 0.055f;
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                    main.gravityModifier = 0.02f;
                    emission.rateOverTime = 9f;
                    velocity.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
                    velocity.y = new ParticleSystem.MinMaxCurve(-0.45f, -0.12f);
                    velocity.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

                    var rotation = ps.rotationOverLifetime;
                    rotation.enabled = true;
                    rotation.z = new ParticleSystem.MinMaxCurve(-2.2f, 2.2f);
                    break;

                case AirMote.Fireflies:
                    main.startLifetime = 6f;
                    main.startSpeed = 0.12f;
                    main.startSize = 0.022f;
                    main.gravityModifier = 0f;
                    emission.rateOverTime = 10f;
                    velocity.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                    velocity.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.18f);
                    velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                    break;

                case AirMote.Pollen:
                case AirMote.Dust:
                    main.startLifetime = 14f;
                    main.startSpeed = 0.18f;
                    main.startSize = landscape.Motes == AirMote.Dust ? 0.05f : 0.02f;
                    main.gravityModifier = -0.002f;   // drifts gently upwards
                    emission.rateOverTime = 12f;
                    velocity.x = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
                    velocity.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.1f);
                    velocity.z = new ParticleSystem.MinMaxCurve(-0.22f, 0.22f);
                    break;
            }

            main.startColor = landscape.MoteColor;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f)
                });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // Snow and pollen are light sources as far as the eye is concerned, so they
                // blend additively; leaves and petals are objects, and need alpha.
                bool additive = landscape.Motes == AirMote.Snow
                                || landscape.Motes == AirMote.Pollen
                                || landscape.Motes == AirMote.Fireflies;

                renderer.sharedMaterial = additive
                    ? ProceduralArt.MakeGlowMaterial(landscape.MoteColor, 1.4f)
                    : ProceduralArt.MakeTransparentMaterial(landscape.MoteColor);
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            ps.Play();

            // The weather follows the player: it is only ever meant to be seen close up.
            go.AddComponent<FollowViewer>();
        }

        private static BirdFlock[] BuildFlocks(Transform parent, Landscape landscape,
                                               SceneryArt.TerrainNoise noise,
                                               System.Random random)
        {
            int count = Mathf.Max(0, landscape.FlockCount);
            if (count == 0 || landscape.BirdsPerFlock <= 0) return System.Array.Empty<BirdFlock>();

            var birdsGo = new GameObject("Birds");
            birdsGo.transform.SetParent(parent, false);

            var flocks = new BirdFlock[count];
            for (int i = 0; i < count; i++)
            {
                // Flocks are spread around the table rather than stacked, so there is
                // usually something in the air whichever way the player happens to look.
                float angle = (i / (float)count) * Mathf.PI * 2f
                              + (float)random.NextDouble() * 0.8f;
                float radius = 10f + (float)random.NextDouble() * 18f;
                var centre = new Vector3(Mathf.Cos(angle) * radius, 0f,
                                         Mathf.Sin(angle) * radius);

                float ground = SceneryArt.HeightAt(noise, landscape, centre.x, centre.z,
                                                   FlatRadius, FlatFalloff);
                if (landscape.HasWater) ground = Mathf.Max(ground, landscape.WaterLevel);

                flocks[i] = BirdFlock.Create(birdsGo.transform, landscape, i, random,
                                             parent.TransformPoint(centre), ground);
            }

            return flocks;
        }

        // ---------------------------------------------------------------- light and fog

        /// <summary>
        /// Points the scene light the way the biome wants it, creating one if the scene has
        /// none, and sets the ambient light to match the sky.
        /// </summary>
        private static Light ApplyLighting(Transform parent, Landscape landscape)
        {
            Light sun = null;
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                sun = light;
                break;
            }

            if (sun == null)
            {
                var go = new GameObject("SunLight");
                go.transform.SetParent(parent, false);
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            // The light comes *from* the sun, so it points along the inverse of the
            // direction the disc was placed in. Getting this backwards lights the scene from
            // the far side of the sky and the shadows fall towards the sun, which reads as
            // subtly, unplaceably wrong.
            Vector3 toSun = Quaternion.Euler(-landscape.SunAngles.x, landscape.SunAngles.y, 0f)
                            * Vector3.forward;
            sun.transform.rotation = Quaternion.LookRotation(-toSun, Vector3.up);

            sun.color = landscape.SunColor;
            sun.intensity = landscape.SunIntensity;
            sun.shadows = LightShadows.Soft;

            // Trilight ambient: sky overhead, ground bounce from below. On an overcast moor
            // or a bright beach this is doing more for the look than the directional light.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = landscape.AmbientSky;
            RenderSettings.ambientEquatorColor = Color.Lerp(landscape.AmbientSky,
                                                            landscape.AmbientGround, 0.5f);
            RenderSettings.ambientGroundColor = landscape.AmbientGround;

            return sun;
        }

        private static void ApplyCameraAndFog(Landscape landscape)
        {
            RenderSettings.fog = true;
            RenderSettings.fogColor = landscape.FogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = landscape.FogDensity;

            Camera main = Camera.main;
            if (main == null) return;

            // The world is sixty metres across and the sky sits at a hundred and fifty. The
            // table-scale far plane the game shipped with would clip the entire landscape
            // away, so it is raised here rather than left to whoever built the camera.
            if (main.farClipPlane < SkyRadius * 2.2f) main.farClipPlane = SkyRadius * 2.2f;

            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = landscape.SkyHorizon;
        }
    }
}
