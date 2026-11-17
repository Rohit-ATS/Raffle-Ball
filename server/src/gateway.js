/**
 * The WebSocket gateway: one socket per player, carrying the whole match.
 *
 * Every message is a JSON object with a `type`. There is no binary protocol and no delta
 * compression, because a turn-based game sends a handful of messages per minute - the entire
 * traffic of a match is a few kilobytes - and legibility is worth far more here than bytes.
 *
 * Three rules keep this robust against the things that actually go wrong in VR:
 *
 * - Reconnection is the normal case, not an error path. A player who drops and comes back
 *   with the same token is put back in their seat with a full state snapshot, because
 *   headsets lose Wi-Fi constantly and restarting the match every time would be unplayable.
 * - The server never trusts a client's turn. Every action is checked against the room's own
 *   turn order, so a lagging or malicious client cannot throw twice or throw out of turn.
 * - Dead sockets are reaped on a timer with ping/pong. Render's proxy will hold a half-open
 *   connection indefinitely, and without this a room would fill up with ghosts.
 */

import { WebSocketServer } from 'ws';
import { ROOM_STATE, RoomRegistry, roomError } from './rooms.js';
import { createAuth } from './auth.js';

const HEARTBEAT_MS = 25_000;
const SWEEP_MS = 5_000;
const MAX_MESSAGE_BYTES = 16 * 1024;

