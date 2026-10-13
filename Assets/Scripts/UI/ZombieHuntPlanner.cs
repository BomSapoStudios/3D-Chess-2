using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zombie Survival's Turn Planner: for each zombie, the fastest way one of your pieces can
/// destroy it within a few turns, aiming at where the zombie will be rather than where it is.
///
/// Zombie paths are predicted by playing the horde's own rules forward on a board copy
/// (front-most first: bite a piece on a forward diagonal, else step forward, else wait),
/// with your other pieces standing still and no new zombies (those rise at random).
/// A hunter's route is a breadth-first search over its moves, a turn at a time, against
/// the zombies as they'll stand that turn. Routes that never stop where a zombie could
/// bite are preferred.
/// </summary>
public static class ZombieHuntPlanner
{
    public const int MaxTurns = 4;
    private const int MaxZombies = 16; // the ones nearest your side

    public class Hunt
    {
        public ChessPiece hunter;
        public ChessPiece zombie;
        public int turns;                         // your moves, counting the capture
        public List<BoardPosition> route;         // hunter's squares: start, stops…, the kill square
        public List<BoardPosition> zombiePath;    // where the zombie stands before each of your moves
        public bool safeRoute;                    // no stop on the way can be bitten
        public bool safeAfter;                    // nothing can take the hunter right after the kill
        public bool Reachable => route != null;
        public BoardPosition FirstStep => route[1];
    }

    /// <summary>One hunt per zombie (nearest your side first), fastest and safest first</summary>
    public static List<Hunt> Plan()
    {
        var hunts = new List<Hunt>();
        if (ChessBoard.Instance == null || BoardDimensionsManager.Instance == null) return hunts;
        int n = BoardDimensionsManager.Instance.GetDimensions().x;
        LiteBoard now = RobotBrain.FromGame(n);
        var game = ChessBoard.Instance;

        var zombies = new List<ChessPiece>();
        var hunters = new List<ChessPiece>();
        for (int sq = 0; sq < now.Cells; sq++)
        {
            if (now.cell[sq] == 0) continue;
            now.Coords(sq, out int x, out int y, out int z);
            ChessPiece p = game.GetPieceAt(new BoardPosition(x, y, z));
            if (p == null) continue;
            if (p.pieceColor == ZombieMode.ZombieColor) zombies.Add(p);
            else if (p.pieceColor == ZombieMode.PlayerColor && p.GetValidMoves().Count > 0) hunters.Add(p);
        }
        zombies.Sort((a, b) => a.CurrentPosition.x.CompareTo(b.CurrentPosition.x));
        if (zombies.Count > MaxZombies) zombies.RemoveRange(MaxZombies, zombies.Count - MaxZombies);

        foreach (ChessPiece zombie in zombies)
        {
            Hunt best = null;
            foreach (ChessPiece hunter in hunters)
            {
                Hunt h = Search(now, hunter, zombie);
                if (h == null) continue;
                if (best == null || Better(h, best)) best = h;
            }
            hunts.Add(best ?? new Hunt { zombie = zombie, zombiePath = Predict(now, null, Index(now, zombie.CurrentPosition)).path });
        }

        hunts.Sort((a, b) =>
        {
            if (a.Reachable != b.Reachable) return a.Reachable ? -1 : 1;
            if (a.Reachable && a.turns != b.turns) return a.turns.CompareTo(b.turns);
            if (a.Reachable && a.safeRoute != b.safeRoute) return a.safeRoute ? -1 : 1;
            return a.zombie.CurrentPosition.x.CompareTo(b.zombie.CurrentPosition.x); // closest to your side first
        });
        return hunts;
    }

    private static bool Better(Hunt a, Hunt b)
    {
        if (a.turns != b.turns) return a.turns < b.turns;
        int sa = (a.safeRoute ? 2 : 0) + (a.safeAfter ? 1 : 0), sb = (b.safeRoute ? 2 : 0) + (b.safeAfter ? 1 : 0);
        if (sa != sb) return sa > sb;
        return RobotSearch.Value(a.hunter.pieceType) < RobotSearch.Value(b.hunter.pieceType); // send the cheaper piece
    }

    private static int Index(LiteBoard b, BoardPosition p) => b.Index(p.x, p.y, p.z);

    private static BoardPosition Pos(LiteBoard b, int sq)
    {
        b.Coords(sq, out int x, out int y, out int z);
        return new BoardPosition(x, y, z);
    }

    // ───────────────────────── Zombie prediction ─────────────────────────

