using UnityEngine;

namespace RiftRally.Modifiers
{
    /// <summary>
    /// Posts planted in the middle of the rink that stones bounce off.
    ///
    /// This one changes the game by closing the direct line to the centre: the player either
    /// threads a narrowed gap or plays a deliberate bank shot off a post. The posts sit in
    /// fixed, clearly visible positions and do not move during a round, so a blocked shot is
    /// always the player's read rather than bad luck.
    /// </summary>
    public class BumperModifier : RinkModifier
    {
        [Header("Posts")]
        [Tooltip("Colliders enabled while this modifier is active.")]
        public Collider[] PostColliders;

        [Tooltip("Bounciness applied to the posts, 0 = dead, 1 = perfectly elastic.")]
        [Range(0f, 1f)]
        public float Bounciness = 0.72f;

        [Header("Feedback")]
        public ParticleSystem HitSpark;

        private PhysicsMaterial _material;

        protected override void Awake()
        {
            base.Awake();

            // One shared material so every post reacts identically.
            _material = new PhysicsMaterial("BumperPost")
            {
                bounciness = Bounciness,
                dynamicFriction = 0.05f,
                staticFriction = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };

            ApplyColliders(false);
        }

        protected override void OnActivated() => ApplyColliders(true);
        protected override void OnDeactivated() => ApplyColliders(false);

        private void ApplyColliders(bool on)
        {
            if (PostColliders == null) return;
            for (int i = 0; i < PostColliders.Length; i++)
            {
                var c = PostColliders[i];
                if (c == null) continue;
                c.enabled = on;
                if (_material != null) c.sharedMaterial = _material;
            }
        }

        /// <summary>Called by a post when a stone strikes it, to spark at the contact point.</summary>
        public void ReportHit(Vector3 contactPoint)
        {
            if (HitSpark == null) return;
            HitSpark.transform.position = contactPoint;
            HitSpark.Play();
        }
    }
}
