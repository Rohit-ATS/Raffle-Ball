/**
 * Persistence.
 *
 * Two interchangeable backends behind one interface: Postgres when DATABASE_URL is set, and
 * an in-memory store when it is not. That is not a convenience shim - it is what lets the
 * whole test suite and a local dev session run with no database installed, while production
 * on Render uses real Postgres through exactly the same calls. Any query that works in one
 * must work in the other, which is enforced by running the test suite against both.
 *
 * Guests are first-class rows, not a special case. A guest is simply a player with no email
 * and no password hash, which is why guests appear on the same leaderboard as registered
 * accounts without a single branch in the ranking query.
 */

import { randomUUID } from 'node:crypto';

const SCHEMA = `
CREATE TABLE IF NOT EXISTS players (
  id             TEXT PRIMARY KEY,
  kind           TEXT NOT NULL CHECK (kind IN ('guest', 'account')),
  display_name   TEXT NOT NULL,
  email          TEXT UNIQUE,
  password_hash  TEXT,
  rating         INTEGER NOT NULL DEFAULT 1000,
  wins           INTEGER NOT NULL DEFAULT 0,
  losses         INTEGER NOT NULL DEFAULT 0,
  points_for     INTEGER NOT NULL DEFAULT 0,
  points_against INTEGER NOT NULL DEFAULT 0,
  best_round     INTEGER NOT NULL DEFAULT 0,
  matches        INTEGER NOT NULL DEFAULT 0,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS matches (
  id             TEXT PRIMARY KEY,
  room_code      TEXT,
  winner_id      TEXT REFERENCES players(id) ON DELETE SET NULL,
  loser_id       TEXT REFERENCES players(id) ON DELETE SET NULL,
  winner_points  INTEGER NOT NULL DEFAULT 0,
  loser_points   INTEGER NOT NULL DEFAULT 0,
  rounds         INTEGER NOT NULL DEFAULT 0,
  ended_reason   TEXT NOT NULL DEFAULT 'completed',
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS players_rating_idx ON players (rating DESC, wins DESC);
CREATE INDEX IF NOT EXISTS matches_created_idx ON matches (created_at DESC);
`;

/** Shape every backend returns, so callers never need to know which one they have. */
function publicPlayer(row) {
  if (!row) return null;
  return {
    id: row.id,
    kind: row.kind,
    displayName: row.display_name,
    rating: row.rating,
    wins: row.wins,
    losses: row.losses,
    matches: row.matches,
    pointsFor: row.points_for,
    pointsAgainst: row.points_against,
    bestRound: row.best_round,
  };
}

class MemoryStore {
  constructor() {
    this.players = new Map();
    this.matches = [];
    this.kind = 'memory';
  }

  async init() {}
  async close() {}

  async createPlayer({ kind, displayName, email = null, passwordHash = null }) {
    if (email) {
      const taken = [...this.players.values()].some(
        (p) => p.email && p.email.toLowerCase() === email.toLowerCase(),
      );
      if (taken) throw Object.assign(new Error('email already registered'), { code: 'EMAIL_TAKEN' });
    }

    const row = {
      id: randomUUID(),
      kind,
      display_name: displayName,
      email,
      password_hash: passwordHash,
      rating: 1000,
      wins: 0,
      losses: 0,
      points_for: 0,
      points_against: 0,
      best_round: 0,
      matches: 0,
      created_at: new Date(),
      last_seen_at: new Date(),
    };
    this.players.set(row.id, row);
    return publicPlayer(row);
  }

  async findPlayerById(id) {
    return publicPlayer(this.players.get(id));
  }

  async findPlayerRowByEmail(email) {
    if (!email) return null;
    for (const row of this.players.values()) {
      if (row.email && row.email.toLowerCase() === email.toLowerCase()) return row;
    }
    return null;
  }

  async touchPlayer(id) {
    const row = this.players.get(id);
    if (row) row.last_seen_at = new Date();
  }

  async renamePlayer(id, displayName) {
    const row = this.players.get(id);
    if (!row) return null;
    row.display_name = displayName;
    return publicPlayer(row);
  }

