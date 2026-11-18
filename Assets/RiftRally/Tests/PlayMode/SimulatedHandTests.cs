using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Setup;

namespace RiftRally.Tests
{
    /// <summary>
    /// Drives the game the way a player does - reach, pinch, pull, release - through the
    /// desktop VR simulator.
    ///
    /// This covers the gap every other suite leaves. The scoring tests know nothing about
    /// hands, the physics tests launch stones by calling Launch directly, and the match tests
    /// use the AI. None of them would notice if grabbing stopped working, if the grab radius
    /// were wrong, if the pull-back direction inverted, or if releasing threw nothing at all -
    /// which is the entire interaction the game is built on.
    /// </summary>
    public class SimulatedHandTests
    {
        private GameObject _host;
        private GameBootstrap _boot;
        private DesktopVrSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("SimHost");
            _host.SetActive(false);

            _boot = _host.AddComponent<GameBootstrap>();
            _boot.CreateAudio = false;
            _boot.CreateHud = false;
            _boot.EnableMultiplayer = false;      // no lobby, no sockets
            _boot.ApplyQualitySettings = false;
            _boot.EnableSlowMotion = false;
            _boot.SimulateVrOnDesktop = true;

            _host.SetActive(true);
            _sim = _boot.Simulator;

            // Build has already run in Awake; switching the component off now stops its
            // attract loop. With no lobby in this fixture, that loop treats any pinch as
            // "start a match", which would clear the rink out from under the throw being
            // tested. The real game has the lobby, where the same pinch is a button press.
            _boot.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);

