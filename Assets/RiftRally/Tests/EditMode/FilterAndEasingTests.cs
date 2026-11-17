using NUnit.Framework;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Presentation;

namespace RiftRally.Tests
{
    /// <summary>
    /// The smoothing layer, which is what the game's feel rests on.
    ///
    /// These are the properties that matter in a headset rather than the implementation
    /// details: a still hand must stop jittering, a moving hand must not lag, and nothing may
    /// behave differently at 72 Hz than at 90. All three are easy to break with a one-line
    /// change and impossible to notice without a device, which is exactly why they are pinned
    /// here.
    /// </summary>
    public class FilterAndEasingTests
    {
        private const float Dt = 1f / 90f;

        [Test]
        public void TheFilterRemovesJitterFromAStillHand()
        {
            // Built with the game's own defaults, so this guards the shipped tuning rather
            // than a set of numbers that only exist in the test.
            var filter = new OneEuroFilter();
            var random = new System.Random(1234);

            // A hand held still, with two millimetres of tracker noise.
            float sum = 0f;
            float worstInput = 0f;
            float worstOutput = 0f;

            for (int i = 0; i < 300; i++)
            {
                float noise = (float)(random.NextDouble() - 0.5) * 0.004f;
                float filtered = filter.Filter(0.5f + noise, Dt);

                if (i < 60) continue;   // let the filter settle first
                sum += filtered;
                worstInput = Mathf.Max(worstInput, Mathf.Abs(noise));
                worstOutput = Mathf.Max(worstOutput, Mathf.Abs(filtered - 0.5f));
            }

            Assert.That(worstOutput, Is.LessThan(worstInput * 0.6f),
                "a still hand must come out visibly steadier than it went in");
            Assert.That(sum / 240f, Is.EqualTo(0.5f).Within(0.001f),
                "and the filter must not introduce a bias");
        }

        [Test]
        public void TheFilterKeepsUpWithAFastPull()
        {
            var filter = new OneEuroFilter();

            // A 30 cm pull over a third of a second, which is a normal throw.
            float position = 0f;
            float filtered = 0f;
            for (int i = 0; i < 30; i++)
            {
                position += 0.01f;
                filtered = filter.Filter(position, Dt);
            }

            float lag = position - filtered;
            Assert.That(lag, Is.LessThan(0.02f),
                $"lag during a fast pull must stay small; was {lag * 1000f:0.0} mm");
        }

        [Test]
        public void TheFilterPassesTheFirstSampleThrough()
        {
            var filter = new OneEuroFilter();
            Assert.That(filter.Filter(0.73f, Dt), Is.EqualTo(0.73f).Within(1e-6f),
                "with no history there is nothing to smooth towards, so it must not lerp from zero");
        }

        [Test]
        public void TheFilterSurvivesAZeroLengthFrame()
        {
            var filter = new OneEuroFilter();
            filter.Filter(1f, Dt);
            Assert.DoesNotThrow(() => filter.Filter(2f, 0f));
        }

        [Test]
        public void VelocityIsEstimatedAcrossTheWindowNotTheLastFrame()
        {
            var estimator = new VelocityEstimator(8);

            // One metre per second along X, sampled at 90 Hz.
            for (int i = 0; i < 8; i++) estimator.Add(new Vector3(i * Dt, 0f, 0f), i * Dt);

            Assert.That(estimator.Velocity.x, Is.EqualTo(1f).Within(0.05f));
        }

        [Test]
        public void VelocityIsZeroUntilThereIsEnoughHistory()
        {
            var estimator = new VelocityEstimator(8);
            Assert.AreEqual(Vector3.zero, estimator.Velocity,
                "a throw released on the first tracked frame would otherwise be random");

            estimator.Add(Vector3.zero, 0f);
            Assert.AreEqual(Vector3.zero, estimator.Velocity);
        }

        [Test]
        public void PeakSpeedCatchesABriefFlick()
        {
            var estimator = new VelocityEstimator(8);
            float t = 0f;

            for (int i = 0; i < 6; i++) { estimator.Add(Vector3.zero, t); t += Dt; }

            // One fast frame among slow ones: the average hides it, the peak does not.
            estimator.Add(new Vector3(0.05f, 0f, 0f), t);

            Assert.That(estimator.PeakSpeed, Is.GreaterThan(2f));
        }

        [Test]
        public void DampingIsIndependentOfFrameRate()
        {
            // The same 100 ms of real time, taken in different numbers of steps, must land in
            // the same place. Otherwise the game feels different on every headset.
            float at90 = 0f;
            for (int i = 0; i < 9; i++) at90 = Easing.Damp(at90, 1f, 10f, 1f / 90f);

            float at72 = 0f;
            for (int i = 0; i < 7; i++) at72 = Easing.Damp(at72, 1f, 10f, 1f / 72f);

            float at120 = 0f;
            for (int i = 0; i < 12; i++) at120 = Easing.Damp(at120, 1f, 10f, 1f / 120f);

            Assert.That(at72, Is.EqualTo(at90).Within(0.02f));
            Assert.That(at120, Is.EqualTo(at90).Within(0.02f));
        }

        [Test]
        public void EasingCurvesStartAtZeroAndEndAtOne()
        {
            Assert.That(Easing.SmoothStep(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Easing.SmoothStep(1f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Easing.OutCubic(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Easing.OutCubic(1f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Easing.InCubic(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Easing.InCubic(1f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Easing.OutBack(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Easing.OutBack(1f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Easing.OutElastic(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Easing.OutElastic(1f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void EasingCurvesAreClampedOutsideTheUnitRange()
        {
            Assert.That(Easing.SmoothStep(-5f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Easing.SmoothStep(5f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Easing.OutCubic(9f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void OutBackOvershootsAndComesBack()
        {
            bool overshot = false;
            for (float t = 0f; t <= 1f; t += 0.01f)
                if (Easing.OutBack(t) > 1.01f) overshot = true;

            Assert.IsTrue(overshot, "the whole point of this curve is the overshoot");
            Assert.That(Easing.OutBack(1f), Is.EqualTo(1f).Within(1e-4f), "but it must settle");
        }

        [Test]
        public void PulseRisesAndFalls()
        {
            Assert.That(Easing.Pulse(0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Easing.Pulse(0.5f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Easing.Pulse(1f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TheSpringArrivesWithoutOvershooting()
        {
            Vector3 position = Vector3.zero;
            Vector3 velocity = Vector3.zero;
            var target = new Vector3(0.3f, 0f, 0f);

            float furthest = 0f;
            for (int i = 0; i < 180; i++)
            {
                position = Easing.Spring(position, ref velocity, target, 4.2f, Dt);
                furthest = Mathf.Max(furthest, position.x);
            }

            Assert.That(position.x, Is.EqualTo(target.x).Within(0.002f), "it must arrive");
            Assert.That(furthest, Is.LessThan(target.x * 1.02f),
                "critically damped means no overshoot, which is why a held stone does not wobble");
        }

        [Test]
        public void TheSpringIsStableAtAZeroLengthFrame()
        {
            Vector3 position = Vector3.zero;
            Vector3 velocity = Vector3.zero;
            Assert.DoesNotThrow(() =>
                position = Easing.Spring(position, ref velocity, Vector3.one, 4f, 0f));
            Assert.AreEqual(Vector3.zero, position);
        }
    }
}
