using NUnit.Framework;
using UnityEngine;
using RiftRally.Scenery;

namespace RiftRally.Tests
{
    /// <summary>
    /// Guards on the landscape catalogue and on the maths the world is generated from.
    ///
    /// These are worth having because the catalogue is eight hand-written colour schemes and
    /// the failure mode is not a crash. A biome with text the same brightness as its own
    /// panel, or a lagoon whose water sits above every hill, builds perfectly and looks
    /// broken only once somebody happens to roll onto it - which, with eight environments in
    /// rotation, might be a fortnight after the mistake was made.
    /// </summary>
    public class LandscapeTests
    {
        private static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        [Test]
        public void TheCatalogueHasSeveralLandscapes()
        {
            Assert.That(LandscapeCatalog.Count, Is.GreaterThanOrEqualTo(6),
                "the rotation needs enough biomes that one does not come round too often");
        }

        [Test]
        public void EveryLandscapeIsNamedAndDescribed()
        {
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                Assert.That(landscape.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(landscape.Tagline, Is.Not.Null.And.Not.Empty,
                    $"{landscape.Name} has no tagline, so the HUD has nothing to announce");
            }
        }

        [Test]
        public void LandscapeNamesAreUnique()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                Assert.That(seen.Add(landscape.Name), Is.True,
                    $"'{landscape.Name}' appears twice, so lookup by name is ambiguous");
            }
        }

        [Test]
        public void EveryPaletteKeepsTextLegibleAgainstItsPanel()
        {
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                float text = Luminance(landscape.Ui.Text);
                float panel = Luminance(landscape.Ui.Panel);

                Assert.That(Mathf.Abs(text - panel), Is.GreaterThan(0.35f),
                    $"{landscape.Name}: body text and its own panel are too close in "
                    + "brightness to read");

                Assert.That(landscape.Ui.Panel.a, Is.GreaterThan(0.6f),
                    $"{landscape.Name}: the panel is too transparent to hold text up "
                    + "against a bright sky");
            }
        }

        [Test]
        public void EveryPaletteSeparatesItsPrimaryFromItsBodyText()
        {
            // A title that is the same colour as the line under it stops being a title.
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                Color a = landscape.Ui.Primary;
                Color b = landscape.Ui.Text;
                float distance = Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g)
                                 + Mathf.Abs(a.b - b.b);

                Assert.That(distance, Is.GreaterThan(0.18f),
                    $"{landscape.Name}: the primary colour is indistinguishable from "
                    + "body text");
            }
        }

        [Test]
        public void SkiesAreBrighterAtTheHorizonThanOverhead()
        {
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                Assert.That(Luminance(landscape.SkyHorizon),
                    Is.GreaterThan(Luminance(landscape.SkyZenith)),
                    $"{landscape.Name}: an upside-down sky reads as a ceiling, not a sky");
            }
        }

        [Test]
        public void FloweringBiomesHaveABloomTheirOwnGroundCoverDoesNot()
        {
            // Flowers are the one piece of planting whose job is colour. A flower colour that
            // matches the grass it stands in is a few hundred draw calls spent on nothing,
            // and it is the easy mistake to make when filling in a new biome by eye.
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                if (landscape.FlowerCount <= 0) continue;

                float distance = Mathf.Abs(landscape.FlowerColor.r - landscape.GrassColor.r)
                               + Mathf.Abs(landscape.FlowerColor.g - landscape.GrassColor.g)
                               + Mathf.Abs(landscape.FlowerColor.b - landscape.GrassColor.b);

                Assert.That(distance, Is.GreaterThan(0.25f),
                    $"{landscape.Name}: the flowers are the same colour as the grass");
            }
        }

        [Test]
        public void OnlyWaterBiomesAskForReeds()
        {
            // Reeds are placed by searching for ground near the waterline. In a biome with no
            // water there is no waterline, so the count is not merely ignored - it is a sign
            // that whoever filled the entry in was copying rather than describing a place.
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                if (landscape.ReedCount <= 0) continue;

                Assert.That(landscape.HasWater, Is.True,
                    $"{landscape.Name} grows reeds but has no water for them to stand in");
            }
        }

        [Test]
        public void EveryBiomeHasSomethingBetweenItsGrassAndItsTrees()
        {
            // The undergrowth layer is what stops a landscape reading as trees standing in a
            // lawn. A biome with none of it builds perfectly well and looks like a diagram.
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                Assert.That(landscape.BushCount + landscape.FlowerCount, Is.GreaterThan(0),
                    $"{landscape.Name} has no scrub and no bloom, so the middle distance is "
                    + "bare ground between the tufts and the treeline");
            }
        }

        [Test]
        public void WaterBiomesHaveGroundBelowTheirWaterline()
        {
            // If the lowest the ground can go is still above the water plane, the water is
            // buried and the biome has no lake in it at all.
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                if (!landscape.HasWater) continue;

                float lowest = -SceneryArt.GroundBaseline * landscape.HillHeight;
                Assert.That(lowest, Is.LessThan(landscape.WaterLevel),
                    $"{landscape.Name}: the water plane sits above every hollow in the ground");
            }
        }

        [Test]
        public void IndexingWrapsInBothDirections()
        {
            int count = LandscapeCatalog.Count;

            Assert.That(LandscapeCatalog.At(count), Is.SameAs(LandscapeCatalog.At(0)));
            Assert.That(LandscapeCatalog.At(-1), Is.SameAs(LandscapeCatalog.At(count - 1)));
            Assert.That(LandscapeCatalog.At(-count - 1),
                        Is.SameAs(LandscapeCatalog.At(count - 1)));
        }

        [Test]
        public void LookupByNameIgnoresCaseAndRejectsNonsense()
        {
            string name = LandscapeCatalog.At(0).Name;

            Assert.That(LandscapeCatalog.ByName(name.ToLowerInvariant()), Is.Not.Null);
            Assert.That(LandscapeCatalog.ByName("Atlantis"), Is.Null);
            Assert.That(LandscapeCatalog.IndexOf(null), Is.EqualTo(-1));
        }

        // ---------------------------------------------------------------- rotation

        [SetUp]
        public void ClearStoredRotation() => LandscapeRotation.Forget();

        [TearDown]
        public void ClearStoredRotationAfter() => LandscapeRotation.Forget();

        [Test]
        public void TheRotationVisitsEveryLandscapeBeforeRepeating()
        {
            // This is the whole reason the rotation exists rather than a random pick: a
            // random choice out of eight shows the same biome twice running about one game in
            // eight, which reads as the feature being broken.
            int count = LandscapeCatalog.Count;
            var seen = new System.Collections.Generic.HashSet<string>();

            string start = LandscapeRotation.Current.Name;
            seen.Add(start);
            for (int i = 1; i < count; i++) seen.Add(LandscapeRotation.Advance().Name);

            Assert.That(seen.Count, Is.EqualTo(count),
                "a full cycle must show every landscape exactly once");

            Assert.That(LandscapeRotation.Advance().Name, Is.EqualTo(start),
                "and then it comes back round to where it began");
        }

        [Test]
        public void SteppingForwardAndBackReturnsToWhereItStarted()
        {
            string start = LandscapeRotation.Current.Name;

            LandscapeRotation.Next();
            LandscapeRotation.Previous();

            Assert.That(LandscapeRotation.Current.Name, Is.EqualTo(start));
        }

        [Test]
        public void TheIndexStaysInRangeHoweverFarItIsPushed()
        {
            LandscapeRotation.CurrentIndex = 10_000;
            Assert.That(LandscapeRotation.CurrentIndex,
                Is.InRange(0, LandscapeCatalog.Count - 1));

            LandscapeRotation.CurrentIndex = -10_000;
            Assert.That(LandscapeRotation.CurrentIndex,
                Is.InRange(0, LandscapeCatalog.Count - 1));
        }

        [Test]
        public void SelectingAnUnknownNameChangesNothing()
        {
            string start = LandscapeRotation.Current.Name;

            Assert.That(LandscapeRotation.Select("Nowhere"), Is.False);
            Assert.That(LandscapeRotation.Current.Name, Is.EqualTo(start),
                "a stale name in a saved preference must not disturb the rotation");
        }

        [Test]
        public void TurningOffAutoRotationHoldsTheLandscape()
        {
            LandscapeRotation.AutoRotate = false;
            string start = LandscapeRotation.Current.Name;

            LandscapeRotation.AdvanceForNewGame();
            LandscapeRotation.AdvanceForNewGame();

            Assert.That(LandscapeRotation.Current.Name, Is.EqualTo(start));

            LandscapeRotation.AutoRotate = true;
            Assert.That(LandscapeRotation.AdvanceForNewGame().Name, Is.Not.EqualTo(start));
        }

        // ---------------------------------------------------------------- generation

        [Test]
        public void TheGroundIsFlatUnderTheTableAndNotFlatFurtherOut()
        {
            Landscape landscape = LandscapeCatalog.ByName("Alpine Dawn");
            var noise = new SceneryArt.TerrainNoise(1234, landscape.HillRoughness);

            // The table stands on this, so any slope at all under it would tilt the rink.
            Assert.That(Mathf.Abs(SceneryArt.HeightAt(noise, landscape, 0f, 0f)),
                Is.LessThan(0.001f));
            Assert.That(Mathf.Abs(SceneryArt.HeightAt(noise, landscape, 1.5f, 0.5f)),
                Is.LessThan(0.001f));

            // ...and out past the falloff there had better be hills.
            float relief = 0f;
            for (int i = 0; i < 40; i++)
            {
                float angle = i / 40f * Mathf.PI * 2f;
                float h = SceneryArt.HeightAt(noise, landscape,
                    Mathf.Cos(angle) * 40f, Mathf.Sin(angle) * 40f);
                relief = Mathf.Max(relief, Mathf.Abs(h));
            }

            Assert.That(relief, Is.GreaterThan(1f), "the distance is suspiciously flat");
        }

        [Test]
        public void TheSameSeedBuildsTheSameGround()
        {
            Landscape landscape = LandscapeCatalog.At(0);
            var a = new SceneryArt.TerrainNoise(77, landscape.HillRoughness);
            var b = new SceneryArt.TerrainNoise(77, landscape.HillRoughness);

            Assert.That(SceneryArt.HeightAt(b, landscape, 19f, -23f),
                Is.EqualTo(SceneryArt.HeightAt(a, landscape, 19f, -23f)).Within(1e-5f),
                "planting is placed by asking this function where the ground is, so it has "
                + "to agree with the mesh built from it");
        }

        [Test]
        public void DifferentSeedsBuildDifferentGround()
        {
            Landscape landscape = LandscapeCatalog.At(0);
            var a = new SceneryArt.TerrainNoise(1, landscape.HillRoughness);
            var b = new SceneryArt.TerrainNoise(2, landscape.HillRoughness);

            bool differs = false;
            for (int i = 0; i < 20 && !differs; i++)
            {
                float x = 12f + i * 2.3f;
                differs = Mathf.Abs(SceneryArt.HeightAt(a, landscape, x, 17f)
                                    - SceneryArt.HeightAt(b, landscape, x, 17f)) > 0.05f;
            }

            Assert.That(differs, Is.True,
                "the same biome should be laid out differently each time it comes round");
        }

        [Test]
        public void ScatteringSpreadsEvenlyByAreaRatherThanByRadius()
        {
            // Interpolating the radius linearly crowds everything into the inner ring. Taking
            // the square root distributes by area, so roughly half the points should land
            // outside the mid-radius of an annulus.
            var random = new System.Random(5);
            const float inner = 4f, outer = 40f;
            float mid = Mathf.Sqrt((inner * inner + outer * outer) * 0.5f);

            int beyondMid = 0;
            const int samples = 2000;
            for (int i = 0; i < samples; i++)
            {
                Vector2 point = SceneryArt.ScatterPoint(random, inner, outer);
                float radius = point.magnitude;

                Assert.That(radius, Is.InRange(inner - 0.01f, outer + 0.01f));
                if (radius > mid) beyondMid++;
            }

            Assert.That(beyondMid / (float)samples, Is.EqualTo(0.5f).Within(0.06f));
        }

        [Test]
        public void TheBlobIsCheapAndRoundAndUnitSized()
        {
            Mesh blob = SceneryArt.BuildBlob();

            Assert.That(blob.triangles.Length / 3, Is.EqualTo(32),
                "an octahedron subdivided once; if this grows, so does every tree crown "
                + "in the game");

            foreach (Vector3 vertex in blob.vertices)
            {
                Assert.That(vertex.magnitude, Is.EqualTo(0.5f).Within(0.001f),
                    "the blob has to be a unit diameter, so localScale reads as a size");
            }
        }

        [Test]
        public void ThePrismStandsOnTheGroundAndIsClosedOnTop()
        {
            const float height = 3f;
            Mesh prism = SceneryArt.BuildPrism(6, 0.5f, height);

            float lowest = float.MaxValue, highest = float.MinValue;
            foreach (Vector3 vertex in prism.vertices)
            {
                lowest = Mathf.Min(lowest, vertex.y);
                highest = Mathf.Max(highest, vertex.y);
            }

            Assert.That(lowest, Is.EqualTo(0f).Within(0.001f),
                "a trunk is placed at ground height, so its base must be at its own origin");
            Assert.That(highest, Is.EqualTo(height).Within(0.001f));

            // Sides plus a top cap: 6 * 2 + 6.
            Assert.That(prism.triangles.Length / 3, Is.EqualTo(18));
        }

        [Test]
        public void TheSkyDomeRampCoordinateCoversHorizonToZenith()
        {
            Mesh dome = SceneryArt.BuildSkyDome(100f, 16, 8);

            float lowest = float.MaxValue, highest = float.MinValue;
            foreach (Vector2 uv in dome.uv)
            {
                lowest = Mathf.Min(lowest, uv.x);
                highest = Mathf.Max(highest, uv.x);
            }

            Assert.That(lowest, Is.EqualTo(0f).Within(0.001f),
                "the horizon has to sample the bottom of the sky ramp");
            Assert.That(highest, Is.EqualTo(1f).Within(0.001f),
                "the zenith has to sample the top of it");
        }

        [Test]
        public void ARampInterpolatesBetweenItsStops()
        {
            Texture2D ramp = Setup.ProceduralArt.MakeRamp(Color.black, Color.white);

            Color first = ramp.GetPixel(0, 0);
            Color last = ramp.GetPixel(ramp.width - 1, 0);
            Color middle = ramp.GetPixel(ramp.width / 2, 0);

            Assert.That(first.r, Is.EqualTo(0f).Within(0.02f));
            Assert.That(last.r, Is.EqualTo(1f).Within(0.02f));
            Assert.That(middle.r, Is.EqualTo(0.5f).Within(0.05f));

            Object.DestroyImmediate(ramp);
        }

        [Test]
        public void ARampOfOneColourIsThatColour()
        {
            Texture2D ramp = Setup.ProceduralArt.MakeRamp(Color.green);

            Assert.That(ramp.GetPixel(0, 0).g, Is.EqualTo(1f).Within(0.02f));
            Assert.That(ramp.GetPixel(ramp.width - 1, 0).g, Is.EqualTo(1f).Within(0.02f));

            Object.DestroyImmediate(ramp);
        }

        [Test]
        public void PlantingDensityFallsWithTheQualityPreset()
        {
            float crisp = LandscapeBuilder.DensityFor(Setup.QualityTuner.Preset.Crisp);
            float balanced = LandscapeBuilder.DensityFor(Setup.QualityTuner.Preset.Balanced);
            float fast = LandscapeBuilder.DensityFor(Setup.QualityTuner.Preset.Fast);

            Assert.That(crisp, Is.GreaterThan(balanced));
            Assert.That(balanced, Is.GreaterThan(fast));
            Assert.That(fast, Is.GreaterThan(0f), "even the cheapest preset keeps a world");
        }
    }
}
