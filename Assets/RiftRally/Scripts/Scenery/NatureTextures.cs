using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using RiftRally.Setup;

namespace RiftRally.Scenery
{
    /// <summary>
    /// Bakes the landscape's surfaces as real high-resolution textures - 4096 square for the
    /// ground and for the sky on a desktop GPU - instead of the colour ramps the scenery used
    /// to be shaded with.
    ///
    /// Why this exists at all: the first version of the landscape got its colour from a
    /// 128-pixel ramp sampled by a height coordinate written into the mesh. That is a very
    /// cheap way to shade a hillside and it reads correctly in a screenshot, but in a headset
    /// it fails in a specific way - a surface with no detail anywhere between one metre and
    /// sixty has no sense of scale, so the hills read as a painted backdrop rather than as
    /// ground you could walk on. Stereo vision makes that worse rather than better: both eyes
    /// agree the shape is distant, and flat colour gives them nothing to converge on.
    ///
    /// What fixes it is texture detail at several scales at once, which is what this bakes:
    /// metre-scale patches of grass, soil and rock, decimetre grain under that, and a normal
    /// map for the relief below even that. The resolution is the whole point of the exercise,
    /// so it is a budget rather than a constant: <see cref="Ultra"/> on a desktop GPU, half
    /// that on a standalone headset, and lower again once the frame rate says so.
    ///
    /// The bake is fast enough to run at load time because of two deliberate choices. The
    /// terrain's large-scale shape is evaluated once at a coarse resolution on the main thread
    /// - it has to come from <see cref="SceneryArt.HeightAt"/>, the same function the mesh and
    /// the planting use, or the texture would disagree with the ground it is painted on - and
    /// everything finer than that is generated from this class's own thread-safe noise across
    /// every core the device has.
    /// </summary>
    public static class NatureTextures
    {
        /// <summary>Desktop-class detail. This is the "4K" in a 4K landscape.</summary>
        public const int Ultra = 4096;

        /// <summary>Standalone-headset detail: a quarter of the memory, still far from flat.</summary>
        public const int High = 2048;

        public const int Standard = 1024;
        public const int Economy = 512;

        private static int _budget;

        /// <summary>
        /// Whether baked textures keep their CPU-side copy after being uploaded.
        ///
        /// Off in a game, because at 4096 the managed copy of one map is sixty-seven
        /// megabytes of system memory that nothing will ever read again. On in tests and
        /// tools, which check what was actually baked rather than that a bake happened - and
        /// "a texture exists" is not a guard worth having when the failure being guarded
        /// against is a texture that came out flat.
        /// </summary>
        public static bool KeepReadable { get; set; }

