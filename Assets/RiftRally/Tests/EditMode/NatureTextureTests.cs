using NUnit.Framework;
using UnityEngine;
using RiftRally.Scenery;
using RiftRally.Setup;

namespace RiftRally.Tests
{
    /// <summary>
    /// Guards on the baked landscape surfaces.
    ///
    /// These matter because the failure mode of a procedural texture is not an exception, it
    /// is a flat colour. Every one of these tests was written against a way the bake can come
    /// out looking plausible in the inspector and wrong in the headset: a ground texture whose
    /// noise is scaled so finely it averages to grey, a sky that is identical whichever way
    /// the player turns, a grass cutout with no gaps in it, a budget that silently clamps to
    /// something other than what was asked for. None of those break a build.
    /// </summary>
    public class NatureTextureTests
    {
        private const float WorldExtent = 62f;

        [SetUp]
        public void SetUp()
        {
            // Read back what was baked, and bake it small: these tests are about the content
            // of the image, and the content does not change with its resolution.
            NatureTextures.KeepReadable = true;
            NatureTextures.Budget = NatureTextures.Standard;
        }

        [TearDown]
        public void TearDown()
        {
            NatureTextures.Flush();
            NatureTextures.KeepReadable = false;
        }

        private static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        // ---------------------------------------------------------------- the budget

        [Test]
        public void TheTextureBudgetFallsWithTheQualityPreset()
        {
            int crisp = NatureTextures.BudgetFor(QualityTuner.Preset.Crisp);
            int balanced = NatureTextures.BudgetFor(QualityTuner.Preset.Balanced);
            int fast = NatureTextures.BudgetFor(QualityTuner.Preset.Fast);

            Assert.That(crisp, Is.GreaterThan(balanced));
            Assert.That(balanced, Is.GreaterThan(fast));
        }

        [Test]
        public void TheCrispPresetIsFourKayOnADesktopGpu()
        {
            if (Application.isMobilePlatform)
            {
                Assert.Pass("a standalone headset is capped below 4K on purpose");
                return;
            }

            Assert.That(NatureTextures.BudgetFor(QualityTuner.Preset.Crisp),
                Is.EqualTo(NatureTextures.Ultra),
                "the top preset is the whole point of the texture ladder existing");
            Assert.That(NatureTextures.Ultra, Is.EqualTo(4096));
        }

        [Test]
        public void TheBudgetIsAlwaysAPowerOfTwoInRange()
        {
            NatureTextures.Budget = 3000;
            Assert.That(Mathf.IsPowerOfTwo(NatureTextures.Budget), Is.True);

            NatureTextures.Budget = 99999;
            Assert.That(NatureTextures.Budget, Is.EqualTo(NatureTextures.Ultra),
                "a budget above the ceiling has to clamp, not allocate");

            NatureTextures.Budget = 1;
            Assert.That(NatureTextures.Budget, Is.GreaterThanOrEqualTo(256));
        }

        // ---------------------------------------------------------------- the ground

        [Test]
        public void TheBakedGroundIsBuiltAtTheBudgetWithMips()
        {
            NatureTextures.GroundSet set = NatureTextures.Ground(
                LandscapeCatalog.ByName("Alpine Dawn"), 99, WorldExtent, 3.6f, 5.5f);

            Assert.That(set.Resolution, Is.EqualTo(NatureTextures.Budget));
            Assert.That(set.Albedo.width, Is.EqualTo(NatureTextures.Budget));
            Assert.That(set.Normal.width, Is.EqualTo(NatureTextures.Budget));

            // Mips are not optional on this texture. It is seen edge-on across sixty metres,
            // which without a mip chain is the textbook case for aliasing that crawls as the
            // head moves - and crawling detail in a headset is a comfort problem, not a
            // cosmetic one.
            Assert.That(set.Albedo.mipmapCount, Is.GreaterThan(1));
        }

        [Test]
        public void TheBakedGroundHasDetailAtSeveralScales()
        {
            NatureTextures.GroundSet set = NatureTextures.Ground(
                LandscapeCatalog.ByName("Amber Woods"), 5, WorldExtent, 3.6f, 5.5f);

            // Variation between neighbouring texels is the grain; variation between distant
            // texels is the terrain's own colouring. A bake can lose either one on its own -
            // scale the noise wrongly and the grain averages out to a flat tint, get the
            // elevation blend wrong and the whole hillside comes out one colour - so both
            // are measured separately.
            float localSpread = 0f;
            float globalMin = 1f, globalMax = 0f;
            int size = set.Albedo.width;

            for (int i = 0; i < 400; i++)
            {
                int x = (i * 7919) % (size - 2);
                int y = (i * 104729) % (size - 2);

                float a = Luminance(set.Albedo.GetPixel(x, y));
                float b = Luminance(set.Albedo.GetPixel(x + 1, y + 1));

                localSpread = Mathf.Max(localSpread, Mathf.Abs(a - b));
                globalMin = Mathf.Min(globalMin, a);
                globalMax = Mathf.Max(globalMax, a);
            }

            Assert.That(localSpread, Is.GreaterThan(0.01f),
                "no texel-scale grain: the ground will read as a flat painted surface");
            Assert.That(globalMax - globalMin, Is.GreaterThan(0.1f),
                "no large-scale variation: the biome's own terrain is not coming through");
        }

