# 3D Chess game server (Cloudflare)

Free-tier game server for online play between the iPhone and web builds.
Runs on Cloudflare Workers; each game room is a Durable Object (SQLite-backed,
the kind included in Cloudflare's free plan). The chess rules in `src/game` are
ported from `server-v2` and validate every move on the server.

## Endpoints

| Method | Path | What it does |
|---|---|---|
| GET | `/health` | `{ ok: true }` |
| POST | `/auth/guest` `{ name }` | Guest token: `{ token, userId, name }` |
| POST | `/rooms` `{ config, isPublic? }` | Create a room (header `Authorization: Bearer <token>`) → `{ code, config }` |
| GET | `/rooms/:code` | Room summary: players, open seats |
| GET (WebSocket) | `/rooms/:code/connect?token=…` | Join / rejoin a room |
| GET (WebSocket) | `/matchmaking/connect?token=…` | Find Match queue |
| GET | `/blitz/stats?pack=…` | Queen Blitz difficulty totals per puzzle (header `X-Stats-Key`: the `STATS_KEY` secret; read with `Tools/QueenBlitz/stats.py`) |

## Room messages (JSON over WebSocket)

Client → server (add `requestId` to get an `{ type: 'ack', requestId, ok, error? }` reply):
`game:placePiece {pieceType, position}`, `game:ready`, `game:move {from, to}`,
`game:promotePawn {position, pieceType}`, `game:resign`, `game:offerDraw`,
`game:respondDraw {accept}`, `room:state`, `ping`.

Server → client: `room:joined {roomCode, yourColor, isHost, state}`,
`room:playerJoined`, `room:playerLeft {graceMs}`, `room:playerReconnected`,
`game:piecePlaced`, `game:playerReady`, `game:phaseChanged`, `game:moveValidated`, `game:moveRejected`,
`game:pawnPromotion`, `game:chaosRotation`, `game:playerEliminated`,
`game:drawOffered`, `game:over {winner, reason}`, `game:stateUpdate {reason, state}`.

Matchmaking: send `matchmaking:join {config}`; receive `matchmaking:queued`, then
`matchmaking:found {roomCode}`, and connect to that room.

## Develop

```bash
npm install
npm run dev          # local server on http://localhost:8787 (uses .dev.vars)
npm run test:rules   # chess rule tests
npm run test:e2e     # two simulated players against the local server
```

`.dev.vars` holds a local `TOKEN_SECRET` and is not committed.

## Deploy

```bash
npx wrangler login                    # once, approves this Mac in the browser
npx wrangler secret put TOKEN_SECRET  # once, any long random string
npm run deploy
```

Queen Blitz solve-time totals need a key, once: `npx wrangler secret put STATS_KEY` (a long random string, 16+
characters); save the same string in `~/.appstore-publisher/keys/blitz-stats-key.txt` for `Tools/QueenBlitz/stats.py`.
