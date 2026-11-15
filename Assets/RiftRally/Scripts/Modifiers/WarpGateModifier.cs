using System.Collections.Generic;
using UnityEngine;
using RiftRally.Core;
using RiftRally.Gameplay;

namespace RiftRally.Modifiers
{
    /// <summary>
    /// A pair of linked gates. A stone that slides into one gate comes out of the other.
    ///
    /// The exit direction is deliberately NOT a reflection of the entry angle: a stone always
    /// leaves along the exit gate's forward axis, keeping its speed. That makes the gate a
    /// tool the player can aim with rather than a dice roll, which is the whole point. A
    /// player who cannot predict the outcome will simply avoid the gate.
    /// </summary>
    public class WarpGateModifier : RinkModifier
    {
        [Header("Gates")]
        public Transform GateA;
        public Transform GateB;

        [Tooltip("How close a stone's centre must get to a gate to be taken.")]
        public float CaptureRadius = 0.055f;

        [Tooltip("Speed boost applied on exit. 1 = preserve speed exactly.")]
        [Range(0.6f, 1.4f)]
        public float ExitSpeedScale = 1.0f;

        [Header("Feedback")]
        public ParticleSystem EnterBurst;
        public ParticleSystem ExitBurst;
        public AudioSource WarpAudio;

        /// <summary>Raised when a stone warps, for audio and FX. Argument: exit position.</summary>
        public event System.Action<Vector3> Warped;

        // Stones are briefly immune after a warp so they cannot ping-pong between gates.
        private readonly Dictionary<Stone, float> _cooldowns = new();
        private const float CooldownSeconds = 0.4f;

        protected override void OnActivated()
        {
            _cooldowns.Clear();
        }

        protected override void Tick()
        {
            if (Playfield == null || GateA == null || GateB == null) return;

            var stones = Playfield.Stones;
            for (int i = 0; i < stones.Count; i++)
            {
                Stone s = stones[i];
                if (s == null || s.State != StoneState.InFlight) continue;

                if (_cooldowns.TryGetValue(s, out float until) && Time.time < until) continue;

                if (TryCapture(s, GateA, GateB)) continue;
                TryCapture(s, GateB, GateA);
            }
        }

        private bool TryCapture(Stone stone, Transform entry, Transform exit)
        {
            Vector3 toStone = stone.transform.position - entry.position;
            toStone.y = 0f;

            if (toStone.sqrMagnitude > CaptureRadius * CaptureRadius) return false;

            // Only take the stone if it is actually travelling into the gate, so a stone
            // resting beside a gate is never swallowed.
            if (Vector3.Dot(stone.Velocity.normalized, -toStone.normalized) < 0.15f) return false;

            Vector3 exitDir = exit.forward;
            exitDir.y = 0f;
            if (exitDir.sqrMagnitude < 0.0001f) exitDir = Playfield.DownRink;

            Vector3 exitPos = exit.position + exitDir.normalized * (GameConfig.StoneRadius * 1.6f);

            if (EnterBurst != null)
            {
                EnterBurst.transform.position = entry.position;
                EnterBurst.Play();
            }

            stone.WarpTo(exitPos, exitDir);

            if (ExitSpeedScale != 1f)
            {
                float speed = stone.Speed * ExitSpeedScale;
                stone.Launch(exitDir.normalized * speed, 0f);
            }

            if (ExitBurst != null)
            {
                ExitBurst.transform.position = exitPos;
                ExitBurst.Play();
            }

            if (WarpAudio != null) WarpAudio.Play();

            _cooldowns[stone] = Time.time + CooldownSeconds;
            Warped?.Invoke(exitPos);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (GateA == null || GateB == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(GateA.position, CaptureRadius);
            Gizmos.DrawWireSphere(GateB.position, CaptureRadius);
            Gizmos.DrawLine(GateA.position, GateB.position);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(GateB.position, GateB.forward * 0.12f);
            Gizmos.DrawRay(GateA.position, GateA.forward * 0.12f);
        }
    }
}
