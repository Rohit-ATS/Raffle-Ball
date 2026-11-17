/**
 * Rooms, matchmaking and the authoritative turn order.
 *
 * ## Why this is a turn relay rather than a physics server
 *
 * Rift Rally is strictly turn based: one player throws, every stone comes to rest, then the
 * other player throws. That changes the netcode problem completely. There is no need to
 * reconcile two players moving at once, no need for rollback, and no need to run the physics
 * twice. The thrower simulates their own shot locally - so their own throw is perfectly
 * responsive, which is the one thing that must never feel laggy - and then reports where
 * every stone ended up. The opponent replays the throw for the look of it and then adopts the
 * reported positions as truth.
 *
 * The consequence worth being explicit about: the thrower is trusted for stone positions.
 * A modified client could lie about where its stones stopped. What it cannot do is award
 * itself points, because the *server* scores the rink from those positions using the same
 * curling rule the client uses, and the server owns the turn order, the round count and the
 * match result. That is the right trade for a hackathon game with a public leaderboard: it
 * removes the trivial "post a fake score" attack, which is the one that actually gets used,
 * without the cost of a full authoritative simulation.
 *
 * ## Why disconnects end rounds rather than pausing them
 *
 * A dropped headset is common - battery, guardian, someone taking it off. The room keeps a
 * short reconnect window, and after it expires the match is awarded to the player still
 * present. Leaving a stalled match open is worse than resolving it: the other player is stuck
 * in an empty room with no way to finish, and their rating never settles.
 */

import { randomUUID } from 'node:crypto';
import { scoreRink, STONES_PER_PLAYER, ROUNDS_TO_WIN, MAX_ROUNDS } from './scoring.js';

/**
 * Unambiguous alphabet. Every confusable pair is dropped in full, not half of it: B/8, I/1/L,
 * O/0, S/5 and Z/2 all go. Room codes get read aloud over voice chat and typed on a floating
 * keyboard with hand tracking, where a single misread character means "no room with that
 * code" and the player assumes the game is broken. 25 characters still gives 390,625 codes.
 */
const CODE_ALPHABET = 'ACDEFGHJKMNPQRTUVWXY34679';
const CODE_LENGTH = 4;

export const ROOM_STATE = {
  WAITING: 'waiting',
  PLAYING: 'playing',
  FINISHED: 'finished',
};

export function generateRoomCode(random = Math.random) {
  let code = '';
  for (let i = 0; i < CODE_LENGTH; i++) {
    code += CODE_ALPHABET[Math.floor(random() * CODE_ALPHABET.length)];
  }
  return code;
}

/**
 * One 1v1 match. Owns the turn order and the score; knows nothing about sockets, which is
 * what makes the whole thing testable without a network.
 */
export class Room {
  constructor({ code, hostId, isPublic = false, now = () => Date.now() }) {
    this.code = code;
    this.id = randomUUID();
    this.hostId = hostId;
    this.isPublic = isPublic;
    this.state = ROOM_STATE.WAITING;
    this.now = now;
    this.createdAt = now();

    /** Seat 0 and seat 1 map to PlayerSide.One and PlayerSide.Two in the client. */
    this.seats = [null, null];

    this.round = 0;
    this.roundsWon = [0, 0];
    this.totalPoints = [0, 0];
    this.hammer = 1;           // seat that throws last in the current round
    this.turnSeat = 0;
    this.stonesThrown = 0;
    this.stones = [];          // [{ id, seat, x, z }] in rink-local metres
    this.seed = Math.floor(Math.random() * 2 ** 31);
    this.modifierByRound = {};
    this.winnerSeat = -1;
    this.endedReason = null;
    this.lastActivityAt = this.createdAt;
    this.disconnectedAt = [null, null];
  }

  get playerCount() {
    return this.seats.filter(Boolean).length;
  }

  get isFull() {
    return this.playerCount >= 2;
  }

