using NUnit.Framework;
using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Tests
{
    /// <summary>
    /// Guards on the tuning values themselves. <see cref="GameConfig"/> is edited often while
    /// chasing feel, and some of its numbers are only meaningful in relation to each other -
    /// a pinch release gap below the grab gap, for instance, would make grabs flicker on and
    /// off. These tests catch that class of mistake the moment it is typed, long before a
    /// headset is on.
    /// </summary>
    public class ConfigTests
    {
        [Test]
        public void PinchHysteresisReleasesWiderThanItGrabs()
        {
            Assert.That(GameConfig.PinchExitDistance,
                Is.GreaterThan(GameConfig.PinchEnterDistance),
                "the release gap must be the wider one or grabs will chatter");
        }

        [Test]
        public void LaunchSpeedRangeIsOrdered()
        {
            Assert.That(GameConfig.MinLaunchSpeed, Is.GreaterThan(0f));
            Assert.That(GameConfig.MaxLaunchSpeed, Is.GreaterThan(GameConfig.MinLaunchSpeed));
        }

        [Test]
        public void TheRinkIsLongerThanItIsWide()
        {
            Assert.That(GameConfig.RinkLength, Is.GreaterThan(GameConfig.RinkWidth));
        }

        [Test]
        public void TheHouseFitsInsideTheRink()
        {
            float outer = GameConfig.RingRadii[0];
            Assert.That(outer * 2f, Is.LessThan(GameConfig.RinkWidth),
                "the outer ring must not run into the side walls");
            Assert.That(GameConfig.TargetFromFarEnd, Is.GreaterThan(outer),
                "the house must sit clear of the far wall");
        }

        [Test]
        public void TheLaunchZoneDoesNotOverlapTheHouse()
        {
            float launchZ = -GameConfig.RinkLength * 0.5f + GameConfig.LaunchLineFromNearEnd;
            float houseZ = GameConfig.RinkLength * 0.5f - GameConfig.TargetFromFarEnd;
            float gap = houseZ - launchZ - GameConfig.RingRadii[0];

            Assert.That(gap, Is.GreaterThan(0.2f),
                "there must be real ice between the launch line and the house");
        }

        [Test]
        public void StonesFitSideBySideAcrossTheRink()
        {
            // Three stones abreast is the minimum for guards to be meaningful: if the rink
            // were narrower, a single stone would wall off the house entirely.
            float needed = GameConfig.StoneRadius * 2f * 3f;
            Assert.That(GameConfig.RinkWidth, Is.GreaterThan(needed));
        }

        [Test]
        public void MatchLengthIsAWinnableBestOfThree()
        {
            Assert.That(GameConfig.MaxRounds, Is.GreaterThanOrEqualTo(GameConfig.RoundsToWinMatch));
            Assert.That(GameConfig.RoundsToWinMatch * 2 - 1,
                Is.LessThanOrEqualTo(GameConfig.MaxRounds),
                "there must be enough rounds for either side to reach the target");
        }

        [Test]
        public void FrictionStopsAStoneBeforeItLeavesTheRink()
        {
            // A full-power throw must run out of ice somewhere on the sheet; otherwise every
            // hard shot ends in the back wall and power stops being a choice.
            float stoppingDistance = GameConfig.MaxLaunchSpeed * GameConfig.MaxLaunchSpeed
                                     / (2f * GameConfig.IceFriction);
            Assert.That(stoppingDistance, Is.GreaterThan(GameConfig.RinkLength),
                "full power should still be able to reach the back of the rink");
            Assert.That(GameConfig.IceFriction, Is.GreaterThan(0f));
        }

        [Test]
        public void SettleThresholdsAreSaneForTheFrictionModel()
        {
            float speedLostPerHold = GameConfig.IceFriction * GameConfig.SettleHoldTime;
            Assert.That(speedLostPerHold, Is.GreaterThan(GameConfig.SettleSpeed),
                "friction must be able to cross the settle threshold inside the hold window, " +
                "or stones would hover just above it");
        }

        [Test]
        public void AFullPullStaysOnTheIce()
        {
            // The pull happens behind the stone, on the rink. If the launch line sits closer
            // to the near edge than the longest pull, a hard throw drags the stone off the
            // open end, where it is counted out of bounds and destroyed on release - so the
            // harder you threw, the more certainly your stone vanished.
            Assert.That(GameConfig.LaunchLineFromNearEnd,
                Is.GreaterThan(GameConfig.MaxPullDistance + GameConfig.StoneRadius),
                "a full pull must not reach past the near edge of the rink");
        }

        [Test]
        public void PullDistanceIsWithinArmsReach()
        {
            Assert.That(GameConfig.MaxPullDistance, Is.LessThan(0.6f),
                "a seated player must be able to complete the full pull without leaning");
            Assert.That(GameConfig.MaxPullDistance, Is.GreaterThan(GameConfig.GrabRadius));
        }

        [Test]
        public void TeamColoursAreDistinguishable()
        {
            Color one = PlayerSide.One.ToColor();
            Color two = PlayerSide.Two.ToColor();
            float separation = Mathf.Abs(one.r - two.r) + Mathf.Abs(one.g - two.g)
                             + Mathf.Abs(one.b - two.b);

            Assert.That(separation, Is.GreaterThan(0.75f),
                "the two sides must be tellable apart at a glance");
        }

        [Test]
        public void SidesAreEachOthersOpposite()
        {
            Assert.AreEqual(PlayerSide.Two, PlayerSide.One.Other());
            Assert.AreEqual(PlayerSide.One, PlayerSide.Two.Other());
            Assert.AreNotEqual(PlayerSide.One.Label(), PlayerSide.Two.Label());
        }
    }
}
