using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RiftRally.Core;
using RiftRally.Scenery;

namespace RiftRally.Tests
{
    /// <summary>
    /// Builds real landscapes and checks the things that can only be checked once they are
    /// standing: that the world actually gets built, that it moves, that swapping it leaves
    /// nothing behind, and that the interface palette follows the biome.
    ///
    /// The leak test is the one that matters most. The landscape is now rebuilt between every
    /// game and can be stepped through by hand from the options dock, so a build that forgets
    /// to destroy its predecessor does not fail - it accumulates. Ten games in, the player is
    /// standing in ten overlapping worlds and the frame rate has quietly halved.
    /// </summary>
    public class SceneryTests
    {
        private GameObject _host;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            LandscapeRotation.Forget();

            // The sky and the weather station themselves on the main camera, so there has to
            // be one for a build to be representative.
            var camGo = new GameObject("TestCamera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 1.3f, 0f);
            _camera = camGo.AddComponent<Camera>();
            _camera.farClipPlane = 50f;

            _host = new GameObject("SceneryHost");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_camera != null) Object.DestroyImmediate(_camera.gameObject);
            LandscapeRotation.Forget();
            Theme.Reset();
        }

        [UnityTest]
        public IEnumerator ABuiltLandscapeHasGroundSkyPlantingAndBirds()
        {
            Landscape landscape = LandscapeCatalog.ByName("Amber Woods");
            LandscapeBuilder.Built built = LandscapeBuilder.Build(_host.transform, landscape, 42);
            yield return null;

            Assert.That(built.Root, Is.Not.Null);
            Assert.That(built.Sky, Is.Not.Null, "without a sky the player is in a void again");
            Assert.That(built.Root.Find("Ground"), Is.Not.Null);
            Assert.That(built.Root.Find("Ridges"), Is.Not.Null,
                "the ridge line is what gives the world a horizon rather than an edge");
            Assert.That(built.PlantedCount, Is.GreaterThan(20));
            Assert.That(built.Flocks.Length, Is.EqualTo(landscape.FlockCount));
            Assert.That(built.Sun, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator TheBirdsFlyAndBeatTheirWings()
        {
            Landscape landscape = LandscapeCatalog.ByName("Highland Moor");
            LandscapeBuilder.Built built = LandscapeBuilder.Build(_host.transform, landscape, 7);
            yield return null;

            BirdFlock flock = built.Flocks[0];
            Transform bird = flock.transform.GetChild(0);
            Transform wing = bird.Find("WingL");

            Assert.That(wing, Is.Not.Null, "a bird with no wings cannot flap");

            Vector3 startPosition = bird.position;
            Quaternion startWing = wing.localRotation;

            // Long enough for the slowest wingbeat in the game to have moved.
            for (int i = 0; i < 40; i++) yield return null;

            Assert.That(Vector3.Distance(bird.position, startPosition),
                Is.GreaterThan(0.05f), "the flock is not going anywhere");
            Assert.That(Quaternion.Angle(startWing, wing.localRotation),
                Is.GreaterThan(0.5f), "the wings are not moving");
        }

        [UnityTest]
        public IEnumerator BirdsStayAboveTheGround()
        {
            // Skimming flocks fly deliberately low, which is exactly the case that can put a
            // bird through a hillside.
            Landscape landscape = LandscapeCatalog.ByName("Coral Lagoon");
            LandscapeBuilder.Built built = LandscapeBuilder.Build(_host.transform, landscape, 3);
            yield return null;

            for (int frame = 0; frame < 60; frame++)
            {
                foreach (BirdFlock flock in built.Flocks)
                {
                    foreach (Transform bird in flock.transform)
                    {
                        Assert.That(bird.position.y, Is.GreaterThan(landscape.WaterLevel),
                            "a bird has gone under the water");
                    }
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CloudsDrift()
        {
            Landscape landscape = LandscapeCatalog.ByName("Blossom Valley");
            LandscapeBuilder.Built built = LandscapeBuilder.Build(_host.transform, landscape, 11);
            yield return null;

            Transform clouds = built.Root.Find("Clouds");
            Assert.That(clouds, Is.Not.Null);

            Transform cloud = clouds.GetChild(0);
            Vector3 start = cloud.position;

            for (int i = 0; i < 40; i++) yield return null;

            Assert.That(Vector3.Distance(cloud.position, start), Is.GreaterThan(0.0005f),
                "the sky is completely static");
        }

        [UnityTest]
        public IEnumerator BuildingInstallsTheBiomeInterfacePalette()
        {
            Landscape landscape = LandscapeCatalog.ByName("Red Mesa");
            LandscapeBuilder.Build(_host.transform, landscape, 5);
            yield return null;

            Assert.That(Theme.Primary, Is.EqualTo(landscape.Ui.Primary),
                "the HUD has to pick up the palette the biome was designed with");
            Assert.That(Theme.Panel, Is.EqualTo(landscape.Ui.Panel));
        }

        [UnityTest]
        public IEnumerator BuildingRaisesTheCameraFarPlaneToFitTheWorld()
        {
            // The game shipped with a 50 m far plane, which is correct for a table and clips
            // away every hill and the entire sky.
            LandscapeBuilder.Build(_host.transform, LandscapeCatalog.At(0), 1);
            yield return null;

            Assert.That(_camera.farClipPlane, Is.GreaterThan(150f));
        }

        [UnityTest]
        public IEnumerator SwappingLandscapesLeavesOnlyOneWorldStanding()
        {
            var director = _host.AddComponent<LandscapeDirector>();
            director.SetHost(_host.transform);

            director.BuildCurrent();
            yield return null;

            string first = director.CurrentName;

            for (int i = 0; i < 3; i++)
            {
                director.Next();
                // Destroy is deferred to the end of the frame, so the old world is only
                // actually gone after one.
                yield return null;
            }

            Assert.That(director.CurrentName, Is.Not.EqualTo(first));

            int worlds = 0;
            foreach (Transform child in _host.transform)
                if (child.name.StartsWith("Landscape_")) worlds++;

            Assert.That(worlds, Is.EqualTo(1),
                "each rebuild must take its predecessor with it");
        }

        [UnityTest]
        public IEnumerator EveryLandscapeInTheCatalogueBuildsWithoutError()
        {
            // Cheap but valuable: a biome is only seen once the rotation reaches it, so a
            // typo in one of eight literals could sit unnoticed for a long time.
            var director = _host.AddComponent<LandscapeDirector>();
            director.SetHost(_host.transform);
            director.Density = 0.25f;          // keep the test quick

            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                director.Build(landscape);
                yield return null;

                Assert.That(director.Current, Is.SameAs(landscape));
                Assert.That(director.BuiltWorld.PlantedCount, Is.GreaterThan(0),
                    $"{landscape.Name} grew nothing at all");
            }
        }

        [UnityTest]
        public IEnumerator DisablingTheSceneryPutsTheOriginalPaletteBack()
        {
            var director = _host.AddComponent<LandscapeDirector>();
            director.SetHost(_host.transform);
            director.BuildCurrent();
            yield return null;

            director.Disable();
            yield return null;

            Assert.That(Theme.Primary, Is.EqualTo(Theme.Default().Primary));
            Assert.That(director.Current, Is.Null);
        }
    }
}
