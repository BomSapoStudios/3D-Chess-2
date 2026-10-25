using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Game end condition types for different scenarios
/// </summary>
public enum GameEndCondition
{
    None,                    // Game continues
    Checkmate,              // King in check with no moves (2-player)
    Stalemate,              // No moves but king not in check (2-player)
    DrawByAgreement,        // Players agreed to draw
    DrawByRepetition,       // Same position 3 times
    Timeout,                // Player ran out of time
    Forfeit,                // Player forfeited
    LastPlayerStanding,     // Only one player remains (multi-player)
    ConquestComplete        // All but one player eliminated (multi-player)
}

/// <summary>
/// Result of game condition evaluation
/// </summary>
public struct GameConditionResult
{
    public bool isGameOver;
    public GameEndCondition condition;
    public PieceColor winner;          // Winner (if any)
    public PieceColor loser;           // Loser (if specific player)
    public List<PieceColor> activePlayers;
    public string description;
    
    public static GameConditionResult GameContinues(List<PieceColor> activePlayers)
    {
        return new GameConditionResult
        {
            isGameOver = false,
            condition = GameEndCondition.None,
            activePlayers = activePlayers,
            description = "Game continues"
        };
    }
    
    public static GameConditionResult GameOver(GameEndCondition condition, string description, 
        PieceColor winner = PieceColor.White, PieceColor loser = PieceColor.White)
    {
        return new GameConditionResult
        {
            isGameOver = true,
            condition = condition,
            winner = winner,
            loser = loser,
            description = description
        };
    }
}

/// <summary>
/// AUTHORITATIVE GAME CONDITION VALIDATOR
/// Single source of truth for determining legitimate game end conditions.
/// This class determines if the game is truly over based on chess rules,
/// not based on corrupted board state or false detection.
/// </summary>
public class GameConditionValidator : MonoBehaviour
{
    [Header("Validation Settings")]
    public bool enableDebugLogging = true;
    
