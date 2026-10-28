using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI difficulty levels that control search depth and thinking time
/// PERFORMANCE FIX: Reduced depths for 3D chess complexity
/// </summary>
public enum AIDifficulty
{
    Easy = 1,      // Depth 1, fast moves
    Medium = 2,    // Depth 2, moderate thinking  
    Hard = 3       // Depth 3, deeper analysis
}

/// <summary>
/// Manages AI player behavior, move calculation, and difficulty settings.
/// Integrates with the existing turn management system to provide computer opponents.
/// </summary>
public class AIPlayer : MonoBehaviour
{
    [Header("AI Settings")]
    public AIDifficulty difficulty = AIDifficulty.Medium;
    public float thinkingTimeMin = 0.2f;  // Minimum thinking time for realism (reduced for performance)
    public float thinkingTimeMax = 1.0f;  // Maximum thinking time (reduced for performance)
    public bool showThinkingIndicator = true;
    
    [Header("Placement Settings")]
    public float placementThinkingTimeMin = 0.5f;  // Faster thinking for placement
    public float placementThinkingTimeMax = 2.0f;  // Shorter max time for placement
    
    [Header("Debug")]
    public bool enableDebugLogging = true;
    public bool showEvaluationScores = false;
    
    private bool isThinking = false;
    private bool isPlacing = false;  // Track if currently in placement phase
    private Coroutine currentThinkingCoroutine;
    private MinimaxEngine minimaxEngine;
    private AIPlacementEngine placementEngine;
    
    // QUEUE FIX: Handle sequential AI placement requests
    private System.Collections.Generic.Queue<PieceColor> placementQueue = new System.Collections.Generic.Queue<PieceColor>();
    
    public static AIPlayer Instance { get; private set; }
    
    // Events
    public System.Action<PieceColor> OnAIThinkingStarted;
    public System.Action<PieceColor> OnAIThinkingFinished;
    public System.Action<PieceColor, BoardPosition, BoardPosition> OnAIMoveDecided;
    
    // Placement Events
    public System.Action<PieceColor> OnAIPlacementStarted;
    public System.Action<PieceColor> OnAIPlacementFinished;
    public System.Action<PieceColor, ChessPiece, BoardPosition> OnAIPlacementDecided;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            minimaxEngine = new MinimaxEngine(enableDebugLogging);
            placementEngine = new AIPlacementEngine(difficulty, enableDebugLogging);
            