        [Test]
        public void TheBakedGroundIsClampedRatherThanTiled()
        {
            NatureTextures.GroundSet set = NatureTextures.Ground(
                LandscapeCatalog.At(0), 1, WorldExtent, 3.6f, 5.5f);

            // The image is the whole world once, addressed by a planar UV. Repeating it would
            // wrap the far edge of the terrain back onto the near one, and the seam runs
            // right across the middle distance where the eye is already looking.
            Assert.That(set.Albedo.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }

        [Test]
        public void TheSameBiomeAndSeedIsBakedOnlyOnce()
        {
            Landscape landscape = LandscapeCatalog.At(1);

            NatureTextures.GroundSet first = NatureTextures.Ground(
                landscape, 1234, WorldExtent, 3.6f, 5.5f);
            NatureTextures.GroundSet second = NatureTextures.Ground(
                landscape, 1234, WorldExtent, 3.6f, 5.5f);

            Assert.That(second, Is.SameAs(first),
                "the rotation comes back to the same biomes forever; baking a 4K surface "
                + "again each time is the one cost here worth caching away");
        }

        [Test]
        public void ChangingTheBudgetThrowsTheBakedTexturesAway()
        {
            NatureTextures.GroundSet first = NatureTextures.Ground(
                LandscapeCatalog.At(0), 77, WorldExtent, 3.6f, 5.5f);
            Texture2D albedo = first.Albedo;

            NatureTextures.Budget = NatureTextures.Economy;

            // Dropping a preset has to actually free the memory it was holding, or an
            // adaptive step down on a device that is already short of it makes things worse.
            Assert.That(albedo == null, Is.True,
                "the old textures should be destroyed when the budget changes");

            NatureTextures.GroundSet second = NatureTextures.Ground(
                LandscapeCatalog.At(0), 77, WorldExtent, 3.6f, 5.5f);
            Assert.That(second.Resolution, Is.EqualTo(NatureTextures.Economy));
        }

        // ---------------------------------------------------------------- the sky

        [Test]
        public void TheSkyIsBrightAtTheHorizonAndDarkerOverheadAtEveryBearing()
        {
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                Texture2D sky = NatureTextures.Sky(landscape);

                // UV.x is the horizon-to-zenith coordinate, so column zero is the horizon.
                for (int i = 0; i < 6; i++)
                {
                    int row = (int)(sky.height * (i / 6f));
                    float horizon = Luminance(sky.GetPixel(0, row));
                    float zenith = Luminance(sky.GetPixel(sky.width - 1, row));

                    Assert.That(horizon, Is.GreaterThan(zenith),
                        $"{landscape.Name}: an upside-down sky reads as a ceiling, and the "
                        + "bake has to keep the property the palette was chosen for");
                }

                NatureTextures.Flush();
            }
        }

        [Test]
        public void TheSkyLooksDifferentDependingWhichWayYouFace()
        {
            // This is the entire reason the sky is a 2D bake rather than the vertical ramp it
            // used to be. A player in a headset turns around constantly; a sky that varies
            // only with height is the same backdrop in all four directions, and that reads as
            // being inside a dome rather than outdoors.
            Texture2D sky = NatureTextures.Sky(LandscapeCatalog.ByName("Coral Lagoon"));

            float widest = 0f;
            int column = sky.width / 4;
            for (int i = 0; i < 16; i++)
            {
                int rowA = (int)(sky.height * (i / 16f));
                int rowB = (int)(sky.height * ((i + 8) % 16 / 16f));

                widest = Mathf.Max(widest,
                    Mathf.Abs(Luminance(sky.GetPixel(column, rowA))
                              - Luminance(sky.GetPixel(column, rowB))));
            }

            Assert.That(widest, Is.GreaterThan(0.02f),
                "the sky is identical in every direction, so turning round shows nothing new");
        }