  /** Promotes a guest in place, so a guest who signs up keeps the record they just earned. */
  async upgradeGuest(id, { displayName, email, passwordHash }) {
    const row = this.players.get(id);
    if (!row) return null;
    if (row.kind !== 'guest') {
      throw Object.assign(new Error('not a guest'), { code: 'NOT_GUEST' });
    }
    const existing = await this.findPlayerRowByEmail(email);
    if (existing) throw Object.assign(new Error('email already registered'), { code: 'EMAIL_TAKEN' });

    row.kind = 'account';
    row.email = email;
    row.password_hash = passwordHash;
    if (displayName) row.display_name = displayName;
    return publicPlayer(row);
  }

  async recordMatch({
    roomCode, winnerId, loserId, winnerPoints, loserPoints, rounds, endedReason, rated = true,
  }) {
    const match = {
      id: randomUUID(),
      room_code: roomCode ?? null,
      winner_id: winnerId,
      loser_id: loserId,
      winner_points: winnerPoints,
      loser_points: loserPoints,
      rounds,
      ended_reason: endedReason ?? 'completed',
      created_at: new Date(),
    };
    this.matches.unshift(match);

    const winner = this.players.get(winnerId);
    const loser = this.players.get(loserId);

    if (rated && winner) {
      winner.wins += 1;
      winner.matches += 1;
      winner.points_for += winnerPoints;
      winner.points_against += loserPoints;
      winner.best_round = Math.max(winner.best_round, winnerPoints);
    }
    if (rated && loser) {
      loser.losses += 1;
      loser.matches += 1;
      loser.points_for += loserPoints;
      loser.points_against += winnerPoints;
      loser.best_round = Math.max(loser.best_round, loserPoints);
    }

    if (rated && winner && loser) {
      const { winnerRating, loserRating } = elo(winner.rating, loser.rating);
      winner.rating = winnerRating;
      loser.rating = loserRating;
    }

    return { id: match.id, winner: publicPlayer(winner), loser: publicPlayer(loser) };
  }

  async leaderboard({ limit = 50, kind = 'all' } = {}) {
    let rows = [...this.players.values()].filter((p) => p.matches > 0);
    if (kind !== 'all') rows = rows.filter((p) => p.kind === kind);

    rows.sort(
      (a, b) =>
        b.rating - a.rating ||
        b.wins - a.wins ||
        a.losses - b.losses ||
        b.points_for - a.points_for ||
        a.display_name.localeCompare(b.display_name),
    );

    return rows.slice(0, limit).map((row, i) => ({ rank: i + 1, ...publicPlayer(row) }));
  }

  async playerRank(id) {
    const all = await this.leaderboard({ limit: Number.MAX_SAFE_INTEGER, kind: 'all' });
    const found = all.find((p) => p.id === id);
    return found ? found.rank : null;
  }

  async recentMatches(limit = 20) {
    return this.matches.slice(0, limit).map((m) => ({
      id: m.id,
      winner: publicPlayer(this.players.get(m.winner_id)),
      loser: publicPlayer(this.players.get(m.loser_id)),
      winnerPoints: m.winner_points,
      loserPoints: m.loser_points,
      rounds: m.rounds,
      endedReason: m.ended_reason,
      createdAt: m.created_at,
    }));
  }

  async stats() {
    return {
      players: this.players.size,
      guests: [...this.players.values()].filter((p) => p.kind === 'guest').length,
      accounts: [...this.players.values()].filter((p) => p.kind === 'account').length,
      matches: this.matches.length,
    };
  }
}

class PostgresStore {
  constructor(pool) {
    this.pool = pool;
    this.kind = 'postgres';
  }

  async init() {
    await this.pool.query(SCHEMA);
  }

  async close() {
    await this.pool.end();
  }

  async createPlayer({ kind, displayName, email = null, passwordHash = null }) {
    try {
      const { rows } = await this.pool.query(
        `INSERT INTO players (id, kind, display_name, email, password_hash)
         VALUES ($1, $2, $3, $4, $5) RETURNING *`,
        [randomUUID(), kind, displayName, email, passwordHash],
      );
      return publicPlayer(rows[0]);
    } catch (err) {
      // 23505 is a unique violation, which here can only be the email index.
      if (err.code === '23505') {
        throw Object.assign(new Error('email already registered'), { code: 'EMAIL_TAKEN' });
      }
      throw err;
    }
  }

  async findPlayerById(id) {
    const { rows } = await this.pool.query('SELECT * FROM players WHERE id = $1', [id]);
    return publicPlayer(rows[0]);
  }

  async findPlayerRowByEmail(email) {
    if (!email) return null;
    const { rows } = await this.pool.query('SELECT * FROM players WHERE LOWER(email) = LOWER($1)', [
      email,
    ]);
    return rows[0] ?? null;
  }

