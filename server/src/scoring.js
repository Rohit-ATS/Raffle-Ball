/**
 * Server-side scoring.
 *
 * This is a deliberate duplicate of the client's ScoreKeeper. Sharing one implementation
 * would mean either running the game's C# on the server or shipping JavaScript into Unity,
 * and neither is worth it for forty lines of arithmetic. What matters is that the two stay in
 * agreement, so the constants live here as named exports, the rule is written out in the same
 * terms, and the server test suite covers the same cases as the Unity test suite - including
 * the ones that only differ by a millimetre.
 *
 * The reason the server scores at all is that the client cannot be trusted to report its own
 * points. It is trusted to report where the stones stopped (see rooms.js), but turning
 * positions into a score is the step that decides the leaderboard, so it happens here.
 */

/** Must match GameConfig.RingRadii, outermost first. */
export const RING_RADII = [0.17, 0.11, 0.055];

/** Must match GameConfig.StonesPerPlayerPerRound. */
export const STONES_PER_PLAYER = 3;

/** Must match GameConfig.RoundsToWinMatch. */
export const ROUNDS_TO_WIN = 2;

/** Must match GameConfig.MaxRounds. */
export const MAX_ROUNDS = 3;

/** Stones further than this from the centre are out of the house and score nothing. */
export const SCORING_RADIUS = RING_RADII[0];

/** The target centre in rink-local metres; must match the client's rink layout. */
export const TARGET_Z = 0.8 - 0.34;
export const TARGET_X = 0;

export function distanceToTarget(stone) {
  const dx = stone.x - TARGET_X;
  const dz = stone.z - TARGET_Z;
  return Math.sqrt(dx * dx + dz * dz);
}

/**
 * Scores a rink with curling's rule: only the side holding the closest stone scores, and it
 * scores one point for every stone of its own that is closer than the other side's best.
 *
 * That single rule is the whole tactical game - it is why knocking a stone out is worth a
 * turn, and why guarding one matters - so it is also the thing most worth getting exactly
 * right on both sides of the wire.
 */
export function scoreRink(stones) {
  const inHouse = [];

  for (const stone of stones ?? []) {
    const distance = distanceToTarget(stone);
    if (distance <= SCORING_RADIUS) inHouse.push({ ...stone, distance });
  }

  if (inHouse.length === 0) {
    // -1 rather than null for the seat: the Unity client parses these messages with
    // JsonUtility, which decodes a JSON null into 0 for an int field. A null winner would
    // therefore arrive as "seat 0 won", so the absence of a winner is sent as a value that
    // cannot be mistaken for a real seat.
    return { blank: true, winnerSeat: -1, points: 0, closestDistance: 0, inHouse: 0 };
  }

  inHouse.sort((a, b) => a.distance - b.distance);

  const winnerSeat = inHouse[0].seat;

  // The other side's best stone, or infinity when they have none in the house.
  let opponentBest = Infinity;
  for (const stone of inHouse) {
    if (stone.seat !== winnerSeat) {
      opponentBest = stone.distance;
      break;
    }
  }

  let points = 0;
  for (const stone of inHouse) {
    if (stone.seat === winnerSeat && stone.distance < opponentBest) points += 1;
  }

  return {
    blank: false,
    winnerSeat,
    points: Math.max(1, points),
    closestDistance: inHouse[0].distance,
    inHouse: inHouse.length,
  };
}

/** Scoring ring a distance falls in: 3 is the bullseye, 1 the outer ring, 0 outside. */
export function ringValueFor(distance) {
  for (let i = RING_RADII.length - 1; i >= 0; i--) {
    if (distance <= RING_RADII[i]) return i + 1;
  }
  return 0;
}
