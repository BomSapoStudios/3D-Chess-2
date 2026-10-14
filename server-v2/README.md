# 3D Chess Server v2

Authoritative TypeScript game server for 3D Chess. Supports Unity desktop, web browser, and iOS clients via a unified Socket.IO protocol.

## What's in here

| Layer | Technology |
|---|---|
| Language | TypeScript 5.3 (strict) |
| HTTP + WebSocket | Express 4 + Socket.IO 4 |
| Auth | JWT (access + refresh tokens) + guest sessions |
| Database | Prisma ORM — SQLite in dev, PostgreSQL in prod |
| Game logic | Authoritative server-side validation |
| Logging | Winston |

## First-time setup (run once on your Mac)

```bash
cd "3D Chess 2/server-v2"
chmod +x setup.sh
./setup.sh
```

This installs npm packages, generates the Prisma client, and creates a local SQLite database. No external services needed for development.

## Running the server

```bash
npm run dev      # Hot-reload development server on port 3001
npm run build    # Compile TypeScript → dist/
npm start        # Run compiled server
npm test         # Run all tests
```

## Architecture

```
src/
├── index.ts               — server entry point
├── types/
│   ├── game.ts            — GameState, BoardPosition, PlayerColor, etc.
│   └── network.ts         — all Socket.IO event payloads + EVENTS constants
├── auth/
│   ├── auth.service.ts    — JWT, register, login, guest sessions
│   ├── auth.middleware.ts — Express + Socket.IO auth middleware
│   └── auth.router.ts     — /auth/* REST endpoints
├── game/
│   ├── ChessBoard.ts      — 3D board state
│   ├── pieces/            — King, Queen, Rook, Bishop, Knight, Pawn
│   ├── CheckDetection.ts  — threat analysis, check/checkmate/stalemate
│   ├── GameEndDetection.ts — end conditions
│   ├── ChaosRotation.ts   — Rubik's-cube board rotations
│   └── GameEngine.ts      — authoritative game coordinator
├── rooms/
│   └── RoomManager.ts     — in-memory room store, disconnect grace periods
├── sockets/
│   └── gameHandler.ts     — all Socket.IO event handlers
├── api/
│   └── rooms.router.ts    — /rooms REST endpoints
├── db/
│   └── client.ts          — Prisma client singleton
└── utils/
    ├── env.ts             — typed environment variables
    └── logger.ts          — Winston logger
```

## REST API

| Method | Path | Description |
|---|---|---|
| POST | /auth/register | Create account |
| POST | /auth/login | Login → JWT |
| POST | /auth/guest | Auto-create guest JWT (for Unity quick-play) |
| POST | /auth/refresh | Refresh access token |
| DELETE | /auth/logout | Revoke refresh token |
| GET | /rooms | List public rooms |
| GET | /rooms/:code | Room details |
| GET | /health | Server health check |

## Socket.IO Events

See `src/types/network.ts` for the complete typed event protocol.
Use the `EVENTS` constants to avoid hardcoding strings.

**Quick reference — client → server:**
```
room:create       { config, playerName }
room:join         { roomCode, playerName }
room:leave        { roomCode }
game:placePiece   { roomCode, pieceType, position:{x,y,z} }
game:ready        { roomCode }
game:move         { roomCode, from:{x,y,z}, to:{x,y,z} }
game:promotePawn  { roomCode, position, pieceType }
game:resign       { roomCode }
game:offerDraw    { roomCode }
game:respondDraw  { roomCode, accept:boolean }
```

**Server → client:**
```
connected           { userId, username, socketId, serverVersion }
room:joined         { roomCode, assignedColor, gameState, isHost }
room:playerJoined   { userId, username, color, isAI }
room:playerLeft     { userId, color, reason, gracePeriodMs }
game:moveValidated  { move, newBoardState, nextTurn, turnNumber }
game:moveRejected   { reason, attemptedMove }
game:checkUpdate    { checkStatuses[] }
game:pawnPromotion  { position, playerColor, validPromotions }
game:chaosRotation  { rotation, newBoardState, nextChaosIn }
game:playerEliminated { eliminatedColor, conqueredBy? }
game:over           { winner, endReason, finalState }
game:stateUpdate    { gameState, reason }
```

## Production deployment

1. **PostgreSQL** — get a free database at [supabase.com](https://supabase.com)
   - Copy the `DATABASE_URL` connection string to `.env`
   - Change the Prisma schema `provider` from `"sqlite"` to `"postgresql"`
   - Run `npm run db:migrate`

2. **Redis** (optional, for horizontal scaling) — free at [upstash.com](https://upstash.com)
   - Set `REDIS_URL` in `.env`

3. **Deploy** — push to [Railway](https://railway.app) or [Render](https://render.com)
   - Both support Node.js with auto-deploy from GitHub
   - Set your env vars in their dashboard

4. **Update Unity** — change `ServerUrlConfig.cs` to point to your deployed URL

## Backward compatibility with existing Unity clients

Legacy Unity clients that pass `"UNITY"` as the auth token are automatically
upgraded to guest sessions. No changes required to the existing Unity game to
connect to this server.

## Environment variables

See `.env.example` for all variables with documentation.
