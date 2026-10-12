using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Core system for detecting check conditions in 3D chess.
/// Handles threat detection, king safety validation, and attack square calculation.
/// Integrates with existing ChessBoard and piece movement systems.
/// </summary>
public class CheckDetectionManager : MonoBehaviour
{
    [Header("Check Detection Settings")]
    public bool enableCheckValidation = true;
    public bool debugMode = false;
    
    [Header("Performance Settings")]
    public bool enableSimulationMode = false; // Disable expensive operations during AI evaluation
    
    [Header("Events")]
    public System.Action<PieceColor> OnKingInCheck;
    public System.Action<PieceColor> OnCheckResolved;
    public System.Action<PieceColor> OnCheckmate;
    public System.Action<PieceColor> OnStalemate;
    
    public static CheckDetectionManager Instance { get; private set; }
    
    // Cache for performance optimization
    private Dictionary<PieceColor, bool> _checkStateCache = new Dictionary<PieceColor, bool>();
    private bool _cacheValid = false;
    
    // UI state tracking - tracks what we last communicated to UI components
    private bool _lastWhiteCheckStateToUI = false;
    private bool _lastBlackCheckStateToUI = false;
    private bool _uiStateInitialized = false;
    
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("CheckDetectionManager: Instance created");
        }
        else
        {
            Debug.LogWarning("CheckDetectionManager: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Enable simulation mode for AI evaluation - reduces expensive operations
    /// </summary>
    public void EnableSimulationMode()
    {
        enableSimulationMode = true;
    }
    
    /// <summary>
    /// Disable simulation mode - restores full check detection
    /// </summary>
    public void DisableSimulationMode()
    {
        enableSimulationMode = false;
    }
    
    private void Start()
    {
        // Initialize cache
        _checkStateCache[PieceColor.White] = false;
        _checkStateCache[PieceColor.Black] = false;
        
        // Subscribe to game state changes
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
        }
    }
    
    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
    }
    
    private void OnGameStateChanged(GameState newState)
    {
        if (newState == GameState.Playing)
        {
            Debug.Log("CheckDetectionManager: Game started - enabling check detection");
            enableCheckValidation = true;
            InvalidateCache();
            ResetUIStateTracking();
        }
        else if (newState == GameState.GameOver)
        {
            Debug.Log("CheckDetectionManager: Game ended - disabling check detection");
            enableCheckValidation = false;
        }
    }
    
    /// <summary>
    /// Check if any king on the board is currently in check
    /// Used for determining if a move caused check
    /// </summary>
    /// <returns>True if any king is in check</returns>
    public bool IsAnyKingInCheck()
    {
        if (!enableCheckValidation)
        {
            return false;
        }

        // Get all active players
        List<PieceColor> activePlayers = new List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetActivePlayers();
        }
        else
        {
            // Fallback to 2-player mode
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
        }

        // Check if any player's king is in check
        foreach (PieceColor player in activePlayers)
        {
            if (IsKingInCheck(player))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a king of the specified color is currently in check
    /// </summary>
    /// <param name="kingColor">Color of the king to check</param>
    /// <returns>True if the king is in check</returns>
    public bool IsKingInCheck(PieceColor kingColor)
    {
        if (!enableCheckValidation)
        {
            return false;
        }
        
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("CheckDetectionManager.IsKingInCheck: ChessBoard.Instance is null");
            return false;
        }
        
        // Use cache if valid
        if (_cacheValid && _checkStateCache.ContainsKey(kingColor))
        {
            return _checkStateCache[kingColor];
        }
        
        // Find the king of the specified color
        BoardPosition kingPosition = FindKingPosition(kingColor);
        if (!kingPosition.IsValid())
        {
            Debug.LogWarning($"CheckDetectionManager.IsKingInCheck: No {kingColor} king found on board, returning false");
            return false;
        }

        // Check if any enemy piece is attacking the king's position
        // For multi-player games, check threats from ALL opponents
        bool inCheck = false;

        if (PlayerManager.Instance != null)
        {
            // Multi-player: Check each opponent
            List<PieceColor> opponents = PlayerManager.Instance.GetOpponents(kingColor);
            foreach (PieceColor opponentColor in opponents)
            {
                if (IsPositionUnderAttack(kingPosition, opponentColor))
                {
                    inCheck = true;
                    break;
                }
            }
        }
        else
        {
            // Fallback for 2-player games
            PieceColor enemyColor = (kingColor == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            inCheck = IsPositionUnderAttack(kingPosition, enemyColor);
        }
        
        // Update cache
        _checkStateCache[kingColor] = inCheck;
        _cacheValid = true;
        
        return inCheck;
    }
    
    /// <summary>
    /// Check if a position is under attack by pieces of the specified color
    /// </summary>
    /// <param name="position">Position to check</param>
    /// <param name="attackingColor">Color of attacking pieces</param>
    /// <returns>True if position is under attack</returns>
    public bool IsPositionUnderAttack(BoardPosition position, PieceColor attackingColor)
    {
        if (!enableCheckValidation || ChessBoard.Instance == null)
        {
            return false;
        }

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Check all pieces of the attacking color
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition piecePos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(piecePos);
                    
                    // PERFORMANCE: Skip expensive validation in simulation mode
                    if (piece != null && piece.pieceColor == attackingColor)
                    {
                        // Skip expensive corruption checks during AI simulation
                        bool isValid = enableSimulationMode || IsValidPieceForCheckDetection(piece, piecePos);
                        
                        if (isValid)
                        {
                            List<BoardPosition> attackSquares = GetPieceAttackSquares(piece);
                            
                            if (attackSquares.Contains(position))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }
    
    /// <summary>
    /// Get all squares attacked by pieces of the specified color
    /// </summary>
    /// <param name="attackingColor">Color of attacking pieces</param>
    /// <returns>List of all attacked positions</returns>
    public List<BoardPosition> GetThreatenedSquares(PieceColor attackingColor)
    {
        List<BoardPosition> threatenedSquares = new List<BoardPosition>();
        
        if (!enableCheckValidation || ChessBoard.Instance == null)
        {
            return threatenedSquares;
        }

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Check all pieces of the attacking color
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition piecePos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(piecePos);

                    // ANTI-CORRUPTION: Validate piece before using for threat detection
                    if (piece != null && piece.pieceColor == attackingColor && IsValidPieceForCheckDetection(piece, piecePos))
                    {
                        List<BoardPosition> attackSquares = GetPieceAttackSquares(piece);
                        foreach (BoardPosition square in attackSquares)
                        {
                            if (!threatenedSquares.Contains(square))
                            {
                                threatenedSquares.Add(square);
                            }
                        }
                    }
                }
            }
        }
        
        return threatenedSquares;
    }
    
    /// <summary>
    /// Get all pieces attacking a specific position
    /// </summary>
    /// <param name="position">Position being attacked</param>
    /// <returns>List of pieces attacking the position</returns>
    public List<ChessPiece> GetAttackingPieces(BoardPosition position)
    {
        List<ChessPiece> attackingPieces = new List<ChessPiece>();
        
        if (!enableCheckValidation || ChessBoard.Instance == null)
        {
            return attackingPieces;
        }
        
        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Find what color king is at this position to determine enemy color(s)
        ChessPiece kingAtPosition = ChessBoard.Instance.GetPieceAt(position);
        if (kingAtPosition == null || kingAtPosition.pieceType != ChessPieceType.King)
        {
            // If no king at position, check for attacks from all colors
            // This shouldn't happen in normal check detection but handles edge cases
            for (int x = 0; x < boardDimensions.x; x++)
            {
                for (int y = 0; y < boardDimensions.y; y++)
                {
                    for (int z = 0; z < boardDimensions.z; z++)
                    {
                        BoardPosition piecePos = new BoardPosition(x, y, z);
                        ChessPiece piece = ChessBoard.Instance.GetPieceAt(piecePos);

                        if (piece != null && IsValidPieceForCheckDetection(piece, piecePos))
                        {
                            List<BoardPosition> attackSquares = GetPieceAttackSquares(piece);
                            if (attackSquares.Contains(position))
                            {
                                attackingPieces.Add(piece);
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // King found at position - check all opponent pieces
            PieceColor kingColor = kingAtPosition.pieceColor;

            // Get all opponents of this king
            List<PieceColor> opponentColors = new List<PieceColor>();
            if (PlayerManager.Instance != null)
            {
                opponentColors = PlayerManager.Instance.GetOpponents(kingColor);
            }
            else
            {
                // Fallback for 2-player games
                PieceColor enemyColor = (kingColor == PieceColor.White) ? PieceColor.Black : PieceColor.White;
                opponentColors.Add(enemyColor);
            }

            for (int x = 0; x < boardDimensions.x; x++)
            {
                for (int y = 0; y < boardDimensions.y; y++)
                {
                    for (int z = 0; z < boardDimensions.z; z++)
                    {
                        BoardPosition piecePos = new BoardPosition(x, y, z);
                        ChessPiece piece = ChessBoard.Instance.GetPieceAt(piecePos);

                        // Check pieces of opponent colors only
                        // ANTI-CORRUPTION: Validate piece before using for check detection
                        if (piece != null && opponentColors.Contains(piece.pieceColor) && IsValidPieceForCheckDetection(piece, piecePos))
                        {
                            List<BoardPosition> attackSquares = GetPieceAttackSquares(piece);
                            if (attackSquares.Contains(position))
                            {
                                attackingPieces.Add(piece);
                            }
                        }
                    }
                }
            }
        }
        
        return attackingPieces;
    }

    /// <summary>
    /// Get all player colors that are attacking a specific king (for multi-player conquest system)
    /// Returns the colors of players whose pieces are attacking the king
    /// </summary>
    /// <param name="kingColor">Color of the king being attacked</param>
    /// <returns>List of player colors attacking the king</returns>
    public List<PieceColor> GetAttackingPlayers(PieceColor kingColor)
    {
        List<PieceColor> attackingPlayers = new List<PieceColor>();

        if (!enableCheckValidation || ChessBoard.Instance == null)
        {
            return attackingPlayers;
        }

        // Find the king position
        BoardPosition kingPosition = FindKingPosition(kingColor);
        if (!kingPosition.IsValid())
        {
            Debug.LogWarning($"CheckDetectionManager.GetAttackingPlayers: No {kingColor} king found");
            return attackingPlayers;
        }

        // Get all pieces attacking the king
        List<ChessPiece> attackingPieces = GetAttackingPieces(kingPosition);

        // Extract unique player colors from attacking pieces
        foreach (ChessPiece attacker in attackingPieces)
        {
            if (!attackingPlayers.Contains(attacker.pieceColor))
            {
                attackingPlayers.Add(attacker.pieceColor);
            }
        }

        Debug.Log($"🎯 CONQUEST: {kingColor} king at {kingPosition} is being attacked by {attackingPlayers.Count} player(s): {string.Join(", ", attackingPlayers)}");

        return attackingPlayers;
    }

    /// <summary>
    /// Get comprehensive threat information for a king in check
    /// </summary>
    /// <param name="kingColor">Color of the king to check for threats</param>
    /// <returns>CheckThreatInfo containing king position and attacking pieces</returns>
    public CheckThreatInfo GetCheckThreats(PieceColor kingColor)
    {
        CheckThreatInfo threatInfo = new CheckThreatInfo();
        
        if (!enableCheckValidation || ChessBoard.Instance == null)
        {
            return threatInfo;
        }
        
        // Find the king position
        BoardPosition kingPosition = FindKingPosition(kingColor);
        if (!kingPosition.IsValid())
        {
            Debug.LogWarning($"CheckDetectionManager.GetCheckThreats: No {kingColor} king found");
            return threatInfo;
        }
        
        threatInfo.kingPosition = kingPosition;
        threatInfo.kingColor = kingColor;
        
        // Get all pieces attacking the king
        threatInfo.attackingPieces = GetAttackingPieces(kingPosition);
        
        Debug.Log($"CheckDetectionManager.GetCheckThreats: {kingColor} king at {kingPosition} threatened by {threatInfo.attackingPieces.Count} pieces");
        
        foreach (ChessPiece attacker in threatInfo.attackingPieces)
        {
            Debug.Log($"  - {attacker.pieceColor} {attacker.pieceType} at {attacker.CurrentPosition}");
        }
        
        return threatInfo;
    }
    
    /// <summary>
    /// Check if a position is safe for a king of the specified color
    /// </summary>
    /// <param name="position">Position to check</param>
    /// <param name="kingColor">Color of the king</param>
    /// <returns>True if position is safe</returns>
    public bool IsPositionSafe(BoardPosition position, PieceColor kingColor)
    {
        if (!enableCheckValidation)
        {
            return true;
        }

        // For multi-player games, position is safe only if NOT under attack by ANY opponent
        if (PlayerManager.Instance != null)
        {
            List<PieceColor> opponents = PlayerManager.Instance.GetOpponents(kingColor);
            foreach (PieceColor opponentColor in opponents)
            {
                if (IsPositionUnderAttack(position, opponentColor))
                {
                    return false; // Under attack by at least one opponent
                }
            }
            return true; // Not under attack by any opponent
        }
        else
        {
            // Fallback for 2-player games
            PieceColor enemyColor = (kingColor == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            return !IsPositionUnderAttack(position, enemyColor);
        }
    }
    
    /// <summary>
    /// Check if a move would leave the moving player's king in check
    /// </summary>
    /// <param name="piece">Piece being moved</param>
    /// <param name="from">Starting position</param>
    /// <param name="to">Target position</param>
    /// <returns>True if move would leave king in check</returns>
    public bool WouldMoveLeaveKingInCheck(ChessPiece piece, BoardPosition from, BoardPosition to)
    {
        if (!enableCheckValidation || ChessBoard.Instance == null || piece == null)
        {
            return false;
        }
        
        // Temporarily disable cache during simulation to prevent interference, and keep the real
        // cached answers (the simulated position must not overwrite them)
        bool originalCacheState = _cacheValid;
        var savedCache = new Dictionary<PieceColor, bool>(_checkStateCache);
        _cacheValid = false;
        
        // CRITICAL FIX: Enable simulation mode to prevent corruption checks during move testing
        ChessBoard.Instance.EnableSimulationMode();
        
        // Store original piece positions for proper restoration
        ChessPiece capturedPiece = ChessBoard.Instance.GetPieceAt(to);
        BoardPosition originalPiecePosition = piece.CurrentPosition;
        
        // Temporarily execute the move
        ChessBoard.Instance.SetPieceAt(from, null);
        ChessBoard.Instance.SetPieceAt(to, piece);
        
        // Check if king is in check after the move (without using cache)
        bool kingInCheck = IsKingInCheck(piece.pieceColor);
        
        // Restore the board state properly
        ChessBoard.Instance.SetPieceAt(to, capturedPiece);
        ChessBoard.Instance.SetPieceAt(from, piece);
        
        // Ensure piece's internal position is restored
        piece.SetCurrentPosition(originalPiecePosition);
        
        // CRITICAL FIX: Disable simulation mode to restore normal corruption protection
        ChessBoard.Instance.DisableSimulationMode();
        
        // Restore original cache state
        _checkStateCache.Clear();
        foreach (var kv in savedCache) _checkStateCache[kv.Key] = kv.Value;
        _cacheValid = originalCacheState;
        
        return kingInCheck;
    }
    
    /// <summary>
    /// Get squares that a piece can attack (distinct from valid moves for some pieces like pawns)
    /// </summary>
    /// <param name="piece">Piece to get attack squares for</param>
    /// <returns>List of squares the piece can attack</returns>
    public List<BoardPosition> GetPieceAttackSquares(ChessPiece piece)
    {
        if (piece == null)
        {
            return new List<BoardPosition>();
        }
        
        // Use the piece's GetAttackSquares method which may differ from GetValidMoves for some pieces
        return piece.GetAttackSquares();
    }
    
    /// <summary>
    /// Validate that a piece is legitimate and not a duplicate/phantom
    /// Used to filter out corrupted pieces during check detection
    /// </summary>
    /// <param name="piece">Piece to validate</param>
    /// <param name="expectedPosition">Position where piece should be</param>
    /// <returns>True if piece is valid for check detection</returns>
    private bool IsValidPieceForCheckDetection(ChessPiece piece, BoardPosition expectedPosition)
    {
        if (piece == null)
        {
            return false;
        }
        
        // ANTI-CORRUPTION: Validate piece position synchronization
        BoardPosition pieceCurrentPosition = piece.CurrentPosition;
        
        // Check if piece's internal position matches board array position
        if (pieceCurrentPosition != expectedPosition)
        {
            Debug.LogWarning($"🚨 PHANTOM PIECE FILTERED: {piece.pieceColor} {piece.pieceType} at board[{expectedPosition}] thinks it's at {pieceCurrentPosition} - EXCLUDED from check detection");
            return false;
        }
        
        // Additional validation for Kings/Queens (most critical pieces)
        if (piece.pieceType == ChessPieceType.King || piece.pieceType == ChessPieceType.Queen)
        {
            // Get dynamic board dimensions from BoardDimensionsManager
            Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
                ? BoardDimensionsManager.Instance.GetDimensions()
                : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

            // Cross-reference: ensure this piece isn't registered at multiple positions
            int registrationCount = 0;
            for (int x = 0; x < boardDimensions.x; x++)
            {
                for (int y = 0; y < boardDimensions.y; y++)
                {
                    for (int z = 0; z < boardDimensions.z; z++)
                    {
                        if (ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z)) == piece)
                        {
                            registrationCount++;
                        }
                    }
                }
            }

            if (registrationCount > 1)
            {
                Debug.LogError($"🚨 DUPLICATE PIECE FILTERED: {piece.pieceColor} {piece.pieceType} registered at {registrationCount} positions - EXCLUDED from check detection");
                return false;
            }
        }
        
        // Piece passed all validation checks
        return true;
    }
    
    /// <summary>
    /// Find the position of a king of the specified color
    /// </summary>
    /// <param name="kingColor">Color of king to find</param>
    /// <returns>King's position, or invalid position if not found</returns>
    private BoardPosition FindKingPosition(PieceColor kingColor)
    {
        if (ChessBoard.Instance == null)
        {
            Debug.LogError($"CheckDetectionManager.FindKingPosition: ChessBoard.Instance is NULL");
            return new BoardPosition(-1, -1, -1);
        }

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);

                    if (piece != null)
                    {
                        if (piece.pieceType == ChessPieceType.King && piece.pieceColor == kingColor)
                        {
                            return pos;
                        }
                    }
                }
            }
        }

        Debug.LogError($"CheckDetectionManager.FindKingPosition: Could not find {kingColor} king on the board!");
        return new BoardPosition(-1, -1, -1); // Invalid position
    }
    
    /// <summary>
    /// Invalidate the check state cache
    /// </summary>
    public void InvalidateCache()
    {
        _cacheValid = false;
    }
    
    /// <summary>
    /// Manually trigger check state evaluation and fire events
    /// DISABLED: This method is temporarily disabled to prevent interference with king movement
    /// </summary>
    public void EvaluateCheckState()
    {
        // DISABLED to prevent interference with king movement logic
        // Core check detection (IsKingInCheck, WouldMoveLeaveKingInCheck) still works
        // UI updates will be addressed separately after confirming king movement works
        return;
    }
    
    /// <summary>
    /// Safe method to update visual feedback without interfering with king movement validation
    /// Only fires events when actual state changes occur
    /// </summary>
    public void SafeUpdateVisualFeedback()
    {
        if (!enableCheckValidation)
        {
            Debug.Log("SafeUpdateVisualFeedback: Check validation disabled, exiting");
            return;
        }
        
        Debug.Log("SafeUpdateVisualFeedback: ENTRY");
        
        // Force fresh cache invalidation to ensure we get current state
        InvalidateCache();
        
        // AGGRESSIVE DEBUGGING: Force complete cache reset and validation
        _cacheValid = false;
        _checkStateCache.Clear();
        Debug.Log("SafeUpdateVisualFeedback: Aggressively cleared cache to force fresh calculation");
        
        // Get current check states with fresh data
        bool currentWhiteInCheck = IsKingInCheck(PieceColor.White);
        bool currentBlackInCheck = IsKingInCheck(PieceColor.Black);
        
        // Compare with previously communicated UI states
        bool previousWhiteInCheck = _lastWhiteCheckStateToUI;
        bool previousBlackInCheck = _lastBlackCheckStateToUI;
        
        Debug.Log($"SafeUpdateVisualFeedback: Current states - White: {currentWhiteInCheck}, Black: {currentBlackInCheck}");
        Debug.Log($"SafeUpdateVisualFeedback: Previous UI states - White: {previousWhiteInCheck}, Black: {previousBlackInCheck}");
        Debug.Log($"SafeUpdateVisualFeedback: UI initialized: {_uiStateInitialized}");
        Debug.Log($"SafeUpdateVisualFeedback: Cache valid: {_cacheValid}");
        
        // Enhanced debugging: validate king positions and check logic
        BoardPosition whiteKingPos = FindKingPosition(PieceColor.White);
        BoardPosition blackKingPos = FindKingPosition(PieceColor.Black);
        Debug.Log($"SafeUpdateVisualFeedback: White king at {whiteKingPos}, Black king at {blackKingPos}");
        
        // Force recalculation with debugging for problematic states
        if (currentWhiteInCheck)
        {
            Debug.Log($"🔍 SafeUpdateVisualFeedback: WHITE KING DETECTED IN CHECK - validating...");
            Debug.Log($"  White king position: {whiteKingPos}");
            if (whiteKingPos.IsValid())
            {
                bool isUnderAttack = IsPositionUnderAttack(whiteKingPos, PieceColor.Black);
                Debug.Log($"  Position {whiteKingPos} under attack by Black pieces: {isUnderAttack}");
                if (isUnderAttack)
                {
                    var attackingPieces = GetAttackingPieces(whiteKingPos);
                    Debug.Log($"  Attacking pieces: {string.Join(", ", attackingPieces.Select(p => $"{p.pieceColor} {p.pieceType} at {p.CurrentPosition}"))}");
                }
            }
        }
        
        if (currentBlackInCheck)
        {
            Debug.Log($"🔍 SafeUpdateVisualFeedback: BLACK KING DETECTED IN CHECK - validating...");
            Debug.Log($"  Black king position: {blackKingPos}");
            if (blackKingPos.IsValid())
            {
                bool isUnderAttack = IsPositionUnderAttack(blackKingPos, PieceColor.White);
                Debug.Log($"  Position {blackKingPos} under attack by White pieces: {isUnderAttack}");
                if (isUnderAttack)
                {
                    var attackingPieces = GetAttackingPieces(blackKingPos);
                    Debug.Log($"  Attacking pieces: {string.Join(", ", attackingPieces.Select(p => $"{p.pieceColor} {p.pieceType} at {p.CurrentPosition}"))}");
                }
            }
        }
        
        // Fire events only when state actually changes
        // FIXED: Handle initial check scenarios more robustly
        if (currentWhiteInCheck && (!_uiStateInitialized || !previousWhiteInCheck))
        {
            Debug.Log("SafeUpdateVisualFeedback: Firing OnKingInCheck for WHITE");
            OnKingInCheck?.Invoke(PieceColor.White);
            _lastWhiteCheckStateToUI = true;
        }
        else if (!currentWhiteInCheck && previousWhiteInCheck)
        {
            // FIXED: Simplified logic - fire OnCheckResolved when check is actually resolved
            Debug.Log("SafeUpdateVisualFeedback: Firing OnCheckResolved for WHITE");
            OnCheckResolved?.Invoke(PieceColor.White);
            _lastWhiteCheckStateToUI = false;
        }
        else
        {
            Debug.Log($"SafeUpdateVisualFeedback: No White event fired - current:{currentWhiteInCheck}, previous:{previousWhiteInCheck}, initialized:{_uiStateInitialized}");
        }
        
        if (currentBlackInCheck && (!_uiStateInitialized || !previousBlackInCheck))
        {
            Debug.Log("SafeUpdateVisualFeedback: Firing OnKingInCheck for BLACK");
            OnKingInCheck?.Invoke(PieceColor.Black);
            _lastBlackCheckStateToUI = true;
        }
        else if (!currentBlackInCheck && previousBlackInCheck)
        {
            // FIXED: Simplified logic - fire OnCheckResolved when check is actually resolved
            Debug.Log("SafeUpdateVisualFeedback: Firing OnCheckResolved for BLACK");
            OnCheckResolved?.Invoke(PieceColor.Black);
            _lastBlackCheckStateToUI = false;
        }
        else
        {
            Debug.Log($"SafeUpdateVisualFeedback: No Black event fired - current:{currentBlackInCheck}, previous:{previousBlackInCheck}, initialized:{_uiStateInitialized}");
        }
        
        // Update tracking for states that didn't change but need initialization
        if (!currentWhiteInCheck && (!_uiStateInitialized || !previousWhiteInCheck))
        {
            _lastWhiteCheckStateToUI = false;
        }
        if (!currentBlackInCheck && (!_uiStateInitialized || !previousBlackInCheck))
        {
            _lastBlackCheckStateToUI = false;
        }
        
        _uiStateInitialized = true;
        
        // Fallback: Force visual state refresh if there are any mismatches
        if (CheckVisualFeedbackManager.Instance != null)
        {
            bool whiteVisualMatch = (currentWhiteInCheck == CheckVisualFeedbackManager.Instance.WhiteKingInCheck);
            bool blackVisualMatch = (currentBlackInCheck == CheckVisualFeedbackManager.Instance.BlackKingInCheck);
            
            Debug.Log($"SafeUpdateVisualFeedback: Visual state comparison - White: game({currentWhiteInCheck}) vs visual({CheckVisualFeedbackManager.Instance.WhiteKingInCheck}), Black: game({currentBlackInCheck}) vs visual({CheckVisualFeedbackManager.Instance.BlackKingInCheck})");
            
            if (!whiteVisualMatch || !blackVisualMatch)
            {
                Debug.LogWarning($"SafeUpdateVisualFeedback: Visual state mismatch detected! Forcing refresh. White match: {whiteVisualMatch}, Black match: {blackVisualMatch}");
                ForceVisualStateRefresh();
                
                // Also trigger RefreshCheckIndicators as additional fallback
                CheckVisualFeedbackManager.Instance.RefreshCheckIndicators();
            }
            else
            {
                Debug.Log("SafeUpdateVisualFeedback: Visual states match current game state");
            }
        }
        
        Debug.Log("SafeUpdateVisualFeedback: EXIT");
    }
    
    /// <summary>
    /// Get debug information about current check state
    /// </summary>
    /// <returns>Debug string with check state information</returns>
    public string GetDebugInfo()
    {
        if (!enableCheckValidation)
        {
            return "Check validation disabled";
        }
        
        bool whiteInCheck = IsKingInCheck(PieceColor.White);
        bool blackInCheck = IsKingInCheck(PieceColor.Black);
        
        string info = $"Check State: White={whiteInCheck}, Black={blackInCheck}";
        
        if (whiteInCheck)
        {
            BoardPosition whiteKingPos = FindKingPosition(PieceColor.White);
            List<ChessPiece> attackers = GetAttackingPieces(whiteKingPos);
            info += $"\nWhite king at {whiteKingPos} attacked by {attackers.Count} pieces";
        }
        
        if (blackInCheck)
        {
            BoardPosition blackKingPos = FindKingPosition(PieceColor.Black);
            List<ChessPiece> attackers = GetAttackingPieces(blackKingPos);
            info += $"\nBlack king at {blackKingPos} attacked by {attackers.Count} pieces";
        }
        
        return info;
    }
    
    /// <summary>
    /// Reset UI state tracking - call when starting a new game
    /// </summary>
    public void ResetUIStateTracking()
    {
        _lastWhiteCheckStateToUI = false;
        _lastBlackCheckStateToUI = false;
        _uiStateInitialized = false;
        Debug.Log("CheckDetectionManager: UI state tracking reset");
    }
    
    /// <summary>
    /// Initialize UI state tracking with known initial check states
    /// Called after post-placement check evaluation to ensure proper state tracking
    /// </summary>
    public void InitializeUIStateWithInitialChecks(bool whiteInCheck, bool blackInCheck)
    {
        Debug.Log($"CheckDetectionManager.InitializeUIStateWithInitialChecks: Setting initial UI states - White: {whiteInCheck}, Black: {blackInCheck}");
        
        _lastWhiteCheckStateToUI = whiteInCheck;
        _lastBlackCheckStateToUI = blackInCheck;
        _uiStateInitialized = true;
        
        // Fire initial check events if either player starts in check
        if (whiteInCheck)
        {
            OnKingInCheck?.Invoke(PieceColor.White);
        }
        
        if (blackInCheck)
        {
            OnKingInCheck?.Invoke(PieceColor.Black);
        }
    }
    
    /// <summary>
    /// Force visual indicator refresh when state tracking fails
    /// This is a failsafe method to ensure visual indicators match actual game state
    /// ENHANCED: Now fires proper events to ensure all UI components receive state changes
    /// </summary>
    public void ForceVisualStateRefresh()
    {
        Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: ENTRY - forcing visual state synchronization");
        
        if (!enableCheckValidation)
        {
            Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Check validation disabled, exiting");
            return;
        }
        
        // Get actual check states
        InvalidateCache();
        bool actualWhiteInCheck = IsKingInCheck(PieceColor.White);
        
        InvalidateCache();
        bool actualBlackInCheck = IsKingInCheck(PieceColor.Black);
        
        Debug.Log($"🔧 CheckDetectionManager.ForceVisualStateRefresh: Actual states - White: {actualWhiteInCheck}, Black: {actualBlackInCheck}");
        Debug.Log($"🔧 CheckDetectionManager.ForceVisualStateRefresh: Tracked UI states - White: {_lastWhiteCheckStateToUI}, Black: {_lastBlackCheckStateToUI}");
        
        // Store previous states before updating
        bool previousWhiteState = _lastWhiteCheckStateToUI;
        bool previousBlackState = _lastBlackCheckStateToUI;
        
        // Force visual feedback to match actual state
        if (CheckVisualFeedbackManager.Instance != null)
        {
            // Clear all visual indicators first to ensure clean state
            CheckVisualFeedbackManager.Instance.ClearAllCheckIndicators();
            
            // Apply correct visual indicators based on actual state
            if (actualWhiteInCheck)
            {
                Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Forcing White check indicators");
                CheckVisualFeedbackManager.Instance.ShowCheckIndicator(PieceColor.White);
                if (ThreatIndicatorManager.Instance != null)
                {
                    ThreatIndicatorManager.Instance.ShowThreatIndicators(PieceColor.White);
                }
            }
            
            if (actualBlackInCheck)
            {
                Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Forcing Black check indicators");
                CheckVisualFeedbackManager.Instance.ShowCheckIndicator(PieceColor.Black);
                if (ThreatIndicatorManager.Instance != null)
                {
                    ThreatIndicatorManager.Instance.ShowThreatIndicators(PieceColor.Black);
                }
            }
            
            // Update UI status
            if (CheckStatusUI.Instance != null)
            {
                if (actualWhiteInCheck)
                {
                    CheckStatusUI.Instance.ShowCheckMessage(PieceColor.White);
                }
                else if (actualBlackInCheck)
                {
                    CheckStatusUI.Instance.ShowCheckMessage(PieceColor.Black);
                }
                else
                {
                    CheckStatusUI.Instance.HideCheckMessage();
                }
            }
            
            // ENHANCED: Force persistent status UI synchronization as fallback
            if (PersistentCheckStatusUI.Instance != null)
            {
                Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Triggering PersistentCheckStatusUI.ForceUpdate()");
                PersistentCheckStatusUI.Instance.ForceUpdate();
            }
        }
        
        // ENHANCED: Fire proper events to ensure all event subscribers get state changes
        if (actualWhiteInCheck && !previousWhiteState)
        {
            Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Firing OnKingInCheck for WHITE");
            OnKingInCheck?.Invoke(PieceColor.White);
        }
        else if (!actualWhiteInCheck && previousWhiteState)
        {
            Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Firing OnCheckResolved for WHITE");
            OnCheckResolved?.Invoke(PieceColor.White);
        }
        
        if (actualBlackInCheck && !previousBlackState)
        {
            Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Firing OnKingInCheck for BLACK");
            OnKingInCheck?.Invoke(PieceColor.Black);
        }
        else if (!actualBlackInCheck && previousBlackState)
        {
            Debug.Log("🔧 CheckDetectionManager.ForceVisualStateRefresh: Firing OnCheckResolved for BLACK");
            OnCheckResolved?.Invoke(PieceColor.Black);
        }
        
        // Update tracked UI states to match actual state
        _lastWhiteCheckStateToUI = actualWhiteInCheck;
        _lastBlackCheckStateToUI = actualBlackInCheck;
        _uiStateInitialized = true;
        
        Debug.Log($"🔧 CheckDetectionManager.ForceVisualStateRefresh: Refresh complete - UI states synchronized");
    }
}

/// <summary>
/// Information about threats to a king in check
/// </summary>
[System.Serializable]
public class CheckThreatInfo
{
    public PieceColor kingColor;
    public BoardPosition kingPosition;
    public List<ChessPiece> attackingPieces = new List<ChessPiece>();
    
    public bool HasThreats => attackingPieces.Count > 0;
    public bool IsDoubleCheck => attackingPieces.Count > 1;
    
    public override string ToString()
    {
        return $"{kingColor} king at {kingPosition} threatened by {attackingPieces.Count} pieces";
    }
}