using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Works out what a move would do, for the Turn Planner. Each move is tried on the board
/// in simulation (nothing visible changes) and compared with the position before it:
///  - what it captures,
///  - which enemy pieces could take the moved piece on its new square,
///  - which of your other pieces are newly attackable because of the move,
///  - which enemy pieces the moved piece would threaten from its new square.
/// </summary>
public static class TurnPlannerAnalysis
{
    public class MoveOutcome
    {
        public ChessPiece piece;
        public BoardPosition from, to;
        public ChessPiece captures;
        public List<ChessPiece> attackers = new List<ChessPiece>();     // can take the moved piece afterwards
        public List<ChessPiece> defenders = new List<ChessPiece>();     // your pieces guarding the new square
        public List<(ChessPiece piece, List<ChessPiece> by)> exposed = new List<(ChessPiece, List<ChessPiece>)>();
        public List<ChessPiece> threatens = new List<ChessPiece>();     // enemy pieces it attacks from the new square
        public bool givesCheck;

        public bool IsSafe => attackers.Count == 0;
        public int Score =>
            (captures != null ? Value(captures.pieceType) * 10 : 0)
            - (attackers.Count > 0 ? Value(piece.pieceType) * (defenders.Count > 0 ? 4 : 8) : 0)
            - exposed.Count * 6 + threatens.Count * 2 + (givesCheck ? 5 : 0);
    }

    public static int Value(ChessPieceType t)
    {
        switch (t)
        {
            case ChessPieceType.Queen: return 9;
            case ChessPieceType.Rook: return 5;
            case ChessPieceType.Bishop:
            case ChessPieceType.Knight: return 3;
            case ChessPieceType.King: return 12;
            default: return 1;
        }
    }

    /// <summary>All of a player's pieces that have at least one legal move</summary>
    public static List<ChessPiece> MovablePieces(PieceColor color)
    {
        var result = new List<ChessPiece>();
        foreach (ChessPiece p in AllPieces())
        {
            if (p.pieceColor == color && p.GetLegalMoves().Count > 0) result.Add(p);
        }
        result.Sort((a, b) =>
        {
            int byValue = Value(b.pieceType).CompareTo(Value(a.pieceType));
            return byValue != 0 ? byValue : string.CompareOrdinal(Label(a.CurrentPosition), Label(b.CurrentPosition));
        });
        return result;
    }

    /// <summary>Outcomes of every legal move for a piece, best first</summary>
    public static List<MoveOutcome> Analyze(ChessPiece piece)
    {
        var outcomes = new List<MoveOutcome>();
        if (piece == null || ChessBoard.Instance == null) return outcomes;

        BoardPosition from = piece.CurrentPosition;
        List<BoardPosition> moves = piece.GetLegalMoves();
        var attackedBefore = AttackedPieces(piece.pieceColor);

        var board = ChessBoard.Instance;
        bool cache = CheckDetectionManager.Instance != null;
        board.EnableSimulationMode();
        try
        {
            foreach (BoardPosition to in moves)
            {
                var o = new MoveOutcome { piece = piece, from = from, to = to };
                ChessPiece target = board.GetPieceAt(to);
                if (target != null && target.pieceColor != piece.pieceColor) o.captures = target;

                // Try the move
                board.SetPieceAt(from, null);
                board.SetPieceAt(to, piece);
                piece.SetCurrentPosition(to);
                if (target != null) target.SetCurrentPosition(new BoardPosition(-1, -1, -1));
                if (cache) CheckDetectionManager.Instance.InvalidateCache();

                o.attackers = AttackersOf(to, piece.pieceColor, enemies: true);
                o.defenders = AttackersOf(to, piece.pieceColor, enemies: false, except: piece);
                foreach (var kv in AttackedPieces(piece.pieceColor))
                {
                    if (kv.Key == piece) continue;
                    if (!attackedBefore.ContainsKey(kv.Key)) o.exposed.Add((kv.Key, kv.Value));
                }
                foreach (BoardPosition sq in piece.GetAttackSquares())
                {
                    ChessPiece victim = board.GetPieceAt(sq);
                    if (victim != null && victim.pieceColor != piece.pieceColor)
                    {
                        o.threatens.Add(victim);
                        if (victim.pieceType == ChessPieceType.King) o.givesCheck = true;
                    }
                }

                // Undo
                board.SetPieceAt(to, target);
                board.SetPieceAt(from, piece);
                piece.SetCurrentPosition(from);
                if (target != null) target.SetCurrentPosition(to);
                outcomes.Add(o);
            }
        }
        finally
        {
            board.DisableSimulationMode();
            if (cache) CheckDetectionManager.Instance.InvalidateCache();
        }

        outcomes.Sort((a, b) => b.Score.CompareTo(a.Score));
        return outcomes;
    }

    /// <summary>A1a-style name of a square: file letter, floor number, rank letter</summary>
    public static string Label(BoardPosition p) => ChessNotationConverter.BoardPositionToNotation(p);

    public static string Name(ChessPiece p) => $"{p.pieceColor} {p.pieceType}";

    // ── helpers ──

    private static IEnumerable<ChessPiece> AllPieces()
    {
        var board = ChessBoard.Instance;
        Vector3Int d = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = board.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) yield return p;
        }
    }

    private static bool IsEnemy(PieceColor a, PieceColor b)
    {
        if (a == b) return false;
        return PlayerManager.Instance == null || !PlayerManager.Instance.IsPlayerEliminated(b);
    }

    /// <summary>Pieces (enemy or friendly to `color`) that attack a square</summary>
    private static List<ChessPiece> AttackersOf(BoardPosition square, PieceColor color, bool enemies, ChessPiece except = null)
    {
        var list = new List<ChessPiece>();
        foreach (ChessPiece p in AllPieces())
        {
            if (p == except) continue;
            bool enemy = IsEnemy(color, p.pieceColor);
            if (enemies != enemy || (!enemies && p.pieceColor != color)) continue;
            if (p.GetAttackSquares().Contains(square)) list.Add(p);
        }
        return list;
    }

    /// <summary>Each of `color`'s pieces that an enemy can take, and by whom</summary>
    private static Dictionary<ChessPiece, List<ChessPiece>> AttackedPieces(PieceColor color)
    {
        var result = new Dictionary<ChessPiece, List<ChessPiece>>();
        var mine = new List<ChessPiece>();
        var theirs = new List<ChessPiece>();
        foreach (ChessPiece p in AllPieces())
        {
            if (p.pieceColor == color) mine.Add(p);
            else if (IsEnemy(color, p.pieceColor)) theirs.Add(p);
        }
        foreach (ChessPiece enemy in theirs)
        {
            foreach (BoardPosition sq in enemy.GetAttackSquares())
            {
                foreach (ChessPiece m in mine)
                {
                    if (m.CurrentPosition != sq) continue;
                    if (!result.TryGetValue(m, out var by)) result[m] = by = new List<ChessPiece>();
                    by.Add(enemy);
                }
            }
        }
        return result;
    }
}
