import { GameEngine } from '../game/GameEngine.js';
import type { GameConfig, PlayerColor, PlayerState } from '../types/game.js';
import { PLAYER_COLORS_BY_COUNT } from '../types/game.js';
import { logger } from '../utils/logger.js';
import { env } from '../utils/env.js';

// ─────────────────────────────────────────────────────────────────────────────
// RoomManager — in-memory store of all active game rooms
// One GameEngine per room; rooms are GC'd when empty.
// ─────────────────────────────────────────────────────────────────────────────

export interface Room {
  engine: GameEngine;
  roomCode: string;
  hostUserId: string;
  isPublic: boolean;
  socketIdToColor: Map<string, PlayerColor>; // socketId → playerColor
  colorToSocketId: Map<PlayerColor, string>;  // reverse
  disconnectTimers: Map<PlayerColor, NodeJS.Timeout>;
  createdAt: number;
}

export class RoomManager {
  private rooms = new Map<string, Room>();

  // ── Room lifecycle ───────────────────────────────────────────────────────────

  createRoom(
    hostUserId: string,
    hostUsername: string,
    hostSocketId: string,
    config: Partial<GameConfig>,
    isPublic = true,
  ): Room {
    const roomCode = this.generateCode();
    const engine = new GameEngine(roomCode, config);
    const colors = PLAYER_COLORS_BY_COUNT[config.playerCount ?? 2];
    const hostColor: PlayerColor = colors[0]; // host always gets White

    engine.addPlayer(hostUserId, hostUsername, hostColor, 'human');

    const room: Room = {
      engine,
      roomCode,
      hostUserId,
      isPublic,
      socketIdToColor: new Map([[hostSocketId, hostColor]]),
      colorToSocketId: new Map([[hostColor, hostSocketId]]),
      disconnectTimers: new Map(),
      createdAt: Date.now(),
    };
    this.rooms.set(roomCode, room);
    logger.info(`Room created: ${roomCode} by ${hostUsername} (${hostColor})`);
    return room;
  }

  joinRoom(
    roomCode: string,
    userId: string,
    username: string,
    socketId: string,
  ): { room: Room; assignedColor: PlayerColor } | { error: string } {
    const room = this.rooms.get(roomCode.toUpperCase());
    if (!room) return { error: 'Room not found' };

    const state = room.engine.getState();
    if (state.phase !== 'waiting' && state.phase !== 'placement') {
      // Allow reconnection if player was already in the room
      const existingColor = this.findColorForUser(room, userId);
      if (existingColor) {
        return this.reconnectPlayer(room, userId, username, socketId, existingColor);
      }
      return { error: 'Game already in progress' };
    }

    const usedColors = state.players.map(p => p.color);
    const allColors = PLAYER_COLORS_BY_COUNT[state.config.playerCount];
    const nextColor = allColors.find(c => !usedColors.includes(c));
    if (!nextColor) return { error: 'Room is full' };

    const added = room.engine.addPlayer(userId, username, nextColor, 'human');
    if (!added) return { error: 'Could not add player' };

    room.socketIdToColor.set(socketId, nextColor);
    room.colorToSocketId.set(nextColor, socketId);
    logger.info(`Player ${username} joined room ${roomCode} as ${nextColor}`);
    return { room, assignedColor: nextColor };
  }

  leaveRoom(roomCode: string, socketId: string, graceful = false): void {
    const room = this.rooms.get(roomCode);
    if (!room) return;
    const color = room.socketIdToColor.get(socketId);
    if (!color) return;

    room.engine.setPlayerConnected(color, false);

    if (graceful) {
      // Initiate grace period
      const timer = setTimeout(() => {
        this.removePlayerFromRoom(room, color, socketId);
        if (this.isRoomEmpty(room)) this.deleteRoom(roomCode);
      }, env.DISCONNECT_GRACE_PERIOD_MS);
      room.disconnectTimers.set(color, timer);
      logger.info(`Player ${color} disconnected from ${roomCode} — grace period started`);
    } else {
      this.removePlayerFromRoom(room, color, socketId);
      if (this.isRoomEmpty(room)) this.deleteRoom(roomCode);
    }
  }

  private reconnectPlayer(
    room: Room,
    userId: string,
    username: string,
    socketId: string,
    color: PlayerColor,
  ): { room: Room; assignedColor: PlayerColor } {
    // Cancel any pending disconnect timer
    const timer = room.disconnectTimers.get(color);
    if (timer) {
      clearTimeout(timer);
      room.disconnectTimers.delete(color);
    }

    // Update socket mappings
    const oldSocketId = room.colorToSocketId.get(color);
    if (oldSocketId) room.socketIdToColor.delete(oldSocketId);
    room.socketIdToColor.set(socketId, color);
    room.colorToSocketId.set(color, socketId);
    room.engine.setPlayerConnected(color, true);
    logger.info(`Player ${username} reconnected to ${room.roomCode} as ${color}`);
    return { room, assignedColor: color };
  }

  private removePlayerFromRoom(room: Room, color: PlayerColor, socketId: string): void {
    room.socketIdToColor.delete(socketId);
    room.colorToSocketId.delete(color);
  }

  private isRoomEmpty(room: Room): boolean {
    return room.socketIdToColor.size === 0;
  }

  private deleteRoom(roomCode: string): void {
    this.rooms.delete(roomCode);
    logger.info(`Room ${roomCode} deleted (empty)`);
  }

  // ── Lookups ──────────────────────────────────────────────────────────────────

  getRoom(roomCode: string): Room | undefined {
    return this.rooms.get(roomCode.toUpperCase());
  }

  getRoomBySocketId(socketId: string): Room | undefined {
    for (const room of this.rooms.values()) {
      if (room.socketIdToColor.has(socketId)) return room;
    }
    return undefined;
  }

  getColorForSocket(room: Room, socketId: string): PlayerColor | undefined {
    return room.socketIdToColor.get(socketId);
  }

  findColorForUser(room: Room, userId: string): PlayerColor | undefined {
    const state = room.engine.getState();
    return state.players.find(p => p.userId === userId)?.color;
  }

  getPublicRooms(): { roomCode: string; playerCount: number; maxPlayers: number; boardSize: string; phase: string }[] {
    const result = [];
    for (const room of this.rooms.values()) {
      if (!room.isPublic) continue;
      const state = room.engine.getState();
      if (state.phase === 'gameover') continue;
      result.push({
        roomCode: room.roomCode,
        playerCount: state.players.length,
        maxPlayers: state.config.playerCount,
        boardSize: state.config.boardSize,
        phase: state.phase,
      });
    }
    return result;
  }

  // ── Code generation ──────────────────────────────────────────────────────────

  private generateCode(): string {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    let code: string;
    do {
      code = Array.from({ length: 6 }, () =>
        chars[Math.floor(Math.random() * chars.length)]
      ).join('');
    } while (this.rooms.has(code));
    return code;
  }
}

// Singleton exported for use across the server
export const roomManager = new RoomManager();