        [Test]
        public void TheSkyJoinsUpWithItselfWhereTheCompassWraps()
        {
            Texture2D sky = NatureTextures.Sky(LandscapeCatalog.ByName("Savanna Dusk"));

            // The bearing axis has to repeat, and the colours either side of the wrap have to
            // match, or there is a vertical seam in the sky at one compass heading - at eye
            // height, where it is impossible to miss.
            Assert.That(sky.wrapModeV, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(sky.wrapModeU, Is.EqualTo(TextureWrapMode.Clamp),
                "the dome's skirt below the horizon must keep the haze colour rather than "
                + "wrap round to the zenith");

            for (int i = 0; i < 8; i++)
            {
                int column = (int)(sky.width * (i / 8f));
                float first = Luminance(sky.GetPixel(column, 0));
                float last = Luminance(sky.GetPixel(column, sky.height - 1));

                Assert.That(Mathf.Abs(first - last), Is.LessThan(0.08f),
                    "the two ends of the compass are different colours, which is a seam");
            }
        }

        // ---------------------------------------------------------------- surfaces

        [Test]
        public void TheBladeTextureIsDarkAtTheRootAndBrightAtTheTip()
        {
            // The blade texture shades real blade geometry now rather than cutting a shape
            // out of a card, so what matters is the gradient along it: dark where the clump
            // shadows itself, bright where the tip catches the light. That gradient is free
            // ambient occlusion on a piece of geometry too small to justify any other kind.
            Texture2D blades = NatureTextures.Blades();

            float Row(int y)
            {
                float sum = 0f;
                for (int x = 0; x < blades.width; x += 4) sum += Luminance(blades.GetPixel(x, y));
                return sum / (blades.width / 4);
            }

            float root = Row((int)(blades.height * 0.1f));
            float tip = Row((int)(blades.height * 0.85f));

            Assert.That(tip, Is.GreaterThan(root * 1.15f),
                "a blade with no gradient along it is a flat green stick");
        }

        [Test]
        public void AGrassTuftIsLitGeometryRatherThanATexturedCard()
        {
            // This is the shape that replaced the alpha-cutout quads, and the reason is in
            // the test name: in a player, URP had stripped the alpha-test variant - nothing
            // in the project declares it, because every material here is made at run time -
            // and several hundred tufts per biome drew as solid rectangles. Geometry cannot
            // be stripped.
            Mesh tuft = SceneryArt.BuildGrassTuft(seed: 11, blades: 5);

            Assert.That(tuft.vertexCount, Is.EqualTo(5 * 6),
                "five blades, each with its own vertices front and back");
            Assert.That(tuft.triangles.Length / 3, Is.EqualTo(10));

            foreach (Vector3 normal in tuft.normals)
            {
                Assert.That(normal.magnitude, Is.GreaterThan(0.5f),
                    "a blade with a zero normal renders black, which is the bug this shape "
                    + "exists to end");
            }

            // Standing on its own origin and a unit tall, so localScale reads as a size.
            Bounds bounds = tuft.bounds;
            Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.02f));
            Assert.That(bounds.max.y, Is.InRange(0.5f, 1.3f));

            // Blades lean different ways: a tuft whose blades all point one way is a card
            // again, just a more expensive one.
            bool leansSeveralWays = false;
            foreach (Vector3 vertex in tuft.vertices)
            {
                if (vertex.y > 0.4f && vertex.x < -0.03f) leansSeveralWays = true;
            }

            Assert.That(leansSeveralWays, Is.True);
            Object.DestroyImmediate(tuft);
        }

        [Test]
        public void TheTilingSurfacesRepeatAndHaveGrain()
        {
            foreach (Texture2D surface in new[]
            {
                NatureTextures.Bark(), NatureTextures.Leaves(), NatureTextures.Stone()
            })
            {
                Assert.That(surface.wrapMode, Is.EqualTo(TextureWrapMode.Repeat),
                    $"{surface.name} is tiled over many objects and has to wrap");

                float min = 1f, max = 0f;
                for (int i = 0; i < 200; i++)
                {
                    int x = (i * 7919) % surface.width;
                    int y = (i * 104729) % surface.height;
                    float lum = Luminance(surface.GetPixel(x, y));
                    min = Mathf.Min(min, lum);
                    max = Mathf.Max(max, lum);
                }

                Assert.That(max - min, Is.GreaterThan(0.08f),
                    $"{surface.name} came out flat, so the material is just a tint again");
            }
        }

        // ---------------------------------------------------------------- the noise