  async touchPlayer(id) {
    await this.pool.query('UPDATE players SET last_seen_at = NOW() WHERE id = $1', [id]);
  }

  async renamePlayer(id, displayName) {
    const { rows } = await this.pool.query(
      'UPDATE players SET display_name = $2 WHERE id = $1 RETURNING *',
      [id, displayName],
    );
    return publicPlayer(rows[0]);
  }

  async upgradeGuest(id, { displayName, email, passwordHash }) {
    const existing = await this.findPlayerRowByEmail(email);
    if (existing) throw Object.assign(new Error('email already registered'), { code: 'EMAIL_TAKEN' });

    const { rows } = await this.pool.query(
      `UPDATE players
          SET kind = 'account',
              email = $2,
              password_hash = $3,
              display_name = COALESCE($4, display_name)
        WHERE id = $1 AND kind = 'guest'
      RETURNING *`,
      [id, email, passwordHash, displayName ?? null],
    );

    if (!rows[0]) throw Object.assign(new Error('not a guest'), { code: 'NOT_GUEST' });
    return publicPlayer(rows[0]);
  }

  async recordMatch({
    roomCode, winnerId, loserId, winnerPoints, loserPoints, rounds, endedReason, rated = true,
  }) {
    const client = await this.pool.connect();
    try {
      await client.query('BEGIN');

      const { rows: winnerRows } = await client.query('SELECT * FROM players WHERE id = $1', [
        winnerId,
      ]);
      const { rows: loserRows } = await client.query('SELECT * FROM players WHERE id = $1', [
        loserId,
      ]);

      const winnerBefore = winnerRows[0];
      const loserBefore = loserRows[0];
      if (!winnerBefore || !loserBefore) {
        await client.query('ROLLBACK');
        throw Object.assign(new Error('unknown player in match'), { code: 'UNKNOWN_PLAYER' });
      }

      const { winnerRating, loserRating } = rated
        ? elo(winnerBefore.rating, loserBefore.rating)
        : { winnerRating: winnerBefore.rating, loserRating: loserBefore.rating };
      const id = randomUUID();

      await client.query(
        `INSERT INTO matches
           (id, room_code, winner_id, loser_id, winner_points, loser_points, rounds, ended_reason)
         VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
        [id, roomCode ?? null, winnerId, loserId, winnerPoints, loserPoints, rounds,
         endedReason ?? 'completed'],
      );

      const { rows: updatedWinner } = await client.query(
        `UPDATE players
            SET wins = wins + $5,
                matches = matches + $5,
                points_for = points_for + $2,
                points_against = points_against + $3,
                best_round = GREATEST(best_round, $2),
                rating = $4
          WHERE id = $1 RETURNING *`,
        [winnerId, rated ? winnerPoints : 0, rated ? loserPoints : 0, winnerRating, rated ? 1 : 0],
      );

      const { rows: updatedLoser } = await client.query(
        `UPDATE players
            SET losses = losses + $5,
                matches = matches + $5,
                points_for = points_for + $2,
                points_against = points_against + $3,
                best_round = GREATEST(best_round, $2),
                rating = $4
          WHERE id = $1 RETURNING *`,
        [loserId, rated ? loserPoints : 0, rated ? winnerPoints : 0, loserRating, rated ? 1 : 0],
      );

      await client.query('COMMIT');
      return {
        id,
        winner: publicPlayer(updatedWinner[0]),
        loser: publicPlayer(updatedLoser[0]),
      };
    } catch (err) {
      try {
        await client.query('ROLLBACK');
      } catch {
        /* the transaction is already gone; the original error is what matters */
      }
      throw err;
    } finally {
      client.release();
    }
  }

  async leaderboard({ limit = 50, kind = 'all' } = {}) {
    const params = [limit];
    let filter = 'WHERE matches > 0';
    if (kind !== 'all') {
      filter += ' AND kind = $2';
      params.push(kind);
    }

    const { rows } = await this.pool.query(
      `SELECT *, ROW_NUMBER() OVER (
         ORDER BY rating DESC, wins DESC, losses ASC, points_for DESC, display_name ASC
       ) AS rank
       FROM players ${filter}
       ORDER BY rank
       LIMIT $1`,
      params,
    );

    return rows.map((row) => ({ rank: Number(row.rank), ...publicPlayer(row) }));
  }

  async playerRank(id) {
    const { rows } = await this.pool.query(
      `SELECT rank FROM (
         SELECT id, ROW_NUMBER() OVER (
           ORDER BY rating DESC, wins DESC, losses ASC, points_for DESC, display_name ASC
         ) AS rank
         FROM players WHERE matches > 0
       ) ranked WHERE id = $1`,
      [id],
    );
    return rows[0] ? Number(rows[0].rank) : null;
  }

  async recentMatches(limit = 20) {
    const { rows } = await this.pool.query(
      `SELECT m.*,
              w.display_name AS winner_name, w.kind AS winner_kind, w.rating AS winner_rating,
              l.display_name AS loser_name,  l.kind AS loser_kind,  l.rating AS loser_rating
         FROM matches m
         LEFT JOIN players w ON w.id = m.winner_id
         LEFT JOIN players l ON l.id = m.loser_id
        ORDER BY m.created_at DESC
        LIMIT $1`,
      [limit],
    );

    return rows.map((m) => ({
      id: m.id,
      winner: { id: m.winner_id, displayName: m.winner_name, kind: m.winner_kind,
                rating: m.winner_rating },
      loser: { id: m.loser_id, displayName: m.loser_name, kind: m.loser_kind,
               rating: m.loser_rating },
      winnerPoints: m.winner_points,
      loserPoints: m.loser_points,
      rounds: m.rounds,
      endedReason: m.ended_reason,
      createdAt: m.created_at,
    }));
  }

  async stats() {
    const { rows } = await this.pool.query(
      `SELECT
         (SELECT COUNT(*) FROM players) AS players,
         (SELECT COUNT(*) FROM players WHERE kind = 'guest') AS guests,
         (SELECT COUNT(*) FROM players WHERE kind = 'account') AS accounts,
         (SELECT COUNT(*) FROM matches) AS matches`,
    );
    const row = rows[0];
    return {
      players: Number(row.players),
      guests: Number(row.guests),
      accounts: Number(row.accounts),
      matches: Number(row.matches),
    };
  }
}

/**
 * Standard Elo with a fixed K. Chosen over a "wins only" ladder because 1v1 matchmaking needs
 * a number that means something after three games as well as after three hundred, and because
 * beating a strong opponent should be worth more than farming a weak one.
 */
export function elo(winnerRating, loserRating, k = 28) {
  const expected = 1 / (1 + 10 ** ((loserRating - winnerRating) / 400));
  const delta = Math.round(k * (1 - expected));
  return {
    winnerRating: winnerRating + delta,
    loserRating: Math.max(100, loserRating - delta),
    delta,
  };
}

/**
 * Builds the store. Falls back to memory when no DATABASE_URL is configured, which is what
 * makes `npm test` and a bare `npm start` work on a clean machine.
 */
export function postgresSslOptions({
  databaseUrl,
  mode = process.env.DATABASE_SSL_MODE,
  ca = process.env.DATABASE_CA,
} = {}) {
  const parsed = new URL(databaseUrl);
  const loopback = ['localhost', '127.0.0.1', '::1'].includes(parsed.hostname);
  const selected = mode || (loopback ? 'disable' : 'verify-full');

  if (selected === 'disable') return false;
  if (selected !== 'verify-full') {
    throw new Error('DATABASE_SSL_MODE must be either disable or verify-full');
  }

  return { rejectUnauthorized: true, ...(ca ? { ca } : {}) };
}

export async function createStore({
  databaseUrl = process.env.DATABASE_URL,
  databaseSslMode = process.env.DATABASE_SSL_MODE,
  databaseCa = process.env.DATABASE_CA,
  logger = console,
} = {}) {
  if (!databaseUrl) {
    logger.warn?.('[store] no DATABASE_URL set; using the in-memory store (data is not durable)');
    const store = new MemoryStore();
    await store.init();
    return store;
  }

  const { default: pg } = await import('pg');
  const pool = new pg.Pool({
    connectionString: databaseUrl,
    ssl: postgresSslOptions({ databaseUrl, mode: databaseSslMode, ca: databaseCa }),
    max: 8,
    idleTimeoutMillis: 30_000,
  });

  const store = new PostgresStore(pool);
  await store.init();
  logger.log?.('[store] connected to Postgres');
  return store;
}

export { MemoryStore, PostgresStore, SCHEMA, publicPlayer };