            foreach (var stone in Object.FindObjectsByType<Stone>(FindObjectsSortMode.None))
                if (stone != null) Object.DestroyImmediate(stone.gameObject);
        }

        [UnityTest]
        public IEnumerator TheSimulatorSuppliesTrackedHands()
        {
            yield return null;

            Assert.IsNotNull(_sim, "a desktop run should stand in for the headset");
            Assert.IsNotNull(_boot.Hands);

            yield return null;

            Assert.IsTrue(_boot.Hands.Right.IsTracked, "the right hand must be tracked");
            Assert.IsTrue(_boot.Hands.Left.IsTracked, "and so must the left");
            Assert.IsFalse(_boot.Hands.Right.IsPinching, "hands start open");
        }

        [UnityTest]
        public IEnumerator PinchingOnAStoneGrabsItAndReleasingThrowsIt()
        {
            yield return null;

            _boot.Launcher.BeginTurn(PlayerSide.One);
            yield return null;

            Stone stone = _boot.Launcher.ActiveStone;
            Assert.IsNotNull(stone, "a stone should be docked and waiting");
            Assert.AreEqual(LauncherState.Ready, _boot.Launcher.State);

            // Reach to the stone and close the hand.
            _sim.SetHand(HandSide.Right, stone.transform.position, false);
            yield return null;
            _sim.SetPinch(HandSide.Right, true);
            yield return null;
            yield return null;

            Assert.AreEqual(LauncherState.Aiming, _boot.Launcher.State,
                "pinching on the stone must grab it");
            Assert.AreEqual(StoneState.Held, stone.State);

            // Pull back down the rink, giving the spring time to follow the hand.
            Vector3 anchor = stone.transform.position;
            Vector3 pullBack = anchor - _boot.Playfield.DownRink * 0.22f;

            for (int i = 0; i < 40; i++)
            {
                _sim.SetHand(HandSide.Right, pullBack, true);
                yield return null;
            }

            float travelled = Vector3.Distance(stone.transform.position, anchor);
            Assert.That(travelled, Is.GreaterThan(0.1f),
                $"the stone should follow the hand back; moved {travelled:0.000} m");

            // Opening the hand is the throw.
            _sim.SetPinch(HandSide.Right, false);
            yield return null;
            yield return null;

            Assert.AreEqual(LauncherState.Thrown, _boot.Launcher.State,
                "releasing a pulled stone must throw it");
            Assert.AreEqual(StoneState.InFlight, stone.State);
            Assert.That(stone.Speed, Is.GreaterThan(GameConfig.MinLaunchSpeed * 0.9f),
                "and it must leave with real speed");

            Vector3 velocityLocal =
                _boot.Playfield.Surface.InverseTransformDirection(stone.Velocity);
            Assert.That(velocityLocal.z, Is.GreaterThan(0f),
                "pulling back must send the stone forwards, not backwards");
        }

        [UnityTest]
        public IEnumerator PullingFurtherThrowsHarder()
        {
            yield return null;

            yield return ThrowWithPull(0.08f);
            float soft = _lastSpeed;

            yield return ClearStones();

            yield return ThrowWithPull(0.30f);
            float hard = _lastSpeed;

            Assert.That(soft, Is.GreaterThan(0f), "the gentle throw should still leave the hand");
            Assert.That(hard, Is.GreaterThan(soft * 1.2f),
                $"a longer pull must throw harder; {soft:0.00} vs {hard:0.00} m/s");
        }

        private float _lastSpeed;

        private IEnumerator ClearStones()
        {
            _boot.Launcher.EndTurn();
            foreach (var stone in Object.FindObjectsByType<Stone>(FindObjectsSortMode.None))
                if (stone != null) Object.DestroyImmediate(stone.gameObject);
            yield return null;
        }

        /// <summary>Grabs the docked stone, pulls it back a set distance, and releases.</summary>
        private IEnumerator ThrowWithPull(float pullDistance)
        {
            _boot.Launcher.BeginTurn(PlayerSide.One);
            yield return null;

            Stone stone = _boot.Launcher.ActiveStone;
            Vector3 anchor = stone.transform.position;

            _sim.SetHand(HandSide.Right, anchor, false);
            yield return null;
            _sim.SetPinch(HandSide.Right, true);
            yield return null;

            Vector3 target = anchor - _boot.Playfield.DownRink * pullDistance;
            for (int i = 0; i < 45; i++)
            {
                _sim.SetHand(HandSide.Right, target, true);
                yield return null;
            }

            _sim.SetPinch(HandSide.Right, false);
            yield return null;
            yield return null;

            _lastSpeed = stone != null ? stone.Speed : 0f;
        }

        [UnityTest]
        public IEnumerator PinchingAwayFromTheStoneDoesNotGrabIt()
        {
            yield return null;

            _boot.Launcher.BeginTurn(PlayerSide.One);
            yield return null;

            Stone stone = _boot.Launcher.ActiveStone;
            Assert.IsNotNull(stone);

            // Well outside the grab radius.
            _sim.SetHand(HandSide.Right, stone.transform.position + Vector3.right * 0.45f, false);
            yield return null;
            _sim.SetPinch(HandSide.Right, true);
            yield return null;
            yield return null;

            Assert.AreEqual(LauncherState.Ready, _boot.Launcher.State,
                "a pinch in mid-air must not pull a stone across the table");
        }

        [UnityTest]
        public IEnumerator ReleasingWithoutPullingCancelsInsteadOfThrowing()
        {
            yield return null;

            _boot.Launcher.BeginTurn(PlayerSide.One);
            yield return null;

            Stone stone = _boot.Launcher.ActiveStone;
            _sim.SetHand(HandSide.Right, stone.transform.position, false);
            yield return null;
            _sim.SetPinch(HandSide.Right, true);
            yield return null;
            yield return null;

            Assert.AreEqual(LauncherState.Aiming, _boot.Launcher.State);

            // Let go having barely moved: a fumble, not a throw.
            _sim.SetPinch(HandSide.Right, false);
            yield return null;
            yield return null;

            Assert.AreEqual(LauncherState.Ready, _boot.Launcher.State,
                "a pinch that slips must hand the stone back, not fire it");
            Assert.AreEqual(StoneState.Docked, stone.State);
        }

        [UnityTest]
        public IEnumerator LosingTrackingMidPullNeverCountsAsAThrow()
        {
            yield return null;

            _boot.Launcher.BeginTurn(PlayerSide.One);
            yield return null;

            Stone stone = _boot.Launcher.ActiveStone;
            _sim.SetHand(HandSide.Right, stone.transform.position, false);
            yield return null;
            _sim.SetPinch(HandSide.Right, true);
            yield return null;
            yield return null;
            Assert.AreEqual(LauncherState.Aiming, _boot.Launcher.State);

            // The hand leaves the tracking volume mid-pull. This happens constantly on a
            // headset, and firing a random shot because of it would be infuriating.
            _sim.SetHandTracked(HandSide.Right, false);

            float waited = 0f;
            while (waited < 1.5f && _boot.Launcher.State == LauncherState.Aiming)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Assert.AreNotEqual(LauncherState.Thrown, _boot.Launcher.State,
                "lost tracking must never count as a throw");
        }
    }
}
