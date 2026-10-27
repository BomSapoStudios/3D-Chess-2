# 3D Chess

Chess on a cube for iPhone (web later), built in Unity. Modes: Quick Play against robots,
online games for 2, 4 or 6 players, Zombies! and Queen Blitz puzzles.

## Where things are

- `Assets/Scripts` — the game. The home screen is `UI/NeonMenu*.cs`; the board game starts in
  `GameManager.cs` and `PlacementManager.cs`; robots are in `AI/`; online play is in `Network/Online/`.
- `Assets/Scripts/Testing` — development-build demos used for screenshots and Simulator checks.
  They are compiled out of release builds.
- `Assets/Editor` — build menu (`Build > Build iOS`) and the store image renderer.
- `server-cf` — the live online server (Cloudflare Workers + Durable Objects). See its README.
- `server-v2` — the earlier server that `server-cf` was ported from, kept for reference.
- `Tools/QueenBlitz` — the Queen Blitz puzzle generator and pack builders (Python).
- `GAMEPLAY_MECHANICS.txt` — the full rules and features reference. Keep it in sync when gameplay changes.
- `CLAUDE.md` — build and store image steps.