  seatOf(playerId) {
    const index = this.seats.findIndex((s) => s && s.player.id === playerId);
    return index === -1 ? null : index;
  }

  occupant(seat) {
    return this.seats[seat];
  }

  opponentSeat(seat) {
    return seat === 0 ? 1 : 0;
  }

  /** Adds a player, returning their seat. Rejoining an existing seat is not an error. */
  join(player) {
    const existing = this.seatOf(player.id);
    if (existing !== null) {
      this.disconnectedAt[existing] = null;
      this.touch();
      return existing;
    }

    if (this.state === ROOM_STATE.FINISHED) {
      throw roomError('ROOM_FINISHED', 'that match has already finished');
    }
    if (this.isFull) {
      throw roomError('ROOM_FULL', 'that room already has two players');
    }

    const seat = this.seats[0] ? 1 : 0;
    this.seats[seat] = { player, ready: false, connected: true };
    this.touch();
    return seat;
  }

  leave(playerId) {
    const seat = this.seatOf(playerId);
    if (seat === null) return null;
    this.seats[seat] = null;
    this.touch();
    return seat;
  }

  markConnected(seat, connected) {
    const occupant = this.seats[seat];
    if (!occupant) return;
    occupant.connected = connected;
    this.disconnectedAt[seat] = connected ? null : this.now();
    this.touch();
  }

  setReady(playerId, ready = true) {
    const seat = this.seatOf(playerId);
    if (seat === null) throw roomError('NOT_IN_ROOM', 'you are not in that room');
    this.seats[seat].ready = ready;
    this.touch();
    return seat;
  }

  get bothReady() {
    return this.isFull && this.seats.every((s) => s && s.ready && s.connected);
  }

  /** Starts the match. The player without the hammer throws first. */
  start() {
    if (!this.isFull) throw roomError('NEED_TWO_PLAYERS', 'a match needs two players');
    if (this.state === ROOM_STATE.PLAYING) return this.snapshot();

    this.state = ROOM_STATE.PLAYING;
    this.round = 0;
    this.roundsWon = [0, 0];
    this.totalPoints = [0, 0];
    this.hammer = 1;
    this.beginRound();
    return this.snapshot();
  }

  beginRound() {
    this.round += 1;
    this.stones = [];
    this.stonesThrown = 0;
    this.turnSeat = this.opponentSeat(this.hammer);

    // Round one is always clean, matching the client's modifier director, so both sides
    // agree on what the rink looks like without the modifier itself being networked.
    this.modifierByRound[this.round] =
      this.round <= 1 ? null : deterministicModifier(this.seed, this.round);

    this.touch();
  }

  /**
   * Records a throw. `stones` is the full resting state of the rink as the thrower saw it,
   * which is simpler and more robust than sending deltas: a dropped message cannot leave the
   * two clients holding different rinks.
   */
  submitThrow({ playerId, launch, stones }) {
    if (this.state !== ROOM_STATE.PLAYING) {
      throw roomError('NOT_PLAYING', 'that match is not in progress');
    }

    const seat = this.seatOf(playerId);
    if (seat === null) throw roomError('NOT_IN_ROOM', 'you are not in that room');
    if (seat !== this.turnSeat) throw roomError('NOT_YOUR_TURN', 'it is not your turn');

    this.stones = sanitizeStones(stones);
    this.stonesThrown += 1;

    const totalStones = STONES_PER_PLAYER * 2;
    const roundComplete = this.stonesThrown >= totalStones;

    if (!roundComplete) {
      this.turnSeat = this.opponentSeat(seat);
      this.touch();
      return { roundComplete: false, launch, seat, snapshot: this.snapshot() };
    }

    return { roundComplete: true, launch, seat, ...this.finishRound() };
  }

