/**
 * Full multiplayer, over real sockets.
 *
 * Two clients connect, authenticate, find each other, and play a complete match. Client-
 * simulated matches stay unranked but remain in match history. This proves the thing people do
 * works, and it is the one worth keeping slow and realistic rather than mocking: every bug
 * found here so far has been in the seams - a message sent to the wrong player, a turn
 * advanced twice, a match recorded before the scores were applied.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { WebSocket } from 'ws';
import { createApp } from '../src/app.js';
import { createAuth } from '../src/auth.js';
import { createGateway } from '../src/gateway.js';
import { MemoryStore } from '../src/store.js';
import { RoomRegistry } from '../src/rooms.js';
import { STONES_PER_PLAYER, TARGET_X, TARGET_Z } from '../src/scoring.js';

const CONNECT_TIMEOUT = 4000;

async function startServer({ reconnectGraceMs = 30_000, now } = {}) {
  const store = new MemoryStore();
  await store.init();

  const auth = createAuth({ secret: 'test-secret', logger: { warn() {} } });
  const app = createApp({ store, auth, logger: { error() {} } });
  const server = http.createServer(app);
  const registry = new RoomRegistry({ reconnectGraceMs, now });
  const gateway = createGateway({
    server,
    store,
    auth,
    registry,
    logger: { error() {}, warn() {} },
  });

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;

  return {
    store,
    auth,
    gateway,
    registry,
    url: `ws://127.0.0.1:${port}/ws`,
    httpBase: `http://127.0.0.1:${port}`,
    async close() {
      await gateway.close();
      await new Promise((resolve) => server.close(resolve));
      await store.close();
    },
  };
}

/**
 * A tiny test client that buffers every message, so a test can await a message type that may
 * already have arrived. Without the buffer these tests would be a race.
 */
class TestClient {
  constructor(url, label = '?') {
    this.label = label;
    this.socket = new WebSocket(url);
    this.messages = [];
    this.waiters = [];

    this.socket.on('message', (raw) => {
      const message = JSON.parse(raw.toString());
      if (process.env.DEBUG_WS) {
        console.error(`[${this.label ?? '?'}] <- ${message.type} ${JSON.stringify(message).slice(0, 160)}`);
      }
      // A message handed to a waiter must NOT also stay in the buffer, or a later wait for
      // the same type will match it again and the test will read a stale turn as the current
      // one. Each message is delivered exactly once.
      const waiterIndex = this.waiters.findIndex((w) => w.type === message.type);
      if (waiterIndex !== -1) {
        const [waiter] = this.waiters.splice(waiterIndex, 1);
        waiter.resolve(message);
        return;
      }

      this.messages.push(message);
    });

    this.ready = new Promise((resolve, reject) => {
      this.socket.once('open', resolve);
      this.socket.once('error', reject);
    });
  }

  send(payload) {
    this.socket.send(JSON.stringify(payload));
  }

  /** Resolves with the first message of `type`, past or future. */
  wait(type, timeout = CONNECT_TIMEOUT) {
    const existing = this.messages.find((m) => m.type === type);
    if (existing) {
      this.messages = this.messages.filter((m) => m !== existing);
      return Promise.resolve(existing);
    }

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        const seen = this.messages.map((m) => m.type).join(', ');
        reject(new Error(`timed out waiting for "${type}"; saw: [${seen}]`));
      }, timeout);

      this.waiters.push({
        type,
        resolve: (message) => {
          clearTimeout(timer);
          resolve(message);
        },
      });
    });
  }

  /** Throws away any buffered messages of a type, so a later wait cannot match a stale one. */
  drain(type) {
    this.messages = this.messages.filter((m) => m.type !== type);
  }

  close() {
    this.socket.close();
  }
}

async function guest(server, displayName) {
  const response = await fetch(`${server.httpBase}/api/auth/guest`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ displayName }),
  });
  return response.json();
}

async function connectAs(server, token, label = '?') {
  const client = new TestClient(server.url, label);
  await client.ready;
  await client.wait('hello');
  client.send({ type: 'auth', token });
  const authenticated = await client.wait('authenticated');
  return { client, player: authenticated.player };
}

function stoneAt(seat, distance, angleDegrees = 0) {
  const radians = (angleDegrees * Math.PI) / 180;
  return {
    seat,
    x: TARGET_X + Math.cos(radians) * distance,
    z: TARGET_Z + Math.sin(radians) * distance,
  };
}

/**
 * Plays one full round, with `winnerSeat` ending up closest. Returns the roundScored message.
 *
 * Whose turn it is comes from the server's own `turn` messages rather than from reading the
 * Room object, for two reasons: it is what a real client has to do, and reading the server's
 * state directly races the message it is about to send.
 */
