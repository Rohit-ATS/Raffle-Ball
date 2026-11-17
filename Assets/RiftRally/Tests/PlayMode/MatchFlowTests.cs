using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Modifiers;
using RiftRally.Setup;

namespace RiftRally.Tests
{
    /// <summary>
    /// End-to-end cover for the match loop. The game is built exactly as it is in the shipped
    /// scene, then an AI-versus-AI match is played at speed with no hands attached. This is
    /// the test that would catch a hang - a stone that never settles, a turn that never ends -
    /// which is the one failure a player cannot work around.
    /// </summary>
    public class MatchFlowTests
    {
        private GameObject _host;
        private GameBootstrap _boot;
        private float _originalTimeScale;
        private float _originalMaxDelta;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _originalMaxDelta = Time.maximumDeltaTime;

            // Run the match fast, and let physics catch up in bigger chunks so the stones
            // still simulate properly at the higher time scale.
            Time.timeScale = 8f;
            Time.maximumDeltaTime = 0.5f;

            // Built while inactive so the configuration below lands before Awake runs.
            _host = new GameObject("RiftRallyTestHost");
            _host.SetActive(false);
            _boot = _host.AddComponent<GameBootstrap>();

            // AI against AI, but not started automatically: each test subscribes to the
            // match events first and kicks it off itself, so no round-one event is missed.
            _boot.Mode = GameMode.AiVsAi;
            _boot.DemoMatch = false;
            _boot.AutoStartMatch = false;
            _boot.AiSkill = AiSkill.Sharp;
            _boot.CreateHud = true;
            _boot.CreateAudio = false;           // no need to synthesise audio in a test
            _boot.CreateFallbackCameraAndLight = true;

            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);

            // Thrown stones live at the scene root, so they outlive the host object.
            foreach (var stone in Object.FindObjectsByType<Stone>(FindObjectsSortMode.None))
                if (stone != null) Object.DestroyImmediate(stone.gameObject);

