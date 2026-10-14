import bcrypt from 'bcryptjs';
import jwt from 'jsonwebtoken';
import { v4 as uuidv4 } from 'uuid';
import { prisma } from '../db/client.js';
import { env } from '../utils/env.js';

// ─────────────────────────────────────────────────────────────────────────────
// Auth Service — JWT authentication with guest session support
// ─────────────────────────────────────────────────────────────────────────────

export interface TokenPayload {
  userId: string;
  username: string;
  isGuest: boolean;
}

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  expiresIn: number; // seconds
}

export interface AuthResult {
  user: { id: string; username: string; email: string | null; eloRating: number; isGuest: boolean };
  tokens: AuthTokens;
}

export class AuthService {
  // ── Registration ────────────────────────────────────────────────────────────

  static async register(
    username: string,
    email: string,
    password: string,
  ): Promise<AuthResult> {
    // Check uniqueness
    const existing = await prisma.user.findFirst({
      where: { OR: [{ email }, { username }] },
    });
    if (existing) {
      if (existing.username === username) throw new Error('Username already taken');
      if (existing.email === email) throw new Error('Email already registered');
    }

    const passwordHash = await bcrypt.hash(password, 12);
    const user = await prisma.user.create({
      data: { id: uuidv4(), username, email, passwordHash, isGuest: false },
    });

    const tokens = await AuthService.generateTokens(user.id, user.username, false);
    return {
      user: { id: user.id, username: user.username, email: user.email, eloRating: user.eloRating, isGuest: false },
      tokens,
    };
  }

  // ── Login ────────────────────────────────────────────────────────────────────

  static async login(emailOrUsername: string, password: string): Promise<AuthResult> {
    const user = await prisma.user.findFirst({
      where: {
        OR: [
          { email: emailOrUsername },
          { username: emailOrUsername },
        ],
        isGuest: false,
      },
    });

    if (!user || !user.passwordHash) {
      throw new Error('Invalid credentials');
    }

    const valid = await bcrypt.compare(password, user.passwordHash);
    if (!valid) throw new Error('Invalid credentials');

    await prisma.user.update({
      where: { id: user.id },
      data: { lastSeenAt: new Date() },
    });

    const tokens = await AuthService.generateTokens(user.id, user.username, false);
    return {
      user: { id: user.id, username: user.username, email: user.email, eloRating: user.eloRating, isGuest: false },
      tokens,
    };
  }

  // ── Guest session ────────────────────────────────────────────────────────────
  // Generates a temporary guest account — no password required.
  // Unity clients that don't log in receive a guest token automatically.

  static async createGuest(displayName?: string): Promise<AuthResult> {
    const username = displayName ?? `Guest_${Math.random().toString(36).slice(2, 8).toUpperCase()}`;
    const user = await prisma.user.create({
      data: { id: uuidv4(), username, isGuest: true },
    });

    const tokens = await AuthService.generateTokens(user.id, user.username, true);
    return {
      user: { id: user.id, username: user.username, email: null, eloRating: user.eloRating, isGuest: true },
      tokens,
    };
  }

  // ── Token refresh ─────────────────────────────────────────────────────────────

  static async refresh(refreshToken: string): Promise<AuthTokens> {
    let payload: TokenPayload;
    try {
      payload = jwt.verify(refreshToken, env.JWT_REFRESH_SECRET) as TokenPayload;
    } catch {
      throw new Error('Invalid or expired refresh token');
    }

    const stored = await prisma.refreshToken.findUnique({ where: { token: refreshToken } });
    if (!stored || stored.revokedAt || stored.expiresAt < new Date()) {
      throw new Error('Refresh token revoked or expired');
    }

    // Rotate: revoke old, issue new
    await prisma.refreshToken.update({
      where: { id: stored.id },
      data: { revokedAt: new Date() },
    });

    return AuthService.generateTokens(payload.userId, payload.username, payload.isGuest);
  }

  // ── Logout ───────────────────────────────────────────────────────────────────

  static async logout(refreshToken: string): Promise<void> {
    await prisma.refreshToken.updateMany({
      where: { token: refreshToken },
      data: { revokedAt: new Date() },
    });
  }

  // ── Token verification (used by Socket.IO middleware) ─────────────────────────

  static verifyAccessToken(token: string): TokenPayload {
    return jwt.verify(token, env.JWT_SECRET) as TokenPayload;
  }

  // ── Private ──────────────────────────────────────────────────────────────────

  private static async generateTokens(
    userId: string,
    username: string,
    isGuest: boolean,
  ): Promise<AuthTokens> {
    const payload: TokenPayload = { userId, username, isGuest };

    const accessToken = jwt.sign(payload, env.JWT_SECRET, {
      expiresIn: env.JWT_EXPIRES_IN,
    });

    const refreshTokenStr = uuidv4();
    const expiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000); // 30 days

    await prisma.refreshToken.create({
      data: { id: uuidv4(), token: refreshTokenStr, userId, expiresAt },
    });

    // Decode to get actual expiry
    const decoded = jwt.decode(accessToken) as { exp: number };
    const expiresIn = decoded.exp - Math.floor(Date.now() / 1000);

    return { accessToken, refreshToken: refreshTokenStr, expiresIn };
  }
}
