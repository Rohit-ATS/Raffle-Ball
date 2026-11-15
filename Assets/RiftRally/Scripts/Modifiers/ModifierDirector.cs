using System.Collections.Generic;
using UnityEngine;

namespace RiftRally.Modifiers
{
    /// <summary>
    /// Chooses which modifier is live each round and guarantees exactly one is active.
    ///
    /// Round one is always clean (no modifier). New players need one round to learn the throw
    /// before the rink starts arguing with them, and it gives the later reveals contrast:
    /// the change only reads as a twist if the player knows the baseline.
    /// </summary>
    public class ModifierDirector : MonoBehaviour
    {
        [Tooltip("All modifiers present in the scene.")]
        public List<RinkModifier> Modifiers = new();

        [Tooltip("Leave round 1 with no modifier so the player learns the basic throw first.")]
        public bool FirstRoundIsClean = true;

        public RinkModifier Current { get; private set; }

        private readonly List<RinkModifier> _bag = new();
        private RinkModifier _lastUsed;

        private void Awake()
        {
            if (Modifiers == null || Modifiers.Count == 0)
            {
                Modifiers = new List<RinkModifier>(GetComponentsInChildren<RinkModifier>(true));
            }
            DeactivateAll();
        }

        /// <summary>
        /// Picks the modifier for a round, activates it, and returns it (null for a clean rink).
        /// </summary>
        public RinkModifier SelectForRound(int round)
        {
            DeactivateAll();

            if (FirstRoundIsClean && round <= 1)
            {
                Current = null;
                return null;
            }

            RinkModifier pick = DrawFromBag();
            Current = pick;

            if (pick != null)
            {
                pick.Activate();
                _lastUsed = pick;
            }

            return pick;
        }

        /// <summary>
        /// Shuffle-bag selection: every modifier appears once before any repeats, and a
        /// modifier never immediately follows itself. Pure random would happily show the same
        /// rink three rounds running, which would waste the whole feature.
        /// </summary>
        private RinkModifier DrawFromBag()
        {
            var usable = new List<RinkModifier>();
            foreach (var m in Modifiers) if (m != null) usable.Add(m);
            if (usable.Count == 0) return null;
            if (usable.Count == 1) return usable[0];

            if (_bag.Count == 0)
            {
                _bag.AddRange(usable);
                // Fisher-Yates.
                for (int i = _bag.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
                }

                // Avoid an immediate repeat across bag refills.
                if (_bag.Count > 1 && _bag[0] == _lastUsed)
                {
                    (_bag[0], _bag[1]) = (_bag[1], _bag[0]);
                }
            }

            RinkModifier pick = _bag[0];
            _bag.RemoveAt(0);
            return pick;
        }

        public void DeactivateAll()
        {
            foreach (var m in Modifiers)
            {
                if (m != null && m.IsActive) m.Deactivate();
                else if (m != null) m.Deactivate();
            }
            Current = null;
        }
    }
}
