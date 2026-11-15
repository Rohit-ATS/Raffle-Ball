using System.Collections.Generic;
using UnityEngine;
using RiftRally.Gameplay;

namespace RiftRally.Core
{
    /// <summary>Outcome of scoring a single round.</summary>
    public struct RoundResult
    {
        public bool IsBlank;            // nobody had a stone in the scoring area
        public PlayerSide Winner;
        public int Points;
        public Stone ClosestStone;
        public float ClosestDistance;

        public static RoundResult Blank => new RoundResult { IsBlank = true, Points = 0 };
    }

    /// <summary>
    /// Scores a round using curling's rule, which is what makes the game tactical:
    /// only the player with the closest stone scores, and they score one point for every
    /// stone of theirs that is closer than the opponent's best stone.
    ///
    /// That single rule is why knocking an opponent's stone out is worth a whole turn, and
    /// why guarding your own stone matters. Simple sum-of-rings scoring would remove all of it.
    /// </summary>
    public static class ScoreKeeper
    {
        /// <summary>Stones further than this from the centre do not count at all.</summary>
        public static float ScoringRadius => GameConfig.RingRadii[0];

        public static RoundResult ScoreRound(Rink rink)
        {
            if (rink == null) return RoundResult.Blank;

            var inHouse = new List<(Stone stone, float dist)>();

            foreach (var s in rink.Stones)
            {
                if (s == null || !s.CountsForScoring) continue;
                float d = rink.DistanceToTarget(s.transform.position);
                if (d <= ScoringRadius) inHouse.Add((s, d));
            }

            if (inHouse.Count == 0) return RoundResult.Blank;

            inHouse.Sort((a, b) => a.dist.CompareTo(b.dist));

            Stone best = inHouse[0].stone;
            PlayerSide winner = best.Owner;

            // Distance of the opponent's closest stone, or infinity if they have none.
            float opponentBest = float.PositiveInfinity;
            for (int i = 0; i < inHouse.Count; i++)
            {
                if (inHouse[i].stone.Owner != winner)
                {
                    opponentBest = inHouse[i].dist;
                    break;
                }
            }

            // One point per winning stone closer than the opponent's best.
            int points = 0;
            for (int i = 0; i < inHouse.Count; i++)
            {
                if (inHouse[i].stone.Owner == winner && inHouse[i].dist < opponentBest) points++;
            }

            points = Mathf.Max(1, points);

            return new RoundResult
            {
                IsBlank = false,
                Winner = winner,
                Points = points,
                ClosestStone = best,
                ClosestDistance = inHouse[0].dist
            };
        }

        /// <summary>
        /// True when a thrown stone just stole the lead, i.e. it became the closest stone and
        /// its owner was not previously winning. Used to trigger the big celebration.
        /// </summary>
        public static bool IsSteal(Rink rink, Stone thrown, PlayerSide previousLeader, bool hadLeader)
        {
            if (rink == null || thrown == null) return false;
            var closest = rink.ClosestScoringStone();
            if (closest == null) return false;
            if (closest.Owner != thrown.Owner) return false;
            return !hadLeader || previousLeader != thrown.Owner;
        }
    }
}
