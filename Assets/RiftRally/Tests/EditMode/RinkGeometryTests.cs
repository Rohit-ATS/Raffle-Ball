using NUnit.Framework;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;

namespace RiftRally.Tests
{
    /// <summary>
    /// The rink is the coordinate system the whole game is expressed in, so these tests pin
    /// down the conversions and the scoring geometry. A silent change here would move the
    /// target out from under every shot the player has learned.
    /// </summary>
    public class RinkGeometryTests
    {
        private GameObject _root;
        private Rink _rink;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("RinkTest");
            var surface = new GameObject("Surface").transform;
            surface.SetParent(_root.transform, false);
            surface.localPosition = new Vector3(0f, GameConfig.RinkHeight, 0f);

            _rink = _root.AddComponent<Rink>();
            _rink.Surface = surface;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void SurfaceSitsAtTableHeight()
        {
            Assert.That(_rink.SurfaceY, Is.EqualTo(GameConfig.RinkHeight).Within(1e-5f));
            Assert.That(_rink.StoneCenterY,
                Is.EqualTo(GameConfig.RinkHeight + GameConfig.StoneHeight * 0.5f).Within(1e-5f));
        }

        [Test]
        public void RinkAndWorldConversionsRoundTrip()
        {
            var local = new Vector3(0.21f, 0f, -0.44f);
            Vector3 world = _rink.RinkToWorld(local.x, local.z);
            Vector3 back = _rink.WorldToRink(world);

            Assert.That(back.x, Is.EqualTo(local.x).Within(1e-4f));
            Assert.That(back.z, Is.EqualTo(local.z).Within(1e-4f));
        }

        [Test]
        public void ConversionsSurviveTheRinkBeingMovedAndTurned()
        {
            // Passthrough placement drops the rink anywhere in the room at any angle, so the
            // conversions must be fully relative.
            _root.transform.position = new Vector3(3.1f, 0.2f, -7.4f);
            _root.transform.rotation = Quaternion.Euler(0f, 57f, 0f);

            Vector3 world = _rink.RinkToWorld(0.1f, 0.5f);
            Vector3 back = _rink.WorldToRink(world);

            Assert.That(back.x, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(back.z, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void TargetSitsTheConfiguredDistanceFromTheFarEnd()
        {
            Vector3 local = _rink.WorldToRink(_rink.TargetCenterWorld);
            float expected = GameConfig.RinkLength * 0.5f - GameConfig.TargetFromFarEnd;
            Assert.That(local.z, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(local.x, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void LaunchPointsSitBehindTheLaunchLineAndOppositeEachOther()
        {
            Vector3 one = _rink.WorldToRink(_rink.LaunchPointWorld(PlayerSide.One));
            Vector3 two = _rink.WorldToRink(_rink.LaunchPointWorld(PlayerSide.Two));

            float expectedZ = -GameConfig.RinkLength * 0.5f + GameConfig.LaunchLineFromNearEnd;
            Assert.That(one.z, Is.EqualTo(expectedZ).Within(1e-4f));
            Assert.That(two.z, Is.EqualTo(expectedZ).Within(1e-4f));
            Assert.That(one.x, Is.LessThan(0f));
            Assert.That(two.x, Is.GreaterThan(0f));
            Assert.That(Mathf.Abs(one.x), Is.EqualTo(Mathf.Abs(two.x)).Within(1e-4f));
        }

        [Test]
        public void RingValuesFallOffFromTheCentre()
        {
            var radii = GameConfig.RingRadii;

            Assert.AreEqual(3, _rink.RingValueFor(0f), "dead centre is the bullseye");
            Assert.AreEqual(3, _rink.RingValueFor(radii[2] - 0.001f));
            Assert.AreEqual(2, _rink.RingValueFor(radii[1] - 0.001f));
            Assert.AreEqual(1, _rink.RingValueFor(radii[0] - 0.001f));
            Assert.AreEqual(0, _rink.RingValueFor(radii[0] + 0.001f), "outside the house");
        }

        [Test]
        public void RingRadiiShrinkTowardsTheCentre()
        {
            var radii = GameConfig.RingRadii;
            for (int i = 1; i < radii.Length; i++)
                Assert.That(radii[i], Is.LessThan(radii[i - 1]),
                    "ring radii must be listed outermost first");
        }

        [Test]
        public void BoundsAcceptTheSurfaceAndRejectWhatLeavesIt()
        {
            Assert.IsTrue(_rink.IsInsideBounds(_rink.RinkToWorld(0f, 0f)));
            Assert.IsTrue(_rink.IsInsideBounds(_rink.TargetCenterWorld));

            float outX = GameConfig.RinkWidth * 0.5f + GameConfig.StoneRadius + 0.02f;
            float outZ = GameConfig.RinkLength * 0.5f + GameConfig.StoneRadius + 0.02f;

            Assert.IsFalse(_rink.IsInsideBounds(_rink.RinkToWorld(outX, 0f)));
            Assert.IsFalse(_rink.IsInsideBounds(_rink.RinkToWorld(0f, outZ)));
            Assert.IsFalse(_rink.IsInsideBounds(_rink.RinkToWorld(0f, -outZ)));
        }

        [Test]
        public void TargetIsReachableFromTheLaunchPoint()
        {
            // If the house were further away than the fastest legal throw can carry, the game
            // would be unplayable. v^2 = 2*a*d under the stone's constant-deceleration model.
            Vector3 launch = _rink.LaunchPointWorld(PlayerSide.One);
            float distance = _rink.DistanceToTarget(launch);
            float reach = GameConfig.MaxLaunchSpeed * GameConfig.MaxLaunchSpeed
                          / (2f * GameConfig.IceFriction);

            Assert.That(reach, Is.GreaterThan(distance * 1.2f),
                "a maximum-power throw must comfortably reach the house");

            float minReach = GameConfig.MinLaunchSpeed * GameConfig.MinLaunchSpeed
                             / (2f * GameConfig.IceFriction);
            Assert.That(minReach, Is.LessThan(distance),
                "a minimum-power throw must fall short, so power is a real decision");
        }
    }
}