    public static GameConditionValidator Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("GameConditionValidator: Instance created");
        }
        else
        {
            Debug.LogWarning("GameConditionValidator: Duplicate instance detected, destroying");
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// AUTHORITATIVE: Evaluate current game state and determine if game should end
    /// This is the single source of truth for legitimate game end conditions
    /// </summary>
    public GameConditionResult EvaluateGameState()
    {
        if (enableDebugLogging)
        {
            Debug.Log("🏁 GameConditionValidator: Evaluating game state for legitimate end conditions");
        }
        
        try
        {
            // Get current active players
            List<PieceColor> activePlayers = GetActivePlayers();
            
            if (activePlayers == null || activePlayers.Count == 0)
            {
                Debug.LogError("🚨 GameConditionValidator: No active players found - invalid game state");
                return GameConditionResult.GameContinues(new List<PieceColor>());
            }
            
            // Check for multi-player victory condition first
            if (activePlayers.Count == 1)
            {
                PieceColor winner = activePlayers[0];
                string description = $"Victory! {winner} is the last player standing";
                
                if (enableDebugLogging)
                {
                    Debug.Log($"🏆 GameConditionValidator: LEGITIMATE GAME END - {description}");
                }
                
                return GameConditionResult.GameOver(GameEndCondition.LastPlayerStanding, description, winner);
            }
            
            // For 2+ active players, check current player's conditions
            if (TurnManager.Instance == null)
            {
                Debug.LogWarning("🚨 GameConditionValidator: TurnManager not available");
                return GameConditionResult.GameContinues(activePlayers);
            }
            
            PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
            
            // Check if current player is actually active
            if (!activePlayers.Contains(currentPlayer))
            {
                Debug.LogWarning($"🚨 GameConditionValidator: Current player {currentPlayer} not in active players list - turn management issue");
                return GameConditionResult.GameContinues(activePlayers);
            }
            
            // Check for traditional chess end conditions (checkmate/stalemate)
            bool hasLegalMoves = PlayerHasLegalMoves(currentPlayer);
            bool kingInCheck = IsKingInCheck(currentPlayer);
            
            if (!hasLegalMoves)
            {
                if (kingInCheck)
                {
                    // Checkmate
                    if (activePlayers.Count == 2)
                    {
                        // 2-player checkmate - game ends
                        PieceColor winner = activePlayers.Find(p => p != currentPlayer);
                        string description = $"Checkmate! {winner} wins - {currentPlayer} is checkmated";
                        
                        if (enableDebugLogging)
                        {
                            Debug.Log($"🏆 GameConditionValidator: LEGITIMATE CHECKMATE - {description}");
                        }
                        
                        return GameConditionResult.GameOver(GameEndCondition.Checkmate, description, winner, currentPlayer);
                    }
                    else
                    {
                        // Multi-player checkmate - player eliminated, game continues
                        if (enableDebugLogging)
                        {
                            Debug.Log($"🏰 GameConditionValidator: {currentPlayer} checkmated in multi-player game - player eliminated, game continues");
                        }
                        
                        // This should trigger player elimination, not game end
                        // Return "game continues" because elimination is handled separately
                        return GameConditionResult.GameContinues(activePlayers);
                    }
                }
                else
                {
                    // Stalemate
                    string description = $"Stalemate! Game ends in a draw - {currentPlayer} has no legal moves but is not in check";
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log($"🏁 GameConditionValidator: LEGITIMATE STALEMATE - {description}");
                    }
                    
                    return GameConditionResult.GameOver(GameEndCondition.Stalemate, description);
                }
            }
            
            // Game continues - no legitimate end condition met
            if (enableDebugLogging)
            {
                Debug.Log($"✅ GameConditionValidator: Game continues - {activePlayers.Count} active players, {currentPlayer} has moves");
            }
            
            return GameConditionResult.GameContinues(activePlayers);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🚨 GameConditionValidator: Exception during evaluation: {ex.Message}");
            return GameConditionResult.GameContinues(new List<PieceColor>());
        }
    }
    
    /// <summary>
    /// AUTHORITATIVE: Evaluate if draw by repetition is legitimate
    /// </summary>
    public GameConditionResult EvaluateDrawByRepetition()
    {
        if (enableDebugLogging)
        {
            Debug.Log("🔄 GameConditionValidator: Evaluating draw by repetition");
        }
        
        try
        {
            // Get current active players to ensure game is still valid
            List<PieceColor> activePlayers = GetActivePlayers();
            
            if (activePlayers == null || activePlayers.Count < 2)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"🚨 GameConditionValidator: Draw by repetition invalid - insufficient active players ({activePlayers?.Count ?? 0})");
                }
                return GameConditionResult.GameContinues(activePlayers ?? new List<PieceColor>());
            }
            
            // Validate that TurnManager has proper repetition detection
            if (TurnManager.Instance == null)
            {
                Debug.LogWarning("🚨 GameConditionValidator: Cannot validate repetition - TurnManager not available");
                return GameConditionResult.GameContinues(activePlayers);
            }
            
            // Additional validation could be added here to verify the repetition is legitimate
            // For now, trust TurnManager's repetition detection since it already does secondary validation
            
            string description = "Draw by repetition! The same position has occurred three times";
            
            if (enableDebugLogging)
            {
                Debug.Log($"🏁 GameConditionValidator: LEGITIMATE DRAW BY REPETITION - {description}");
            }
            
            return GameConditionResult.GameOver(GameEndCondition.DrawByRepetition, description);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"🚨 GameConditionValidator: Exception during repetition evaluation: {ex.Message}");
            return GameConditionResult.GameContinues(new List<PieceColor>());
        }
    }
    
    /// <summary>
    /// Check if game is legitimately over (authoritative check)
    /// </summary>
    public bool IsGameLegitimatelyOver()
    {
        GameConditionResult result = EvaluateGameState();
        return result.isGameOver;
    }
    
    /// <summary>
    /// Get list of active players (not eliminated)
    /// </summary>
    public List<PieceColor> GetActivePlayers()
    {
        // SIMPLIFIED: For 2-player chess, active players are always White and Black
        // unless one of their kings has been captured
        List<PieceColor> activePlayers = new List<PieceColor>();
        
        if (ChessBoard.Instance == null)
        {
            Debug.LogWarning("GameConditionValidator: Cannot determine active players - ChessBoard not available");
            return activePlayers;
        }
        
        HashSet<PieceColor> playersWithKings = new HashSet<PieceColor>();
        Vector3Int dimensions = GetBoardDimensions();
        
        for (int x = 0; x < dimensions.x; x++)
        {
            for (int y = 0; y < dimensions.y; y++)
            {
                for (int z = 0; z < dimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);
                    
                    if (piece != null && piece.pieceType == ChessPieceType.King)
                    {
                        playersWithKings.Add(piece.pieceColor);
                    }
                }
            }
        }
        
        activePlayers.AddRange(playersWithKings);
        return activePlayers;
    }
    
    /// <summary>
    /// Check if a specific player has legal moves (corruption-resistant)
    /// </summary>
    private bool PlayerHasLegalMoves(PieceColor player)
    {
        // SIMPLIFIED: Use direct board scan for 2-player chess
        return PlayerHasLegalMovesLegacy(player);
    }

    /// <summary>
    /// Check if a specific player has legal moves (ledger-based - DISABLED)
    /// </summary>
    private bool PlayerHasLegalMovesWithLedger(PieceColor player)
    {
        // DISABLED: PieceLedger not available in 2-player version
        // This method is kept for reference but not used
        try
        {
            // Get all pieces of the specified color from the ledger
            List<ChessPiece> playerPieces = null; // PieceLedger.Instance.GetPiecesOfColor(player);
            
            if (playerPieces == null || playerPieces.Count == 0)
            {
                Debug.LogWarning($"GameConditionValidator: No {player} pieces found in ledger - possible corruption, assuming player has moves");
                return true; // Don't trigger game end due to corruption
            }
            
            int totalPieces = playerPieces.Count;
            int totalLegalMoves = 0;
            
            if (enableDebugLogging)
            {
                Debug.Log($"🗃️ GameConditionValidator: Checking {totalPieces} {player} pieces from ledger for legal moves");
            }
            
            foreach (ChessPiece piece in playerPieces)
            {
                if (piece == null) continue;
                
                try
                {
                    // POSITION SYNC: Ensure piece position is synchronized before checking legal moves
                    if (!piece.ValidatePosition())
                    {
                        if (enableDebugLogging)
                        {
                            Debug.LogWarning($"🔧 GameConditionValidator: Position sync issue detected for {player} {piece.pieceType} - attempting repair");
                        }
                        piece.RepairPosition();
                    }
                    
                    List<BoardPosition> legalMoves = piece.GetLegalMoves();
                    totalLegalMoves += legalMoves.Count;
                    
                    if (enableDebugLogging)
                    {
                        Debug.Log($"🗃️ GameConditionValidator: {player} {piece.pieceType} at {piece.CurrentPosition} has {legalMoves.Count} legal moves");
                    }
                    
                    // Early exit if we find any legal move
                    if (legalMoves.Count > 0)
                    {
                        if (enableDebugLogging)
                        {
                            Debug.Log($"✅ GameConditionValidator: {player} has legal moves - {piece.pieceType} at {piece.CurrentPosition} has {legalMoves.Count} moves");
                        }
                        return true;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"GameConditionValidator: Error checking moves for {player} {piece.pieceType} at {piece.CurrentPosition}: {ex.Message}");
                }
            }
            
            if (enableDebugLogging)
            {
                Debug.LogWarning($"🚨 GameConditionValidator: {player} has {totalPieces} pieces but {totalLegalMoves} legal moves total (ledger-based check)");
            }
            
            return totalLegalMoves > 0;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"GameConditionValidator: Exception checking legal moves for {player} using ledger: {ex.Message}");
            Debug.LogError($"Falling back to legacy board scan method");
            return PlayerHasLegalMovesLegacy(player);
        }
    }
    
    /// <summary>
    /// Legacy method for checking legal moves by scanning the board (fallback)
    /// </summary>
    private bool PlayerHasLegalMovesLegacy(PieceColor player)
    {
        if (ChessBoard.Instance == null)
        {
            Debug.LogWarning($"GameConditionValidator: Cannot check legal moves for {player} - ChessBoard not available");
            return true; // Assume player has moves if we can't check
        }
        
        try
        {
            Vector3Int dimensions = GetBoardDimensions();
            int totalPieces = 0;
            int totalLegalMoves = 0;
            
            for (int x = 0; x < dimensions.x; x++)
            {
                for (int y = 0; y < dimensions.y; y++)
                {
                    for (int z = 0; z < dimensions.z; z++)
                    {
                        BoardPosition pos = new BoardPosition(x, y, z);
                        ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);
                        
                        if (piece != null && piece.pieceColor == player)
                        {
                            totalPieces++;
                            
                            try
                            {
                                List<BoardPosition> legalMoves = piece.GetLegalMoves();
                                totalLegalMoves += legalMoves.Count;
                                
                                // Early exit if we find any legal move
                                if (legalMoves.Count > 0)
                                {
                                    if (enableDebugLogging)
                                    {
                                        Debug.Log($"✅ GameConditionValidator: {player} has legal moves - {piece.pieceType} at {pos} has {legalMoves.Count} moves (legacy check)");
                                    }
                                    return true;
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"GameConditionValidator: Error checking moves for {player} {piece.pieceType} at {pos}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            
            if (totalPieces == 0)
            {
                Debug.LogWarning($"GameConditionValidator: No {player} pieces found on board - possible corruption, assuming player has moves");
                return true; // Don't trigger game end due to corruption
            }
            
            if (enableDebugLogging)
            {
                Debug.LogWarning($"GameConditionValidator: {player} has {totalPieces} pieces but {totalLegalMoves} legal moves total (legacy check)");
            }
            
            return totalLegalMoves > 0;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"GameConditionValidator: Exception checking legal moves for {player}: {ex.Message}");
            return true; // Assume player has moves if we can't check due to error
        }
    }
    
    /// <summary>
    /// Check if a player's king is in check (corruption-resistant)
    /// </summary>
    private bool IsKingInCheck(PieceColor player)
    {
        if (CheckDetectionManager.Instance == null)
        {
            Debug.LogWarning($"GameConditionValidator: Cannot check if {player} king in check - CheckDetectionManager not available");
            return false; // Assume not in check if we can't verify
        }
        
        try
        {
            return CheckDetectionManager.Instance.IsKingInCheck(player);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"GameConditionValidator: Exception checking if {player} king in check: {ex.Message}");
            return false; // Assume not in check if error occurs
        }
    }
    
    /// <summary>
    /// Get board dimensions (with fallback)
    /// </summary>
    private Vector3Int GetBoardDimensions()
    {
        return BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(5, 5, 5);
    }
    
    /// <summary>
    /// Validate that current game state is consistent with actual conditions
    /// </summary>
    public bool ValidateGameStateConsistency()
    {
        if (GameStateManager.Instance == null)
        {
            Debug.LogWarning("GameConditionValidator: Cannot validate consistency - GameStateManager not available");
            return true; // Assume consistent if we can't check
        }
        
        GameState currentState = GameStateManager.Instance.currentState;
        GameConditionResult actualCondition = EvaluateGameState();
        
        bool shouldBeGameOver = actualCondition.isGameOver;
        bool isCurrentlyGameOver = (currentState == GameState.GameOver);
        
        if (shouldBeGameOver != isCurrentlyGameOver)
        {
            Debug.LogError($"🚨 GAME STATE INCONSISTENCY DETECTED:");
            Debug.LogError($"  Current state: {currentState}");
            Debug.LogError($"  Should be game over: {shouldBeGameOver}");
            Debug.LogError($"  Actual condition: {actualCondition.condition}");
            Debug.LogError($"  Description: {actualCondition.description}");
            
            return false;
        }
        
        if (enableDebugLogging)
        {
            Debug.Log($"✅ GameConditionValidator: Game state consistency validated - State: {currentState}, Condition: {actualCondition.condition}");
        }
        
        return true;
    }
    
    /// <summary>
    /// Repair inconsistent game state to match actual conditions
    /// </summary>
    public bool RepairGameStateInconsistency()
    {
        if (GameStateManager.Instance == null)
        {
            Debug.LogError("GameConditionValidator: Cannot repair state - GameStateManager not available");
            return false;
        }
        
        GameConditionResult actualCondition = EvaluateGameState();
        GameState currentState = GameStateManager.Instance.currentState;
        
        if (actualCondition.isGameOver && currentState != GameState.GameOver)
        {
            Debug.LogError($"🔧 REPAIRING: Game should be over but state is {currentState} - transitioning to GameOver");
            Debug.LogError($"  Condition: {actualCondition.condition}");
            Debug.LogError($"  Description: {actualCondition.description}");
            
            GameStateManager.Instance.ChangeState(GameState.GameOver);
            return true;
        }
        else if (!actualCondition.isGameOver && currentState == GameState.GameOver)
        {
            Debug.LogError($"🔧 REPAIRING: Game is marked GameOver but should continue - transitioning to Playing");
            Debug.LogError($"  Active players: {actualCondition.activePlayers.Count}");
            Debug.LogError($"  Players: {string.Join(", ", actualCondition.activePlayers)}");
            
            GameStateManager.Instance.ChangeState(GameState.Playing);
            return true;
        }
        
        // State is consistent
        return false;
    }
}