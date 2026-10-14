import type { Server, Socket } from 'socket.io';
import { AuthService } from '../auth/auth.service.js';
import { roomManager } from '../rooms/RoomManager.js';
import { EVENTS } from '../types/network.js';
import type { GameConfig, PlayerColor, PieceType, BoardPosition } from '../types/game.js';
import { logger } from '../utils/logger.js';

// ─────────────────────────────────────────────────────────────────────────────
// gameHandler — registers all Socket.IO event handlers for a socket connection
// ─────────────────────────────────────────────────────────────────────────────

export function registerGameHandlers(io: Server, socket: Socket): void {
  const authUser: { userId: string; username: string; isGuest: boolean } | undefined =
    (socket as any).authUser;

  // ── Connection acknowledgement ────────────────────────────────────────────────

  socket.emit(EVENTS.CONNECTED, {
    userId: authUser?.userId ?? socket.id,
    username: authUser?.username ?? 'Guest',
    socketId: socket.id,
    serverVersion: '2.0.0',
  });

  // ── Room: Create ─────────────────────────────────────────────────────────────

  socket.on(EVENTS.ROOM_CREATE, async (data: { config?: Partial<GameConfig>; playerName?: string }, cb?: Function) => {
    try {
      const userId = authUser?.userId ?? socket.id;
      const username = data.playerName ?? authUser?.username ?? 'Guest';
      const room = roomManager.createRoom(userId, username, socket.id, data.config ?? {});
      const state = room.engine.getState();

      socket.join(room.roomCode);
      const response = {
        success: true,
        roomCode: room.roomCode,
        assignedColor: 'White' as PlayerColor,
        gameState: state,
        isHost: true,
      };
      cb?.(response);
      socket.emit(EVENTS.ROOM_JOINED, response);
    } catch (err: any) {
      cb?.({ success: false, error: err.message });
    }
  });

  // ── Room: Join ───────────────────────────────────────────────────────────────

  socket.on(EVENTS.ROOM_JOIN, async (data: { roomCode: string; playerName?: string }, cb?: Function) => {
    try {
      const userId = authUser?.userId ?? socket.id;
      const username = data.playerName ?? authUser?.username ?? 'Guest';
      const result = roomManager.joinRoom(data.roomCode, userId, username, socket.id);

      if ('error' in result) {
        cb?.({ success: false, error: result.error });
        return;
      }

      const { room, assignedColor } = result;
      socket.join(room.roomCode);

      const state = room.engine.getState();
      const response = {
        success: true,
        roomCode: room.roomCode,
        assignedColor,
        gameState: state,
        isHost: false,
      };
      cb?.(response);
      socket.emit(EVENTS.ROOM_JOINED, response);

      // Notify others in the room
      socket.to(room.roomCode).emit(EVENTS.ROOM_PLAYER_JOINED, {
        userId,
        username,
        color: assignedColor,
        isAI: false,
      });
    } catch (err: any) {
      cb?.({ success: false, error: err.message });
    }
  });

  // ── Room: Leave ──────────────────────────────────────────────────────────────

  socket.on(EVENTS.ROOM_LEAVE, (data: { roomCode?: string }, cb?: Function) => {
    const room = data.roomCode
      ? roomManager.getRoom(data.roomCode)
      : roomManager.getRoomBySocketId(socket.id);

    if (!room) { cb?.({ success: false, error: 'Not in a room' }); return; }

    const color = roomManager.getColorForSocket(room, socket.id);
    if (color) {
      io.to(room.roomCode).emit(EVENTS.ROOM_PLAYER_LEFT, {
        userId: authUser?.userId ?? socket.id,
        color,
        reason: 'leave',
      });
    }
    roomManager.leaveRoom(room.roomCode, socket.id, false);
    socket.leave(room.roomCode);
    cb?.({ success: true });
  });

  // ── Room: Fill with AI ───────────────────────────────────────────────────────

  socket.on(EVENTS.ROOM_FILL_AI, (data: { roomCode: string }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }

    const state = room.engine.getState();
    const usedColors = state.players.map(p => p.color);
    const { PLAYER_COLORS_BY_COUNT } = require('../types/game.js');
    const allColors = PLAYER_COLORS_BY_COUNT[state.config.playerCount];
    const openColors = allColors.filter((c: PlayerColor) => !usedColors.includes(c));

    for (const color of openColors) {
      room.engine.addPlayer(`ai_${color}`, `AI_${color}`, color, 'ai', state.config.aiDifficulty);
    }
    const newState = room.engine.getState();
    io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, { gameState: newState, reason: 'ai_fill' });
    cb?.({ success: true, addedColors: openColors });
  });

  // ── Game: Place piece ─────────────────────────────────────────────────────────

  socket.on(EVENTS.GAME_PLACE_PIECE, (data: { roomCode: string; pieceType: PieceType; position: BoardPosition }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }

    const color = roomManager.getColorForSocket(room, socket.id);
    if (!color) { cb?.({ success: false, error: 'You are not in this room' }); return; }

    const result = room.engine.placePiece(color, data.pieceType, data.position);
    if (!result.success) { cb?.({ success: false, error: result.error }); return; }

    cb?.({ success: true });
    io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, {
      gameState: room.engine.getState(),
      reason: 'piece_placed',
    });
  });

  // ── Game: Ready ────────────────────────────────────────────────────────────────

  socket.on(EVENTS.GAME_READY, (data: { roomCode: string }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }

    const color = roomManager.getColorForSocket(room, socket.id);
    if (!color) { cb?.({ success: false, error: 'Not in room' }); return; }

    room.engine.setPlayerReady(color, true);
    room.engine.completePlacement(color);

    // Start placement phase if not started
    const state = room.engine.getState();
    if (state.phase === 'waiting' && room.engine.allPlayersReady()) {
      room.engine.startPlacement();
    }

    // Transition to playing when all placements complete
    if (state.phase === 'placement' && room.engine.allPlacementsComplete()) {
      const started = room.engine.startGame();
      if (started) {
        const newState = room.engine.getState();
        io.to(room.roomCode).emit(EVENTS.GAME_PHASE_CHANGED, {
          phase: 'playing',
          gameState: newState,
        });
        io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, { gameState: newState, reason: 'game_started' });
        cb?.({ success: true, phase: 'playing' });
        return;
      }
    }

    io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, {
      gameState: room.engine.getState(),
      reason: 'player_ready',
    });
    cb?.({ success: true });
  });

  // ── Game: Move ────────────────────────────────────────────────────────────────

  socket.on(EVENTS.GAME_MOVE, (data: { roomCode: string; from: BoardPosition; to: BoardPosition }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }

    const color = roomManager.getColorForSocket(room, socket.id);
    if (!color) { cb?.({ success: false, error: 'Not in room' }); return; }

    const result = room.engine.executeMove(color, data.from, data.to);

    if (!result.success) {
      socket.emit(EVENTS.GAME_MOVE_REJECTED, {
        reason: result.error,
        attemptedMove: { from: data.from, to: data.to },
      });
      cb?.({ success: false, error: result.error });
      return;
    }

    const newState = result.newState!;

    // Broadcast validated move + new state to all players in room
    io.to(room.roomCode).emit(EVENTS.GAME_MOVE_VALIDATED, {
      move: newState.moveHistory[newState.moveHistory.length - 1],
      newBoardState: newState.board,
      nextTurn: newState.currentTurn,
      turnNumber: newState.turnNumber,
    });

    // Broadcast check status
    const checksInProgress = newState.checkStatus.filter(c => c.isInCheck);
    if (checksInProgress.length > 0) {
      io.to(room.roomCode).emit(EVENTS.GAME_CHECK_UPDATE, { checkStatuses: newState.checkStatus });
    }

    // Pawn promotion required
    if (result.requiresPromotion) {
      io.to(room.roomCode).emit(EVENTS.GAME_PAWN_PROMOTION, {
        position: result.requiresPromotion.position,
        playerColor: result.requiresPromotion.color,
        validPromotions: ['Queen', 'Rook', 'Bishop', 'Knight'],
      });
      cb?.({ success: true, requiresPromotion: true });
      return;
    }

    // Player eliminated (multi-player)
    if (result.eliminatedPlayer) {
      io.to(room.roomCode).emit(EVENTS.GAME_PLAYER_ELIMINATED, {
        eliminatedColor: result.eliminatedPlayer,
        conqueredBy: result.conqueredBy,
      });
    }

    // Game over
    if (result.gameOver) {
      io.to(room.roomCode).emit(EVENTS.GAME_OVER, {
        winner: result.gameOver.winner,
        endReason: result.gameOver.reason,
        finalState: newState,
      });
    }

    // Full state update for all clients
    io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, { gameState: newState, reason: 'move' });
    cb?.({ success: true });
  });

  // ── Game: Promote pawn ────────────────────────────────────────────────────────

  socket.on(EVENTS.GAME_PROMOTE_PAWN, (data: { roomCode: string; position: BoardPosition; pieceType: PieceType }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }

    const color = roomManager.getColorForSocket(room, socket.id);
    if (!color) { cb?.({ success: false, error: 'Not in room' }); return; }

    const result = room.engine.promotePawn(color, data.position, data.pieceType);
    if (!result.success) { cb?.({ success: false, error: result.error }); return; }

    const newState = result.newState!;
    io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, { gameState: newState, reason: 'promotion' });

    if (result.gameOver) {
      io.to(room.roomCode).emit(EVENTS.GAME_OVER, {
        winner: result.gameOver.winner,
        endReason: result.gameOver.reason,
        finalState: newState,
      });
    }
    cb?.({ success: true });
  });

  // ── Game: Resign ─────────────────────────────────────────────────────────────

  socket.on(EVENTS.GAME_RESIGN, (data: { roomCode: string }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }

    const color = roomManager.getColorForSocket(room, socket.id);
    if (!color) { cb?.({ success: false, error: 'Not in room' }); return; }

    const result = room.engine.resign(color);
    if (!result.success) { cb?.({ success: false, error: result.error }); return; }

    const newState = result.newState!;
    io.to(room.roomCode).emit(EVENTS.GAME_PLAYER_ELIMINATED, { eliminatedColor: color });
    if (result.gameOver) {
      io.to(room.roomCode).emit(EVENTS.GAME_OVER, {
        winner: result.gameOver.winner,
        endReason: result.gameOver.reason,
        finalState: newState,
      });
    }
    io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, { gameState: newState, reason: 'resignation' });
    cb?.({ success: true });
  });

  // ── Game: Draw offer ─────────────────────────────────────────────────────────

  socket.on(EVENTS.GAME_OFFER_DRAW, (data: { roomCode: string }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }
    const color = roomManager.getColorForSocket(room, socket.id);
    if (!color) { cb?.({ success: false, error: 'Not in room' }); return; }

    room.engine.offerDraw(color);
    socket.to(room.roomCode).emit(EVENTS.GAME_DRAW_OFFERED, { fromColor: color });
    cb?.({ success: true });
  });

  socket.on(EVENTS.GAME_RESPOND_DRAW, (data: { roomCode: string; accept: boolean }, cb?: Function) => {
    const room = roomManager.getRoom(data.roomCode);
    if (!room) { cb?.({ success: false, error: 'Room not found' }); return; }
    const color = roomManager.getColorForSocket(room, socket.id);
    if (!color) { cb?.({ success: false, error: 'Not in room' }); return; }

    const result = room.engine.respondDraw(color, data.accept);
    if (!result) { cb?.({ success: false, error: 'No draw offer to respond to' }); return; }

    if (result.gameOver) {
      io.to(room.roomCode).emit(EVENTS.GAME_OVER, {
        winner: null,
        endReason: 'draw_agreement',
        finalState: result.newState,
      });
      io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, { gameState: result.newState, reason: 'draw' });
    } else {
      io.to(room.roomCode).emit(EVENTS.GAME_STATE_UPDATE, { gameState: result.newState!, reason: 'draw_declined' });
    }
    cb?.({ success: true });
  });

  // ── Heartbeat / ping ──────────────────────────────────────────────────────────

  socket.on(EVENTS.PING, () => {
    socket.emit(EVENTS.PONG, Date.now());
  });

  socket.on(EVENTS.HEARTBEAT, () => {
    socket.emit('heartbeat_response', { timestamp: Date.now() });
  });

  // ── Disconnect ────────────────────────────────────────────────────────────────

  socket.on(EVENTS.DISCONNECTED, (reason: string) => {
    const room = roomManager.getRoomBySocketId(socket.id);
    if (!room) return;

    const color = roomManager.getColorForSocket(room, socket.id);
    const isNetworkIssue = ['transport close', 'transport error', 'ping timeout'].includes(reason);

    if (color) {
      socket.to(room.roomCode).emit(EVENTS.ROOM_PLAYER_LEFT, {
        userId: authUser?.userId ?? socket.id,
        color,
        reason: 'disconnect',
        gracePeriodMs: isNetworkIssue ? 120000 : 0,
      });
    }

    roomManager.leaveRoom(room.roomCode, socket.id, isNetworkIssue);
    logger.info(`Socket ${socket.id} disconnected (${reason})`);
  });
}
