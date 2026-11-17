import test from 'node:test';
import assert from 'node:assert/strict';
import { MemoryStore, postgresSslOptions } from '../src/store.js';

test('remote PostgreSQL defaults to verified TLS', () => {
  assert.deepEqual(
    postgresSslOptions({ databaseUrl: 'postgres://user:pass@db.example.com/game' }),
    { rejectUnauthorized: true },
  );
});

test('PostgreSQL CA material is passed to verified TLS', () => {
  assert.deepEqual(
    postgresSslOptions({
      databaseUrl: 'postgres://user:pass@db.example.com/game',
      ca: 'test-ca',
    }),
    { rejectUnauthorized: true, ca: 'test-ca' },
  );
});

test('loopback and explicit private-network mode disable TLS without substring guessing', () => {
  assert.equal(postgresSslOptions({ databaseUrl: 'postgres://u:p@localhost/db' }), false);
  assert.equal(postgresSslOptions({ databaseUrl: 'postgres://u:p@127.0.0.1/db' }), false);
  assert.equal(
    postgresSslOptions({ databaseUrl: 'postgres://u:p@private-db/db', mode: 'disable' }),
    false,
  );
  assert.throws(
    () => postgresSslOptions({ databaseUrl: 'postgres://u:p@db.example/db', mode: 'require' }),
    /DATABASE_SSL_MODE/,
  );
});

test('unrated match history cannot change wins, losses, matches, points, or Elo', async () => {
  const store = new MemoryStore();
  const winner = await store.createPlayer({ kind: 'guest', displayName: 'Winner' });
  const loser = await store.createPlayer({ kind: 'guest', displayName: 'Loser' });

  await store.recordMatch({
    roomCode: 'SAFE',
    winnerId: winner.id,
    loserId: loser.id,
    winnerPoints: 99,
    loserPoints: 0,
    rounds: 2,
    rated: false,
  });

  const afterWinner = await store.findPlayerById(winner.id);
  const afterLoser = await store.findPlayerById(loser.id);
  assert.deepEqual(
    [afterWinner.wins, afterWinner.matches, afterWinner.pointsFor, afterWinner.rating],
    [0, 0, 0, 1000],
  );
  assert.deepEqual(
    [afterLoser.losses, afterLoser.matches, afterLoser.pointsAgainst, afterLoser.rating],
    [0, 0, 0, 1000],
  );
  assert.equal((await store.recentMatches(10)).length, 1, 'casual match history is preserved');
});
