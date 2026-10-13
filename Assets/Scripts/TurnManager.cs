using UnityEngine;

/// <summary>
/// Manages turn-based gameplay to ensure players alternate turns properly.
/// Prevents any color from taking consecutive moves during the Playing phase.
/// </summary>
public class TurnManager : MonoBehaviour
{
    [Header("Turn Settings")]
    public PieceColor currentPlayer = PieceColor.White;
    public bool enableTurnValidation = true;

    [Header("Player Types - DEPRECATED: Use PlayerManager instead")]
    [System.Obsolete("Use PlayerManager.GetPlayerType() instead. Kept for backwards compatibility with 2-player games.")]
    public PlayerType whitePlayerType = PlayerType.Human;
    [System.Obsolete("Use PlayerManager.GetPlayerType() instead. Kept for backwards compatibility with 2-player games.")]
    public PlayerType blackPlayerType = PlayerType.Human;

    [Header("Check State Tracking - DEPRECATED: Use scalable dictionary in future")]
    [System.Obsolete("Check tracking needs to be scalable for multi-player. Currently limited to 2 players.")]
    public bool whiteInCheck = false;
    [System.Obsolete("Check tracking needs to be scalable for multi-player. Currently limited to 2 players.")]
    public bool blackInCheck = false;
    
    [Header("Opening Move Protection")]
    private bool isFirstTurnAfterPlacement = true;
    private int totalMovesMade = 0;
    
    [Header("Initial Check States")]
    private bool initialWhiteInCheck = false;
    private bool initialBlackInCheck = false;
    private bool bothPlayersStartInCheck = false;
    // Players placed into check who haven't had their first turn yet: no king can be taken until they have
    private readonly System.Collections.Generic.List<PieceColor> startedInCheck = new System.Collections.Generic.List<PieceColor>();
    private System.Collections.Generic.Dictionary<PieceColor, bool> initialCheckStates = new System.Collections.Generic.Dictionary<PieceColor, bool>();
    
    [Header("Anti-Repetition System")]
    private System.Collections.Generic.List<string> gameHistory = new System.Collections.Generic.List<string>();
    private const int MAX_HISTORY_LENGTH = 20; // Keep last 20 positions
    private const int REPETITION_DRAW_THRESHOLD = 3; // 3-fold repetition = draw
    
    [Header("Events")]
    public System.Action<PieceColor, PieceColor> OnTurnChanged; // (previousPlayer, newCurrentPlayer)
    public System.Action<PieceColor, PieceColor> OnInvalidTurnAttempt; // attempted player, current player
    public System.Action<PieceColor> OnPlayerInCheck;
    public System.Action<PieceColor> OnCheckResolved;
    
    [Header("Animation State Management")]
    private bool isWaitingForAnimationCompletion = false;
    private PieceColor pendingAIPlayer;
    private ChessPiece lastAnimatingPiece;
    
