using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queen piece that can move in straight lines in any direction within the 4x4x4 board
/// Combines the movement patterns of a 3D Rook and Bishop with capture logic
/// </summary>
public class Queen : ChessPiece
{
    public override List<BoardPosition> GetValidMoves()
    {
        if (QueenBlitz.Active) return QueenBlitz.SweepMoves(this); // drives through pawns, stops at blocks

        List<BoardPosition> validMoves = new List<BoardPosition>();
        
        // Queen can move in all 26 directions (like King but unlimited range)
        // This includes straight lines, diagonals, and 3D diagonals
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    // Skip the current position (no movement)
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    
                    // Get all valid moves in this direction until blocked or board edge
                    // GetValidMovesInDirection handles board bounds, obstacles, and capture logic
                    Vector3Int direction = new Vector3Int(dx, dy, dz);
                    List<BoardPosition> movesInDirection = GetValidMovesInDirection(direction); // Slides until the board edge or a piece
                    
                    validMoves.AddRange(movesInDirection);
                }
            }
        }
        
        return validMoves;
    }
    
    protected override void Start()
    {
        base.Start();
        pieceType = ChessPieceType.Queen;
    }
}