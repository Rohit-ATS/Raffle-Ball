/**
 * Process entry point.
 *
 * Binds HTTP and the WebSocket gateway to one port, which is what Render expects: a single
 * $PORT per service. Shutdown is handled explicitly so a deploy drains cleanly instead of
 * cutting live matches off mid-throw.
 */

import http from 'node:http';
import { createApp } from './app.js';
import { createAuth } from './auth.js';
import { createGateway } from './gateway.js';
import { createStore } from './store.js';

const PORT = Number(process.env.PORT) || 8080;
const HOST = process.env.HOST || '0.0.0.0';

async function main() {
  const store = await createStore();
  const auth = createAuth();

  const app = createApp({ store, auth });
  const server = http.createServer(app);
  const gateway = createGateway({ server, store, auth });

  server.listen(PORT, HOST, () => {
    console.log(`[rift-rally] listening on http://${HOST}:${PORT} (store: ${store.kind})`);
    console.log(`[rift-rally] websocket endpoint: ws://${HOST}:${PORT}/ws`);
  });

  let shuttingDown = false;
  const shutdown = async (signal) => {
    if (shuttingDown) return;
    shuttingDown = true;
    console.log(`[rift-rally] ${signal} received, shutting down`);

    // Close the sockets first: a client that sees a clean close reconnects, whereas one cut
    // off by the process exiting sits waiting for a timeout.
    await gateway.close();
    server.close(async () => {
      await store.close();
      process.exit(0);
    });

    // Render sends SIGKILL after 30 seconds; leave rather than be killed mid-write.
    setTimeout(() => process.exit(0), 10_000).unref();
  };

  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGINT', () => shutdown('SIGINT'));
}

main().catch((err) => {
  console.error('[rift-rally] failed to start', err);
  process.exit(1);
});