            Time.timeScale = _originalTimeScale;
            Time.maximumDeltaTime = _originalMaxDelta;
        }

        [UnityTest]
        public IEnumerator TheBootstrapBuildsAPlayableRink()
        {
            yield return null;

            Assert.IsNotNull(_boot.Playfield, "a rink must exist");
            Assert.IsNotNull(_boot.Launcher, "a launcher must exist");
            Assert.IsNotNull(_boot.Match, "a match manager must exist");
            Assert.IsNotNull(_boot.Ai, "an opponent must exist");
            Assert.IsNotNull(_boot.Hands, "hand input must exist");
            Assert.IsNotNull(_boot.Launcher.StonePrefab, "the launcher needs a stone to throw");
            Assert.IsNotNull(_boot.Launcher.Preview, "the throw needs an aim preview");
            Assert.AreSame(_boot.Playfield, Rink.Instance);
            Assert.IsNotNull(Camera.main, "something must be rendering");
        }

        [UnityTest]
        public IEnumerator AnAiMatchPlaysThroughToAWinnerWithoutStalling()
        {
            yield return null;

            MatchManager match = _boot.Match;
            Assert.IsNotNull(match);
            Assert.AreEqual(MatchState.Attract, match.State,
                "the match must wait to be started");

            int endedCount = 0;
            PlayerSide winner = PlayerSide.One;
            var roundsStarted = new List<int>();
            var results = new List<RoundResult>();
            int mostStonesSeen = 0;

            match.MatchEnded += side => { endedCount++; winner = side; };
            match.RoundStarted += (round, modifier) => roundsStarted.Add(round);
            match.RoundScored += result => results.Add(result);

            match.StartMatch(GameMode.AiVsAi);
            yield return null;

            float deadline = Time.realtimeSinceStartup + 240f;
            while (match.State != MatchState.MatchOver)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"the match stalled in state {match.State} " +
                                $"on round {match.Round} after {roundsStarted.Count} round(s)");
                }

                mostStonesSeen = Mathf.Max(mostStonesSeen, _boot.Playfield.Stones.Count);
                yield return null;
            }

            Assert.AreEqual(1, endedCount, "the match must end exactly once");
            Assert.That(roundsStarted.Count, Is.GreaterThanOrEqualTo(GameConfig.RoundsToWinMatch),
                "a best-of-three cannot be decided in fewer rounds than it takes to win");
            Assert.That(roundsStarted.Count, Is.LessThanOrEqualTo(GameConfig.MaxRounds));
            Assert.AreEqual(roundsStarted.Count, results.Count,
                "every round that starts must also be scored");

            int roundsWon = match.RoundsWon[0] + match.RoundsWon[1];
            Assert.That(roundsWon, Is.LessThanOrEqualTo(GameConfig.MaxRounds));
            Assert.That(match.RoundsWon[(int)winner],
                Is.GreaterThanOrEqualTo(match.RoundsWon[(int)winner.Other()]),
                "the declared winner must not have won fewer rounds than the loser");

            int expectedMax = GameConfig.StonesPerPlayerPerRound * 2;
            Assert.That(mostStonesSeen, Is.LessThanOrEqualTo(expectedMax),
                "the rink must be cleared between rounds");
            Assert.That(mostStonesSeen, Is.GreaterThan(0), "stones must actually be thrown");
        }

        [UnityTest]
        public IEnumerator EveryRoundAfterTheFirstRevealsAModifier()
        {
            yield return null;

            var byRound = new Dictionary<int, RinkModifier>();
            _boot.Match.RoundStarted += (round, modifier) => byRound[round] = modifier;

            _boot.Match.StartMatch(GameMode.AiVsAi);
            yield return null;

            float deadline = Time.realtimeSinceStartup + 240f;
            while (_boot.Match.State != MatchState.MatchOver)
            {
                if (Time.realtimeSinceStartup > deadline) break;
                yield return null;
            }

            Assert.That(byRound.Count, Is.GreaterThan(0), "at least one round must have started");

            foreach (var pair in byRound)
            {
                if (pair.Key == 1)
                {
                    Assert.IsNull(pair.Value, "round one teaches the plain throw");
                }
                else
                {
                    Assert.IsNotNull(pair.Value, $"round {pair.Key} should have a modifier");
                    Assert.IsFalse(string.IsNullOrEmpty(pair.Value.DisplayName),
                        "a modifier the player cannot read about is a trap");
                    Assert.IsFalse(string.IsNullOrEmpty(pair.Value.Description));
                }
            }
        }

        [UnityTest]
        public IEnumerator TurnsAlternateBetweenTheTwoSides()
        {
            yield return null;

            var order = new List<PlayerSide>();
            _boot.Match.TurnStarted += (side, stoneNumber) => order.Add(side);

            _boot.Match.StartMatch(GameMode.AiVsAi);
            yield return null;

            // Watch the first round only; that is enough to prove the alternation.
            float deadline = Time.realtimeSinceStartup + 150f;
            int wanted = GameConfig.StonesPerPlayerPerRound * 2;
            while (order.Count < wanted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(order.Count, Is.GreaterThanOrEqualTo(wanted),
                $"expected {wanted} turns in a round, saw {order.Count}");

            for (int i = 1; i < wanted; i++)
            {
                Assert.AreNotEqual(order[i - 1], order[i],
                    "the same side must not throw twice in a row inside a round");
            }

            int blue = 0, orange = 0;
            for (int i = 0; i < wanted; i++)
            {
                if (order[i] == PlayerSide.One) blue++; else orange++;
            }
            Assert.AreEqual(GameConfig.StonesPerPlayerPerRound, blue, "blue throws its share");
            Assert.AreEqual(GameConfig.StonesPerPlayerPerRound, orange, "orange throws its share");
        }
    }
}
