import { Router } from 'express';
import { roomManager } from '../rooms/RoomManager.js';

const router = Router();

// GET /rooms — list public rooms
router.get('/', (req, res) => {
  res.json({ rooms: roomManager.getPublicRooms() });
});

// GET /rooms/:code — specific room info
router.get('/:code', (req, res) => {
  const room = roomManager.getRoom(req.params.code.toUpperCase());
  if (!room) {
    res.status(404).json({ error: 'Room not found' });
    return;
  }
  const state = room.engine.getState();
  res.json({
    roomCode: room.roomCode,
    phase: state.phase,
    config: state.config,
    players: state.players.map(p => ({
      username: p.username,
      color: p.color,
      isConnected: p.isConnected,
      isReady: p.isReady,
    })),
  });
});

export { router as roomsRouter };
