/**
 * The HTTP API, exercised against a real listening server.
 *
 * Deliberately not mocked: these tests start the actual express app on an ephemeral port and
 * talk to it over fetch, because most of what can go wrong here - status codes, header
 * parsing, JSON shapes, the leaderboard ordering - is invisible to a unit test of a handler.
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { createApp } from '../src/app.js';
import { createAuth } from '../src/auth.js';
import { MemoryStore } from '../src/store.js';

/** Starts the API on a free port and returns helpers bound to it. */
async function startApi() {
  const store = new MemoryStore();
  await store.init();

  const auth = createAuth({ secret: 'test-secret', logger: { warn() {} } });
  const app = createApp({ store, auth, logger: { error() {} } });
  const server = http.createServer(app);

  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;

  const call = async (method, path, { token, body } = {}) => {
    const response = await fetch(base + path, {
      method,
      headers: {
        ...(body ? { 'content-type': 'application/json' } : {}),
        ...(token ? { authorization: `Bearer ${token}` } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });

    const text = await response.text();
    let json = null;
    try {
      json = text ? JSON.parse(text) : null;
    } catch {
      json = { raw: text };
    }
    return { status: response.status, body: json };
  };

  return {
    store,
    auth,
    call,
    base,
    async close() {
      await new Promise((resolve) => server.close(resolve));
      await store.close();
    },
  };
}

test('health check reports the store it is using', async () => {
  const api = await startApi();
  try {
    const { status, body } = await api.call('GET', '/healthz');
    assert.equal(status, 200);
    assert.equal(body.ok, true);
    assert.equal(body.store, 'memory');
  } finally {
    await api.close();
  }
});

test('a guest account is created with a single call and no input', async () => {
  const api = await startApi();
  try {
    const { status, body } = await api.call('POST', '/api/auth/guest');
    assert.equal(status, 201);
    assert.equal(body.player.kind, 'guest');
    assert.ok(body.token, 'a guest must receive a usable token');
    assert.ok(body.player.displayName.length >= 3, 'guests get a readable generated name');
    assert.equal(body.player.email, null);
    assert.equal(body.player.rating, 1000);
  } finally {
    await api.close();
  }
});

test('a guest can choose their own display name', async () => {
  const api = await startApi();
  try {
    const { body } = await api.call('POST', '/api/auth/guest', {
      body: { displayName: '  Rohit  ' },
    });
    assert.equal(body.player.displayName, 'Rohit', 'names are trimmed');
  } finally {
    await api.close();
  }
});

test('display names cannot smuggle newlines or control characters onto the leaderboard', async () => {
  const api = await startApi();
  try {
    const { body } = await api.call('POST', '/api/auth/guest', {
      body: { displayName: 'Bad\n\nName\u0007' },
    });
    assert.ok(!body.player.displayName.includes('\n'));
    assert.equal(body.player.displayName, 'Bad Name');
  } finally {
    await api.close();
  }
});

test('registration rejects a bad email and a short password', async () => {
  const api = await startApi();
  try {
    const bad = await api.call('POST', '/api/auth/register', {
      body: { email: 'not-an-email', password: 'longenough1' },
    });
    assert.equal(bad.status, 400);

    const short = await api.call('POST', '/api/auth/register', {
      body: { email: 'player@example.com', password: 'abc' },
    });
    assert.equal(short.status, 400);
    assert.match(short.body.error, /8 characters/);
  } finally {
    await api.close();
  }
});

test('an account can register, then log in', async () => {
  const api = await startApi();
  try {
    const created = await api.call('POST', '/api/auth/register', {
      body: { email: 'player@example.com', password: 'hunter2hunter2', displayName: 'Player' },
    });
    assert.equal(created.status, 201);
    assert.equal(created.body.player.kind, 'account');

    const login = await api.call('POST', '/api/auth/login', {
      body: { email: 'player@example.com', password: 'hunter2hunter2' },
    });
    assert.equal(login.status, 200);
    assert.equal(login.body.player.id, created.body.player.id);
  } finally {
    await api.close();
  }
});

test('login is case-insensitive on the email', async () => {
  const api = await startApi();
  try {
    await api.call('POST', '/api/auth/register', {
      body: { email: 'Player@Example.com', password: 'hunter2hunter2' },
    });
    const login = await api.call('POST', '/api/auth/login', {
      body: { email: 'player@example.COM', password: 'hunter2hunter2' },
    });
    assert.equal(login.status, 200);
  } finally {
    await api.close();
  }
});

test('a wrong password and an unknown account are indistinguishable', async () => {
  const api = await startApi();
  try {
    await api.call('POST', '/api/auth/register', {
      body: { email: 'player@example.com', password: 'hunter2hunter2' },
    });

    const wrong = await api.call('POST', '/api/auth/login', {
      body: { email: 'player@example.com', password: 'wrongpassword' },
    });
    const missing = await api.call('POST', '/api/auth/login', {
      body: { email: 'nobody@example.com', password: 'wrongpassword' },
    });

    assert.equal(wrong.status, 401);
    assert.equal(missing.status, 401);
    assert.deepEqual(wrong.body, missing.body, 'the two must not be tellable apart');
  } finally {
    await api.close();
  }
});

test('an email cannot be registered twice', async () => {
  const api = await startApi();
  try {
    await api.call('POST', '/api/auth/register', {
      body: { email: 'player@example.com', password: 'hunter2hunter2' },
    });
    const again = await api.call('POST', '/api/auth/register', {
      body: { email: 'player@example.com', password: 'differentpass1' },
    });
    assert.equal(again.status, 409);
  } finally {
    await api.close();
  }
});

test('a guest who registers keeps the record they already earned', async () => {
  const api = await startApi();
  try {
    const guest = await api.call('POST', '/api/auth/guest');
    const rival = await api.call('POST', '/api/auth/guest');

    // Give the guest a win, so there is something to lose.
    await api.store.recordMatch({
      roomCode: 'TEST',
      winnerId: guest.body.player.id,
      loserId: rival.body.player.id,
      winnerPoints: 3,
      loserPoints: 1,
      rounds: 2,
    });

    const upgraded = await api.call('POST', '/api/auth/register', {
      token: guest.body.token,
      body: { email: 'guest@example.com', password: 'hunter2hunter2' },
    });

    assert.equal(upgraded.status, 201);
    assert.equal(upgraded.body.player.id, guest.body.player.id, 'same row, promoted in place');
    assert.equal(upgraded.body.player.kind, 'account');
    assert.equal(upgraded.body.player.wins, 1, 'the win survives the upgrade');
    assert.ok(upgraded.body.player.rating > 1000, 'so does the rating');
  } finally {
    await api.close();
  }
});

test('/api/me needs a token and returns the rank', async () => {
  const api = await startApi();
  try {
    const anonymous = await api.call('GET', '/api/me');
    assert.equal(anonymous.status, 401);

    const guest = await api.call('POST', '/api/auth/guest');
    const me = await api.call('GET', '/api/me', { token: guest.body.token });
    assert.equal(me.status, 200);
    assert.equal(me.body.player.id, guest.body.player.id);
    assert.equal(me.body.rank, null, 'unranked until a match has been played');
  } finally {
    await api.close();
  }
});

test('a garbage token is rejected rather than crashing', async () => {
  const api = await startApi();
  try {
    const { status } = await api.call('GET', '/api/me', { token: 'not.a.jwt' });
    assert.equal(status, 401);
  } finally {
    await api.close();
  }
});

test('the leaderboard is readable without signing in, and ranks guests alongside accounts',
  async () => {
    const api = await startApi();
    try {
      const guest = await api.call('POST', '/api/auth/guest', { body: { displayName: 'Guesty' } });
      const account = await api.call('POST', '/api/auth/register', {
        body: { email: 'acc@example.com', password: 'hunter2hunter2', displayName: 'Accounty' },
      });

      // The account wins twice, so it must outrank the guest.
      for (let i = 0; i < 2; i++) {
        await api.store.recordMatch({
          roomCode: 'TEST',
          winnerId: account.body.player.id,
          loserId: guest.body.player.id,
          winnerPoints: 3,
          loserPoints: 0,
          rounds: 2,
        });
      }

      const board = await api.call('GET', '/api/leaderboard');
      assert.equal(board.status, 200, 'no token required');
      assert.equal(board.body.entries.length, 2);
      assert.equal(board.body.entries[0].displayName, 'Accounty');
      assert.equal(board.body.entries[0].rank, 1);
      assert.equal(board.body.entries[1].rank, 2);
      assert.equal(board.body.me, null, 'no token, so no personal row');

      const kinds = board.body.entries.map((e) => e.kind).sort();
      assert.deepEqual(kinds, ['account', 'guest'], 'both kinds appear on one board');
    } finally {
      await api.close();
    }
  });

test('the leaderboard includes your own row when you send a token', async () => {
  const api = await startApi();
  try {
    const a = await api.call('POST', '/api/auth/guest');
    const b = await api.call('POST', '/api/auth/guest');

    await api.store.recordMatch({
      roomCode: 'TEST',
      winnerId: b.body.player.id,
      loserId: a.body.player.id,
      winnerPoints: 2,
      loserPoints: 1,
      rounds: 2,
    });

    const board = await api.call('GET', '/api/leaderboard', { token: a.body.token });
    assert.ok(board.body.me, 'a signed-in player gets their own row back');
    assert.equal(board.body.me.id, a.body.player.id);
    assert.equal(board.body.me.rank, 2);
  } finally {
    await api.close();
  }
});

test('the leaderboard can be filtered to guests only', async () => {
  const api = await startApi();
  try {
    const guest = await api.call('POST', '/api/auth/guest');
    const account = await api.call('POST', '/api/auth/register', {
      body: { email: 'acc@example.com', password: 'hunter2hunter2' },
    });

    await api.store.recordMatch({
      roomCode: 'TEST',
      winnerId: account.body.player.id,
      loserId: guest.body.player.id,
      winnerPoints: 2,
      loserPoints: 0,
      rounds: 2,
    });

    const guests = await api.call('GET', '/api/leaderboard?kind=guest');
    assert.equal(guests.body.entries.length, 1);
    assert.equal(guests.body.entries[0].kind, 'guest');
  } finally {
    await api.close();
  }
});

test('players with no matches are left off the board', async () => {
  const api = await startApi();
  try {
    await api.call('POST', '/api/auth/guest');
    const board = await api.call('GET', '/api/leaderboard');
    assert.equal(board.body.entries.length, 0, 'signing up is not an achievement');
  } finally {
    await api.close();
  }
});

test('the leaderboard limit is clamped to something sane', async () => {
  const api = await startApi();
  try {
    const huge = await api.call('GET', '/api/leaderboard?limit=99999');
    assert.equal(huge.body.limit, 200);

    const nonsense = await api.call('GET', '/api/leaderboard?limit=abc');
    assert.equal(nonsense.body.limit, 50, 'falls back to the default');
  } finally {
    await api.close();
  }
});

test('recent matches are listed newest first', async () => {
  const api = await startApi();
  try {
    const a = await api.call('POST', '/api/auth/guest', { body: { displayName: 'Ayy' } });
    const b = await api.call('POST', '/api/auth/guest', { body: { displayName: 'Bee' } });

    await api.store.recordMatch({
      roomCode: 'ONE',
      winnerId: a.body.player.id,
      loserId: b.body.player.id,
      winnerPoints: 2,
      loserPoints: 1,
      rounds: 2,
    });
    await api.store.recordMatch({
      roomCode: 'TWO',
      winnerId: b.body.player.id,
      loserId: a.body.player.id,
      winnerPoints: 3,
      loserPoints: 0,
      rounds: 2,
    });

    const { body } = await api.call('GET', '/api/matches');
    assert.equal(body.matches.length, 2);
    assert.equal(body.matches[0].winner.displayName, 'Bee');
  } finally {
    await api.close();
  }
});

test('an unknown route returns a JSON 404, not an HTML error page', async () => {
  const api = await startApi();
  try {
    const { status, body } = await api.call('GET', '/nope');
    assert.equal(status, 404);
    assert.equal(body.error, 'not found');
  } finally {
    await api.close();
  }
});
