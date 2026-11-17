/**
 * The HTTP API: accounts and the leaderboard.
 *
 * Everything in the match itself goes over the WebSocket (see gateway.js). HTTP handles the
 * things that are naturally request/response, and the leaderboard is deliberately open
 * without a token: a guest who has not signed in still gets to see the full board, which is
 * the whole point of ranking guests and accounts together.
 */

import express from 'express';
import cors from 'cors';
import {
  createAuth,
  hashPassword,
  isValidEmail,
  randomGuestName,
  sanitizeDisplayName,
  validatePassword,
  verifyPassword,
} from './auth.js';

export function createApp({ store, auth = createAuth(), logger = console }) {
  const app = express();

  app.disable('x-powered-by');
  app.use(express.json({ limit: '32kb' }));

  // The game client is not a browser page and sends no cookies, so a permissive origin is
  // not a credential-leak risk here; it also keeps a web-based leaderboard viewer possible.
  app.use(cors({ origin: true, credentials: false }));

  /** Pulls the bearer token and resolves it to a player, or null. */
  async function currentPlayer(req) {
    const header = req.get('authorization') ?? '';
    const token = header.toLowerCase().startsWith('bearer ') ? header.slice(7).trim() : null;
    const payload = auth.verifyToken(token);
    if (!payload?.sub) return null;
    return store.findPlayerById(payload.sub);
  }

  function requireAuth(handler) {
    return async (req, res, next) => {
      try {
        const player = await currentPlayer(req);
        if (!player) return res.status(401).json({ error: 'authentication required' });
        req.player = player;
        return await handler(req, res, next);
      } catch (err) {
        return next(err);
      }
    };
  }

  // ---------------------------------------------------------------- health

  // Render polls this to decide whether a deploy is live, so it must stay cheap and must not
  // depend on the database being reachable.
  app.get('/healthz', (_req, res) => {
    res.json({ ok: true, store: store.kind, uptime: Math.round(process.uptime()) });
  });

  app.get('/api/stats', async (_req, res, next) => {
    try {
      res.json(await store.stats());
    } catch (err) {
      next(err);
    }
  });

  // ---------------------------------------------------------------- auth

  /**
   * One pinch to play. No email, no password, no confirmation step - and the resulting
   * account is a real row that can be upgraded later without losing its record.
   */
  app.post('/api/auth/guest', async (req, res, next) => {
    try {
      const fallback = randomGuestName();
      const displayName = sanitizeDisplayName(req.body?.displayName, fallback);
      const player = await store.createPlayer({ kind: 'guest', displayName });
      res.status(201).json({ token: auth.issueToken(player), player });
    } catch (err) {
      next(err);
    }
  });

  app.post('/api/auth/register', async (req, res, next) => {
    try {
      const email = typeof req.body?.email === 'string' ? req.body.email.trim() : '';
      const password = req.body?.password;

      if (!isValidEmail(email)) return res.status(400).json({ error: 'a valid email is required' });

      const passwordProblem = validatePassword(password);
      if (passwordProblem) return res.status(400).json({ error: passwordProblem });

      const displayName = sanitizeDisplayName(req.body?.displayName, email.split('@')[0]);
      const passwordHash = await hashPassword(password);

      // An upgrade token lets a guest keep the rating they have already earned.
      const existingGuest = await currentPlayer(req);
      let player;

      if (existingGuest && existingGuest.kind === 'guest') {
        player = await store.upgradeGuest(existingGuest.id, {
          displayName,
          email,
          passwordHash,
        });
      } else {
        player = await store.createPlayer({
          kind: 'account',
          displayName,
          email,
          passwordHash,
        });
      }

      res.status(201).json({ token: auth.issueToken(player), player });
    } catch (err) {
      if (err.code === 'EMAIL_TAKEN') {
        return res.status(409).json({ error: 'that email is already registered' });
      }
      next(err);
    }
  });

  app.post('/api/auth/login', async (req, res, next) => {
    try {
      const email = typeof req.body?.email === 'string' ? req.body.email.trim() : '';
      const password = req.body?.password;

      const row = await store.findPlayerRowByEmail(email);
      const ok = row ? await verifyPassword(String(password ?? ''), row.password_hash) : false;

      // One message for both "no such account" and "wrong password", so the endpoint cannot
      // be used to find out which emails are registered.
      if (!ok) return res.status(401).json({ error: 'email or password is incorrect' });

      await store.touchPlayer(row.id);
      const player = await store.findPlayerById(row.id);
      res.json({ token: auth.issueToken(player), player });
    } catch (err) {
      next(err);
    }
  });

  app.get(
    '/api/me',
    requireAuth(async (req, res) => {
      const rank = await store.playerRank(req.player.id);
      res.json({ player: req.player, rank });
    }),
  );

  app.patch(
    '/api/me',
    requireAuth(async (req, res) => {
      const displayName = sanitizeDisplayName(req.body?.displayName, null);
      if (!displayName) return res.status(400).json({ error: 'displayName is too short' });
      const player = await store.renamePlayer(req.player.id, displayName);
      res.json({ player });
    }),
  );

  // ---------------------------------------------------------------- leaderboard

  /**
   * Open to everyone, including players with no token at all. Guests and accounts are ranked
   * in one table; `kind` is returned so the client can badge them, not so it can split them.
   */
  app.get('/api/leaderboard', async (req, res, next) => {
    try {
      const limit = clampInt(req.query.limit, 1, 200, 50);
      const kind = ['all', 'guest', 'account'].includes(req.query.kind) ? req.query.kind : 'all';

      const entries = await store.leaderboard({ limit, kind });

      let me = null;
      const player = await currentPlayer(req);
      if (player) {
        const rank = await store.playerRank(player.id);
        me = { ...player, rank };
      }

      res.json({ entries, me, kind, limit });
    } catch (err) {
      next(err);
    }
  });

  app.get('/api/matches', async (req, res, next) => {
    try {
      const limit = clampInt(req.query.limit, 1, 100, 20);
      res.json({ matches: await store.recentMatches(limit) });
    } catch (err) {
      next(err);
    }
  });

  // ---------------------------------------------------------------- errors

  app.use((_req, res) => res.status(404).json({ error: 'not found' }));

  app.use((err, _req, res, _next) => {
    // Expected, named failures carry their own message; anything else is a bug and must not
    // leak internals to the client, so it is logged here and reported generically.
    if (err?.expected) {
      return res.status(400).json({ error: err.message, code: err.code });
    }
    logger.error?.('[api] unhandled error', err);
    res.status(500).json({ error: 'internal error' });
  });

  return app;
}

function clampInt(value, min, max, fallback) {
  const n = Number.parseInt(value, 10);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}
