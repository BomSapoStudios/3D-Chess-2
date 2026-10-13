using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

/// <summary>
/// How a robot picks its move, thinking on a LiteBoard copy in the background so the
/// screen never waits on it. Same ideas as RobotSearch (which it falls back to if anything
/// goes wrong), with more time and a few standard speed-ups:
///
///  - The robot's own moves are the game's real legal moves (taken from the pieces before the
///    search starts). Deeper in, a move that leaves a king open simply loses it on the reply.
///  - Turn order follows the real game; with 3+ players every opponent is assumed to play
///    against the robot ("paranoid" search) and scores are always the robot's.
///  - Iterative deepening with a time budget, alpha-beta, a transposition table (positions
///    reached by different move orders are only searched once), killer moves, and captures
///    first (most valuable victim, cheapest attacker).
///  - Captures are followed past the depth limit until things go quiet.
///  - Pawns that reach the far side become queens.
/// </summary>
public class RobotBrain
{
    public struct Settings
    {
        public int maxDepth;        // plies (one player's move each)
        public float seconds;       // thinking budget
        public bool quiescence;     // follow captures past the depth limit
        public float noise;         // random wobble added to each root move's score, in pawns
        public float blunderChance; // chance to pick any of the top few moves instead of the best

        public static Settings For(AIDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AIDifficulty.Easy:
                    return new Settings { maxDepth = 1, seconds = 0.3f, quiescence = false, noise = 1.2f, blunderChance = 0.25f };
                case AIDifficulty.Hard:
                    return new Settings { maxDepth = 8, seconds = 3.0f, quiescence = true, noise = 0f, blunderChance = 0f };
                default:
                    return new Settings { maxDepth = 4, seconds = 1.2f, quiescence = true, noise = 0.25f, blunderChance = 0f };
            }
        }
    }

    public int Nodes => nodes;
    public int DepthReached { get; private set; }
    public double Seconds { get; private set; }

    // ───────────────────────── Unity side (main thread) ─────────────────────────

    /// <summary>Think in the background; the result arrives through done (null if there's no legal move)</summary>
    public IEnumerator FindMoveRoutine(PieceColor player, Settings settings, System.Action<AIMove> done)
    {
        if (!Prepare(player, settings, out List<(ChessPiece piece, BoardPosition to)> legal))
        {
            done(legal.Count == 0 ? null : Fallback(player, settings));
            yield break;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        // No threads on the web: think right here (a short pause)
        Run();
#else
        var thread = new Thread(Run) { IsBackground = true, Name = "Robot brain" };
        thread.Start();
        while (thread.IsAlive) yield return null;
#endif
        done(Finish(player, settings, legal));
    }

    /// <summary>The same, all at once (tests)</summary>
    public AIMove FindMove(PieceColor player, Settings settings)
    {
        if (!Prepare(player, settings, out List<(ChessPiece piece, BoardPosition to)> legal))
            return legal.Count == 0 ? null : Fallback(player, settings);
        Run();
        return Finish(player, settings, legal);
    }

    /// <summary>Stop thinking early (the game ended or the scene is closing)</summary>
    public void Cancel() => cancelled = true;

    private LiteBoard board;
    private int rootColor;
    private int[] order;             // colors in turn order
    private List<int> rootMoves;
    private Settings settings;
    private List<(int move, float score)> result;
    private string failure;

    /// <summary>Copy the board and collect the robot's legal moves</summary>
    private bool Prepare(PieceColor player, Settings s, out List<(ChessPiece piece, BoardPosition to)> legal)
    {
        legal = new List<(ChessPiece, BoardPosition)>();
        settings = s;
        Vector3Int dims = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        var game = ChessBoard.Instance;
        if (game == null) return false;

        board = FromGame(dims.x);
        rootColor = (int)player;

        var colors = new List<int>();
        foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
        {
            bool active = PlayerManager.Instance != null
                ? PlayerManager.Instance.GetActivePlayers().Contains(c) && !PlayerManager.Instance.IsPlayerEliminated(c)
                : c == PieceColor.White || c == PieceColor.Black;
            if (active) colors.Add((int)c);
        }
        if (!colors.Contains(rootColor)) colors.Add(rootColor);
        order = colors.ToArray();

        rootMoves = new List<int>();
        for (int sq = 0; sq < board.Cells; sq++)
        {
            board.Coords(sq, out int x, out int y, out int z);
            ChessPiece piece = game.GetPieceAt(new BoardPosition(x, y, z));
            if (piece == null || piece.pieceColor != player) continue;
            foreach (BoardPosition to in piece.GetLegalMoves())
            {
                ChessPiece there = game.GetPieceAt(to);
                if (there != null && there.pieceColor == player) continue;
                legal.Add((piece, to));
                int target = board.Index(to.x, to.y, to.z);
                bool promotes = piece.pieceType == ChessPieceType.Pawn && piece is Pawn pawn && pawn.IsPromotionMove(to);
                rootMoves.Add(sq | target << 10 | (promotes ? 1 << 20 : 0));
            }
        }
        // Size must match what the pieces think (the board's own size can differ during a resize)
        return legal.Count > 0 && dims.x == dims.y && dims.y == dims.z;
    }

    private static LiteBoard FromGame(int n)
    {
        var lite = new LiteBoard(n);
        var game = ChessBoard.Instance;
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        for (int z = 0; z < n; z++)
        {
            ChessPiece p = game.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) lite.Put(lite.Index(x, y, z), LiteBoard.Make((int)p.pieceColor, TypeCode(p.pieceType)));
        }
        return lite;
    }

    public static int TypeCode(ChessPieceType t)
    {
        switch (t)
        {
            case ChessPieceType.Pawn: return LiteBoard.Pawn;
            case ChessPieceType.Knight: return LiteBoard.Knight;
            case ChessPieceType.Bishop: return LiteBoard.Bishop;
            case ChessPieceType.Rook: return LiteBoard.Rook;
            case ChessPieceType.Queen: return LiteBoard.Queen;
            default: return LiteBoard.King;
        }
    }

    private AIMove Finish(PieceColor player, Settings s, List<(ChessPiece piece, BoardPosition to)> legal)
    {
        if (failure != null || result == null || result.Count == 0)
        {
            if (failure != null) UnityEngine.Debug.LogError("RobotBrain: " + failure);
            return Fallback(player, s);
        }
        int pick = Choose(result, s);
        int move = result[pick].move;
        board.Coords(LiteBoard.From(move), out int fx, out int fy, out int fz);
        board.Coords(LiteBoard.To(move), out int tx, out int ty, out int tz);
        var from = new BoardPosition(fx, fy, fz);
        var to = new BoardPosition(tx, ty, tz);
        foreach (var (piece, target) in legal)
        {
            if (piece != null && piece.CurrentPosition == from && target == to)
                return new AIMove { piece = piece, fromPosition = from, toPosition = to, evaluationScore = result[pick].score };
        }
        return Fallback(player, s); // shouldn't happen: the board changed while thinking
    }

    private AIMove Fallback(PieceColor player, Settings s)
    {
        var old = new RobotSearch();
        return old.FindMove(player, new RobotSearch.Settings { maxDepth = Mathf.Min(s.maxDepth, 3), seconds = 0.6f, quiescence = s.quiescence, noise = s.noise, blunderChance = s.blunderChance });
    }

    /// <summary>The best move, give or take the difficulty's wobble and occasional slip</summary>
    private static int Choose(List<(int move, float score)> scored, Settings s)
    {
        bool decided = scored[0].score >= WIN - 100;
        int best = 0;
        if (!decided && s.noise > 0f)
        {
            float bestNoisy = float.NegativeInfinity;
            for (int i = 0; i < scored.Count; i++)
            {
                if (scored[i].score <= -WIN + 100) continue;
                float noisy = scored[i].score + Random.Range(-s.noise, s.noise);
                if (noisy > bestNoisy) { bestNoisy = noisy; best = i; }
            }
        }
        if (!decided && s.blunderChance > 0f && Random.value < s.blunderChance)
        {
            int safe = 0;
            while (safe < scored.Count && safe < 4 && scored[safe].score > -WIN + 100) safe++;
            if (safe > 0) best = Random.Range(0, safe);
        }
        return best;
    }

    // ───────────────────────── Search (background thread: no Unity calls below) ─────────────────────────

    private const float WIN = 100000f;
    private const int MAX_PLY = 64;
    private const int MAX_QUIESCENCE = 4;
    private const int MAX_QUIESCENCE_MOVES = 8;

    private Stopwatch clock;
    private volatile bool cancelled;
    private bool aborted;
    private int nodes;
    private List<int>[] moveLists;
    private readonly List<int> hits = new List<int>(32);
    private int[,] killers;

    // Transposition table: one slot per hash bucket
    private const int TT_BITS = 18;
    private ulong[] ttKey;
    private float[] ttScore;
    private sbyte[] ttDepth;
    private byte[] ttFlag;   // 1 exact, 2 lower bound, 3 upper bound
    private int[] ttMove;

    private void Run()
    {
        try
        {
            clock = Stopwatch.StartNew();
            moveLists = new List<int>[MAX_PLY + MAX_QUIESCENCE + 2];
            for (int i = 0; i < moveLists.Length; i++) moveLists[i] = new List<int>(128);
            killers = new int[MAX_PLY + 2, 2];
            int size = 1 << TT_BITS;
            ttKey = new ulong[size]; ttScore = new float[size]; ttDepth = new sbyte[size]; ttFlag = new byte[size]; ttMove = new int[size];

            List<int> moves = new List<int>(rootMoves);
            OrderMoves(moves, 0, 0);
            for (int depth = 1; depth <= settings.maxDepth; depth++)
            {
                var scored = SearchRoot(moves, depth, exact: settings.noise > 0f);
                if (aborted) break;
                result = scored;
                DepthReached = depth;
                // Best first next time
                moves.Clear();
                foreach (var s in scored) moves.Add(s.move);
                if (scored[0].score >= WIN - 100) break;                         // a forced win: no need to look further
                if (clock.Elapsed.TotalSeconds > settings.seconds * 0.45f) break; // the next pass would run out of time
            }
            Seconds = clock.Elapsed.TotalSeconds;
        }
        catch (System.Exception e)
        {
            failure = e.ToString();
        }
    }

    private int Next(int orderIndex) => (orderIndex + 1) % order.Length;

    private bool OutOfTime()
    {
        // The first pass always finishes, so there's always a scored move
        if ((nodes & 1023) == 0 && (cancelled || (DepthReached >= 1 && clock.Elapsed.TotalSeconds > settings.seconds))) aborted = true;
        return aborted;
    }

    private List<(int move, float score)> SearchRoot(List<int> moves, int depth, bool exact)
    {
        var scored = new List<(int, float)>(moves.Count);
        float alpha = -WIN * 2;
        int rootIndex = System.Array.IndexOf(order, rootColor);
        foreach (int m in moves)
        {
            byte victim = board.cell[LiteBoard.To(m)];
            float score;
            if (victim != 0 && LiteBoard.TypeOf(victim) == LiteBoard.King) score = WIN;
            else
            {
                byte mover = Make(m);
                score = Search(depth - 1, alpha, WIN * 2, Next(rootIndex), 1);
                Unmake(m, mover, victim);
            }
            if (aborted) break;
            scored.Add((m, score));
            if (score > alpha && !exact) alpha = score;
        }
        // Moves after the first got narrow windows; a score at the bound means "no better than"
        scored.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return scored;
    }

    /// <summary>Paranoid alpha-beta: the robot maximizes, every other player minimizes the robot's score</summary>
    private float Search(int depth, float alpha, float beta, int moverIndex, int ply)
    {
        nodes++;
        if (OutOfTime()) return 0;
        int mover = order[moverIndex];
        bool maximizing = mover == rootColor;
        if (depth <= 0 || ply >= MAX_PLY) return settings.quiescence ? Quiesce(alpha, beta, moverIndex, 0, ply) : Evaluate();

        // Seen this position before, at least this deep?
        ulong key = board.Hash ^ board.SideKey(moverIndex);
        int slot = (int)(key & ((1UL << TT_BITS) - 1));
        int ttBest = 0;
        if (ttKey[slot] == key)
        {
            ttBest = ttMove[slot];
            if (ttDepth[slot] >= depth)
            {
                float s = ttScore[slot];
                if (ttFlag[slot] == 1) return s;
                if (ttFlag[slot] == 2 && s >= beta) return s;
                if (ttFlag[slot] == 3 && s <= alpha) return s;
            }
        }

        List<int> moves = moveLists[ply];
        moves.Clear();
        board.Moves(mover, moves, false);
        if (moves.Count == 0) return Evaluate(); // stuck (in multiplayer the turn would pass)
        OrderMoves(moves, ply, ttBest);

        float alpha0 = alpha, beta0 = beta;
        float best = maximizing ? -WIN * 2 : WIN * 2;
        int bestMove = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            int m = moves[i];
            byte victim = board.cell[LiteBoard.To(m)];
            float score;
            if (victim != 0 && LiteBoard.TypeOf(victim) == LiteBoard.King)
            {
                // Taking a king ends it: the robot's king lost, or the robot took one
                if (LiteBoard.ColorOf(victim) == rootColor) score = -WIN + ply;
                else if (maximizing) score = WIN - ply;
                else continue; // one opponent taking another's king: not the robot's concern
            }
            else
            {
                byte piece = Make(m);
                score = Search(depth - 1, alpha, beta, Next(moverIndex), ply + 1);
                Unmake(m, piece, victim);
            }
            if (aborted) return 0;

            if (maximizing ? score > best : score < best) { best = score; bestMove = m; }
            if (maximizing) { if (score > alpha) alpha = score; }
            else { if (score < beta) beta = score; }
            if (beta <= alpha)
            {
                if (victim == 0 && killers[ply, 0] != m) { killers[ply, 1] = killers[ply, 0]; killers[ply, 0] = m; }
                break;
            }
        }

        // Every move loses the king. If it isn't in check now, that's stalemate, not checkmate:
        // a draw with two players (with more, the stuck player just sits out a turn)
        bool kingLost = maximizing ? best <= -WIN + ply + 1.5f : best >= WIN - ply - 1.5f;
        if (kingLost && !(order.Length > 2 && !maximizing))
        {
            int king = board.KingOf(mover);
            if (king >= 0 && !board.Attacked(king)) return order.Length > 2 ? Evaluate() : 0f;
        }

        // Remember it (scores near a win depend on the ply, so those aren't kept)
        if (System.Math.Abs(best) < WIN - 1000)
        {
            ttKey[slot] = key;
            ttScore[slot] = best;
            ttDepth[slot] = (sbyte)System.Math.Min(depth, 127);
            ttMove[slot] = bestMove;
            ttFlag[slot] = best <= alpha0 ? (byte)3 : best >= beta0 ? (byte)2 : (byte)1;
        }
        return best;
    }

    /// <summary>Follow captures until the position is quiet, so an exchange isn't judged halfway</summary>
    private float Quiesce(float alpha, float beta, int moverIndex, int qdepth, int ply)
    {
        nodes++;
        if (OutOfTime()) return 0;
        float standPat = Evaluate();
        if (qdepth >= MAX_QUIESCENCE) return standPat;
        int mover = order[moverIndex];
        bool maximizing = mover == rootColor;

        // The player to move may decline to capture
        if (maximizing) { if (standPat >= beta) return standPat; if (standPat > alpha) alpha = standPat; }
        else { if (standPat <= alpha) return standPat; if (standPat < beta) beta = standPat; }

        float best = standPat;
        List<int> captures = moveLists[System.Math.Min(ply, moveLists.Length - 1)];
        captures.Clear();
        board.Moves(mover, captures, true);
        OrderMoves(captures, -1, 0);
        int limit = System.Math.Min(captures.Count, MAX_QUIESCENCE_MOVES);
        for (int i = 0; i < limit; i++)
        {
            int m = captures[i];
            byte victim = board.cell[LiteBoard.To(m)];
            float score;
            if (LiteBoard.TypeOf(victim) == LiteBoard.King)
            {
                if (LiteBoard.ColorOf(victim) == rootColor) score = -WIN + ply;
                else if (maximizing) score = WIN - ply;
                else continue;
            }
            else
            {
                byte piece = Make(m);
                score = Quiesce(alpha, beta, Next(moverIndex), qdepth + 1, ply + 1);
                Unmake(m, piece, victim);
            }
            if (aborted) return 0;
            if (maximizing) { if (score > best) best = score; if (score > alpha) alpha = score; }
            else { if (score < best) best = score; if (score < beta) beta = score; }
            if (beta <= alpha) break;
        }
        return best;
    }

    private byte Make(int m)
    {
        int from = LiteBoard.From(m), to = LiteBoard.To(m);
        byte piece = board.cell[from];
        board.Put(from, 0);
        board.Put(to, LiteBoard.Promotes(m) ? LiteBoard.Make(LiteBoard.ColorOf(piece), LiteBoard.Queen) : piece);
        return piece;
    }

    private void Unmake(int m, byte piece, byte victim)
    {
        int from = LiteBoard.From(m), to = LiteBoard.To(m);
        board.Put(to, victim);
        board.Put(from, piece);
    }

    // ───────────────────────── Move order ─────────────────────────

    private readonly List<(int move, int key)> sortBuffer = new List<(int, int)>(128);

    /// <summary>Best guess first: the remembered best move, captures (big victims, small attackers), killers, the rest</summary>
    private void OrderMoves(List<int> moves, int ply, int ttBest)
    {
        sortBuffer.Clear();
        foreach (int m in moves)
        {
            int key;
            byte victim = board.cell[LiteBoard.To(m)];
            if (m == ttBest && ttBest != 0) key = 1_000_000;
            else if (victim != 0) key = 100_000 + PieceValue100(LiteBoard.TypeOf(victim)) * 10 - PieceValue100(LiteBoard.TypeOf(board.cell[LiteBoard.From(m)])) / 10;
            else if (LiteBoard.Promotes(m)) key = 90_000;
            else if (ply >= 0 && ply < killers.GetLength(0) && (killers[ply, 0] == m || killers[ply, 1] == m)) key = 50_000;
            else key = 0;
            sortBuffer.Add((m, key));
        }
        sortBuffer.Sort((a, b) => b.key.CompareTo(a.key));
        for (int i = 0; i < moves.Count; i++) moves[i] = sortBuffer[i].move;
    }

    private static int PieceValue100(int type)
    {
        switch (type)
        {
            case LiteBoard.Pawn: return 100;
            case LiteBoard.Knight: return 300;
            case LiteBoard.Bishop: return 320;
            case LiteBoard.Rook: return 500;
            case LiteBoard.Queen: return 900;
            default: return 10000;
        }
    }

    // ───────────────────────── Evaluation (always for the robot) ─────────────────────────

    private readonly float[] sideScore = new float[6];

    /// <summary>
    /// The robot's side minus the average opponent: material, a little for central pieces,
    /// pieces attacked and defended, mobility, and pieces around the king (as RobotSearch).
    /// </summary>
    private float Evaluate()
    {
        System.Array.Clear(sideScore, 0, sideScore.Length);
        int n = board.N;
        float c = (n - 1) / 2f;
        float maxDist = (float)System.Math.Sqrt(3 * c * c);

        for (int sq = 0; sq < board.Cells; sq++)
        {
            byte p = board.cell[sq];
            if (p == 0) continue;
            int color = LiteBoard.ColorOf(p), type = LiteBoard.TypeOf(p);
            float s = Value(type);
            board.Coords(sq, out int x, out int y, out int z);
            if (type != LiteBoard.King)
            {
                float dx = x - c, dy = y - c, dz = z - c;
                s += (maxDist - (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz)) * 0.06f;
            }

            board.Reach(sq, out int reach, hits);
            s += reach * 0.02f; // mobility
            foreach (int h in hits)
            {
                byte t = board.cell[h];
                if (LiteBoard.ColorOf(t) == color) s += 0.04f;                    // defends a friend
                else if (LiteBoard.TypeOf(t) == LiteBoard.King) s += 0.5f;      // gives check
                else s += Value(LiteBoard.TypeOf(t)) * 0.05f;                    // pressure
            }

            if (type == LiteBoard.King)
            {
                int guards = 0;
                for (int ax = -1; ax <= 1; ax++)
                for (int ay = -1; ay <= 1; ay++)
                for (int az = -1; az <= 1; az++)
                {
                    if (ax == 0 && ay == 0 && az == 0) continue;
                    int gx = x + ax, gy = y + ay, gz = z + az;
                    if (gx < 0 || gy < 0 || gz < 0 || gx >= n || gy >= n || gz >= n) continue;
                    byte g = board.cell[board.Index(gx, gy, gz)];
                    if (g != 0 && LiteBoard.ColorOf(g) == color) guards++;
                }
                s += System.Math.Min(guards, 4) * 0.1f;
            }
            sideScore[color] += s;
        }

        float others = 0f;
        int count = 0;
        foreach (int col in order)
        {
            if (col == rootColor) continue;
            others += sideScore[col];
            count++;
        }
        return sideScore[rootColor] - (count > 0 ? others / count : 0f);
    }

    private static float Value(int type)
    {
        switch (type)
        {
            case LiteBoard.Pawn: return 1f;
            case LiteBoard.Knight: return 3f;
            case LiteBoard.Bishop: return 3.2f;
            case LiteBoard.Rook: return 5f;
            case LiteBoard.Queen: return 9f;
            default: return 1000f;
        }
    }
}
