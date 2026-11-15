using UnityEngine;
using RiftRally.Gameplay;

namespace RiftRally.Modifiers
{
    /// <summary>
    /// A band across the middle of the rink that pushes sliding stones sideways.
    ///
    /// Tactically this makes the straight line to the centre wrong: the player has to aim
    /// upstream and let the current carry the stone in, and a faster throw drifts less than a
    /// slow one. That trade between speed and accuracy is the decision the modifier adds.
    /// </summary>
    public class CurrentModifier : RinkModifier
    {
        [Header("Zone (rink-local Z band)")]
        public float BandCenterZ = 0.05f;
        public float BandHalfDepth = 0.26f;

        [Header("Force")]
        [Tooltip("Sideways acceleration in m/s^2. Positive pushes towards rink-local +X.")]
        public float Strength = 0.85f;

        [Tooltip("Flip direction each round so players cannot memorise one answer.")]
        public bool RandomiseDirection = true;

        [Header("Feedback")]
        public ParticleSystem FlowParticles;

        private float _sign = 1f;

        protected override void OnActivated()
        {
            _sign = RandomiseDirection && Random.value < 0.5f ? -1f : 1f;

            if (FlowParticles != null)
            {
                // Point the visual flow the way the force actually pushes.
                var t = FlowParticles.transform;
                Vector3 local = t.localEulerAngles;
                local.y = _sign > 0f ? 90f : -90f;
                t.localEulerAngles = local;
                FlowParticles.Play();
            }
        }

        protected override void OnDeactivated()
        {
            if (FlowParticles != null) FlowParticles.Stop();
        }

        /// <summary>World-space direction the current is pushing, for UI arrows.</summary>
        public Vector3 FlowDirection
            => Playfield != null ? Playfield.Surface.right * _sign : Vector3.right;

        protected override void Tick()
        {
            if (Playfield == null) return;

            Vector3 push = Playfield.Surface.right * (_sign * Strength);

            var stones = Playfield.Stones;
            for (int i = 0; i < stones.Count; i++)
            {
                Stone s = stones[i];
                if (s == null || s.State != StoneState.InFlight) continue;

                Vector3 local = Playfield.WorldToRink(s.transform.position);
                if (Mathf.Abs(local.z - BandCenterZ) > BandHalfDepth) continue;

                // Continuous acceleration, not an impulse: a stone crossing quickly is
                // deflected less than a slow one, which is exactly the risk/reward we want.
                s.AddExternalAcceleration(push);
            }
        }
    }
}