async function playRound(clients, winnerSeat) {
  const total = STONES_PER_PLAYER * 2;

  // Consume the turn announcement both sockets have already been sent. Awaiting it rather
  // than discarding it is what makes this deterministic: a message that has not arrived yet
  // cannot be drained, and it would otherwise be mistaken for the next turn.
  const [opening] = await Promise.all([clients[0].wait('turn'), clients[1].wait('turn')]);
  let turnSeat = opening.seat;

  for (let i = 0; i < total; i++) {
    const last = i === total - 1;

    const stones = last
      ? [stoneAt(winnerSeat, 0.02), stoneAt(winnerSeat === 0 ? 1 : 0, 0.13)]
      : [stoneAt(winnerSeat, 0.02)];

    clients[turnSeat].send({
      type: 'throw',
      launch: { x: 0, z: -0.6, dirX: 0, dirZ: 1, speed: 1.2, spin: 0 },
      stones,
    });

    if (last) break;

    // Both sides are told whose turn it is next; waiting for both keeps the two clients in
    // step with each other as well as with the server.
    const [next] = await Promise.all([clients[0].wait('turn'), clients[1].wait('turn')]);
    turnSeat = next.seat;
  }

  const scored = await clients[0].wait('roundScored');
  await clients[1].wait('roundScored');
  return scored;
}

test('a client must authenticate before doing anything else', async () => {
  const server = await startServer();
  try {
    const client = new TestClient(server.url);
    await client.ready;
    await client.wait('hello');

    client.send({ type: 'createRoom' });
    const error = await client.wait('error');
    assert.equal(error.code, 'UNAUTHENTICATED');

    client.close();
  } finally {
    await server.close();
  }
});

test('a bad token is refused', async () => {
  const server = await startServer();
  try {
    const client = new TestClient(server.url);
    await client.ready;
    await client.wait('hello');

    client.send({ type: 'auth', token: 'rubbish' });
    const error = await client.wait('error');
    assert.equal(error.code, 'BAD_TOKEN');

    client.close();
  } finally {
    await server.close();
  }
});

test('malformed JSON does not take the connection down', async () => {
  const server = await startServer();
  try {
    const client = new TestClient(server.url);
    await client.ready;
    await client.wait('hello');

    client.socket.send('{ not json');
    const error = await client.wait('error');
    assert.equal(error.code, 'BAD_JSON');
    assert.equal(client.socket.readyState, WebSocket.OPEN, 'the socket stays usable');

    client.close();
  } finally {
    await server.close();
  }
});

test('a host creates a room and a friend joins it by code', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Host');
    const friendAuth = await guest(server, 'Friend');

    const host = await connectAs(server, hostAuth.token);
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    assert.equal(created.seat, 0);
    assert.match(created.room.code, /^[A-Z0-9]{4}$/);

    const friend = await connectAs(server, friendAuth.token);
    friend.client.send({ type: 'joinRoom', code: created.room.code.toLowerCase() });
    const joined = await friend.client.wait('roomJoined');

    assert.equal(joined.seat, 1, 'the friend takes the second seat');
    assert.equal(joined.room.players[0].displayName, 'Host');
    assert.equal(joined.room.players[1].displayName, 'Friend');

    host.client.close();
    friend.client.close();
  } finally {
    await server.close();
  }
});

test('joining a code that does not exist is a clean error', async () => {
  const server = await startServer();
  try {
    const auth = await guest(server, 'Lost');
    const { client } = await connectAs(server, auth.token);

    client.send({ type: 'joinRoom', code: 'ZZZZ' });
    const error = await client.wait('error');
    assert.equal(error.code, 'NO_SUCH_ROOM');

    client.close();
  } finally {
    await server.close();
  }
});

test('quick match pairs two strangers', async () => {
  const server = await startServer();
  try {
    const oneAuth = await guest(server, 'One');
    const twoAuth = await guest(server, 'Two');

    const one = await connectAs(server, oneAuth.token);
    one.client.send({ type: 'quickMatch' });
    const queued = await one.client.wait('queued');
    assert.equal(queued.seat, 0);

    const two = await connectAs(server, twoAuth.token);
    two.client.send({ type: 'quickMatch' });
    const paired = await two.client.wait('roomJoined');

    assert.equal(paired.room.code, queued.room.code, 'both land in the same room');
    assert.equal(paired.seat, 1);

    one.client.close();
    two.client.close();
  } finally {
    await server.close();
  }
});