    /// <summary>
    /// The board before each of your next MaxTurns moves (with the hunter lifted off, so the
    /// horde doesn't chase it), and the target zombie's square on each.
    /// </summary>
    private static (List<LiteBoard> boards, List<BoardPosition> path, List<int> squares) Predict(LiteBoard now, ChessPiece hunter, int target)
    {
        LiteBoard board = Copy(now);
        if (hunter != null) board.Put(Index(board, hunter.CurrentPosition), 0);
        var boards = new List<LiteBoard> { Copy(board) };
        var squares = new List<int> { target };
        var path = new List<BoardPosition> { Pos(board, target) };
        var moves = new List<int>();
        int black = (int)ZombieMode.ZombieColor, white = (int)ZombieMode.PlayerColor;

        for (int turn = 1; turn < MaxTurns; turn++)
        {
            // Zombie pawns, front-most (lowest x) first
            var pawns = new List<int>();
            for (int sq = 0; sq < board.Cells; sq++)
                if (board.cell[sq] != 0 && LiteBoard.ColorOf(board.cell[sq]) == black && LiteBoard.TypeOf(board.cell[sq]) == LiteBoard.Pawn) pawns.Add(sq);
            pawns.Sort((a, b) => (a / (board.N * board.N)).CompareTo(b / (board.N * board.N)));
            foreach (int from in pawns)
            {
                moves.Clear();
                board.PieceMoves(from, moves, false);
                int pick = -1;
                float bestValue = -1f;
                foreach (int m in moves)
                {
                    int to = LiteBoard.To(m);
                    byte there = board.cell[to];
                    float value = there == 0 ? 0f : LiteBoard.ColorOf(there) == white ? ValueOf(LiteBoard.TypeOf(there)) : -1f;
                    if (value > bestValue) { bestValue = value; pick = to; }
                }
                if (pick < 0) continue; // blocked: waits
                byte piece = board.cell[from];
                board.Put(from, 0);
                board.Put(pick, piece);
                if (from == target) target = pick;
            }
            boards.Add(Copy(board));
            squares.Add(target);
            path.Add(Pos(board, target));
        }
        return (boards, path, squares);
    }

    private static float ValueOf(int liteType)
    {
        switch (liteType)
        {
            case LiteBoard.Queen: return 9f;
            case LiteBoard.Rook: return 5f;
            case LiteBoard.Bishop: return 3.2f;
            case LiteBoard.Knight: return 3f;
            case LiteBoard.King: return 100f;
            default: return 1f;
        }
    }

    private static LiteBoard Copy(LiteBoard b)
    {
        var c = new LiteBoard(b.N);
        for (int i = 0; i < b.Cells; i++) if (b.cell[i] != 0) c.Put(i, b.cell[i]);
        return c;
    }

    // ───────────────────────── Hunter route ─────────────────────────

    /// <summary>The fewest moves for this piece to take this zombie, preferring routes zombies can't bite</summary>
    private static Hunt Search(LiteBoard now, ChessPiece hunter, ChessPiece zombie)
    {
        int start = Index(now, hunter.CurrentPosition);
        byte piece = now.cell[start];
        var (boards, path, squares) = Predict(now, hunter, Index(now, zombie.CurrentPosition));

        // layer: square → (parent square, safe so far)
        var layer = new Dictionary<int, (int parent, bool safe)> { [start] = (-1, true) };
        var history = new List<Dictionary<int, (int parent, bool safe)>> { layer };
        var visited = new HashSet<int> { start };
        var moves = new List<int>();

        for (int turn = 1; turn <= MaxTurns && turn <= boards.Count; turn++)
        {
            LiteBoard board = boards[turn - 1];
            int target = squares[turn - 1];
            var next = new Dictionary<int, (int parent, bool safe)>();
            int killFrom = -1;
            bool killSafe = false;

            foreach (var kv in layer)
            {
                int at = kv.Key;
                board.Put(at, piece);
                moves.Clear();
                board.PieceMoves(at, moves, false);
                foreach (int m in moves)
                {
                    int to = LiteBoard.To(m);
                    if (to == target)
                    {
                        if (killFrom < 0 || (kv.Value.safe && !killSafe)) { killFrom = at; killSafe = kv.Value.safe; }
                        continue;
                    }
                    if (board.cell[to] != 0 || visited.Contains(to) && !next.ContainsKey(to)) continue;
                    // Would a zombie bite it there before your next move?
                    board.Put(to, piece);
                    bool safe = kv.Value.safe && !board.Attacked(to);
                    board.Put(to, 0);
                    if (!next.TryGetValue(to, out var existing) || (safe && !existing.safe)) next[to] = (at, safe);
                }
                board.Put(at, 0);
            }

            if (killFrom >= 0)
            {
                // Walk the route back
                var route = new List<BoardPosition> { Pos(board, target) };
                int step = killFrom;
                for (int t = turn - 1; t >= 0; t--)
                {
                    route.Insert(0, Pos(board, step));
                    step = history[t][step].parent;
                }
                // After the kill: could anything take the hunter on the zombie's square?
                byte victim = board.cell[target];
                board.Put(target, piece);
                bool safeAfter = !board.Attacked(target);
                board.Put(target, victim);
                return new Hunt
                {
                    hunter = hunter, zombie = zombie, turns = turn, route = route,
                    zombiePath = path.GetRange(0, turn), safeRoute = killSafe, safeAfter = safeAfter,
                };
            }

            foreach (int sq in next.Keys) visited.Add(sq);
            layer = next;
            history.Add(next);
            if (layer.Count == 0) break;
        }
        return null;
    }
}