        [Test]
        public void TheNoiseIsInRangeSmoothAndRepeatable()
        {
            var a = new NatureTextures.Noise2D(42);
            var b = new NatureTextures.Noise2D(42);
            var other = new NatureTextures.Noise2D(43);

            float previous = a.Value(0f, 0f);
            bool differsFromOtherSeed = false;

            for (int i = 1; i < 500; i++)
            {
                float x = i * 0.05f;
                float value = a.Value(x, 3.7f);

                Assert.That(value, Is.InRange(0f, 1f));
                Assert.That(b.Value(x, 3.7f), Is.EqualTo(value).Within(1e-6f),
                    "the same seed has to give the same field, on every thread and every run");

                // Smoothness, not just range. Hashed noise without interpolation is in range
                // too, and looks like television static.
                Assert.That(Mathf.Abs(value - previous), Is.LessThan(0.35f),
                    "the field jumps between neighbouring samples, so it is not interpolated");

                if (Mathf.Abs(other.Value(x, 3.7f) - value) > 0.05f) differsFromOtherSeed = true;
                previous = value;
            }

            Assert.That(differsFromOtherSeed, Is.True,
                "two seeds produce the same field, so every biome would bake identically");
        }

        [Test]
        public void TheFbmStaysInRangeAtEveryOctaveCount()
        {
            var noise = new NatureTextures.Noise2D(7);

            for (int octaves = 1; octaves <= 8; octaves++)
            {
                for (int i = 0; i < 120; i++)
                {
                    float value = noise.Fbm(i * 0.31f, i * 0.17f, octaves);

                    // An unnormalised sum of octaves drifts above one, which clips to white
                    // over whole regions of the bake.
                    Assert.That(value, Is.InRange(0f, 1f),
                        $"{octaves} octaves left the field outside 0..1");
                }
            }
        }

        // ---------------------------------------------------------------- the terrain mesh

        [Test]
        public void TheTerrainCarriesAPlanarUvForTheBakedGroundAndKeepsTheShadingOne()
        {
            Landscape landscape = LandscapeCatalog.At(0);
            Mesh mesh = SceneryArt.BuildTerrain(landscape, 3, WorldExtent, 32);

            float minU = 1f, maxU = 0f, minV = 1f, maxV = 0f;
            foreach (Vector2 uv in mesh.uv)
            {
                minU = Mathf.Min(minU, uv.x);
                maxU = Mathf.Max(maxU, uv.x);
                minV = Mathf.Min(minV, uv.y);
                maxV = Mathf.Max(maxV, uv.y);
            }

            // The whole world square maps onto the whole image exactly once.
            Assert.That(minU, Is.EqualTo(0f).Within(0.001f));
            Assert.That(maxU, Is.EqualTo(1f).Within(0.001f));
            Assert.That(minV, Is.EqualTo(0f).Within(0.001f));
            Assert.That(maxV, Is.EqualTo(1f).Within(0.001f));

            // The old height-and-slope coordinate is still there on the second channel, so a
            // plain colour ramp remains a working fallback for anything that wants one.
            var shade = new System.Collections.Generic.List<Vector2>();
            mesh.GetUVs(1, shade);
            Assert.That(shade.Count, Is.EqualTo(mesh.vertexCount));

            Assert.That(mesh.tangents.Length, Is.EqualTo(mesh.vertexCount),
                "the ground wears a normal map, which needs a tangent frame");
        }

        [Test]
        public void TheTerrainResolutionFallsWithTheQualityPreset()
        {
            int crisp = LandscapeBuilder.TerrainResolutionFor(QualityTuner.Preset.Crisp);
            int balanced = LandscapeBuilder.TerrainResolutionFor(QualityTuner.Preset.Balanced);
            int fast = LandscapeBuilder.TerrainResolutionFor(QualityTuner.Preset.Fast);

            Assert.That(crisp, Is.GreaterThan(balanced));
            Assert.That(balanced, Is.GreaterThan(fast));

            // A vertex every two metres is what the ground used to be built at, and it is not
            // enough to carry a surface: the baked grain sits on facets whose edges you can
            // see, and a ridge against the sky is visibly straight between samples.
            Assert.That(fast, Is.GreaterThan(56),
                "even the cheapest preset should beat the flat-colour-era terrain grid");
        }

        [Test]
        public void TheTrunkAndCrownMeshesCarryUvsThatCanHoldATexture()
        {
            Mesh trunk = SceneryArt.BuildPrism(6, 0.5f, 2f);
            Mesh crown = SceneryArt.BuildBlob();

            // Both of these used to park every UV at the texture centre, which is harmless
            // when nothing is textured and means bark and leaf textures come out as a single
            // flat colour the moment something is.
            foreach (Mesh mesh in new[] { trunk, crown })
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (Vector2 uv in mesh.uv)
                {
                    min = Mathf.Min(min, uv.x);
                    max = Mathf.Max(max, uv.x);
                }

                Assert.That(max - min, Is.GreaterThan(0.2f),
                    $"{mesh.name} has no UV spread, so a texture on it is a flat tint");
            }

            Object.DestroyImmediate(trunk);
            Object.DestroyImmediate(crown);
        }
    }
}