test('the match starts only once both players are ready', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Host');
    const friendAuth = await guest(server, 'Friend');

    const host = await connectAs(server, hostAuth.token);
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    const friend = await connectAs(server, friendAuth.token);
    friend.client.send({ type: 'joinRoom', code: created.room.code });
    await friend.client.wait('roomJoined');

    host.client.send({ type: 'ready' });
    const afterOne = await friend.client.wait('roomState');
    assert.notEqual(afterOne.room.state, 'playing', 'one ready player is not enough');

    friend.client.send({ type: 'ready' });
    const start = await host.client.wait('matchStart');

    assert.equal(start.room.state, 'playing');
    assert.equal(start.room.round, 1);
    assert.equal(start.room.modifier, -1, 'round one is clean');

    host.client.close();
    friend.client.close();
  } finally {
    await server.close();
  }
});

test('throwing out of turn is refused', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Host');
    const friendAuth = await guest(server, 'Friend');

    const host = await connectAs(server, hostAuth.token);
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    const friend = await connectAs(server, friendAuth.token);
    friend.client.send({ type: 'joinRoom', code: created.room.code });
    await friend.client.wait('roomJoined');

    host.client.send({ type: 'ready' });
    friend.client.send({ type: 'ready' });
    const start = await host.client.wait('matchStart');

    const wrongSeat = start.room.turnSeat === 0 ? friend : host;
    wrongSeat.client.send({ type: 'throw', launch: {}, stones: [] });

    const error = await wrongSeat.client.wait('error');
    assert.equal(error.code, 'NOT_YOUR_TURN');

    host.client.close();
    friend.client.close();
  } finally {
    await server.close();
  }
});

test('a throw is relayed to the opponent with the authoritative stone positions', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Host');
    const friendAuth = await guest(server, 'Friend');

    const host = await connectAs(server, hostAuth.token);
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    const friend = await connectAs(server, friendAuth.token);
    friend.client.send({ type: 'joinRoom', code: created.room.code });
    await friend.client.wait('roomJoined');

    host.client.send({ type: 'ready' });
    friend.client.send({ type: 'ready' });
    const start = await host.client.wait('matchStart');

    const thrower = start.room.turnSeat === 0 ? host : friend;
    const watcher = start.room.turnSeat === 0 ? friend : host;

    // The match start already announced the first turn. Await it on both sockets so the wait
    // below can only match the turn caused by this throw.
    await Promise.all([host.client.wait('turn'), friend.client.wait('turn')]);

    thrower.client.send({
      type: 'throw',
      launch: { x: 0.1, z: -0.6, dirX: 0, dirZ: 1, speed: 1.4, spin: 2 },
      stones: [stoneAt(start.room.turnSeat, 0.04)],
    });

    const relayed = await watcher.client.wait('opponentThrow');
    assert.equal(relayed.seat, start.room.turnSeat);
    assert.equal(relayed.launch.speed, 1.4, 'the throw is replayed for the look of it');
    assert.equal(relayed.stones.length, 1, 'and the resting positions come with it');

    const turn = await watcher.client.wait('turn');
    assert.equal(turn.seat, start.room.turnSeat === 0 ? 1 : 0, 'the turn passes over');

    host.client.close();
    friend.client.close();
  } finally {
    await server.close();
  }
});

test('an absurd launch is clamped rather than relayed as-is', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Host');
    const friendAuth = await guest(server, 'Friend');

    const host = await connectAs(server, hostAuth.token);
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    const friend = await connectAs(server, friendAuth.token);
    friend.client.send({ type: 'joinRoom', code: created.room.code });
    await friend.client.wait('roomJoined');

    host.client.send({ type: 'ready' });
    friend.client.send({ type: 'ready' });
    const start = await host.client.wait('matchStart');

    const thrower = start.room.turnSeat === 0 ? host : friend;
    const watcher = start.room.turnSeat === 0 ? friend : host;

    thrower.client.send({
      type: 'throw',
      launch: { speed: 99999, spin: 1e9, dirZ: Number.NaN },
      stones: [],
    });

    const relayed = await watcher.client.wait('opponentThrow');
    assert.ok(relayed.launch.speed <= 6, 'speed is capped');
    assert.ok(Math.abs(relayed.launch.spin) <= 8, 'spin is capped');
    assert.ok(Number.isFinite(relayed.launch.dirZ), 'NaN is replaced');

    host.client.close();
    friend.client.close();
  } finally {
    await server.close();
  }
});