  /** Scores the round server-side, then either starts the next round or ends the match. */
  finishRound() {
    const result = scoreRink(this.stones);

    if (!result.blank) {
      this.totalPoints[result.winnerSeat] += result.points;
      this.roundsWon[result.winnerSeat] += 1;
      this.hammer = this.opponentSeat(result.winnerSeat);
    }

    const decided =
      this.roundsWon[0] >= ROUNDS_TO_WIN ||
      this.roundsWon[1] >= ROUNDS_TO_WIN ||
      this.round >= MAX_ROUNDS;

    if (!decided) {
      this.beginRound();
      return { roundResult: result, matchOver: false, snapshot: this.snapshot() };
    }

    this.state = ROOM_STATE.FINISHED;
    this.endedReason = 'completed';
    this.winnerSeat = decideWinner(this.roundsWon, this.totalPoints);
    this.touch();

    return { roundResult: result, matchOver: true, snapshot: this.snapshot() };
  }

  /** Ends the match early, awarding it to the other seat. */
  forfeit(seat, reason = 'forfeit') {
    if (this.state === ROOM_STATE.FINISHED) return this.snapshot();
    this.state = ROOM_STATE.FINISHED;
    this.endedReason = reason;
    this.winnerSeat = this.opponentSeat(seat);
    this.touch();
    return this.snapshot();
  }

  touch() {
    this.lastActivityAt = this.now();
  }

  snapshot() {
    return {
      code: this.code,
      state: this.state,
      seed: this.seed,
      round: this.round,
      roundsWon: [...this.roundsWon],
      totalPoints: [...this.totalPoints],
      hammer: this.hammer,
      turnSeat: this.turnSeat,
      stonesThrown: this.stonesThrown,
      stoneNumber: Math.floor(this.stonesThrown / 2) + 1,
      modifier: this.modifierByRound[this.round] ?? -1,
      winnerSeat: this.winnerSeat ?? -1,
      endedReason: this.endedReason,
      isPublic: this.isPublic,
      players: this.seats.map((s) =>
        s
          ? {
              id: s.player.id,
              displayName: s.player.displayName,
              kind: s.player.kind,
              rating: s.player.rating,
              ready: s.ready,
              connected: s.connected,
            }
          : null,
      ),
    };
  }
}

/**
 * Picks the match winner. Rounds won decides it; total points break a tie, which can happen
 * when the round limit is reached at one win each.
 */
export function decideWinner(roundsWon, totalPoints) {
  if (roundsWon[0] !== roundsWon[1]) return roundsWon[0] > roundsWon[1] ? 0 : 1;
  if (totalPoints[0] !== totalPoints[1]) return totalPoints[0] > totalPoints[1] ? 0 : 1;
  return 0;
}

/**
 * Chooses the round's modifier from the room seed, so both clients independently derive the
 * same one with nothing sent over the wire. Index 0 is the cross current, 1 the bumpers,
 * 2 the warp gates, matching the client's build order.
 */
export function deterministicModifier(seed, round) {
  // A small integer hash: cheap, stable across platforms, and good enough to avoid the
  // obvious pattern of a plain modulo on the round number.
  let h = (seed ^ (round * 0x9e3779b1)) >>> 0;
  h = Math.imul(h ^ (h >>> 16), 0x85ebca6b) >>> 0;
  h = Math.imul(h ^ (h >>> 13), 0xc2b2ae35) >>> 0;
  h = (h ^ (h >>> 16)) >>> 0;
  return h % 3;
}

