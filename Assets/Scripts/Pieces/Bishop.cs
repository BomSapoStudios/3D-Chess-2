using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bishop piece that can move diagonally in any dimension within the 4x4x4 board
/// Moves only in directions where at least 2 coordinates change simultaneously
/// </summary>
public class Bishop : ChessPiece
{
    public override List<BoardPosition> GetValidMoves()
    {
        List<BoardPosition> validMoves = new List<BoardPosition>();
        
        // Bishop can move diagonally in 20 directions (excludes 6 straight-line directions)
        // This includes 2D diagonals and 3D diagonals, but NOT straight lines
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    // Skip the current position (no movement)
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    
                    // CRITICAL: Bishop moves only diagonally - at least 2 coordinates must change
                    // Exclude straight-line movements where only 1 coordinate changes
                    int nonZeroCount = 0;
                    if (dx != 0) nonZeroCount++;
                    if (dy != 0) nonZeroCount++;
                    if (dz != 0) nonZeroCount++;
                    
                    // Skip straight-line directions (only 1 coordinate changes)
                    if (nonZeroCount < 2) continue;
                    
                    // This direction is a valid diagonal - get all moves in this direction
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
        pieceType = ChessPieceType.Bishop;
    }
}