test('a complete client-authoritative match is played and retained without changing Elo', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Winner');
    const friendAuth = await guest(server, 'Loser');

    const host = await connectAs(server, hostAuth.token, 'host');
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    const friend = await connectAs(server, friendAuth.token, 'friend');
    friend.client.send({ type: 'joinRoom', code: created.room.code });
    await friend.client.wait('roomJoined');

    host.client.send({ type: 'ready' });
    friend.client.send({ type: 'ready' });
    const start = await host.client.wait('matchStart');

    const clients = [host.client, friend.client];
    assert.equal(start.room.turnSeat, 0);

    // Seat 0 takes both rounds, which is the shortest decisive match.
    const firstRound = await playRound(clients, 0);
    assert.equal(firstRound.result.winnerSeat, 0);
    assert.equal(firstRound.room.roundsWon[0], 1);
    assert.ok(firstRound.room.modifier >= 0, 'round two reveals a modifier');

    await playRound(clients, 0);

    const hostOver = await host.client.wait('matchOver');
    const friendOver = await friend.client.wait('matchOver');

    assert.equal(hostOver.youWon, true);
    assert.equal(friendOver.youWon, false);
    assert.equal(hostOver.winnerSeat, 0);
    assert.equal(hostOver.reason, 'completed');
    assert.equal(hostOver.player.wins, 0, 'unverified client physics cannot grant wins');
    assert.equal(friendOver.player.losses, 0);
    assert.equal(hostOver.player.rating, 1000, 'unverified results do not change Elo');
    assert.equal(friendOver.player.rating, 1000);
    assert.equal(hostOver.rank, null);

    const board = await (await fetch(`${server.httpBase}/api/leaderboard`)).json();
    assert.equal(board.entries.length, 0, 'unranked matches do not populate the leaderboard');

    const matches = await (await fetch(`${server.httpBase}/api/matches`)).json();
    assert.equal(matches.matches.length, 1);
    assert.equal(matches.matches[0].winner.displayName, 'Winner');

    host.client.close();
    friend.client.close();
  } finally {
    await server.close();
  }
});

test('leaving a live match awards it to the player still there', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Stayer');
    const friendAuth = await guest(server, 'Quitter');

    const host = await connectAs(server, hostAuth.token);
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    const friend = await connectAs(server, friendAuth.token);
    friend.client.send({ type: 'joinRoom', code: created.room.code });
    await friend.client.wait('roomJoined');

    host.client.send({ type: 'ready' });
    friend.client.send({ type: 'ready' });
    await host.client.wait('matchStart');

    friend.client.send({ type: 'leaveRoom' });

    const left = await host.client.wait('opponentLeft');
    assert.equal(left.seat, 1);

    const over = await host.client.wait('matchOver');
    assert.equal(over.youWon, true);
    assert.equal(over.reason, 'left');

    host.client.close();
    friend.client.close();
  } finally {
    await server.close();
  }
});

test('a dropped connection holds the seat, and reconnecting restores the match', async () => {
  const server = await startServer();
  try {
    const hostAuth = await guest(server, 'Host');
    const friendAuth = await guest(server, 'Flaky');

    const host = await connectAs(server, hostAuth.token);
    host.client.send({ type: 'createRoom' });
    const created = await host.client.wait('roomCreated');

    const friend = await connectAs(server, friendAuth.token);
    friend.client.send({ type: 'joinRoom', code: created.room.code });
    await friend.client.wait('roomJoined');

    host.client.send({ type: 'ready' });
    friend.client.send({ type: 'ready' });
    await host.client.wait('matchStart');

    // The headset drops off Wi-Fi mid-match.
    friend.client.socket.terminate();
    const dropped = await host.client.wait('opponentDisconnected');
    assert.equal(dropped.seat, 1);

    const room = server.registry.get(created.room.code);
    assert.equal(room.state, 'playing', 'the match is held, not ended');

    // And comes back on the same token.
    const back = await connectAs(server, friendAuth.token);
    const rejoined = await back.client.wait('rejoined');

    assert.equal(rejoined.seat, 1, 'the same seat is restored');
    assert.equal(rejoined.room.state, 'playing');
    assert.ok(Array.isArray(rejoined.stones), 'the rink comes back with it');

    await host.client.wait('opponentReconnected');

    host.client.close();
    back.client.close();
  } finally {
    await server.close();
  }
});

test('signing in twice closes the older session instead of splitting the match', async () => {
  const server = await startServer();
  try {
    const auth = await guest(server, 'Doubled');

    const first = await connectAs(server, auth.token);
    const second = await connectAs(server, auth.token);

    const replaced = await first.client.wait('replaced');
    assert.match(replaced.message, /another device/);

    assert.equal(second.client.socket.readyState, WebSocket.OPEN);

    first.client.close();
    second.client.close();
  } finally {
    await server.close();
  }
});
