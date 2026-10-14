import type { Request, Response, NextFunction } from 'express';
import type { Socket } from 'socket.io';
import { AuthService } from './auth.service.js';

// ─── REST middleware ──────────────────────────────────────────────────────────

export function requireAuth(req: Request, res: Response, next: NextFunction): void {
  const header = req.headers.authorization;
  if (!header?.startsWith('Bearer ')) {
    res.status(401).json({ error: 'Missing authorization header' });
    return;
  }
  try {
    const payload = AuthService.verifyAccessToken(header.slice(7));
    (req as any).user = payload;
    next();
  } catch {
    res.status(401).json({ error: 'Invalid or expired token' });
  }
}

export function optionalAuth(req: Request, res: Response, next: NextFunction): void {
  const header = req.headers.authorization;
  if (header?.startsWith('Bearer ')) {
    try {
      (req as any).user = AuthService.verifyAccessToken(header.slice(7));
    } catch {
      // Ignore — user is unauthenticated
    }
  }
  next();
}

// ─── Socket.IO middleware ────────────────────────────────────────────────────
// Called on every new socket connection. Extracts JWT from handshake.
// Supports:
//   - Bearer token in auth.token
//   - Legacy Unity tokens (backward-compatible: auto-create guest)

export function socketAuthMiddleware(
  socket: Socket,
  next: (err?: Error) => void,
): void {
  const token =
    (socket.handshake.auth as any)?.token ??
    socket.handshake.query?.token as string | undefined;

  // Legacy Unity clients that pass "UNITY" or "WEB_CLIENT" etc.
  // Auto-upgrade to guest sessions — handled in connection handler
  if (!token || isLegacyToken(token)) {
    (socket as any).isLegacyClient = true;
    (socket as any).legacyToken = token;
    return next();
  }

  try {
    const payload = AuthService.verifyAccessToken(token);
    (socket as any).authUser = payload;
    next();
  } catch {
    // Expired/invalid — reject with specific error so client can refresh
    next(new Error('JWT_EXPIRED'));
  }
}

function isLegacyToken(token: string): boolean {
  const LEGACY = ['UNITY', 'Unity', 'WEB_CLIENT', 'MOBILE_CLIENT', 'DASHBOARD', 'TEST'];
  return LEGACY.includes(token) || token.startsWith('guest_');
}