export function createGateway({
  server,
  store,
  auth = createAuth(),
  logger = console,
  registry = new RoomRegistry(),
}) {
  const wss = new WebSocketServer({ server, path: '/ws', maxPayload: MAX_MESSAGE_BYTES });

  /** playerId -> socket. One session per player; a second login replaces the first. */
  const sockets = new Map();

  wss.on('connection', (socket) => {
    socket.isAlive = true;
    socket.player = null;

    socket.on('pong', () => {
      socket.isAlive = true;
    });

    socket.on('message', async (raw) => {
      let message;
      try {
        message = JSON.parse(raw.toString());
      } catch {
        return send(socket, { type: 'error', code: 'BAD_JSON', message: 'malformed message' });
      }

      try {
        await handle(socket, message);
      } catch (err) {
        if (err?.expected) {
          return send(socket, { type: 'error', code: err.code, message: err.message });
        }
        logger.error?.('[ws] handler failed', err);
        send(socket, { type: 'error', code: 'INTERNAL', message: 'internal error' });
      }
    });

    socket.on('close', () => onDisconnect(socket));
    socket.on('error', () => onDisconnect(socket));

    send(socket, { type: 'hello', protocol: 1 });
  });

  // ---------------------------------------------------------------- dispatch

  async function handle(socket, message) {
    const type = message?.type;

    // Authentication first: nothing else is reachable without a resolved player.
    if (type === 'auth') return authenticate(socket, message);

    if (!socket.player) {
      throw roomError('UNAUTHENTICATED', 'send an auth message first');
    }

    switch (type) {
      case 'ping':
        return send(socket, { type: 'pong', t: message.t ?? null });

      case 'createRoom':
        return createRoom(socket, message);

      case 'joinRoom':
        return joinRoom(socket, message);

      case 'quickMatch':
        return quickMatch(socket);

      case 'ready':
        return setReady(socket, message);

      case 'throw':
        return submitThrow(socket, message);

      case 'leaveRoom':
        return leaveRoom(socket);

      case 'roomState': {
        const room = registry.roomOf(socket.player.id);
        return send(socket, {
          type: 'roomState',
          room: room ? room.snapshot() : null,
          seat: room ? room.seatOf(socket.player.id) : null,
        });
      }

      case 'listRooms':
        return send(socket, { type: 'roomList', rooms: registry.publicRooms() });

      default:
        throw roomError('UNKNOWN_TYPE', `unknown message type: ${type}`);
    }
  }

  async function authenticate(socket, message) {
    const payload = auth.verifyToken(message?.token);
    if (!payload?.sub) {
      return send(socket, { type: 'error', code: 'BAD_TOKEN', message: 'invalid token' });
    }

    const player = await store.findPlayerById(payload.sub);
    if (!player) {
      return send(socket, { type: 'error', code: 'NO_PLAYER', message: 'unknown player' });
    }

    // One socket per player. A stale session is closed rather than left to receive half the
    // match's messages.
    const previous = sockets.get(player.id);
    if (previous && previous !== socket) {
      send(previous, { type: 'replaced', message: 'signed in from another device' });
      previous.close(4001, 'replaced');
    }

    socket.player = player;
    sockets.set(player.id, socket);
    await store.touchPlayer(player.id);

    send(socket, { type: 'authenticated', player });

    // Reconnecting into a live match: restore the seat and resend everything needed to
    // rebuild the rink, rather than making the client start over.
    const room = registry.roomOf(player.id);
    if (room) {
      const seat = room.seatOf(player.id);
      room.markConnected(seat, true);
      send(socket, {
        type: 'rejoined',
        room: room.snapshot(),
        seat,
        stones: room.stones,
      });
      broadcast(room, { type: 'opponentReconnected', seat }, player.id);
    }
  }

  function createRoom(socket, message) {
    const player = socket.player;
    leaveCurrentRoom(player.id);

    const room = registry.create({
      hostId: player.id,
      isPublic: Boolean(message?.isPublic),
    });
    const seat = room.join(player);

    send(socket, { type: 'roomCreated', room: room.snapshot(), seat });
  }

  function joinRoom(socket, message) {
    const player = socket.player;
    const room = registry.get(message?.code);
    if (!room) throw roomError('NO_SUCH_ROOM', 'no room with that code');

    if (room.seatOf(player.id) === null) leaveCurrentRoom(player.id);

    const seat = room.join(player);
    send(socket, { type: 'roomJoined', room: room.snapshot(), seat });
    broadcast(room, { type: 'roomState', room: room.snapshot() });
  }

  function quickMatch(socket) {
    const player = socket.player;
    const { room, created, seat } = registry.quickMatch(player);

    send(socket, {
      type: created ? 'queued' : 'roomJoined',
      room: room.snapshot(),
      seat,
    });

    if (!created) broadcast(room, { type: 'roomState', room: room.snapshot() });
  }

  function setReady(socket, message) {
    const player = socket.player;
    const room = registry.roomOf(player.id);
    if (!room) throw roomError('NOT_IN_ROOM', 'you are not in a room');

    room.setReady(player.id, message?.ready !== false);
    broadcast(room, { type: 'roomState', room: room.snapshot() });

    if (!room.bothReady || room.state === ROOM_STATE.PLAYING) return;

    room.start();
    broadcast(room, { type: 'matchStart', room: room.snapshot() });
    broadcast(room, {
      type: 'turn',
      seat: room.turnSeat,
      stoneNumber: room.snapshot().stoneNumber,
      room: room.snapshot(),
    });
  }

  async function submitThrow(socket, message) {
    const player = socket.player;
    const room = registry.roomOf(player.id);
    if (!room) throw roomError('NOT_IN_ROOM', 'you are not in a room');

    const launch = sanitizeLaunch(message?.launch);
    const result = room.submitThrow({
      playerId: player.id,
      launch,
      stones: message?.stones,
    });

    // The opponent replays the throw for the look of it, then adopts the authoritative
    // positions. Both are sent together so a lost message cannot desynchronise the rink.
    broadcast(
      room,
      {
        type: 'opponentThrow',
        seat: result.seat,
        launch: result.launch,
        stones: room.stones,
      },
      player.id,
    );

    if (!result.roundComplete) {
      broadcast(room, {
        type: 'turn',
        seat: room.turnSeat,
        stoneNumber: room.snapshot().stoneNumber,
        room: room.snapshot(),
      });
      return;
    }

    broadcast(room, {
      type: 'roundScored',
      result: result.roundResult,
      room: room.snapshot(),
    });

    if (!result.matchOver) {
      broadcast(room, {
        type: 'turn',
        seat: room.turnSeat,
        stoneNumber: room.snapshot().stoneNumber,
        room: room.snapshot(),
      });
      return;
    }

    await finishMatch(room);
  }

  async function finishMatch(room) {
    const snapshot = room.snapshot();
    const winnerSeat = room.winnerSeat >= 0 ? room.winnerSeat : 0;
    const loserSeat = winnerSeat === 0 ? 1 : 0;

    const winner = room.occupant(winnerSeat)?.player;
    const loser = room.occupant(loserSeat)?.player;

    let record = null;
    if (winner && loser) {
      try {
        record = await store.recordMatch({
          roomCode: room.code,
          winnerId: winner.id,
          loserId: loser.id,
          winnerPoints: room.totalPoints[winnerSeat],
          loserPoints: room.totalPoints[loserSeat],
          rounds: room.round,
          endedReason: room.endedReason ?? 'completed',
        });
      } catch (err) {
        // A failed write must not stop the players being told who won.
        logger.error?.('[ws] could not record match', err);
      }
    }

    for (let seat = 0; seat < 2; seat++) {
      const occupant = room.occupant(seat);
      if (!occupant) continue;
      const socket = sockets.get(occupant.player.id);
      if (!socket) continue;

      const isWinner = seat === winnerSeat;
      const updated = record ? (isWinner ? record.winner : record.loser) : null;

      send(socket, {
        type: 'matchOver',
        room: snapshot,
        youWon: isWinner,
        winnerSeat,
        reason: room.endedReason ?? 'completed',
        player: updated,
        rank: updated ? await store.playerRank(updated.id) : null,
      });
    }
  }

  function leaveRoom(socket) {
    const player = socket.player;
    const room = registry.roomOf(player.id);
    if (!room) return;

    const seat = room.seatOf(player.id);

    // Walking out of a live match is a forfeit. Anything else would make quitting the
    // optimal move whenever you are behind.
    if (room.state === ROOM_STATE.PLAYING) {
      room.forfeit(seat, 'left');
      broadcast(room, { type: 'opponentLeft', seat }, player.id);
      finishMatch(room).catch((err) => logger.error?.('[ws] forfeit record failed', err));
    }

    room.leave(player.id);
    send(socket, { type: 'roomLeft' });
    broadcast(room, { type: 'roomState', room: room.snapshot() });
    if (room.playerCount === 0) registry.remove(room.code);
  }

  function leaveCurrentRoom(playerId) {
    const room = registry.roomOf(playerId);
    if (!room) return;
    if (room.state === ROOM_STATE.PLAYING) {
      const seat = room.seatOf(playerId);
      room.forfeit(seat, 'left');
      broadcast(room, { type: 'opponentLeft', seat }, playerId);
    }
    room.leave(playerId);
    if (room.playerCount === 0) registry.remove(room.code);
  }

  function onDisconnect(socket) {
    const player = socket.player;
    if (!player) return;

    if (sockets.get(player.id) === socket) sockets.delete(player.id);

    const room = registry.roomOf(player.id);
    if (!room) return;

    const seat = room.seatOf(player.id);

    // A waiting room empties immediately; a live match holds the seat open for the
    // reconnect window, because a dropped headset is not the same as quitting.
    if (room.state === ROOM_STATE.PLAYING) {
      room.markConnected(seat, false);
      broadcast(room, { type: 'opponentDisconnected', seat }, player.id);
      return;
    }

    room.leave(player.id);
    broadcast(room, { type: 'roomState', room: room.snapshot() });
    if (room.playerCount === 0) registry.remove(room.code);
  }

  // ---------------------------------------------------------------- plumbing

  function send(socket, payload) {
    if (!socket || socket.readyState !== socket.OPEN) return;
    socket.send(JSON.stringify(payload));
  }

  function broadcast(room, payload, exceptPlayerId = null) {
    for (const occupant of room.seats) {
      if (!occupant) continue;
      if (occupant.player.id === exceptPlayerId) continue;
      const socket = sockets.get(occupant.player.id);
      if (socket) send(socket, payload);
    }
  }

  const heartbeat = setInterval(() => {
    for (const socket of wss.clients) {
      if (!socket.isAlive) {
        socket.terminate();
        continue;
      }
      socket.isAlive = false;
      socket.ping();
    }
  }, HEARTBEAT_MS);

  const sweeper = setInterval(() => {
    const { forfeited } = registry.sweep();
    for (const { room, seat } of forfeited) {
      broadcast(room, { type: 'opponentTimedOut', seat });
      finishMatch(room).catch((err) => logger.error?.('[ws] timeout record failed', err));
    }
  }, SWEEP_MS);

  heartbeat.unref?.();
  sweeper.unref?.();

  return {
    wss,
    registry,
    sockets,
    close() {
      clearInterval(heartbeat);
      clearInterval(sweeper);
      for (const socket of wss.clients) socket.terminate();
      return new Promise((resolve) => wss.close(resolve));
    },
  };
}

/** Keeps replayed throws inside believable bounds; the visual replay must not be weaponisable. */
export function sanitizeLaunch(launch) {
  const safe = (value, limit, fallback = 0) => {
    const n = Number(value);
    if (!Number.isFinite(n)) return fallback;
    return Math.min(limit, Math.max(-limit, n));
  };

  return {
    x: safe(launch?.x, 1.5),
    z: safe(launch?.z, 2.5),
    dirX: safe(launch?.dirX, 1),
    dirZ: safe(launch?.dirZ, 1, 1),
    speed: Math.min(6, Math.max(0, Number(launch?.speed) || 0)),
    spin: safe(launch?.spin, 8),
  };
}
