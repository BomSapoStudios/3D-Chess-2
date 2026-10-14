import 'dotenv/config';
import express from 'express';
import { createServer } from 'http';
import { Server as SocketIOServer } from 'socket.io';
import cors from 'cors';
import helmet from 'helmet';

import { env } from './utils/env.js';
import { logger } from './utils/logger.js';
import { prisma } from './db/client.js';
import { authRouter } from './auth/auth.router.js';
import { roomsRouter } from './api/rooms.router.js';
import { socketAuthMiddleware } from './auth/auth.middleware.js';
import { registerGameHandlers } from './sockets/gameHandler.js';
import { AuthService } from './auth/auth.service.js';

// ─────────────────────────────────────────────────────────────────────────────
// 3D Chess Server v2 — Entry Point
// ─────────────────────────────────────────────────────────────────────────────

const app = express();
const httpServer = createServer(app);

// ── Middleware ────────────────────────────────────────────────────────────────

app.use(helmet({
  contentSecurityPolicy: false, // Relaxed for WebGL/Unity clients
  crossOriginEmbedderPolicy: false,
}));
app.use(cors({
  origin: env.isProduction ? env.ALLOWED_ORIGINS : true,
  credentials: true,
}));
app.use(express.json());

// ── REST API ──────────────────────────────────────────────────────────────────

app.get('/health', (_, res) => res.json({ status: 'ok', version: '2.0.0' }));
app.get('/', (_, res) => res.json({
  name: '3D Chess Server v2',
  version: '2.0.0',
  status: 'running',
  endpoints: ['/health', '/auth/*', '/rooms', '/api/rooms'],
}));

app.use('/auth', authRouter);
app.use('/rooms', roomsRouter);
app.use('/api/rooms', roomsRouter); // alias for backward compat

// ── Socket.IO ─────────────────────────────────────────────────────────────────

const io = new SocketIOServer(httpServer, {
  cors: {
    origin: env.isProduction ? env.ALLOWED_ORIGINS : true,
    methods: ['GET', 'POST'],
    credentials: true,
  },
  pingTimeout: 30000,
  pingInterval: 15000,
  connectTimeout: 45000,
  transports: ['websocket', 'polling'],
});

// Auth middleware runs on every connection
io.use(socketAuthMiddleware);

// Auto-create guest session for legacy Unity clients
io.use(async (socket, next) => {
  if ((socket as any).isLegacyClient && !(socket as any).authUser) {
    try {
      const legacyToken = (socket as any).legacyToken ?? '';
      const displayName = legacyToken.startsWith('guest_')
        ? legacyToken.replace('guest_', 'Guest_')
        : undefined;
      const { user, tokens } = await AuthService.createGuest(displayName);
      (socket as any).authUser = { userId: user.id, username: user.username, isGuest: true };
      logger.debug(`Auto-created guest session for legacy client: ${user.username}`);
    } catch (err) {
      logger.warn('Failed to auto-create guest session, proceeding as anonymous');
    }
  }
  next();
});

io.on('connection', (socket) => {
  logger.info(`Socket connected: ${socket.id} (${(socket as any).authUser?.username ?? 'anonymous'})`);
  registerGameHandlers(io, socket);
});

// ── Timer tick for timed-mode games ─────────────────────────────────────────
// Runs every 500ms. Checks all active timed games for timeouts.

const TIMER_INTERVAL_MS = 500;
setInterval(() => {
  // Timer tick implementation would iterate active rooms here
  // Left as extension point — full implementation uses room manager
}, TIMER_INTERVAL_MS);

// ── Database connection + server start ───────────────────────────────────────

async function start(): Promise<void> {
  try {
    await prisma.$connect();
    logger.info('Database connected');
  } catch (err) {
    logger.warn(`Database connection failed (will retry): ${err}`);
    // Non-fatal: server can still handle connections
  }

  httpServer.listen(env.PORT, () => {
    logger.info(`━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`);
    logger.info(`  3D Chess Server v2 running on port ${env.PORT}`);
    logger.info(`  Environment: ${env.NODE_ENV}`);
    logger.info(`  Health: http://localhost:${env.PORT}/health`);
    logger.info(`━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`);
  });
}

// ── Graceful shutdown ─────────────────────────────────────────────────────────

process.on('SIGTERM', async () => {
  logger.info('SIGTERM received — shutting down gracefully');
  httpServer.close(() => process.exit(0));
  await prisma.$disconnect();
});

process.on('SIGINT', async () => {
  logger.info('SIGINT received — shutting down');
  httpServer.close(() => process.exit(0));
  await prisma.$disconnect();
});

start().catch((err) => {
  logger.error('Failed to start server:', err);
  process.exit(1);
});