/** Drops anything malformed and clamps the rest to the rink, so one bad client cannot poison a room. */
export function sanitizeStones(stones) {
  if (!Array.isArray(stones)) return [];

  const out = [];
  for (const stone of stones.slice(0, STONES_PER_PLAYER * 2 + 2)) {
    if (!stone || typeof stone !== 'object') continue;

    const seat = stone.seat === 1 ? 1 : 0;
    const x = Number(stone.x);
    const z = Number(stone.z);
    if (!Number.isFinite(x) || !Number.isFinite(z)) continue;

    out.push({
      id: typeof stone.id === 'string' ? stone.id.slice(0, 48) : `s${out.length}`,
      seat,
      x: clamp(x, -1.5, 1.5),
      z: clamp(z, -2.5, 2.5),
    });
  }
  return out;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

export function roomError(code, message) {
  return Object.assign(new Error(message), { code, expected: true });
}

/**
 * The registry: room lookup by code, the quick-match queue, and reaping of dead rooms.
 */
export class RoomRegistry {
  constructor({ now = () => Date.now(), reconnectGraceMs = 30_000, idleTimeoutMs = 15 * 60_000 } = {}) {
    this.rooms = new Map();      // code -> Room
    this.queue = [];             // player ids waiting for a quick match
    this.now = now;
    this.reconnectGraceMs = reconnectGraceMs;
    this.idleTimeoutMs = idleTimeoutMs;
  }

  create({ hostId, isPublic = false, random = Math.random }) {
    let code = generateRoomCode(random);
    let attempts = 0;
    while (this.rooms.has(code)) {
      code = generateRoomCode(random);
      if (++attempts > 200) throw roomError('NO_CODES', 'could not allocate a room code');
    }

    const room = new Room({ code, hostId, isPublic, now: this.now });
    this.rooms.set(code, room);
    return room;
  }

  get(code) {
    if (typeof code !== 'string') return null;
    return this.rooms.get(code.trim().toUpperCase()) ?? null;
  }

  roomOf(playerId) {
    for (const room of this.rooms.values()) {
      if (room.seatOf(playerId) !== null) return room;
    }
    return null;
  }

  remove(code) {
    this.rooms.delete(code);
  }

  /**
   * Quick match: joins the oldest waiting public room, or opens one and waits. Returning the
   * room either way keeps the caller free of "did I create or join" branching.
   */
  quickMatch(player, { random = Math.random } = {}) {
    const existing = this.roomOf(player.id);
    if (existing) return { room: existing, created: false, seat: existing.seatOf(player.id) };

    let oldest = null;
    for (const room of this.rooms.values()) {
      if (!room.isPublic) continue;
      if (room.state !== ROOM_STATE.WAITING) continue;
      if (room.isFull) continue;
      if (room.hostId === player.id) continue;
      if (!oldest || room.createdAt < oldest.createdAt) oldest = room;
    }

    if (oldest) {
      const seat = oldest.join(player);
      return { room: oldest, created: false, seat };
    }

    const room = this.create({ hostId: player.id, isPublic: true, random });
    const seat = room.join(player);
    return { room, created: true, seat };
  }

  /**
   * Housekeeping. Returns the rooms that changed so the caller can notify players: rooms
   * forfeited because someone never came back, and rooms dropped for being empty or stale.
   */
  sweep() {
    const forfeited = [];
    const removed = [];
    const now = this.now();

    for (const [code, room] of this.rooms) {
      if (room.state === ROOM_STATE.PLAYING) {
        for (let seat = 0; seat < 2; seat++) {
          const at = room.disconnectedAt[seat];
          if (at === null) continue;
          if (now - at < this.reconnectGraceMs) continue;

          room.forfeit(seat, 'disconnect');
          forfeited.push({ room, seat });
          break;
        }
      }

      const empty = room.playerCount === 0;
      const idle = now - room.lastActivityAt > this.idleTimeoutMs;
      const finishedAndIdle =
        room.state === ROOM_STATE.FINISHED && now - room.lastActivityAt > 60_000;

      if (empty || idle || finishedAndIdle) {
        this.rooms.delete(code);
        removed.push(room);
      }
    }

    return { forfeited, removed };
  }

  publicRooms(limit = 20) {
    const out = [];
    for (const room of this.rooms.values()) {
      if (!room.isPublic || room.state !== ROOM_STATE.WAITING || room.isFull) continue;
      out.push({ code: room.code, players: room.playerCount, createdAt: room.createdAt });
      if (out.length >= limit) break;
    }
    return out;
  }

  get size() {
    return this.rooms.size;
  }
}