            // Subscribe to game state changes to handle game end cleanup
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
            }
            
            Debug.Log("AIPlayer: Instance created with minimax and placement engines");
        }
        else
        {
            Debug.LogWarning("AIPlayer: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    /// <summary>
    /// Request the AI to make a move for the specified player color
    /// </summary>
    public void RequestMove(PieceColor playerColor)
    {
        if (ZombieMode.Active && playerColor == ZombieMode.ZombieColor)
        {
            if (ZombieDirector.Instance != null) ZombieDirector.Instance.TakeTurn();
            return;
        }
        if (isThinking)
        {
            Debug.LogWarning($"AIPlayer: Already thinking, ignoring move request for {playerColor}");
            return;
        }
        
        // Validate game state
        // CRITICAL FIX: Don't check CanMovePieces() as it may still show animations in progress
        // due to cache delays even after pieces have finished moving
        // Instead, check the state directly and let the move execution handle animation waiting
        if (GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing)
        {
            Debug.LogError("AIPlayer: Cannot make move - not in playing state");
            return;
        }
        
        // Validate it's the AI's turn
        if (TurnManager.Instance == null || TurnManager.Instance.GetCurrentPlayer() != playerColor)
        {
            Debug.LogError($"AIPlayer: Cannot make move - not {playerColor}'s turn");
            return;
        }
        
        // Validate this player is set to AI
        if (!TurnManager.Instance.IsPlayerAI(playerColor))
        {
            Debug.LogError($"AIPlayer: {playerColor} is not set as AI player");
            return;
        }
        
        Debug.Log($"🤖 AIPlayer: Move requested for {playerColor} at difficulty {difficulty}");
        
        // Start thinking process
        currentThinkingCoroutine = StartCoroutine(ThinkAndMoveCoroutine(playerColor));
    }
    
    /// <summary>
    /// Request the AI to place a piece for the specified player color during placement phase
    /// </summary>
    public void RequestPlacement(PieceColor playerColor)
    {
        Debug.Log($"🚀 AIPlayer.RequestPlacement: ENTRY - {playerColor} AI placement requested");
        Debug.Log($"🚀 Current AI state - isThinking: {isThinking}, isPlacing: {isPlacing}");
        
        if (isThinking || isPlacing)
        {
            Debug.Log($"🔄 AIPlayer: Already thinking/placing, queueing placement request for {playerColor}");
            Debug.Log($"  Current state - isThinking: {isThinking}, isPlacing: {isPlacing}");
            
            // QUEUE FIX: Instead of ignoring, add to queue
            placementQueue.Enqueue(playerColor);
            Debug.Log($"✅ AIPlayer: {playerColor} placement request queued. Queue size: {placementQueue.Count}");
            return;
        }
        
        // Enhanced diagnostic logging for game state validation
        Debug.Log($"🔍 AIPlayer: Validating game state for {playerColor} placement...");
        
        if (GameStateManager.Instance == null)
        {
            Debug.LogError("🚨 AIPlayer: GameStateManager.Instance is NULL - cannot validate game state");
            return;
        }
        
        GameState currentState = GameStateManager.Instance.currentState;
        bool canPlacePieces = GameStateManager.Instance.CanPlacePieces();
        Debug.Log($"🎮 AIPlayer: Current game state: {currentState}, CanPlacePieces: {canPlacePieces}");
        
        if (!canPlacePieces)
        {
            Debug.LogError($"🚨 AIPlayer: Cannot place pieces - not in placement phase (current: {currentState})");
            return;
        }
        
        // Enhanced player type validation
        Debug.Log($"🔍 AIPlayer: Validating {playerColor} is set as AI player...");
        
        if (TurnManager.Instance == null)
        {
            Debug.LogError("🚨 AIPlayer: TurnManager.Instance is NULL - cannot validate player type");
            return;
        }
        
        bool isAIPlayer = TurnManager.Instance.IsPlayerAI(playerColor);
        PlayerType playerType = TurnManager.Instance.GetPlayerType(playerColor);
        Debug.Log($"🎭 AIPlayer: {playerColor} player type: {playerType}, IsPlayerAI: {isAIPlayer}");
        
        if (!isAIPlayer)
        {
            Debug.LogError($"🚨 AIPlayer: {playerColor} is not set as AI player (type: {playerType})");
            return;
        }
        
        // Enhanced placement engine validation
        Debug.Log($"🔍 AIPlayer: Validating placement engine and piece availability...");
        
        if (placementEngine == null)
        {
            Debug.LogError("🚨 AIPlayer: placementEngine is NULL - cannot perform placement");
            return;
        }
        
        // Check piece tray availability using PieceTrayManager for scalability
        PieceTray playerTray = null;
        if (PieceTrayManager.Instance != null)
        {
            playerTray = PieceTrayManager.Instance.GetTray(playerColor);
            Debug.Log($"🔍 AIPlayer.RequestPlacement: Using PieceTrayManager.GetTray({playerColor}) - tray instance ID: {(playerTray != null ? playerTray.GetInstanceID().ToString() : "NULL")}");
        }
        else
        {
            // Fallback for 2-player games
            playerTray = (playerColor == PieceColor.White) ? PieceTray.WhiteTray : PieceTray.BlackTray;
        }

        if (playerTray == null)
        {
            Debug.LogError($"🚨 AIPlayer: {playerColor} tray is NULL - cannot access pieces for placement");
            return;
        }

        int availablePieces = playerTray.GetPieceCount();
        Debug.Log($"📦 AIPlayer: {playerColor} tray (instance {playerTray.GetInstanceID()}) has {availablePieces} pieces available for placement");
        
        if (availablePieces == 0)
        {
            Debug.LogError($"🚨 AIPlayer: {playerColor} tray has no pieces - cannot place anything");
            return;
        }
        
        // Final validation: Check PlacementManager availability
        if (PlacementManager.Instance == null)
        {
            Debug.LogError("🚨 AIPlayer: PlacementManager.Instance is NULL - cannot execute placement");
            return;
        }
        
        Debug.Log($"✅ AIPlayer: All validations passed! Starting {playerColor} AI placement at difficulty {difficulty}");
        Debug.Log($"🚀 AIPlayer: Initiating ThinkAndPlaceCoroutine for {playerColor}...");
        
        // Start placement thinking process
        try
        {
            currentThinkingCoroutine = StartCoroutine(ThinkAndPlaceCoroutine(playerColor));
            Debug.Log($"✅ AIPlayer: Successfully started ThinkAndPlaceCoroutine for {playerColor}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"🚨 AIPlayer: Failed to start ThinkAndPlaceCoroutine for {playerColor}: {e.Message}");
            Debug.LogError($"  Stack trace: {e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Main AI thinking and move execution coroutine
    /// </summary>
    private IEnumerator ThinkAndMoveCoroutine(PieceColor playerColor)
    {
        isThinking = true;
        OnAIThinkingStarted?.Invoke(playerColor);
        
        if (enableDebugLogging)
            Debug.Log($"🧠 AIPlayer: {playerColor} AI started thinking...");
        
        // DEFENSIVE: Ensure pieces are actually on the board before trying to move
        if (ChessBoard.Instance != null)
        {
            // Get dynamic board dimensions from BoardDimensionsManager
            Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
                ? BoardDimensionsManager.Instance.GetDimensions()
                : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

            int piecesOnBoard = 0;
            for (int x = 0; x < boardDimensions.x; x++)
            {
                for (int y = 0; y < boardDimensions.y; y++)
                {
                    for (int z = 0; z < boardDimensions.z; z++)
                    {
                        if (ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z)) != null)
                            piecesOnBoard++;
                    }
                }
            }

            if (piecesOnBoard == 0)
            {
                Debug.LogWarning($"🛡️ AIPlayer: No pieces found on board - game may still be in placement phase, aborting AI move");
                isThinking = false;
                OnAIThinkingFinished?.Invoke(playerColor);
                yield break;
            }

            if (enableDebugLogging)
                Debug.Log($"🛡️ AIPlayer: Found {piecesOnBoard} pieces on board, proceeding with AI move");
        }
        
        // Calculate the best move using minimax algorithm
        float thinkingStartTime = Time.time;
        AIMove bestMove = null;
        
        // Think for as long as the difficulty allows,
        // on a copy of the board in the background, so the screen keeps moving
        var search = new RobotBrain();
        int searchDepth = 0;
        yield return search.FindMoveRoutine(playerColor, RobotBrain.Settings.For(difficulty), m => bestMove = m);
        searchDepth = search.DepthReached;
        Debug.Log($"AIPlayer: {playerColor} ({difficulty}) chose {bestMove} after {search.Nodes} positions, {searchDepth} moves deep, {search.Seconds:F2}s");

        if (bestMove != null)
        {
            Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: bestMove details - piece={bestMove.piece?.pieceType}, from={bestMove.fromPosition}, to={bestMove.toPosition}, score={bestMove.evaluationScore:F2}");
        }

        if (enableDebugLogging && bestMove != null)
        {
            Debug.Log($"Search depth reached: {searchDepth}");
            if (showEvaluationScores)
                Debug.Log($"Best move evaluation: {bestMove.evaluationScore:F2}");
        }
        
        // Ensure minimum thinking time for realism
        float thinkingTime = Time.time - thinkingStartTime;
        float remainingThinkingTime = Mathf.Max(0, thinkingTimeMin - thinkingTime);

        Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: {playerColor} - About to yield for {remainingThinkingTime}s (min thinking time)");
        Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: {playerColor} - MonoBehaviour enabled={enabled}, GameObject active={gameObject != null && gameObject.activeInHierarchy}");

        if (remainingThinkingTime > 0)
        {
            Debug.Log($"⏰ AIPlayer.ThinkAndMoveCoroutine: {playerColor} - BEFORE yield WaitForSeconds({remainingThinkingTime}s)");
            yield return new WaitForSeconds(remainingThinkingTime);
            Debug.Log($"⏰ AIPlayer.ThinkAndMoveCoroutine: {playerColor} - AFTER yield WaitForSeconds - coroutine survived!");
        }
        else
        {
            Debug.Log($"⏰ AIPlayer.ThinkAndMoveCoroutine: {playerColor} - Skipping yield (remainingTime=0)");
        }

        Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: {playerColor} - Post-yield state check - enabled={enabled}, GameObject active={gameObject != null && gameObject.activeInHierarchy}");

        // Ensure we don't exceed maximum thinking time
        float totalThinkingTime = Time.time - thinkingStartTime;
        if (totalThinkingTime > thinkingTimeMax)
        {
            Debug.LogWarning($"AIPlayer: Thinking time exceeded maximum ({totalThinkingTime:F2}s > {thinkingTimeMax}s)");
        }

        Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: {playerColor} - Setting isThinking=false and firing OnAIThinkingFinished");
        isThinking = false;
        OnAIThinkingFinished?.Invoke(playerColor);
        Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: {playerColor} - OnAIThinkingFinished event fired successfully");

        Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: About to process bestMove - is null: {bestMove == null}");

        if (bestMove != null)
        {
            if (enableDebugLogging)
                Debug.Log($"🎯 AIPlayer: {playerColor} decided to move {bestMove.piece.pieceType} from {bestMove.fromPosition} to {bestMove.toPosition}");

            Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: Invoking OnAIMoveDecided event for {playerColor}");
            OnAIMoveDecided?.Invoke(playerColor, bestMove.fromPosition, bestMove.toPosition);
            Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: OnAIMoveDecided event invoked");

            // Execute the move
            Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: Calling ExecuteMove for {playerColor}...");
            ExecuteMove(bestMove);
            Debug.Log($"🔍 AIPlayer.ThinkAndMoveCoroutine: ExecuteMove call completed for {playerColor}");
            
            // PERFORMANCE: Clear minimax cache after move execution to prevent stale data
            if (minimaxEngine != null)
            {
                minimaxEngine.ClearCache();
            }
            
            // DRAW EVALUATION: Check if AI should offer a draw after making this move
            EvaluateDrawOfferAfterMove(playerColor);
        }
        else
        {
            Debug.LogError($"💀 AIPlayer: No valid moves found for {playerColor}!");
            Debug.LogError($"💀 This may indicate checkmate, stalemate, or a critical game state issue.");
            
            // CRITICAL: Notify game systems that AI cannot move
            Debug.LogError($"💀 AI is stuck and cannot continue. Game may need manual intervention.");
            
            // Check if this is a checkmate/stalemate situation
            if (CheckDetectionManager.Instance != null)
            {
                bool isInCheck = CheckDetectionManager.Instance.IsKingInCheck(playerColor);
                Debug.LogError($"💀 {playerColor} king in check: {isInCheck}");
                
                if (isInCheck)
                {
                    Debug.LogError($"💀 POTENTIAL CHECKMATE: {playerColor} king in check with no legal moves!");
                }
                else
                {
                    Debug.LogError($"💀 POTENTIAL STALEMATE: {playerColor} has no legal moves but king not in check!");
                }
            }
            
            // GAME END FIX: Trigger proper game end detection
            if (GameEndDetectionManager.Instance != null)
            {
                Debug.Log($"🏁 AIPlayer: Triggering game end detection for {playerColor} (no legal moves)");
                GameEndDetectionManager.Instance.CheckForGameEnd();
            }
            else
            {
                Debug.LogError($"💀 AIPlayer: GameEndDetectionManager.Instance is NULL - cannot handle game end!");
                Debug.LogError($"💀 Game will remain stuck. Manual intervention required.");
            }
        }
        
        currentThinkingCoroutine = null;
    }
    
    /// <summary>
    /// Once every human is ready, the robots still setting up place the rest of their pieces at once and say Ready
    /// </summary>
    public void FinishPlacementNow()
    {
        if (OnlineSession.IsActive || OnlineSession.Replaying) return;
        if (PlacementManager.Instance == null || TurnManager.Instance == null || PlayerManager.Instance == null) return;
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanPlacePieces()) return;

        var robots = new System.Collections.Generic.List<PieceColor>();
        foreach (PieceColor color in PlayerManager.Instance.GetActivePlayers())
        {
            if (PlacementManager.Instance.IsPlayerReady(color)) continue;
            if (!TurnManager.Instance.IsPlayerAI(color)) return; // a person is still setting up
            robots.Add(color);
        }
        if (robots.Count == 0) return;

        StopThinking();
        foreach (PieceColor color in robots)
        {
            for (int guard = 0; guard < 64 && ShouldContinuePlacement(color); guard++)
            {
                AIPlacement placement = placementEngine.FindBestPlacement(color);
                if (placement == null) break;
                ExecutePlacement(placement);
            }
            if (!ShouldContinuePlacement(color)) PlacementManager.Instance.SetPlayerReady(color, true);
            else RequestPlacement(color); // couldn't finish: carry on the usual way
        }
    }

    /// <summary>
    /// Main AI thinking and placement execution coroutine
    /// </summary>
    private IEnumerator ThinkAndPlaceCoroutine(PieceColor playerColor)
    {
        isPlacing = true;
        OnAIPlacementStarted?.Invoke(playerColor);
        
        if (enableDebugLogging)
            Debug.Log($"🧠 AIPlayer: {playerColor} AI started thinking about placement...");
        
        // Calculate the best placement using placement engine
        float thinkingStartTime = Time.time;
        AIPlacement bestPlacement = null;
        
        // Use placement engine to find the best placement
        bestPlacement = placementEngine.FindBestPlacement(playerColor);
        
        if (enableDebugLogging && bestPlacement != null)
        {
            Debug.Log($"AIPlacementEngine found placement: {bestPlacement}");
            if (showEvaluationScores)
                Debug.Log($"Best placement evaluation: {bestPlacement.evaluationScore:F2}");
        }
        
        // Ensure minimum thinking time for realism
        float thinkingTime = Time.time - thinkingStartTime;
        float remainingThinkingTime = Mathf.Max(0, placementThinkingTimeMin - thinkingTime);
        
        if (remainingThinkingTime > 0)
        {
            yield return new WaitForSeconds(remainingThinkingTime);
        }
        
        // Ensure we don't exceed maximum thinking time
        float totalThinkingTime = Time.time - thinkingStartTime;
        if (totalThinkingTime > placementThinkingTimeMax)
        {
            Debug.LogWarning($"AIPlayer: Placement thinking time exceeded maximum ({totalThinkingTime:F2}s > {placementThinkingTimeMax}s)");
        }
        
        isPlacing = false;
        OnAIPlacementFinished?.Invoke(playerColor);
        
        if (bestPlacement != null)
        {
            Debug.Log($"🎯 AIPlayer: {playerColor} decided to place {bestPlacement.piece.pieceType} at {bestPlacement.position}");
            Debug.Log($"  Evaluation score: {bestPlacement.evaluationScore:F2}");
            Debug.Log($"  About to fire OnAIPlacementDecided event...");
            
            OnAIPlacementDecided?.Invoke(playerColor, bestPlacement.piece, bestPlacement.position);
            Debug.Log($"✅ AIPlayer: OnAIPlacementDecided event fired for {playerColor}");
            
            // Execute the placement
            Debug.Log($"🎮 AIPlayer: Executing placement for {playerColor}...");
            try
            {
                ExecutePlacement(bestPlacement);
                Debug.Log($"✅ AIPlayer: Placement execution completed for {playerColor}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"🚨 AIPlayer: Placement execution FAILED for {playerColor}: {e.Message}");
                Debug.LogError($"  Stack trace: {e.StackTrace}");
            }
            
            // ASYNCHRONOUS PLACEMENT: Continue placing pieces until tray is empty
            if (ShouldContinuePlacement(playerColor))
            {
                Debug.Log($"🔄 AIPlayer: {playerColor} AI continuing placement - more pieces available");
                StartCoroutine(DelayedPlacementRequest(playerColor));
            }
            else
            {
                Debug.Log($"✅ AIPlayer: {playerColor} AI completed all placements");
                
                // AI AUTO-READINESS: When AI finishes placing all pieces, automatically set ready
                if (PlacementManager.Instance != null)
                {
                    Debug.Log($"🤖 AIPlayer: {playerColor} AI auto-setting ready state (tray empty)");
                    PlacementManager.Instance.SetPlayerReady(playerColor, true);
                }
                else
                {
                    Debug.LogError($"🤖 AIPlayer: Cannot set {playerColor} AI ready - PlacementManager.Instance is null");
                }
            }
        }
        else
        {
            Debug.LogError($"🚨 AIPlayer: No valid placements found for {playerColor}!");
            
            // ERROR RECOVERY: Try to diagnose and potentially retry
            Debug.LogError($"🔧 AIPlayer: Attempting error recovery for {playerColor} placement failure...");
            
            // Check if we can retry after a short delay
            if (GameStateManager.Instance != null && 
                GameStateManager.Instance.currentState == GameState.PiecePlacement)
            {
                Debug.LogError($"🔧 AIPlayer: Still in placement phase, will retry {playerColor} placement in 2 seconds...");
                StartCoroutine(DelayedRetryPlacement(playerColor, 2f));
            }
            else
            {
                Debug.LogError($"🚨 AIPlayer: Not in placement phase anymore - cannot retry {playerColor} placement");
            }
        }
        
        currentThinkingCoroutine = null;
        
        // QUEUE FIX: Process next queued placement request if any
        ProcessNextQueuedPlacement();
    }
    
    /// <summary>
    /// QUEUE FIX: Process the next queued placement request if any exist
    /// </summary>
    private void ProcessNextQueuedPlacement()
    {
        Debug.Log($"🔄 AIPlayer.ProcessNextQueuedPlacement: ENTRY - Queue size: {placementQueue.Count}");
        
        if (placementQueue.Count > 0)
        {
            PieceColor nextPlayer = placementQueue.Dequeue();
            Debug.Log($"🎯 AIPlayer.ProcessNextQueuedPlacement: Processing queued request for {nextPlayer}");
            Debug.Log($"  Remaining queue size: {placementQueue.Count}");
            
            // Start the next placement request
            RequestPlacement(nextPlayer);
        }
        else
        {
            Debug.Log($"✅ AIPlayer.ProcessNextQueuedPlacement: No queued requests - all AI placements complete");
        }
    }
    
    /// <summary>
    /// Execute the AI's chosen move
    /// </summary>
    private void ExecuteMove(AIMove move)
    {
        Debug.Log($"🔍 AIPlayer.ExecuteMove: ENTRY - called with move");
        Debug.Log($"🔍 AIPlayer.ExecuteMove: move is null: {move == null}");

        if (move == null || move.piece == null)
        {
            Debug.LogError($"🚨 AIPlayer.ExecuteMove: Cannot execute null move - move is null: {move == null}, piece is null: {move?.piece == null}");
            return;
        }

        Debug.Log($"🔍 AIPlayer.ExecuteMove: Move validation passed - piece={move.piece.pieceType}, from={move.fromPosition}, to={move.toPosition}");

        // Check if game is still active before executing move
        Debug.Log($"🔍 AIPlayer.ExecuteMove: Checking game state...");
        if (GameStateManager.Instance != null)
        {
            Debug.Log($"🔍 AIPlayer.ExecuteMove: GameStateManager.currentState = {GameStateManager.Instance.currentState}");
        }
        else
        {
            Debug.LogError($"🚨 AIPlayer.ExecuteMove: GameStateManager.Instance is NULL");
        }

        if (GameStateManager.Instance != null && GameStateManager.Instance.currentState != GameState.Playing)
        {
            Debug.Log($"🏁 AIPlayer.ExecuteMove: ABORTING - game no longer in Playing state (current: {GameStateManager.Instance.currentState})");
            return;
        }

        Debug.Log($"🔍 AIPlayer.ExecuteMove: Game state check passed - in Playing state");

        // Check if chaos animation is in progress before executing move
        Debug.Log($"🔍 AIPlayer.ExecuteMove: Checking if pieces can move (CanMovePieces)...");
        bool canMovePieces = GameStateManager.Instance != null && GameStateManager.Instance.CanMovePieces();
        Debug.Log($"🔍 AIPlayer.ExecuteMove: CanMovePieces = {canMovePieces}");

        if (GameStateManager.Instance != null && !GameStateManager.Instance.CanMovePieces())
        {
            Debug.LogWarning($"🌪️ AIPlayer.ExecuteMove: DELAYING - animations in progress, starting DelayedExecuteMove coroutine");
            StartCoroutine(DelayedExecuteMove(move));
            return;
        }

        Debug.Log($"🔍 AIPlayer.ExecuteMove: All validations passed - ready to execute move");
        Debug.Log($"🎮 AIPlayer.ExecuteMove: Executing move {move.piece.pieceType} {move.fromPosition} → {move.toPosition}");

        // Use the existing chess board move system
        Debug.Log($"🔍 AIPlayer.ExecuteMove: Checking ChessBoard.Instance...");
        if (ChessBoard.Instance != null)
        {
            Debug.Log($"🔍 AIPlayer.ExecuteMove: ChessBoard.Instance exists, calling MovePiece({move.fromPosition}, {move.toPosition})...");
            ChessBoard.Instance.MovePiece(move.fromPosition, move.toPosition);
            Debug.Log($"✅ AIPlayer.ExecuteMove: MovePiece call completed successfully");
        }
        else
        {
            Debug.LogError($"🚨 AIPlayer.ExecuteMove: ChessBoard.Instance is NULL - cannot execute move");
        }

        Debug.Log($"🔍 AIPlayer.ExecuteMove: EXIT");
    }
    
    /// <summary>
    /// Delay move execution until animations complete
    /// </summary>
    private IEnumerator DelayedExecuteMove(AIMove move)
    {
        // Wait for animations to complete
        while (GameStateManager.Instance != null && !GameStateManager.Instance.CanMovePieces())
        {
            // Check if game ended while waiting for animations
            if (GameStateManager.Instance.currentState != GameState.Playing)
            {
                Debug.Log($"🏁 AIPlayer: Cancelling delayed move execution - game no longer in Playing state (current: {GameStateManager.Instance.currentState})");
                yield break;
            }
            
            Debug.Log("🌪️ AIPlayer: Waiting for animations to complete before executing move...");
            yield return new WaitForSeconds(0.1f);
        }
        
        // Final check if game is still active before executing delayed move
        if (GameStateManager.Instance != null && GameStateManager.Instance.currentState != GameState.Playing)
        {
            Debug.Log($"🏁 AIPlayer: Cancelling delayed move execution - game ended during wait (current: {GameStateManager.Instance.currentState})");
            yield break;
        }
        
        // Execute the move once animations are done
        Debug.Log($"🎮 AIPlayer: Animations complete, executing delayed move {move.piece.pieceType} {move.fromPosition} → {move.toPosition}");
        
        if (ChessBoard.Instance != null)
        {
            ChessBoard.Instance.MovePiece(move.fromPosition, move.toPosition);
        }
        else
        {
            Debug.LogError("AIPlayer: ChessBoard.Instance is null, cannot execute delayed move");
        }
    }
    
    /// <summary>
    /// Execute the AI's chosen placement
    /// </summary>
    private void ExecutePlacement(AIPlacement placement)
    {
        Debug.Log($"🎯 AIPlayer.ExecutePlacement: ENTRY - executing AI placement decision");
        
        if (placement == null || placement.piece == null)
        {
            Debug.LogError("🚨 AIPlayer: Cannot execute null placement");
            Debug.LogError($"  placement is null: {placement == null}");
            Debug.LogError($"  placement.piece is null: {placement?.piece == null}");
            return;
        }
        
        Debug.Log($"🎮 AIPlayer: Executing placement {placement.piece.pieceColor} {placement.piece.pieceType} at {placement.position}");
        Debug.Log($"  Piece details: {placement.piece.name} (position: {placement.piece.CurrentPosition})");
        Debug.Log($"  Target position: {placement.position}");
        Debug.Log($"  Evaluation score: {placement.evaluationScore:F2}");
        
        // Validate PlacementManager before use
        if (PlacementManager.Instance == null)
        {
            Debug.LogError("🚨 AIPlayer: PlacementManager.Instance is null, cannot execute placement");
            return;
        }
        
        Debug.Log($"✅ AIPlayer: PlacementManager available, simulating placement process...");
        
        try
        {
            // Use the protected AI placement method that doesn't interfere with human selection
            Debug.Log($"🤖 AIPlayer: Using protected AI placement method to avoid human selection interference");
            PlacementManager.Instance.ExecuteAIPlacement(placement.piece, placement.position);
            Debug.Log($"🎉 AIPlayer: Successfully executed placement of {placement.piece.pieceColor} {placement.piece.pieceType} at {placement.position}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"🚨 AIPlayer: Exception during placement execution: {e.Message}");
            Debug.LogError($"  Stack trace: {e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Set the AI difficulty level
    /// </summary>
    public void SetDifficulty(AIDifficulty newDifficulty)
    {
        difficulty = newDifficulty;
        
        // Adjust thinking times based on difficulty (optimized for performance)
        switch (difficulty)
        {
            case AIDifficulty.Easy:
                thinkingTimeMin = 0.1f;
                thinkingTimeMax = 0.5f;
                break;
            case AIDifficulty.Medium:
                thinkingTimeMin = 0.2f;
                thinkingTimeMax = 1.0f;
                break;
            case AIDifficulty.Hard:
                thinkingTimeMin = 0.3f;
                thinkingTimeMax = 1.5f;
                break;
        }
        
        Debug.Log($"AIPlayer: Difficulty set to {difficulty} (thinking time: {thinkingTimeMin}-{thinkingTimeMax}s, depth: {GetSearchDepth()})");
        
        // Update minimax engine debug settings
        if (minimaxEngine != null)
        {
            minimaxEngine = new MinimaxEngine(enableDebugLogging);
        }
        
        // Update placement engine with new difficulty
        if (placementEngine != null)
        {
            placementEngine = new AIPlacementEngine(difficulty, enableDebugLogging);
        }
    }
    
    /// <summary>
    /// Get the current search depth based on difficulty (optimized for performance)
    /// ENHANCED: Increased depth slightly to help AI see aggressive opportunities
    /// </summary>
    public int GetSearchDepth()
    {
        // AGGRESSIVE: Increased depths slightly to see more attacking opportunities
        switch (difficulty)
        {
            case AIDifficulty.Easy:
                return 2;    // Increased from 1 for better play
            case AIDifficulty.Medium:
                return 2;    // Increased from 1 for better tactics
            case AIDifficulty.Hard:
                return 3;    // Increased from 2 for deeper analysis
            default:
                return 2;
        }
    }
    
    /// <summary>
    /// Get adaptive search depth based on position complexity for better performance
    /// PERFORMANCE OPTIMIZATION: Uses deeper search only when needed
    /// </summary>
    public int GetAdaptiveSearchDepth()
    {
        // Count total pieces on board to gauge position complexity
        int totalPieces = CountTotalPieces();
        
        // Base depth on difficulty
        int baseDepth = GetBaseSearchDepth();
        
        // PERFORMANCE OPTIMIZATION: Use lower depth for simple positions
        if (totalPieces <= 8) // Very few pieces remaining
        {
            return Mathf.Max(1, baseDepth - 1); // Use minimum depth
        }
        else if (totalPieces <= 16) // Mid-game
        {
            return baseDepth; // Use base depth
        }
        else // Opening/early game with many pieces
        {
            return Mathf.Max(1, baseDepth - 1); // Reduce depth to improve speed
        }
    }
    
    /// <summary>
    /// Get base search depth for adaptive calculation
    /// </summary>
    private int GetBaseSearchDepth()
    {
        switch (difficulty)
        {
            case AIDifficulty.Easy:
                return 2;    // Kept at 2 for tactical vision
            case AIDifficulty.Medium:
                return 2;    // Kept at 2 for balance
            case AIDifficulty.Hard:
                return 3;    // Kept at 3 for advanced play
            default:
                return 2;
        }
    }
    
    /// <summary>
    /// Count total pieces on board for position complexity assessment
    /// </summary>
    private int CountTotalPieces()
    {
        int count = 0;
        if (ChessBoard.Instance != null)
        {
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
                        if (ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z)) != null)
                        {
                            count++;
                        }
                    }
                }
            }
        }
        return count;
    }
    
    /// <summary>
    /// Check if the AI is currently thinking (move or placement)
    /// </summary>
    public bool IsThinking()
    {
        return isThinking || isPlacing;
    }
    
    /// <summary>
    /// Check if the AI is currently placing pieces
    /// </summary>
    public bool IsPlacing()
    {
        return isPlacing;
    }
    
    /// <summary>
    /// Stop the AI thinking process (for cancellation)
    /// </summary>
    public void StopThinking()
    {
        if (currentThinkingCoroutine != null)
        {
            StopCoroutine(currentThinkingCoroutine);
            currentThinkingCoroutine = null;
        }
        
        isThinking = false;
        isPlacing = false;
        
        // QUEUE FIX: Clear any queued placement requests when stopping
        if (placementQueue.Count > 0)
        {
            Debug.Log($"AIPlayer: Clearing {placementQueue.Count} queued placement requests");
            placementQueue.Clear();
        }
        
        Debug.Log("AIPlayer: Thinking/placement process stopped");
    }
    
    // === DRAW DECISION METHODS ===
    
    /// <summary>
    /// Check if AI should offer a draw based on current position
    /// Called after AI makes a move to evaluate if a draw offer is appropriate
    /// </summary>
    public bool ShouldOfferDraw(PieceColor aiPlayer)
    {
        // Validate game state and AI eligibility
        if (!CanParticipateInDraws(aiPlayer)) return false;
        
        // Use minimax engine's draw evaluation
        bool shouldOffer = minimaxEngine.ShouldOfferDraw(aiPlayer, difficulty);
        
        if (enableDebugLogging)
        {
            Debug.Log($"AIPlayer: {aiPlayer} draw offer evaluation = {shouldOffer} (difficulty: {difficulty})");
        }
        
        return shouldOffer;
    }
    
    /// <summary>
    /// Check if AI should accept an opponent's draw offer
    /// Called when AI needs to respond to a human or other AI's draw offer
    /// </summary>
    public bool ShouldAcceptDrawOffer(PieceColor aiPlayer)
    {
        // Validate game state and AI eligibility
        if (!CanParticipateInDraws(aiPlayer)) return false;
        
        // Use minimax engine's draw acceptance evaluation
        bool shouldAccept = minimaxEngine.ShouldAcceptDraw(aiPlayer, difficulty);
        
        if (enableDebugLogging)
        {
            Debug.Log($"AIPlayer: {aiPlayer} draw acceptance evaluation = {shouldAccept} (difficulty: {difficulty})");
        }
        
        return shouldAccept;
    }
    
    /// <summary>
    /// Evaluate if AI should offer a draw after making a move
    /// This is called automatically after AI moves to check for draw opportunities
    /// </summary>
    public void EvaluateDrawOfferAfterMove(PieceColor aiPlayer)
    {
        if (!CanParticipateInDraws(aiPlayer)) return;
        
        // Check if draw offer would be appropriate
        if (ShouldOfferDraw(aiPlayer))
        {
            StartCoroutine(DelayedDrawOffer(aiPlayer));
        }
    }
    
    /// <summary>
    /// Handle AI response to an opponent's draw offer
    /// This should be called when a draw offer is pending and it's the AI's turn to respond
    /// </summary>
    public void HandleDrawOfferResponse(PieceColor aiPlayer)
    {
        if (!CanParticipateInDraws(aiPlayer)) return;
        
        StartCoroutine(DelayedDrawResponse(aiPlayer));
    }
    
    /// <summary>
    /// Check if AI can participate in draw offers (validation)
    /// </summary>
    private bool CanParticipateInDraws(PieceColor aiPlayer)
    {
        // Must be in playing state
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePieces())
        {
            return false;
        }
        
        // Must be an AI player
        if (TurnManager.Instance == null || !TurnManager.Instance.IsPlayerAI(aiPlayer))
        {
            return false;
        }
        
        // Game end detection manager must be available
        if (GameEndDetectionManager.Instance == null)
        {
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Offer a draw after a short thinking delay for realism
    /// </summary>
    private System.Collections.IEnumerator DelayedDrawOffer(PieceColor aiPlayer)
    {
        // Realistic thinking time for draw decision
        float thinkingTime = Random.Range(1.0f, 3.0f);
        
        if (enableDebugLogging)
            Debug.Log($"AIPlayer: {aiPlayer} thinking about offering draw for {thinkingTime:F1}s...");
        
        yield return new WaitForSeconds(thinkingTime);
        
        // Double-check conditions after delay
        if (CanParticipateInDraws(aiPlayer) && GameEndDetectionManager.Instance != null)
        {
            Debug.Log($"🤝 AIPlayer: {aiPlayer} AI offering draw");
            GameEndDetectionManager.Instance.HandleDrawOffer(aiPlayer);
        }
    }
    
    /// <summary>
    /// Respond to a draw offer after a short thinking delay for realism
    /// </summary>
    private System.Collections.IEnumerator DelayedDrawResponse(PieceColor aiPlayer)
    {
        // Realistic thinking time for draw response
        float thinkingTime = Random.Range(1.5f, 4.0f);
        
        if (enableDebugLogging)
            Debug.Log($"AIPlayer: {aiPlayer} thinking about draw offer response for {thinkingTime:F1}s...");
        
        yield return new WaitForSeconds(thinkingTime);
        
        // Double-check conditions after delay
        if (CanParticipateInDraws(aiPlayer) && GameEndDetectionManager.Instance != null)
        {
            bool acceptDraw = ShouldAcceptDrawOffer(aiPlayer);
            
            if (acceptDraw)
            {
                Debug.Log($"✅ AIPlayer: {aiPlayer} AI accepting draw offer");
                GameEndDetectionManager.Instance.HandleDrawResponse(aiPlayer, true);
            }
            else
            {
                Debug.Log($"❌ AIPlayer: {aiPlayer} AI declining draw offer");
                GameEndDetectionManager.Instance.HandleDrawResponse(aiPlayer, false);
            }
        }
    }
    
    /// <summary>
    /// Check if AI should continue placing pieces (tray not empty)
    /// </summary>
    private bool ShouldContinuePlacement(PieceColor playerColor)
    {
        // Check if the player's tray still has pieces using PieceTrayManager for scalability
        PieceTray playerTray = null;
        if (PieceTrayManager.Instance != null)
        {
            playerTray = PieceTrayManager.Instance.GetTray(playerColor);
        }
        else
        {
            // Fallback for 2-player games
            playerTray = (playerColor == PieceColor.White) ? PieceTray.WhiteTray : PieceTray.BlackTray;
        }

        if (playerTray == null)
        {
            return false;
        }

        int remainingPieces = playerTray.GetPieceCount();
        Debug.Log($"AIPlayer: {playerColor} has {remainingPieces} pieces remaining in tray");

        return remainingPieces > 0;
    }
    
    /// <summary>
    /// Request another placement after a short delay (for realistic AI behavior)
    /// </summary>
    private System.Collections.IEnumerator DelayedPlacementRequest(PieceColor playerColor)
    {
        // Short delay between placements for realistic AI behavior
        float delay = Random.Range(0.5f, 1.5f);
        yield return new WaitForSeconds(delay);
        
        // Check if we're still in placement phase and tray isn't empty
        if (GameStateManager.Instance != null && 
            GameStateManager.Instance.currentState == GameState.PiecePlacement &&
            ShouldContinuePlacement(playerColor))
        {
            RequestPlacement(playerColor);
        }
    }
    
    /// <summary>
    /// Retry a failed AI placement after a delay (error recovery)
    /// </summary>
    private System.Collections.IEnumerator DelayedRetryPlacement(PieceColor playerColor, float delay)
    {
        Debug.Log($"🔧 AIPlayer.DelayedRetryPlacement: Waiting {delay} seconds before retrying {playerColor} placement...");
        yield return new WaitForSeconds(delay);
        
        // Check if we're still in placement phase
        if (GameStateManager.Instance != null && 
            GameStateManager.Instance.currentState == GameState.PiecePlacement)
        {
            Debug.Log($"🔧 AIPlayer.DelayedRetryPlacement: Retrying {playerColor} placement now...");
            RequestPlacement(playerColor);
        }
        else
        {
            Debug.LogError($"🚨 AIPlayer.DelayedRetryPlacement: Cannot retry {playerColor} placement - no longer in placement phase");
        }
    }
    
    /// <summary>
    /// Handle game state changes to clean up AI operations
    /// </summary>
    private void OnGameStateChanged(GameState newState)
    {
        Debug.Log($"🔄 AIPlayer: Game state changed to {newState}");

        // CACHE CLEARING: When transitioning to Playing state, clear minimax cache to prevent stale placement data
        if (newState == GameState.Playing)
        {
            Debug.Log("🧹 AIPlayer: Entering Playing state - clearing minimax cache to prevent stale data");
            if (minimaxEngine != null)
            {
                minimaxEngine.ClearCache();
                Debug.Log("✅ AIPlayer: Minimax cache cleared successfully");
            }
        }

        // Only stop AI operations when game truly ends (not during placement phase or active play)
        if (newState == GameState.GameOver || newState == GameState.WaitingForConfiguration)
        {
            Debug.Log("🏁 AIPlayer: Game ended - stopping all AI operations");

            // Stop current thinking coroutine if active
            if (currentThinkingCoroutine != null)
            {
                StopCoroutine(currentThinkingCoroutine);
                currentThinkingCoroutine = null;
                Debug.Log("🚫 AIPlayer: Stopped thinking coroutine");
            }

            // Reset AI state
            isThinking = false;
            isPlacing = false;

            // Clear placement queue
            placementQueue.Clear();
            Debug.Log("🧹 AIPlayer: Cleared placement queue and reset AI state");
        }
    }
    
    /// <summary>
    /// Clean up when AI player is destroyed
    /// </summary>
    private void OnDestroy()
    {
        // Unsubscribe from game state events
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
    }
}

/// <summary>
/// Represents a potential move the AI can make
/// </summary>
[System.Serializable]
public class AIMove
{
    public ChessPiece piece;
    public BoardPosition fromPosition;
    public BoardPosition toPosition;
    public float evaluationScore;
    
    public override string ToString()
    {
        return $"{piece?.pieceType} {fromPosition} → {toPosition} (score: {evaluationScore:F2})";
    }
}