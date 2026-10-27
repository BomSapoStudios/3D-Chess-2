using UnityEngine;

public class BoardCell : MonoBehaviour
{
    public BoardPosition Position { get; private set; }
    
    public void Initialize(BoardPosition position)
    {
        Position = position;
    }
    
    // Taps are handled by InputManager when the finger lifts. A board cell used to act on
    // OnMouseDown, which fires as the finger touches, so starting to turn the board could move a piece.

    private void OnTriggerEnter(Collider other)
    {
        ChessPiece piece = other.GetComponent<ChessPiece>();
        if (piece != null)
        {
            // Handle piece entering this cell
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        ChessPiece piece = other.GetComponent<ChessPiece>();
        if (piece != null)
        {
            // Handle piece exiting this cell
        }
    }
}