using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;

namespace RiftRally.Tests
{
    /// <summary>
    /// Scoring is the rule that makes the game tactical, so it is pinned down case by case:
    /// only the side holding the closest stone scores, and it scores one point per stone of
    /// its own that is closer than the other side's best. Everything about how the game plays
    /// - takeouts, guards, the value of the hammer - follows from that.
    /// </summary>
    public class ScoringTests
    {
        private GameObject _root;
        private Rink _rink;
        private readonly List<GameObject> _stones = new();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ScoringRink");
            var surface = new GameObject("Surface").transform;
            surface.SetParent(_root.transform, false);
            surface.localPosition = new Vector3(0f, GameConfig.RinkHeight, 0f);

            _rink = _root.AddComponent<Rink>();
            _rink.Surface = surface;
            _stones.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _stones) if (go != null) Object.DestroyImmediate(go);
            _stones.Clear();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        /// <summary>Places a settled stone the given distance down-rink from the target.</summary>
        private Stone PlaceStone(PlayerSide owner, float distanceFromCentre,
                                 bool settled = true, float angleDegrees = 0f)
        {
            var go = new GameObject($"Stone_{owner}_{_stones.Count}");
            _stones.Add(go);

            var stone = go.AddComponent<Stone>();
            stone.Initialise(owner);

            Vector3 offsetLocal = Quaternion.Euler(0f, angleDegrees, 0f)
                                  * new Vector3(distanceFromCentre, 0f, 0f);
            Vector3 centreLocal = _rink.WorldToRink(_rink.TargetCenterWorld);
            go.transform.position = _rink.RinkToWorld(centreLocal.x + offsetLocal.x,
                                                      centreLocal.z + offsetLocal.z);

            if (settled) stone.SetState(StoneState.Settled);
            return stone;
        }

        [Test]
        public void AnEmptyRinkIsABlankRound()
        {
            RoundResult result = ScoreKeeper.ScoreRound(_rink);
            Assert.IsTrue(result.IsBlank);
            Assert.AreEqual(0, result.Points);
        }

        [Test]
        public void StonesOutsideTheHouseDoNotScore()
        {
            PlaceStone(PlayerSide.One, ScoreKeeper.ScoringRadius + 0.05f);
            RoundResult result = ScoreKeeper.ScoreRound(_rink);
            Assert.IsTrue(result.IsBlank, "a stone past the outer ring is not in the house");
        }

        [Test]
        public void UnsettledStonesDoNotScore()
        {
            PlaceStone(PlayerSide.One, 0.01f, settled: false);
            RoundResult result = ScoreKeeper.ScoreRound(_rink);
            Assert.IsTrue(result.IsBlank, "a stone still sliding cannot be counted");
        }

        [Test]
        public void ClosestStoneTakesTheRound()
        {
            PlaceStone(PlayerSide.One, 0.02f);
            PlaceStone(PlayerSide.Two, 0.08f);

            RoundResult result = ScoreKeeper.ScoreRound(_rink);

            Assert.IsFalse(result.IsBlank);
            Assert.AreEqual(PlayerSide.One, result.Winner);
            Assert.AreEqual(1, result.Points);
        }

        [Test]
        public void EveryWinningStoneCloserThanTheOpponentScoresAPoint()
        {
            PlaceStone(PlayerSide.One, 0.02f);
            PlaceStone(PlayerSide.One, 0.04f, angleDegrees: 90f);
            PlaceStone(PlayerSide.One, 0.06f, angleDegrees: 180f);
            PlaceStone(PlayerSide.Two, 0.09f);

            RoundResult result = ScoreKeeper.ScoreRound(_rink);

            Assert.AreEqual(PlayerSide.One, result.Winner);
            Assert.AreEqual(3, result.Points);
        }

        [Test]
        public void AnOpponentStoneInBetweenCapsTheCount()
        {
            PlaceStone(PlayerSide.One, 0.02f);
            PlaceStone(PlayerSide.Two, 0.05f);
            PlaceStone(PlayerSide.One, 0.08f, angleDegrees: 120f);

            RoundResult result = ScoreKeeper.ScoreRound(_rink);

            Assert.AreEqual(PlayerSide.One, result.Winner);
            Assert.AreEqual(1, result.Points,
                "the second blue stone is behind an orange stone, so it does not count");
        }

        [Test]
        public void SweepingTheHouseScoresEveryStone()
        {
            PlaceStone(PlayerSide.Two, 0.01f);
            PlaceStone(PlayerSide.Two, 0.05f, angleDegrees: 90f);
            PlaceStone(PlayerSide.Two, 0.10f, angleDegrees: 200f);

            RoundResult result = ScoreKeeper.ScoreRound(_rink);

            Assert.AreEqual(PlayerSide.Two, result.Winner);
            Assert.AreEqual(3, result.Points, "with no opponent stone in the house, all count");
        }

        [Test]
        public void TheWinnerAlwaysScoresAtLeastOne()
        {
            PlaceStone(PlayerSide.One, 0.03f);
            RoundResult result = ScoreKeeper.ScoreRound(_rink);
            Assert.GreaterOrEqual(result.Points, 1);
        }

        [Test]
        public void ClosestStoneQueriesAgreeWithScoring()
        {
            Stone blue = PlaceStone(PlayerSide.One, 0.03f);
            Stone orange = PlaceStone(PlayerSide.Two, 0.07f);

            Assert.AreSame(blue, _rink.ClosestScoringStone());
            Assert.AreSame(blue, _rink.ClosestStoneFor(PlayerSide.One));
            Assert.AreSame(orange, _rink.ClosestStoneFor(PlayerSide.Two));
        }

        [Test]
        public void TakingTheLeadFromTheOpponentCountsAsASteal()
        {
            PlaceStone(PlayerSide.Two, 0.06f);
            Stone thief = PlaceStone(PlayerSide.One, 0.02f);

            Assert.IsTrue(ScoreKeeper.IsSteal(_rink, thief, PlayerSide.Two, hadLeader: true));
        }

        [Test]
        public void ExtendingYourOwnLeadIsNotASteal()
        {
            PlaceStone(PlayerSide.One, 0.05f);
            Stone second = PlaceStone(PlayerSide.One, 0.02f);

            Assert.IsFalse(ScoreKeeper.IsSteal(_rink, second, PlayerSide.One, hadLeader: true));
        }

        [Test]
        public void TheFirstStoneInTheHouseIsASteal()
        {
            Stone first = PlaceStone(PlayerSide.One, 0.04f);
            Assert.IsTrue(ScoreKeeper.IsSteal(_rink, first, PlayerSide.One, hadLeader: false),
                "with nobody lying closest yet, taking the lead is worth calling out");
        }

        [Test]
        public void AStoneThatMissedTheHouseDoesNotSteal()
        {
            PlaceStone(PlayerSide.Two, 0.04f);
            Stone wide = PlaceStone(PlayerSide.One, ScoreKeeper.ScoringRadius + 0.08f);

            Assert.IsFalse(ScoreKeeper.IsSteal(_rink, wide, PlayerSide.Two, hadLeader: true));
        }

        [Test]
        public void ClearingTheRinkRemovesEveryStoneFromScoring()
        {
            PlaceStone(PlayerSide.One, 0.02f);
            PlaceStone(PlayerSide.Two, 0.04f);
            Assert.AreEqual(2, _rink.Stones.Count);

            _rink.ClearStones();

            Assert.AreEqual(0, _rink.Stones.Count);
            Assert.IsTrue(ScoreKeeper.ScoreRound(_rink).IsBlank);
        }
    }
}
