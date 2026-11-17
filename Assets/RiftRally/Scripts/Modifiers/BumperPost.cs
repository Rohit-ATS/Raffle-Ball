using UnityEngine;
using RiftRally.Gameplay;

namespace RiftRally.Modifiers
{
    /// <summary>
    /// Sits on a single bumper post and reports stone strikes back to the modifier, so the
    /// spark fires at the actual contact point rather than at the post's centre.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BumperPost : MonoBehaviour
    {
        public BumperModifier Owner;

        private void OnCollisionEnter(Collision collision)
        {
            if (Owner == null || !Owner.IsActive) return;

            var stone = collision.collider.GetComponentInParent<Stone>();
            if (stone == null) return;
            if (collision.impulse.magnitude < 0.02f) return;

            Vector3 point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;

            Owner.ReportHit(point);
        }
    }
}