    public static TurnManager Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // CRITICAL FIX: Ensure currentPlayer is always initialized to White at startup
            // This prevents any possibility of Black moving first due to incorrect initialization
            currentPlayer = PieceColor.White;
            Debug.Log($"TurnManager: Instance created with starting player {currentPlayer} (explicitly set to White)");
            Debug.Log($"TurnManager: Player types initialized - White: {whitePlayerType}, Black: {blackPlayerType}");
        }
        else
        {
            Debug.LogWarning("TurnManager: Multiple instances detected, destroying duplicate");
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
            CheckDetectionManager.Instance.OnCheckResolved += OnCheckResolvedDetected;
        }
        
        // Subscribe to chaos rotation completion events
        if (ChaosRotationManager.Instance != null)
        {
            ChaosRotationManager.Instance.OnChaosRotationCompleted += OnChaosRotationCompleted;
            Debug.Log("TurnManager: Subscribed to ChaosRotationManager completion events");
        }
    }
    
    private void OnDestroy()
    {
        // Clean up animation event subscriptions to prevent memory leaks
        if (isWaitingForAnimationCompletion)
        {
            CleanupAnimationWaiting();
        }
        
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
        
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.OnKingInCheck -= OnKingInCheckDetected;
            CheckDetectionManager.Instance.OnCheckResolved -= OnCheckResolvedDetected;
        }
        
        if (ChaosRotationManager.Instance != null)
        {
            ChaosRotationManager.Instance.OnChaosRotationCompleted -= OnChaosRotationCompleted;
        }
    }
    
    /// <summary>
    /// Handle game state changes - initialize gameplay settings when entering Playing phase
    /// Turn order is now determined by initial check states rather than defaulting to White
    /// </summary>
    private void OnGameStateChanged(GameState newState)
    {
        if (newState == GameState.Playing)
        {
            Debug.Log($"TurnManager: Game started - current player is {currentPlayer} (determined by initial check states)");
            // Don't override currentPlayer here - it's already set by DetermineOpeningTurnOrder()
            isFirstTurnAfterPlacement = true;
            totalMovesMade = 0;
            Debug.Log("TurnManager: Normal gameplay ready - all moves allowed from start");
            
            // CRITICAL FIX: Only trigger AI move after a small delay to ensure proper turn order establishment
            // Start timer for the first player in timed play mode
            if (TimerManager.Instance != null)
            {
                bool isTimedPlayActive = TimerManager.Instance.IsTimedPlayActive();
                Debug.Log($"⏰ TurnManager: TimerManager found. IsTimedPlayActive: {isTimedPlayActive}");
                
                if (isTimedPlayActive)
                {
                    TimerManager.Instance.StartTimer(currentPlayer);
                    Debug.Log($"⏰ TurnManager: Started initial timer for {currentPlayer}");
                }
                else
                {
                    Debug.LogWarning($"⏰ TurnManager: Timer NOT started - IsTimedPlayActive returned false");
                    Debug.LogWarning($"⏰ TurnManager: TimerManager debug info: {TimerManager.Instance.GetDebugInfo()}");
                }
            }
            else
            {
                Debug.LogError($"⏰ TurnManager: TimerManager.Instance is NULL - cannot start timer!");
            }
            
            // This prevents Black AI from moving first when turn order hasn't been properly set yet
            if (IsCurrentPlayerAI())
            {
                Debug.Log($"TurnManager: Starting player {currentPlayer} is AI - scheduling delayed initial move");
                StartCoroutine(DelayedInitialAIMove(currentPlayer, 0.1f));
            }
        }
        else if (newState == GameState.PiecePlacement)
        {
            Debug.Log("TurnManager: Returned to placement phase");
        }
        else if (newState == GameState.GameOver)
        {
            Debug.Log("TurnManager: Game over - turn system disabled");
        }
    }
    
    /// <summary>
    /// Check if it's the specified player's turn to move
    /// </summary>
    public bool IsPlayerTurn(PieceColor player)
    {
        if (!enableTurnValidation)
        {
            Debug.Log($"TurnManager: Turn validation disabled - allowing {player} to move");
            return true;
        }
        
        bool isValidTurn = currentPlayer == player;
        
        if (!isValidTurn)
        {
            Debug.LogWarning($"TurnManager: INVALID TURN - {player} tried to move but it's {currentPlayer}'s turn");
            OnInvalidTurnAttempt?.Invoke(player, currentPlayer);
        }
        else
        {
            Debug.Log($"TurnManager: Valid turn confirmed for {player}");
        }
        
        return isValidTurn;
    }
    
    /// <summary>
    /// Check if a specific piece can be moved (belongs to current player)
    /// </summary>
    public bool CanMovePiece(ChessPiece piece)
    {
        if (piece == null)
        {
            Debug.LogError("TurnManager: Cannot validate null piece");
            return false;
        }
        
        // During placement phase, use placement manager rules
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            Debug.Log("TurnManager: Placement phase active - deferring to PlacementManager");
            return true; // Let PlacementManager handle placement rules
        }
        
        // During playing phase, enforce turn-based rules
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanMovePieces())
        {
            bool canMove = IsPlayerTurn(piece.pieceColor);
            if (!canMove)
            {
                Debug.LogWarning($"TurnManager: Cannot move {piece.pieceColor} {piece.pieceType} - not their turn (current: {currentPlayer})");
            }
            else
            {
                Debug.Log($"TurnManager: {piece.pieceColor} {piece.pieceType} can move - correct turn");
            }
            return canMove;
        }
        
        // Invalid game state
        Debug.LogWarning($"TurnManager: Cannot move pieces in current game state: {GameStateManager.Instance?.currentState}");
        return false;
    }
    
    /// <summary>
    /// Switch to the next player's turn after a successful move
    /// </summary>
    public void NextTurn()
    {
        Debug.Log($"🔍 TurnManager.NextTurn: CALLED - currentPlayer before switch: {currentPlayer}");

        // Only switch turns during active gameplay
        // CRITICAL FIX: Don't check CanMovePieces() here as it checks for animations
        // We need to allow turn switching even while pieces are animating
        // The deferred AI move logic below handles waiting for animations to complete
        if (GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing)
        {
            Debug.LogWarning($"TurnManager: Cannot switch turns - not in playing state ({GameStateManager.Instance?.currentState})");
            return;
        }

        PieceColor previousPlayer = currentPlayer;

        // Use PlayerManager for scalable turn cycling (supports 2-6 players)
        if (PlayerManager.Instance != null)
        {
            currentPlayer = PlayerManager.Instance.GetNextPlayer(currentPlayer);
            Debug.Log($"🔍 TurnManager.NextTurn: PlayerManager returned next player: {currentPlayer}");
        }
        else
        {
            // Fallback to binary toggle for 2-player games if PlayerManager unavailable
            Debug.LogWarning("TurnManager.NextTurn: PlayerManager not available, using fallback 2-player toggle");
            currentPlayer = (currentPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            Debug.Log($"🔍 TurnManager.NextTurn: Fallback toggle to: {currentPlayer}");
        }

        // Track moves for opening protection
        startedInCheck.Remove(previousPlayer);
        totalMovesMade++;
        if (isFirstTurnAfterPlacement && totalMovesMade >= 2)
        {
            isFirstTurnAfterPlacement = false;
            Debug.Log("TurnManager: Opening move protection disabled - king captures now allowed");
        }

        // ANTI-REPETITION: Record current board position after the move
        RecordCurrentPosition();

        // CHAOS MODE: Trigger chaos rotation if enabled
        if (ChaosRotationManager.Instance != null)
        {
            ChaosRotationManager.Instance.OnMoveCompleted(previousPlayer);
        }

        Debug.Log($"🔄 TurnManager: Turn switched from {previousPlayer} to {currentPlayer}");
        Debug.Log($"🔄 TurnManager: Player types - White: {whitePlayerType}, Black: {blackPlayerType}");

        // DIAGNOSTIC: Check player type determination
        PlayerType currentPlayerType = GetPlayerType(currentPlayer);
        Debug.Log($"🔍 TurnManager.NextTurn: GetPlayerType({currentPlayer}) returned: {currentPlayerType}");
        Debug.Log($"🔍 TurnManager.NextTurn: IsCurrentPlayerAI() = {IsCurrentPlayerAI()}");

        OnTurnChanged?.Invoke(previousPlayer, currentPlayer);
        Debug.Log($"🔍 TurnManager.NextTurn: OnTurnChanged event invoked - previousPlayer: {previousPlayer}, currentPlayer: {currentPlayer}");

        // Handle timer switching for timed play mode
        if (TimerManager.Instance != null && TimerManager.Instance.IsTimedPlayActive())
        {
            TimerManager.Instance.PauseTimer(); // Pause previous player's timer
            TimerManager.Instance.StartTimer(currentPlayer); // Start current player's timer
            Debug.Log($"⏰ TurnManager: Timer switched to {currentPlayer}");
        }

        // If the new current player is AI, check for animations before triggering their move.
        // A player conquered this turn has no move to make; GameEndDetectionManager passes their turn on.
        if (IsCurrentPlayerAI() && !(PlayerManager.Instance != null && PlayerManager.Instance.IsPlayerEliminated(currentPlayer)))
        {
            Debug.Log($"🔄 TurnManager: Current player {currentPlayer} is AI");
            Debug.Log($"🔍 TurnManager.NextTurn: Checking for animating pieces...");

            // Check if any pieces are still animating from the previous move
            bool piecesAnimating = AnyPiecesStillAnimating();
            Debug.Log($"🔍 TurnManager.NextTurn: AnyPiecesStillAnimating() returned: {piecesAnimating}");

            if (piecesAnimating)
            {
                Debug.Log($"🎬 TurnManager: Pieces still animating, deferring AI move for {currentPlayer}");
                isWaitingForAnimationCompletion = true;
                pendingAIPlayer = currentPlayer;
                Debug.Log($"🔍 TurnManager.NextTurn: Set pendingAIPlayer = {pendingAIPlayer}");
                SubscribeToAnimationCompletion();
            }
            else
            {
                Debug.Log($"🔄 TurnManager: No animations active, triggering immediate AI move for {currentPlayer}");
                Debug.Log($"🔍 TurnManager.NextTurn: About to call TriggerAIMove()...");
                TriggerAIMove();
            }
        }
        else
        {
            Debug.Log($"🔄 TurnManager: Current player {currentPlayer} is HUMAN, NOT triggering AI move");
        }

        // Check if the current player (human or AI) needs to respond to a pending draw offer
        CheckForPendingAIDrawResponse();

        Debug.Log($"🔍 TurnManager.NextTurn: COMPLETED - currentPlayer is now {currentPlayer}");
    }
    
    /// <summary>
    /// Manually set the current player (for testing or game setup)
    /// </summary>
    public void SetCurrentPlayer(PieceColor player)
    {
        PieceColor previousPlayer = currentPlayer;
        currentPlayer = player;
        
        Debug.Log($"TurnManager: Current player manually set from {previousPlayer} to {currentPlayer}");
        Debug.Log($"TurnManager: Player types - White: {whitePlayerType}, Black: {blackPlayerType}");
        Debug.Log($"TurnManager: Current player {currentPlayer} is AI: {IsCurrentPlayerAI()}");

        OnTurnChanged?.Invoke(previousPlayer, currentPlayer);
        
        // Handle timer switching if timed play is active
        if (TimerManager.Instance != null && TimerManager.Instance.IsTimedPlayActive())
        {
            TimerManager.Instance.PauseTimer();
            TimerManager.Instance.StartTimer(currentPlayer);
            Debug.Log($"⏰ TurnManager: Timer switched to {currentPlayer} (via SetCurrentPlayer)");
        }
        
        // If the new current player is AI, check for animations before triggering their move.
        // A player conquered this turn has no move to make; GameEndDetectionManager passes their turn on.
        if (IsCurrentPlayerAI() && !(PlayerManager.Instance != null && PlayerManager.Instance.IsPlayerEliminated(currentPlayer)))
        {
            Debug.Log($"TurnManager: Current player {currentPlayer} is AI");
            
            // Check if any pieces are still animating from the previous move
            if (AnyPiecesStillAnimating())
            {
                Debug.Log($"🎬 TurnManager: Pieces still animating, deferring AI move for {currentPlayer} (SetCurrentPlayer)");
                isWaitingForAnimationCompletion = true;
                pendingAIPlayer = currentPlayer;
                SubscribeToAnimationCompletion();
            }
            else
            {
                Debug.Log($"TurnManager: No animations active, triggering immediate AI move for {currentPlayer}");
                TriggerAIMove();
            }
        }
        else
        {
            Debug.Log($"TurnManager: Current player {currentPlayer} is HUMAN, NOT triggering AI move");
        }
        
        // Check if the current player (human or AI) needs to respond to a pending draw offer
        CheckForPendingAIDrawResponse();
    }
    
    /// <summary>
    /// Get the current player
    /// </summary>
    public PieceColor GetCurrentPlayer()
    {
        return currentPlayer;
    }
    
    /// <summary>
    /// Get turn validation status for debugging
    /// </summary>
    public string GetTurnInfo()
    {
        if (GameStateManager.Instance == null)
        {
            return "TurnManager: GameStateManager not available";
        }
        
        string gameState = GameStateManager.Instance.currentState.ToString();
        string validation = enableTurnValidation ? "ON" : "OFF";
        
        return $"Turn: {currentPlayer}, State: {gameState}, Validation: {validation}";
    }
    
    /// <summary>
    /// Enable/disable turn validation (useful for testing)
    /// </summary>
    public void SetTurnValidation(bool enabled)
    {
        enableTurnValidation = enabled;
        Debug.Log($"TurnManager: Turn validation {(enabled ? "ENABLED" : "DISABLED")}");
    }
    
    /// <summary>
    /// Handle king in check event from CheckDetectionManager
    /// </summary>
    private void OnKingInCheckDetected(PieceColor kingColor)
    {
        if (kingColor == PieceColor.White)
        {
            whiteInCheck = true;
        }
        else
        {
            blackInCheck = true;
        }
        
        Debug.Log($"TurnManager: {kingColor} king is in CHECK - player must resolve check before ending turn");
        OnPlayerInCheck?.Invoke(kingColor);
    }
    
    /// <summary>
    /// Handle check resolved event from CheckDetectionManager
    /// </summary>
    private void OnCheckResolvedDetected(PieceColor kingColor)
    {
        if (kingColor == PieceColor.White)
        {
            whiteInCheck = false;
        }
        else
        {
            blackInCheck = false;
        }
        
        Debug.Log($"TurnManager: {kingColor} check resolved");
        OnCheckResolved?.Invoke(kingColor);
    }
    
    /// <summary>
    /// Handle chaos rotation completion - ensure AI moves are triggered if needed
    /// </summary>
    private void OnChaosRotationCompleted(ChaosRotationType rotationType)
    {
        Debug.Log($"🌪️ TurnManager: Chaos rotation completed ({rotationType}) - checking if AI move is needed");
        
        // If current player is AI, trigger their move after chaos completion
        if (IsCurrentPlayerAI())
        {
            Debug.Log($"🌪️ TurnManager: Current player {currentPlayer} is AI - triggering move after chaos completion");
            
            // Use a small delay to ensure all chaos-related state changes are complete
            StartCoroutine(DelayedPostChaosAIMove(currentPlayer, 0.1f));
        }
        else
        {
            Debug.Log($"🌪️ TurnManager: Current player {currentPlayer} is HUMAN - no AI move needed after chaos");
        }
    }
    
    /// <summary>
    /// Delayed AI move trigger after chaos completion to ensure all state updates are complete
    /// </summary>
    private System.Collections.IEnumerator DelayedPostChaosAIMove(PieceColor playerColor, float delay)
    {
        Debug.Log($"🌪️ TurnManager.DelayedPostChaosAIMove: Waiting {delay}s after chaos completion for {playerColor}");
        yield return new UnityEngine.WaitForSeconds(delay);
        
        // Re-validate that this player should still move and is AI
        if (GetCurrentPlayer() == playerColor && IsCurrentPlayerAI() && 
            GameStateManager.Instance != null && GameStateManager.Instance.CanMovePiecesImmediate())
        {
            Debug.Log($"✅ TurnManager.DelayedPostChaosAIMove: Conditions validated, triggering AI move for {playerColor}");
            TriggerAIMove();
        }
        else
        {
            Debug.LogWarning($"⚠️ TurnManager.DelayedPostChaosAIMove: Conditions changed - not triggering AI move");
            Debug.LogWarning($"   currentPlayer: {GetCurrentPlayer()}, target: {playerColor}");
            Debug.LogWarning($"   IsAI: {IsCurrentPlayerAI()}, CanMove: {GameStateManager.Instance?.CanMovePiecesImmediate()}");
        }
    }
    
    /// <summary>
    /// Check if the current player is in check
    /// </summary>
    public bool IsCurrentPlayerInCheck()
    {
        return (currentPlayer == PieceColor.White && whiteInCheck) || 
               (currentPlayer == PieceColor.Black && blackInCheck);
    }
    
    /// <summary>
    /// Check if a specific player is in check
    /// </summary>
    public bool IsPlayerInCheck(PieceColor player)
    {
        return (player == PieceColor.White && whiteInCheck) || 
               (player == PieceColor.Black && blackInCheck);
    }
    
    /// <summary>
    /// Get check state information for debugging
    /// </summary>
    public string GetCheckStateInfo()
    {
        return $"Check State: White={whiteInCheck}, Black={blackInCheck}, Current={currentPlayer} in check={IsCurrentPlayerInCheck()}";
    }
    
    /// <summary>
    /// Check if this is the first turn after piece placement
    /// </summary>
    public bool IsFirstTurnAfterPlacement()
    {
        return isFirstTurnAfterPlacement;
    }
    
    /// <summary>
    /// Check if king captures should be blocked (opening move protection)
    /// Only blocks king captures when both players start in check to prevent instant checkmate
    /// </summary>
    public bool ShouldBlockKingCapture()
    {
        // Multiplayer: someone placed into check hasn't had their turn to get out of it yet
        if (startedInCheck.Count > 0) return true;

        // Block king captures if both players started in check (to prevent instant checkmate)
        if (bothPlayersStartInCheck && isFirstTurnAfterPlacement)
        {
            Debug.Log("🔄 TurnManager: Blocking king capture - both players started in check");
            return true;
        }
        
        // FIXED: Don't block king captures during normal opening when no players are in check
        // Standard gameplay should allow all legal moves from the start
        
        return false;
    }
    
    /// <summary>
    /// Get total moves made since game started
    /// </summary>
    public int GetTotalMovesMade()
    {
        return totalMovesMade;
    }
    
    /// <summary>
    /// Set the player type for a specific color
    /// </summary>
    public void SetPlayerType(PieceColor color, PlayerType playerType)
    {
        // Use PlayerManager for scalable player type management
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.SetPlayerType(color, playerType);
        }
        else
        {
            // Fallback for 2-player games
            Debug.LogWarning("TurnManager.SetPlayerType: PlayerManager not available, using fallback");
            if (color == PieceColor.White)
            {
                whitePlayerType = playerType;
            }
            else if (color == PieceColor.Black)
            {
                blackPlayerType = playerType;
            }
        }

        Debug.Log($"TurnManager: {color} player type set to {playerType}");
    }

    /// <summary>
    /// Get the player type for a specific color
    /// </summary>
    public PlayerType GetPlayerType(PieceColor color)
    {
        Debug.Log($"🔍 TurnManager.GetPlayerType: Called for {color}");

        // Use PlayerManager for scalable player type retrieval
        if (PlayerManager.Instance != null)
        {
            PlayerType result = PlayerManager.Instance.GetPlayerType(color);
            Debug.Log($"🔍 TurnManager.GetPlayerType: PlayerManager returned {result} for {color}");
            return result;
        }

        // Fallback for 2-player games
        Debug.LogWarning("TurnManager.GetPlayerType: PlayerManager not available, using fallback");
        PlayerType fallbackResult = color == PieceColor.White ? whitePlayerType : blackPlayerType;
        Debug.Log($"🔍 TurnManager.GetPlayerType: Fallback returned {fallbackResult} for {color}");
        return fallbackResult;
    }

    /// <summary>
    /// Check if the current player is an AI
    /// </summary>
    public bool IsCurrentPlayerAI()
    {
        Debug.Log($"🔍 TurnManager.IsCurrentPlayerAI: Called for currentPlayer={currentPlayer}");
        PlayerType playerType = GetPlayerType(currentPlayer);
        bool isAI = playerType == PlayerType.Computer;
        Debug.Log($"🔍 TurnManager.IsCurrentPlayerAI: {currentPlayer} type is {playerType}, isAI={isAI}");
        return isAI;
    }
    
    /// <summary>
    /// Check if a specific player is an AI
    /// </summary>
    public bool IsPlayerAI(PieceColor color)
    {
        return GetPlayerType(color) == PlayerType.Computer;
    }
    
    /// <summary>
    /// Set up a game mode (Human vs Human, Human vs AI, etc.)
    /// Simplified for 2-player games. For multi-player, configure via PlayerManager.
    /// </summary>
    public void SetGameMode(PlayerType whiteType, PlayerType blackType)
    {
        PlayerType prevWhiteType = GetPlayerType(PieceColor.White);
        PlayerType prevBlackType = GetPlayerType(PieceColor.Black);

        SetPlayerType(PieceColor.White, whiteType);
        SetPlayerType(PieceColor.Black, blackType);

        string gameMode = $"{whiteType} vs {blackType}";
        Debug.Log($"🎭 TurnManager: Game mode set to {gameMode} (was {prevWhiteType} vs {prevBlackType})");
        Debug.Log($"🎭 TurnManager: Current player: {currentPlayer}, IsCurrentPlayerAI(): {IsCurrentPlayerAI()}");

        // Only trigger AI move if we're in playing state and it's an AI's turn
        // Use immediate check to avoid cache delays
        if (GameStateManager.Instance != null &&
            GameStateManager.Instance.CanMovePiecesImmediate() &&
            IsCurrentPlayerAI())
        {
            Debug.Log($"🎭 TurnManager: Triggering AI move for {currentPlayer} because they are AI");
            TriggerAIMove();
        }
        else
        {
            Debug.Log($"🎭 TurnManager: NOT triggering AI move - GameState: {GameStateManager.Instance?.currentState}, CanMoveImmediate: {GameStateManager.Instance?.CanMovePiecesImmediate()}, IsCurrentPlayerAI: {IsCurrentPlayerAI()}");
        }
    }

    /// <summary>
    /// Get a description of the current game mode
    /// </summary>
    public string GetGameModeDescription()
    {
        if (PlayerManager.Instance != null)
        {
            return PlayerManager.Instance.GetGameModeDescription();
        }

        // Fallback for 2-player games
        return $"{GetPlayerType(PieceColor.White)} vs {GetPlayerType(PieceColor.Black)}";
    }

    /// <summary>
    /// Check if this is a Human vs AI game (any player count)
    /// </summary>
    public bool IsHumanVsAI()
    {
        if (PlayerManager.Instance != null)
        {
            int humanCount = 0;
            int aiCount = 0;

            foreach (PieceColor color in PlayerManager.Instance.GetAllPlayers())
            {
                if (GetPlayerType(color) == PlayerType.Human)
                    humanCount++;
                else
                    aiCount++;
            }

            return humanCount > 0 && aiCount > 0;
        }

        // Fallback for 2-player games
        PlayerType white = GetPlayerType(PieceColor.White);
        PlayerType black = GetPlayerType(PieceColor.Black);
        return (white == PlayerType.Human && black == PlayerType.Computer) ||
               (white == PlayerType.Computer && black == PlayerType.Human);
    }

    /// <summary>
    /// Check if this is an AI vs AI game (all players AI)
    /// </summary>
    public bool IsAIVsAI()
    {
        if (PlayerManager.Instance != null)
        {
            foreach (PieceColor color in PlayerManager.Instance.GetAllPlayers())
            {
                if (GetPlayerType(color) == PlayerType.Human)
                    return false;
            }
            return true;
        }

        // Fallback for 2-player games
        return GetPlayerType(PieceColor.White) == PlayerType.Computer &&
               GetPlayerType(PieceColor.Black) == PlayerType.Computer;
    }

    /// <summary>
    /// Check if this is a Human vs Human game (all players human)
    /// </summary>
    public bool IsHumanVsHuman()
    {
        if (PlayerManager.Instance != null)
        {
            foreach (PieceColor color in PlayerManager.Instance.GetAllPlayers())
            {
                if (GetPlayerType(color) == PlayerType.Computer)
                    return false;
            }
            return true;
        }

        // Fallback for 2-player games
        return GetPlayerType(PieceColor.White) == PlayerType.Human &&
               GetPlayerType(PieceColor.Black) == PlayerType.Human;
    }
    
    /// <summary>
    /// Validate that the current turn order is correct
    /// This ensures White moves first in standard games and prevents Black from moving first incorrectly
    /// </summary>
    public bool ValidateTurnOrder()
    {
        if (OnlineSession.IsActive) return true; // online, the server decides who moves
        if (PlayerManager.Instance != null && PlayerManager.Instance.GetActivePlayerCount() > 2) return true; // set by DetermineOpeningTurnOrder
        // If this is the very first move after placement (totalMovesMade == 0)
        if (totalMovesMade == 0 && isFirstTurnAfterPlacement)
        {
            // Force cache invalidation to ensure fresh check detection
            if (CheckDetectionManager.Instance != null)
            {
                CheckDetectionManager.Instance.InvalidateCache();
                Debug.Log($"🔄 TurnManager.ValidateTurnOrder: Cache invalidated for fresh check detection");
            }
            
            // In standard chess, White should always move first unless Black is in check
            bool whiteInCheck = CheckDetectionManager.Instance?.IsKingInCheck(PieceColor.White) ?? false;
            bool blackInCheck = CheckDetectionManager.Instance?.IsKingInCheck(PieceColor.Black) ?? false;
            
            Debug.Log($"🔄 TurnManager.ValidateTurnOrder: First move validation - White in check: {whiteInCheck}, Black in check: {blackInCheck}");
            Debug.Log($"🔄 TurnManager.ValidateTurnOrder: Current player: {currentPlayer}, Total moves: {totalMovesMade}");
            Debug.Log($"🔄 TurnManager.ValidateTurnOrder: Initial check states - White: {initialWhiteInCheck}, Black: {initialBlackInCheck}");
            
            if (!whiteInCheck && !blackInCheck)
            {
                // Standard opening - White must move first
                if (currentPlayer != PieceColor.White)
                {
                    Debug.LogError($"🚨 TurnManager.ValidateTurnOrder: INVALID TURN ORDER! Black trying to move first in standard opening!");
                    Debug.LogError($"🚨 TurnManager.ValidateTurnOrder: Correcting currentPlayer from {currentPlayer} to White");
                    currentPlayer = PieceColor.White;
                    return false;
                }
            }
            else if (blackInCheck && !whiteInCheck)
            {
                // Black in check should move first to resolve it
                if (currentPlayer != PieceColor.Black)
                {
                    Debug.Log($"🔄 TurnManager.ValidateTurnOrder: Black in check should move first - correcting currentPlayer from {currentPlayer} to Black");
                    PieceColor prev = currentPlayer;
                    currentPlayer = PieceColor.Black;
                    OnTurnChanged?.Invoke(prev, currentPlayer);
                }
            }
            else if (whiteInCheck && !blackInCheck)
            {
                // White in check should move first to resolve it
                if (currentPlayer != PieceColor.White)
                {
                    Debug.Log($"🔄 TurnManager.ValidateTurnOrder: White in check should move first - correcting currentPlayer from {currentPlayer} to White");
                    PieceColor prev = currentPlayer;
                    currentPlayer = PieceColor.White;
                    OnTurnChanged?.Invoke(prev, currentPlayer);
                }
            }
        }
        
        Debug.Log($"✅ TurnManager.ValidateTurnOrder: Turn order validation passed for {currentPlayer}");
        return true;
    }
    
    /// <summary>
    /// Trigger an AI move for the current player (if they are AI)
    /// </summary>
    /// <summary>After a resumed online game is rebuilt: let a robot whose turn it is move</summary>
    public void ResumeAIIfNeeded()
    {
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanMovePiecesImmediate() && IsCurrentPlayerAI())
            TriggerAIMove();
    }

    private void TriggerAIMove()
    {
        if (OnlineSession.Replaying) return; // rebuilding a resumed game: robots wait until it's caught up
        Debug.Log($"🔍 TurnManager.TriggerAIMove: CALLED for currentPlayer={currentPlayer}");

        // CRITICAL VALIDATION: Ensure proper turn order before any AI moves
        Debug.Log($"🔍 TurnManager.TriggerAIMove: Validating turn order...");
        bool turnOrderValid = ValidateTurnOrder();
        Debug.Log($"🔍 TurnManager.TriggerAIMove: ValidateTurnOrder() returned: {turnOrderValid}");

        if (!turnOrderValid)
        {
            Debug.LogError("🚨 TurnManager: BLOCKED AI move due to invalid turn order!");
            return;
        }

        // Enhanced validation to prevent AI from triggering for human players
        PlayerType currentPlayerType = GetPlayerType(currentPlayer);
        bool shouldTriggerAI = IsCurrentPlayerAI();

        Debug.Log($"🎯 TurnManager.TriggerAIMove: Current player {currentPlayer}, PlayerType: {currentPlayerType}, ShouldTriggerAI: {shouldTriggerAI}");
        Debug.Log($"🔍 TurnManager.TriggerAIMove: PlayerManager.Instance exists: {PlayerManager.Instance != null}");

        if (PlayerManager.Instance != null)
        {
            PlayerType pmType = PlayerManager.Instance.GetPlayerType(currentPlayer);
            Debug.Log($"🔍 TurnManager.TriggerAIMove: PlayerManager.GetPlayerType({currentPlayer}) = {pmType}");
        }
        else
        {
            Debug.LogWarning($"🔍 TurnManager.TriggerAIMove: PlayerManager.Instance is NULL, using deprecated fields");
            Debug.Log($"🔍 TurnManager.TriggerAIMove: whitePlayerType={whitePlayerType}, blackPlayerType={blackPlayerType}");
        }

        if (!shouldTriggerAI)
        {
            Debug.LogError($"🚨 TurnManager: BLOCKED AI trigger for HUMAN player {currentPlayer} (type: {currentPlayerType})");
            Debug.LogError($"🚨 TurnManager: whitePlayerType={whitePlayerType}, blackPlayerType={blackPlayerType}");
            return;
        }

        // Double-check game state - use immediate check to avoid cache delays
        Debug.Log($"🔍 TurnManager.TriggerAIMove: Checking game state...");
        bool gameStateValid = GameStateManager.Instance != null && GameStateManager.Instance.CanMovePiecesImmediate();
        Debug.Log($"🔍 TurnManager.TriggerAIMove: GameStateManager exists: {GameStateManager.Instance != null}");
        Debug.Log($"🔍 TurnManager.TriggerAIMove: CanMovePiecesImmediate: {GameStateManager.Instance?.CanMovePiecesImmediate()}");
        Debug.Log($"🔍 TurnManager.TriggerAIMove: Game state valid: {gameStateValid}");

        if (!gameStateValid)
        {
            Debug.LogError($"🚨 TurnManager: Cannot trigger AI move - invalid game state: {GameStateManager.Instance?.currentState}");
            Debug.LogError($"🚨 TurnManager: Animation state check - Chaos: {ChaosRotationManager.Instance?.IsChaosAnimationInProgress()}, Pieces: {AnyPiecesStillAnimating()}");
            return;
        }

        Debug.Log($"🎯 TurnManager: Triggering AI move for {currentPlayer} (validated as AI player)");
        Debug.Log($"🔍 TurnManager.TriggerAIMove: Checking AIPlayer.Instance...");
        Debug.Log($"🔍 TurnManager.TriggerAIMove: AIPlayer.Instance exists: {AIPlayer.Instance != null}");

        // Request move from AI player
        if (AIPlayer.Instance != null)
        {
            Debug.Log($"🔍 TurnManager.TriggerAIMove: Calling AIPlayer.Instance.RequestMove({currentPlayer})...");
            AIPlayer.Instance.RequestMove(currentPlayer);
            Debug.Log($"🔍 TurnManager.TriggerAIMove: AIPlayer.Instance.RequestMove() call completed");
        }
        else
        {
            Debug.LogError("TurnManager: AIPlayer.Instance is null, cannot trigger AI move");
        }

        Debug.Log($"🔍 TurnManager.TriggerAIMove: COMPLETED");
    }
    
    /// <summary>
    /// Trigger an AI placement for the specified player (if they are AI)
    /// </summary>
    public void TriggerAIPlacement(PieceColor playerColor)
    {
        if (!IsPlayerAI(playerColor))
        {
            Debug.LogWarning($"TurnManager: Attempted to trigger AI placement for human player {playerColor}");
            return;
        }
        
        Debug.Log($"TurnManager: Triggering AI placement for {playerColor}");
        
        // Request placement from AI player
        if (AIPlayer.Instance != null)
        {
            AIPlayer.Instance.RequestPlacement(playerColor);
        }
        else
        {
            Debug.LogError("TurnManager: AIPlayer.Instance is null, cannot trigger AI placement");
        }
    }
    
    /// <summary>
    /// Set the initial check states from post-placement evaluation
    /// Called by PlacementManager after all pieces are placed
    /// MULTI-PLAYER SUPPORT: Accepts check states for all active players (2-6 players)
    /// </summary>
    public void SetInitialCheckStates(System.Collections.Generic.Dictionary<PieceColor, bool> playerCheckStates)
    {
        // Clear previous check states
        initialWhiteInCheck = false;
        initialBlackInCheck = false;
        bothPlayersStartInCheck = false;
        startedInCheck.Clear();
        initialCheckStates = new System.Collections.Generic.Dictionary<PieceColor, bool>(playerCheckStates);

        // Extract White and Black check states for backwards compatibility
        if (playerCheckStates.ContainsKey(PieceColor.White))
        {
            initialWhiteInCheck = playerCheckStates[PieceColor.White];
        }

        if (playerCheckStates.ContainsKey(PieceColor.Black))
        {
            initialBlackInCheck = playerCheckStates[PieceColor.Black];
        }

        // For 2-player games, check if both players start in check
        if (playerCheckStates.Count == 2 && playerCheckStates.ContainsKey(PieceColor.White) && playerCheckStates.ContainsKey(PieceColor.Black))
        {
            bothPlayersStartInCheck = initialWhiteInCheck && initialBlackInCheck;
        }

        // Log all player check states
        Debug.Log($"🔄 TurnManager: Initial check states set for {playerCheckStates.Count} players:");
        foreach (var kvp in playerCheckStates)
        {
            Debug.Log($"🔄   {kvp.Key}: {(kvp.Value ? "IN CHECK" : "safe")}");
        }

        if (bothPlayersStartInCheck)
        {
            Debug.Log("🔄 ⚠️ TurnManager: Both White and Black start in check - king capture protection active");
        }

        // Determine opening turn order based on check states
        DetermineOpeningTurnOrder();
    }

    /// <summary>
    /// Set the initial check states from post-placement evaluation (legacy 2-player version)
    /// DEPRECATED: Use SetInitialCheckStates(Dictionary) for multi-player support
    /// Maintained for backwards compatibility with 2-player games
    /// </summary>
    [System.Obsolete("Use SetInitialCheckStates(Dictionary<PieceColor, bool>) for multi-player support")]
    public void SetInitialCheckStates(bool whiteInCheck, bool blackInCheck)
    {
        // Convert to dictionary format and call new version
        var checkStates = new System.Collections.Generic.Dictionary<PieceColor, bool>
        {
            { PieceColor.White, whiteInCheck },
            { PieceColor.Black, blackInCheck }
        };

        SetInitialCheckStates(checkStates);
    }
    
    /// <summary>
    /// Determine who should take the first turn based on initial check states
    /// MULTI-PLAYER SUPPORT: For 2-player games, uses White/Black check states. For multi-player, defaults to first player in rotation.
    /// </summary>
    private void DetermineOpeningTurnOrder()
    {
        PieceColor previousPlayer = currentPlayer;
        PieceColor startingPlayer;
        string reason;

        Debug.Log($"🔄 TurnManager: DetermineOpeningTurnOrder - Initial check states: White={initialWhiteInCheck}, Black={initialBlackInCheck}");

        // MULTI-PLAYER FIX: Get all active players from PlayerManager
        System.Collections.Generic.List<PieceColor> activePlayers = new System.Collections.Generic.List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetActivePlayers();
            Debug.Log($"🔄 TurnManager: PlayerManager found {activePlayers.Count} active players");
        }
        else
        {
            // Fallback to 2-player mode
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
            Debug.Log($"🔄 TurnManager: PlayerManager not available, using 2-player fallback");
        }

        // For 2-player games, use the original White/Black check-based logic
        if (activePlayers.Count == 2 && activePlayers.Contains(PieceColor.White) && activePlayers.Contains(PieceColor.Black))
        {
            Debug.Log("🔄 TurnManager: Using 2-player turn order logic based on check states");

            if (!initialWhiteInCheck && !initialBlackInCheck)
            {
                // Neither in check - ALWAYS White starts (standard chess rule)
                startingPlayer = PieceColor.White;
                reason = "standard opening (neither player in check) - White moves first";
            }
            else if (initialWhiteInCheck && !initialBlackInCheck)
            {
                // White only in check - White must get out of check first
                startingPlayer = PieceColor.White;
                reason = "White in check and must resolve it";
            }
            else if (!initialWhiteInCheck && initialBlackInCheck)
            {
                // Black only in check - Black gets first turn to resolve check
                startingPlayer = PieceColor.Black;
                reason = "Black in check and gets first turn to resolve it";
            }
            else
            {
                // Both in check - White starts but cannot capture king
                startingPlayer = PieceColor.White;
                reason = "both players in check (White starts but cannot capture king)";
            }
        }
        else
        {
            // MULTI-PLAYER: White starts, unless someone was placed into check: then the first player
            // in turn order who is in check moves first to resolve it. Until every player who started
            // in check has had a turn, no king can be taken (ShouldBlockKingCapture).
            var order = new System.Collections.Generic.List<PieceColor>(activePlayers);
            order.Sort();
            startedInCheck.Clear();
            foreach (PieceColor c in order)
                if (initialCheckStates.TryGetValue(c, out bool inCheck) && inCheck) startedInCheck.Add(c);
            startingPlayer = startedInCheck.Count > 0 ? startedInCheck[0] : order[0];
            reason = $"multi-player game - {(startedInCheck.Count > 0 ? "first player in check" : "first player in rotation")}";
            Debug.Log($"🔄 TurnManager: Multi-player mode - {activePlayers.Count} players, first is {startingPlayer}");
        }

        Debug.Log($"🔄 TurnManager: Opening turn order determined - {startingPlayer} starts first ({reason})");
        Debug.Log($"🔄 TurnManager: Player type check - {startingPlayer} is AI: {IsPlayerAI(startingPlayer)}");

        // CRITICAL FIX: Set the current player and validate
        PieceColor oldCurrentPlayer = currentPlayer;
        currentPlayer = startingPlayer;

        // Validation logging
        if (oldCurrentPlayer != currentPlayer)
        {
            Debug.Log($"🔄 TurnManager: Current player changed from {oldCurrentPlayer} to {currentPlayer}");
        }
        else
        {
            Debug.Log($"🔄 TurnManager: Current player confirmed as {currentPlayer}");
        }

        // Log special conditions
        if (bothPlayersStartInCheck)
        {
            Debug.Log("🔄 ⚠️ TurnManager: SPECIAL CONDITION - Both players start in check, king capture protection active");
        }

        if (initialWhiteInCheck)
        {
            Debug.Log("🔄 ⚠️ TurnManager: White must resolve check before ending turn");
        }

        if (initialBlackInCheck)
        {
            Debug.Log("🔄 ⚠️ TurnManager: Black must resolve check before ending turn");
        }

        // Final validation
        Debug.Log($"🔄 TurnManager: Turn order establishment COMPLETE - Starting player: {currentPlayer}");
    }
    
    // ===== ANTI-REPETITION SYSTEM =====
    
    /// <summary>
    /// Record the current board position in game history for repetition detection
    /// </summary>
    private void RecordCurrentPosition()
    {
        if (ChessBoard.Instance == null) return;
        
        string positionHash = GeneratePositionHash();
        gameHistory.Add(positionHash);
        
        // Limit history size to prevent memory bloat
        if (gameHistory.Count > MAX_HISTORY_LENGTH)
        {
            gameHistory.RemoveAt(0); // Remove oldest position
        }
        
        int repetitionCount = CountPositionRepetitions(positionHash);
        
        if (repetitionCount >= 2)
        {
            Debug.LogWarning($"🔄 TurnManager: Position repeated {repetitionCount} times - potential loop detected");
        }
        
        if (repetitionCount >= REPETITION_DRAW_THRESHOLD)
        {
            Debug.Log($"🏁 TurnManager: 3-fold repetition detected - triggering draw");
            if (GameEndDetectionManager.Instance != null)
            {
                GameEndDetectionManager.Instance.HandleDrawByRepetition();
            }
        }
    }
    
    /// <summary>
    /// Generate a hash string representing the current board position
    /// </summary>
    private string GeneratePositionHash()
    {
        if (ChessBoard.Instance == null) return "empty";

        System.Text.StringBuilder hashBuilder = new System.Text.StringBuilder();

        // Include current player in hash (same position but different player = different hash)
        hashBuilder.Append(currentPlayer == PieceColor.White ? "W:" : "B:");

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Generate hash based on piece positions
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
                        // Format: position|color|type
                        hashBuilder.Append($"{x}{y}{z}|{piece.pieceColor}|{piece.pieceType}|");
                    }
                }
            }
        }
        
        return hashBuilder.ToString();
    }
    
    /// <summary>
    /// Count how many times a specific position has occurred in game history
    /// </summary>
    private int CountPositionRepetitions(string positionHash)
    {
        int count = 0;
        foreach (string hash in gameHistory)
        {
            if (hash == positionHash)
            {
                count++;
            }
        }
        return count;
    }
    
    /// <summary>
    /// Get current game history for external access (e.g., AI evaluation)
    /// </summary>
    public System.Collections.Generic.List<string> GetGameHistory()
    {
        return new System.Collections.Generic.List<string>(gameHistory);
    }
    
    /// <summary>
    /// Check if a specific position hash would create a repetition
    /// </summary>
    public int GetRepetitionCount(string positionHash)
    {
        return CountPositionRepetitions(positionHash);
    }
    
    /// <summary>
    /// Clear game history (for new games)
    /// </summary>
    public void ClearGameHistory()
    {
        gameHistory.Clear();
        Debug.Log("🔄 TurnManager: Game history cleared for new game");
    }
    
    /// <summary>
    /// Check if any pieces on the board are still animating (used by GameStateManager and internally)
    /// </summary>
    public bool AnyPiecesStillAnimating()
    {
        return AnyPiecesStillAnimatingInternal();
    }
    
    /// <summary>
    /// Internal implementation to check if any pieces on the board are still animating
    /// </summary>
    private bool AnyPiecesStillAnimatingInternal()
    {
        if (ChessBoard.Instance == null) return false;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Check all positions on the board for animating pieces
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition position = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(position);

                    if (piece != null && piece.IsMoving)
                    {
                        Debug.Log($"🎬 TurnManager: Found animating piece - {piece.pieceColor} {piece.pieceType} at {position}");
                        lastAnimatingPiece = piece;
                        return true;
                    }
                }
            }
        }

        return false;
    }
    
    /// <summary>
    /// Subscribe to animation completion events to trigger delayed AI moves
    /// </summary>
    private void SubscribeToAnimationCompletion()
    {
        ChessPiece.OnMoveAnimationComplete += OnAnimationCompleted;
        Debug.Log($"🎬 TurnManager: Subscribed to animation completion events for {pendingAIPlayer} AI move");
    }
    
    /// <summary>
    /// Handle animation completion event
    /// </summary>
    private void OnAnimationCompleted(ChessPiece animatedPiece)
    {
        if (!isWaitingForAnimationCompletion) 
        {
            Debug.Log($"🎬 TurnManager: Ignoring animation completion - not waiting for animations");
            return;
        }
        
        Debug.Log($"🎬 TurnManager: Animation completed for {animatedPiece.pieceColor} {animatedPiece.pieceType}");
        Debug.Log($"🔍 TurnManager: State check - isWaitingForAnimationCompletion: {isWaitingForAnimationCompletion}");
        Debug.Log($"🔍 TurnManager: State check - pendingAIPlayer: {pendingAIPlayer}");
        Debug.Log($"🔍 TurnManager: State check - currentPlayer: {currentPlayer}");
        Debug.Log($"🔍 TurnManager: State check - IsCurrentPlayerAI(): {IsCurrentPlayerAI()}");
        
        // Double-check that no pieces are still animating
        if (!AnyPiecesStillAnimating())
        {
            Debug.Log($"🎬 TurnManager: All animations complete, preparing to trigger delayed AI move");
            Debug.Log($"🔍 TurnManager: Final validation - pendingAIPlayer == currentPlayer: {pendingAIPlayer == currentPlayer}");
            Debug.Log($"🔍 TurnManager: Final validation - IsCurrentPlayerAI(): {IsCurrentPlayerAI()}");
            
            // Clean up BEFORE checking conditions to avoid state confusion
            PieceColor savedPendingAI = pendingAIPlayer;
            bool wasWaitingForAnimation = isWaitingForAnimationCompletion;
            CleanupAnimationWaiting();
            
            // LOGIC FIX: The problem is that we're checking conditions AFTER cleanup
            // When Black AI's turn starts, pendingAIPlayer was set to Black but we cleaned it up
            // So we need to use savedPendingAI consistently
            bool shouldTriggerAI = IsCurrentPlayerAI() && currentPlayer == savedPendingAI && wasWaitingForAnimation;
            Debug.Log($"🔍 TurnManager: Should trigger AI move: {shouldTriggerAI}");
            Debug.Log($"🔍 TurnManager: Breakdown - IsCurrentPlayerAI: {IsCurrentPlayerAI()}, currentPlayer==savedPendingAI: {currentPlayer == savedPendingAI}, wasWaiting: {wasWaitingForAnimation}");
            
            // CRITICAL FIX: Also validate that we're still in the playing state
            // Use immediate check to avoid cache delays after animation completion
            bool canMovePieces = GameStateManager.Instance != null && GameStateManager.Instance.CanMovePiecesImmediate();
            Debug.Log($"🔍 TurnManager: Can move pieces (immediate check): {canMovePieces}");
            
            if (shouldTriggerAI && canMovePieces)
            {
                Debug.Log($"✅ TurnManager: Triggering delayed AI move for {currentPlayer}");
                TriggerAIMove();
            }
            else
            {
                Debug.LogError($"❌ TurnManager: Failed to trigger AI move");
                Debug.LogError($"   currentPlayer: {currentPlayer}, savedPendingAI: {savedPendingAI}");
                Debug.LogError($"   IsCurrentPlayerAI: {IsCurrentPlayerAI()}, wasWaiting: {wasWaitingForAnimation}");
                Debug.LogError($"   canMovePieces: {canMovePieces}");
                Debug.LogError($"   shouldTriggerAI: {shouldTriggerAI}");
                
                // FALLBACK: If AI should move but conditions failed, try direct trigger after small delay
                if (IsCurrentPlayerAI() && canMovePieces)
                {
                    Debug.LogWarning($"🔧 TurnManager: Attempting fallback AI trigger for {currentPlayer}");
                    StartCoroutine(FallbackAITrigger(currentPlayer, 0.1f));
                }
            }
        }
        else
        {
            Debug.Log($"🎬 TurnManager: Animation completed but other pieces still animating, continuing to wait");
        }
    }
    
    /// <summary>
    /// Clean up animation waiting state and unsubscribe from events
    /// </summary>
    private void CleanupAnimationWaiting()
    {
        isWaitingForAnimationCompletion = false;
        pendingAIPlayer = PieceColor.White; // Reset to default
        lastAnimatingPiece = null;
        
        // Unsubscribe from animation events to prevent memory leaks
        ChessPiece.OnMoveAnimationComplete -= OnAnimationCompleted;
        Debug.Log($"🎬 TurnManager: Cleaned up animation waiting state and unsubscribed from events");
    }
    
    /// <summary>
    /// Delayed initial AI move to ensure proper turn order establishment
    /// </summary>
    private System.Collections.IEnumerator DelayedInitialAIMove(PieceColor playerColor, float delay)
    {
        Debug.Log($"🔄 TurnManager.DelayedInitialAIMove: Waiting {delay}s to ensure turn order is properly established for {playerColor}");
        yield return new UnityEngine.WaitForSeconds(delay);
        
        // Validate that this player should still move first  
        if (GetCurrentPlayer() == playerColor && IsCurrentPlayerAI() && 
            GameStateManager.Instance != null && GameStateManager.Instance.CanMovePiecesImmediate())
        {
            Debug.Log($"✅ TurnManager.DelayedInitialAIMove: Turn order confirmed, triggering initial AI move for {playerColor}");
            Debug.Log($"✅ Final validation: currentPlayer={currentPlayer}, IsAI={IsCurrentPlayerAI()}, CanMove={GameStateManager.Instance.CanMovePiecesImmediate()}");
            TriggerAIMove();
        }
        else
        {
            Debug.LogError($"❌ TurnManager.DelayedInitialAIMove: Turn order validation failed for {playerColor}");
            Debug.LogError($"   Expected player: {playerColor}, Current player: {GetCurrentPlayer()}");
            Debug.LogError($"   IsAI: {IsCurrentPlayerAI()}, CanMovePieces: {GameStateManager.Instance?.CanMovePiecesImmediate()}");
        }
    }
    
    /// <summary>
    /// Fallback mechanism to trigger AI move if normal flow fails
    /// </summary>
    private System.Collections.IEnumerator FallbackAITrigger(PieceColor playerColor, float delay)
    {
        Debug.Log($"🔧 TurnManager.FallbackAITrigger: Waiting {delay}s before fallback trigger for {playerColor}");
        yield return new UnityEngine.WaitForSeconds(delay);
        
        // Re-validate conditions after delay
        if (GetCurrentPlayer() == playerColor && IsCurrentPlayerAI() && 
            GameStateManager.Instance != null && GameStateManager.Instance.CanMovePiecesImmediate())
        {
            Debug.Log($"✅ TurnManager.FallbackAITrigger: Conditions still valid, triggering AI move for {playerColor}");
            TriggerAIMove();
        }
        else
        {
            Debug.LogError($"❌ TurnManager.FallbackAITrigger: Conditions no longer valid for {playerColor}");
            Debug.LogError($"   currentPlayer: {GetCurrentPlayer()}, IsAI: {IsCurrentPlayerAI()}");
            Debug.LogError($"   CanMovePieces: {GameStateManager.Instance?.CanMovePiecesImmediate()}");
        }
    }
    
    /// <summary>
    /// Check if the current player is AI and needs to respond to a pending draw offer
    /// </summary>
    private void CheckForPendingAIDrawResponse()
    {
        // Only check for AI players
        if (!IsCurrentPlayerAI()) return;
        
        // Check if there's a pending draw offer
        if (GameEndDetectionManager.Instance == null || !GameEndDetectionManager.Instance.IsDrawOfferPending())
        {
            return;
        }
        
        PieceColor drawOfferingPlayer = GameEndDetectionManager.Instance.GetDrawOfferingPlayer();
        
        // Only respond if the AI is the correct responder (not the one who offered)
        if (drawOfferingPlayer != currentPlayer)
        {
            Debug.Log($"🤝 TurnManager: AI player {currentPlayer} needs to respond to draw offer from {drawOfferingPlayer}");
            
            if (AIPlayer.Instance != null)
            {
                AIPlayer.Instance.HandleDrawOfferResponse(currentPlayer);
            }
            else
            {
                Debug.LogError("TurnManager: AIPlayer.Instance is null - cannot handle AI draw response");
            }
        }
    }
    
    // ===== FORFEIT AND DRAW INTEGRATION ====="
    
    /// <summary>
    /// Request forfeit for the current player (if they are human)
    /// </summary>
    public void RequestForfeit()
    {
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            Debug.LogWarning("TurnManager: Cannot forfeit - not in playing state");
            return;
        }
        
        PieceColor currentPlayer = GetCurrentPlayer();
        
        // Allow both human and AI players to forfeit
        // AI forfeit decisions will be made by AIPlayer class
        if (IsPlayerAI(currentPlayer))
        {
            Debug.Log($"TurnManager: AI player {currentPlayer} forfeit request processed");
        }
        
        Debug.Log($"TurnManager: Forwarding forfeit request from {currentPlayer} to GameEndDetectionManager");
        
        // Forward to GameEndDetectionManager
        if (GameEndDetectionManager.Instance != null)
        {
            GameEndDetectionManager.Instance.HandleForfeit(currentPlayer);
        }
        else
        {
            Debug.LogError("TurnManager: Cannot forfeit - GameEndDetectionManager.Instance is null");
        }
    }
    
    /// <summary>
    /// Request draw offer for the current player (if they are human and it's their turn)
    /// </summary>
    public void RequestDrawOffer()
    {
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            Debug.LogWarning("TurnManager: Cannot offer draw - not in playing state");
            return;
        }
        
        PieceColor currentPlayer = GetCurrentPlayer();
        
        // Allow both human and AI players to offer draws
        // AI draw decisions will be made by AIPlayer class
        if (IsPlayerAI(currentPlayer))
        {
            Debug.Log($"TurnManager: AI player {currentPlayer} draw offer request processed");
        }
        
        // Check if there's already a pending draw offer
        if (GameEndDetectionManager.Instance != null && GameEndDetectionManager.Instance.IsDrawOfferPending())
        {
            Debug.LogWarning($"TurnManager: {currentPlayer} cannot offer draw - another offer is pending");
            return;
        }
        
        Debug.Log($"TurnManager: Forwarding draw offer from {currentPlayer} to GameEndDetectionManager");
        
        // Forward to GameEndDetectionManager
        if (GameEndDetectionManager.Instance != null)
        {
            GameEndDetectionManager.Instance.HandleDrawOffer(currentPlayer);
        }
        else
        {
            Debug.LogError("TurnManager: Cannot offer draw - GameEndDetectionManager.Instance is null");
        }
    }
    
    /// <summary>
    /// Respond to a pending draw offer (accept or decline)
    /// </summary>
    /// <param name="accept">True to accept, false to decline</param>
    public void RespondToDrawOffer(bool accept)
    {
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            Debug.LogWarning("TurnManager: Cannot respond to draw - not in playing state");
            return;
        }
        
        if (GameEndDetectionManager.Instance == null)
        {
            Debug.LogError("TurnManager: Cannot respond to draw - GameEndDetectionManager.Instance is null");
            return;
        }
        
        if (!GameEndDetectionManager.Instance.IsDrawOfferPending())
        {
            Debug.LogWarning("TurnManager: Cannot respond to draw - no offer is pending");
            return;
        }
        
        PieceColor drawOfferingPlayer = GameEndDetectionManager.Instance.GetDrawOfferingPlayer();
        PieceColor respondingPlayer = DrawResponder(drawOfferingPlayer);

        // Allow both human and AI players to respond to draws
        // For AI players, the response decision should come from AIPlayer class
        if (IsPlayerAI(respondingPlayer))
        {
            Debug.Log($"TurnManager: AI player {respondingPlayer} draw response processed");
        }
        
        Debug.Log($"TurnManager: Forwarding draw response ({(accept ? "ACCEPT" : "DECLINE")}) from {respondingPlayer} to GameEndDetectionManager");
        
        GameEndDetectionManager.Instance.HandleDrawResponse(respondingPlayer, accept);
    }
    
    /// <summary>
    /// Check if the current player can forfeit (both human and AI in playing state)
    /// </summary>
    public bool CanCurrentPlayerForfeit()
    {
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            return false;
        }
        
        // Both human and AI players can forfeit now
        return true;
    }
    
    /// <summary>
    /// Check if the current player can offer a draw (both human and AI, their turn, no pending offer)
    /// </summary>
    public bool CanCurrentPlayerOfferDraw()
    {
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            return false;
        }
        
        // Both human and AI players can offer draws now
        // No pending draw offer
        if (GameEndDetectionManager.Instance != null && GameEndDetectionManager.Instance.IsDrawOfferPending())
        {
            return false;
        }

        if (ZombieMode.Active) return false; // you can't make peace with zombies

        // Online: only on your own turn, for your own side
        if (OnlineSession.IsActive && (!OnlineSession.IsLocal(currentPlayer) || IsPlayerAI(currentPlayer))) return false;

        return true;
    }
    
    /// <summary>
    /// Check if the current player can respond to a draw offer
    /// </summary>
    public bool CanCurrentPlayerRespondToDraw()
    {
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            return false;
        }
        
        if (GameEndDetectionManager.Instance == null || !GameEndDetectionManager.Instance.IsDrawOfferPending())
        {
            return false;
        }
        
        PieceColor drawOfferingPlayer = GameEndDetectionManager.Instance.GetDrawOfferingPlayer();
        PieceColor respondingPlayer = DrawResponder(drawOfferingPlayer);

        // Must be human player and the correct responder (not the one who offered)
        return !IsPlayerAI(respondingPlayer) && respondingPlayer != drawOfferingPlayer && OnlineSession.CanControl(respondingPlayer);
    }

    /// <summary>
    /// Who answers a draw offer: online, the player on this device; otherwise the offerer's
    /// opponent (draws are a two-player option, like the check in GameEndDetectionManager)
    /// </summary>
    private static PieceColor DrawResponder(PieceColor offeringPlayer)
    {
        if (OnlineSession.IsActive) return OnlineSession.LocalColor;
        return offeringPlayer == PieceColor.White ? PieceColor.Black : PieceColor.White;
    }
}