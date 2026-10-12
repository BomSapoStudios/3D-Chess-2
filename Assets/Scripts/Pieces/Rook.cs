using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rook piece that moves in straight lines only within the 4x4x4 board.
/// Moves only in cardinal directions where exactly 1 coordinate changes.
/// This is the complement to Bishop's diagonal-only movement.
/// </summary>
public class Rook : ChessPiece
{
    public override List<BoardPosition> GetValidMoves()
    {
        List<BoardPosition> validMoves = new List<BoardPosition>();
        
        // Rook can move in straight lines only - exactly 1 coordinate changes
        // This includes the 6 cardinal directions: ±X, ±Y, ±Z
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    // Skip the current position (no movement)
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    
                    // CRITICAL: Rook moves only in straight lines - exactly 1 coordinate must change
                    // Exclude diagonal movements where 2 or 3 coordinates change
                    int nonZeroCount = 0;
                    if (dx != 0) nonZeroCount++;
                    if (dy != 0) nonZeroCount++;
                    if (dz != 0) nonZeroCount++;
                    
                    // Include ONLY straight-line directions (exactly 1 coordinate changes)
                    if (nonZeroCount != 1) continue;
                    
                    // This direction is a valid straight line - get all moves in this direction
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
        pieceType = ChessPieceType.Rook;
    }
}