/**
 * Room and match-flow rules.
 *
 * The room is where the fairness of the game lives: whose turn it is, who gets the hammer,
 * what happens when someone walks out. All of it is tested without a socket in sight, which
 * is the point of keeping Room free of transport concerns.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  ROOM_STATE,
  Room,
  RoomRegistry,
  decideWinner,
  deterministicModifier,
  generateRoomCode,
  sanitizeStones,
} from '../src/rooms.js';
import { STONES_PER_PLAYER, TARGET_X, TARGET_Z } from '../src/scoring.js';

function player(id, name = id) {
  return { id, displayName: name, kind: 'guest', rating: 1000 };
}

function stoneAt(seat, distance, angleDegrees = 0) {
  const radians = (angleDegrees * Math.PI) / 180;
  return {
    seat,
    x: TARGET_X + Math.cos(radians) * distance,
    z: TARGET_Z + Math.sin(radians) * distance,
  };
}

/** Plays a whole round, with `winnerSeat` ending up closest. */
function playRound(room, winnerSeat) {
  const total = STONES_PER_PLAYER * 2;
  for (let i = 0; i < total; i++) {
    const seat = room.turnSeat;
    const last = i === total - 1;

    // Only the designated winner ever gets a stone near the button.
    const stones = last
      ? [stoneAt(winnerSeat, 0.02), stoneAt(winnerSeat === 0 ? 1 : 0, 0.12)]
      : [stoneAt(winnerSeat, 0.02)];

    const result = room.submitThrow({
      playerId: room.occupant(seat).player.id,
      launch: { speed: 1.2, dirZ: 1 },
      stones,
    });

    if (result.roundComplete) return result;
  }
  return null;
}

test('room codes avoid ambiguous characters', () => {
  for (let i = 0; i < 400; i++) {
    const code = generateRoomCode();
    assert.equal(code.length, 4);
    assert.match(code, /^[A-Z0-9]{4}$/);
    assert.ok(
      !/[BILOSZ01258]/.test(code),
      `code ${code} contains a character confusable with another`,
    );
  }
});

test('a room seats two players and refuses a third', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  assert.equal(room.join(player('a')), 0);
  assert.equal(room.join(player('b')), 1);
  assert.equal(room.isFull, true);

  assert.throws(() => room.join(player('c')), /two players/);
});

test('rejoining returns the same seat instead of erroring', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  assert.equal(room.join(player('a')), 0);
  assert.equal(room.playerCount, 1);
});

test('a match needs two players before it can start', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  assert.throws(() => room.start(), /two players/);
});

test('the player without the hammer throws first', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  assert.equal(room.hammer, 1);
  assert.equal(room.turnSeat, 0, 'seat 0 throws first because seat 1 holds the hammer');
});

test('turns alternate and a player cannot throw twice in a row', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  const first = room.turnSeat;
  room.submitThrow({ playerId: room.occupant(first).player.id, launch: {}, stones: [] });
  assert.equal(room.turnSeat, first === 0 ? 1 : 0);

  assert.throws(
    () => room.submitThrow({ playerId: room.occupant(first).player.id, launch: {}, stones: [] }),
    /not your turn/,
  );
});

test('someone outside the room cannot throw into it', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  assert.throws(
    () => room.submitThrow({ playerId: 'intruder', launch: {}, stones: [] }),
    /not in that room/,
  );
});

test('throws are refused before the match starts', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));

  assert.throws(() => room.submitThrow({ playerId: 'a', launch: {}, stones: [] }), /not in progress/);
});

test('a round ends after six stones and is scored by the server', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  const result = playRound(room, 0);

  assert.ok(result, 'the round should have completed');
  assert.equal(result.roundComplete, true);
  assert.equal(result.roundResult.winnerSeat, 0);
  assert.ok(result.roundResult.points >= 1);
  assert.equal(room.roundsWon[0], 1);
});

test('losing a round hands over the hammer', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  playRound(room, 0);
  assert.equal(room.hammer, 1, 'seat 1 lost, so seat 1 throws last next round');
  assert.equal(room.turnSeat, 0);
});

test('a match ends once someone has won enough rounds', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  playRound(room, 0);
  const second = playRound(room, 0);

  assert.equal(second.matchOver, true);
  assert.equal(room.state, ROOM_STATE.FINISHED);
  assert.equal(room.winnerSeat, 0);
  assert.equal(room.endedReason, 'completed');
});

test('a finished room refuses new players', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();
  room.forfeit(1, 'left');

  room.leave('b');
  assert.throws(() => room.join(player('c')), /already finished/);
});

test('walking out of a live match awards it to the other player', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  room.forfeit(0, 'left');
  assert.equal(room.winnerSeat, 1);
  assert.equal(room.state, ROOM_STATE.FINISHED);
  assert.equal(room.endedReason, 'left');
});

