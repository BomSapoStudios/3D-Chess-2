// ─────────────────────────────────────────────────────────────────────────────
// Network Message Types — complete Socket.IO event protocol
// All clients (Unity, Web, iOS) use these exact shapes.
// ─────────────────────────────────────────────────────────────────────────────

import type {
  BoardPosition, PlayerColor, PieceType, GameConfig,
  GameState, ChaosRotation, CheckStatus, EndReason,
} from './game.js';

// ─── Client → Server payloads ────────────────────────────────────────────────

export interface C2S_RoomCreate {
  config: Partial<GameConfig>;
  playerName?: string;
}

export interface C2S_RoomJoin {
  roomCode: string;
  playerName?: string;
}

export interface C2S_PlacePiece {
  roomCode: string;
  pieceType: PieceType;
  position: BoardPosition;
}

export interface C2S_Ready {
  roomCode: string;
}

export interface C2S_Move {
  roomCode: string;
  from: BoardPosition;
  to: BoardPosition;
}

export interface C2S_PromotePawn {
  roomCode: string;
  position: BoardPosition;
  pieceType: PieceType; // Queen | Rook | Bishop | Knight
}

export interface C2S_Resign {
  roomCode: string;
}

export interface C2S_OfferDraw {
  roomCode: string;
}

export interface C2S_RespondDraw {
  roomCode: string;
  accept: boolean;
}

export interface C2S_MatchmakingJoin {
  preferences: Partial<GameConfig>;
}

export interface C2S_FriendRequest {
  targetUserId: string;
}

export interface C2S_FriendResponse {
  requestId: string;
  accept: boolean;
}

export interface C2S_FriendInvite {
  friendId: string;
  roomCode: string;
}

// ─── Server → Client payloads ────────────────────────────────────────────────

export interface S2C_Connected {
  userId: string;
  username: string;
  socketId: string;
  serverVersion: string;
}

export interface S2C_RoomJoined {
  roomCode: string;
  assignedColor: PlayerColor;
  gameState: GameState;
  isHost: boolean;
}

export interface S2C_PlayerJoined {
  userId: string;
  username: string;
  color: PlayerColor;
  isAI: boolean;
}

export interface S2C_PlayerLeft {
  userId: string;
  color: PlayerColor;
  reason: 'disconnect' | 'kick' | 'leave';
  gracePeriodMs?: number;
}

export interface S2C_PlayerReconnected {
  userId: string;
  color: PlayerColor;
}

export interface S2C_MoveValidated {
  move: {
    from: BoardPosition;
    to: BoardPosition;
    playerColor: PlayerColor;
    pieceType: PieceType;
    isCapture: boolean;
    capturedPieceType?: PieceType;
    notation: string;
  };
  newBoardState: GameState['board'];
  nextTurn: PlayerColor;
  turnNumber: number;
}

export interface S2C_MoveRejected {
  reason: string;
  attemptedMove: { from: BoardPosition; to: BoardPosition };
}

export interface S2C_CheckUpdate {
  checkStatuses: CheckStatus[];
}

export interface S2C_GameOver {
  winner: PlayerColor | null;
  endReason: EndReason;
  finalState: GameState;
  eloChanges?: { color: PlayerColor; userId: string; delta: number }[];
}

export interface S2C_PlayerEliminated {
  eliminatedColor: PlayerColor;
  conqueredBy?: PlayerColor; // present when Conquest System triggers
  conqueredPieces?: number;
}

export interface S2C_ChaosRotation {
  rotation: ChaosRotation;
  newBoardState: GameState['board'];
  nextChaosIn: number; // turns
}

export interface S2C_PawnPromotionRequired {
  position: BoardPosition;
  playerColor: PlayerColor;
  validPromotions: PieceType[];
}

export interface S2C_TimerUpdate {
  timers: { color: PlayerColor; remainingMs: number }[];
  currentTurn: PlayerColor;
}

export interface S2C_DrawOffered {
  fromColor: PlayerColor;
}

export interface S2C_MatchFound {
  roomCode: string;
  assignedColor: PlayerColor;
}

export interface S2C_FriendStatusChanged {
  userId: string;
  username: string;
  isOnline: boolean;
}

export interface S2C_FriendRequestReceived {
  requestId: string;
  fromUserId: string;
  fromUsername: string;
}

export interface S2C_FriendInvite {
  fromUserId: string;
  fromUsername: string;
  roomCode: string;
}

export interface S2C_GameStateUpdate {
  gameState: GameState;
  reason: string;
}

// ─── Socket.IO event name constants ──────────────────────────────────────────
// Use these instead of raw strings to catch typos at compile time.

export const EVENTS = {
  // Connection
  CONNECTED: 'connected',
  DISCONNECTED: 'disconnect',

  // Rooms
  ROOM_CREATE: 'room:create',
  ROOM_JOIN: 'room:join',
  ROOM_LEAVE: 'room:leave',
  ROOM_JOINED: 'room:joined',
  ROOM_PLAYER_JOINED: 'room:playerJoined',
  ROOM_PLAYER_LEFT: 'room:playerLeft',
  ROOM_PLAYER_RECONNECTED: 'room:playerReconnected',
  ROOM_FILL_AI: 'room:fillAI',

  // Game lifecycle
  GAME_PLACE_PIECE: 'game:placePiece',
  GAME_READY: 'game:ready',
  GAME_PHASE_CHANGED: 'game:phaseChanged',
  GAME_STATE_UPDATE: 'game:stateUpdate',

  // Moves
  GAME_MOVE: 'game:move',
  GAME_MOVE_VALIDATED: 'game:moveValidated',
  GAME_MOVE_REJECTED: 'game:moveRejected',

  // Special move events
  GAME_CHECK_UPDATE: 'game:checkUpdate',
  GAME_PAWN_PROMOTION: 'game:pawnPromotion',
  GAME_PROMOTE_PAWN: 'game:promotePawn',
  GAME_CHAOS_ROTATION: 'game:chaosRotation',
  GAME_TIMER_UPDATE: 'game:timerUpdate',

  // End of game
  GAME_PLAYER_ELIMINATED: 'game:playerEliminated',
  GAME_OVER: 'game:over',
  GAME_RESIGN: 'game:resign',
  GAME_OFFER_DRAW: 'game:offerDraw',
  GAME_DRAW_OFFERED: 'game:drawOffered',
  GAME_RESPOND_DRAW: 'game:respondDraw',

  // Matchmaking
  MATCHMAKING_JOIN: 'matchmaking:join',
  MATCHMAKING_LEAVE: 'matchmaking:leave',
  MATCHMAKING_FOUND: 'matchmaking:found',
  MATCHMAKING_QUEUED: 'matchmaking:queued',

  // Friends
  FRIEND_REQUEST: 'friend:request',
  FRIEND_RESPOND: 'friend:respond',
  FRIEND_REMOVE: 'friend:remove',
  FRIEND_INVITE: 'friend:invite',
  FRIEND_REQUEST_RECEIVED: 'friend:requestReceived',
  FRIEND_STATUS_CHANGED: 'friend:statusChanged',
  FRIEND_INVITE_RECEIVED: 'friend:inviteReceived',

  // Utility
  PING: 'ping',
  PONG: 'pong',
  HEARTBEAT: 'heartbeat',
  ERROR: 'error',
} as const;

export type EventName = typeof EVENTS[keyof typeof EVENTS];
