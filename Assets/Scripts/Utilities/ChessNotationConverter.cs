using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Utility class for converting between 3D chess board positions and standard chess notation.
/// Supports 4x4x4, 6x6x6, and 8x8x8 board configurations.
///
/// Notation System:
/// - X-axis (files): A-H (A=0, B=1, C=2, ...)
/// - Y-axis (levels): 1-8 (1=0, 2=1, 3=2, ...)
/// - Z-axis (ranks): a-h (a=0, b=1, c=2, ...)
///
/// Example: Position (1, 2, 2) = "B3c"
/// </summary>
public static class ChessNotationConverter
{
    // File letters (X-axis): A-H
    private static readonly char[] FILES = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H' };

    // Rank letters (Z-axis): a-h (lowercase to distinguish from X)
    private static readonly char[] RANKS = { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h' };

    /// <summary>
    /// Convert a BoardPosition to chess notation (e.g., "B3c")
    /// </summary>
    public static string BoardPositionToNotation(BoardPosition position)
    {
        if (!position.IsValid())
        {
            Debug.LogWarning($"ChessNotationConverter: Invalid position {position}");
            return "???";
        }

        // Get board dimensions to validate ranges
        Vector3Int boardDims = GetBoardDimensions();

        if (position.x >= boardDims.x || position.y >= boardDims.y || position.z >= boardDims.z)
        {
            Debug.LogWarning($"ChessNotationConverter: Position {position} out of bounds for {boardDims.x}x{boardDims.y}x{boardDims.z} board");
            return "???";
        }

        char file = FILES[position.x];        // X-axis: A-H
        int level = position.y + 1;           // Y-axis: 1-8 (add 1 because chess starts at 1)
        char rank = RANKS[position.z];        // Z-axis: a-h

        return $"{file}{level}{rank}";
    }

    /// <summary>
    /// Convert chess notation to BoardPosition (e.g., "B3c" -> (1,2,2))
    /// </summary>
    public static BoardPosition NotationToBoardPosition(string notation)
    {
        if (string.IsNullOrEmpty(notation) || notation.Length < 3)
        {
            Debug.LogWarning($"ChessNotationConverter: Invalid notation '{notation}'");
            return new BoardPosition(-1, -1, -1);
        }

        try
        {
            char file = char.ToUpper(notation[0]);
            char level = notation[1];
            char rank = char.ToLower(notation[2]);

            // Convert file (A-H) to x
            int x = System.Array.IndexOf(FILES, file);
            if (x < 0)
            {
                Debug.LogWarning($"ChessNotationConverter: Invalid file '{file}' in notation '{notation}'");
                return new BoardPosition(-1, -1, -1);
            }

            // Convert level (1-8) to y
            int y = int.Parse(level.ToString()) - 1; // Subtract 1 because array starts at 0
            if (y < 0 || y >= 8)
            {
                Debug.LogWarning($"ChessNotationConverter: Invalid level '{level}' in notation '{notation}'");
                return new BoardPosition(-1, -1, -1);
            }

            // Convert rank (a-h) to z
            int z = System.Array.IndexOf(RANKS, rank);
            if (z < 0)
            {
                Debug.LogWarning($"ChessNotationConverter: Invalid rank '{rank}' in notation '{notation}'");
                return new BoardPosition(-1, -1, -1);
            }

            return new BoardPosition(x, y, z);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ChessNotationConverter: Error parsing notation '{notation}': {e.Message}");
            return new BoardPosition(-1, -1, -1);
        }
    }

    /// <summary>
    /// Get piece notation with color prefix (e.g., "WQ" for White Queen, "BN" for Black Knight)
    /// </summary>
    public static string GetPieceNotation(ChessPiece piece)
    {
        if (piece == null)
        {
            return "??";
        }

        // Get color prefix (first letter of color)
        string colorPrefix = GetColorPrefix(piece.pieceColor);

        // Get piece type abbreviation
        string pieceType = GetPieceTypeAbbreviation(piece.pieceType);

        return $"{colorPrefix}{pieceType}";
    }

    /// <summary>
    /// Get color prefix for piece notation
    /// </summary>
    public static string GetColorPrefix(PieceColor color)
    {
        return color switch
        {
            PieceColor.White => "W",
            PieceColor.Black => "B",
            PieceColor.Green => "G",
            PieceColor.Purple => "P",
            PieceColor.Yellow => "Y",
            PieceColor.Orange => "O",
            _ => "?"
        };
    }

    /// <summary>
    /// Get piece type abbreviation
    /// </summary>
    public static string GetPieceTypeAbbreviation(ChessPieceType pieceType)
    {
        return pieceType switch
        {
            ChessPieceType.King => "K",
            ChessPieceType.Queen => "Q",
            ChessPieceType.Rook => "R",
            ChessPieceType.Bishop => "B",
            ChessPieceType.Knight => "N", // N instead of K to avoid confusion with King
            ChessPieceType.Pawn => "P",
            _ => "?"
        };
    }

    /// <summary>
    /// Format a move in chess notation (e.g., "WQ B3c→B3d")
    /// </summary>
    public static string FormatMove(ChessPiece piece, BoardPosition from, BoardPosition to)
    {
        if (piece == null)
        {
            return "??? ???→???";
        }

        string pieceNotation = GetPieceNotation(piece);
        string fromNotation = BoardPositionToNotation(from);
        string toNotation = BoardPositionToNotation(to);

        return $"{pieceNotation} {fromNotation}→{toNotation}";
    }

    /// <summary>
    /// Format a move with capture notation (e.g., "WQ B3c×B3d")
    /// </summary>
    public static string FormatMoveWithCapture(ChessPiece piece, BoardPosition from, BoardPosition to, bool isCapture)
    {
        if (piece == null)
        {
            return "??? ???→???";
        }

        string pieceNotation = GetPieceNotation(piece);
        string fromNotation = BoardPositionToNotation(from);
        string toNotation = BoardPositionToNotation(to);
        string separator = isCapture ? "×" : "→";

        return $"{pieceNotation} {fromNotation}{separator}{toNotation}";
    }

    /// <summary>
    /// Format check status (e.g., "BK@A1a ⚠️ ← WQ@B3d")
    /// </summary>
    public static string FormatCheckStatus(ChessPiece threatenedKing, ChessPiece attacker)
    {
        if (threatenedKing == null || attacker == null)
        {
            return "Check status unknown";
        }

        string kingNotation = GetPieceNotation(threatenedKing);
        string kingPos = BoardPositionToNotation(threatenedKing.CurrentPosition);
        string attackerNotation = GetPieceNotation(attacker);
        string attackerPos = BoardPositionToNotation(attacker.CurrentPosition);

        return $"{kingNotation}@{kingPos} ⚠️ ← {attackerNotation}@{attackerPos}";
    }

    /// <summary>
    /// Format check status with multiple attackers (e.g., "BK@A1a ⚠️ ← WQ@B3d, WR@A4a")
    /// </summary>
    public static string FormatCheckStatusMultipleAttackers(ChessPiece threatenedKing, List<ChessPiece> attackers)
    {
        if (threatenedKing == null || attackers == null || attackers.Count == 0)
        {
            return "Check status unknown";
        }

        string kingNotation = GetPieceNotation(threatenedKing);
        string kingPos = BoardPositionToNotation(threatenedKing.CurrentPosition);

        // Build attacker list
        List<string> attackerStrings = new List<string>();
        foreach (ChessPiece attacker in attackers)
        {
            if (attacker != null)
            {
                string attackerNotation = GetPieceNotation(attacker);
                string attackerPos = BoardPositionToNotation(attacker.CurrentPosition);
                attackerStrings.Add($"{attackerNotation}@{attackerPos}");
            }
        }

        string attackerList = string.Join(", ", attackerStrings);
        return $"{kingNotation}@{kingPos} ⚠️ ← {attackerList}";
    }

    /// <summary>
    /// Get Unity Color for a piece color (for text color-coding)
    /// </summary>
    public static Color GetPieceDisplayColor(PieceColor color) => PlayerSymbols.Nudge(color, BaseDisplayColor(color));

    private static Color BaseDisplayColor(PieceColor color)
    {
        return color switch
        {
            PieceColor.White => new Color(1f, 1f, 1f, 1f),          // White
            PieceColor.Black => new Color(0.5f, 0.5f, 0.5f, 1f),    // Gray (not pure black for visibility)
            PieceColor.Green => new Color(0f, 1f, 0f, 1f),          // Green
            PieceColor.Purple => new Color(0.58f, 0.44f, 0.86f, 1f), // Medium purple
            PieceColor.Yellow => new Color(1f, 0.84f, 0f, 1f),      // Gold yellow
            PieceColor.Orange => new Color(1f, 0.55f, 0f, 1f),      // Dark orange
            _ => Color.white
        };
    }

    /// <summary>
    /// Get current board dimensions from BoardDimensionsManager
    /// </summary>
    private static Vector3Int GetBoardDimensions()
    {
        if (BoardDimensionsManager.Instance != null)
        {
            return BoardDimensionsManager.Instance.GetDimensions();
        }

        // Fallback to default 4x4x4
        return new Vector3Int(4, 4, 4);
    }

    /// <summary>
    /// Get file labels for current board size (e.g., ["A", "B", "C", "D"] for 4x4x4)
    /// </summary>
    public static string[] GetFileLabels()
    {
        Vector3Int dims = GetBoardDimensions();
        string[] labels = new string[dims.x];

        for (int i = 0; i < dims.x; i++)
        {
            labels[i] = FILES[i].ToString();
        }

        return labels;
    }

    /// <summary>
    /// Get level labels for current board size (e.g., ["1", "2", "3", "4"] for 4x4x4)
    /// </summary>
    public static string[] GetLevelLabels()
    {
        Vector3Int dims = GetBoardDimensions();
        string[] labels = new string[dims.y];

        for (int i = 0; i < dims.y; i++)
        {
            labels[i] = (i + 1).ToString();
        }

        return labels;
    }

    /// <summary>
    /// Get rank labels for current board size (e.g., ["a", "b", "c", "d"] for 4x4x4)
    /// </summary>
    public static string[] GetRankLabels()
    {
        Vector3Int dims = GetBoardDimensions();
        string[] labels = new string[dims.z];

        for (int i = 0; i < dims.z; i++)
        {
            labels[i] = RANKS[i].ToString();
        }

        return labels;
    }
}
