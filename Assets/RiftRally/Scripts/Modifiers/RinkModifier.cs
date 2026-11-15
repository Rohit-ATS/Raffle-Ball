using UnityEngine;
using RiftRally.Gameplay;

namespace RiftRally.Modifiers
{
    /// <summary>
    /// Base class for the things that change the rink between rounds.
    ///
    /// Design rule for every modifier: it must change where a smart player aims. A modifier
    /// that only changes how the rink looks is noise, and would make rounds feel varied while
    /// playing identically. Each one here alters the optimal line in a way the player can
    /// read before they throw.
    /// </summary>
    public abstract class RinkModifier : MonoBehaviour
    {
        [Header("Presentation")]
        [Tooltip("Shown on the round-intro banner.")]
        public string DisplayName = "Modifier";

        [Tooltip("One short line telling the player how to play it.")]
        public string Description = "";

        [Tooltip("Root object holding this modifier's visuals. Toggled with the modifier.")]
        public GameObject Visuals;

        public bool IsActive { get; private set; }

        protected Rink Playfield => Rink.Instance;

        protected virtual void Awake()
        {
            // Modifiers start switched off; the director turns exactly one on per round.
            ApplyActive(false);
        }

        public void Activate()
        {
            IsActive = true;
            ApplyActive(true);
            OnActivated();
        }

        public void Deactivate()
        {
            IsActive = false;
            ApplyActive(false);
            OnDeactivated();
        }

        private void ApplyActive(bool on)
        {
            if (Visuals != null) Visuals.SetActive(on);
        }

        protected virtual void OnActivated() { }
        protected virtual void OnDeactivated() { }

        /// <summary>
        /// Called every frame while active, after stones have moved. Override to affect play.
        /// </summary>
        protected virtual void Tick() { }

        private void Update()
        {
            if (IsActive) Tick();
        }
    }
}
