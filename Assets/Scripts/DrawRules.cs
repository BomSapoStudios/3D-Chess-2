using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the game calls by itself, the same rules as the server (server-cf GameEndDetection.ts):
/// • Nobody left can checkmate anybody. Two players: kings alone, King + one Bishop or Knight
///   against a lone King, or King + Bishop each. Three or more: no Rook, Queen or Pawn anywhere
///   and at most one Bishop or Knight on the whole board.
/// • 50 moves each with no capture and no pawn move.
/// • Out of time against a player who could never have checkmated (they have no Rook, Queen or
///   Pawn and at most one minor piece): a draw, not a win.
/// </summary>
public static class DrawRules
{
    public const int QuietMovesPerPlayer = 50;

    private struct Forces { public int minors, bishops; public bool heavy; }

    private static Forces ForcesOf(PieceColor color)
    {
        var f = new Forces();
        foreach (ChessPiece p in Pieces())
        {
            if (p.pieceColor != color) continue;
            switch (p.pieceType)
            {
                case ChessPieceType.Bishop: f.minors++; f.bishops++; break;
                case ChessPieceType.Knight: f.minors++; break;
                case ChessPieceType.Queen:
                case ChessPieceType.Rook:
                case ChessPieceType.Pawn: f.heavy = true; break; // a Pawn can become a Queen
            }
        }
        return f;
    }

    /// <summary>Nobody among these players can ever checkmate anybody</summary>
    public static bool IsInsufficientMaterial(IList<PieceColor> active)
    {
        if (active == null || active.Count < 2) return false;
        var all = new List<Forces>();
        foreach (PieceColor c in active) all.Add(ForcesOf(c));
        int minors = 0;
        foreach (Forces f in all) { if (f.heavy) return false; minors += f.minors; }
        if (active.Count > 2) return minors <= 1;
        if (minors <= 1) return true;
        return all[0].minors == 1 && all[1].minors == 1 && all[0].bishops == 1 && all[1].bishops == 1;
    }

    /// <summary>This player alone has too little left to ever checkmate</summary>
    public static bool CannotMate(PieceColor color)
    {
        Forces f = ForcesOf(color);
        return !f.heavy && f.minors <= 1;
    }

    /// <summary>
    /// What counts for the 50-move rule: how many pieces are on the board and where the Pawns
    /// are. A capture, a pawn move or a promotion changes it; any other move doesn't.
    /// </summary>
    public static string ProgressSignature()
    {
        var pawns = new List<string>();
        int count = 0;
        foreach (ChessPiece p in Pieces())
        {
            count++;
            if (p.pieceType == ChessPieceType.Pawn) pawns.Add($"{p.pieceColor}{p.CurrentPosition.x},{p.CurrentPosition.y},{p.CurrentPosition.z}");
        }
        pawns.Sort();
        return count + "|" + string.Join(";", pawns);
    }

    private static IEnumerable<ChessPiece> Pieces()
    {
        if (ChessBoard.Instance == null) yield break;
        Vector3Int d = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(8, 8, 8);
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) yield return p;
        }
    }
}