test('round one is clean and later rounds get a modifier', () => {
  const room = new Room({ code: 'AAAA', hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();

  assert.equal(room.snapshot().modifier, -1, 'round one teaches the plain throw');

  playRound(room, 0);
  const later = room.snapshot().modifier;
  assert.ok(later >= 0 && later <= 2, 'round two should name one of the three modifiers');
});

test('both clients derive the same modifier from the same seed', () => {
  for (let round = 2; round <= 6; round++) {
    const a = deterministicModifier(12345, round);
    const b = deterministicModifier(12345, round);
    assert.equal(a, b);
    assert.ok(a >= 0 && a <= 2, 'there are three modifiers');
  }

  // Different seeds should not produce identical sequences, or every match would be the same.
  const first = [2, 3, 4].map((r) => deterministicModifier(1, r)).join('');
  const second = [2, 3, 4].map((r) => deterministicModifier(999, r)).join('');
  assert.notEqual(first, second);
});

test('rounds won decides the match, and points break a tie', () => {
  assert.equal(decideWinner([2, 1], [0, 0]), 0);
  assert.equal(decideWinner([1, 2], [0, 0]), 1);
  assert.equal(decideWinner([1, 1], [3, 5]), 1, 'level on rounds, so points decide');
  assert.equal(decideWinner([1, 1], [5, 3]), 0);
});

test('stone lists are sanitised before being stored', () => {
  const cleaned = sanitizeStones([
    { seat: 0, x: 0.1, z: 0.2, id: 'ok' },
    { seat: 9, x: 0.1, z: 0.2 },            // seat clamped to a real seat
    { seat: 1, x: 'nonsense', z: 0.2 },      // dropped
    { seat: 1, x: 99, z: -99 },              // clamped into the rink
    null,
    'not a stone',
  ]);

  assert.equal(cleaned.length, 3);
  assert.equal(cleaned[1].seat, 0, 'an out-of-range seat falls back to seat 0');
  assert.ok(cleaned[2].x <= 1.5 && cleaned[2].z >= -2.5, 'positions are clamped');
});

test('a flood of stones cannot be used to grow the room state without limit', () => {
  const many = Array.from({ length: 500 }, (_, i) => ({ seat: i % 2, x: 0, z: 0 }));
  assert.ok(sanitizeStones(many).length <= STONES_PER_PLAYER * 2 + 2);
});

// ---------------------------------------------------------------- registry

test('quick match pairs the second player into the first waiting room', () => {
  const registry = new RoomRegistry();

  const first = registry.quickMatch(player('a'));
  assert.equal(first.created, true);
  assert.equal(first.seat, 0);

  const second = registry.quickMatch(player('b'));
  assert.equal(second.created, false);
  assert.equal(second.seat, 1);
  assert.equal(second.room.code, first.room.code);
  assert.equal(registry.size, 1, 'both players should be in one room');
});

test('quick match never pairs a player with themselves', () => {
  const registry = new RoomRegistry();
  const first = registry.quickMatch(player('a'));
  const again = registry.quickMatch(player('a'));
  assert.equal(again.room.code, first.room.code);
  assert.equal(registry.size, 1);
});

test('quick match ignores private rooms', () => {
  const registry = new RoomRegistry();
  const private_ = registry.create({ hostId: 'host', isPublic: false });
  private_.join(player('host'));

  const result = registry.quickMatch(player('b'));
  assert.notEqual(result.room.code, private_.code);
  assert.equal(result.created, true);
});

test('rooms are found case-insensitively, because codes get typed by hand', () => {
  const registry = new RoomRegistry();
  const room = registry.create({ hostId: 'a' });
  assert.equal(registry.get(room.code.toLowerCase())?.code, room.code);
  assert.equal(registry.get(` ${room.code} `)?.code, room.code);
  assert.equal(registry.get('ZZZZ'), null);
  assert.equal(registry.get(undefined), null);
});

test('a disconnected player keeps their seat until the grace window expires', () => {
  let now = 1000;
  const registry = new RoomRegistry({ now: () => now, reconnectGraceMs: 5000 });

  const room = registry.create({ hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();
  room.markConnected(1, false);

  now += 2000;
  assert.equal(registry.sweep().forfeited.length, 0, 'still inside the grace window');
  assert.equal(room.state, ROOM_STATE.PLAYING);

  now += 4000;
  const swept = registry.sweep();
  assert.equal(swept.forfeited.length, 1);
  assert.equal(room.winnerSeat, 0, 'the player still present wins');
  assert.equal(room.endedReason, 'disconnect');
});

test('reconnecting inside the window cancels the forfeit', () => {
  let now = 1000;
  const registry = new RoomRegistry({ now: () => now, reconnectGraceMs: 5000 });

  const room = registry.create({ hostId: 'a' });
  room.join(player('a'));
  room.join(player('b'));
  room.start();
  room.markConnected(1, false);

  now += 2000;
  room.markConnected(1, true);
  now += 10_000;

  const swept = registry.sweep();
  assert.equal(swept.forfeited.length, 0);
  assert.equal(room.state, ROOM_STATE.PLAYING);
});

test('empty and stale rooms are reaped', () => {
  let now = 1000;
  const registry = new RoomRegistry({ now: () => now, idleTimeoutMs: 10_000 });

  const empty = registry.create({ hostId: 'a' });
  assert.equal(registry.size, 1);
  registry.sweep();
  assert.equal(registry.size, 0, 'a room nobody joined should not linger');

  const stale = registry.create({ hostId: 'b' });
  stale.join(player('b'));
  now += 60_000;
  registry.sweep();
  assert.equal(registry.size, 0, 'an abandoned room should be reaped');
  assert.ok(stale);
  assert.ok(empty);
});
