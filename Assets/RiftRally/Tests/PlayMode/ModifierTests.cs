using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RiftRally.Modifiers;

namespace RiftRally.Tests
{
    /// <summary>
    /// The modifier director is what stops the per-round twist feeling random. These tests
    /// hold it to the two promises that make the feature work: round one is always clean so
    /// the player learns the baseline throw, and no modifier repeats until the rest have been
    /// seen.
    /// </summary>
    public class ModifierTests
    {
        private GameObject _root;
        private ModifierDirector _director;
        private readonly List<RinkModifier> _mods = new();

        /// <summary>A modifier with no behaviour, used to observe the director's choices.</summary>
        private class ProbeModifier : RinkModifier
        {
            public int ActivationCount;
            protected override void OnActivated() => ActivationCount++;
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Modifiers");
            _mods.Clear();

            for (int i = 0; i < 3; i++)
            {
                var child = new GameObject($"Probe{i}");
                child.transform.SetParent(_root.transform, false);

                var probe = child.AddComponent<ProbeModifier>();
                probe.DisplayName = $"Probe {i}";
                probe.Visuals = new GameObject($"Visuals{i}");
                probe.Visuals.transform.SetParent(child.transform, false);
                _mods.Add(probe);
            }

            _director = _root.AddComponent<ModifierDirector>();
            _director.Modifiers = new List<RinkModifier>(_mods);
            _director.FirstRoundIsClean = true;
            _director.DeactivateAll();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void TheFirstRoundIsClean()
        {
            Assert.IsNull(_director.SelectForRound(1));
            Assert.IsNull(_director.Current);
            foreach (var m in _mods) Assert.IsFalse(m.IsActive);
        }

        [Test]
        public void LaterRoundsActivateExactlyOneModifier()
        {
            var picked = _director.SelectForRound(2);

            Assert.IsNotNull(picked);
            Assert.AreSame(picked, _director.Current);
            Assert.IsTrue(picked.IsActive);

            int active = 0;
            foreach (var m in _mods) if (m.IsActive) active++;
            Assert.AreEqual(1, active, "exactly one modifier may be live at a time");
        }

        [Test]
        public void ActivatingShowsTheVisualsAndDeactivatingHidesThem()
        {
            var picked = _director.SelectForRound(2);
            Assert.IsTrue(picked.Visuals.activeSelf);

            _director.DeactivateAll();
            Assert.IsFalse(picked.Visuals.activeSelf);
            Assert.IsNull(_director.Current);
        }

        [Test]
        public void EveryModifierIsSeenBeforeAnyRepeats()
        {
            var seen = new List<RinkModifier>();
            for (int round = 2; round <= 4; round++) seen.Add(_director.SelectForRound(round));

            CollectionAssert.AllItemsAreUnique(seen);
            Assert.AreEqual(_mods.Count, seen.Count);
        }

        [Test]
        public void AModifierNeverFollowsItselfAcrossBagRefills()
        {
            RinkModifier previous = null;
            for (int round = 2; round < 40; round++)
            {
                var picked = _director.SelectForRound(round);
                Assert.IsNotNull(picked);
                Assert.AreNotSame(previous, picked,
                    $"round {round} repeated the previous modifier");
                previous = picked;
            }
        }

        [Test]
        public void SwitchingRoundsDeactivatesTheOutgoingModifier()
        {
            var first = _director.SelectForRound(2);
            var second = _director.SelectForRound(3);

            Assert.AreNotSame(first, second);
            Assert.IsFalse(first.IsActive, "the previous modifier must be switched off");
            Assert.IsTrue(second.IsActive);
        }

        [Test]
        public void ADirectorWithNoModifiersSimplyStaysClean()
        {
            var bare = new GameObject("BareDirector");
            try
            {
                var director = bare.AddComponent<ModifierDirector>();
                director.Modifiers = new List<RinkModifier>();
                Assert.IsNull(director.SelectForRound(2));
                Assert.IsNull(director.Current);
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }
    }
}
