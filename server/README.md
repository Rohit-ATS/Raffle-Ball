# Rift Rally multiplayer server

Rooms, matchmaking, accounts and the leaderboard for the VR game. Node 20+, Express for the
API, `ws` for the match socket, Postgres for persistence.

Written for: whoever deploys or maintains this service.

---

## Deploying to Render

The repository contains a [`render.yaml`](../render.yaml) blueprint, so the whole stack —
web service, Postgres database, generated `JWT_SECRET` — comes up in one step.

1. Push this repository to GitHub.
2. In Render: **New → Blueprint**, select the repository, apply.
3. Wait for the first deploy. Copy the service URL, e.g.
   `https://rift-rally-server.onrender.com`.
4. In Unity, set that URL on the `GameBootstrap` component's **Server Url** field (or on
   `RiftNetClient` directly) and rebuild the APK.

Verify a live deploy:

```bash
curl https://YOUR-SERVICE.onrender.com/healthz
# {"ok":true,"store":"postgres","uptime":12}
```

If `store` says `memory`, `DATABASE_URL` did not reach the service and **nothing is being
persisted** — the leaderboard will reset on every restart. Check the env var in the Render
dashboard.

### What the free plan costs you

An idle free instance is spun down and takes roughly 30–60 seconds to answer the first
request. The client handles this: it shows a "the free server sleeps when idle" message and
keeps retrying with backoff rather than failing. Both players see this only on the first
connection after a quiet period. Upgrading the service to a paid instance removes it.

The free Postgres plan expires after 90 days. Either upgrade it or create a new one and
update `DATABASE_URL` — the schema is recreated automatically on boot.

---

## Running locally

```bash
cd server
npm install
npm start          # http://localhost:8080, in-memory store
npm test           # 72 tests, no database needed
```

With no `DATABASE_URL` the server uses an in-memory store that implements the same interface
as Postgres. That is what makes the test suite and a clean checkout work with no setup; it is
not durable, so never run production that way.

Against a real database:

```bash
DATABASE_URL=postgres://user:pass@localhost:5432/riftrally npm start
```

The schema is applied on boot (every statement is `CREATE ... IF NOT EXISTS`), so there is no
separate migration step. `npm run migrate` exists for pipelines that want one.

---

## API

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `GET` | `/healthz` | — | Render health check; reports which store is live |
| `GET` | `/api/stats` | — | Player and match counts |
| `POST` | `/api/auth/guest` | — | Create a guest; returns a token |
| `POST` | `/api/auth/register` | optional | Create an account, or **upgrade the guest** whose token is supplied |
| `POST` | `/api/auth/login` | — | Email and password, returns a token |
| `GET` | `/api/me` | bearer | Your profile and rank |
| `PATCH` | `/api/me` | bearer | Change display name |
| `GET` | `/api/leaderboard` | optional | Top players; `?limit=` `?kind=all\|guest\|account` |
| `GET` | `/api/matches` | — | Recent results |

The leaderboard is deliberately readable with **no token at all**, so a player who has not
signed in still sees the full board.

### Guests are real accounts

A guest is a row in `players` with no email and no password hash. That one decision removes a
lot of special cases:

- Guests appear on the same leaderboard as registered accounts, with no branching in the
  ranking query. `kind` is returned so the client can badge them, not so it can split them.
- A guest who later registers is **promoted in place** — same row, same id, same rating, same
  record. Pass the guest's token to `/api/auth/register` and the account keeps everything it
  earned. Without this, signing up would cost a player their rating, so nobody would.

---

## Match protocol

JSON over a WebSocket at `/ws`. Every message has a `type`. Authenticate first with
`{"type":"auth","token":"..."}`; everything else is rejected until that succeeds.

**Client → server:** `auth`, `createRoom`, `joinRoom`, `quickMatch`, `ready`, `throw`,
`leaveRoom`, `roomState`, `listRooms`, `ping`

**Server → client:** `hello`, `authenticated`, `roomCreated`, `roomJoined`, `queued`,
`rejoined`, `roomState`, `matchStart`, `turn`, `opponentThrow`, `roundScored`, `matchOver`,
`opponentDisconnected`, `opponentReconnected`, `opponentLeft`, `opponentTimedOut`, `replaced`,
`error`, `pong`

### Why this is a turn relay, not a physics server

Rift Rally is strictly turn based: one player throws, every stone stops, then the other throws.
That removes the hard parts of netcode entirely — no simultaneous movement, no rollback, no
double simulation.

The thrower simulates their own shot locally, so **their own throw has zero latency**, which is
the one thing that must never feel networked. They then report where every stone came to rest.
The opponent replays the launch for the look of it and adopts the reported positions as truth.

The honest consequence: **the thrower is trusted for stone positions.** A modified client could
lie about where its stones stopped. The server still scores the rink from those positions
([`scoring.js`](src/scoring.js)) and owns turn order, round count and the displayed match result.
Because that is not equivalent to authoritative physics, these matches are **unranked by
default**: they remain visible in recent match history but do not change wins, losses or Elo.
`createGateway` has an explicit `rankClientAuthoritativeMatches` switch for isolated demos, but
production must leave it disabled until the server has a trustworthy physics/result source.

`scoring.js` is a deliberate duplicate of the client's `ScoreKeeper.cs`. The two test suites are
kept parallel on purpose — including the one-millimetre-lead case — so a change to the rule on
one side fails a test on the other.

### Disconnects

A dropped headset is routine: battery, guardian, someone taking it off. A live match holds the
seat open for 30 seconds and a player returning on the same token is restored to it with a full
rink snapshot. After the window the match is awarded to whoever is still there.

Leaving a stalled match open would be worse: the other player is stuck in an empty room with no
way to finish and their rating never settles. For the same reason, walking out of a live match
is a forfeit — otherwise quitting would be the correct move whenever you are behind.

---

## Layout

```
server/
  src/
    index.js     process entry; binds HTTP and the socket to one port
    app.js       HTTP API
    gateway.js   WebSocket gateway; sessions, relay, reconnection
    rooms.js     Room and RoomRegistry: turn order, hammer, matchmaking, sweeping
    scoring.js   server-side curling count (mirrors the Unity implementation)
    auth.js      tokens, password hashing, guest names, input sanitising
    store.js     Postgres and in-memory backends behind one interface
  tests/         72 tests: scoring parity, room rules, API, full two-client match
```

`Room` knows nothing about sockets, which is why the whole match flow is tested without a
network. `gateway.js` is the only file that touches both.
