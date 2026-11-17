/**
 * Accounts and tokens.
 *
 * The design goal is that a player can be in a match within two seconds of putting the
 * headset on. Typing an email address on a floating keyboard with hand tracking is genuinely
 * unpleasant, so the default path is a guest account created with one pinch, and signing up
 * is an upgrade of that same account rather than a prerequisite for playing. A guest who
 * later registers keeps their rating, their record and their place on the leaderboard,
 * because the row is promoted in place instead of a second row being created.
 */

import bcrypt from 'bcryptjs';
import jwt from 'jsonwebtoken';

const TOKEN_TTL = '60d'; // headsets are shared and sign-in is friction; long-lived tokens

/** Cost 10: around 60 ms on Render's smallest instance, which is the right trade here. */
const BCRYPT_ROUNDS = 10;

const ADJECTIVES = [
  'Swift', 'Frost', 'Iron', 'Lunar', 'Solar', 'Rapid', 'Glacial', 'Vivid',
  'Quiet', 'Bold', 'Nimble', 'Stellar', 'Crisp', 'Keen', 'Arc', 'Neon',
];
const NOUNS = [
  'Slider', 'Comet', 'Curler', 'Rook', 'Falcon', 'Pilot', 'Drifter', 'Shot',
  'Hammer', 'Rally', 'Striker', 'Vector', 'Echo', 'Quartz', 'Lynx', 'Bolt',
];

export function randomGuestName(random = Math.random) {
  const adjective = ADJECTIVES[Math.floor(random() * ADJECTIVES.length)];
  const noun = NOUNS[Math.floor(random() * NOUNS.length)];
  const number = 100 + Math.floor(random() * 900);
  return `${adjective}${noun}${number}`;
}

export function isValidEmail(email) {
  if (typeof email !== 'string') return false;
  if (email.length > 254) return false;
  // Deliberately permissive: the authoritative check is whether the address works, and
  // over-strict patterns reject real addresses.
  return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(email.trim());
}

export function validatePassword(password) {
  if (typeof password !== 'string') return 'password is required';
  if (password.length < 8) return 'password must be at least 8 characters';
  if (password.length > 200) return 'password must be shorter than 200 characters';
  return null;
}

export function sanitizeDisplayName(name, fallback) {
  if (typeof name !== 'string') return fallback;
  // Collapse whitespace and strip control characters: this string is shown to other players
  // on the leaderboard, so it must not be able to contain newlines or invisible padding.
  // Control characters become spaces rather than being deleted: deleting a newline would
  // weld two words together and quietly change the name someone chose.
  const cleaned = name.replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim();
  if (cleaned.length < 2) return fallback;
  return cleaned.slice(0, 24);
}

export function hashPassword(password) {
  return bcrypt.hash(password, BCRYPT_ROUNDS);
}

export function verifyPassword(password, hash) {
  if (!hash) return Promise.resolve(false);
  return bcrypt.compare(password, hash);
}

export function createAuth({ secret = process.env.JWT_SECRET, logger = console } = {}) {
  let signingSecret = secret;

  if (!signingSecret) {
    // A random per-boot secret keeps a misconfigured deploy secure rather than convenient:
    // tokens simply stop working on restart, which is loud and obvious, instead of every
    // deployment sharing one well-known key.
    signingSecret = randomSecret();
    logger.warn?.(
      '[auth] JWT_SECRET is not set; using a random per-process secret. ' +
        'Sessions will not survive a restart. Set JWT_SECRET in production.',
    );
  }

  return {
    issueToken(player) {
      return jwt.sign(
        { sub: player.id, kind: player.kind, name: player.displayName },
        signingSecret,
        { expiresIn: TOKEN_TTL },
      );
    },

    /** Returns the payload, or null for anything invalid, expired or malformed. */
    verifyToken(token) {
      if (!token || typeof token !== 'string') return null;
      try {
        return jwt.verify(token, signingSecret);
      } catch {
        return null;
      }
    },
  };
}

function randomSecret() {
  const bytes = new Uint8Array(48);
  globalThis.crypto.getRandomValues(bytes);
  return Buffer.from(bytes).toString('base64url');
}
