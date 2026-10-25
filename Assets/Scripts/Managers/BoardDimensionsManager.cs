using UnityEngine;

/// <summary>
/// Manages board dimensions for scalable 3D chess.
/// Supports 5x5x5 (2 players), 6x6x6 (2-4 players), and 8x8x8 (2-6 players) boards.
/// </summary>
public class BoardDimensionsManager : MonoBehaviour
{
    public static BoardDimensionsManager Instance { get; private set; }

    [Header("Board Configuration")]
    private BoardSize currentBoardSize = BoardSize.Small5x5x5;
    private Vector3Int boardDimensions = new Vector3Int(5, 5, 5);
    private int maxPlayers = 2;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("BoardDimensionsManager: Instance created");
        }
        else
        {
            Debug.LogWarning("BoardDimensionsManager: Duplicate instance detected, destroying");
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Set the board size and update dimensions and player limits
    /// </summary>
    public void SetBoardSize(BoardSize size)
    {
        Debug.Log($"🔍 TRACE SetBoardSize: ENTRY with size = {size}");
        Debug.Log($"🔍 TRACE SetBoardSize: currentBoardSize BEFORE = {currentBoardSize}");
        Debug.Log($"🔍 TRACE SetBoardSize: boardDimensions BEFORE = {boardDimensions}");

        currentBoardSize = size;

        switch (size)
        {
            case BoardSize.Small4x4x4:
                boardDimensions = new Vector3Int(4, 4, 4);
                maxPlayers = 2;
                Debug.Log($"🔍 TRACE SetBoardSize: Set to Small4x4x4");
                break;
            case BoardSize.Small5x5x5:
                boardDimensions = new Vector3Int(5, 5, 5);
                maxPlayers = 2;
                Debug.Log($"🔍 TRACE SetBoardSize: Set to Small5x5x5");
                break;
            case BoardSize.Medium6x6x6:
                boardDimensions = new Vector3Int(6, 6, 6);
                maxPlayers = 4;
                Debug.Log($"🔍 TRACE SetBoardSize: Set to Medium6x6x6");
                break;
            case BoardSize.Large8x8x8:
                boardDimensions = new Vector3Int(8, 8, 8);
                maxPlayers = 6;
                Debug.Log($"🔍 TRACE SetBoardSize: Set to Large8x8x8");
                break;
            default:
                boardDimensions = new Vector3Int(5, 5, 5);
                maxPlayers = 2;
                Debug.Log($"🔍 TRACE SetBoardSize: DEFAULT case hit!");
                break;
        }

        Debug.Log($"BoardDimensionsManager: Board size set to {size} ({boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z}), max players: {maxPlayers}");
        Debug.Log($"🔍 TRACE SetBoardSize: EXIT with boardDimensions = {boardDimensions}");
    }

    /// <summary>
    /// Get the current board dimensions
    /// </summary>
    public Vector3Int GetDimensions()
    {
        return boardDimensions;
    }

    /// <summary>
    /// Get maximum number of players supported by current board size
    /// </summary>
    public int GetMaxPlayers()
    {
        return maxPlayers;
    }

    /// <summary>
    /// Get the current board size enum
    /// </summary>
    public BoardSize GetBoardSize()
    {
        return currentBoardSize;
    }

    /// <summary>
    /// Validate if a player count is supported by the current board size
    /// Valid combinations:
    /// - 5x5x5 (and the 4x4x4 puzzle board): 2 players only
    /// - 6x6x6: 2 or 4 players
    /// - 8x8x8: 2, 4, or 6 players
    /// </summary>
    public bool ValidatePlayerCount(int playerCount)
    {
        // Only even player counts are supported
        if (playerCount % 2 != 0 && playerCount != 2)
        {
            Debug.LogWarning($"BoardDimensionsManager: Player count {playerCount} is invalid (must be 2, 4, or 6)");
            return false;
        }

        // Check board size constraints
        bool isValid = playerCount == 2 ||
                      (playerCount == 4 && maxPlayers >= 4) ||
                      (playerCount == 6 && maxPlayers >= 6);

        if (!isValid)
        {
            Debug.LogWarning($"BoardDimensionsManager: {playerCount} players not supported on {currentBoardSize} board (max: {maxPlayers})");
        }

        return isValid;
    }

    /// <summary>
    /// Get the total number of board cells
    /// </summary>
    public int GetTotalCells()
    {
        return boardDimensions.x * boardDimensions.y * boardDimensions.z;
    }

    /// <summary>
    /// Check if a position is within board bounds
    /// </summary>
    public bool IsPositionValid(BoardPosition position)
    {
        return position.x >= 0 && position.x < boardDimensions.x &&
               position.y >= 0 && position.y < boardDimensions.y &&
               position.z >= 0 && position.z < boardDimensions.z;
    }

    /// <summary>
    /// Check if a position is within board bounds (Vector3Int version)
    /// </summary>
    public bool IsPositionValid(Vector3Int position)
    {
        return position.x >= 0 && position.x < boardDimensions.x &&
               position.y >= 0 && position.y < boardDimensions.y &&
               position.z >= 0 && position.z < boardDimensions.z;
    }
}
