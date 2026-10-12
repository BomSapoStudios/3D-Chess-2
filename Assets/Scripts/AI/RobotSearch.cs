using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// How a robot picks its move. Plays on the real board in simulation mode (pieces are moved
/// and put back; nothing is shown) and always scores positions for the robot that is moving.
///
///  - Turn order follows the real game, so with 3+ players every opponent gets their turn in
///    the search. Every opponent is assumed to play against the robot ("paranoid" search).
///  - Moves are tried captures first (most valuable victim, cheapest attacker), and the best
///    move from the last pass first, so alpha-beta cuts off more.
///  - Capturing a king ends the line: a move that leaves the robot's king takeable loses, and
///    a position where every move does is checkmate.
///  - At the end of the search, captures are followed until things go quiet, so the robot
///    doesn't stop looking halfway through an exchange.
///  - Iterative deepening: one move deeper each pass until the difficulty's time budget runs
///    out. The work is split across frames between passes so the screen keeps moving.
/// </summary>
public class RobotSearch
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
                    return new Settings { maxDepth = 6, seconds = 1.2f, quiescence = true, noise = 0f, blunderChance = 0f };
                default:
                    return new Settings { maxDepth = 3, seconds = 0.6f, quiescence = true, noise = 0.25f, blunderChance = 0f };
            }
        }
    }

    private const float WIN = 100000f;
    private const float KING_VALUE = 1000f;
    private const int MAX_QUIESCENCE = 3;
    private const int MAX_QUIESCENCE_MOVES = 6; // only the most promising captures (most valuable victim first)

    private PieceColor root;
    private List<PieceColor> order;           // active players in turn order
    private Vector3Int dims;
    private Stopwatch clock;
    private float budget;
    private bool aborted;
    private bool useQuiescence;
    public int Nodes { get; private set; }
    public int DepthReached { get; private set; }

    private struct Move
    {
        public ChessPiece piece;
        public BoardPosition from, to;
        public ChessPiece victim;
        public int order;
    }

    // ───────────────────────── Entry points ─────────────────────────

    /// <summary>Think across several frames; result arrives through done (null if no legal move)</summary>
    public IEnumerator FindMoveRoutine(PieceColor player, Settings settings, System.Action<AIMove> done)
    {
        Begin(player, settings);
        List<Move> rootMoves = RootMoves(player);
        if (rootMoves.Count == 0) { done(null); yield break; }

        Move best = rootMoves[0];
        List<(Move move, float score)> scored = null;
        for (int depth = 1; depth <= settings.maxDepth; depth++)
        {
            // With a random wobble every root score must be exact, not just "no better than the best"
            var result = SearchRoot(rootMoves, depth, exact: settings.noise > 0f);
            if (aborted) break;
            scored = result;
            DepthReached = depth;
            best = scored[0].move;
            // Best first next time
            rootMoves.Sort((a, b) => IndexOf(scored, b).CompareTo(IndexOf(scored, a)));
            if (scored[0].score >= WIN - 100) break;          // a forced win: no need to look further
            if (clock.Elapsed.TotalSeconds > budget * 0.5f) break; // the next pass would take longer than what's left
            // Let a frame draw between passes; the clock only counts time spent thinking
            clock.Stop();
            yield return null;
            clock.Start();
        }
        done(Choose(scored, best, settings));
    }

    /// <summary>The same, all at once (tests)</summary>
    public AIMove FindMove(PieceColor player, Settings settings)
    {
        AIMove result = null;
        IEnumerator routine = FindMoveRoutine(player, settings, m => result = m);
        while (routine.MoveNext()) { }
        return result;
    }

    // ───────────────────────── Search ─────────────────────────

    private void Begin(PieceColor player, Settings settings)
    {
        root = player;
        dims = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        order = new List<PieceColor>();
        foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
        {
            bool active = PlayerManager.Instance != null
                ? PlayerManager.Instance.GetActivePlayers().Contains(c) && !PlayerManager.Instance.IsPlayerEliminated(c)
                : c == PieceColor.White || c == PieceColor.Black;
            if (active) order.Add(c);
        }
        if (!order.Contains(player)) order.Add(player);
        clock = Stopwatch.StartNew();
        budget = settings.seconds;
        useQuiescence = settings.quiescence;
        aborted = false;
        Nodes = 0;
        DepthReached = 0;
    }

    private PieceColor Next(PieceColor c)
    {
        int i = order.IndexOf(c);
        return order[(i + 1) % order.Count];
    }

    private bool OutOfTime()
    {
        // The first pass always finishes (otherwise a crowded opening board could leave no
        // scored move at all); after that, stop when the time is up
        if ((Nodes & 255) == 0 && DepthReached >= 1 && clock.Elapsed.TotalSeconds > budget) aborted = true;
        return aborted;
    }

    /// <summary>Score every root move at this depth, best first</summary>
    private List<(Move move, float score)> SearchRoot(List<Move> moves, int depth, bool exact)
    {
        var scored = new List<(Move, float)>();
        var board = ChessBoard.Instance;
        board.EnableSimulationMode();
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.EnableSimulationMode();
        try
        {
            float alpha = -WIN * 2;
            foreach (Move m in moves)
            {
                Make(m);
                float score = m.victim != null && m.victim.pieceType == ChessPieceType.King
                    ? WIN
                    : Search(depth - 1, alpha, WIN * 2, Next(root), 1);
                Unmake(m);
                if (aborted) break;
                scored.Add((m, score));
                if (score > alpha && !exact) alpha = score;
            }
        }
        finally
        {
            board.DisableSimulationMode();
            if (CheckDetectionManager.Instance != null)
            {
                CheckDetectionManager.Instance.DisableSimulationMode();
                CheckDetectionManager.Instance.InvalidateCache();
            }
        }
        // Moves after the first got narrow windows; a score at the bound means "no better than"
        scored.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return scored;
    }

    /// <summary>Paranoid alpha-beta: the root maximizes, every other player minimizes the root's score</summary>
    private float Search(int depth, float alpha, float beta, PieceColor mover, int ply)
    {
        Nodes++;
        if (OutOfTime()) return 0;
        if (depth <= 0) return useQuiescence ? Quiesce(alpha, beta, mover, 0, ply) : Evaluate();

        List<Move> moves = Moves(mover, false);
        bool maximizing = mover == root;
        if (moves.Count == 0) return Evaluate(); // stuck (in multiplayer the turn would pass)

        float best = maximizing ? -WIN * 2 : WIN * 2;
        foreach (Move m in moves)
        {
            float score;
            if (m.victim != null && m.victim.pieceType == ChessPieceType.King)
            {
                // Taking a king ends it: the robot's king lost, or the robot took one
                if (m.victim.pieceColor == root) score = -WIN + ply;
                else if (maximizing) score = WIN - ply;
                else continue; // one opponent taking another's king: not the robot's concern
            }
            else
            {
                Make(m);
                score = Search(depth - 1, alpha, beta, Next(mover), ply + 1);
                Unmake(m);
            }
            if (aborted) return 0;

            if (maximizing)
            {
                if (score > best) best = score;
                if (score > alpha) alpha = score;
            }
            else
            {
                if (score < best) best = score;
                if (score < beta) beta = score;
            }
            if (beta <= alpha) break;
        }

        // Every move loses the king. If it isn't in check now, that's stalemate, not checkmate:
        // a draw with two players (with more, the stuck player just sits out a turn)
        bool kingLost = maximizing ? best <= -WIN + ply + 1.5f : best >= WIN - ply - 1.5f;
        if (kingLost && !(order.Count > 2 && !maximizing) && !KingAttacked(mover))
            return order.Count > 2 ? Evaluate() : 0f;
        return best;
    }

    /// <summary>Is this player's king attacked by anyone right now?</summary>
    private bool KingAttacked(PieceColor color)
    {
        List<ChessPiece> all = Pieces();
        ChessPiece king = all.Find(p => p.pieceColor == color && p.pieceType == ChessPieceType.King);
        if (king == null) return false;
        foreach (ChessPiece p in all)
            if (p.pieceColor != color && p.GetAttackSquares().Contains(king.CurrentPosition)) return true;
        return false;
    }

    /// <summary>Follow captures until the position is quiet, so an exchange isn't judged halfway</summary>
    private float Quiesce(float alpha, float beta, PieceColor mover, int qdepth, int ply)
    {
        Nodes++;
        if (OutOfTime()) return 0;
        float standPat = Evaluate();
        if (qdepth >= MAX_QUIESCENCE) return standPat;
        bool maximizing = mover == root;

        // The player to move may decline to capture
        if (maximizing) { if (standPat >= beta) return standPat; if (standPat > alpha) alpha = standPat; }
        else { if (standPat <= alpha) return standPat; if (standPat < beta) beta = standPat; }

        float best = standPat;
        List<Move> captures = Moves(mover, true);
        if (captures.Count > MAX_QUIESCENCE_MOVES) captures.RemoveRange(MAX_QUIESCENCE_MOVES, captures.Count - MAX_QUIESCENCE_MOVES);
        foreach (Move m in captures)
        {
            float score;
            if (m.victim.pieceType == ChessPieceType.King)
            {
                if (m.victim.pieceColor == root) score = -WIN + ply;
                else if (maximizing) score = WIN - ply;
                else continue;
            }
            else
            {
                Make(m);
                score = Quiesce(alpha, beta, Next(mover), qdepth + 1, ply + 1);
                Unmake(m);
            }
            if (aborted) return 0;
            if (maximizing)
            {
                if (score > best) best = score;
                if (score > alpha) alpha = score;
            }
            else
            {
                if (score < best) best = score;
                if (score < beta) beta = score;
            }
            if (beta <= alpha) break;
        }
        return best;
    }

    // ───────────────────────── Moves ─────────────────────────

    /// <summary>The robot's real legal moves (never leaves its own king in check)</summary>
    private List<Move> RootMoves(PieceColor player)
    {
        var moves = new List<Move>();
        foreach (ChessPiece piece in Pieces())
        {
            if (piece.pieceColor != player) continue;
            foreach (BoardPosition to in piece.GetLegalMoves())
            {
                ChessPiece victim = ChessBoard.Instance.GetPieceAt(to);
                if (victim != null && victim.pieceColor == player) continue;
                moves.Add(new Move { piece = piece, from = piece.CurrentPosition, to = to, victim = victim, order = OrderScore(piece, victim) });
            }
        }
        moves.Sort((a, b) => b.order.CompareTo(a.order));
        return moves;
    }

    /// <summary>Moves inside the search (may leave the king in check; the reply takes it). Captures first.</summary>
    private List<Move> Moves(PieceColor player, bool capturesOnly)
    {
        var moves = new List<Move>();
        foreach (ChessPiece piece in Pieces())
        {
            if (piece.pieceColor != player) continue;
            List<BoardPosition> targets = capturesOnly ? piece.GetAttackSquares() : piece.GetValidMoves();
            foreach (BoardPosition to in targets)
            {
                ChessPiece victim = ChessBoard.Instance.GetPieceAt(to);
                if (victim != null && victim.pieceColor == player) continue;
                if (capturesOnly && victim == null) continue;
                if (capturesOnly && piece.pieceType == ChessPieceType.Pawn && !piece.GetValidMoves().Contains(to)) continue;
                moves.Add(new Move { piece = piece, from = piece.CurrentPosition, to = to, victim = victim, order = OrderScore(piece, victim) });
            }
        }
        moves.Sort((a, b) => b.order.CompareTo(a.order));
        return moves;
    }

    /// <summary>Most valuable victim, least valuable attacker</summary>
    private static int OrderScore(ChessPiece piece, ChessPiece victim)
    {
        if (victim == null) return 0;
        return (int)(Value(victim.pieceType) * 10) - (int)Value(piece.pieceType);
    }

    private void Make(Move m)
    {
        var board = ChessBoard.Instance;
        board.SetPieceAt(m.from, null);
        board.SetPieceAt(m.to, m.piece);
        m.piece.SetCurrentPosition(m.to);
    }

    private void Unmake(Move m)
    {
        var board = ChessBoard.Instance;
        board.SetPieceAt(m.to, m.victim);
        board.SetPieceAt(m.from, m.piece);
        m.piece.SetCurrentPosition(m.from);
        if (m.victim != null) m.victim.SetCurrentPosition(m.to);
    }

    private List<ChessPiece> Pieces()
    {
        var list = new List<ChessPiece>(32);
        var board = ChessBoard.Instance;
        for (int x = 0; x < dims.x; x++)
        for (int y = 0; y < dims.y; y++)
        for (int z = 0; z < dims.z; z++)
        {
            ChessPiece p = board.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) list.Add(p);
        }
        return list;
    }

    // ───────────────────────── Evaluation (always for the robot) ─────────────────────────

    private readonly Dictionary<PieceColor, float> sideScore = new Dictionary<PieceColor, float>();

    /// <summary>
    /// The robot's side minus the average opponent: material, a little for central and
    /// advanced pieces, pieces attacked and defended, mobility, and pieces around the king.
    /// </summary>
    private float Evaluate()
    {
        sideScore.Clear();
        foreach (PieceColor c in order) sideScore[c] = 0f;
        var board = ChessBoard.Instance;
        Vector3 center = new Vector3((dims.x - 1) / 2f, (dims.y - 1) / 2f, (dims.z - 1) / 2f);
        float maxDist = center.magnitude;

        foreach (ChessPiece p in Pieces())
        {
            if (!sideScore.ContainsKey(p.pieceColor)) continue;
            float s = Value(p.pieceType);
            BoardPosition pos = p.CurrentPosition;
            float dist = Vector3.Distance(new Vector3(pos.x, pos.y, pos.z), center);
            if (p.pieceType != ChessPieceType.King) s += (maxDist - dist) * 0.06f;

            List<BoardPosition> attacks = p.GetAttackSquares();
            s += attacks.Count * 0.02f; // mobility
            foreach (BoardPosition a in attacks)
            {
                ChessPiece t = board.GetPieceAt(a);
                if (t == null) continue;
                if (t.pieceColor == p.pieceColor) s += 0.04f;                       // defends a friend
                else if (t.pieceType == ChessPieceType.King) s += 0.5f;            // gives check
                else s += Value(t.pieceType) * 0.05f;                               // pressure
            }

            if (p.pieceType == ChessPieceType.King)
            {
                int guards = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    var n = new BoardPosition(pos.x + dx, pos.y + dy, pos.z + dz);
                    if (n.x < 0 || n.y < 0 || n.z < 0 || n.x >= dims.x || n.y >= dims.y || n.z >= dims.z) continue;
                    ChessPiece g = board.GetPieceAt(n);
                    if (g != null && g.pieceColor == p.pieceColor) guards++;
                }
                s += Mathf.Min(guards, 4) * 0.1f;
            }
            sideScore[p.pieceColor] += s;
        }

        float mine = sideScore.TryGetValue(root, out float r) ? r : 0f;
        float others = 0f;
        int count = 0;
        foreach (var kv in sideScore)
        {
            if (kv.Key == root) continue;
            others += kv.Value;
            count++;
        }
        return mine - (count > 0 ? others / count : 0f);
    }

    public static float Value(ChessPieceType t)
    {
        switch (t)
        {
            case ChessPieceType.Pawn: return 1f;
            case ChessPieceType.Knight: return 3f;
            case ChessPieceType.Bishop: return 3.2f;
            case ChessPieceType.Rook: return 5f;
            case ChessPieceType.Queen: return 9f;
            case ChessPieceType.King: return KING_VALUE;
            default: return 0f;
        }
    }

    // ───────────────────────── Picking the move ─────────────────────────

    private static int IndexOf(List<(Move move, float score)> scored, Move m)
    {
        for (int i = 0; i < scored.Count; i++)
            if (scored[i].move.piece == m.piece && scored[i].move.to == m.to) return scored.Count - i;
        return 0;
    }

    /// <summary>The best move, give or take the difficulty's wobble and occasional slip</summary>
    private AIMove Choose(List<(Move move, float score)> scored, Move fallback, Settings settings)
    {
        Move pick = fallback;
        float pickScore = 0f;
        if (scored != null && scored.Count > 0)
        {
            // Never throw away a forced win or walk into a forced loss on purpose
            bool decided = scored[0].score >= WIN - 100;
            int bestIndex = 0;
            if (!decided && settings.noise > 0f)
            {
                float bestNoisy = float.NegativeInfinity;
                for (int i = 0; i < scored.Count; i++)
                {
                    if (scored[i].score <= -WIN + 100) continue;
                    float noisy = scored[i].score + Random.Range(-settings.noise, settings.noise);
                    if (noisy > bestNoisy) { bestNoisy = noisy; bestIndex = i; }
                }
            }
            if (!decided && settings.blunderChance > 0f && Random.value < settings.blunderChance)
            {
                int safe = 0;
                while (safe < scored.Count && safe < 4 && scored[safe].score > -WIN + 100) safe++;
                if (safe > 0) bestIndex = Random.Range(0, safe);
            }
            pick = scored[bestIndex].move;
            pickScore = scored[bestIndex].score;
        }
        return new AIMove { piece = pick.piece, fromPosition = pick.from, toPosition = pick.to, evaluationScore = pickScore };
    }
}
