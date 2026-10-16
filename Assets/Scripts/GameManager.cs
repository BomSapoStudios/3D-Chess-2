using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject kingPiecePrefab;
    public GameObject queenPiecePrefab;
    public GameObject bishopPiecePrefab;
    public GameObject knightPiecePrefab;
    public GameObject rookPiecePrefab;
    public GameObject pawnPiecePrefab;
    
    [Header("Game Settings")]
    public bool setupTestPieces = true;
    
    // State tracking to prevent multiple initialization attempts
    private bool traysCreated = false;
    
    private void Start()
    {
        InitializeGame();
    }
    
    private void InitializeGame()
    {
        Debug.Log("GameManager: Starting game initialization...");
        
        // Create game state management
        CreateGameStateManager();
        
        // Create ChessBoard for piece management alongside EmergencyChessBoard for grid visualization  
        CreateChessBoardInstance();
        Debug.Log("GameManager: ChessBoard created for piece management");
        
        // CRITICAL: Wait one frame for ChessBoard singleton to be fully initialized
        StartCoroutine(InitializeGameDelayed());
    }
    
    private System.Collections.IEnumerator InitializeGameDelayed()
    {
        Debug.Log("GameManager: Waiting one frame for ChessBoard to initialize...");
        yield return null; // Wait one frame
        
        // Verify ChessBoard.Instance is ready
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("GameManager: ChessBoard.Instance is still NULL after waiting! Cannot continue.");
            yield break;
        }
        
        Debug.Log("GameManager: ChessBoard.Instance confirmed ready, continuing initialization...");
        
        // DIAGNOSTIC: Create test input script first to verify Unity compilation
        Debug.Log("GameManager: Creating InputManagerTest for diagnostics...");
        GameObject testObject = new GameObject("InputManagerTest");
        testObject.AddComponent<InputManagerTest>();
        Debug.Log("GameManager: InputManagerTest created");
        
        // Ensure InputManager exists
        if (InputManager.Instance == null)
        {
            Debug.Log("GameManager: Creating Input Manager GameObject...");
            GameObject inputObject = new GameObject("Input Manager");
            inputObject.AddComponent<InputManager>();
            Debug.Log("GameManager: Input Manager created successfully");
        }
        else
        {
            Debug.Log("GameManager: Input Manager already exists");
        }
        
        // Set up camera target for board center
        SetupCameraTarget();
        
        // Set up rotation UI controls
        SetupRotationUI();
        
        // Clean up any existing King pieces BEFORE creating trays
        CleanUpExistingKingPieces();
        
        // CRITICAL: Wait for EmergencyChessBoard and Board Rotator to be created
        // The trays need to be children of Board Rotator to rotate properly with the board
        yield return null; // Wait one more frame for board structure to be complete
        
        // Create piece trays (they will automatically parent themselves to Board Rotator)
        Debug.Log("GameManager: About to call SetupPieceTrays()...");
        SetupPieceTrays();
        Debug.Log($"GameManager: SetupPieceTrays() completed. Tray status:");
        Debug.Log($"  WhiteTray: {(PieceTray.WhiteTray != null ? PieceTray.WhiteTray.name : "NULL")}");
        Debug.Log($"  BlackTray: {(PieceTray.BlackTray != null ? PieceTray.BlackTray.name : "NULL")}");
        Debug.Log($"  GreenTray: {(PieceTray.GreenTray != null ? PieceTray.GreenTray.name : "NULL")}");
        Debug.Log($"  PurpleTray: {(PieceTray.PurpleTray != null ? PieceTray.PurpleTray.name : "NULL")}");
        Debug.Log($"  YellowTray: {(PieceTray.YellowTray != null ? PieceTray.YellowTray.name : "NULL")}");
        Debug.Log($"  OrangeTray: {(PieceTray.OrangeTray != null ? PieceTray.OrangeTray.name : "NULL")}");
        
        // Create placement manager
        Debug.Log("🎯 GameManager: About to call SetupPlacementManager()...");
        try
        {
            SetupPlacementManager();
            Debug.Log("✅ GameManager: SetupPlacementManager() completed successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("💀 GameManager: EXCEPTION in SetupPlacementManager()!");
            Debug.LogError($"💀 Exception: {ex.GetType().Name}: {ex.Message}");
            Debug.LogError($"💀 StackTrace: {ex.StackTrace}");
            Debug.LogError("💀 This prevents PlacementManager from being created!");
        }
        
        // Create placement UI
        SetupPlacementUI();
        
        // Create pawn promotion components
        SetupPawnPromotionSystem();
        
        // Create check detection system
        SetupCheckDetectionSystem();
        
        // Create timer system for timed play mode
        SetupTimerSystem();
        
        // Create timer display UI for timed play mode
        SetupTimerDisplayUI();
        
        // Wait one more frame for trays to be fully initialized
        yield return null;
        
        if (setupTestPieces)
        {
            Debug.Log("GameManager: Setting up pieces in trays...");
            SetupPiecesInTrays();
            
            // TIMING FIX: Notify PlacementManager that pieces are ready for AI placement
            if (PlacementManager.Instance != null)
            {
                Debug.Log("🎯 GameManager: Notifying PlacementManager that pieces are ready for AI placement");
                PlacementManager.Instance.NotifyPiecesReady();
                Debug.Log("✅ GameManager: PlacementManager notified successfully");
            }
            else
            {
                Debug.LogError("🚨 GameManager: PlacementManager.Instance is NULL! Cannot notify pieces are ready.");
                Debug.LogError("🚨 This explains why AI placement timing is broken!");
            }
        }
        
        // Add the automated test system
        GameObject movementTestObject = new GameObject("Movement Test System");
        movementTestObject.AddComponent<TestMovementSystem>();
        Debug.Log("GameManager: Added TestMovementSystem for verification");
        
        // CheckUITester removed from automatic creation to prevent debug text in UI
        // The CheckUITester component can still be manually added for debugging if needed
        // GameObject checkUITesterObject = new GameObject("Check UI Tester");
        // checkUITesterObject.AddComponent<CheckUITester>();
        // Debug.Log("GameManager: Added CheckUITester for manual UI testing (T/Y/U keys)");
        
        // Create confirmation dialog system
        SetupConfirmationDialog();
        
        // Create game menu UI (replaces standalone forfeit/draw buttons)
        SetupGameMenuUI();

        // Create coordinate labels UI
        SetupCoordinateLabelsUI();

        Debug.Log("GameManager: Game initialization complete!");
        
        // ANTI-REPETITION: Clear any existing game history for new game
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.ClearGameHistory();
        }
        
        // INTELLIGENT GAME MODE SETUP: Only apply fallback if not already configured
        if (GameStateManager.Instance != null && 
            GameStateManager.Instance.currentState == GameState.WaitingForConfiguration)
        {
            Debug.Log("GameManager: No configuration received from menu, applying fallback configuration");
            
            // Check for AI vs AI testing mode (could be set via inspector, debug command, etc.)
            bool forceAIVsAI = System.Environment.GetCommandLineArgs() != null && 
                               System.Array.Exists(System.Environment.GetCommandLineArgs(), arg => arg == "-aivsai");
            
            if (forceAIVsAI)
            {
                Debug.Log("GameManager: Command line -aivsai detected, setting up AI vs AI mode");
                SetupGameMode(PlayerType.Computer, PlayerType.Computer);
                Debug.Log("GameManager: Applied AI vs AI mode for testing");
            }
            else
            {
                Debug.Log("GameManager: Applying default Human vs AI setup");
                SetupGameMode(PlayerType.Human, PlayerType.Computer);
                Debug.Log("GameManager: Applied fallback Human vs AI mode for testing");
            }
        }
        else
        {
            Debug.Log($"GameManager: Game already configured by SceneController (state: {GameStateManager.Instance?.currentState}), skipping default setup");
            
            // CRITICAL FIX: Even when configured by SceneController, we need to ensure PlacementManager is properly configured
            Debug.Log("🔧 PLACEMENTMANAGER FIX: Ensuring PlacementManager is configured for the current game mode");
            
            if (TurnManager.Instance != null)
            {
                bool whiteIsAI = TurnManager.Instance.IsPlayerAI(PieceColor.White);
                bool blackIsAI = TurnManager.Instance.IsPlayerAI(PieceColor.Black);
                
                Debug.Log($"🔧 Detected game mode - White: {(whiteIsAI ? "AI" : "Human")}, Black: {(blackIsAI ? "AI" : "Human")}");
                
                // Configure PlacementManager for the detected game mode
                if (PlacementManager.Instance != null)
                {
                    // Configure alternating turns for AI vs Human games
                    bool shouldUseAlternatingTurns = (whiteIsAI && !blackIsAI) || (!whiteIsAI && blackIsAI);
                    
                    Debug.Log($"🔧 Configuring PlacementManager - alternatingTurns: {shouldUseAlternatingTurns}");
                    // Note: PlacementManager doesn't have alternatingTurns property, but this ensures it's configured
                    
                    Debug.Log("✅ PlacementManager configuration completed for SceneController-configured game");
                }
                else
                {
                    Debug.LogError("🚨 PlacementManager.Instance is NULL! Cannot configure for current game mode.");
                    Debug.LogError("🚨 This explains why AI placement is not working!");
                }
            }
            else
            {
                Debug.LogError("🚨 TurnManager.Instance is NULL! Cannot determine game mode for PlacementManager configuration.");
            }
        }
    }
    
    /// <summary>
    /// PUBLIC DEBUG METHOD: Force AI vs AI mode for testing
    /// This can be called from console or inspector for testing purposes
    /// </summary>
    [ContextMenu("Force AI vs AI Mode")]
    public void ForceAIVsAIMode()
    {
        Debug.Log("🤖 DEBUG: ForceAIVsAIMode called - forcing AI vs AI setup");
        SetupGameMode(PlayerType.Computer, PlayerType.Computer);
        
        // Force state transition if needed
        if (GameStateManager.Instance != null && 
            GameStateManager.Instance.currentState == GameState.WaitingForConfiguration)
        {
            Debug.Log("🤖 DEBUG: Transitioning to PiecePlacement state");
            GameStateManager.Instance.ChangeState(GameState.PiecePlacement);
        }
        
        Debug.Log("🤖 DEBUG: AI vs AI mode forced successfully");
    }
    
    /// <summary>
    /// DEBUG METHOD: Check system state for AI functionality
    /// </summary>
    [ContextMenu("Debug AI System State")]
    public void DebugAISystemState()
    {
        Debug.Log("🔍 === AI SYSTEM STATE DEBUG ===");
        Debug.Log($"GameState: {(GameStateManager.Instance?.currentState.ToString() ?? "NULL")}");
        Debug.Log($"TurnManager exists: {(TurnManager.Instance != null)}");
        if (TurnManager.Instance != null)
        {
            Debug.Log($"  Game Mode: {TurnManager.Instance.GetGameModeDescription()}");
            Debug.Log($"  White PlayerType: {TurnManager.Instance.GetPlayerType(PieceColor.White)}");
            Debug.Log($"  Black PlayerType: {TurnManager.Instance.GetPlayerType(PieceColor.Black)}");
            Debug.Log($"  Current Player: {TurnManager.Instance.GetCurrentPlayer()}");
            Debug.Log($"  Is Current Player AI: {TurnManager.Instance.IsCurrentPlayerAI()}");
        }
        Debug.Log($"AIPlayer exists: {(AIPlayer.Instance != null)}");
        Debug.Log($"PlacementManager exists: {(PlacementManager.Instance != null)}");
        
        // Check tray states for all player colors
        PieceColor[] allColors = { PieceColor.White, PieceColor.Black, PieceColor.Green, PieceColor.Purple, PieceColor.Yellow, PieceColor.Orange };
        foreach (PieceColor color in allColors)
        {
            PieceTray tray = PieceTray.GetTrayForColor(color);
            if (tray != null)
            {
                var pieces = tray.GetComponentsInChildren<ChessPiece>();
                Debug.Log($"{color} tray: {tray.GetPieceCount()} pieces reported, {pieces.Length} pieces found in hierarchy");
            }
            else
            {
                Debug.Log($"{color} tray: NULL");
            }
        }
        
        Debug.Log("🔍 === END AI SYSTEM DEBUG ===");
    }
    
    /// <summary>
    /// Public method to access piece prefabs, ensuring they are created if needed
    /// </summary>
    public GameObject GetPiecePrefab(ChessPieceType pieceType)
    {
        // Ensure prefabs are created if they don't exist
        EnsurePrefabsExist();
        
        switch (pieceType)
        {
            case ChessPieceType.King:
                return kingPiecePrefab;
            case ChessPieceType.Queen:
                return queenPiecePrefab;
            case ChessPieceType.Bishop:
                return bishopPiecePrefab;
            case ChessPieceType.Knight:
                return knightPiecePrefab;
            case ChessPieceType.Rook:
                return rookPiecePrefab;
            case ChessPieceType.Pawn:
                return pawnPiecePrefab;
            default:
                Debug.LogError($"GameManager.GetPiecePrefab: Unknown piece type {pieceType}");
                return null;
        }
    }
    
    /// <summary>
    /// Ensure all piece prefabs exist, creating them if necessary
    /// </summary>
    private void EnsurePrefabsExist()
    {
        if (kingPiecePrefab == null)
        {
            kingPiecePrefab = CreateKingPiecePrefab();
            Debug.Log("GameManager: Created King prefab on demand");
        }
        
        if (queenPiecePrefab == null)
        {
            queenPiecePrefab = CreateQueenPiecePrefab();
            Debug.Log("GameManager: Created Queen prefab on demand");
        }
        
        if (bishopPiecePrefab == null)
        {
            bishopPiecePrefab = CreateBishopPiecePrefab();
            Debug.Log("GameManager: Created Bishop prefab on demand");
        }
        
        if (knightPiecePrefab == null)
        {
            knightPiecePrefab = CreateKnightPiecePrefab();
            Debug.Log("GameManager: Created Knight prefab on demand");
        }
        
        if (rookPiecePrefab == null)
        {
            rookPiecePrefab = CreateRookPiecePrefab();
            Debug.Log("GameManager: Created Rook prefab on demand");
        }
        
        if (pawnPiecePrefab == null)
        {
            pawnPiecePrefab = CreatePawnPiecePrefab();
            Debug.Log("GameManager: Created Pawn prefab on demand");
        }
    }
    
    private void SetupRotationUI()
    {
        // Add BoardRotationUI component to this GameObject
        BoardRotationUI rotationUI = gameObject.GetComponent<BoardRotationUI>();
        if (rotationUI == null)
        {
            rotationUI = gameObject.AddComponent<BoardRotationUI>();
            Debug.Log("GameManager: Added BoardRotationUI component");
        }
        else
        {
            Debug.Log("GameManager: BoardRotationUI already exists");
        }
    }
    
    private void SetupCameraTarget()
    {
        // Find the camera controller and set target to board center
        CameraController cameraController = FindFirstObjectByType<CameraController>();
        if (cameraController != null)
        {
            // Look for the Board Rotator (parent of the actual board)
            GameObject boardRotator = GameObject.Find("Board Rotator");
            if (boardRotator != null)
            {
                cameraController.SetTarget(boardRotator.transform);
                Debug.Log("GameManager: Camera target set to Board Rotator");
            }
            else
            {
                // Fallback: create a target at world center
                GameObject cameraTarget = new GameObject("Camera Target");
                cameraTarget.transform.position = Vector3.zero;
                cameraController.SetTarget(cameraTarget.transform);
                Debug.Log("GameManager: Created camera target at world center");
            }
        }
        else
        {
            Debug.LogWarning("GameManager: No CameraController found to set target");
        }
    }
    
    private void CreateChessBoardInstance()
    {
        // Create a simple ChessBoard GameObject for piece management
        // This works alongside the EmergencyChessBoard for grid visualization
        GameObject chessBoardObject = new GameObject("Chess Board");
        chessBoardObject.AddComponent<ChessBoard>();
        Debug.Log("GameManager: ChessBoard instance created");
    }
    
    private void CreateGameStateManager()
    {
        GameObject gameStateObject = new GameObject("Game State Manager");
        gameStateObject.AddComponent<GameStateManager>();
        Debug.Log("GameManager: GameStateManager created");

        // Create TurnManager for turn-based gameplay
        GameObject turnManagerObject = new GameObject("Turn Manager");
        turnManagerObject.AddComponent<TurnManager>();
        Debug.Log("GameManager: TurnManager created for turn-based gameplay");

        // Create AIPlayer for AI functionality
        GameObject aiPlayerObject = new GameObject("AI Player");
        aiPlayerObject.AddComponent<AIPlayer>();
        Debug.Log("GameManager: AIPlayer created for AI placement and moves");

        // Create PieceTrayManager for centralized tray tracking (scalable multi-player)
        GameObject pieceTrayManagerObject = new GameObject("Piece Tray Manager");
        pieceTrayManagerObject.AddComponent<PieceTrayManager>();
        Debug.Log("GameManager: PieceTrayManager created for centralized tray management");

        // Create ChaosRotationManager for chaos mode functionality
        GameObject chaosManagerObject = new GameObject("Chaos Rotation Manager");
        chaosManagerObject.AddComponent<ChaosRotationManager>();
        Debug.Log("GameManager: ChaosRotationManager created for chaos mode slice rotations");

        // NOTE: BoardDimensionsManager is created in SceneBootstrap before any scenes load
        // This ensures it's available when SceneController configures the board size
    }
    
    /// <summary>
    /// Clean up any existing King pieces in the scene before creating the tray system
    /// </summary>
    private void CleanUpExistingKingPieces()
    {
        Debug.Log("GameManager: Cleaning up any existing King pieces in scene...");
        
        // Find ALL King components in the scene (more aggressive than ChessBoard cleanup)
        King[] allKings = FindObjectsByType<King>(FindObjectsSortMode.None);
        Debug.Log($"GameManager: Found {allKings.Length} King GameObjects in scene");
        
        int destroyedCount = 0;
        foreach (King king in allKings)
        {
            Debug.Log($"GameManager: Found King {king.pieceColor} {king.pieceType} at {king.transform.position}");
            Debug.Log($"  Parent: {(king.transform.parent != null ? king.transform.parent.name : "None")}");
            Debug.Log($"  GameObject: {king.gameObject.name}");
            Debug.Log($"  CurrentPosition valid: {king.CurrentPosition.IsValid()}");
            
            // Destroy ALL Kings found in scene - we'll recreate them properly in trays
            Debug.Log($"GameManager: Destroying King GameObject '{king.gameObject.name}'");
            DestroyImmediate(king.gameObject);
            destroyedCount++;
        }
        
        // Also search for any ChessPiece components (broader search)
        ChessPiece[] allPieces = FindObjectsByType<ChessPiece>(FindObjectsSortMode.None);
        Debug.Log($"GameManager: Found {allPieces.Length} ChessPiece GameObjects in scene");
        
        foreach (ChessPiece piece in allPieces)
        {
            if (piece != null) // Check if not already destroyed
            {
                Debug.Log($"GameManager: Found ChessPiece {piece.pieceColor} {piece.pieceType} at {piece.transform.position}");
                Debug.Log($"GameManager: Destroying ChessPiece GameObject '{piece.gameObject.name}'");
                DestroyImmediate(piece.gameObject);
                destroyedCount++;
            }
        }
        
        Debug.Log($"GameManager: Destroyed {destroyedCount} total King/ChessPiece GameObjects from scene");
    }
    
    private void SetupPieceTrays()
    {
        if (traysCreated)
        {
            Debug.Log("GameManager: Trays already created, skipping...");
            return;
        }

        Debug.Log("GameManager: Setting up piece trays...");

        // Determine how many players are in the game
        int playerCount = 2; // Default to 2 players
        if (PlayerManager.Instance != null)
        {
            playerCount = PlayerManager.Instance.GetTotalPlayerCount();
            Debug.Log($"GameManager: PlayerManager reports {playerCount} players");
        }
        else
        {
            Debug.LogWarning("GameManager: PlayerManager not available, defaulting to 2 players");
        }

        // Create trays for each active player
        // Player order: White, Black, Green, Purple, Yellow, Orange
        PieceColor[] playerColors = { PieceColor.White, PieceColor.Black, PieceColor.Green, PieceColor.Purple, PieceColor.Yellow, PieceColor.Orange };

        for (int i = 0; i < playerCount && i < playerColors.Length; i++)
        {
            PieceColor color = playerColors[i];
            CreateTrayForColor(color);
        }

        traysCreated = true;
        Debug.Log($"GameManager: Created {playerCount} piece trays successfully");
    }

    /// <summary>
    /// Create a tray for a specific player color
    /// </summary>
    private void CreateTrayForColor(PieceColor color)
    {
        // Determine tray side based on color
        // White=left, Black=right, Green/Purple/Yellow/Orange use their specific positions
        bool isLeftSide = (color == PieceColor.White);

        GameObject trayObject = new GameObject($"{color} Tray");
        PieceTray tray = trayObject.AddComponent<PieceTray>();
        tray.Initialize(color, isLeftSide);

        Debug.Log($"GameManager: Created {color} tray");
    }
    
    private void SetupPlacementManager()
    {
        Debug.Log("🎯 GameManager.SetupPlacementManager: ENTRY - Creating PlacementManager GameObject");
        
        GameObject placementObject = new GameObject("Placement Manager");
        Debug.Log($"🎯 GameObject created: {placementObject.name}, active: {placementObject.activeInHierarchy}");
        
        PlacementManager placementComponent = placementObject.AddComponent<PlacementManager>();
        Debug.Log($"🎯 PlacementManager component added, enabled: {placementComponent.enabled}");
        Debug.Log($"🎯 PlacementManager.Instance after creation: {(PlacementManager.Instance != null ? "SET" : "NULL")}");
        
        Debug.Log("✅ GameManager.SetupPlacementManager: PlacementManager created successfully");
    }
    
    private void SetupPlacementUI()
    {
        GameObject placementUIObject = new GameObject("Placement UI");
        placementUIObject.AddComponent<PlacementUI>();
        Debug.Log("GameManager: PlacementUI created");
    }
    
    private void SetupPawnPromotionSystem()
    {
        // Create PawnPromotionManager
        GameObject promotionManagerObject = new GameObject("Pawn Promotion Manager");
        promotionManagerObject.AddComponent<PawnPromotionManager>();
        Debug.Log("GameManager: PawnPromotionManager created");
        
        // Create PawnPromotionUI
        GameObject promotionUIObject = new GameObject("Pawn Promotion UI");
        promotionUIObject.AddComponent<PawnPromotionUI>();
        Debug.Log("GameManager: PawnPromotionUI created");
        
        Debug.Log("GameManager: Pawn promotion system setup complete");
    }
    
    private void SetupCheckDetectionSystem()
    {
        // Create CheckDetectionManager
        GameObject checkDetectionObject = new GameObject("Check Detection Manager");
        checkDetectionObject.AddComponent<CheckDetectionManager>();
        Debug.Log("GameManager: CheckDetectionManager created");
        
        // Create GameEndDetectionManager 
        GameObject gameEndDetectionObject = new GameObject("Game End Detection Manager");
        gameEndDetectionObject.AddComponent<GameEndDetectionManager>();
        Debug.Log("GameManager: GameEndDetectionManager created");
        
        // Create CheckVisualFeedbackManager
        GameObject visualFeedbackObject = new GameObject("Check Visual Feedback Manager");
        visualFeedbackObject.AddComponent<CheckVisualFeedbackManager>();
        Debug.Log("GameManager: CheckVisualFeedbackManager created");
        
        // Create ThreatIndicatorManager for orange threat indicators
        GameObject threatIndicatorObject = new GameObject("Threat Indicator Manager");
        threatIndicatorObject.AddComponent<ThreatIndicatorManager>();
        Debug.Log("GameManager: ThreatIndicatorManager created");
        
        // NOTE: CheckStatusUI disabled - using persistent status display instead
        // GameObject checkStatusUIObject = new GameObject("Check Status UI");
        // checkStatusUIObject.AddComponent<CheckStatusUI>();
        // Debug.Log("GameManager: CheckStatusUI created");
        
        // Create UIManager for coordinated UI system (persistent check status only)
        GameObject uiManagerObject = new GameObject("UI Manager");
        uiManagerObject.AddComponent<UIManager>();
        Debug.Log("GameManager: UIManager created with persistent check status");
        
        // Create PiecePreviewRenderer for visual promotion buttons
        GameObject piecePreviewObject = new GameObject("Piece Preview Renderer");
        piecePreviewObject.AddComponent<PiecePreviewRenderer>();
        Debug.Log("GameManager: PiecePreviewRenderer created");
        
        Debug.Log("GameManager: Check detection system setup complete");
    }
    
    /// <summary>
    /// Set up timer system for timed play mode
    /// </summary>
    private void SetupTimerSystem()
    {
        if (TimerManager.Instance == null)
        {
            Debug.Log("🕒 GameManager: Creating Timer Manager...");
            GameObject timerManagerObject = new GameObject("Timer Manager");
            timerManagerObject.AddComponent<TimerManager>();
            Debug.Log("🕒 GameManager: Timer Manager created successfully");
        }
        else
        {
            Debug.Log("🕒 GameManager: Timer Manager already exists");
        }
    }
    
    /// <summary>
    /// Set up timer display UI for timed play mode
    /// </summary>
    private void SetupTimerDisplayUI()
    {
        if (TimerDisplayUI.Instance == null)
        {
            Debug.Log("🕒 GameManager: Creating Timer Display UI...");
            GameObject timerUIObject = new GameObject("Timer Display UI");
            timerUIObject.AddComponent<TimerDisplayUI>();
            Debug.Log("🕒 GameManager: Timer Display UI created successfully");
        }
        else
        {
            Debug.Log("🕒 GameManager: Timer Display UI already exists");
        }
    }
    
    private void SetupPiecesInTrays()
    {
        // Create piece prefabs if they don't exist
        if (kingPiecePrefab == null)
        {
            kingPiecePrefab = CreateKingPiecePrefab();
        }
        
        if (queenPiecePrefab == null)
        {
            queenPiecePrefab = CreateQueenPiecePrefab();
        }
        
        if (bishopPiecePrefab == null)
        {
            bishopPiecePrefab = CreateBishopPiecePrefab();
        }
        
        if (knightPiecePrefab == null)
        {
            knightPiecePrefab = CreateKnightPiecePrefab();
        }
        
        if (rookPiecePrefab == null)
        {
            rookPiecePrefab = CreateRookPiecePrefab();
        }
        
        if (pawnPiecePrefab == null)
        {
            pawnPiecePrefab = CreatePawnPiecePrefab();
        }
        
        Debug.Log("🎭 GameManager: Starting piece creation in trays...");

        // Get all active players from PlayerManager
        List<PieceColor> activePlayers = new List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetAllPlayers();
            Debug.Log($"GameManager: PlayerManager reports {activePlayers.Count} active players");
        }
        else
        {
            // Fallback to White and Black
            Debug.LogWarning("GameManager: PlayerManager not available, defaulting to White and Black");
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
        }

        // Verify all trays exist for active players
        bool allTraysReady = true;
        foreach (PieceColor color in activePlayers)
        {
            PieceTray tray = PieceTray.GetTrayForColor(color);
            if (tray == null)
            {
                Debug.LogError($"GameManager: {color} tray not found!");
                allTraysReady = false;
            }
            else
            {
                Debug.Log($"GameManager: {color} tray ready: {tray.name}");
            }
        }

        if (!allTraysReady)
        {
            Debug.LogError("GameManager: Not all trays are ready! Cannot create pieces.");
            return;
        }

        // Create pieces for each active player
        // Each player gets: 1 King + 1 Queen + 2 Bishops + 2 Knights + 2 Rooks
        foreach (PieceColor color in activePlayers)
        {
            if (ZombieMode.Active && color == ZombieMode.ZombieColor) continue; // the zombies arrive during play
            if (QueenBlitz.Active)
            {
                if (color == QueenBlitz.PlayerColor) CreatePieceInTray(color, ChessPieceType.Queen); // puzzles: a lone queen
                continue;
            }
            Debug.Log($"🎭 Creating pieces for {color} player...");
            CreatePieceInTray(color, ChessPieceType.King);
            CreatePieceInTray(color, ChessPieceType.Queen);
            CreatePieceInTray(color, ChessPieceType.Bishop);
            CreatePieceInTray(color, ChessPieceType.Bishop);
            CreatePieceInTray(color, ChessPieceType.Knight);
            CreatePieceInTray(color, ChessPieceType.Knight);
            CreatePieceInTray(color, ChessPieceType.Rook);
            CreatePieceInTray(color, ChessPieceType.Rook);
        }

        Debug.Log("🎭 GameManager: All pieces created in trays");

        // Validate that all pieces were successfully placed in trays
        int expectedPiecesPerTray = 8; // King, Queen, 2 Bishops, 2 Knights, 2 Rooks
        bool allValid = true;

        foreach (PieceColor color in activePlayers)
        {
            PieceTray tray = PieceTray.GetTrayForColor(color);
            int pieceCount = tray != null ? tray.GetPieceCount() : 0;
            Debug.Log($"🎭 {color} tray has {pieceCount}/{expectedPiecesPerTray} pieces");

            if (pieceCount != expectedPiecesPerTray)
            {
                Debug.LogError($"❌ {color} tray validation FAILED: {pieceCount}/{expectedPiecesPerTray} pieces");
                allValid = false;
            }
        }

        if (allValid)
        {
            Debug.Log($"✅ SUCCESS: All {activePlayers.Count} player trays have {expectedPiecesPerTray} pieces!");
        }
        else
        {
            Debug.LogError($"❌ VALIDATION FAILED: Some trays do not have the expected {expectedPiecesPerTray} pieces");
        }
    }
    
    private void CreatePieceInTray(PieceColor color, ChessPieceType pieceType)
    {
        Debug.Log($"🎭 GameManager.CreatePieceInTray: Creating {color} {pieceType}");
        
        // Select the appropriate prefab based on piece type
        GameObject prefabToUse = null;
        string prefabName = "";
        
        switch (pieceType)
        {
            case ChessPieceType.King:
                prefabToUse = kingPiecePrefab;
                prefabName = "King";
                break;
            case ChessPieceType.Queen:
                prefabToUse = queenPiecePrefab;
                prefabName = "Queen";
                break;
            case ChessPieceType.Bishop:
                prefabToUse = bishopPiecePrefab;
                prefabName = "Bishop";
                break;
            case ChessPieceType.Knight:
                prefabToUse = knightPiecePrefab;
                prefabName = "Knight";
                break;
            case ChessPieceType.Rook:
                prefabToUse = rookPiecePrefab;
                prefabName = "Rook";
                break;
            default:
                Debug.LogError($"🎭❌ Unsupported piece type: {pieceType}");
                return;
        }
        
        if (prefabToUse == null)
        {
            Debug.LogError($"🎭❌ No prefab available for {pieceType}");
            return;
        }
        
        Debug.Log($"  Using {prefabName} prefab: {prefabToUse.name}");
        
        GameObject pieceObject = Instantiate(prefabToUse);
        pieceObject.name = $"{color} {pieceType}";
        // CRITICAL FIX: Do NOT activate yet - set properties first
        Debug.Log($"  Created GameObject: {pieceObject.name} at position {pieceObject.transform.position}");
        
        // Get the ChessPiece component (works for both King and Queen)
        ChessPiece piece = pieceObject.GetComponent<ChessPiece>();
        if (piece != null)
        {
            Debug.Log($"  Found ChessPiece component: {piece.GetType().Name}");
            Debug.Log($"  Piece initial state - Color: {piece.pieceColor}, Type: {piece.pieceType}, Position: {piece.CurrentPosition}");
            
            // Set properties BEFORE activation to prevent timing issues
            piece.pieceColor = color;
            piece.pieceType = pieceType;
            
            // CRITICAL FIX: Set tray pieces to have INVALID position to prevent board contamination
            piece.SetCurrentPosition(new BoardPosition(-1, -1, -1));
            Debug.Log($"  Set tray piece to invalid position (-1, -1, -1) to prevent board contamination");
            
            // CRITICAL: Apply material after setting color to ensure correct appearance
            piece.ApplyMaterial();
            Debug.Log($"  Applied {color} material to piece");
            
            Debug.Log($"  Set piece properties - Color: {piece.pieceColor}, Type: {piece.pieceType}");

            // NOW activate the piece with all properties properly set
            pieceObject.SetActive(true);
            Debug.Log($"  Activated piece after setting properties");

            // Add piece to appropriate tray using dynamic lookup
            PieceTray targetTray = PieceTray.GetTrayForColor(color);
            Debug.Log($"  Target tray: {(targetTray != null ? targetTray.name : "NULL")}");
            
            if (targetTray != null)
            {
                Debug.Log($"  Attempting to add piece to tray...");
                bool success = targetTray.AddPiece(piece);
                Debug.Log($"  AddPiece result: {success}");
                
                if (success)
                {
                    Debug.Log($"🎭✅ Successfully added {color} {pieceType} to tray");
                    Debug.Log($"  Final piece position: {piece.transform.position}");
                    Debug.Log($"  Final piece parent: {(piece.transform.parent != null ? piece.transform.parent.name : "None")}");
                    Debug.Log($"  Piece CurrentPosition: {piece.CurrentPosition} (should be invalid for tray pieces)");
                }
                else
                {
                    Debug.LogError($"🎭❌ CRITICAL: Failed to add {color} {pieceType} to tray - tray may be full!");
                    Debug.LogError($"  Tray capacity: {targetTray.maxPieces}, Current count: {targetTray.GetPieceCount()}");
                    
                    // Destroy the piece rather than letting it auto-position on board
                    Debug.LogError($"  Destroying piece to prevent unwanted board placement");
                    DestroyImmediate(pieceObject);
                    return;
                }
            }
            else
            {
                Debug.LogError($"🎭 No tray found for {color} pieces");
            }
        }
        else
        {
            Debug.LogError($"🎭❌ Created piece object does not have ChessPiece component");
        }
    }
    
    private GameObject CreateKingPiecePrefab()
    {
        Debug.Log("🎭 GameManager.CreateKingPiecePrefab: Creating King prefab template...");
        
        GameObject prefab = new GameObject("King Piece Prefab");
        
        // CRITICAL: Deactivate during setup to prevent Start() from being called
        prefab.SetActive(false);
        
        // Add visual representation - capsule pill shape
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.transform.SetParent(prefab.transform);
        
        // Position piece so bottom rests on floor plane
        // Cell height is 2.8, piece height should be 0.75 * 2.8 = 2.1
        // Capsule default height is 2, so scale Y to 1.05 to get 2.1 height
        // Position up by half the height (1.05) so bottom touches floor
        visual.transform.localPosition = new Vector3(0, 1.05f, 0);
        visual.transform.localScale = new Vector3(0.8f, 1.05f, 0.8f); // Slightly wider, 2.1 units tall
        
        // Remove the collider from the visual object
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            DestroyImmediate(visualCollider);
        }
        
        // Add larger invisible collider for better mobile touch targets
        SphereCollider collider = prefab.AddComponent<SphereCollider>();
        collider.center = new Vector3(0, 1.05f, 0);
        collider.radius = 1.2f; // Larger than visual for easier touching
        
        // Add the King script
        King kingScript = prefab.AddComponent<King>();
        
        // Set the piece tag for input detection
        prefab.tag = "ChessPiece";
        
        Debug.Log("🎭 GameManager.CreateKingPiecePrefab: Prefab created successfully (inactive)");
        
        return prefab;
    }
    
    private GameObject CreateQueenPiecePrefab()
    {
        Debug.Log("🎭 GameManager.CreateQueenPiecePrefab: Creating Queen prefab template...");
        
        GameObject prefab = new GameObject("Queen Piece Prefab");
        
        // CRITICAL: Deactivate during setup to prevent Start() from being called
        prefab.SetActive(false);
        
        // Add visual representation - cylinder for Queen (taller/different from King's capsule)
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.transform.SetParent(prefab.transform);
        
        // Position piece so bottom rests on floor plane
        // Cell height is 2.8, piece height should be 0.85 * 2.8 = 2.38 (taller than King)
        // Cylinder default height is 2, so scale Y to 1.19 to get 2.38 height
        // Position up by half the height (1.19) so bottom touches floor
        visual.transform.localPosition = new Vector3(0, 1.19f, 0);
        visual.transform.localScale = new Vector3(0.7f, 1.19f, 0.7f); // Narrower but taller than King
        
        // Remove the collider from the visual object
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            DestroyImmediate(visualCollider);
        }
        
        // Add larger invisible collider for better mobile touch targets
        SphereCollider collider = prefab.AddComponent<SphereCollider>();
        collider.center = new Vector3(0, 1.19f, 0);
        collider.radius = 1.3f; // Slightly larger than King for easier touching
        
        // Add the Queen script
        Queen queenScript = prefab.AddComponent<Queen>();
        
        // Set the piece tag for input detection
        prefab.tag = "ChessPiece";
        
        Debug.Log("🎭 GameManager.CreateQueenPiecePrefab: Prefab created successfully (inactive)");
        
        return prefab;
    }
    
    private GameObject CreateBishopPiecePrefab()
    {
        Debug.Log("🎭 GameManager.CreateBishopPiecePrefab: Creating Bishop prefab template...");
        
        GameObject prefab = new GameObject("Bishop Piece Prefab");
        
        // CRITICAL: Deactivate during setup to prevent Start() from being called
        prefab.SetActive(false);
        
        // Add visual representation - cone/pyramid for Bishop (distinctive diagonal shape)
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.transform.SetParent(prefab.transform);
        
        // Position piece so bottom rests on floor plane
        // Cell height is 2.8, piece height should be 0.8 * 2.8 = 2.24 (between King and Queen)
        // Cube default height is 1, so scale Y to 2.24 to get proper height
        // Position up by half the height (1.12) so bottom touches floor
        visual.transform.localPosition = new Vector3(0, 1.12f, 0);
        visual.transform.localScale = new Vector3(0.6f, 2.24f, 0.6f); // Tall narrow cube resembling traditional bishop
        
        // Rotate 45 degrees to make it diamond-shaped from above (distinctive)
        visual.transform.localRotation = Quaternion.Euler(0, 45f, 0);
        
        // Remove the collider from the visual object
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            DestroyImmediate(visualCollider);
        }
        
        // Add larger invisible collider for better mobile touch targets
        SphereCollider collider = prefab.AddComponent<SphereCollider>();
        collider.center = new Vector3(0, 1.12f, 0);
        collider.radius = 1.2f; // Same as King for consistency
        
        // Add the Bishop script
        Bishop bishopScript = prefab.AddComponent<Bishop>();
        
        // Set the piece tag for input detection
        prefab.tag = "ChessPiece";
        
        Debug.Log("🎭 GameManager.CreateBishopPiecePrefab: Prefab created successfully (inactive)");
        
        return prefab;
    }
    
    private GameObject CreateKnightPiecePrefab()
    {
        Debug.Log("🎭 GameManager.CreateKnightPiecePrefab: Creating Knight prefab template...");
        
        GameObject prefab = new GameObject("Knight Piece Prefab");
        
        // CRITICAL: Deactivate during setup to prevent Start() from being called
        prefab.SetActive(false);
        
        // Add visual representation - L-shaped geometry for Knight (distinctive movement pattern)
        GameObject visual1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject visual2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual1.transform.SetParent(prefab.transform);
        visual2.transform.SetParent(prefab.transform);
        
        // Position pieces to create an L-shape representing Knight's movement
        // Cell height is 2.8, piece height should be 0.75 * 2.8 = 2.1 (similar to King)
        // Create L-shape: vertical part (2.1 tall) and horizontal part (extends sideways)
        
        // Vertical part of L - tall narrow cube
        visual1.transform.localPosition = new Vector3(0, 1.05f, 0);
        visual1.transform.localScale = new Vector3(0.4f, 2.1f, 0.4f); // Tall and narrow
        
        // Horizontal part of L - extends to the side
        visual2.transform.localPosition = new Vector3(0.4f, 0.3f, 0);
        visual2.transform.localScale = new Vector3(0.8f, 0.6f, 0.4f); // Wide and low
        
        // Remove colliders from visual objects
        Collider visualCollider1 = visual1.GetComponent<Collider>();
        if (visualCollider1 != null)
        {
            DestroyImmediate(visualCollider1);
        }
        
        Collider visualCollider2 = visual2.GetComponent<Collider>();
        if (visualCollider2 != null)
        {
            DestroyImmediate(visualCollider2);
        }
        
        // Add larger invisible collider for better mobile touch targets
        SphereCollider collider = prefab.AddComponent<SphereCollider>();
        collider.center = new Vector3(0, 1.05f, 0);
        collider.radius = 1.3f; // Larger for easier touching (distinctive from other pieces)
        
        // Add the Knight script
        Knight knightScript = prefab.AddComponent<Knight>();
        
        // Set the piece tag for input detection
        prefab.tag = "ChessPiece";
        
        Debug.Log("🎭 GameManager.CreateKnightPiecePrefab: Prefab created successfully (inactive)");
        
        return prefab;
    }
    
    private GameObject CreateRookPiecePrefab()
    {
        Debug.Log("🎭 GameManager.CreateRookPiecePrefab: Creating Rook prefab template...");
        
        GameObject prefab = new GameObject("Rook Piece Prefab");
        
        // CRITICAL: Deactivate during setup to prevent Start() from being called
        prefab.SetActive(false);
        
        // Add visual representation - simplified castle tower (following Knight pattern)
        GameObject baseVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject towerVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseVisual.transform.SetParent(prefab.transform);
        towerVisual.transform.SetParent(prefab.transform);
        
        // Position piece so bottom rests on floor plane (following other pieces' pattern)
        // Cell height is 2.8, piece height should be 0.9 * 2.8 = 2.52 (tallest after Queen)
        // Position up by half the total height so bottom touches floor
        
        // Base of castle - wide foundation at bottom
        baseVisual.transform.localPosition = new Vector3(0, 0.3f, 0);
        baseVisual.transform.localScale = new Vector3(1.0f, 0.6f, 1.0f); // Wide and low base
        
        // Tower body - main vertical structure
        towerVisual.transform.localPosition = new Vector3(0, 1.26f, 0);
        towerVisual.transform.localScale = new Vector3(0.7f, 1.9f, 0.7f); // Tall central tower
        
        // Remove colliders from visual objects
        Collider baseCollider = baseVisual.GetComponent<Collider>();
        if (baseCollider != null)
        {
            DestroyImmediate(baseCollider);
        }
        
        Collider towerCollider = towerVisual.GetComponent<Collider>();
        if (towerCollider != null)
        {
            DestroyImmediate(towerCollider);
        }
        
        // Add larger invisible collider for better mobile touch targets (following Knight pattern)
        SphereCollider collider = prefab.AddComponent<SphereCollider>();
        collider.center = new Vector3(0, 1.26f, 0); // Center on tower middle
        collider.radius = 1.3f; // Large touch target
        
        // Add the Rook script
        Rook rookScript = prefab.AddComponent<Rook>();
        
        // Set the piece tag for input detection
        prefab.tag = "ChessPiece";
        
        Debug.Log("🎭 GameManager.CreateRookPiecePrefab: Prefab created successfully (inactive)");
        
        return prefab;
    }
    
    private GameObject CreatePawnPiecePrefab()
    {
        Debug.Log("🎭 GameManager.CreatePawnPiecePrefab: Creating Pawn prefab template...");
        
        GameObject prefab = new GameObject("Pawn Piece Prefab");
        
        // CRITICAL: Deactivate during setup to prevent Start() from being called
        prefab.SetActive(false);
        
        // Add visual representation - simple sphere for Pawn (distinctive from all other pieces)
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.transform.SetParent(prefab.transform);
        
        // Position piece so bottom rests on floor plane
        // Cell height is 2.8, piece height should be 0.6 * 2.8 = 1.68 (smallest piece)
        // Sphere default diameter is 1, so scale to 1.68 to get proper height
        // Position up by half the height (0.84) so bottom touches floor
        visual.transform.localPosition = new Vector3(0, 0.84f, 0);
        visual.transform.localScale = new Vector3(1.68f, 1.68f, 1.68f); // Uniform sphere scaling
        
        // Remove the collider from the visual object
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            DestroyImmediate(visualCollider);
        }
        
        // Add larger invisible collider for better mobile touch targets
        SphereCollider collider = prefab.AddComponent<SphereCollider>();
        collider.center = new Vector3(0, 0.84f, 0);
        collider.radius = 1.1f; // Smaller than other pieces but still easy to touch
        
        // Add the Pawn script
        Pawn pawnScript = prefab.AddComponent<Pawn>();
        
        // Set the piece tag for input detection
        prefab.tag = "ChessPiece";
        
        Debug.Log("🎭 GameManager.CreatePawnPiecePrefab: Prefab created successfully (inactive)");
        
        return prefab;
    }
    
    private void CreateKingPiece(BoardPosition position, PieceColor color)
    {
        Debug.Log($"🎭 GameManager.CreateKingPiece: Starting creation of {color} King at {position}");
        
        if (ChessBoard.Instance == null) 
        {
            Debug.LogError("GameManager: Cannot create King piece - ChessBoard.Instance is null!");
            return;
        }
        
        Debug.Log($"GameManager: ChessBoard.Instance confirmed available for {color} King");
        
        GameObject pieceObject = Instantiate(kingPiecePrefab);
        pieceObject.name = $"{color} King at {position}"; // Better naming for debugging
        King piece = pieceObject.GetComponent<King>();
        
        Debug.Log($"GameManager: Instantiated piece object '{pieceObject.name}' with King component: {piece != null}");
        
        // Make piece a child of the rotating board structure
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        if (piecesContainer != null)
        {
            pieceObject.transform.SetParent(piecesContainer.transform);
            Debug.Log($"GameManager: Set piece parent to Pieces Container");
        }
        else
        {
            Debug.LogError("GameManager: Pieces Container not found! Creating piece without parent - this will break rotation!");
            // Try to find any emergency board structure
            GameObject boardRotator = GameObject.Find("Board Rotator");
            if (boardRotator != null)
            {
                pieceObject.transform.SetParent(boardRotator.transform);
                Debug.Log($"GameManager: Fallback - set piece parent to Board Rotator");
            }
        }
        
        if (piece != null)
        {
            Debug.Log($"GameManager: About to call piece.Initialize({position}, {color})");
            piece.Initialize(position, color);
            Debug.Log($"GameManager: piece.Initialize() completed");
            
            // Verify the piece was actually registered
            ChessPiece verifyRegistration = ChessBoard.Instance.GetPieceAt(position);
            if (verifyRegistration == piece)
            {
                Debug.Log($"✅ GameManager: REGISTRATION SUCCESSFUL - {color} King at {position}");
            }
            else
            {
                Debug.LogError($"❌ GameManager: REGISTRATION FAILED - {color} King at {position} not found in board!");
                Debug.LogError($"   Expected: {piece.GetType().Name}, Got: {(verifyRegistration != null ? verifyRegistration.GetType().Name : "NULL")}");
            }
            
            Vector3 expectedLocal = ChessBoard.Instance.BoardToLocalPosition(position);
            Debug.Log($"GameManager: Created {color} King at board {position}");
            Debug.Log($"  Expected local position: {expectedLocal}");
            Debug.Log($"  Actual local position: {pieceObject.transform.localPosition}");
            Debug.Log($"  Parent: {(pieceObject.transform.parent != null ? pieceObject.transform.parent.name : "None")}");
        }
        else
        {
            Debug.LogError("GameManager: Failed to get King component!");
        }
        
        Debug.Log($"🎭 GameManager.CreateKingPiece: Completed creation of {color} King at {position}");
    }
    
    /// <summary>
    /// Set up game mode by configuring player types for white and black
    /// </summary>
    /// <param name="whitePlayerType">Type of player controlling white pieces</param>
    /// <param name="blackPlayerType">Type of player controlling black pieces</param>
    public void SetupGameMode(PlayerType whitePlayerType, PlayerType blackPlayerType)
    {
        Debug.Log($"GameManager.SetupGameMode: Configuring {whitePlayerType} vs {blackPlayerType}");
        
        // Configure TurnManager with player types
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.SetGameMode(whitePlayerType, blackPlayerType);
            Debug.Log($"GameManager: TurnManager configured for {whitePlayerType} vs {blackPlayerType}");
        }
        else
        {
            Debug.LogError("GameManager.SetupGameMode: TurnManager.Instance is null!");
        }
        
        // Configure AIPlayer difficulty (default to Medium for testing)
        if (AIPlayer.Instance != null)
        {
            AIPlayer.Instance.SetDifficulty(AIDifficulty.Medium);
            Debug.Log("GameManager: AIPlayer difficulty set to Medium");
        }
        else
        {
            Debug.LogError("GameManager.SetupGameMode: AIPlayer.Instance is null!");
        }
        
        // Configure PlacementManager for AI games
        if (PlacementManager.Instance != null)
        {
            // Enable alternating turns if either player is AI for proper AI integration
            bool hasAIPlayer = (whitePlayerType == PlayerType.Computer || blackPlayerType == PlayerType.Computer);
            PlacementManager.Instance.alternatingTurns = hasAIPlayer;
            Debug.Log($"GameManager: PlacementManager alternatingTurns set to {hasAIPlayer} (AI players present: {hasAIPlayer})");
        }
        else
        {
            Debug.LogError("GameManager.SetupGameMode: PlacementManager.Instance is null!");
        }
        
        // Log the final configuration
        string gameMode = $"{whitePlayerType} vs {blackPlayerType}";
        Debug.Log($"GameManager: Game mode configured successfully - {gameMode}");
        
        // Special handling for AI vs AI games
        bool isAIVsAI = (whitePlayerType == PlayerType.Computer && blackPlayerType == PlayerType.Computer);
        if (isAIVsAI)
        {
            Debug.Log("🤖 GameManager: AI vs AI mode detected - enabling full automation");
            Debug.Log("🤖 GameManager: Placement phase will auto-complete when sufficient pieces are placed");
            Debug.Log("🤖 GameManager: AI moves will be automatic during gameplay");
            
            // Additional AI vs AI specific configuration
            ConfigureAIVsAIMode();
        }
        else if (whitePlayerType == PlayerType.Computer || blackPlayerType == PlayerType.Computer)
        {
            Debug.Log("GameManager: Mixed Human vs AI mode - AI players will automatically place pieces during setup phase");
            Debug.Log("GameManager: AI thinking time configured for realistic gameplay");
        }
        
        // Validate configuration was successful
        ValidateGameModeConfiguration(whitePlayerType, blackPlayerType);
    }
    
    /// <summary>
    /// Quick method to set up common game modes for testing
    /// </summary>
    public void SetHumanVsAI(bool humanIsWhite = true)
    {
        if (humanIsWhite)
        {
            SetupGameMode(PlayerType.Human, PlayerType.Computer);
        }
        else
        {
            SetupGameMode(PlayerType.Computer, PlayerType.Human);
        }
    }
    
    /// <summary>
    /// Set up AI vs AI mode for testing AI placement logic
    /// This automatically configures full AI vs AI automation:
    /// - Both players set as Computer type
    /// - Alternating AI placement during setup phase
    /// - Auto-completion of placement when sufficient pieces are placed
    /// - Automatic AI move triggering during gameplay
    /// </summary>
    public void SetAIVsAI()
    {
        SetupGameMode(PlayerType.Computer, PlayerType.Computer);
    }
    
    /// <summary>
    /// Manual trigger for AI placement debugging (can be called from console)
    /// </summary>
    [ContextMenu("Trigger AI Placement Test")]
    public void TriggerAIPlacementTest()
    {
        Debug.Log("🧪 GameManager.TriggerAIPlacementTest: Manual AI placement trigger");
        
        if (AIPlayer.Instance != null)
        {
            // Test with current turn player
            if (TurnManager.Instance != null)
            {
                PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
                Debug.Log($"🧪 GameManager: Triggering AI placement for current player: {currentPlayer}");
                AIPlayer.Instance.RequestPlacement(currentPlayer);
            }
            else
            {
                Debug.LogError("🚨 GameManager: TurnManager.Instance is null - cannot determine current player");
            }
        }
        else
        {
            Debug.LogError("🚨 GameManager: AIPlayer.Instance is null - cannot trigger AI placement");
        }
    }
    
    /// <summary>
    /// Manual trigger for AI placement debugging with specific color
    /// </summary>
    public void TriggerAIPlacementTest(PieceColor playerColor)
    {
        Debug.Log($"🧪 GameManager.TriggerAIPlacementTest: Manual AI placement trigger for {playerColor}");
        
        // Diagnostic information about the current game state
        if (GameStateManager.Instance != null)
        {
            Debug.Log($"🎮 Current game state: {GameStateManager.Instance.currentState}");
            Debug.Log($"🎮 Can place pieces: {GameStateManager.Instance.CanPlacePieces()}");
        }
        else
        {
            Debug.LogError("🚨 GameStateManager.Instance is null");
        }
        
        // Check AI system availability
        if (AIPlayer.Instance == null)
        {
            Debug.LogError("🚨 GameManager: AIPlayer.Instance is null - AI system not initialized");
            return;
        }
        
        // Check player configuration
        if (TurnManager.Instance != null)
        {
            PlayerType playerType = TurnManager.Instance.GetPlayerType(playerColor);
            bool isAI = TurnManager.Instance.IsPlayerAI(playerColor);
            Debug.Log($"🎭 {playerColor} player type: {playerType}, IsAI: {isAI}");
            
            if (!isAI)
            {
                Debug.LogWarning($"⚠️ {playerColor} is not configured as AI player - forcing placement test anyway");
            }
        }
        
        // Force the AI placement regardless of current configuration
        Debug.Log($"🚀 GameManager: Forcing AI placement request for {playerColor}");
        AIPlayer.Instance.RequestPlacement(playerColor);
    }
    
    private void Update()
    {
        // DIAGNOSTIC: Check InputManager status every 5 seconds - DISABLED to prevent log spam
        if (false && Time.time > 5f && Mathf.FloorToInt(Time.time) % 5 == 0 && Time.time % 1f < 0.1f)
        {
            Debug.Log("=== GAMEMANAGER DIAGNOSTIC ===");
            Debug.Log($"InputManager.Instance exists: {InputManager.Instance != null}");
            
            if (InputManager.Instance != null)
            {
                GameObject inputGO = InputManager.Instance.gameObject;
                Debug.Log($"InputManager GameObject: {inputGO.name}");
                Debug.Log($"InputManager active: {inputGO.activeInHierarchy}");
                Debug.Log($"InputManager enabled: {InputManager.Instance.enabled}");
                
                // Force verify all components
                MonoBehaviour[] components = inputGO.GetComponents<MonoBehaviour>();
                Debug.Log($"InputManager GameObject has {components.Length} MonoBehaviour components:");
                foreach (var comp in components)
                {
                    if (comp != null)
                    {
                        Debug.Log($"  - {comp.GetType().Name}: enabled={comp.enabled}");
                    }
                    else
                    {
                        Debug.Log($"  - NULL component found!");
                    }
                }
            }
            else
            {
                Debug.LogError("InputManager.Instance is NULL! Trying to recreate...");
                
                // Try to find existing InputManager in scene
                InputManager[] found = FindObjectsByType<InputManager>(FindObjectsSortMode.None);
                Debug.Log($"Found {found.Length} InputManager instances in scene");
                
                if (found.Length == 0)
                {
                    Debug.Log("Creating new InputManager GameObject...");
                    GameObject inputObject = new GameObject("InputManager - Recreated");
                    inputObject.AddComponent<InputManager>();
                    Debug.Log("InputManager recreated!");
                }
                else
                {
                    Debug.Log($"Found existing InputManager on GameObject: {found[0].gameObject.name}");
                }
            }
        }
        
        // Handle escape key to clear selection (useful for testing)
        if (InputHelper.GetKeyDown(KeyCode.Escape))
        {
            if (InputManager.Instance != null)
            {
                InputManager.Instance.ClearSelection();
            }
        }
        
        // AI Placement Testing Shortcuts
        if (InputHelper.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("🧪 F1 pressed - Testing White AI placement");
            TriggerAIPlacementTest(PieceColor.White);
        }
        
        if (InputHelper.GetKeyDown(KeyCode.F2))
        {
            Debug.Log("🧪 F2 pressed - Testing Black AI placement");
            TriggerAIPlacementTest(PieceColor.Black);
        }
        
        if (InputHelper.GetKeyDown(KeyCode.F3))
        {
            Debug.Log("🧪 F3 pressed - Testing current player AI placement");
            TriggerAIPlacementTest();
        }
        
        // Handle camera control shortcuts
        CameraController cameraController = FindFirstObjectByType<CameraController>();
        if (cameraController != null)
        {
            // I key for Interior zoom (close-up preset)
            if (InputHelper.GetKeyDown(KeyCode.I))
            {
                cameraController.SetZoomPreset(5f); // Close zoom preset (5 units)
                Debug.Log("🎥 Zoomed to Interior preset (5 units) - press O for Overview");
            }
            
            // O key for Overview zoom (far preset)
            if (InputHelper.GetKeyDown(KeyCode.O))
            {
                cameraController.SetZoomPreset(20f); // Far zoom preset (20 units)
                Debug.Log("🎥 Zoomed to Overview preset (20 units) - press I for Interior");
            }
            
            // R key to reset camera
            if (InputHelper.GetKeyDown(KeyCode.R))
            {
                cameraController.ResetToDefaultView();
                Debug.Log("🎥 Camera reset to default view");
            }
        }
        
        // Display helpful controls info (for first few seconds) - FIXED: Only print once every 4 seconds
        if (Time.time < 20f && Time.time > 3f && Mathf.FloorToInt(Time.time) % 4 == 0 && Time.time % 1f < Time.deltaTime)
        {
            Debug.Log("🎮 CONTROLS: Right-click+drag = Camera Orbit | Q/E = Rotate Board | I = Close Zoom (5 units) | O = Far Zoom (20 units) | R = Reset | Mouse wheel/Pinch = Continuous Zoom (3-35 units) | Left-click = Select");
            Debug.Log("🤖 AI TESTING: F1 = Test White AI Placement | F2 = Test Black AI Placement | F3 = Test Current Player AI Placement");
        }
    }
    
    /// <summary>
    /// Configure additional settings specific to AI vs AI games
    /// </summary>
    private void ConfigureAIVsAIMode()
    {
        Debug.Log("🤖 GameManager: Configuring AI vs AI specific settings...");
        
        // Ensure PlacementManager has alternating turns enabled for AI vs AI
        if (PlacementManager.Instance != null)
        {
            PlacementManager.Instance.alternatingTurns = true;
            Debug.Log("🤖 GameManager: Enabled alternating turns for AI vs AI placement");
        }
        
        // Optional: Adjust AI thinking times for faster AI vs AI gameplay
        if (AIPlayer.Instance != null)
        {
            // Keep current difficulty but could adjust thinking times for AI vs AI
            Debug.Log("🤖 GameManager: AI vs AI thinking times configured");
        }
        
        Debug.Log("🤖 GameManager: AI vs AI configuration complete");
    }
    
    /// <summary>
    /// Validate that the game mode was configured correctly
    /// </summary>
    private void ValidateGameModeConfiguration(PlayerType whiteType, PlayerType blackType)
    {
        Debug.Log("GameManager: Validating game mode configuration...");
        
        bool configurationValid = true;
        
        // Validate TurnManager configuration
        if (TurnManager.Instance != null)
        {
            bool whiteMatch = TurnManager.Instance.GetPlayerType(PieceColor.White) == whiteType;
            bool blackMatch = TurnManager.Instance.GetPlayerType(PieceColor.Black) == blackType;
            
            if (!whiteMatch || !blackMatch)
            {
                Debug.LogError($"GameManager: TurnManager player type mismatch!");
                Debug.LogError($"  Expected: White={whiteType}, Black={blackType}");
                Debug.LogError($"  Actual: White={TurnManager.Instance.GetPlayerType(PieceColor.White)}, Black={TurnManager.Instance.GetPlayerType(PieceColor.Black)}");
                configurationValid = false;
            }
        }
        else
        {
            Debug.LogError("GameManager: TurnManager.Instance is null during validation!");
            configurationValid = false;
        }
        
        // Validate AI components for AI players
        if (whiteType == PlayerType.Computer || blackType == PlayerType.Computer)
        {
            if (AIPlayer.Instance == null)
            {
                Debug.LogError("GameManager: AIPlayer.Instance is null but AI players are configured!");
                configurationValid = false;
            }
        }
        
        // Validate PlacementManager for AI games
        if (PlacementManager.Instance == null && (whiteType == PlayerType.Computer || blackType == PlayerType.Computer))
        {
            Debug.LogError("GameManager: PlacementManager.Instance is null but AI players need placement support!");
            configurationValid = false;
        }
        
        if (configurationValid)
        {
            Debug.Log("✅ GameManager: Game mode configuration validation passed");
        }
        else
        {
            Debug.LogError("❌ GameManager: Game mode configuration validation FAILED!");
        }
    }
    
    /// <summary>
    /// Public method to configure AI vs AI mode (can be called from main menu)
    /// </summary>
    public void ConfigureAIVsAI()
    {
        Debug.Log("🤖 GameManager: Configuring AI vs AI mode...");
        SetupGameMode(PlayerType.Computer, PlayerType.Computer);
    }
    
    /// <summary>
    /// Check if the current game is AI vs AI
    /// </summary>
    public bool IsAIVsAIGame()
    {
        return TurnManager.Instance != null && TurnManager.Instance.IsAIVsAI();
    }
    
    /// <summary>
    /// Set up game menu UI (integrated forfeit, draw, and menu functionality)
    /// </summary>
    private void SetupGameMenuUI()
    {
        if (GameMenuUI.Instance == null)
        {
            Debug.Log("GameManager: Creating Game Menu UI...");
            GameObject gameMenuObject = new GameObject("Game Menu UI");
            gameMenuObject.AddComponent<GameMenuUI>();
            Debug.Log("GameManager: Game Menu UI created successfully");
        }
        else
        {
            Debug.Log("GameManager: Game Menu UI already exists");
        }
    }

    /// <summary>
    /// Set up coordinate labels UI (displays chess notation around the board)
    /// </summary>
    private void SetupCoordinateLabelsUI()
    {
        if (CoordinateLabelsUI.Instance == null)
        {
            Debug.Log("GameManager: Creating Coordinate Labels UI...");
            GameObject coordinateLabelsObject = new GameObject("Coordinate Labels UI");
            coordinateLabelsObject.AddComponent<CoordinateLabelsUI>();
            Debug.Log("GameManager: Coordinate Labels UI created successfully");
        }
        else
        {
            Debug.Log("GameManager: Coordinate Labels UI already exists");
        }
    }

    /// <summary>
    /// Set up confirmation dialog system
    /// </summary>
    private void SetupConfirmationDialog()
    {
        if (ConfirmationDialog.Instance == null)
        {
            Debug.Log("GameManager: Creating Confirmation Dialog...");
            GameObject confirmationObject = new GameObject("Confirmation Dialog");
            confirmationObject.AddComponent<ConfirmationDialog>();
            Debug.Log("GameManager: Confirmation Dialog created successfully");
        }
        else
        {
            Debug.Log("GameManager: Confirmation Dialog already exists");
        }
    }
}