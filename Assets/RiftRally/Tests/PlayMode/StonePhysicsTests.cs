using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RiftRally.Core;
using RiftRally.Gameplay;
using RiftRally.Setup;

namespace RiftRally.Tests
{
    /// <summary>
    /// The stone's motion model is shared by three systems: the stone itself, the aim preview
    /// the player trusts, and the AI's shot solver. If the real stone stops somewhere other
    /// than the analytic prediction, the preview lies and the AI misses every draw. These
    /// tests run real physics frames and check that the three agree.
    /// </summary>
    public class StonePhysicsTests
    {
        private GameObject _rig;
        private Rink _rink;
        private GameObject _template;
        private readonly List<GameObject> _spawned = new();
        private float _originalFixedDelta;

        [SetUp]
        public void SetUp()
        {
            _originalFixedDelta = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 1f / 90f;

            _rig = new GameObject("PhysicsRig");
            var root = new GameObject("Root").transform;
            root.SetParent(_rig.transform, false);

            RinkBuilder.Built built = RinkBuilder.Build(root);
            _rink = built.Playfield;
            _template = built.StoneTemplate;
            _spawned.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_rig != null) Object.DestroyImmediate(_rig);
            Time.fixedDeltaTime = _originalFixedDelta;
        }

        private Stone SpawnAt(Vector3 world, PlayerSide side = PlayerSide.One)
        {
            var go = Object.Instantiate(_template, world, Quaternion.identity);
            go.SetActive(true);
            _spawned.Add(go);

            var stone = go.GetComponent<Stone>();
            stone.Initialise(side);
            stone.MoveTo(world);
            return stone;
        }

        /// <summary>Analytic stopping distance for the constant-deceleration ice model.</summary>
        private static float ExpectedTravel(float speed)
            => speed * speed / (2f * GameConfig.IceFriction);

        private IEnumerator WaitForSettle(Stone stone, float maxSeconds = 12f)
        {
            float t = 0f;
            while (stone != null && stone.State == StoneState.InFlight && t < maxSeconds)
            {
                t += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator AStoneSlidesTheDistanceTheModelPredicts()
        {
            Vector3 start = _rink.LaunchPointWorld(PlayerSide.One);
            Stone stone = SpawnAt(start);

            // Chosen so the stone stops well short of the far wall, with nothing in its way.
            const float speed = 1.0f;
            stone.Launch(_rink.DownRink * speed, 0f);

            yield return WaitForSettle(stone, 15f);

            Assert.AreEqual(StoneState.Settled, stone.State, "the stone must come to rest");

            float travelled = Vector3.Distance(
                new Vector3(start.x, 0f, start.z),
                new Vector3(stone.transform.position.x, 0f, stone.transform.position.z));
            float expected = ExpectedTravel(speed);

            // The settle threshold and its hold time eat a little distance, so the band is
            // looser on the short side than the long side.
            Assert.That(travelled, Is.GreaterThan(expected * 0.80f),
                $"expected about {expected:0.000} m, travelled {travelled:0.000} m");
            Assert.That(travelled, Is.LessThan(expected * 1.12f),
                $"expected about {expected:0.000} m, travelled {travelled:0.000} m");
        }

        [UnityTest]
        public IEnumerator FasterThrowsFinishNearerTheHouse()
        {
            Vector3 start = _rink.LaunchPointWorld(PlayerSide.One);

            Stone slow = SpawnAt(start);
            slow.Launch(_rink.DownRink * 0.9f, 0f);
            yield return WaitForSettle(slow);
            float slowDistance = _rink.DistanceToTarget(slow.transform.position);

            Stone fast = SpawnAt(start);
            fast.Launch(_rink.DownRink * 1.15f, 0f);
            yield return WaitForSettle(fast);
            float fastDistance = _rink.DistanceToTarget(fast.transform.position);

            Assert.That(fastDistance, Is.LessThan(slowDistance),
                "a firmer throw must finish nearer the house");
        }

        [UnityTest]
        public IEnumerator AStoneStaysFlatOnTheSurfaceWhileSliding()
        {
            Vector3 start = _rink.LaunchPointWorld(PlayerSide.Two);
            Stone stone = SpawnAt(start, PlayerSide.Two);
            float surfaceY = _rink.StoneCenterY;

            stone.Launch(_rink.DownRink * 1.3f, 3f);

            for (int i = 0; i < 90; i++)
            {
                yield return new WaitForFixedUpdate();
                if (stone == null) break;
                Assert.That(stone.transform.position.y, Is.EqualTo(surfaceY).Within(0.003f),
                    "stones are constrained to the plane, so they must never leave it");
            }
        }

        [UnityTest]
        public IEnumerator AStonePushedOffTheNearEndIsOutOfPlay()
        {
            // The near end is deliberately open, so a stone pushed backwards leaves the rink.
            Vector3 start = _rink.RinkToWorld(0f, -GameConfig.RinkLength * 0.5f + 0.06f,
                                              GameConfig.StoneHeight * 0.5f);
            Stone stone = SpawnAt(start);
            stone.Launch(-_rink.DownRink * 2.0f, 0f);

            float timeout = 0f;
            while (stone != null && stone.State == StoneState.InFlight && timeout < 6f)
            {
                timeout += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(StoneState.Dead, stone.State);
            Assert.IsFalse(stone.CountsForScoring, "a dead stone cannot score");
        }

        [UnityTest]
        public IEnumerator SideWallsKeepStonesOnTheRink()
        {
            Vector3 start = _rink.LaunchPointWorld(PlayerSide.One);
            Stone stone = SpawnAt(start);

            // Thrown at a sharp angle, the stone must bank off the wall rather than vanish.
            Vector3 dir = (_rink.DownRink + _rink.Surface.right * 1.5f).normalized;
            stone.Launch(dir * 1.4f, 0f);

            yield return WaitForSettle(stone, 14f);

            Assert.AreNotEqual(StoneState.Dead, stone.State,
                "the side walls must contain the stone");

            Vector3 local = _rink.WorldToRink(stone.transform.position);
            Assert.That(Mathf.Abs(local.x),
                Is.LessThanOrEqualTo(GameConfig.RinkWidth * 0.5f + 0.005f));
        }

        [UnityTest]
        public IEnumerator AStruckStoneIsPutBackInPlayAndReSettles()
        {
            // Takeouts are the core tactical move, so a resting stone must be knockable.
            Vector3 targetSpot = _rink.TargetCenterWorld;
            Stone resting = SpawnAt(targetSpot, PlayerSide.Two);
            resting.SetState(StoneState.Settled);

            Vector3 approach = targetSpot - _rink.DownRink * 0.45f;
            Stone shooter = SpawnAt(approach, PlayerSide.One);
            shooter.Launch(_rink.DownRink * 1.6f, 0f);

            bool wasDisturbed = false;
            bool allRested = false;
            float timeout = 0f;

            while (timeout < 12f)
            {
                timeout += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
                if (resting == null) break;

                if (resting.State == StoneState.InFlight) wasDisturbed = true;
                if (wasDisturbed && _rink.AllStonesSettled()) { allRested = true; break; }
            }

            Assert.IsTrue(wasDisturbed, "the resting stone should have been knocked back in play");
            Assert.IsTrue(allRested, "and everything must come to rest again");
        }
    }
}
