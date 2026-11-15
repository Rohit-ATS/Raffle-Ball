using System.Collections;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Modifiers;

namespace RiftRally.Gameplay
{
    public enum AiSkill
    {
        Friendly,   // misses often, keeps solo play relaxed
        Sharp,      // a real opponent
        Ruthless
    }

    /// <summary>
    /// The solo-practice opponent.
    ///
    /// It plays with the same physics the player does: it solves the friction equation for
    /// the launch speed that stops a stone on its chosen target, then adds aim and power
    /// error. That matters because a scripted "always lands on the button" opponent would
    /// feel unfair and teach the player nothing, whereas an opponent making human-shaped
    /// mistakes produces readable, beatable play.
    /// </summary>
    public class AiOpponent : MonoBehaviour
    {
        [Header("References")]
        public Rink Playfield;
        public StoneLauncher Launcher;
        public ModifierDirector Modifiers;

        [Header("Behaviour")]
        public AiSkill Skill = AiSkill.Sharp;

        [Tooltip("Pause before throwing, so the player can see the turn change.")]
        public float ThinkTime = 1.1f;

        [Tooltip("Residual speed aimed for when attempting a takeout, in m/s.")]
        public float TakeoutImpactSpeed = 1.25f;

        private void Start()
        {
            if (Playfield == null)
                Playfield = Rink.Instance != null ? Rink.Instance : FindFirstObjectByType<Rink>();
            if (Launcher == null) Launcher = FindFirstObjectByType<StoneLauncher>();
            if (Modifiers == null) Modifiers = FindFirstObjectByType<ModifierDirector>();
        }

        /// <summary>Plays one stone. Yields until the stone has been released.</summary>
        public IEnumerator TakeTurn(PlayerSide side)
        {
            yield return new WaitForSeconds(ThinkTime);

            if (Playfield == null || Launcher == null) yield break;

            Vector3 origin = Playfield.LaunchPointWorld(side);
            Vector3 aimPoint = ChooseAimPoint(side, out bool isTakeout);

            Vector3 toTarget = aimPoint - origin;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance < 0.05f) distance = 0.05f;

            float speed = SpeedToTravel(distance, isTakeout ? TakeoutImpactSpeed : 0f);
            Vector3 direction = toTarget.normalized;

            direction = ApplyCurrentCompensation(direction, distance);
            (direction, speed) = ApplyError(direction, speed);

            speed = Mathf.Clamp(speed, GameConfig.MinLaunchSpeed, GameConfig.MaxLaunchSpeed);

            Launcher.ThrowForAi(side, direction, speed);
        }

        // ---------------------------------------------------------------- decisions

        /// <summary>
        /// Picks a target. The priority is deliberately simple and legible: remove the
        /// opponent's scoring stone if there is one worth removing, otherwise draw to the
        /// centre.
        /// </summary>
        private Vector3 ChooseAimPoint(PlayerSide me, out bool isTakeout)
        {
            isTakeout = false;
            Vector3 center = Playfield.TargetCenterWorld;

            Stone theirBest = Playfield.ClosestStoneFor(me.Other());
            Stone myBest = Playfield.ClosestStoneFor(me);

            float theirDist = theirBest != null
                ? Playfield.DistanceToTarget(theirBest.transform.position)
                : float.PositiveInfinity;
            float myDist = myBest != null
                ? Playfield.DistanceToTarget(myBest.transform.position)
                : float.PositiveInfinity;

            bool theyAreScoring = theirDist <= GameConfig.RingRadii[0];
            bool theyAreBeatingMe = theirDist < myDist;

            if (theyAreScoring && theyAreBeatingMe && Skill != AiSkill.Friendly)
            {
                isTakeout = true;
                return theirBest.transform.position;
            }

            // Draw shot: aim at the centre, with a slight bias so stones do not all stack
            // on the exact same point and bounce off each other.
            Vector2 jitter = Random.insideUnitCircle * 0.022f;
            return center + Playfield.Surface.right * jitter.x + Playfield.Surface.forward * jitter.y;
        }

        /// <summary>
        /// Launch speed needed to travel a distance under constant-deceleration friction and
        /// arrive with a chosen residual speed: v = sqrt(vEnd^2 + 2*a*d).
        /// </summary>
        private float SpeedToTravel(float distance, float arrivalSpeed)
        {
            float a = GameConfig.IceFriction;
            return Mathf.Sqrt(arrivalSpeed * arrivalSpeed + 2f * a * distance);
        }

        /// <summary>Aims upstream when the current modifier is pushing stones sideways.</summary>
        private Vector3 ApplyCurrentCompensation(Vector3 direction, float distance)
        {
            var current = Modifiers != null ? Modifiers.Current as CurrentModifier : null;
            if (current == null || !current.IsActive) return direction;

            // Rough correction: lean against the flow, more so on longer shots.
            float lean = Mathf.Clamp(distance * 0.16f, 0f, 0.22f);
            if (Skill == AiSkill.Friendly) lean *= 0.35f;

            Vector3 corrected = direction - current.FlowDirection.normalized * lean;
            corrected.y = 0f;
            return corrected.normalized;
        }

        private (Vector3 direction, float speed) ApplyError(Vector3 direction, float speed)
        {
            float aimDegrees;
            float powerPercent;

            switch (Skill)
            {
                case AiSkill.Friendly: aimDegrees = 6.5f; powerPercent = 0.16f; break;
                case AiSkill.Ruthless: aimDegrees = 1.2f; powerPercent = 0.035f; break;
                default: aimDegrees = 3.0f; powerPercent = 0.08f; break;
            }

            float angle = Random.Range(-aimDegrees, aimDegrees);
            Vector3 up = Playfield != null ? Playfield.Surface.up : Vector3.up;
            Vector3 jittered = Quaternion.AngleAxis(angle, up) * direction;

            float scaled = speed * (1f + Random.Range(-powerPercent, powerPercent));
            return (jittered.normalized, scaled);
        }
    }
}
