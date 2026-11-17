/**
 * Applies the schema. Safe to run repeatedly: every statement is CREATE ... IF NOT EXISTS,
 * so this doubles as the Render pre-deploy step and as local setup.
 */

import { createStore } from './store.js';

const store = await createStore();

if (store.kind !== 'postgres') {
  console.log('[migrate] no DATABASE_URL set; nothing to migrate (memory store is schemaless)');
  process.exit(0);
}

console.log('[migrate] schema applied');
await store.close();
process.exit(0);