        /// <summary>
        /// Longest edge any baked surface is allowed. Always a power of two between 256 and
        /// <see cref="Ultra"/>. Changing it throws the cache away, so the next landscape built
        /// comes back at the new resolution.
        /// </summary>
        public static int Budget
        {
            get
            {
                if (_budget <= 0) _budget = DefaultBudget();
                return _budget;
            }
            set
            {
                int clamped = Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), 256, Ultra);
                if (clamped == _budget) return;
                _budget = clamped;
                Flush();
            }
        }

        /// <summary>
        /// The texture budget for a render preset.
        ///
        /// A standalone headset is capped below the desktop figure on purpose, and not because
        /// of fill rate: a 4096 albedo plus its normal map is about 134 MB of uncompressed
        /// video memory, which on a Quest is a large slice of the whole budget and buys detail
        /// nobody can resolve through that panel anyway.
        /// </summary>
        public static int BudgetFor(QualityTuner.Preset preset) => preset switch
        {
            QualityTuner.Preset.Crisp => Application.isMobilePlatform ? High : Ultra,
            QualityTuner.Preset.Balanced => Application.isMobilePlatform ? Standard : High,
            _ => Economy
        };

        private static int DefaultBudget() => Application.isMobilePlatform ? High : Ultra;

        /// <summary>The ground's surface: what it looks like, and the relief under that.</summary>
        public sealed class GroundSet
        {
            public Texture2D Albedo;
            public Texture2D Normal;
            public int Resolution;
        }

        // Baking a biome is the most expensive thing the landscape does, and the rotation
        // comes back round to the same eight biomes for the whole life of the install, so the
        // results are kept. Only a couple are held at once: at 4096 each set is over a hundred
        // megabytes, and a cache that remembered all eight would be worse than no cache.
        private const int MaxCachedGrounds = 2;

        private static readonly Dictionary<string, GroundSet> Grounds = new();
        private static readonly List<string> GroundOrder = new();
        private static readonly Dictionary<string, Texture2D> Skies = new();
        private static readonly Dictionary<string, Texture2D> Surfaces = new();

        /// <summary>
        /// Throws every baked texture away. Called when the budget changes, and by tests so
        /// one test's resolution cannot leak into the next.
        /// </summary>
        public static void Flush()
        {
            foreach (GroundSet set in Grounds.Values)
            {
                Destroy(set.Albedo);
                Destroy(set.Normal);
            }
            foreach (Texture2D sky in Skies.Values) Destroy(sky);
            foreach (Texture2D surface in Surfaces.Values) Destroy(surface);

            Grounds.Clear();
            GroundOrder.Clear();
            Skies.Clear();
            Surfaces.Clear();
        }

        private static void Destroy(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }

        // ------------------------------------------------------------------ ground

        /// <summary>
        /// Bakes the ground of <paramref name="landscape"/> as seen from above: at full budget
        /// that is one texel per three centimetres of a hundred-and-twenty-metre world.
        ///
        /// The texture is addressed by a planar world-space UV rather than by a height
        /// coordinate, which is what lets one image carry grass in the hollows, rock on the
        /// steep faces and a bleached ridge top, each in the place the terrain actually puts
        /// it - and all on one stock URP material.
        /// </summary>
        public static GroundSet Ground(Landscape landscape, int seed, float extent,
                                       float flatRadius, float flatFalloff)
        {
            if (landscape == null) landscape = LandscapeCatalog.At(0);

            int resolution = Budget;
            string key = $"{landscape.Name}|{seed}|{resolution}";
            if (Grounds.TryGetValue(key, out GroundSet cached) && cached.Albedo != null)
                return cached;

            GroundSet set = BakeGround(landscape, seed, extent, flatRadius, flatFalloff,
                                       resolution);

            Grounds[key] = set;
            GroundOrder.Add(key);
            while (GroundOrder.Count > MaxCachedGrounds)
            {
                string oldest = GroundOrder[0];
                GroundOrder.RemoveAt(0);
                if (!Grounds.TryGetValue(oldest, out GroundSet evicted)) continue;
                Destroy(evicted.Albedo);
                Destroy(evicted.Normal);
                Grounds.Remove(oldest);
            }

            return set;
        }

        private static GroundSet BakeGround(Landscape landscape, int seed, float extent,
                                            float flatRadius, float flatFalloff,
                                            int resolution)
        {
            // Pass one, on the main thread: the biome's own terrain function, sampled
            // coarsely. Mathf.PerlinNoise is engine code and carries no thread-safety
            // promise, and this is the only part of the bake that needs it - so it is the
            // only part that is not parallel. A 512 grid over a 124-metre world is a sample
            // every 24 centimetres, finer than the mesh the texture is painted onto.
            int coarse = Mathf.Clamp(resolution / 8, 64, 512);
            var height = new float[coarse * coarse];
            var relief = new float[coarse * coarse];
            var noise = new SceneryArt.TerrainNoise(seed, landscape.HillRoughness);
            float span = extent * 2f;
            float coarseStep = span / (coarse - 1);

            for (int y = 0; y < coarse; y++)
            {
                float wz = -extent + y * coarseStep;
                for (int x = 0; x < coarse; x++)
                {
                    float wx = -extent + x * coarseStep;
                    int i = y * coarse + x;

                    // Two fields, and the difference between them is the whole reason this
                    // bake stopped painting a bullseye on the ground.
                    //
                    // The ground is flattened to a level disc under the table. Colouring from
                    // that flattened field puts a plateau of valley colour under the rink and
                    // - far worse - a ring of steep "slope" around the edge of the disc,
                    // which the rock rule then painted grey. The result was a target ring
                    // around the table in every biome.
                    //
                    // So the colouring reads the hill the terrain would have been without the
                    // flattening, which carries straight across the disc with nothing to see;
                    // only the things that genuinely care where the ground really is - how
                    // wet it is next to the water - read the real one.
                    height[i] = SceneryArt.HeightAt(noise, landscape, wx, wz,
                                                    flatRadius, flatFalloff);
                    relief[i] = SceneryArt.HeightAt(noise, landscape, wx, wz, 0f, 0.001f);
                }
            }

            // Slope from the coarse field. This is what separates a cliff from a meadow, and
            // it comes from the same samples the mesh is built from, so the rock appears on
            // the faces that really are steep.
            var slope = new float[coarse * coarse];
            for (int y = 0; y < coarse; y++)
            {
                for (int x = 0; x < coarse; x++)
                {
                    int left = y * coarse + Mathf.Max(0, x - 1);
                    int right = y * coarse + Mathf.Min(coarse - 1, x + 1);
                    int down = Mathf.Max(0, y - 1) * coarse + x;
                    int up = Mathf.Min(coarse - 1, y + 1) * coarse + x;

                    float dx = (relief[right] - relief[left]) / (coarseStep * 2f);
                    float dz = (relief[up] - relief[down]) / (coarseStep * 2f);
                    slope[y * coarse + x] = Mathf.Sqrt(dx * dx + dz * dz);
                }
            }

            var albedo = new Color32[resolution * resolution];
            var normal = new Color32[resolution * resolution];

            var detail = new Noise2D(seed ^ 0x5F3A91);
            var patchNoise = new Noise2D(seed ^ 0x13C7D5);

            float range = Mathf.Max(0.01f, landscape.HillHeight);
            float low = -SceneryArt.GroundBaseline * range;
            float high = (1f - SceneryArt.GroundBaseline) * range;

            Color groundLow = landscape.GroundLow;
            Color groundMid = landscape.GroundMid;
            Color groundHigh = landscape.GroundHigh;
            Color rock = landscape.RockColor;
            Color grass = landscape.GrassColor;
            bool hasWater = landscape.HasWater;
            float waterLevel = landscape.WaterLevel;

            // How far a texel is in metres. The normal map's gradients are in real units, so
            // the relief does not change strength when the budget does.
            float metresPerTexel = span / resolution;
            bool packForAg = PackNormalsForAg();

            // Pass two, across every core: everything smaller than the coarse grid.
            Parallel.For(0, resolution, y =>
            {
                float v = (y + 0.5f) / resolution;
                float wz = -extent + v * span;

                for (int x = 0; x < resolution; x++)
                {
                    float u = (x + 0.5f) / resolution;
                    float wx = -extent + u * span;
                    int i = y * resolution + x;

                    float h = Bilinear(height, coarse, u, v);
                    float hill = Bilinear(relief, coarse, u, v);
                    float s = Bilinear(slope, coarse, u, v);

                    float elevation = Mathf.Clamp01(Mathf.InverseLerp(low, high, hill));

                    // Three scales of variation, and they do different jobs. The broad one is
                    // the size of a field or a stand of trees and is what gives the ground
                    // regions; the patch is metre-scale and breaks up the boundaries between
                    // them; the grain is what the eye reads as texture at walking distance.
                    float broad = patchNoise.Fbm(wx * 0.028f, wz * 0.028f, 3);
                    float patch = patchNoise.Fbm(wx * 0.11f, wz * 0.11f, 3);
                    float grain = detail.Fbm(wx * 1.3f, wz * 1.3f, 4);
                    float speck = detail.Value(wx * 9.5f, wz * 9.5f);

                    // The same three-stop elevation blend the old ramp did, with the noise
                    // pushing the boundaries around so they do not read as contour lines. The
                    // broad term is weighted heavily: at a quarter of its current strength
                    // the hillside came out as one flat tint with speckle on it, which is a
                    // more expensive way of having no texture at all.
                    float band = Mathf.Clamp01(elevation * 0.80f
                                               + (broad - 0.5f) * 0.42f
                                               + (patch - 0.5f) * 0.20f);
                    Color colour = band < 0.5f
                        ? Color.Lerp(groundLow, groundMid, band * 2f)
                        : Color.Lerp(groundMid, groundHigh, (band - 0.5f) * 2f);

                    // Bare rock wherever the hill is too steep to hold soil.
                    float bare = Mathf.Clamp01((s - 0.38f) * 1.7f);
                    colour = Color.Lerp(colour, rock, bare * (0.45f + patch * 0.55f));

                    // Grass takes the sheltered low ground in broad sweeps and loses to the
                    // ridges. Driven by the broad noise rather than the patch, so it arrives
                    // as meadows rather than as a green haze over everything.
                    float lush = Mathf.Clamp01((1f - elevation) * 1.35f) * (1f - bare);
                    colour = Color.Lerp(colour, grass,
                                        lush * Mathf.Clamp01(broad * 1.45f - 0.15f) * 0.85f);

                    // Damp ground around a waterline reads darker, which is most of what
                    // makes a lake look like it sits in the land rather than on it.
                    if (hasWater)
                    {
                        // The unflattened hill again, for the same reason as the bands: read
                        // from the real ground, the level disc under the table sits at a
                        // fixed distance above the waterline while everything around it
                        // varies, and it prints itself onto the texture as a dark coin.
                        float wet = Mathf.Clamp01(1f - (hill - waterLevel) / 1.2f);
                        colour = Color.Lerp(colour, colour * 0.62f, wet * 0.8f);
                    }

                    // Grain and speckle as a brightness multiplier. This is the scale that
                    // does the work in a headset: without it the ground is a flat field of
                    // colour and the eye has nothing to judge distance by.
                    // Brightness at three scales, not just colour at three scales. Several
                    // biomes - the autumn wood, the valley in blossom - are built from three
                    // ground colours that are close together by design, and blending between
                    // them produced a flat tint however hard the noise pushed. Light and
                    // shade across tens of metres works in every palette, because it is the
                    // value that changes rather than the hue, and it reads as cloud shadow
                    // and open ground rather than as noise.
                    float shade = (0.78f + grain * 0.40f + (speck - 0.5f) * 0.06f)
                                  * (0.90f + broad * 0.20f);
                    colour.r = Mathf.Clamp01(colour.r * shade);
                    colour.g = Mathf.Clamp01(colour.g * shade);
                    colour.b = Mathf.Clamp01(colour.b * shade);
                    colour.a = 1f;

                    albedo[i] = ToColor32(colour);

                    // Relief from the same grain field, by central difference in world units.
                    float e = metresPerTexel;
                    float hx = detail.Fbm((wx + e) * 1.3f, wz * 1.3f, 4)
                             - detail.Fbm((wx - e) * 1.3f, wz * 1.3f, 4);
                    float hy = detail.Fbm(wx * 1.3f, (wz + e) * 1.3f, 4)
                             - detail.Fbm(wx * 1.3f, (wz - e) * 1.3f, 4);

                    // Scaled by the texel size so this is a real gradient: six centimetres of
                    // relief per unit of noise, flattening where bare rock takes over, because
                    // a cliff face gets its form from the mesh rather than from this.
                    float strength = 0.06f * (1f - bare * 0.5f) / Mathf.Max(1e-4f, e);
                    Vector3 n = new Vector3(-hx * strength, -hy * strength, 1f).normalized;
                    normal[i] = EncodeNormal(n, packForAg);
                }
            });

            var albedoMap = new Texture2D(resolution, resolution, TextureFormat.RGBA32,
                                          true, false)
            {
                name = $"RR_Ground_{landscape.Name}_{resolution}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            albedoMap.SetPixelData(albedo, 0);
            albedoMap.Apply(true, !KeepReadable);

            var normalMap = new Texture2D(resolution, resolution, TextureFormat.RGBA32,
                                          true, true)
            {
                name = $"RR_GroundNormal_{landscape.Name}_{resolution}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            normalMap.SetPixelData(normal, 0);
            normalMap.Apply(true, !KeepReadable);

            return new GroundSet
            {
                Albedo = albedoMap,
                Normal = normalMap,
                Resolution = resolution
            };
        }

        // ------------------------------------------------------------------ sky

        /// <summary>
        /// Bakes the sky as a two-dimensional image rather than as a vertical ramp.
        ///
        /// The dome's UV.x is the old horizon-to-zenith coordinate, so the gradient still
        /// lands exactly where it used to; UV.y is the compass bearing, which is the new axis
        /// and the reason this is worth baking at all. A sky that varies only with height
        /// looks identical whichever way the player faces, and a player in a headset turns
        /// around constantly - so the one thing the old sky could not do is the first thing
        /// anybody notices. Cirrus banding, a glow where the sun is and a brighter quarter of
        /// the horizon give the world a direction to be oriented by.
        /// </summary>
        public static Texture2D Sky(Landscape landscape)
        {
            if (landscape == null) landscape = LandscapeCatalog.At(0);

            int elevationRes = Mathf.Clamp(Budget / 4, 128, 1024);
            int azimuthRes = Mathf.Clamp(Budget, 512, Ultra);

            string key = $"{landscape.Name}|{elevationRes}x{azimuthRes}";
            if (Skies.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            var pixels = new Color32[elevationRes * azimuthRes];
            var cloud = new Noise2D(landscape.Name.GetHashCode() ^ 0x2B1F07);
            var star = new Noise2D(landscape.Name.GetHashCode() ^ 0x77A3E1);

            Color haze = landscape.SkyHaze;
            Color horizon = landscape.SkyHorizon;
            Color zenith = landscape.SkyZenith;
            Color cloudColour = landscape.CloudColor;
            Color sunColour = landscape.SunColor;
            bool clouds = landscape.Clouds;

            // Where the sun is, in the coordinates the dome is drawn in, so the glow in the
            // texture sits behind the sun disc the builder places rather than beside it.
            float sunAzimuth = landscape.SunAngles.y;
            float sunElevation = Mathf.Clamp01(Mathf.Sin(landscape.SunAngles.x * Mathf.Deg2Rad));
            bool night = Luminance(zenith) < 0.12f;

            Parallel.For(0, azimuthRes, a =>
            {
                float azimuth = (a + 0.5f) / azimuthRes;
                float angle = azimuth * Mathf.PI * 2f;

                // Sampling the noise around a circle rather than along a strip is what makes
                // the sky seamless where the compass wraps. A strip has a visible join, and a
                // visible join in a sky is a visible join at eye height.
                float cx = Mathf.Cos(angle), cz = Mathf.Sin(angle);

                float bearing = Mathf.Abs(Mathf.DeltaAngle(azimuth * 360f, sunAzimuth)) / 180f;

                for (int e = 0; e < elevationRes; e++)
                {
                    float elevation = e / (float)(elevationRes - 1);
                    int i = a * elevationRes + e;

                    // Haze crushed into the bottom fifth, then horizon to zenith above it:
                    // the distribution real sky colour actually has.
                    Color colour = elevation < 0.2f
                        ? Color.Lerp(haze, horizon, elevation / 0.2f)
                        : Color.Lerp(horizon, zenith,
                                     Mathf.Pow((elevation - 0.2f) / 0.8f, 0.75f));

                    // A bright quarter around the sun's bearing, strongest at its own height.
                    // Wide and strong rather than subtle: measured across the finished image
                    // a cautious version of this came out at barely one per cent of
                    // brightness, which is a sky that is still the same backdrop in all four
                    // directions - the exact thing a two-dimensional sky exists to fix.
                    float toSun = Mathf.Clamp01(1f - bearing * 1.25f)
                                  * Mathf.Clamp01(1f - Mathf.Abs(elevation - sunElevation) * 1.7f);
                    colour = Color.Lerp(colour, Color.Lerp(colour, sunColour, 0.68f),
                                        Mathf.Pow(toSun, 1.7f));

                    // Cirrus: stretched sideways, thinning towards the zenith, squashed
                    // towards the horizon the way perspective does to a cloud deck.
                    if (clouds)
                    {
                        float deck = Mathf.Pow(Mathf.Clamp01(elevation * 1.25f), 0.55f);
                        float wisp = cloud.Fbm(cx * 2.6f + 11f, cz * 2.6f + elevation * 5.5f, 4);
                        float streak = cloud.Fbm(cx * 7f, cz * 7f + elevation * 1.4f, 3);
                        float cover = Mathf.Clamp01((wisp * 0.72f + streak * 0.28f - 0.47f) * 3.9f)
                                      * deck * (1f - elevation * 0.45f);
                        colour = Color.Lerp(colour, cloudColour, cover * cloudColour.a * 0.95f);
                    }

                    // Stars, only where the sky is dark enough for them to be believable.
                    if (night && elevation > 0.08f)
                    {
                        float field = star.Value(cx * 220f, cz * 220f + elevation * 180f);
                        if (field > 0.9965f)
                        {
                            float twinkle = (field - 0.9965f) / 0.0035f;
                            colour += new Color(twinkle, twinkle, twinkle * 0.95f, 0f) * 0.85f;
                        }
                    }

                    colour.a = 1f;
                    pixels[i] = ToColor32(colour);
                }
            });

            var texture = new Texture2D(elevationRes, azimuthRes, TextureFormat.RGBA32,
                                        true, false)
            {
                name = $"RR_Sky_{landscape.Name}",
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                // Clamped up the gradient, repeating around the compass: the dome's skirt
                // below the horizon has to keep the haze colour rather than wrap back to the
                // zenith, and the compass has to join up with itself.
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Repeat
            };
            texture.SetPixelData(pixels, 0);
            texture.Apply(true, !KeepReadable);

            Skies[key] = texture;
            return texture;
        }

        // ------------------------------------------------------------------ surfaces

        /// <summary>
        /// A tiling bark texture: vertical fibre, deep splits and a darker crevice between the
        /// ridges. In most biomes a trunk is the nearest natural thing to the player.
        /// </summary>
        public static Texture2D Bark() => Surface("bark", 0.25f, (u, v, n) =>
        {
            // Fibre runs up the trunk, so the noise is stretched hard along v.
            float fibre = n.Fbm(u * 26f, v * 3.2f, 3);
            float split = n.Fbm(u * 7f, v * 1.1f, 2);
            float crack = Mathf.Pow(Mathf.Abs(split - 0.5f) * 2f, 0.6f);
            float shade = 0.52f + fibre * 0.42f + crack * 0.22f;
            return new Color(shade, shade * 0.97f, shade * 0.92f, 1f);
        });

        /// <summary>
        /// A tiling leaf mass: overlapping blotches at two scales, so a crown reads as leaves
        /// catching the light rather than as a painted ball.
        /// </summary>
        public static Texture2D Leaves() => Surface("leaves", 0.25f, (u, v, n) =>
        {
            float clump = n.Fbm(u * 9f, v * 9f, 3);
            float leaf = n.Fbm(u * 34f, v * 34f, 2);
            float shade = 0.56f + clump * 0.38f + (leaf - 0.5f) * 0.30f;
            return new Color(shade * 0.94f, shade, shade * 0.86f, 1f);
        });

        /// <summary>Granular rock with mineral veining, for boulders and scree.</summary>
        public static Texture2D Stone() => Surface("stone", 0.25f, (u, v, n) =>
        {
            float grain = n.Fbm(u * 18f, v * 18f, 4);
            float vein = Mathf.Abs(n.Fbm(u * 5f, v * 5f, 2) - 0.5f) * 2f;
            float shade = 0.62f + grain * 0.32f - Mathf.Pow(vein, 2.5f) * 0.18f;
            return new Color(shade, shade * 0.99f, shade * 0.97f, 1f);
        });

        /// <summary>
        /// A clump of grass blades on a transparent background, for the cutout quads the
        /// ground cover is made of. A tuft used to be two solid rectangles; with this it is
        /// two clumps of blades with daylight between them, which is the whole difference
        /// between ground cover and confetti.
        /// </summary>
        public static Texture2D Blades() => Surface("blades", 0.25f, (u, v, n) =>
        {
            // Blades are near-vertical: a column of noise decides whether this x is a blade
            // at all, and the blade thins and leans as it rises.
            float lean = (n.Value(u * 3.1f, 7.3f) - 0.5f) * 0.22f * v;
            float column = n.Value((u + lean) * 24f, 1.7f);
            float width = Mathf.Lerp(0.42f, 0.14f, v);
            float blade = Mathf.Clamp01((column - (1f - width)) / Mathf.Max(0.01f, width));

            // Tapered away at the top, so the clump ends in tips rather than a straight cut.
            float taper = Mathf.Clamp01((1f - v) * 2.4f);
            float alpha = Mathf.Clamp01(blade * 3.2f) * taper;

            // Dark at the root, bright at the tip: free ambient occlusion for a flat quad.
            float shade = Mathf.Lerp(0.55f, 1.12f, v) * (0.85f + column * 0.3f);
            return new Color(shade, shade, shade, alpha);
        });

        /// <summary>
        /// A soft tiling ripple for water, at the current budget rather than at 64 pixels.
        /// Two scrolling copies of this are what make a flat plane read as a lake.
        /// </summary>
        public static Texture2D Ripples() => Surface("ripples", 0.125f, (u, v, n) =>
        {
            // Whole numbers of cycles, so the tile is seamless at its edges.
            float wave = Mathf.Sin((u * 4f + v * 2f) * Mathf.PI * 2f) * 0.5f
                       + Mathf.Sin((u * 2f - v * 5f) * Mathf.PI * 2f) * 0.3f
                       + Mathf.Sin((u * 7f + v * 6f) * Mathf.PI * 2f) * 0.2f;
            float chop = n.Fbm(u * 16f, v * 16f, 3) - 0.5f;
            float shade = 0.84f + wave * 0.12f + chop * 0.10f;
            return new Color(shade, shade, shade, 1f);
        });

        private delegate Color Shade2D(float u, float v, Noise2D noise);

        /// <summary>
        /// Bakes a seamless tiling surface at <paramref name="scaleOfBudget"/> of the current
        /// budget. These are tiled over many objects each, so they do not need the ground's
        /// resolution - and they are shared between every biome, which is why they are tinted
        /// by the material rather than baked per biome.
        /// </summary>
        private static Texture2D Surface(string id, float scaleOfBudget, Shade2D shade)
        {
            int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo((int)(Budget * scaleOfBudget)),
                                   64, Ultra);
            string key = $"{id}|{size}";
            if (Surfaces.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            var pixels = new Color32[size * size];
            var noise = new Noise2D(id.GetHashCode());

            Parallel.For(0, size, y =>
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    pixels[y * size + x] = ToColor32(shade(u, v, noise));
                }
            });

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = $"RR_Surface_{id}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            texture.SetPixelData(pixels, 0);
            texture.Apply(true, !KeepReadable);

            Surfaces[key] = texture;
            return texture;
        }

        // ------------------------------------------------------------------ helpers

        private static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        private static Color32 ToColor32(Color c) => new Color32(
            (byte)(Mathf.Clamp01(c.r) * 255f),
            (byte)(Mathf.Clamp01(c.g) * 255f),
            (byte)(Mathf.Clamp01(c.b) * 255f),
            (byte)(Mathf.Clamp01(c.a) * 255f));

        /// <summary>
        /// Whether this platform's shaders decode a normal map from its alpha and green
        /// channels rather than straight from RGB.
        ///
        /// This is not a detail that can be left to chance. URP's shader library picks its
        /// unpacking by platform - mobile builds read RGB, desktop builds read the DXT5nm pair
        /// - and a texture generated at runtime has no importer to normalise it. Encoding for
        /// the wrong one does not look like a bug in the texture; it looks like the light is
        /// coming from the wrong place, which is the kind of thing that gets blamed on the sun
        /// angle for a week.
        /// </summary>
        private static bool PackNormalsForAg() =>
            !(Application.isMobilePlatform || Application.platform == RuntimePlatform.Android);

        /// <summary>
        /// Packs a tangent-space normal for whichever decoder is in play. The RGB form is the
        /// obvious one; the AG form stores x in alpha with red held at one, because that
        /// decoder multiplies alpha by red before it uses it.
        /// </summary>
        private static Color32 EncodeNormal(Vector3 n, bool forAg)
        {
            float x = n.x * 0.5f + 0.5f;
            float y = n.y * 0.5f + 0.5f;
            float z = n.z * 0.5f + 0.5f;

            return forAg
                ? new Color32(255, (byte)(y * 255f), 255, (byte)(x * 255f))
                : new Color32((byte)(x * 255f), (byte)(y * 255f), (byte)(z * 255f), 255);
        }

        /// <summary>Bilinear lookup into a square field addressed by a 0..1 UV.</summary>
        private static float Bilinear(float[] field, int size, float u, float v)
        {
            float fx = Mathf.Clamp01(u) * (size - 1);
            float fy = Mathf.Clamp01(v) * (size - 1);

            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Mathf.Min(size - 1, x0 + 1), y1 = Mathf.Min(size - 1, y0 + 1);
            float tx = fx - x0, ty = fy - y0;

            float a = Mathf.Lerp(field[y0 * size + x0], field[y0 * size + x1], tx);
            float b = Mathf.Lerp(field[y1 * size + x0], field[y1 * size + x1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>
        /// Hashed value noise, written out by hand rather than taken from the engine.
        ///
        /// The reason is threading, not taste. <see cref="Mathf.PerlinNoise"/> is engine code
        /// and carries no thread-safety promise, and the whole point of this bakery is that
        /// sixteen million texels are generated across every core at once. This is a pure
        /// function of its inputs with no shared state, so it can be called from anywhere, and
        /// being integer-hashed it produces the same field on every platform and every run.
        /// </summary>
        public readonly struct Noise2D
        {
            private readonly int _seed;

            public Noise2D(int seed) => _seed = seed;

            private static float Hash(int x, int y, int seed)
            {
                unchecked
                {
                    int h = x * 374761393 + y * 668265263 + seed * 1274126177;
                    h = (h ^ (h >> 13)) * 1274126177;
                    h ^= h >> 16;
                    return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
                }
            }

            /// <summary>Smoothly interpolated value noise in 0..1.</summary>
            public float Value(float x, float y)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                float fx = x - xi, fy = y - yi;

                // Smoothstep on the fraction, which is what stops the lattice showing as a
                // grid of diamonds.
                fx = fx * fx * (3f - 2f * fx);
                fy = fy * fy * (3f - 2f * fy);

                float a = Hash(xi, yi, _seed);
                float b = Hash(xi + 1, yi, _seed);
                float c = Hash(xi, yi + 1, _seed);
                float d = Hash(xi + 1, yi + 1, _seed);

                return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
            }

            /// <summary>Several octaves of <see cref="Value"/>, normalised back to 0..1.</summary>
            public float Fbm(float x, float y, int octaves, float gain = 0.5f,
                             float lacunarity = 2.07f)
            {
                octaves = Mathf.Clamp(octaves, 1, 8);

                float sum = 0f, amplitude = 1f, total = 0f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Value(x, y) * amplitude;
                    total += amplitude;
                    amplitude *= gain;

                    // The lacunarity is deliberately not exactly two: a power-of-two cascade
                    // lines every octave's lattice up on the same points, and the sum of that
                    // shows a faint grid.
                    x = x * lacunarity + 17.3f;
                    y = y * lacunarity + 11.7f;
                }

                return total <= 0f ? 0f : sum / total;
            }
        }
    }
}
