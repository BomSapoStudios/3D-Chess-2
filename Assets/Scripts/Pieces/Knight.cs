using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Knight piece that moves in L-shaped patterns in 3D space.
/// Moves 2 squares straight in any cardinal direction, then 1 square orthogonally.
/// This creates 24 possible moves in 3D (6 cardinal directions × 4 perpendicular directions each).
/// </summary>
public class Knight : ChessPiece
{
    public override List<BoardPosition> GetValidMoves()
    {
        List<BoardPosition> validMoves = new List<BoardPosition>();
        
        // Knight moves in L-shapes: 2 squares in one cardinal direction, then 1 square orthogonally
        // In 3D, this means 6 cardinal directions × 4 orthogonal directions each = 24 total moves
        
        // Define the 6 cardinal directions (straight lines, no diagonals)
        Vector3Int[] cardinalDirections = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0),   // +X
            new Vector3Int(-1, 0, 0),  // -X
            new Vector3Int(0, 1, 0),   // +Y
            new Vector3Int(0, -1, 0),  // -Y
            new Vector3Int(0, 0, 1),   // +Z
            new Vector3Int(0, 0, -1)   // -Z
        };
        
        foreach (Vector3Int primaryDirection in cardinalDirections)
        {
            // Move 2 squares in the primary cardinal direction
            BoardPosition intermediatePosition = new BoardPosition(
                currentPosition.x + primaryDirection.x * 2,
                currentPosition.y + primaryDirection.y * 2,
                currentPosition.z + primaryDirection.z * 2
            );
            
            // Skip if the 2-square move goes out of bounds (Knight cannot complete its L-shape)
            if (!intermediatePosition.IsValid())
                continue;
            
            // From the intermediate position, move 1 square in each orthogonal direction
            Vector3Int[] orthogonalDirections = GetOrthogonalDirections(primaryDirection);
            
            foreach (Vector3Int orthogonalDirection in orthogonalDirections)
            {
                BoardPosition finalPosition = new BoardPosition(
                    intermediatePosition.x + orthogonalDirection.x,
                    intermediatePosition.y + orthogonalDirection.y,
                    intermediatePosition.z + orthogonalDirection.z
                );
                
                // Check if this final L-shaped move is valid
                if (IsValidMove(finalPosition))
                {
                    validMoves.Add(finalPosition);
                }
            }
        }
        
        return validMoves;
    }
    
    /// <summary>
    /// Get all directions orthogonal (perpendicular) to the given direction.
    /// In 3D space, there are 4 orthogonal directions to any cardinal direction.
    /// </summary>
    private Vector3Int[] GetOrthogonalDirections(Vector3Int direction)
    {
        List<Vector3Int> orthogonal = new List<Vector3Int>();
        
        // For each cardinal direction, find the 4 perpendicular directions
        if (direction.x != 0) // Primary movement was along X-axis
        {
            // Orthogonal directions are along Y and Z axes
            orthogonal.Add(new Vector3Int(0, 1, 0));   // +Y
            orthogonal.Add(new Vector3Int(0, -1, 0));  // -Y
            orthogonal.Add(new Vector3Int(0, 0, 1));   // +Z
            orthogonal.Add(new Vector3Int(0, 0, -1));  // -Z
        }
        else if (direction.y != 0) // Primary movement was along Y-axis
        {
            // Orthogonal directions are along X and Z axes
            orthogonal.Add(new Vector3Int(1, 0, 0));   // +X
            orthogonal.Add(new Vector3Int(-1, 0, 0));  // -X
            orthogonal.Add(new Vector3Int(0, 0, 1));   // +Z
            orthogonal.Add(new Vector3Int(0, 0, -1));  // -Z
        }
        else if (direction.z != 0) // Primary movement was along Z-axis
        {
            // Orthogonal directions are along X and Y axes
            orthogonal.Add(new Vector3Int(1, 0, 0));   // +X
            orthogonal.Add(new Vector3Int(-1, 0, 0));  // -X
            orthogonal.Add(new Vector3Int(0, 1, 0));   // +Y
            orthogonal.Add(new Vector3Int(0, -1, 0));  // -Y
        }
        
        return orthogonal.ToArray();
    }
    
    protected override void Start()
    {
        base.Start();
        pieceType = ChessPieceType.Knight;
    }
}