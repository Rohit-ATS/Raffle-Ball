/**
 * Scoring parity.
 *
 * These cases mirror the Unity ScoringTests one for one. The server and the client each have
 * their own implementation of the curling count, and the leaderboard depends on them agreeing,
 * so the test suites are kept deliberately parallel: if someone changes the rule on one side,
 * the matching test on the other side is what catches it.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  RING_RADII,
  SCORING_RADIUS,
  TARGET_X,
  TARGET_Z,
  distanceToTarget,
  ringValueFor,
  scoreRink,
} from '../src/scoring.js';

/** Places a stone a given distance from the centre, at an angle around it. */
function stoneAt(seat, distance, angleDegrees = 0, id = undefined) {
  const radians = (angleDegrees * Math.PI) / 180;
  return {
    id: id ?? `${seat}-${distance}-${angleDegrees}`,
    seat,
    x: TARGET_X + Math.cos(radians) * distance,
    z: TARGET_Z + Math.sin(radians) * distance,
  };
}

test('an empty rink is a blank round', () => {
  const result = scoreRink([]);
  assert.equal(result.blank, true);
  assert.equal(result.points, 0);
  assert.equal(result.winnerSeat, -1, 'no winner is -1, never null: see the note in scoring.js');
});

test('a missing stone list is treated as empty rather than throwing', () => {
  assert.equal(scoreRink(undefined).blank, true);
  assert.equal(scoreRink(null).blank, true);
});

test('stones outside the house do not score', () => {
  const result = scoreRink([stoneAt(0, SCORING_RADIUS + 0.05)]);
  assert.equal(result.blank, true);
});

test('a stone exactly on the outer ring still counts', () => {
  const result = scoreRink([stoneAt(0, SCORING_RADIUS)]);
  assert.equal(result.blank, false);
  assert.equal(result.winnerSeat, 0);
});

test('the closest stone takes the round', () => {
  const result = scoreRink([stoneAt(0, 0.02), stoneAt(1, 0.08)]);
  assert.equal(result.winnerSeat, 0);
  assert.equal(result.points, 1);
});

test('every winning stone closer than the opponent scores a point', () => {
  const result = scoreRink([
    stoneAt(0, 0.02),
    stoneAt(0, 0.04, 90),
    stoneAt(0, 0.06, 180),
    stoneAt(1, 0.09),
  ]);
  assert.equal(result.winnerSeat, 0);
  assert.equal(result.points, 3);
});

test('an opponent stone in between caps the count', () => {
  const result = scoreRink([stoneAt(0, 0.02), stoneAt(1, 0.05), stoneAt(0, 0.08, 120)]);
  assert.equal(result.winnerSeat, 0);
  assert.equal(result.points, 1, 'the second stone is behind an opponent stone');
});

test('sweeping the house scores every stone', () => {
  const result = scoreRink([stoneAt(1, 0.01), stoneAt(1, 0.05, 90), stoneAt(1, 0.1, 200)]);
  assert.equal(result.winnerSeat, 1);
  assert.equal(result.points, 3);
});

test('the winner always scores at least one', () => {
  const result = scoreRink([stoneAt(0, 0.03)]);
  assert.ok(result.points >= 1);
});

test('ring values fall off from the centre', () => {
  assert.equal(ringValueFor(0), 3);
  assert.equal(ringValueFor(RING_RADII[2] - 0.001), 3);
  assert.equal(ringValueFor(RING_RADII[1] - 0.001), 2);
  assert.equal(ringValueFor(RING_RADII[0] - 0.001), 1);
  assert.equal(ringValueFor(RING_RADII[0] + 0.001), 0);
});

test('ring radii are listed outermost first', () => {
  for (let i = 1; i < RING_RADII.length; i++) {
    assert.ok(RING_RADII[i] < RING_RADII[i - 1]);
  }
});

test('distance is measured from the target, not the rink origin', () => {
  assert.ok(TARGET_Z > 0.4, 'the house sits down the rink, not at the middle');
  assert.equal(distanceToTarget({ x: TARGET_X, z: TARGET_Z }), 0);
  assert.ok(Math.abs(distanceToTarget({ x: TARGET_X, z: 0 }) - TARGET_Z) < 1e-9);
});

test('a one millimetre lead decides the round', () => {
  // The case most likely to diverge between two implementations, and the one players argue
  // about, so it is pinned explicitly.
  const result = scoreRink([stoneAt(0, 0.0500), stoneAt(1, 0.0501)]);
  assert.equal(result.winnerSeat, 0);
  assert.equal(result.points, 1);
});

test('malformed stones are ignored rather than crashing the round', () => {
  const result = scoreRink([
    stoneAt(0, 0.02),
    { seat: 1, x: Number.NaN, z: 0 },
  ]);
  assert.equal(result.winnerSeat, 0);
  // The NaN stone's distance is NaN, which fails the radius test, so it never enters the house.
  assert.equal(result.inHouse, 1);
});
