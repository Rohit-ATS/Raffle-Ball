using System.Collections.Generic;
using UnityEngine;
using RiftRally.Core;

namespace RiftRally.Gameplay
{
    /// <summary>
    /// Owns the playfield. Everything spatial in the game is expressed relative to this
    /// transform, so the whole rink can be re-anchored onto a real table without any
    /// gameplay code caring where it ended up.
    ///
    /// Local space convention:
    ///   X = across the rink (left/right), Z = down the rink (near -> far), Y = up.
    ///   The origin sits at the centre of the playing surface.
    /// </summary>
    public class Rink : MonoBehaviour
    {
        public static Rink Instance { get; private set; }

        [Header("Scene references")]
        public Transform Surface;
        public Transform TargetCenter;

        private readonly List<Stone> _stones = new();

        /// <summary>Every stone currently alive on the rink (thrown or in hand).</summary>
        public IReadOnlyList<Stone> Stones => _stones;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            if (Surface == null) Surface = transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>Height of the playing surface in world space.</summary>
        public float SurfaceY => Surface.position.y;

        /// <summary>Y position a stone's centre should sit at while sliding.</summary>
        public float StoneCenterY => SurfaceY + GameConfig.StoneHeight * 0.5f;

        /// <summary>Converts a rink-local (x, z) pair into a world point on the surface.</summary>
        public Vector3 RinkToWorld(float localX, float localZ, float heightAboveSurface = 0f)
            => Surface.TransformPoint(new Vector3(localX, heightAboveSurface, localZ));

        /// <summary>Converts a world point into rink-local coordinates.</summary>
        public Vector3 WorldToRink(Vector3 world) => Surface.InverseTransformPoint(world);

        /// <summary>World position of the scoring target centre.</summary>
        public Vector3 TargetCenterWorld
            => TargetCenter != null
                ? TargetCenter.position
                : RinkToWorld(0f, GameConfig.RinkLength * 0.5f - GameConfig.TargetFromFarEnd);

        /// <summary>World position stones are launched from for the given player.</summary>
        public Vector3 LaunchPointWorld(PlayerSide side)
        {
            float x = side == PlayerSide.One ? -GameConfig.RinkWidth * 0.18f
                                             : GameConfig.RinkWidth * 0.18f;
            float z = -GameConfig.RinkLength * 0.5f + GameConfig.LaunchLineFromNearEnd;
            return RinkToWorld(x, z, GameConfig.StoneHeight * 0.5f);
        }

        /// <summary>Horizontal distance from a world point to the target centre.</summary>
        public float DistanceToTarget(Vector3 world)
        {
            Vector3 a = WorldToRink(world);
            Vector3 b = WorldToRink(TargetCenterWorld);
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Scoring ring a distance falls in: 3 = bullseye, 1 = outermost ring, 0 = outside.
        /// </summary>
        public int RingValueFor(float distanceToTarget)
        {
            var radii = GameConfig.RingRadii;           // outermost first
            for (int i = radii.Length - 1; i >= 0; i--)
            {
                if (distanceToTarget <= radii[i]) return i + 1;
            }
            return distanceToTarget <= radii[0] ? 1 : 0;
        }

        /// <summary>True while a stone is still on the playing surface.</summary>
        public bool IsInsideBounds(Vector3 world)
        {
            Vector3 local = WorldToRink(world);
            float halfW = GameConfig.RinkWidth * 0.5f + GameConfig.StoneRadius;
            float halfL = GameConfig.RinkLength * 0.5f + GameConfig.StoneRadius;
            return Mathf.Abs(local.x) <= halfW
                && Mathf.Abs(local.z) <= halfL
                && local.y > -0.25f;
        }

        /// <summary>Direction, in world space, pointing down the rink towards the target.</summary>
        public Vector3 DownRink => Surface.forward;

        // ---------------------------------------------------------------- stone registry

        public void Register(Stone stone)
        {
            if (stone != null && !_stones.Contains(stone)) _stones.Add(stone);
        }

        public void Unregister(Stone stone)
        {
            if (stone != null) _stones.Remove(stone);
        }

        /// <summary>True once every thrown stone has stopped moving (or died).</summary>
        public bool AllStonesSettled()
        {
            for (int i = 0; i < _stones.Count; i++)
            {
                var s = _stones[i];
                if (s == null) continue;
                if (s.State == StoneState.InFlight || s.State == StoneState.Held) return false;
            }
            return true;
        }

        /// <summary>The scoring stone closest to the target, or null if the rink is empty.</summary>
        public Stone ClosestScoringStone()
        {
            Stone best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _stones.Count; i++)
            {
                var s = _stones[i];
                if (s == null || !s.CountsForScoring) continue;
                float d = DistanceToTarget(s.transform.position);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        /// <summary>Closest scoring stone belonging to one player, or null.</summary>
        public Stone ClosestStoneFor(PlayerSide side)
        {
            Stone best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _stones.Count; i++)
            {
                var s = _stones[i];
                if (s == null || !s.CountsForScoring || s.Owner != side) continue;
                float d = DistanceToTarget(s.transform.position);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        /// <summary>Removes every stone from the rink. Called between rounds.</summary>
        public void ClearStones()
        {
            for (int i = _stones.Count - 1; i >= 0; i--)
            {
                var s = _stones[i];
                if (s != null) Destroy(s.gameObject);
            }
            _stones.Clear();
        }
    }
}
