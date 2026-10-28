using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages checkmate and stalemate detection for 3D chess.
/// Determines when a player has no legal moves and whether the game should end.
/// Integrates with CheckDetectionManager and GameStateManager.
/// </summary>
public class GameEndDetectionManager : MonoBehaviour
{
    [Header("Game End Detection Settings")]
    public bool enableGameEndDetection = true;
    public bool debugMode = false;
    
    [Header("Events")]
    public System.Action<PieceColor> OnCheckmate;
    public System.Action<PieceColor> OnStalemate;
    public System.Action<string> OnGameEnd; // reason for game end
    /// <summary>Who won the game that just ended (null for a draw)</summary>
    public PieceColor? Winner { get; private set; }

    // Conquest events for multi-player games
    public System.Action<PieceColor, PieceColor, int> OnConquest; // (conqueror, defeated, pieces transferred)

    // New forfeit and draw events
    public System.Action<PieceColor> OnForfeit; // player who forfeited
    public System.Action<PieceColor> OnDrawOffered; // player who offered draw
    public System.Action<PieceColor> OnDrawAccepted; // player who accepted draw
    public System.Action<PieceColor> OnDrawDeclined; // player who declined draw
    
    public static GameEndDetectionManager Instance { get; private set; }
    
    // Draw offer state tracking
    private bool drawOfferPending = false;
    private PieceColor drawOfferingPlayer;
    private float drawOfferTimestamp;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("GameEndDetectionManager: Instance created");
        }
        else
        {
            Debug.LogWarning("GameEndDetectionManager: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        // Subscribe to game state changes
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
        }
        
        // Subscribe to check detection events
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.OnKingInCheck += OnKingInCheckDetected;
        }
        
        // Subscribe to turn manager events
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += OnTurnChanged;
        }
        
        // Subscribe to timer events for timed play mode
        if (TimerManager.Instance != null)
        {
            TimerManager.Instance.OnTimerExpired += OnTimerExpired;
        }
    }
    
    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
        
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.OnKingInCheck -= OnKingInCheckDetected;
        }
        
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= OnTurnChanged;
        }
        
        if (TimerManager.Instance != null)
        {
            TimerManager.Instance.OnTimerExpired -= OnTimerExpired;
        }
    }
    
    private void OnGameStateChanged(GameState newState)
    {
        if (newState == GameState.Playing)
        {
            Debug.Log("GameEndDetectionManager: Game started - enabling game end detection");
            enableGameEndDetection = !ZombieMode.Active && !QueenBlitz.Active; // zombie runs end when the player has nothing left (ZombieDirector)
            if (SavedGames.Resuming) enableGameEndDetection = false; // the stand-in setup isn't the real game: SavedGames turns this back on
        }
        else if (newState == GameState.GameOver)
        {
            Debug.Log("GameEndDetectionManager: Game ended - disabling game end detection");
            enableGameEndDetection = false;
        }
    }
    
    private void OnKingInCheckDetected(PieceColor kingColor)
    {
        if (!enableGameEndDetection) return;
        // With 3+ players, checkmates are found after every move in OnTurnChanged, where we know who moved
        if (IsMultiplayerInProgress()) return;
        
        // When a king is in check, immediately check for checkmate
        if (IsCheckmate(kingColor))
        {
            HandleCheckmate(kingColor);
        }
    }
    
    private bool IsMultiplayerInProgress() =>
        PlayerManager.Instance != null && PlayerManager.Instance.GetActivePlayerCount() > 2;

    private void OnTurnChanged(PieceColor previousPlayer, PieceColor newCurrentPlayer)
    {
        if (!enableGameEndDetection) return;
        // Not a real move: the turn is being (re)set, e.g. when play starts. Nobody can have been mated by it.
        if (previousPlayer == newCurrentPlayer) return;
        // The board just changed: work from fresh check states, or a mate could be mistaken for a stalemate
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
        UpdateFirstCheckers(previousPlayer);

        // Multiplayer: the player who just moved may have checkmated ANY opponent, not just the
        // next player. Credit the conquest to the mover. (Skipped when the "move" was just the
        // turn passing over an eliminated player.)
        if (IsMultiplayerInProgress() && !PlayerManager.Instance.IsPlayerEliminated(previousPlayer))
        {
            foreach (PieceColor player in PlayerManager.Instance.GetActivePlayers().ToList())
            {
                if (player == previousPlayer || PlayerManager.Instance.IsPlayerEliminated(player)) continue;
                if (CheckDetectionManager.Instance != null && CheckDetectionManager.Instance.IsKingInCheck(player) && !HasLegalMoves(player))
                {
                    HandleCheckmate(player, previousPlayer);
                    if (!enableGameEndDetection) return; // that was the final checkmate
                }
            }
        }

        // The player whose turn it now is was just conquered: pass the turn on
        if (PlayerManager.Instance != null && PlayerManager.Instance.IsPlayerEliminated(newCurrentPlayer))
        {
            StartCoroutine(SkipEliminatedTurn());
            return;
        }

        // When a turn changes, check if the new current player has any legal moves
        if (!HasLegalMoves(newCurrentPlayer))
        {
            // No legal moves - check if it's checkmate or stalemate
            if (CheckDetectionManager.Instance != null && CheckDetectionManager.Instance.IsKingInCheck(newCurrentPlayer))
            {
                // Pass previousPlayer as the one who delivered checkmate
                HandleCheckmate(newCurrentPlayer, previousPlayer);
            }
            else
            {
                HandleStalemate(newCurrentPlayer);
            }
        }
    }
    
    /// <summary>
    /// Handle timer expiry - end the game with the opposing player winning
    /// </summary>
    private void OnTimerExpired(PieceColor playerWhoRanOutOfTime)
    {
        if (!enableGameEndDetection)
        {
            Debug.LogWarning("GameEndDetectionManager: Timer expiry ignored - game end detection disabled");
            return;
        }
        
        // Validate game state
        if (GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing)
        {
            Debug.LogWarning("GameEndDetectionManager: Timer expiry ignored - not in playing state");
            return;
        }
        
        PieceColor winner = (playerWhoRanOutOfTime == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        string reason = $"Time Out! {winner} wins - {playerWhoRanOutOfTime} ran out of time";
        
        Debug.Log($"⏰ GameEndDetectionManager: TIMER EXPIRED - {reason}");
        
        // Stop timers immediately (defensive - timer should already be stopped but ensure it)
        if (TimerManager.Instance != null)
        {
            TimerManager.Instance.StopAllTimers();
            Debug.Log("🛑 GameEndDetectionManager: Stopped all timers due to timer expiry");
        }
        
        // Clear any pending draw offers
        ClearDrawOffer();
        
        // Fire events
        Winner = winner;
        OnGameEnd?.Invoke(reason);
        
        // End the game
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.GameOver);
        }
        
        Debug.Log($"🏁 GAME OVER: {reason}");
    }
    
    /// <summary>
    /// Check if a player has any legal moves available
    /// </summary>
    /// <param name="player">Player to check</param>
    /// <returns>True if player has legal moves</returns>
    public bool HasLegalMoves(PieceColor player)
    {
        if (!enableGameEndDetection || ChessBoard.Instance == null)
        {
            return true; // Assume player has moves if detection is disabled
        }

        int totalLegalMoves = 0;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Check all pieces of the specified color
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);

                    if (piece != null && piece.pieceColor == player)
                    {
                        List<BoardPosition> legalMoves = piece.GetLegalMoves();
                        totalLegalMoves += legalMoves.Count;

                        if (debugMode)
                        {
                            Debug.Log($"GameEndDetectionManager: {player} {piece.pieceType} at {pos} has {legalMoves.Count} legal moves");
                        }

                        // Early exit if we find any legal move
                        if (legalMoves.Count > 0)
                        {
                            if (debugMode)
                            {
                                Debug.Log($"GameEndDetectionManager: {player} has legal moves (found {legalMoves.Count} moves for {piece.pieceType})");
                            }
                            return true;
                        }
                    }
                }
            }
        }

        if (debugMode)
        {
            Debug.Log($"GameEndDetectionManager: {player} has NO legal moves (total: {totalLegalMoves})");
        }

        return false;
    }
    
    /// <summary>
    /// Check if a player is in checkmate (king in check + no legal moves)
    /// </summary>
    /// <param name="player">Player to check</param>
    /// <returns>True if player is in checkmate</returns>
    public bool IsCheckmate(PieceColor player)
    {
        if (!enableGameEndDetection)
        {
            return false;
        }
        
        // Checkmate requires both: king in check AND no legal moves
        bool kingInCheck = CheckDetectionManager.Instance != null && 
                          CheckDetectionManager.Instance.IsKingInCheck(player);
        bool hasNoLegalMoves = !HasLegalMoves(player);
        
        bool isCheckmate = kingInCheck && hasNoLegalMoves;
        
        if (debugMode)
        {
            Debug.Log($"GameEndDetectionManager: Checkmate check for {player} - King in check: {kingInCheck}, No legal moves: {hasNoLegalMoves}, Result: {isCheckmate}");
        }
        
        return isCheckmate;
    }
    
    /// <summary>
    /// Check if a player is in stalemate (king not in check + no legal moves)
    /// </summary>
    /// <param name="player">Player to check</param>
    /// <returns>True if player is in stalemate</returns>
    public bool IsStalemate(PieceColor player)
    {
        if (!enableGameEndDetection)
        {
            return false;
        }
        
        // Stalemate requires: king NOT in check AND no legal moves
        bool kingNotInCheck = CheckDetectionManager.Instance == null || 
                             !CheckDetectionManager.Instance.IsKingInCheck(player);
        bool hasNoLegalMoves = !HasLegalMoves(player);
        
        bool isStalemate = kingNotInCheck && hasNoLegalMoves;
        
        if (debugMode)
        {
            Debug.Log($"GameEndDetectionManager: Stalemate check for {player} - King not in check: {kingNotInCheck}, No legal moves: {hasNoLegalMoves}, Result: {isStalemate}");
        }
        
        return isStalemate;
    }
    
    /// <summary>
    /// Handle checkmate detection - end game or trigger conquest based on player count
    /// </summary>
    /// <param name="checkmatedPlayer">The player who was checkmated</param>
    /// <param name="lastMover">The player who just moved (delivered checkmate). Optional - if not provided, will be determined from game state</param>
    /// <summary>
    /// Online: another device reports a player knocked out (resigned, ran out of time, left).
    /// Checkmates are detected here too, so a player already out is left alone.
    /// </summary>
    public void ApplyRemoteElimination(PieceColor loser, PieceColor? conqueror)
    {
        if (PlayerManager.Instance == null || PlayerManager.Instance.IsPlayerEliminated(loser)) return;
        if (PlayerManager.Instance.GetActivePlayerCount() <= 2) return; // two-player endings come as game:over
        if (conqueror.HasValue) PlayerManager.Instance.TransferPiecesToPlayer(loser, conqueror.Value);
        else RemoveAllPieces(loser); // forfeited while not in check
        PlayerManager.Instance.EliminatePlayer(loser);
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
        if (TurnManager.Instance != null && TurnManager.Instance.GetCurrentPlayer() == loser) StartCoroutine(SkipEliminatedTurn());
    }

    private System.Collections.IEnumerator SkipEliminatedTurn()
    {
        yield return null;
        if (TurnManager.Instance != null && PlayerManager.Instance != null &&
            PlayerManager.Instance.IsPlayerEliminated(TurnManager.Instance.GetCurrentPlayer()))
        {
            TurnManager.Instance.NextTurn();
        }
    }

    private void HandleCheckmate(PieceColor checkmatedPlayer, PieceColor? lastMover = null)
    {
        // Already conquered (detected twice): nothing more to do
        if (PlayerManager.Instance != null && PlayerManager.Instance.IsPlayerEliminated(checkmatedPlayer)) return;

        Debug.Log($"🏁 GameEndDetectionManager: CHECKMATE DETECTED for {checkmatedPlayer}, lastMover: {lastMover?.ToString() ?? "unknown"}");

        // Get active player count to determine if this triggers conquest or game end
        int activePlayerCount = PlayerManager.Instance != null
            ? PlayerManager.Instance.GetActivePlayerCount()
            : 2;

        Debug.Log($"🏁 Active player count: {activePlayerCount}");

        if (activePlayerCount > 2)
        {
            // MULTI-PLAYER CONQUEST: Transfer pieces instead of ending game
            HandleConquest(checkmatedPlayer, lastMover);
        }
        else
        {
            // FINAL SHOWDOWN: Only 2 players left, end the game
            HandleFinalCheckmate(checkmatedPlayer);
        }
    }

    /// <summary>
    /// Handle conquest when a player is checkmated with more than 2 players remaining
    /// Transfer all checkmated player's pieces to the conquering player
    /// </summary>
    /// <param name="checkmatedPlayer">The player who was checkmated</param>
    /// <param name="lastMover">The player who just moved (delivered checkmate). Optional - if not provided, will be determined from game state</param>
    private void HandleConquest(PieceColor checkmatedPlayer, PieceColor? lastMover = null)
    {
        if (PlayerManager.Instance == null)
        {
            Debug.LogError("GameEndDetectionManager.HandleConquest: PlayerManager.Instance is null!");
            return;
        }

        // Determine who gets the conquered pieces
        PieceColor conqueror;
        if (lastMover.HasValue)
        {
            // Use the provided lastMover (most reliable - comes directly from turn system)
            conqueror = lastMover.Value;
            Debug.Log($"🎨 CONQUEST: Using lastMover from turn system: {conqueror}");
        }
        else
        {
            // Fallback to detection logic (used when checkmate detected from check event)
            conqueror = PlayerManager.Instance.GetCheckmateTriggeringPlayer(checkmatedPlayer);
            Debug.Log($"🎨 CONQUEST: Using fallback detection logic: {conqueror}");
        }

        if (conqueror == checkmatedPlayer)
        {
            Debug.LogWarning($"GameEndDetectionManager: ignoring conquest of {checkmatedPlayer} by itself");
            return;
        }
        Debug.Log($"🎨 CONQUEST: {conqueror} has checkmated {checkmatedPlayer}!");

        // Transfer all pieces from checkmated player to conqueror
        int piecesTransferred = PlayerManager.Instance.TransferPiecesToPlayer(checkmatedPlayer, conqueror);

        // Eliminate the checkmated player
        PlayerManager.Instance.EliminatePlayer(checkmatedPlayer);

        // Get updated active player count
        int remainingPlayers = PlayerManager.Instance.GetActivePlayerCount();

        string conquestMessage = $"Conquest! {conqueror} checkmated {checkmatedPlayer} and conquered {piecesTransferred} pieces! {remainingPlayers} players remain.";

        Debug.Log($"🎨 {conquestMessage}");

        // Fire conquest events (will be used by UI)
        OnCheckmate?.Invoke(checkmatedPlayer);
        OnConquest?.Invoke(conqueror, checkmatedPlayer, piecesTransferred);

        // Invalidate check detection cache since piece colors changed
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.InvalidateCache();
        }

        // Continue game - do NOT change to GameOver state
        Debug.Log($"🎮 Game continues with {remainingPlayers} players remaining");
    }

    /// <summary>
    /// Handle final checkmate when only 2 players remain - end the game
    /// </summary>
    private void HandleFinalCheckmate(PieceColor checkmatedPlayer)
    {
        // Determine winner in 2-player scenario
        PieceColor winner;
        if (PlayerManager.Instance != null)
        {
            List<PieceColor> activePlayers = PlayerManager.Instance.GetActivePlayers();
            // Winner is the remaining active player who is not checkmated
            winner = activePlayers.FirstOrDefault(p => p != checkmatedPlayer);
            if (winner == default(PieceColor))
            {
                // Fallback
                winner = (checkmatedPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            }
        }
        else
        {
            // Fallback for 2-player games without PlayerManager
            winner = (checkmatedPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        }

        string reason = $"Checkmate! {winner} wins by checkmate against {checkmatedPlayer}";

        Debug.Log($"🏁 GameEndDetectionManager: FINAL CHECKMATE - {reason}");

        // Stop timers immediately to prevent them from continuing after checkmate
        if (TimerManager.Instance != null)
        {
            TimerManager.Instance.StopAllTimers();
            Debug.Log("🛑 GameEndDetectionManager: Stopped all timers due to checkmate");
        }

        // Fire events
        OnCheckmate?.Invoke(checkmatedPlayer);
        Winner = winner;
        OnGameEnd?.Invoke(reason);

        // End the game
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.GameOver);
        }

        // Show checkmate message
        Debug.Log($"🏁 GAME OVER: {reason}");
    }
    
    /// <summary>
    /// Handle stalemate detection - end the game in a draw
    /// </summary>
    private void HandleStalemate(PieceColor stalematedPlayer)
    {
        // Check active player count to determine behavior
        int activePlayerCount = PlayerManager.Instance != null
            ? PlayerManager.Instance.GetActivePlayerCount()
            : 2;

        // Multiplayer, but nobody else can move either: skipping would go round forever, so it's a draw
        if (activePlayerCount > 2 && PlayerManager.Instance != null &&
            !PlayerManager.Instance.GetActivePlayers().Any(p => p != stalematedPlayer && !PlayerManager.Instance.IsPlayerEliminated(p) && HasLegalMoves(p)))
        {
            activePlayerCount = 2; // end it below like a two-player stalemate
        }

        if (activePlayerCount > 2)
        {
            // MULTIPLAYER: Player has no legal moves - skip their turn and continue game
            string message = $"{stalematedPlayer} has no legal moves but is not in check - skipping turn";

            Debug.Log($"⏭️ GameEndDetectionManager: TURN SKIP - {message}");

            // Fire stalemate event (for UI notification/logging)
            OnStalemate?.Invoke(stalematedPlayer);

            // Skip to next player's turn (do NOT end game)
            if (TurnManager.Instance != null)
            {
                TurnManager.Instance.NextTurn();
                Debug.Log($"⏭️ GameEndDetectionManager: Advanced to next player after {stalematedPlayer} skip");
            }
            else
            {
                Debug.LogError("⚠️ GameEndDetectionManager: TurnManager is null, cannot skip turn!");
            }
        }
        else
        {
            // 2-PLAYER: Traditional stalemate = draw, end game
            string reason = IsMultiplayerInProgress()
                ? "Stalemate! Game ends in a draw - nobody has a legal move"
                : $"Stalemate! Game ends in a draw - {stalematedPlayer} has no legal moves but is not in check";

            Debug.Log($"🏁 GameEndDetectionManager: STALEMATE DETECTED - {reason}");

            // Stop timers immediately to prevent them from continuing after stalemate
            if (TimerManager.Instance != null)
            {
                TimerManager.Instance.StopAllTimers();
                Debug.Log("🛑 GameEndDetectionManager: Stopped all timers due to stalemate");
            }

            // Fire events
            OnStalemate?.Invoke(stalematedPlayer);
            Winner = null;
            OnGameEnd?.Invoke(reason);

            // End the game
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ChangeState(GameState.GameOver);
            }

            // Show stalemate message (could be enhanced with UI later)
            Debug.Log($"🏁 GAME OVER: {reason}");
        }
    }
    
    /// <summary>
    /// Manually check for game end conditions for the current player
    /// </summary>
    public void CheckForGameEnd()
    {
        if (!enableGameEndDetection || TurnManager.Instance == null)
        {
            return;
        }
        
        PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
        
        if (debugMode)
        {
            Debug.Log($"GameEndDetectionManager: Checking game end conditions for {currentPlayer}");
        }
        
        if (IsCheckmate(currentPlayer))
        {
            HandleCheckmate(currentPlayer);
        }
        else if (IsStalemate(currentPlayer))
        {
            HandleStalemate(currentPlayer);
        }
        else if (debugMode)
        {
            Debug.Log($"GameEndDetectionManager: {currentPlayer} can continue playing");
        }
    }
    
    /// <summary>
    /// Get debug information about current game end state
    /// </summary>
    /// <returns>Debug string with game end state information</returns>
    public string GetDebugInfo()
    {
        if (!enableGameEndDetection)
        {
            return "Game end detection disabled";
        }
        
        if (TurnManager.Instance == null)
        {
            return "TurnManager not available";
        }
        
        PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
        bool hasLegalMoves = HasLegalMoves(currentPlayer);
        bool kingInCheck = CheckDetectionManager.Instance != null && 
                          CheckDetectionManager.Instance.IsKingInCheck(currentPlayer);
        
        string info = $"Current Player: {currentPlayer}\n";
        info += $"Has Legal Moves: {hasLegalMoves}\n";
        info += $"King in Check: {kingInCheck}\n";
        
        if (!hasLegalMoves)
        {
            if (kingInCheck)
            {
                info += "STATUS: CHECKMATE!";
            }
            else
            {
                info += "STATUS: STALEMATE!";
            }
        }
        else
        {
            info += "STATUS: Game continues";
        }
        
        return info;
    }
    
    /// <summary>
    /// Count total legal moves for a player (useful for debugging)
    /// </summary>
    /// <param name="player">Player to count moves for</param>
    /// <returns>Total number of legal moves</returns>
    public int CountLegalMoves(PieceColor player)
    {
        if (!enableGameEndDetection || ChessBoard.Instance == null)
        {
            return -1; // Invalid count
        }

        int totalMoves = 0;

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

                    if (piece != null && piece.pieceColor == player)
                    {
                        totalMoves += piece.GetLegalMoves().Count;
                    }
                }
            }
        }

        return totalMoves;
    }
    
    /// <summary>
    /// Handle draw by repetition (called by TurnManager when 3-fold repetition is detected)
    /// </summary>
    public void HandleDrawByRepetition()
    {
        if (!enableGameEndDetection)
        {
            Debug.LogWarning("GameEndDetectionManager: Draw by repetition ignored - game end detection disabled");
            return;
        }
        
        string reason = "Draw by repetition! The same position has occurred three times";
        
        Debug.Log($"🏁 GameEndDetectionManager: DRAW BY REPETITION DETECTED - {reason}");
        
        // Stop timers immediately to prevent them from continuing after repetition draw
        if (TimerManager.Instance != null)
        {
            TimerManager.Instance.StopAllTimers();
            Debug.Log("🛑 GameEndDetectionManager: Stopped all timers due to draw by repetition");
        }
        
        // Fire events
        Winner = null;
        OnGameEnd?.Invoke(reason);
        
        // End the game
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.GameOver);
        }
        
        // Show draw message
        Debug.Log($"🏁 GAME OVER: {reason}");
    }
    
    // ===== FORFEIT AND DRAW MECHANICS =====
    
    /// <summary>
    /// Handle a forfeit request from a player
    /// </summary>
    /// <param name="forfeitingPlayer">The player who wants to forfeit</param>
    /// <summary>Who wins when this player forfeits a two-player game</summary>
    /// <summary>End the game with this result (zombie runs end this way)</summary>
    public void EndGame(PieceColor? winner, string reason)
    {
        if (TimerManager.Instance != null) TimerManager.Instance.StopAllTimers();
        ClearDrawOffer();
        Winner = winner;
        OnGameEnd?.Invoke(reason);
        if (GameStateManager.Instance != null) GameStateManager.Instance.ChangeState(GameState.GameOver);
    }

    public static PieceColor ForfeitWinner(PieceColor forfeitingPlayer)
    {
        if (PlayerManager.Instance != null)
            foreach (PieceColor p in PlayerManager.Instance.GetActivePlayers())
                if (p != forfeitingPlayer && !PlayerManager.Instance.IsPlayerEliminated(p)) return p;
        return forfeitingPlayer == PieceColor.White ? PieceColor.Black : PieceColor.White;
    }

    // ── Who checked whom first (they win the pieces if the checked player forfeits) ──

    private readonly Dictionary<PieceColor, PieceColor> firstChecker = new Dictionary<PieceColor, PieceColor>();

    /// <summary>The other players attacking this player's King right now</summary>
    private static List<PieceColor> CheckersOf(PieceColor player)
    {
        var result = new List<PieceColor>();
        if (CheckDetectionManager.Instance == null) return result;
        foreach (PieceColor c in CheckDetectionManager.Instance.GetAttackingPlayers(player))
        {
            if (c == player) continue;
            if (PlayerManager.Instance != null && PlayerManager.Instance.IsPlayerEliminated(c)) continue;
            result.Add(c);
        }
        result.Sort(); // turn order
        return result;
    }

    /// <summary>
    /// The first checker keeps the claim while still attacking; a check that starts on a move
    /// belongs to the player who moved.
    /// </summary>
    private void UpdateFirstCheckers(PieceColor? mover)
    {
        if (PlayerManager.Instance == null) return;
        foreach (PieceColor c in PlayerManager.Instance.GetActivePlayers())
        {
            List<PieceColor> checkers = CheckersOf(c);
            if (checkers.Count == 0) { firstChecker.Remove(c); continue; }
            if (firstChecker.TryGetValue(c, out PieceColor current) && checkers.Contains(current)) continue;
            firstChecker[c] = mover.HasValue && checkers.Contains(mover.Value) ? mover.Value : checkers[0];
        }
    }

    /// <summary>Take all of a player's pieces off the board (they forfeited while not in check)</summary>
    private static void RemoveAllPieces(PieceColor player)
    {
        var board = ChessBoard.Instance;
        if (board == null) return;
        Vector3Int d = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            var pos = new BoardPosition(x, y, z);
            ChessPiece p = board.GetPieceAt(pos);
            if (p == null || p.pieceColor != player) continue;
            PieceEffects.Shatter(p, null);
            board.RemovePieceFromBoard(p, pos);
        }
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
    }

    /// <summary>
    /// 4 or 6 player forfeit: the game goes on. In check, it counts as checkmate by whoever checked
    /// them first, who takes their pieces; otherwise their pieces leave the board.
    /// </summary>
    private void HandleMultiplayerForfeit(PieceColor player)
    {
        ClearDrawOffer();
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
        List<PieceColor> checkers = CheckersOf(player);
        PieceColor? checker = null;
        if (checkers.Count > 0)
            checker = firstChecker.TryGetValue(player, out PieceColor first) && checkers.Contains(first) ? first : checkers[0];

        if (checker.HasValue)
        {
            HandleConquest(player, checker);
        }
        else
        {
            RemoveAllPieces(player);
            PlayerManager.Instance.EliminatePlayer(player);
            if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
        }
        firstChecker.Remove(player);
        if (TurnManager.Instance != null && TurnManager.Instance.GetCurrentPlayer() == player) StartCoroutine(SkipEliminatedTurn());
        if (PlayerManager.Instance.GetActivePlayerCount() == 1)
        {
            PieceColor winner = PlayerManager.Instance.GetActivePlayers()[0];
            if (TimerManager.Instance != null) TimerManager.Instance.StopAllTimers();
            Winner = winner;
            OnGameEnd?.Invoke($"{winner} wins - everyone else is out");
            if (GameStateManager.Instance != null) GameStateManager.Instance.ChangeState(GameState.GameOver);
        }
    }

    public void HandleForfeit(PieceColor forfeitingPlayer)
    {
        if (OnlineSession.ShouldSend(forfeitingPlayer)) OnlineClient.Instance.SendResign(forfeitingPlayer);

        // Zombie runs have no game end detection: forfeiting ends the run, scored as it stands
        if (ZombieMode.Active && ZombieDirector.Instance != null)
        {
            ZombieDirector.Instance.GiveUp();
            return;
        }

        // 4 or 6 players: the game goes on without them
        if (enableGameEndDetection && IsMultiplayerInProgress() && GameStateManager.Instance != null && GameStateManager.Instance.CanMovePieces())
        {
            if (OnlineSession.IsActive) return; // the server decides; its game:playerEliminated applies it here
            HandleMultiplayerForfeit(forfeitingPlayer);
            return;
        }
        if (!enableGameEndDetection)
        {
            Debug.LogWarning("GameEndDetectionManager: Forfeit ignored - game end detection disabled");
            return;
        }
        
        // Validate game state
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            Debug.LogWarning("GameEndDetectionManager: Forfeit ignored - not in playing state");
            return;
        }
        
        // Validate it's the forfeiting player's turn (optional - could allow forfeit anytime)
        if (TurnManager.Instance != null && TurnManager.Instance.GetCurrentPlayer() != forfeitingPlayer)
        {
            Debug.LogWarning($"GameEndDetectionManager: {forfeitingPlayer} tried to forfeit but it's {TurnManager.Instance.GetCurrentPlayer()}'s turn");
            // Allow forfeit anyway - players can forfeit anytime
        }
        
        // Allow both human and AI players to forfeit
        // AI forfeit decisions should come from AIPlayer logic
        if (TurnManager.Instance != null && TurnManager.Instance.IsPlayerAI(forfeitingPlayer))
        {
            Debug.Log($"GameEndDetectionManager: Processing AI player {forfeitingPlayer} forfeit");
        }
        
        PieceColor winner = ForfeitWinner(forfeitingPlayer);
        string reason = $"Forfeit! {winner} wins by forfeit - {forfeitingPlayer} forfeited the game";
        
        Debug.Log($"🏁 GameEndDetectionManager: FORFEIT - {reason}");
        
        // Stop timers immediately to prevent them from continuing after forfeit
        if (TimerManager.Instance != null)
        {
            TimerManager.Instance.StopAllTimers();
            Debug.Log("🛑 GameEndDetectionManager: Stopped all timers due to forfeit");
        }
        
        // Clear any pending draw offers
        ClearDrawOffer();
        
        // Fire events
        OnForfeit?.Invoke(forfeitingPlayer);
        Winner = winner;
        OnGameEnd?.Invoke(reason);
        
        // End the game
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.GameOver);
        }
        
        Debug.Log($"🏁 GAME OVER: {reason}");
    }
    
    /// <summary>
    /// Handle a draw offer from a player
    /// </summary>
    /// <param name="offeringPlayer">The player who is offering a draw</param>
    public void HandleDrawOffer(PieceColor offeringPlayer)
    {
        if (!enableGameEndDetection)
        {
            Debug.LogWarning("GameEndDetectionManager: Draw offer ignored - game end detection disabled");
            return;
        }
        
        // Validate game state
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            Debug.LogWarning("GameEndDetectionManager: Draw offer ignored - not in playing state");
            return;
        }
        
        // Validate it's the offering player's turn
        if (TurnManager.Instance == null || TurnManager.Instance.GetCurrentPlayer() != offeringPlayer)
        {
            Debug.LogWarning($"GameEndDetectionManager: {offeringPlayer} tried to offer draw but it's not their turn");
            return;
        }
        
        // Allow both human and AI players to offer draws
        // AI draw decisions should come from AIPlayer logic
        if (TurnManager.Instance.IsPlayerAI(offeringPlayer))
        {
            Debug.Log($"GameEndDetectionManager: Processing AI player {offeringPlayer} draw offer");
        }
        
        // Check if there's already a pending draw offer
        if (drawOfferPending)
        {
            Debug.LogWarning($"GameEndDetectionManager: Draw offer from {offeringPlayer} ignored - another offer is pending");
            return;
        }
        
        // Set draw offer state
        drawOfferPending = true;
        drawOfferingPlayer = offeringPlayer;
        drawOfferTimestamp = Time.time;
        if (OnlineSession.ShouldSend(offeringPlayer)) OnlineClient.Instance.SendOfferDraw(offeringPlayer);
        
        Debug.Log($"🤝 GameEndDetectionManager: Draw offered by {offeringPlayer}");
        
        // Fire event
        OnDrawOffered?.Invoke(offeringPlayer);
        
        // If the responding player is AI, immediately trigger their response evaluation
        PieceColor respondingPlayer = (offeringPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        if (TurnManager.Instance != null && TurnManager.Instance.IsPlayerAI(respondingPlayer))
        {
            Debug.Log($"🤖 GameEndDetectionManager: Draw offer made to AI player {respondingPlayer}, triggering response evaluation");
            
            if (AIPlayer.Instance != null)
            {
                AIPlayer.Instance.HandleDrawOfferResponse(respondingPlayer);
            }
            else
            {
                Debug.LogError("GameEndDetectionManager: AIPlayer.Instance is null - cannot handle AI draw response");
            }
        }
    }
    
    /// <summary>
    /// Handle a draw response (accept or decline)
    /// </summary>
    /// <param name="respondingPlayer">The player responding to the draw offer</param>
    /// <param name="accepted">True if accepting, false if declining</param>
    public void HandleDrawResponse(PieceColor respondingPlayer, bool accepted)
    {
        if (!enableGameEndDetection)
        {
            Debug.LogWarning("GameEndDetectionManager: Draw response ignored - game end detection disabled");
            return;
        }
        
        // Validate there's a pending draw offer
        if (!drawOfferPending)
        {
            Debug.LogWarning($"GameEndDetectionManager: {respondingPlayer} tried to respond to draw but no offer is pending");
            return;
        }
        
        // Validate this is the correct player responding (the one who didn't offer)
        PieceColor expectedResponder = (drawOfferingPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        if (respondingPlayer != expectedResponder)
        {
            Debug.LogWarning($"GameEndDetectionManager: {respondingPlayer} cannot respond to draw offer made by {drawOfferingPlayer}");
            return;
        }
        
        // Allow both human and AI players to respond to draws
        // AI draw responses should come from AIPlayer logic
        if (TurnManager.Instance != null && TurnManager.Instance.IsPlayerAI(respondingPlayer))
        {
            Debug.Log($"GameEndDetectionManager: Processing AI player {respondingPlayer} draw response");
        }
        
        if (OnlineSession.ShouldSend(respondingPlayer)) OnlineClient.Instance.SendRespondDraw(respondingPlayer, accepted);

        if (accepted)
        {
            // Draw accepted - end game in draw
            string reason = $"Draw by agreement! Both players agreed to a draw";
            
            Debug.Log($"🤝 GameEndDetectionManager: DRAW ACCEPTED - {reason}");
            
            // Stop timers immediately to prevent them from continuing after draw acceptance
            if (TimerManager.Instance != null)
            {
                TimerManager.Instance.StopAllTimers();
                Debug.Log("🛑 GameEndDetectionManager: Stopped all timers due to draw acceptance");
            }
            
            // Fire events
            OnDrawAccepted?.Invoke(respondingPlayer);
            Winner = null;
            OnGameEnd?.Invoke(reason);
            
            // End the game
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ChangeState(GameState.GameOver);
            }
            
            Debug.Log($"🏁 GAME OVER: {reason}");
        }
        else
        {
            // Draw declined - continue game
            Debug.Log($"🤝 GameEndDetectionManager: Draw declined by {respondingPlayer}");
            
            // Fire event
            OnDrawDeclined?.Invoke(respondingPlayer);
        }
        
        // Clear draw offer state
        ClearDrawOffer();
    }
    
    /// <summary>
    /// Clear any pending draw offer
    /// </summary>
    public void ClearDrawOffer()
    {
        if (drawOfferPending)
        {
            Debug.Log($"GameEndDetectionManager: Clearing pending draw offer from {drawOfferingPlayer}");
        }
        
        drawOfferPending = false;
        drawOfferingPlayer = PieceColor.White; // Reset to default
        drawOfferTimestamp = 0f;
    }
    
    /// <summary>
    /// Check if there's a pending draw offer
    /// </summary>
    public bool IsDrawOfferPending()
    {
        return drawOfferPending;
    }
    
    /// <summary>
    /// Get the player who made the pending draw offer
    /// </summary>
    public PieceColor GetDrawOfferingPlayer()
    {
        return drawOfferPending ? drawOfferingPlayer : PieceColor.White;
    }
    
    /// <summary>
    /// Get how long ago the draw offer was made (in seconds)
    /// </summary>
    public float GetDrawOfferAge()
    {
        return drawOfferPending ? (Time.time - drawOfferTimestamp) : 0f;
    }
    
    /// <summary>
    /// Auto-decline draw offers that are too old (optional feature)
    /// </summary>
    public void UpdateDrawOfferTimeout(float timeoutSeconds = 60f)
    {
        if (drawOfferPending && GetDrawOfferAge() > timeoutSeconds)
        {
            Debug.Log($"GameEndDetectionManager: Auto-declining draw offer from {drawOfferingPlayer} (timeout after {timeoutSeconds}s)");
            
            PieceColor autoDeclinedBy = (drawOfferingPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            OnDrawDeclined?.Invoke(autoDeclinedBy);
            
            ClearDrawOffer();
        }
    }
}