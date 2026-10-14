using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a 3D placement zone for a player during piece placement phase
/// </summary>
public struct PlacementZone
{
    public int xMin, xMax;   // X-layer range (slices closest to player's tray)
    public int yMin, yMax;   // Y-row range (may be restricted on larger boards)
    public int zMin, zMax;   // Z-column range (may be restricted on larger boards)

    public PlacementZone(int xMin, int xMax, int yMin, int yMax, int zMin, int zMax)
    {
        this.xMin = xMin;
        this.xMax = xMax;
        this.yMin = yMin;
        this.yMax = yMax;
        this.zMin = zMin;
        this.zMax = zMax;
    }

    public bool Contains(BoardPosition position)
    {
        return position.x >= xMin && position.x <= xMax &&
               position.y >= yMin && position.y <= yMax &&
               position.z >= zMin && position.z <= zMax;
    }
}

/// <summary>
/// Manages the piece placement phase where players place pieces from trays onto the board
/// </summary>
public class PlacementManager : MonoBehaviour
{
    [Header("Placement Settings")]
    public bool alternatingTurns = false; // DISABLED: Placement is now asynchronous/simultaneous
    
    [Header("Visual Feedback")]
    public Material validPlacementMaterial;
    public Material invalidPlacementMaterial;
    
    private ChessPiece selectedTrayPiece;
    private List<GameObject> placementIndicators = new List<GameObject>();
    private PieceColor currentPlacingPlayer = PieceColor.White;
    
    // AI-Human Selection State Protection
    private ChessPiece savedHumanSelectedPiece;
    private bool isAIPlacementInProgress = false;
    
    [Header("Player Readiness Tracking")]
    private Dictionary<PieceColor, bool> playerReadiness = new Dictionary<PieceColor, bool>();
    
    [Header("Timing Fix")]
    private bool piecesReadyForAI = false; // Track when pieces are created and ready for AI placement
    
    public static PlacementManager Instance { get; private set; }
    
    // Events
    public System.Action<PieceColor> OnPlayerTurnChanged;
    public System.Action OnPlacementPhaseComplete;
    public System.Action<PieceColor> OnPlayerReadinessChanged; // New event for UI updates
    
    // Public property to check if a piece is selected
    public bool HasSelectedTrayPiece => selectedTrayPiece != null;
    public PieceColor? SelectedTrayColor => selectedTrayPiece != null ? selectedTrayPiece.pieceColor : (PieceColor?)null;
    public ChessPieceType? SelectedTrayType => selectedTrayPiece != null ? selectedTrayPiece.pieceType : (ChessPieceType?)null;
    
    /// <summary>
    /// Get the placement zone for a piece color based on board size and player count
    /// Handles 2-6 player configurations with appropriate Y/Z restrictions on larger boards
    /// </summary>
    public static PlacementZone GetPlacementZoneForColor(PieceColor color)
    {
        if (BoardDimensionsManager.Instance == null)
        {
            Debug.LogError("PlacementManager: BoardDimensionsManager.Instance is null, using default 4x4x4 zone");
            // Fallback for 4x4x4 board
            int x = (color == PieceColor.White) ? 0 : 3;
            return new PlacementZone(x, x, 0, 3, 0, 3);
        }

        Vector3Int dims = BoardDimensionsManager.Instance.GetDimensions();
        int boardSize = dims.x; // Board is cubic (x=y=z)

        // Zombie runs: zombies can come from anywhere, so the player may use their whole first plane
        if (ZombieMode.Active && color == ZombieMode.PlayerColor) return new PlacementZone(0, 0, 0, boardSize - 1, 0, boardSize - 1);

        // Initialize zone boundaries
        int xMin = 0, xMax = boardSize - 1;
        int yMin = 0, yMax = boardSize - 1;
        int zMin = 0, zMax = boardSize - 1;

        // Determine placement zone based on player color and board orientation
        // White/Black place on X-axis sides (left/right)
        // Green/Purple place on Z-axis sides (front/back)
        // Yellow/Orange place on Y-axis sides (bottom/top)
        switch (color)
        {
            case PieceColor.White:
                // White: Left side (X=0 slice)
                xMin = xMax = 0;
                break;

            case PieceColor.Black:
                // Black: Right side (X=max slice)
                xMin = xMax = boardSize - 1;
                break;

            case PieceColor.Green:
                // Green: Front side (Z=0 slice)
                zMin = zMax = 0;
                break;

            case PieceColor.Purple:
                // Purple: Back side (Z=max slice)
                zMin = zMax = boardSize - 1;
                break;

            case PieceColor.Yellow:
                // Yellow: Bottom side (Y=0 slice)
                yMin = yMax = 0;
                break;

            case PieceColor.Orange:
                // Orange: Top side (Y=max slice)
                yMin = yMax = boardSize - 1;
                break;

            default:
                Debug.LogError($"PlacementManager: Unknown player color {color}");
                xMin = xMax = 0;
                break;
        }

        // Apply Y/Z restrictions based on board size for X-axis players (White/Black)
        // Apply X/Y restrictions based on board size for Z-axis players (Green/Purple)
        // Apply X/Z restrictions based on board size for Y-axis players (Yellow/Orange)
        if (boardSize == 6)
        {
            // 6x6x6 board: Divide the board into regions for 4 players
            if (color == PieceColor.White || color == PieceColor.Black)
            {
                // White/Black (X-axis): Use top 3 Y-rows (Y=3,4,5), middle 4 Z-columns (Z=1,2,3,4)
                // Restricts placement to 12 positions (3 rows × 4 columns) to prevent checkmate on placement
                yMin = 3;
                yMax = 5;
                zMin = 1;
                zMax = 4;
            }
            else if (color == PieceColor.Green || color == PieceColor.Purple)
            {
                // Green/Purple (Z-axis): Use bottom 3 Y-rows (Y=0,1,2), middle 4 X-columns (X=1,2,3,4)
                // Restricts placement to 12 positions (3 rows × 4 columns) to prevent checkmate on placement
                yMin = 0;
                yMax = 2;
                xMin = 1;
                xMax = 4;
            }
            // Yellow/Orange not used in 4-player 6x6x6 games
        }
        else if (boardSize == 8)
        {
            // 8x8x8 board: Middle 4x4 grid for all players
            if (color == PieceColor.White || color == PieceColor.Black)
            {
                // X-axis players: Middle YZ region
                yMin = 2;
                yMax = 5;
                zMin = 2;
                zMax = 5;
            }
            else if (color == PieceColor.Green || color == PieceColor.Purple)
            {
                // Z-axis players: Middle XY region
                xMin = 2;
                xMax = 5;
                yMin = 2;
                yMax = 5;
            }
            else if (color == PieceColor.Yellow || color == PieceColor.Orange)
            {
                // Y-axis players: Middle XZ region
                xMin = 2;
                xMax = 5;
                zMin = 2;
                zMax = 5;
            }
        }
        // else 4x4x4: No restrictions, each player gets their full slice

        return new PlacementZone(xMin, xMax, yMin, yMax, zMin, zMax);
    }

    /// <summary>
    /// Legacy method for backward compatibility - returns X-layer for a color
    /// </summary>
    public static int GetValidXForColor(PieceColor color)
    {
        PlacementZone zone = GetPlacementZoneForColor(color);
        return zone.xMin; // Since xMin == xMax for single-slice placement
    }

    /// <summary>
    /// Check if a board position is in the correct placement zone for the given piece color
    /// </summary>
    public static bool IsPositionInColorZone(BoardPosition position, PieceColor color)
    {
        PlacementZone zone = GetPlacementZoneForColor(color);
        return zone.Contains(position);
    }
    
    /// <summary>
    /// Set a player's readiness state for transitioning to gameplay
    /// </summary>
    public void SetPlayerReady(PieceColor playerColor, bool ready = true)
    {
        bool previousState = IsPlayerReady(playerColor);

        if (OnlineSession.IsActive)
        {
            if (!OnlineSession.CanControl(playerColor)) return;  // only the server marks the other side ready
            if (previousState && !ready) return;                  // can't take back "ready" online
            if (ready && !previousState && OnlineSession.ShouldSend(playerColor)) OnlineClient.Instance.SendReady(playerColor);
        }

        // Determine if this is human manual or AI auto-ready
        string playerType = TurnManager.Instance?.IsPlayerAI(playerColor) == true ? "AI (auto)" : "HUMAN (manual)";

        // Set readiness in dictionary (initialize if needed)
        playerReadiness[playerColor] = ready;

        Debug.Log($"🎯 PlacementManager: {playerColor} {playerType} readiness set to {ready}");

        // Log all player readiness states
        string readinessStatus = "";
        foreach (var kvp in playerReadiness)
        {
            readinessStatus += $"{kvp.Key}={kvp.Value} ";
        }
        Debug.Log($"  Current readiness: {readinessStatus}");
        Debug.Log($"  All players ready: {AreAllPlayersReady()}");

        // Fire event if readiness changed
        if (previousState != ready)
        {
            OnPlayerReadinessChanged?.Invoke(playerColor);
            Debug.Log($"🎯 PlacementManager: Fired OnPlayerReadinessChanged for {playerColor}");
        }

        // Check if we should transition to gameplay now
        if (ready && AreAllPlayersReady())
        {
            Debug.Log("🚀 PlacementManager: All players are now ready - checking if transition should occur");
            CheckPlacementCompletion();
        }
        else
        {
            Debug.Log($"🎯 PlacementManager: Not yet transitioning - waiting for all players to be ready");
        }
    }
    
    /// <summary>
    /// Check if a specific player is ready for gameplay
    /// </summary>
    public bool IsPlayerReady(PieceColor playerColor)
    {
        return playerReadiness.ContainsKey(playerColor) && playerReadiness[playerColor];
    }

    /// <summary>
    /// Check if all active players are ready for gameplay (supports 2-6 players)
    /// </summary>
    public bool AreAllPlayersReady()
    {
        if (PlayerManager.Instance == null)
        {
            Debug.LogWarning("PlacementManager.AreAllPlayersReady: PlayerManager.Instance is null, falling back to White+Black only");
            return IsPlayerReady(PieceColor.White) && IsPlayerReady(PieceColor.Black);
        }

        // Get all active player colors
        List<PieceColor> activePlayers = PlayerManager.Instance.GetActivePlayers();

        if (activePlayers.Count == 0)
        {
            Debug.LogWarning("PlacementManager.AreAllPlayersReady: No active players found");
            return false;
        }

        // Check if all active players are ready
        foreach (PieceColor playerColor in activePlayers)
        {
            if (!IsPlayerReady(playerColor))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Legacy method for backward compatibility - checks if both White and Black are ready
    /// </summary>
    [System.Obsolete("Use AreAllPlayersReady() instead for multi-player support")]
    public bool AreBothPlayersReady()
    {
        return IsPlayerReady(PieceColor.White) && IsPlayerReady(PieceColor.Black);
    }
    
    /// <summary>
    /// Reset player readiness states (called when entering placement phase)
    /// </summary>
    public void ResetPlayerReadiness()
    {
        playerReadiness.Clear();
        Debug.Log("🔄 PlacementManager: Player readiness dictionary cleared for all players");
    }
    
    private void Awake()
    {
        Debug.Log("🚀 PlacementManager.Awake: ENTRY - PlacementManager GameObject is instantiating");
        Debug.Log($"🚀 PlacementManager.Awake: GameObject name = {gameObject.name}");
        Debug.Log($"🚀 PlacementManager.Awake: GameObject active = {gameObject.activeInHierarchy}");
        Debug.Log($"🚀 PlacementManager.Awake: Component enabled = {enabled}");
        Debug.Log($"🚀 PlacementManager.Awake: Time = {Time.time:F2}s");
        
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("✅ PlacementManager.Awake: Singleton instance created successfully");
        }
        else
        {
            Debug.LogWarning("⚠️ PlacementManager.Awake: Multiple instances detected, destroying duplicate");
            Debug.LogWarning($"⚠️ Existing instance: {Instance.gameObject.name}, New instance: {gameObject.name}");
            Destroy(gameObject);
            return;
        }
        
        Debug.Log("🚀 PlacementManager.Awake: Awake complete, Start() should be called next");
    }
    
    private void Start()
    {
        Debug.Log("🚀 PlacementManager.Start: ENTRY - Subscribing to game state changes");
        
        try
        {
            // Subscribe to game state changes
            if (GameStateManager.Instance != null)
            {
                Debug.Log("🔍 PlacementManager.Start: GameStateManager.Instance found, subscribing to OnStateChanged event");
                GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
                Debug.Log("✅ PlacementManager.Start: Successfully subscribed to OnStateChanged event");
                
                // CRITICAL FIX: Check if we're already in PiecePlacement state when subscribing
                // This handles the timing issue where state changed before we subscribed
                GameState currentState = GameStateManager.Instance.currentState;
                Debug.Log($"🔍 PlacementManager.Start: Current game state is {currentState}");
                
                if (currentState == GameState.PiecePlacement)
                {
                    Debug.LogWarning("⚠️ TIMING FIX: Already in PiecePlacement state - triggering StartPlacementPhase() manually");
                    Debug.LogWarning("⚠️ This fixes the issue where state changed before PlacementManager could subscribe");
                    
                    Debug.Log("🚀 PlacementManager.Start: About to call StartPlacementPhase() for timing fix");
                    StartPlacementPhase();
                    Debug.Log("🚀 PlacementManager.Start: StartPlacementPhase() call completed");
                }
                else
                {
                    Debug.Log($"🔍 PlacementManager.Start: Not in PiecePlacement state yet ({currentState}), will wait for state change event");
                }
            }
            else
            {
                Debug.LogError("🚨 PlacementManager.Start: GameStateManager.Instance is NULL!");
                Debug.LogError("🚨 Cannot subscribe to state changes - PlacementManager will not function!");
            }
            
            Debug.Log("🚀 PlacementManager.Start: Subscription and state check complete - SUCCESS");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"💀 PlacementManager.Start: EXCEPTION occurred during initialization!");
            Debug.LogError($"💀 Exception: {ex.GetType().Name}: {ex.Message}");
            Debug.LogError($"💀 StackTrace: {ex.StackTrace}");
            Debug.LogError($"💀 This exception prevents PlacementManager from working properly!");
        }
    }
    
    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
    }
    
    /// <summary>
    /// Handle game state changes
    /// </summary>
    private void OnGameStateChanged(GameState newState)
    {
        Debug.Log($"🔔 PlacementManager.OnGameStateChanged: State changed to {newState}");
        
        if (newState == GameState.PiecePlacement)
        {
            Debug.Log("🚀 PlacementManager.OnGameStateChanged: Entering PiecePlacement state - calling StartPlacementPhase()");
            StartPlacementPhase();
        }
        else
        {
            Debug.Log($"🚀 PlacementManager.OnGameStateChanged: Exiting PiecePlacement state (now {newState}) - calling EndPlacementPhase()");
            EndPlacementPhase();
        }
    }
    
    /// <summary>
    /// Start the placement phase
    /// </summary>
    private void StartPlacementPhase()
    {
        Debug.Log("🚀 PlacementManager.StartPlacementPhase: ENTRY - Starting placement phase");
        
        // Reset player readiness states when entering placement phase
        ResetPlayerReadiness();
        
        // Ensure trays are visible during placement phase
        PieceTray.ShowAllTrays();
        Debug.Log("PlacementManager: Trays shown for placement phase");
        
        // DIAGNOSTIC: Check game mode and AI status
        Debug.Log($"🔍 PLACEMENT DIAGNOSTICS:");
        Debug.Log($"  TurnManager.Instance: {(TurnManager.Instance != null ? "EXISTS" : "NULL")}");
        
        if (TurnManager.Instance != null)
        {
            Debug.Log($"  Game Mode: {TurnManager.Instance.GetGameModeDescription()}");
            Debug.Log($"  White IsAI: {TurnManager.Instance.IsPlayerAI(PieceColor.White)}");
            Debug.Log($"  Black IsAI: {TurnManager.Instance.IsPlayerAI(PieceColor.Black)}");
        }
        
        Debug.Log($"  AIPlayer.Instance: {(AIPlayer.Instance != null ? "EXISTS" : "NULL")}");
        Debug.Log($"  GameState: {(GameStateManager.Instance?.currentState.ToString() ?? "NULL")}");
        
        // TIMING FIX: Don't trigger AI placement immediately - wait for pieces to be created first
        // GameManager will call NotifyPiecesReady() when pieces are created in trays
        Debug.Log("⏳ PlacementManager.StartPlacementPhase: Waiting for GameManager to create pieces in trays before triggering AI placement");
    }
    
    /// <summary>
    /// End the placement phase and clean up
    /// </summary>
    private void EndPlacementPhase()
    {
        Debug.Log("PlacementManager: Ending placement phase");
        
        if (selectedTrayPiece != null)
        {
            DeselectTrayPiece();
        }
        
        ClearPlacementIndicators();
        
        // Clean up any remaining repositioning tracking
        if (repositioningPieces.Count > 0)
        {
            Debug.Log($"PlacementManager: Cleaning up {repositioningPieces.Count} repositioning pieces");
            repositioningPieces.Clear();
        }
    }
    
    /// <summary>
    /// Handle clicking on a piece in a tray
    /// </summary>
    public void OnTrayPieceClicked(ChessPiece piece)
    {
        if (!OnlineSession.CanControl(piece.pieceColor)) return; // the other player's pieces
        Debug.Log($"🎯 PlacementManager: Tray piece clicked - {piece.pieceColor} {piece.pieceType}");
        
        // SELECTION STATE DEBUGGING: Identify if this is human or AI selection
        bool isPlayerHuman = TurnManager.Instance != null && !TurnManager.Instance.IsPlayerAI(piece.pieceColor);
        Debug.Log($"🔍 SELECTION CONTEXT: {piece.pieceColor} piece - Player is {(isPlayerHuman ? "HUMAN" : "AI")}");
        Debug.Log($"🔍 AI placement in progress: {isAIPlacementInProgress}");
        Debug.Log($"🔍 Current selectedTrayPiece: {(selectedTrayPiece != null ? $"{selectedTrayPiece.pieceColor} {selectedTrayPiece.pieceType}" : "NONE")}");
        Debug.Log($"🔍 Saved human selection: {(savedHumanSelectedPiece != null ? $"{savedHumanSelectedPiece.pieceColor} {savedHumanSelectedPiece.pieceType}" : "NONE")}");
        
        // DIAGNOSTIC: Log piece position BEFORE any processing
        Debug.Log($"🔍 BEFORE SELECTION - {piece.pieceColor} {piece.pieceType}:");
        Debug.Log($"    Transform position: {piece.transform.position}");
        Debug.Log($"    Transform local position: {piece.transform.localPosition}");  
        Debug.Log($"    Transform parent: {(piece.transform.parent != null ? piece.transform.parent.name : "None")}");
        Debug.Log($"    Is child of White Tray: {piece.transform.IsChildOf(PieceTray.WhiteTray?.transform)}");
        Debug.Log($"    Is child of Black Tray: {piece.transform.IsChildOf(PieceTray.BlackTray?.transform)}");
        
        if (!GameStateManager.Instance.CanPlacePieces())
        {
            Debug.LogWarning("PlacementManager: Cannot place pieces - not in placement phase");
            return;
        }
        
        // OWNERSHIP VALIDATION: Prevent humans from selecting AI player pieces
        if (!isPlayerHuman)
        {
            Debug.LogWarning($"🚫 PlacementManager: Human player cannot select AI {piece.pieceColor} piece during placement");
            ShowHumanAIInteractionFeedback(piece.pieceColor);
            return;
        }
        
        Debug.Log($"✅ PlacementManager: Human player authorized to select {piece.pieceColor} piece");
        
        // ASYNCHRONOUS PLACEMENT: Both players can place pieces simultaneously
        // No turn restrictions - any player can place pieces at any time during placement phase
        PieceTray pieceTray = PieceTray.GetTrayForColor(piece.pieceColor);
        bool isPieceInTray = (pieceTray != null && piece.transform.IsChildOf(pieceTray.transform));
        
        if (isPieceInTray)
        {
            Debug.Log($"🔄 PlacementManager: Allowing simultaneous placement of {piece.pieceColor} {piece.pieceType} from tray");
        }
        else
        {
            Debug.Log($"🔄 PlacementManager: Allowing repositioning of already placed {piece.pieceColor} {piece.pieceType}");
        }
        
        // Select the piece for placement (from tray or for repositioning)
        SelectPieceForPlacement(piece);
    }
    
    /// <summary>
    /// Select a piece for placement (from tray or for repositioning from board)
    /// </summary>
    private void SelectPieceForPlacement(ChessPiece piece)
    {
        Debug.Log($"🔒 PlacementManager: Starting piece selection for {piece.pieceColor} {piece.pieceType}");
        
        // Deselect previous piece if any
        if (selectedTrayPiece != null)
        {
            DeselectTrayPiece();
        }
        
        // DIAGNOSTIC: Log piece position BEFORE selection
        Debug.Log($"🔍 BEFORE piece.OnSelected() - {piece.pieceColor} {piece.pieceType}:");
        Debug.Log($"    Transform position: {piece.transform.position}");
        Debug.Log($"    Transform local position: {piece.transform.localPosition}");
        
        selectedTrayPiece = piece;

        // Handle repositioning: if piece is on board, temporarily remove it
        PieceTray selectedPieceTray = PieceTray.GetTrayForColor(piece.pieceColor);
        bool isPieceInTray = (selectedPieceTray != null && piece.transform.IsChildOf(selectedPieceTray.transform));
        
        if (!isPieceInTray && piece.CurrentPosition.IsValid())
        {
            Debug.Log($"🔄 PlacementManager: Temporarily removing {piece.pieceColor} {piece.pieceType} from board position {piece.CurrentPosition} for repositioning");
            
            // Remove from board
            ChessBoard.Instance?.SetPieceAt(piece.CurrentPosition, null);
            
            // Reset piece position to invalid (will be set when placed again)
            piece.SetCurrentPosition(new BoardPosition(-1, -1, -1));
        }
        
        // Add visual feedback to selected piece
        piece.OnSelected();
        
        // DIAGNOSTIC: Log piece position AFTER OnSelected() 
        Debug.Log($"🔍 AFTER piece.OnSelected() - {piece.pieceColor} {piece.pieceType}:");
        Debug.Log($"    Transform position: {piece.transform.position}");
        Debug.Log($"    Transform local position: {piece.transform.localPosition}");
        Debug.Log($"    Transform parent: {(piece.transform.parent != null ? piece.transform.parent.name : "None")}");
        
        // Show valid placement positions
        ShowValidPlacementPositions();
        
        // DIAGNOSTIC: Log piece position AFTER ShowValidPlacementPositions()
        Debug.Log($"🔍 AFTER ShowValidPlacementPositions() - {piece.pieceColor} {piece.pieceType}:");
        Debug.Log($"    Transform position: {piece.transform.position}");
        Debug.Log($"    Transform local position: {piece.transform.localPosition}");
        
        Debug.Log($"✅ PlacementManager: Selected {piece.pieceColor} {piece.pieceType} for placement");
        Debug.Log($"    Piece remained in tray at position: {piece.transform.position}");
        Debug.Log($"    No unwanted movement occurred during selection process");
        
        // FIX APPLIED: Removed automatic tray validation after piece selection
        // ISSUE: PieceTray.ValidateAndRecoverAllTrayPieces() was being called after every piece selection,
        //        causing pieces to be moved from their tray positions before actual placement.
        // SOLUTION: Tray validation should only occur in genuine emergency situations, not during 
        //           normal piece selection workflow. This ensures pieces stay exactly where they are
        //           in the tray until they're actually placed on the board.
        
        Debug.Log($"🔓 PlacementManager: Finished piece selection for {piece.pieceColor} {piece.pieceType}");
    }
    
    /// <summary>
    /// Deselect the currently selected tray piece
    /// </summary>
    // ── Always-visible placement squares ──
    // During setup, the squares the player in focus can use are shown even before a piece is
    // picked up. Picking up a piece shows that piece's squares as before.

    private PieceColor? previewColor;
    private int previewSignature = -1;
    private float nextPreviewCheck;

    /// <summary>The player whose squares are shown with nothing picked up</summary>
    private static PieceColor? FocusPlayer()
    {
        if (UI_WorkingOn.HasValue) return UI_WorkingOn;
        if (OnlineSession.IsActive) return OnlineSession.LocalColor;
        return null;
    }

    private static PieceColor? UI_WorkingOn => PlacementUI.WorkingOn;

    private void LateUpdate()
    {
        bool placing = GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces() && ChessBoard.Instance != null;
        if (!placing || selectedTrayPiece != null || IsAutoPlacing)
        {
            if (previewColor.HasValue && selectedTrayPiece == null) ClearPlacementIndicators();
            previewColor = null;
            previewSignature = -1;
            return;
        }
        PieceColor? color = FocusPlayer();
        if (color.HasValue && (IsPlayerReady(color.Value) || !OnlineSession.CanControl(color.Value) || NextBenchPiece(color.Value) == null))
            color = null;
        if (!color.HasValue)
        {
            if (previewColor.HasValue) ClearPlacementIndicators();
            previewColor = null;
            previewSignature = -1;
            return;
        }

        // Rebuild only when the player or the empty squares change (checked a few times a second)
        if (color == previewColor && Time.unscaledTime < nextPreviewCheck && placementIndicators.Count > 0) return;
        nextPreviewCheck = Time.unscaledTime + 0.15f;
        List<BoardPosition> squares = ZoneSquares(color.Value);
        int signature = (int)color.Value * 7919 + squares.Count;
        foreach (BoardPosition p in squares) signature = signature * 31 + p.x * 100 + p.y * 10 + p.z;
        if (color == previewColor && signature == previewSignature && placementIndicators.Count > 0) return;
        previewColor = color;
        previewSignature = signature;
        ClearPlacementIndicators();
        foreach (BoardPosition p in squares) CreatePlacementIndicator(p);
    }

    private static List<BoardPosition> ZoneSquares(PieceColor color)
    {
        var list = new List<BoardPosition>();
        PlacementZone zone = GetPlacementZoneForColor(color);
        for (int x = zone.xMin; x <= zone.xMax; x++)
        for (int y = zone.yMin; y <= zone.yMax; y++)
        for (int z = zone.zMin; z <= zone.zMax; z++)
        {
            var p = new BoardPosition(x, y, z);
            if (ChessBoard.Instance.GetPieceAt(p) == null) list.Add(p); // the zone is already the colour's
        }
        return list;
    }

    private static readonly ChessPieceType[] PlacementOrder =
        { ChessPieceType.King, ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Pawn };

    private static ChessPiece NextBenchPiece(PieceColor color)
    {
        PieceTray tray = PieceTray.GetTrayForColor(color);
        if (tray == null) return null;
        ChessPiece[] pieces = tray.GetComponentsInChildren<ChessPiece>();
        foreach (ChessPieceType t in PlacementOrder)
            foreach (ChessPiece p in pieces)
                if (p.pieceType == t) return p;
        return null;
    }

    /// <summary>A piece already on the board is picked up (so it can go back to the bench)</summary>
    public bool CanReturnSelectedToTray => selectedTrayPiece != null && repositioningPieces.ContainsKey(selectedTrayPiece);

    /// <summary>Take the picked-up board piece off the board and put it back on its bench</summary>
    public void ReturnSelectedToTray()
    {
        if (!CanReturnSelectedToTray) return;
        ChessPiece piece = selectedTrayPiece;
        BoardPosition pos = repositioningPieces[piece];
        repositioningPieces.Remove(piece);
        piece.OnDeselected();
        selectedTrayPiece = null;
        ClearPlacementIndicators();
        if (OnlineSession.ShouldSend(piece.pieceColor)) OnlineClient.Instance.SendUnplacePiece(piece.pieceColor, pos);
        ReturnToTray(piece, pos);
    }

    /// <summary>Take a piece off the board and back onto its bench (yours, or another device's in online setup)</summary>
    public void ReturnToTray(ChessPiece piece, BoardPosition pos)
    {
        if (piece == null) return;
        if (ChessBoard.Instance != null && pos.IsValid() && ChessBoard.Instance.GetPieceAt(pos) == piece)
            ChessBoard.Instance.SetPieceAt(pos, null);
        piece.SetCurrentPosition(new BoardPosition(-1, -1, -1));
        PieceTray tray = PieceTray.GetTrayForColor(piece.pieceColor);
        if (tray != null) tray.ReturnPiece(piece);
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
    }

    public void DeselectTrayPiece()
    {
        if (selectedTrayPiece != null)
        {
            // SELECTION STATE DEBUGGING: Log deselection context
            bool isPlayerHuman = TurnManager.Instance != null && !TurnManager.Instance.IsPlayerAI(selectedTrayPiece.pieceColor);
            Debug.Log($"🔍 DESELECTION: {selectedTrayPiece.pieceColor} {selectedTrayPiece.pieceType} - Player is {(isPlayerHuman ? "HUMAN" : "AI")}");
            Debug.Log($"🔍 AI placement in progress during deselection: {isAIPlacementInProgress}");
            Debug.Log($"🔍 Saved human selection during deselection: {(savedHumanSelectedPiece != null ? $"{savedHumanSelectedPiece.pieceColor} {savedHumanSelectedPiece.pieceType}" : "NONE")}");
            
            // If this was a repositioning piece that wasn't placed, restore it
            RestoreRepositioningPieceIfNeeded();
            
            selectedTrayPiece.OnDeselected();
            
            // Clear the selection
            ChessPiece previousSelection = selectedTrayPiece;
            selectedTrayPiece = null;
            
            ClearPlacementIndicators();
            Debug.Log($"PlacementManager: Deselected {previousSelection.pieceColor} {previousSelection.pieceType}");
        }
    }
    
    // Track pieces being repositioned to ensure they don't get lost
    private Dictionary<ChessPiece, BoardPosition> repositioningPieces = new Dictionary<ChessPiece, BoardPosition>();
    
    /// <summary>
    /// Handle clicking on a board piece during placement phase for repositioning
    /// </summary>
    public void OnBoardPieceClickedForRepositioning(ChessPiece piece)
    {
        if (piece == null)
        {
            Debug.LogError("PlacementManager: Cannot reposition null piece");
            return;
        }
        // Online: only your own pieces, until you're ready (the server is told when they move)
        if (OnlineSession.IsActive && (!OnlineSession.CanControl(piece.pieceColor) || IsPlayerReady(piece.pieceColor))) return;
        
        Debug.Log($"🔄 PlacementManager: Board piece clicked for repositioning - {piece.pieceColor} {piece.pieceType} at {piece.CurrentPosition}");
        
        // OWNERSHIP VALIDATION: Prevent humans from repositioning AI player pieces
        bool isPlayerHuman = TurnManager.Instance != null && !TurnManager.Instance.IsPlayerAI(piece.pieceColor);
        if (!isPlayerHuman)
        {
            Debug.LogWarning($"🚫 PlacementManager: Human player cannot reposition AI {piece.pieceColor} piece during placement");
            ShowHumanAIInteractionFeedback(piece.pieceColor);
            return;
        }
        
        Debug.Log($"✅ PlacementManager: Human player authorized to reposition {piece.pieceColor} piece");
        
        // First, validate and repair position if needed
        if (!piece.ValidatePosition())
        {
            Debug.LogWarning($"🔄 Position validation failed for {piece.pieceColor} {piece.pieceType}, attempting repair...");
            if (!piece.RepairPosition())
            {
                Debug.LogError($"🔄 ❌ Cannot reposition piece - position repair failed for {piece.pieceColor} {piece.pieceType}");
                Debug.LogError($"🔄 Piece thinks it's at {piece.CurrentPosition}, but this position is not synchronized with the board");
                return;
            }
            Debug.Log($"🔄 ✅ Position repair successful for {piece.pieceColor} {piece.pieceType}");
        }
        
        // Store original position for recovery if needed (now guaranteed to be valid)
        BoardPosition originalPos = piece.CurrentPosition;
        if (!originalPos.IsValid())
        {
            Debug.LogError($"🔄 ❌ Cannot reposition piece with invalid position {originalPos} even after repair attempt");
            return;
        }
        
        Debug.Log($"🔄 Proceeding with repositioning {piece.pieceColor} {piece.pieceType} from validated position {originalPos}");
        
        // Check if this piece is already being repositioned (prevent double-processing)
        if (repositioningPieces.ContainsKey(piece))
        {
            Debug.LogWarning($"🔄 ⚠️ Piece {piece.pieceColor} {piece.pieceType} is already being repositioned - ignoring duplicate click");
            return;
        }
        
        // Check if this piece is already selected (deselect if so)
        if (selectedTrayPiece == piece)
        {
            Debug.Log($"🔄 Piece {piece.pieceColor} {piece.pieceType} is already selected - deselecting");
            DeselectTrayPiece();
            return;
        }
        
        // First, deselect any currently selected tray piece and restore previous repositioning piece if any
        if (selectedTrayPiece != null)
        {
            Debug.Log($"🔄 Deselecting previously selected piece: {selectedTrayPiece.pieceColor} {selectedTrayPiece.pieceType}");
            RestoreRepositioningPieceIfNeeded();
            DeselectTrayPiece();
        }
        
        // Store the original position for recovery
        repositioningPieces[piece] = originalPos;
        Debug.Log($"🔄 Stored original position {originalPos} for recovery");
        
        // NEW APPROACH: Keep piece in place, just highlight it
        // Do NOT remove from board until actually placing elsewhere
        // Do NOT change piece position - it stays where it is
        
        // Select this piece for placement (same as tray piece selection)
        selectedTrayPiece = piece;
        piece.OnSelected(); // This just adds visual highlighting
        
        // Show valid placement positions using green indicators
        ShowValidPlacementPositions();
        
        Debug.Log($"🔄 ✨ {piece.pieceColor} {piece.pieceType} highlighted for repositioning from {originalPos} (piece stays in place until placed)");
    }
    
    /// <summary>
    /// Restore a repositioning piece to its original position if placement was cancelled
    /// </summary>
    private void RestoreRepositioningPieceIfNeeded()
    {
        if (selectedTrayPiece != null && repositioningPieces.ContainsKey(selectedTrayPiece))
        {
            BoardPosition originalPos = repositioningPieces[selectedTrayPiece];
            Debug.Log($"🔄 PlacementManager: Cancelling repositioning for {selectedTrayPiece.pieceColor} {selectedTrayPiece.pieceType} (was at {originalPos})");
            
            // Since piece never moved during selection, no restoration needed!
            // Piece is already in correct position, just remove from tracking
            repositioningPieces.Remove(selectedTrayPiece);
            
            Debug.Log($"🔄 ✅ Repositioning cancelled - {selectedTrayPiece.pieceColor} {selectedTrayPiece.pieceType} remains at {originalPos}");
        }
    }
    
    /// <summary>
    /// Emergency cleanup method to recover any pieces that may be stuck in repositioning state
    /// </summary>
    public void CleanupRepositioningPieces()
    {
        Debug.Log($"🧹 PlacementManager: Emergency cleanup - checking {repositioningPieces.Count} repositioning pieces");
        
        var piecesToRestore = new List<ChessPiece>(repositioningPieces.Keys);
        foreach (var piece in piecesToRestore)
        {
            if (piece != null && repositioningPieces.ContainsKey(piece))
            {
                BoardPosition originalPos = repositioningPieces[piece];
                Debug.LogWarning($"🧹 Found stuck repositioning piece: {piece.pieceColor} {piece.pieceType} (original: {originalPos})");
                
                // Temporarily select the piece to restore it using existing logic
                ChessPiece previousSelection = selectedTrayPiece;
                selectedTrayPiece = piece;
                RestoreRepositioningPieceIfNeeded();
                selectedTrayPiece = previousSelection;
                
                Debug.Log($"🧹 Restored stuck piece: {piece.pieceColor} {piece.pieceType}");
            }
        }
        
        Debug.Log($"🧹 Cleanup complete - {repositioningPieces.Count} pieces remaining in repositioning state");
    }
    
    /// <summary>
    /// Show indicators for valid placement positions
    /// </summary>
    private void ShowValidPlacementPositions()
    {
        ClearPlacementIndicators();
        
        // CRITICAL: Clear any existing move indicators from InputManager to avoid yellow/green conflicts
        if (InputManager.Instance != null)
        {
            InputManager.Instance.ClearMoveIndicators();
            Debug.Log("PlacementManager: Cleared conflicting move indicators to ensure consistent green placement indicators");
        }
        
        if (selectedTrayPiece == null || ChessBoard.Instance == null)
        {
            Debug.LogError($"🚨 ShowValidPlacementPositions: EARLY RETURN - selectedTrayPiece={(selectedTrayPiece == null ? "NULL" : "valid")}, ChessBoard.Instance={(ChessBoard.Instance == null ? "NULL" : "valid")}");
            return;
        }
        
        Debug.Log("PlacementManager: Showing valid placement positions");
        
        // Get all positions that are valid for piece placement (empty squares only)
        var validPlacementPositions = GetValidPlacementPositions();
        Debug.Log($"🎯 ShowValidPlacementPositions: GetValidPlacementPositions returned {validPlacementPositions.Count} positions");

        foreach (var position in validPlacementPositions)
        {
            Debug.Log($"🔵 Creating indicator {placementIndicators.Count + 1} at position {position}");
            try
            {
                CreatePlacementIndicator(position);
                Debug.Log($"🔵 Indicator created successfully, total count now: {placementIndicators.Count}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"❌ FAILED to create indicator at {position}: {ex.Message}");
                Debug.LogError($"   Exception: {ex}");
                Debug.LogError($"   Stack trace: {ex.StackTrace}");
            }
        }
        
        Debug.Log($"PlacementManager: Created {placementIndicators.Count} placement indicators");
    }
    
    /// <summary>
    /// Get all valid positions for piece placement during placement phase.
    /// Uses strict placement validation - only empty squares in the correct Z-layer are allowed.
    /// </summary>
    private List<BoardPosition> GetValidPlacementPositions()
    {
        List<BoardPosition> validPositions = new List<BoardPosition>();
        
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("PlacementManager: ChessBoard.Instance is null, cannot get valid placement positions");
            return validPositions;
        }
        
        if (selectedTrayPiece == null)
        {
            Debug.LogError("PlacementManager: No piece selected, cannot determine valid placement zone");
            return validPositions;
        }
        
        // Get the valid placement zone for the selected piece color
        PlacementZone zone = GetPlacementZoneForColor(selectedTrayPiece.pieceColor);

        Debug.Log($"PlacementManager: Finding valid positions for {selectedTrayPiece.pieceColor} piece");
        Debug.Log($"  Placement zone: X=[{zone.xMin}-{zone.xMax}], Y=[{zone.yMin}-{zone.yMax}], Z=[{zone.zMin}-{zone.zMax}]");

        // BOARD STATE DIAGNOSTIC: Check if board is in expected state for initial placement
        Debug.Log($"🔍 BOARD STATE: About to validate placement positions");
        Debug.Log($"🔍 Selected piece: {selectedTrayPiece.pieceColor} {selectedTrayPiece.pieceType}");
        Debug.Log($"🔍 Piece CurrentPosition: {selectedTrayPiece.CurrentPosition}");
        Debug.Log($"🔍 Piece transform parent: {(selectedTrayPiece.transform.parent != null ? selectedTrayPiece.transform.parent.name : "None")}");

        // CRITICAL DIAGNOSTIC: Check if tray pieces have invalid positions as they should
        PieceTray diagnosticTray = PieceTray.GetTrayForColor(selectedTrayPiece.pieceColor);
        bool isTrayPiece = (diagnosticTray != null && selectedTrayPiece.transform.IsChildOf(diagnosticTray.transform));
        if (isTrayPiece && selectedTrayPiece.CurrentPosition.IsValid())
        {
            Debug.LogError($"🚨 INVALID STATE: Tray piece {selectedTrayPiece.pieceColor} {selectedTrayPiece.pieceType} has valid CurrentPosition {selectedTrayPiece.CurrentPosition}!");
            Debug.LogError($"🚨 Tray pieces should have INVALID positions, not valid board positions!");
            Debug.LogError($"🚨 This causes position repair to incorrectly place tray pieces on board!");
        }

        // Check what's actually on the board (use board dimensions, not hardcoded 4)
        Vector3Int dims = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        int occupiedPositions = 0;
        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece pieceAt = ChessBoard.Instance.GetPieceAt(pos);
                    if (pieceAt != null)
                    {
                        occupiedPositions++;
                        Debug.LogWarning($"🔍 OCCUPIED: Position {pos} has {pieceAt.pieceColor} {pieceAt.pieceType}");
                        Debug.LogWarning($"🔍   Piece parent: {(pieceAt.transform.parent != null ? pieceAt.transform.parent.name : "None")}");
                        Debug.LogWarning($"🔍   Piece CurrentPosition: {pieceAt.CurrentPosition}");

                        // Check if this is a tray piece incorrectly placed on board
                        PieceTray pieceAtTray = PieceTray.GetTrayForColor(pieceAt.pieceColor);
                        bool isPieceInTray = (pieceAtTray != null && pieceAt.transform.IsChildOf(pieceAtTray.transform));
                        if (isPieceInTray)
                        {
                            Debug.LogError($"🚨 CRITICAL: Piece at {pos} is STILL IN TRAY but registered on board!");
                            Debug.LogError($"🚨 This is the root cause of placement conflicts!");
                        }
                    }
                }
            }
        }
        Debug.Log($"🔍 BOARD STATE: {occupiedPositions} positions occupied on board (should be 0 for initial placement)");

        // Check only positions within the placement zone for this piece color
        int positionsChecked = 0;
        int positionsValidated = 0;
        int positionsAdded = 0;

        // Iterate through the zone's valid ranges
        for (int x = zone.xMin; x <= zone.xMax; x++)
        {
            for (int y = zone.yMin; y <= zone.yMax; y++)
            {
                for (int z = zone.zMin; z <= zone.zMax; z++)
                {
                    BoardPosition position = new BoardPosition(x, y, z);
                    positionsChecked++;

                    Debug.Log($"🔍 LOOP: Checking position {position} (iteration {positionsChecked})");

                    // Check if position is empty and in correct zone (enhanced placement validation)
                    bool canPlace = ChessBoard.Instance.CanPlacePieceAt(position, selectedTrayPiece.pieceColor);
                    Debug.Log($"🔍 VALIDATION: CanPlacePieceAt({position}) = {canPlace}");

                    if (canPlace)
                {
                    positionsValidated++;
                    Debug.Log($"🔍 ADDING: Adding position {position} to validPositions list (#{positionsValidated})");
                    
                    int countBefore = validPositions.Count;
                    validPositions.Add(position);
                    int countAfter = validPositions.Count;
                    positionsAdded++;
                    
                    Debug.Log($"🔍 LIST STATE: validPositions count before={countBefore}, after={countAfter}, total added={positionsAdded}");
                    
                    // Special tracking for the problematic position
                    if (position.x == 0 && position.y == 3 && position.z == 3)
                    {
                        Debug.LogWarning($"🎯 SPECIAL: Position (0,3,3) successfully added to list at index {countAfter - 1}");
                    }
                }
                else
                {
                    Debug.Log($"🔍 SKIPPED: Position {position} failed validation, not adding to list");
                }
                }
            }
        }

        Debug.Log($"🔍 SUMMARY: Checked {positionsChecked} positions, {positionsValidated} validated, {positionsAdded} added");
        Debug.Log($"🔍 FINAL: validPositions.Count = {validPositions.Count}");

        // Log all positions in the final list
        Debug.Log($"🔍 FINAL LIST CONTENTS:");
        for (int i = 0; i < validPositions.Count; i++)
        {
            Debug.Log($"  [{i}]: {validPositions[i]}");
        }

        Debug.Log($"PlacementManager: Found {validPositions.Count} valid placement positions for {selectedTrayPiece.pieceColor} in zone X=[{zone.xMin}-{zone.xMax}], Y=[{zone.yMin}-{zone.yMax}], Z=[{zone.zMin}-{zone.zMax}]");
        return validPositions;
    }
    
    /// <summary>
    /// Create a visual indicator for a valid placement position
    /// </summary>
    private void CreatePlacementIndicator(BoardPosition position)
    {
        try
        {
            GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        indicator.name = $"PlacementIndicator_{position.x}_{position.y}_{position.z}";
        
        // CRITICAL FIX: Parent indicator to board container so it rotates with the board
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        if (piecesContainer != null)
        {
            indicator.transform.SetParent(piecesContainer.transform);
            
            // Use local positioning relative to the board container
            if (ChessBoard.Instance != null)
            {
                Vector3 localPos = ChessBoard.Instance.BoardToLocalPosition(position);
                indicator.transform.localPosition = localPos + Vector3.up * 0.5f; // Slightly above floor
                
                // DIAGNOSTIC: Log comprehensive position information
                Vector3 finalLocalPos = indicator.transform.localPosition;
                Vector3 finalWorldPos = indicator.transform.position;
                Debug.Log($"PlacementManager: Created indicator for {position}");
                Debug.Log($"  Calculated local position: {localPos}");
                Debug.Log($"  Final local position (with Y offset): {finalLocalPos}");
                Debug.Log($"  Final world position: {finalWorldPos}");
                Debug.Log($"  Distance from origin: {finalWorldPos.magnitude:F2}");
                
                // BOUNDS CHECK: Warn if indicator is positioned extremely far from board center
                if (finalWorldPos.magnitude > 20f)
                {
                    Debug.LogWarning($"⚠️ PlacementManager: Indicator for {position} is very far from board center!");
                    Debug.LogWarning($"   World position: {finalWorldPos} (distance: {finalWorldPos.magnitude:F2})");
                    Debug.LogWarning($"   This may cause visibility or interaction issues");
                }
                
                // SPECIAL CASE: Check for high-elevation positions that might be out of view
                if (finalLocalPos.y > 5f)
                {
                    Debug.LogWarning($"⚠️ PlacementManager: High elevation detected for {position}!");
                    Debug.LogWarning($"   Local Y position: {finalLocalPos.y} (may be outside camera view)");
                    Debug.LogWarning($"   Consider camera settings if not visible");
                }
            }
        }
        else
        {
            Debug.LogWarning("PlacementManager: Pieces Container not found - indicators may not rotate with board");
            
            // Fallback to world positioning if container not found
            if (ChessBoard.Instance != null)
            {
                Vector3 worldPos = ChessBoard.Instance.BoardToWorld(position);
                indicator.transform.position = worldPos + Vector3.up * 0.5f; // Slightly above floor
            }
        }
        
        // Large enough to see and tap on a phone screen
        indicator.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
        
        // Ensure rotation is normalized to prevent quaternion warnings
        indicator.transform.rotation = Quaternion.identity;
        
        // Remove collider and add placement data component
        Collider indicatorCollider = indicator.GetComponent<Collider>();
        if (indicatorCollider != null)
        {
            DestroyImmediate(indicatorCollider);
        }
        
        // Add a larger invisible collider for easier clicking
        SphereCollider clickCollider = indicator.AddComponent<SphereCollider>();
        clickCollider.radius = 1.0f; // Larger than the visual for easier touch targeting
        
        // Add component to identify this as a placement target
        PlacementTargetData targetData = indicator.AddComponent<PlacementTargetData>();
        targetData.targetPosition = position;
        
        // Apply material - create indicator with player's color
        Renderer renderer = indicator.GetComponent<Renderer>();
        PieceColor dotColor = selectedTrayPiece != null ? selectedTrayPiece.pieceColor : (previewColor ?? PieceColor.White);
        Color playerColor = GetColorForPieceColor(dotColor);
        Material coloredMaterial = CreatePlacementMaterial(playerColor);
        Debug.Log($"🎨 Material created - color: {coloredMaterial.color}, hasProperty(_BaseColor): {coloredMaterial.HasProperty("_BaseColor")}");
        if (coloredMaterial.HasProperty("_BaseColor"))
        {
            Debug.Log($"🎨 Material _BaseColor: {coloredMaterial.GetColor("_BaseColor")}");
        }
        renderer.material = coloredMaterial;
        
        placementIndicators.Add(indicator);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"💥 CreatePlacementIndicator EXCEPTION for {position}: {ex.Message}");
            Debug.LogError($"   Stack: {ex.StackTrace}");
            throw; // Re-throw to be caught by outer try-catch
        }
    }
    
    /// <summary>
    /// Get a working shader for indicators with fallback chain
    /// Uses the same approach as ThreatIndicatorManager for consistency
    /// </summary>
    private Shader GetIndicatorShader()
    {
        // Try shaders in priority order (same as check indicator system)
        string[] shaderNames = new string[]
        {
            "Transparent/Diffuse",
            "Legacy Shaders/Transparent/Diffuse",
            "Sprites/Default"
        };

        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                Debug.Log($"✅ PlacementManager: Found working shader: {shaderName}");
                return shader;
            }
            else
            {
                Debug.LogWarning($"PlacementManager: Shader '{shaderName}' not found, trying next fallback...");
            }
        }

        Debug.LogError("❌ PlacementManager: No suitable shader found! Indicators will fail to render.");
        return null;
    }

    /// <summary>
    /// Create a material for placement indicators
    /// FIXED: Use shader fallback system instead of hardcoded URP shader
    /// </summary>
    private Material CreatePlacementMaterial(Color color)
    {
        // FIXED: Use shader fallback system (same as check indicators)
        Shader indicatorShader = GetIndicatorShader();
        if (indicatorShader == null)
        {
            Debug.LogError($"🚨 PlacementManager: Cannot create material - no suitable shader found!");
            return null;
        }

        // Solid, unlit neon so the dots stay bright even behind the see-through floors
        // of the big cubes (eight stacked translucent floors washed out transparent dots)
        Shader unlit = Shader.Find("Unlit/Color");
        Material material = new Material(unlit != null ? unlit : indicatorShader);
        material.color = new Color(color.r, color.g, color.b, 1f);
        if (unlit == null) material.renderQueue = 3000;

        Debug.Log($"🎨 PlacementManager: Created placement material with shader {indicatorShader.name}, color {color}");

        return material;
    }

    /// <summary>
    /// Convert PieceColor enum to Unity Color for visual indicators
    /// </summary>
    private Color GetColorForPieceColor(PieceColor pieceColor)
    {
        return pieceColor switch
        {
            // The neon team colors (PieceSets.Tint), with White as bright cyan so it reads on pale floors
            PieceColor.White => NeonTheme.Cyan,
            _ => PieceSets.Tint(PieceSets.Kind.NeonGlow, pieceColor)
        };
    }

    /// <summary>
    /// Clear all placement indicators
    /// </summary>
    private void ClearPlacementIndicators()
    {
        int listCount = placementIndicators.Count;
        int destroyedFromList = 0;

        // Clear indicators tracked in our list
        foreach (var indicator in placementIndicators)
        {
            if (indicator != null)
            {
                // Immediately disable the indicator to make it invisible this frame
                // Destroy() schedules destruction at end of frame, so disable ensures instant visual clearing
                indicator.SetActive(false);
                Destroy(indicator);
                destroyedFromList++;
            }
        }
        placementIndicators.Clear();

        // NUCLEAR OPTION: Also find and destroy any orphaned indicators in the scene
        // This catches indicators that were created before our fixes and aren't in the list
        int orphanedIndicators = DestroyAllOrphanedIndicators();

        if (orphanedIndicators > 0)
        {
            Debug.LogWarning($"🧹 PlacementManager: Found and destroyed {orphanedIndicators} orphaned placement indicators!");
        }

        Debug.Log($"🧹 PlacementManager: Cleared {destroyedFromList} indicators from list (out of {listCount}), plus {orphanedIndicators} orphaned");
    }

    /// <summary>
    /// Find and destroy any placement or move indicators left in the scene that aren't in our lists
    /// This is a cleanup fallback for indicators that somehow got orphaned
    /// </summary>
    private int DestroyAllOrphanedIndicators()
    {
        int destroyedCount = 0;

        // Find all GameObjects in scene with "Indicator" in their name
        GameObject[] allObjects = FindObjectsOfType<GameObject>(true); // Include inactive objects

        foreach (GameObject obj in allObjects)
        {
            if (obj != null && (obj.name.Contains("PlacementIndicator") || obj.name.Contains("MoveIndicator")))
            {
                // Check if this indicator is in our list
                bool isInList = placementIndicators.Contains(obj);

                if (!isInList)
                {
                    // Orphaned indicator - destroy it immediately
                    obj.SetActive(false);
                    Destroy(obj);
                    destroyedCount++;
                    Debug.Log($"🧹 Found orphaned indicator: {obj.name} at {obj.transform.position}");
                }
            }
        }

        return destroyedCount;
    }
    
    /// <summary>
    /// Handle clicking on a placement indicator
    /// </summary>
    public void OnPlacementTargetClicked(BoardPosition targetPosition)
    {
        Debug.Log($"PlacementManager: Placement target clicked at {targetPosition}");
        
        if (selectedTrayPiece == null)
        {
            Debug.LogWarning("PlacementManager: No piece selected for placement");
            return;
        }
        
        if (!GameStateManager.Instance.CanPlacePieces())
        {
            Debug.LogWarning("PlacementManager: Cannot place pieces - not in placement phase");
            return;
        }
        
        // Attempt to place the piece
        PlacePieceOnBoard(selectedTrayPiece, targetPosition);
    }
    
    /// <summary>
    /// Place a piece from the tray onto the board
    /// </summary>
    private void PlacePieceOnBoard(ChessPiece piece, BoardPosition position)
    {
        Debug.Log($"PlacementManager: Placing {piece.pieceColor} {piece.pieceType} at {position}");
        
        // COMPREHENSIVE BOARD STATE DEBUG: Log current board state and tray contents
        DebugBoardAndTrayState(piece, position);
        
        // BOARD STATE CONSISTENCY CHECK: Verify board array matches piece positions
        ValidateBoardConsistency(piece);
        
        // DUPLICATE PIECE VALIDATION: Check for illegal duplicate placements
        if (!ValidateNoDuplicatePlacement(piece, position))
        {
            Debug.LogError($"❌ PlacementManager: BLOCKED - Cannot place duplicate {piece.pieceColor} {piece.pieceType}");
            return;
        }
        
        // Check if this is a repositioning piece that needs special handling
        bool isRepositioningPiece = repositioningPieces.ContainsKey(piece);
        BoardPosition originalPosition = new BoardPosition(-1, -1, -1); // Invalid position
        ChessPiece originalPositionPiece = null;

        if (isRepositioningPiece)
        {
            originalPosition = repositioningPieces[piece];
            Debug.Log($"🔄 PlacementManager: This is a repositioning piece from {originalPosition} to {position}");

            // CRITICAL FIX: Remove from repositioning dictionary immediately to prevent it from
            // staying marked as "repositioning" for future placements
            repositioningPieces.Remove(piece);
            Debug.Log($"🔄 PlacementManager: Cleared repositioning flag for {piece.pieceColor} {piece.pieceType}");
            
            // CRITICAL FIX: Temporarily clear the original position for clean validation
            // This prevents the repositioning piece from interfering with placement validation
            originalPositionPiece = ChessBoard.Instance.GetPieceAt(originalPosition);
            if (originalPositionPiece == piece)
            {
                ChessBoard.Instance.SetPieceAt(originalPosition, null);
                Debug.Log($"🔄 Temporarily cleared original position {originalPosition} for clean validation");
            }
        }
        
        // Now run clean validation without repositioning interference
        if (!ChessBoard.Instance.CanPlacePieceAt(position, piece.pieceColor))
        {
            // RESTORE: Put piece back at original position if validation fails
            if (isRepositioningPiece && originalPositionPiece == piece)
            {
                ChessBoard.Instance.SetPieceAt(originalPosition, piece);
                Debug.Log($"🔄 Restored piece to original position {originalPosition} after validation failure");
            }
            
            // ENHANCED ERROR REPORTING: Provide detailed reason for validation failure
            Debug.LogError($"❌ PlacementManager: PLACEMENT BLOCKED for {piece.pieceColor} {piece.pieceType} at {position}");
            Debug.LogError($"❌ Detailed validation failure analysis:");
            Debug.LogError($"   - Target position: {position}");
            Debug.LogError($"   - Required X-layer for {piece.pieceColor}: {GetValidXForColor(piece.pieceColor)}");
            Debug.LogError($"   - Actual X-layer: {position.x}");
            Debug.LogError($"   - Position in bounds: {ChessBoard.Instance.IsValidPosition(position)}");
            
            ChessPiece positionPiece = ChessBoard.Instance.GetPieceAt(position);
            if (positionPiece != null)
            {
                Debug.LogError($"   - Position occupied by: {positionPiece.pieceColor} {positionPiece.pieceType}");
            }
            else
            {
                Debug.LogError($"   - Position is empty");
            }
            
            if (isRepositioningPiece)
            {
                Debug.LogError($"   - This was a repositioning piece from: {originalPosition}");
            }
            else
            {
                Debug.LogError($"   - This was a fresh placement from tray");
            }
            
            return;
        }
        
        // Double-check that position is truly empty after cleaning repositioning interference
        ChessPiece existingPiece = ChessBoard.Instance.GetPieceAt(position);
        if (existingPiece != null)
        {
            // RESTORE: Put piece back at original position if there's still a conflict
            if (isRepositioningPiece && originalPositionPiece == piece)
            {
                ChessBoard.Instance.SetPieceAt(originalPosition, piece);
                Debug.Log($"🔄 Restored piece to original position {originalPosition} after conflict detection");
            }
            
            // ENHANCED ERROR REPORTING: Detailed conflict analysis
            Debug.LogError($"❌ PlacementManager: POSITION CONFLICT DETECTED");
            Debug.LogError($"❌ Attempted to place: {piece.pieceColor} {piece.pieceType} at {position}");
            Debug.LogError($"❌ Position occupied by: {existingPiece.pieceColor} {existingPiece.pieceType}");
            Debug.LogError($"❌ Conflict analysis:");
            Debug.LogError($"   - Same piece? {existingPiece == piece}");
            Debug.LogError($"   - Same color? {existingPiece.pieceColor == piece.pieceColor}");
            Debug.LogError($"   - Existing piece position: {existingPiece.CurrentPosition}");
            Debug.LogError($"   - Placement position: {position}");
            
            if (isRepositioningPiece)
            {
                Debug.LogError($"   - Repositioning from: {originalPosition}");
                Debug.LogError($"   - Original position piece: {(originalPositionPiece != null ? $"{originalPositionPiece.pieceColor} {originalPositionPiece.pieceType}" : "NULL")}");
            }
            
            return;
        }
        
        Debug.Log($"✅ PlacementManager: Placement validation passed - position {position} is empty and valid");

        // DIAGNOSTIC: Check if repositioning flag is blocking tray removal for AI pieces
        Debug.Log($"🔍 TRACE PlacementManager: About to check isRepositioningPiece for {piece.pieceColor} {piece.pieceType}");
        Debug.Log($"🔍 TRACE: isRepositioningPiece = {isRepositioningPiece}");
        Debug.Log($"🔍 TRACE: repositioningPieces.ContainsKey(piece) = {repositioningPieces.ContainsKey(piece)}");
        if (repositioningPieces.ContainsKey(piece))
        {
            Debug.Log($"🔍 TRACE: repositioningPieces[piece] = {repositioningPieces[piece]}");
        }

        // Remove piece from tray only if it's not a repositioning piece
        if (!isRepositioningPiece)
        {
            // Use PieceTrayManager for scalable tray access
            PieceTray tray = null;
            Debug.Log($"🔍 PlacementManager: Attempting to get tray for {piece.pieceColor}");
            Debug.Log($"🔍   PieceTrayManager.Instance = {(PieceTrayManager.Instance != null ? "EXISTS" : "NULL")}");

            if (PieceTrayManager.Instance != null)
            {
                tray = PieceTrayManager.Instance.GetTray(piece.pieceColor);
                Debug.Log($"🔍   PieceTrayManager.GetTray({piece.pieceColor}) = {(tray != null ? $"FOUND (trayColor={tray.trayColor})" : "NULL")}");
            }
            else
            {
                // Fallback for 2-player games
                tray = (piece.pieceColor == PieceColor.White) ? PieceTray.WhiteTray : PieceTray.BlackTray;
                Debug.Log($"🔍   Using fallback static tray for {piece.pieceColor}: {(tray != null ? "FOUND" : "NULL")}");
            }

            // CRITICAL: Always try GetTrayForColor() as secondary lookup if primary failed
            if (tray == null)
            {
                Debug.LogError($"🚨 PlacementManager: Primary tray lookup returned NULL for {piece.pieceColor}!");
                Debug.LogError($"🚨 Attempting secondary lookup via PieceTray.GetTrayForColor()...");
                tray = PieceTray.GetTrayForColor(piece.pieceColor);
                Debug.LogError($"🚨 Secondary lookup result: {(tray != null ? $"FOUND (trayColor={tray.trayColor})" : "STILL NULL")}");
            }

            if (tray != null)
            {
                Debug.Log($"🎯 PlacementManager: About to call RemovePiece on {tray.trayColor} tray for {piece.pieceColor} {piece.pieceType}");
                bool removed = tray.RemovePiece(piece);
                Debug.Log($"PlacementManager: RemovePiece returned {removed} for {piece.pieceColor} {piece.pieceType}");
            }
            else
            {
                Debug.LogError($"🚨🚨🚨 CRITICAL: Tray is NULL for {piece.pieceColor} - piece will NOT be removed from tray!");
                Debug.LogError($"🚨 This means GetPieceCount() will never decrement and AI will think pieces remain!");
                Debug.LogError($"🚨 Piece being placed: {piece.pieceColor} {piece.pieceType} at {position}");
                Debug.LogError($"🚨 Check if PieceTray.Initialize() was called for {piece.pieceColor} tray");
                Debug.LogError($"🚨 Check if PieceTrayManager.RegisterTray() was called for {piece.pieceColor}");
            }
        }
        else
        {
            // This is a repositioning piece - original position was already cleared during validation
            Debug.Log($"🔄 PlacementManager: Completing repositioning from {originalPosition} to {position}");
            
            // The original position was already cleared during validation above
            // Repositioning already cleared above (line 1143)
            Debug.Log($"🔄 ✅ Completed repositioning of {piece.pieceColor} {piece.pieceType} from {originalPosition} to {position}");
        }
        
        // Find the pieces container to parent the piece to
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        if (piecesContainer != null)
        {
            piece.transform.SetParent(piecesContainer.transform);
        }
        
        // Initialize or reposition the piece at the board position
        bool moved = false;
        BoardPosition movedFrom = default;
        if (repositioningPieces.ContainsKey(piece))
        {
            // This is a repositioning - update position directly without re-initializing
            BoardPosition originalPos = repositioningPieces[piece];
            moved = true;
            movedFrom = originalPos;
            Debug.Log($"🔄 PlacementManager: Completing repositioning from {originalPos} to {position}");
            
            // Update piece position and board registration
            piece.SetCurrentPosition(position);
            
            // Update transform position
            if (ChessBoard.Instance != null)
            {
                Vector3 localPos = ChessBoard.Instance.BoardToLocalPosition(position);
                piece.transform.localPosition = localPos;
                
                // Register piece at new position
                ChessBoard.Instance.SetPieceAt(position, piece);
            }
            
            // Repositioning already cleared above (line 1143)
            Debug.Log($"🔄 ✅ Completed repositioning of {piece.pieceColor} {piece.pieceType} from {originalPos} to {position}");
        }
        else
        {
            // This is a new piece from tray - initialize normally
            piece.Initialize(position, piece.pieceColor);
        }
        
        // Deselect the piece
        DeselectTrayPiece();
        
        // ASYNCHRONOUS PLACEMENT: No turn switching needed
        // Both players can continue placing pieces simultaneously
        
        // Update visibility for blind placement
        if (PlacementVisibilityManager.Instance != null)
        {
            PlacementVisibilityManager.Instance.OnPiecePlaced(piece);
        }
        
        // Online: tell the server (it re-checks the placement); a moved piece leaves its old square first
        if (OnlineSession.ShouldSend(piece.pieceColor))
        {
            if (moved) OnlineClient.Instance.SendUnplacePiece(piece.pieceColor, movedFrom);
            OnlineClient.Instance.SendPlacePiece(piece.pieceColor, piece.pieceType, position);
        }

        // Check if placement phase should end (when BOTH players complete)
        CheckPlacementCompletion();
        
        Debug.Log($"PlacementManager: Successfully placed {piece.pieceColor} {piece.pieceType} at {position}");
    }
    
    /// <summary>
    /// Switch to the other player for alternating placement
    /// </summary>
    private void SwitchPlacingPlayer()
    {
        currentPlacingPlayer = (currentPlacingPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        OnPlayerTurnChanged?.Invoke(currentPlacingPlayer);
        Debug.Log($"PlacementManager: Legacy turn switch to {currentPlacingPlayer} (now unused in asynchronous mode)");
        
        // ASYNCHRONOUS PLACEMENT: No AI triggering needed here since both AIs are already active
    }
    
    /// <summary>
    /// Check if the placement phase should be completed (supports 2-6 players)
    /// </summary>
    private void CheckPlacementCompletion()
    {
        Debug.Log("PlacementManager: Checking placement completion for all active players...");

        if (PlayerManager.Instance == null)
        {
            Debug.LogError("PlacementManager.CheckPlacementCompletion: PlayerManager.Instance is null");
            return;
        }

        // Get all active players
        List<PieceColor> activePlayers = PlayerManager.Instance.GetActivePlayers();
        Debug.Log($"PlacementManager: Checking {activePlayers.Count} active players");

        // Check completion status for all active players
        bool allCompleted = true;
        string completionStatus = "";
        string readinessStatus = "";
        string playerTypes = "";

        foreach (PieceColor playerColor in activePlayers)
        {
            bool completed = IsPlayerPlacementCompleted(playerColor);
            bool ready = IsPlayerReady(playerColor);
            string playerType = TurnManager.Instance?.IsPlayerAI(playerColor) == true ? "AI" : "HUMAN";

            completionStatus += $"{playerColor}={completed} ";
            readinessStatus += $"{playerColor}={ready} ";
            playerTypes += $"{playerColor}={playerType} ";

            if (!completed)
            {
                allCompleted = false;
            }
        }

        Debug.Log($"🎯 PlacementManager: Checking readiness for all game modes");
        Debug.Log($"🎯 Placement status: {completionStatus}");
        Debug.Log($"🎯 Player readiness: {readinessStatus}");
        Debug.Log($"🎯 Player types: {playerTypes}");

        // Require all players to have pieces placed AND be ready (human manual or AI auto)
        if (allCompleted && AreAllPlayersReady())
        {
            Debug.Log($"✅ PlacementManager: All {activePlayers.Count} players completed placement and are ready - transitioning to gameplay");
            foreach (PieceColor playerColor in activePlayers)
            {
                string playerType = TurnManager.Instance?.IsPlayerAI(playerColor) == true ? "AI" : "HUMAN";
                bool completed = IsPlayerPlacementCompleted(playerColor);
                bool ready = IsPlayerReady(playerColor);
                Debug.Log($"  {playerColor}: {playerType} (completed={completed}, ready={ready})");
            }
            CompletePlacementPhase();
            return;
        }
        else
        {
            // Detailed waiting analysis
            string waitingFor = "";
            foreach (PieceColor playerColor in activePlayers)
            {
                bool completed = IsPlayerPlacementCompleted(playerColor);
                bool ready = IsPlayerReady(playerColor);
                string playerType = TurnManager.Instance?.IsPlayerAI(playerColor) == true ? "AI" : "HUMAN";

                if (!completed) waitingFor += $"{playerColor} {playerType} to place pieces ";
                if (!ready) waitingFor += $"{playerColor} {playerType} to be ready ";
            }

            Debug.Log($"🎯 PlacementManager: Waiting for: {waitingFor.TrimEnd()}");
            return;
        }
    }
    
    /// <summary>
    /// Manually complete the placement phase and start gameplay
    /// </summary>
    public void CompletePlacementPhase()
    {
        Debug.Log("🚀 PlacementManager: ==== COMPLETING PLACEMENT PHASE ====");
        Debug.Log($"🚀 PlacementManager: Current GameState: {(GameStateManager.Instance != null ? GameStateManager.Instance.currentState.ToString() : "NULL")}");
        
        // DEBUG: Check what pieces are currently on the board before validation
        if (ChessBoard.Instance != null)
        {
            int totalPieces = 0;
            int whitePieces = 0;
            int blackPieces = 0;

            // Get dynamic board dimensions
            Vector3Int dims = BoardDimensionsManager.Instance != null
                ? BoardDimensionsManager.Instance.GetDimensions()
                : new Vector3Int(4, 4, 4);

            for (int x = 0; x < dims.x; x++)
            {
                for (int y = 0; y < dims.y; y++)
                {
                    for (int z = 0; z < dims.z; z++)
                    {
                        ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                        if (piece != null)
                        {
                            totalPieces++;
                            if (piece.pieceColor == PieceColor.White) whitePieces++;
                            else if (piece.pieceColor == PieceColor.Black) blackPieces++;
                            Debug.Log($"🚀 BOARD SCAN: Found {piece.pieceColor} {piece.pieceType} at ({x},{y},{z})");
                        }
                    }
                }
            }

            Debug.Log($"🚀 BOARD STATE: Total pieces: {totalPieces}, White: {whitePieces}, Black: {blackPieces}");
        }
        else
        {
            Debug.LogError("🚀 ❌ ChessBoard.Instance is NULL during completion!");
        }
        
        // Validate that the game setup is valid before transitioning
        Debug.Log("🚀 PlacementManager: Validating game setup...");
        if (!ValidateGameSetup())
        {
            Debug.LogError("🚀 ❌ PlacementManager: Cannot start game - invalid setup");
            return;
        }
        Debug.Log("🚀 ✅ PlacementManager: Game setup validation passed");
        
        // Clear any remaining placement indicators
        Debug.Log("🚀 PlacementManager: Clearing placement indicators...");
        ClearPlacementIndicators();
        
        // Clear any selected pieces
        Debug.Log("🚀 PlacementManager: Deselecting any selected pieces...");
        DeselectTrayPiece();
        
        // Spawn pawns in front of all placed pieces
        Debug.Log("🚀 PlacementManager: About to call SpawnPawnsForGameplay()...");
        SpawnPawnsForGameplay();
        Debug.Log("🚀 PlacementManager: SpawnPawnsForGameplay() completed");
        
        // NOTE: Defer check state evaluation until AFTER state transition
        // This prevents race condition between UI events and turn validation
        Debug.Log("🚀 PlacementManager: Deferring check state evaluation until after state transition...");
        
        // REVEAL ALL PIECES: End blind placement before gameplay begins
        Debug.Log("🚀 PlacementManager: Revealing all pieces - ending blind placement...");
        if (PlacementVisibilityManager.Instance != null)
        {
            PlacementVisibilityManager.Instance.RevealAllPieces();
        }
        
        Debug.Log("🚀 PlacementManager: Preparing to transition to Playing state");
        
        // CRITICAL FIX: Ensure turn order is properly established BEFORE changing state
        // This prevents race conditions where AI gets triggered before turn order is set
        Debug.Log("🚀 PlacementManager: Validating turn order is properly set...");
        
        if (TurnManager.Instance != null)
        {
            PieceColor currentTurnPlayer = TurnManager.Instance.GetCurrentPlayer();
            Debug.Log($"🚀 PlacementManager: Turn order validation - Current player is: {currentTurnPlayer}");
            
            // Add a small delay to ensure all systems are synchronized
            Debug.Log("🚀 PlacementManager: Adding synchronization delay before state change...");
            StartCoroutine(DelayedStateTransition());
        }
        else
        {
            Debug.LogError("🚀 ❌ PlacementManager: TurnManager.Instance is null! Cannot validate turn order!");
            // Fallback to immediate transition
            ExecuteStateTransition();
        }
    }
    
    /// <summary>
    /// Delayed state transition to ensure proper turn order synchronization
    /// </summary>
    private System.Collections.IEnumerator DelayedStateTransition()
    {
        Debug.Log("🚀 PlacementManager: DelayedStateTransition - Waiting for turn order synchronization...");
        
        // Small delay to ensure all turn order logic completes
        yield return new UnityEngine.WaitForSeconds(0.05f);
        
        // Final validation before state change
        if (TurnManager.Instance != null)
        {
            PieceColor finalCurrentPlayer = TurnManager.Instance.GetCurrentPlayer();
            Debug.Log($"🚀 PlacementManager: Final turn validation - Current player: {finalCurrentPlayer}");
            Debug.Log($"🚀 PlacementManager: Player {finalCurrentPlayer} is AI: {TurnManager.Instance.IsPlayerAI(finalCurrentPlayer)}");
        }
        
        Debug.Log("🚀 PlacementManager: Synchronization complete, executing state transition...");
        ExecuteStateTransition();
    }
    
    /// <summary>
    /// Execute the actual state transition to Playing phase
    /// </summary>
    private void ExecuteStateTransition()
    {
        Debug.Log("🚀 PlacementManager: ExecuteStateTransition - Beginning state change sequence...");
        
        // Hide trays during gameplay for clearer board view
        PieceTray.HideAllTrays();
        Debug.Log("🚀 PlacementManager: Trays hidden for gameplay");
        
        // Fire the placement complete event
        OnPlacementPhaseComplete?.Invoke();
        Debug.Log("🚀 PlacementManager: OnPlacementPhaseComplete event invoked");
        
        // Change game state to Playing
        if (GameStateManager.Instance != null)
        {
            Debug.Log("🚀 PlacementManager: Changing game state to Playing...");
            GameStateManager.Instance.ChangeState(GameState.Playing);
            Debug.Log($"🚀 PlacementManager: GameState changed to: {GameStateManager.Instance.currentState}");
            
            // Final confirmation
            if (TurnManager.Instance != null)
            {
                PieceColor finalPlayer = TurnManager.Instance.GetCurrentPlayer();
                bool finalIsAI = TurnManager.Instance.IsPlayerAI(finalPlayer);
                Debug.Log($"🚀 PlacementManager: State transition complete - {finalPlayer} to move (AI: {finalIsAI})");
            }
        }
        else
        {
            Debug.LogError("🚀 ❌ PlacementManager: GameStateManager.Instance is null!");
        }
        
        // NOW evaluate check states AFTER state transition is complete
        // This ensures UI events and turn validation use the same data
        Debug.Log("🚀 PlacementManager: Now evaluating check states after state transition...");
        EvaluatePostPlacementCheckStates();
        
        Debug.Log("🚀 PlacementManager: ExecuteStateTransition COMPLETE");
    }
    
    /// <summary>
    /// Evaluate check states immediately after placement to determine opening turn order
    /// </summary>
    private void EvaluatePostPlacementCheckStates()
    {
        if (CheckDetectionManager.Instance == null)
        {
            Debug.LogError("🚀 ❌ PlacementManager: CheckDetectionManager.Instance is null - cannot evaluate check states");
            return;
        }
        
        Debug.Log("🚀 📋 PlacementManager: Evaluating initial check states after placement...");

        // Force a fresh evaluation of check states with all pieces on board
        CheckDetectionManager.Instance.InvalidateCache();

        // MULTI-PLAYER FIX: Get check states for all active players (2-6 players)
        System.Collections.Generic.Dictionary<PieceColor, bool> playerCheckStates = new System.Collections.Generic.Dictionary<PieceColor, bool>();

        System.Collections.Generic.List<PieceColor> activePlayers = new System.Collections.Generic.List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetActivePlayers();
            Debug.Log($"🚀 📋 PlacementManager: Checking initial check states for {activePlayers.Count} active players");
        }
        else
        {
            // Fallback to 2-player mode
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
            Debug.Log($"🚀 📋 PlacementManager: PlayerManager not available, checking 2 players (fallback)");
        }

        // Check each active player's king for check state
        foreach (PieceColor player in activePlayers)
        {
            CheckDetectionManager.Instance.InvalidateCache(); // Fresh evaluation for each player
            bool isInCheck = CheckDetectionManager.Instance.IsKingInCheck(player);
            playerCheckStates[player] = isInCheck;

            Debug.Log($"🚀 📋 PlacementManager: {player} king check state: {(isInCheck ? "IN CHECK" : "safe")}");
        }

        // For 2-player games, maintain backwards compatibility with UI initialization
        if (activePlayers.Count == 2 && activePlayers.Contains(PieceColor.White) && activePlayers.Contains(PieceColor.Black))
        {
            bool whiteInCheck = playerCheckStates[PieceColor.White];
            bool blackInCheck = playerCheckStates[PieceColor.Black];

            // Initialize UI state with safe method and update visual feedback
            CheckDetectionManager.Instance.InitializeUIStateWithInitialChecks(whiteInCheck, blackInCheck);

            Debug.Log($"🚀 📋 PlacementManager: 2-player check states - White: {whiteInCheck}, Black: {blackInCheck}");
        }
        else
        {
            // Multi-player games: Initialize UI for all players
            // TODO: CheckDetectionManager needs multi-player UI initialization support
            Debug.Log($"🚀 📋 PlacementManager: Multi-player check states initialized for {activePlayers.Count} players");
        }

        // Trigger safe visual feedback update to ensure UI is in sync
        CheckDetectionManager.Instance.SafeUpdateVisualFeedback();

        // Store the initial check states for TurnManager to use
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.SetInitialCheckStates(playerCheckStates);
            Debug.Log($"🚀 📋 PlacementManager: Initial check states sent to TurnManager for {playerCheckStates.Count} players");
        }
        else
        {
            Debug.LogError("🚀 ❌ PlacementManager: TurnManager.Instance is null - cannot set initial check states");
        }
    }
    
    /// <summary>
    /// Validate that both players have pieces on the board
    /// </summary>
    private bool ValidateGameSetup()
    {
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("PlacementManager: ChessBoard not available for validation");
            return false;
        }
        
        bool hasWhitePieces = false;
        bool hasBlackPieces = false;
        int totalPieces = 0;

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Check all board positions for pieces
        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null)
                    {
                        totalPieces++;
                        if (piece.pieceColor == PieceColor.White)
                            hasWhitePieces = true;
                        else if (piece.pieceColor == PieceColor.Black)
                            hasBlackPieces = true;
                    }
                }
            }
        }
        if (ZombieMode.Active) hasBlackPieces = true; // the zombies arrive once play starts
        
        Debug.Log($"PlacementManager: Game setup validation - Total pieces: {totalPieces}, White: {hasWhitePieces}, Black: {hasBlackPieces}");
        
        if (!hasWhitePieces)
        {
            Debug.LogError("PlacementManager: No White pieces found on board");
            return false;
        }
        
        if (!hasBlackPieces)
        {
            Debug.LogError("PlacementManager: No Black pieces found on board");
            return false;
        }
        
        if (totalPieces < 2)
        {
            Debug.LogError("PlacementManager: Not enough pieces on board for gameplay");
            return false;
        }
        
        Debug.Log("PlacementManager: ✅ Game setup validation passed");
        return true;
    }
    
    /// <summary>
    /// Reset all pieces back to trays
    /// </summary>
    public void ResetToTrays()
    {
        Debug.Log("PlacementManager: Resetting all pieces to trays");
        
        // Clear any selection and indicators first
        DeselectTrayPiece();
        ClearPlacementIndicators();
        
        // Find all pieces on the board and move them back to trays
        List<ChessPiece> boardPieces = new List<ChessPiece>();

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Only collect pieces that are actually on the board (have valid positions)
        if (ChessBoard.Instance != null)
        {
            for (int x = 0; x < dims.x; x++)
            {
                for (int y = 0; y < dims.y; y++)
                {
                    for (int z = 0; z < dims.z; z++)
                    {
                        BoardPosition pos = new BoardPosition(x, y, z);
                        ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);
                        if (piece != null)
                        {
                            boardPieces.Add(piece);
                        }
                    }
                }
            }
        }
        
        Debug.Log($"PlacementManager: Found {boardPieces.Count} pieces on board to reset");
        
        // Move each board piece back to its tray
        foreach (ChessPiece piece in boardPieces)
        {
            Debug.Log($"PlacementManager: Moving {piece.pieceColor} {piece.pieceType} from board to tray");
            
            // Remove from board
            if (piece.CurrentPosition.IsValid())
            {
                ChessBoard.Instance.SetPieceAt(piece.CurrentPosition, null);
            }
            
            // Reset piece state
            piece.ResetPosition(); // This should invalidate CurrentPosition
            
            // Add back to appropriate tray using PieceTrayManager for scalability
            PieceTray tray = null;
            if (PieceTrayManager.Instance != null)
            {
                tray = PieceTrayManager.Instance.GetTray(piece.pieceColor);
            }
            else
            {
                // Fallback for 2-player games
                tray = (piece.pieceColor == PieceColor.White) ? PieceTray.WhiteTray : PieceTray.BlackTray;
            }

            if (tray != null)
            {
                bool success = tray.AddPiece(piece);
                if (success)
                {
                    Debug.Log($"PlacementManager: Successfully moved {piece.pieceColor} {piece.pieceType} to tray");
                }
                else
                {
                    Debug.LogError($"PlacementManager: Failed to add {piece.pieceColor} {piece.pieceType} to tray - tray may be full");
                }
            }
            else
            {
                Debug.LogError($"PlacementManager: No tray found for {piece.pieceColor} pieces");
            }
        }
        
        // Ensure trays are visible after reset
        PieceTray.ShowAllTrays();
        
        // Reset placing player and ensure we're in placement state
        currentPlacingPlayer = PieceColor.White;
        OnPlayerTurnChanged?.Invoke(currentPlacingPlayer);
        
        // Reset player readiness states when resetting to trays
        ResetPlayerReadiness();
        Debug.Log("PlacementManager: Player readiness reset after tray reset");
        
        // Ensure we're in placement phase
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ChangeState(GameState.PiecePlacement);
        }
        
        Debug.Log("PlacementManager: Reset to trays completed");
    }
    
    /// <summary>
    /// Spawn pawns in front of all placed pieces when transitioning to gameplay
    /// </summary>
    private void SpawnPawnsForGameplay()
    {
        if (ZombieMode.Active) return; // zombie runs: the player has no pawns, and the zombies bring their own
        Debug.Log("Spawning pawns for gameplay...");
        
        // ANTI-CORRUPTION: First, clean up any existing pawns to prevent duplicates
        CleanUpExistingPawns();
        
        // Validate ChessBoard
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("PlacementManager: ChessBoard.Instance is null, cannot spawn pawns");
            return;
        }
        
        // Validate GameManager and pawn prefab
        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("PlacementManager: GameManager not found");
            return;
        }
        
        if (gameManager.pawnPiecePrefab == null)
        {
            Debug.LogError("PlacementManager: pawnPiecePrefab is null");
            return;
        }
        
        int pawnCount = 0;
        List<BoardPosition> pawnPositions = new List<BoardPosition>(); // Track to prevent duplicates

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Find all pieces on the board and spawn pawns in front of them
        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    BoardPosition position = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(position);

                    if (piece != null && piece.pieceType != ChessPieceType.Pawn)
                    {
                        // Calculate position directly in front of the piece based on color
                        BoardPosition pawnPosition = GetPawnSpawnPosition(position, piece.pieceColor);

                        // Validate pawn spawn position and check for duplicates
                        if (pawnPosition.IsValid() &&
                            ChessBoard.Instance.GetPieceAt(pawnPosition) == null &&
                            !pawnPositions.Contains(pawnPosition))
                        {
                            SpawnPawn(pawnPosition, piece.pieceColor);
                            pawnPositions.Add(pawnPosition);
                            pawnCount++;
                        }
                        else if (pawnPositions.Contains(pawnPosition))
                        {
                            Debug.LogWarning($"PlacementManager: Skipping duplicate pawn spawn at {pawnPosition}");
                        }
                    }
                }
            }
        }
        
        Debug.Log($"Pawn spawning complete: {pawnCount} pawns spawned");
    }
    
    /// <summary>
    /// Clean up any existing pawns before spawning new ones to prevent duplicates
    /// </summary>
    private void CleanUpExistingPawns()
    {
        Debug.Log("PlacementManager: Cleaning up existing pawns...");
        
        // Find all pawns in the scene
        Pawn[] allPawns = FindObjectsByType<Pawn>(FindObjectsSortMode.None);
        
        int removedCount = 0;
        foreach (Pawn pawn in allPawns)
        {
            if (pawn != null && pawn.gameObject != null)
            {
                // Remove pawn from board if registered
                if (pawn.CurrentPosition.IsValid() && ChessBoard.Instance != null)
                {
                    ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(pawn.CurrentPosition);
                    if (boardPiece == pawn)
                    {
                        ChessBoard.Instance.SetPieceAt(pawn.CurrentPosition, null);
                    }
                }
                
                Debug.Log($"PlacementManager: Removing existing {pawn.pieceColor} pawn");
                DestroyImmediate(pawn.gameObject);
                removedCount++;
            }
        }
        
        Debug.Log($"PlacementManager: Removed {removedCount} existing pawns");
    }
    
    /// <summary>
    /// Calculate the position directly in front of a piece based on color and movement axis
    /// Each color moves along a specific axis:
    /// - White/Black: X-axis
    /// - Green/Purple: Z-axis
    /// - Yellow/Orange: Y-axis
    /// </summary>
    private BoardPosition GetPawnSpawnPosition(BoardPosition piecePosition, PieceColor color)
    {
        Vector3Int forwardDir;

        switch (color)
        {
            case PieceColor.White:
                forwardDir = new Vector3Int(1, 0, 0);   // +X direction
                break;
            case PieceColor.Black:
                forwardDir = new Vector3Int(-1, 0, 0);  // -X direction
                break;
            case PieceColor.Green:
                forwardDir = new Vector3Int(0, 0, 1);   // +Z direction
                break;
            case PieceColor.Purple:
                forwardDir = new Vector3Int(0, 0, -1);  // -Z direction
                break;
            case PieceColor.Yellow:
                forwardDir = new Vector3Int(0, 1, 0);   // +Y direction
                break;
            case PieceColor.Orange:
                forwardDir = new Vector3Int(0, -1, 0);  // -Y direction
                break;
            default:
                Debug.LogWarning($"GetPawnSpawnPosition: Unknown color {color}, defaulting to +X");
                forwardDir = new Vector3Int(1, 0, 0);
                break;
        }

        BoardPosition pawnPosition = new BoardPosition(
            piecePosition.x + forwardDir.x,
            piecePosition.y + forwardDir.y,
            piecePosition.z + forwardDir.z
        );

        return pawnPosition;
    }
    
    /// <summary>
    /// Spawn a single pawn at the specified position
    /// </summary>
    private void SpawnPawn(BoardPosition position, PieceColor color)
    {
        // Get GameManager
        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("SpawnPawn: GameManager not found");
            return;
        }
        
        if (gameManager.pawnPiecePrefab == null)
        {
            Debug.LogError("SpawnPawn: pawnPiecePrefab is null");
            return;
        }
        
        // Instantiate pawn from GameManager's prefab
        GameObject pawnObject = Instantiate(gameManager.pawnPiecePrefab);
        
        pawnObject.name = $"{color} Pawn at {position}";
        
        // Get the Pawn component
        Pawn pawn = pawnObject.GetComponent<Pawn>();
        if (pawn == null)
        {
            Debug.LogError("SpawnPawn: Spawned object missing Pawn component");
            DestroyImmediate(pawnObject);
            return;
        }
        
        // Find the pieces container to parent the pawn to
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        if (piecesContainer != null)
        {
            pawnObject.transform.SetParent(piecesContainer.transform);
        }
        else
        {
            Debug.LogWarning("SpawnPawn: Pieces Container not found, pawn may not rotate with board");
        }
        
        // Initialize the pawn at the board position
        pawn.Initialize(position, color);
        
        // CRITICAL: Activate the pawn object to make it visible
        pawnObject.SetActive(true);
    }
    
    // === AI PLACEMENT INTEGRATION ===
    
    /// <summary>
    /// Trigger AI placement if the current placing player is an AI
    /// </summary>
    private void TriggerAIPlacementIfNeeded()
    {
        if (OnlineSession.Replaying) return; // rebuilding a resumed game: see ResumeAIPlacement
        // Only trigger AI if alternating turns is enabled or if we're in a specific AI mode
        if (!alternatingTurns && !ShouldUseAIPlacement())
        {
            return;
        }
        
        // Check if current placing player is AI
        if (TurnManager.Instance != null && TurnManager.Instance.IsPlayerAI(currentPlacingPlayer))
        {
            Debug.Log($"PlacementManager: Triggering AI placement for {currentPlacingPlayer}");
            
            // Request placement from AI player
            if (AIPlayer.Instance != null)
            {
                AIPlayer.Instance.RequestPlacement(currentPlacingPlayer);
            }
            else
            {
                Debug.LogError("PlacementManager: AIPlayer.Instance is null, cannot trigger AI placement");
            }
        }
    }
    
    /// <summary>
    /// Trigger AI placement for all AI players simultaneously (asynchronous mode, supports 2-6 players)
    /// </summary>
    /// <summary>After a resumed online game is rebuilt in setup: robots finish placing their pieces</summary>
    public void ResumeAIPlacement()
    {
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanPlacePieces()) return;
        if (AIPlayer.Instance == null || TurnManager.Instance == null || PlayerManager.Instance == null) return;
        foreach (PieceColor color in PlayerManager.Instance.GetActivePlayers())
        {
            if (TurnManager.Instance.IsPlayerAI(color) && !IsPlayerPlacementCompleted(color))
                AIPlayer.Instance.RequestPlacement(color);
        }
    }

    private void TriggerAIPlacementForBothPlayers()
    {
        if (OnlineSession.Replaying) return; // rebuilding a resumed game: see ResumeAIPlacement
        Debug.Log("🚀 ENTRY: PlacementManager.TriggerAIPlacementForBothPlayers - Starting AI placement process for all players");

        if (AIPlayer.Instance == null)
        {
            Debug.LogError("🚨 CRITICAL: PlacementManager: AIPlayer.Instance is null, cannot trigger AI placement");
            return;
        }

        if (TurnManager.Instance == null)
        {
            Debug.LogError("🚨 CRITICAL: PlacementManager: TurnManager.Instance is null, cannot determine AI players");
            return;
        }

        if (PlayerManager.Instance == null)
        {
            Debug.LogError("🚨 CRITICAL: PlacementManager: PlayerManager.Instance is null, cannot get active players");
            return;
        }

        // TIMING FIX: Verify pieces are actually ready before attempting AI placement
        if (!piecesReadyForAI)
        {
            Debug.LogWarning("⚠️ TIMING ISSUE: PlacementManager: Pieces not marked as ready for AI placement yet!");
            Debug.LogWarning("⚠️ This method should only be called via NotifyPiecesReady() after GameManager creates pieces");
            return;
        }

        // CRITICAL FIX: Validate and repair tray piece positions before AI placement
        // This fixes the issue where tray pieces get assigned valid board positions, preventing AI placement
        Debug.Log("🔧 PRE-PLACEMENT FIX: Validating and repairing tray piece positions...");
        ValidateAndRepairTrayPositions();

        // Get all active players
        List<PieceColor> activePlayers = PlayerManager.Instance.GetActivePlayers();
        Debug.Log($"🔍 SYSTEM STATE CHECK:");
        Debug.Log($"  GameState: {(GameStateManager.Instance?.currentState.ToString() ?? "NULL")}");
        Debug.Log($"  TurnManager Game Mode: {TurnManager.Instance.GetGameModeDescription()}");
        Debug.Log($"  Active player count: {activePlayers.Count}");
        Debug.Log($"  AIPlayer.Instance: {(AIPlayer.Instance != null ? "✅ EXISTS" : "❌ NULL")}");
        Debug.Log($"  Time: {Time.time:F3}s");

        // Trigger AI placement for each AI player
        foreach (PieceColor playerColor in activePlayers)
        {
            bool isAI = TurnManager.Instance.IsPlayerAI(playerColor);
            Debug.Log($"  {playerColor} - IsPlayerAI: {isAI}, PlayerType: {TurnManager.Instance.GetPlayerType(playerColor)}");

            if (isAI)
            {
                Debug.Log($"🤖 PlacementManager: Starting {playerColor} AI placement asynchronously");
                if (AIPlayer.Instance != null)
                {
                    AIPlayer.Instance.RequestPlacement(playerColor);
                    Debug.Log($"✅ PlacementManager: {playerColor} AI placement request sent");
                }
                else
                {
                    Debug.LogError($"🚨 PlacementManager: Cannot request {playerColor} AI placement - AIPlayer.Instance is NULL");
                }
            }
            else
            {
                Debug.Log($"👤 PlacementManager: {playerColor} is HUMAN player - no AI placement needed");
            }
        }

        Debug.Log($"PlacementManager: Asynchronous AI placement requests sent for {activePlayers.Count} active players");
    }
    
    /// <summary>
    /// TIMING FIX: Called by GameManager when pieces are created in trays and ready for AI placement
    /// </summary>
    /// <summary>Every player's pieces have been created in the trays</summary>
    public bool PiecesReady => piecesReadyForAI;

    public void NotifyPiecesReady()
    {
        Debug.Log("🎯 PlacementManager.NotifyPiecesReady: ENTRY - Pieces are now ready for AI placement!");
        piecesReadyForAI = true;
        
        // Only trigger AI placement if we're in the placement phase
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            Debug.Log("🤖 PlacementManager.NotifyPiecesReady: Now triggering AI placement since pieces are ready");
            TriggerAIPlacementForBothPlayers();
        }
        else
        {
            Debug.LogWarning("⚠️ PlacementManager.NotifyPiecesReady: Not in placement phase, skipping AI placement trigger");
        }
    }
    
    /// <summary>
    /// Check if we should use AI placement based on game configuration
    /// </summary>
    private bool ShouldUseAIPlacement()
    {
        // Use AI placement for AI vs AI games even without alternating turns
        if (TurnManager.Instance != null)
        {
            bool whiteIsAI = TurnManager.Instance.IsPlayerAI(PieceColor.White);
            bool blackIsAI = TurnManager.Instance.IsPlayerAI(PieceColor.Black);
            
            // If both players are AI, use automatic placement
            if (whiteIsAI && blackIsAI)
            {
                return true;
            }
            
            // If current player is AI and it's their turn, use automatic placement
            if (TurnManager.Instance.IsPlayerAI(currentPlacingPlayer))
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Get the current placing player (for external access)
    /// </summary>
    public PieceColor GetCurrentPlacingPlayer()
    {
        return currentPlacingPlayer;
    }
    
    /// <summary>
    /// Check if a specific player is currently placing
    /// </summary>
    public bool IsPlayerPlacing(PieceColor playerColor)
    {
        return currentPlacingPlayer == playerColor;
    }
    
    /// <summary>
    /// Debug board and tray state to help identify piece placement issues
    /// </summary>
    private void DebugBoardAndTrayState(ChessPiece targetPiece, BoardPosition targetPosition)
    {
        Debug.Log($"🔍 === BOARD & TRAY STATE DEBUG ===");
        Debug.Log($"🔍 Target: Place {targetPiece.pieceColor} {targetPiece.pieceType} at {targetPosition}");
        Debug.Log($"🔍 Target piece position: {targetPiece.transform.position}");
        Debug.Log($"🔍 Target piece parent: {(targetPiece.transform.parent != null ? targetPiece.transform.parent.name : "None")}");
        Debug.Log($"🔍 Target piece CurrentPosition: {targetPiece.CurrentPosition}");
        
        // Check what's at the target position
        if (ChessBoard.Instance != null)
        {
            ChessPiece existingPiece = ChessBoard.Instance.GetPieceAt(targetPosition);
            Debug.Log($"🔍 Target position {targetPosition} contains: {(existingPiece != null ? $"{existingPiece.pieceColor} {existingPiece.pieceType}" : "EMPTY")}");
            
            if (existingPiece != null)
            {
                Debug.Log($"🔍 Existing piece transform: {existingPiece.transform.position}");
                Debug.Log($"🔍 Existing piece parent: {(existingPiece.transform.parent != null ? existingPiece.transform.parent.name : "None")}");
                Debug.Log($"🔍 Same piece object? {existingPiece == targetPiece}");
            }
        }
        
        // Count pieces of same type/color on board
        int sameTypeCount = 0;
        if (ChessBoard.Instance != null)
        {
            // Get dynamic board dimensions
            Vector3Int dims = BoardDimensionsManager.Instance != null
                ? BoardDimensionsManager.Instance.GetDimensions()
                : new Vector3Int(4, 4, 4);

            for (int x = 0; x < dims.x; x++)
            {
                for (int y = 0; y < dims.y; y++)
                {
                    for (int z = 0; z < dims.z; z++)
                    {
                        ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                        if (boardPiece != null &&
                            boardPiece.pieceColor == targetPiece.pieceColor &&
                            boardPiece.pieceType == targetPiece.pieceType)
                        {
                            sameTypeCount++;
                            Debug.Log($"🔍 Found {boardPiece.pieceColor} {boardPiece.pieceType} at ({x},{y},{z})");
                        }
                    }
                }
            }
        }
        Debug.Log($"🔍 Total {targetPiece.pieceColor} {targetPiece.pieceType} pieces on board: {sameTypeCount}");
        
        // Check tray contents
        if (PieceTray.WhiteTray != null)
        {
            var whitePieces = PieceTray.WhiteTray.GetPieces();
            int whiteQueens = 0;
            foreach (var piece in whitePieces)
            {
                if (piece.pieceType == ChessPieceType.Queen) whiteQueens++;
            }
            Debug.Log($"🔍 White tray contains {whiteQueens} Queens out of {whitePieces.Count} total pieces");
        }
        
        if (PieceTray.BlackTray != null)
        {
            var blackPieces = PieceTray.BlackTray.GetPieces();
            int blackQueens = 0;
            foreach (var piece in blackPieces)
            {
                if (piece.pieceType == ChessPieceType.Queen) blackQueens++;
            }
            Debug.Log($"🔍 Black tray contains {blackQueens} Queens out of {blackPieces.Count} total pieces");
        }
        
        Debug.Log($"🔍 === END BOARD & TRAY STATE DEBUG ===");
    }
    
    /// <summary>
    /// Validate that we're not trying to place a duplicate piece
    /// </summary>
    private bool ValidateNoDuplicatePlacement(ChessPiece piece, BoardPosition targetPosition)
    {
        if (ChessBoard.Instance == null) return true;
        
        // For repositioning pieces, this is always valid (moving existing piece)
        if (repositioningPieces.ContainsKey(piece))
        {
            Debug.Log($"🔍 ValidateNoDuplicatePlacement: Repositioning piece - allowing duplicate check bypass");
            return true;
        }
        
        // Count how many pieces of this type/color are already on the board
        int existingCount = 0;
        ChessPiece existingPiece = null;

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (boardPiece != null &&
                        boardPiece.pieceColor == piece.pieceColor &&
                        boardPiece.pieceType == piece.pieceType)
                    {
                        existingCount++;
                        existingPiece = boardPiece;
                        Debug.Log($"🔍 Found existing {piece.pieceColor} {piece.pieceType} at ({x},{y},{z})");
                    }
                }
            }
        }
        
        Debug.Log($"🔍 ValidateNoDuplicatePlacement: Found {existingCount} existing {piece.pieceColor} {piece.pieceType} on board");
        
        // Check if we're trying to place a piece when one already exists
        if (existingCount > 0)
        {
            // Special case: Check if the existing piece is actually the same object (shouldn't happen for tray pieces)
            if (existingPiece == piece)
            {
                Debug.LogWarning($"🔍 ValidateNoDuplicatePlacement: WEIRD - Existing piece is same object as placement piece");
                Debug.LogWarning($"   This suggests the piece is already on the board but trying to place from tray");
                Debug.LogWarning($"   Existing piece CurrentPosition: {existingPiece.CurrentPosition}");
                Debug.LogWarning($"   Target position: {targetPosition}");
                return false; // Block this placement
            }
            
            // For unique pieces (King, Queen), only one should exist
            if (piece.pieceType == ChessPieceType.King || piece.pieceType == ChessPieceType.Queen)
            {
                Debug.LogError($"🔍 ValidateNoDuplicatePlacement: BLOCKING - {piece.pieceColor} {piece.pieceType} already exists on board");
                Debug.LogError($"   Existing piece at: {existingPiece.CurrentPosition}");
                Debug.LogError($"   Attempted placement at: {targetPosition}");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Validate board state consistency for a specific piece
    /// </summary>
    private void ValidateBoardConsistency(ChessPiece piece)
    {
        if (ChessBoard.Instance == null || piece == null) return;
        
        // Get piece's reported position
        BoardPosition reportedPosition = piece.CurrentPosition;
        
        // Only validate pieces that think they're on the board (not in tray)
        if (!reportedPosition.IsValid()) return;
        
        // Check what the board array says is at that position
        ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(reportedPosition);
        
        if (boardPiece != piece)
        {
            Debug.LogWarning($"🔧 BOARD CONSISTENCY ISSUE detected for {piece.pieceColor} {piece.pieceType}");
            Debug.LogWarning($"   - Piece thinks it's at: {reportedPosition}");
            Debug.LogWarning($"   - Board array contains: {(boardPiece != null ? $"{boardPiece.pieceColor} {boardPiece.pieceType}" : "NULL")}");
            
            // Auto-repair: Update board array to match piece position
            if (boardPiece == null)
            {
                ChessBoard.Instance.SetPieceAt(reportedPosition, piece);
                Debug.Log($"✅ Auto-repaired: Set board[{reportedPosition}] = {piece.pieceColor} {piece.pieceType}");
            }
            else
            {
                Debug.LogWarning($"⚠️ Cannot auto-repair: Position occupied by different piece");
            }
        }
    }
    
    /// <summary>
    /// Check if this is an AI vs AI game that should auto-complete placement
    /// </summary>
    private bool ShouldAutoCompleteForAIVsAI()
    {
        if (TurnManager.Instance == null)
        {
            return false;
        }
        
        // Only auto-complete for AI vs AI games
        bool isAIVsAI = TurnManager.Instance.IsAIVsAI();
        Debug.Log($"PlacementManager: ShouldAutoCompleteForAIVsAI - IsAIVsAI: {isAIVsAI}");
        
        return isAIVsAI;
    }
    
    /// <summary>
    /// Check if a player has completed their placement (tray is empty)
    /// </summary>
    public bool IsPlayerPlacementCompleted(PieceColor playerColor)
    {
        // CRITICAL FIX: Use PieceTrayManager.GetTray() for consistency with PlacePieceOnBoard() and AIPlayer
        PieceTray playerTray = null;

        if (PieceTrayManager.Instance != null)
        {
            playerTray = PieceTrayManager.Instance.GetTray(playerColor);
            Debug.Log($"🔍 IsPlayerPlacementCompleted: Using PieceTrayManager.GetTray({playerColor}) - tray instance ID: {(playerTray != null ? playerTray.GetInstanceID().ToString() : "NULL")}");
        }
        else
        {
            // Fallback for 2-player games
            playerTray = (playerColor == PieceColor.White) ? PieceTray.WhiteTray : PieceTray.BlackTray;
            Debug.LogWarning($"⚠️ IsPlayerPlacementCompleted: PieceTrayManager null, using static tray for {playerColor}");
        }

        if (playerTray == null)
        {
            Debug.LogWarning($"PlacementManager: {playerColor} tray is null - assuming completed");
            return true;
        }

        int piecesInTray = playerTray.GetPieceCount();
        bool completed = piecesInTray == 0;

        Debug.Log($"PlacementManager: {playerColor} placement completion check - tray instance {playerTray.GetInstanceID()}, pieces in tray: {piecesInTray}, completed: {completed}");

        return completed;
    }
    
    /// <summary>
    /// Check if both players have placed sufficient pieces to start gameplay
    /// For AI vs AI, we want a reasonable number of pieces but not full placement
    /// </summary>
    private bool HasSufficientPiecesForGameplay()
    {
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("PlacementManager: Cannot check piece count - ChessBoard.Instance is null");
            return false;
        }
        
        int whitePieces = 0;
        int blackPieces = 0;

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Count placed pieces by color
        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null)
                    {
                        if (piece.pieceColor == PieceColor.White)
                            whitePieces++;
                        else if (piece.pieceColor == PieceColor.Black)
                            blackPieces++;
                    }
                }
            }
        }
        
        // For AI vs AI, require at least 8 pieces per player (including King + Queen minimum)
        const int minPiecesPerPlayer = 8;
        bool sufficientPieces = whitePieces >= minPiecesPerPlayer && blackPieces >= minPiecesPerPlayer;
        
        Debug.Log($"PlacementManager: Piece count - White: {whitePieces}, Black: {blackPieces}");
        Debug.Log($"PlacementManager: Sufficient pieces ({minPiecesPerPlayer}+ each): {sufficientPieces}");
        
        // Also verify each player has at least a King (essential for gameplay)
        bool hasRequiredPieces = HasEssentialPieces(PieceColor.White) && HasEssentialPieces(PieceColor.Black);
        Debug.Log($"PlacementManager: Has essential pieces (Kings): {hasRequiredPieces}");
        
        return sufficientPieces && hasRequiredPieces;
    }
    
    /// <summary>
    /// Check if a player has placed essential pieces (at minimum, a King)
    /// </summary>
    private bool HasEssentialPieces(PieceColor playerColor)
    {
        if (ChessBoard.Instance == null)
        {
            return false;
        }
        
        bool hasKing = false;

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Search for essential pieces
        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null && piece.pieceColor == playerColor)
                    {
                        if (piece.pieceType == ChessPieceType.King)
                        {
                            hasKing = true;
                        }
                    }
                }
            }
        }
        
        Debug.Log($"PlacementManager: {playerColor} essential pieces - King: {hasKing}");
        return hasKing;
    }
    
    /// <summary>
    /// Validate and repair tray piece positions to ensure AI placement can find available pieces
    /// Critical fix for all player colors where tray pieces get corrupted with valid board positions
    /// SCALABILITY FIX: Now supports all 6 player colors (White/Black/Green/Purple/Yellow/Orange)
    /// </summary>
    private void ValidateAndRepairTrayPositions()
    {
        Debug.Log("🔧 ValidateAndRepairTrayPositions: Starting tray piece validation and repair for all active players...");

        int repairedPieces = 0;
        int totalPiecesChecked = 0;

        // Get all active players from PlayerManager
        if (PlayerManager.Instance == null)
        {
            Debug.LogError("🚨 ValidateAndRepairTrayPositions: PlayerManager.Instance is NULL - cannot get active players!");
            return;
        }

        List<PieceColor> activePlayers = PlayerManager.Instance.GetActivePlayers();
        Debug.Log($"🔧 ValidateAndRepairTrayPositions: Checking {activePlayers.Count} active player trays");

        // Check all active player trays using dynamic tray lookup
        foreach (PieceColor playerColor in activePlayers)
        {
            PieceTray playerTray = PieceTray.GetTrayForColor(playerColor);

            if (playerTray != null)
            {
                Debug.Log($"🔧 Checking {playerColor} Tray pieces...");
                ChessPiece[] trayPieces = playerTray.GetComponentsInChildren<ChessPiece>();
                Debug.Log($"🔧 Found {trayPieces.Length} pieces in {playerColor} tray");

                foreach (ChessPiece piece in trayPieces)
                {
                    totalPiecesChecked++;

                    if (piece.CurrentPosition.IsValid())
                    {
                        Debug.LogError($"🚨 CORRUPTION DETECTED: {playerColor} tray piece {piece.pieceType} has VALID position {piece.CurrentPosition} - should be invalid!");
                        Debug.LogError($"🚨 This prevents AI placement from finding available pieces!");

                        // Force reset to invalid position
                        piece.SetCurrentPosition(new BoardPosition(-1, -1, -1));
                        repairedPieces++;

                        Debug.Log($"✅ REPAIRED: Reset {piece.pieceColor} {piece.pieceType} to invalid position");
                    }
                    else
                    {
                        Debug.Log($"✅ OK: {playerColor} {piece.pieceType} has correct invalid position {piece.CurrentPosition}");
                    }
                }
            }
            else
            {
                Debug.LogError($"🚨 {playerColor} Tray not found!");
            }
        }

        Debug.Log($"🔧 ValidateAndRepairTrayPositions COMPLETE: Checked {totalPiecesChecked} pieces across {activePlayers.Count} trays");
        Debug.Log($"🔧 Repaired {repairedPieces} corrupted tray pieces");

        if (repairedPieces > 0)
        {
            Debug.Log($"✅ TRAY REPAIR SUCCESS: Fixed {repairedPieces} tray pieces - AI placement should now work!");
        }
        else
        {
            Debug.Log($"✅ TRAY VALIDATION CLEAN: All tray pieces have correct invalid positions");
        }
    }
    
    // === AI-HUMAN SELECTION STATE PROTECTION ===
    
    /// <summary>
    /// Save the current human selection state before AI placement
    /// </summary>
    public void SaveHumanSelectionState()
    {
        // Only save if there's an actual human selection and AI isn't already in progress
        if (!isAIPlacementInProgress && selectedTrayPiece != null)
        {
            // Determine if current selection is from a human player
            bool isHumanSelection = IsCurrentSelectionFromHuman();
            
            if (isHumanSelection)
            {
                savedHumanSelectedPiece = selectedTrayPiece;
                Debug.Log($"🔒 PlacementManager: Saved human selection - {savedHumanSelectedPiece.pieceColor} {savedHumanSelectedPiece.pieceType}");
            }
        }
        
        isAIPlacementInProgress = true;
    }
    
    /// <summary>
    /// Restore the human selection state after AI placement
    /// </summary>
    public void RestoreHumanSelectionState()
    {
        isAIPlacementInProgress = false;
        
        if (savedHumanSelectedPiece != null)
        {
            // Verify the saved piece is still in a tray (not placed by AI) - supports all 6 player colors
            PieceTray savedPieceTray = PieceTray.GetTrayForColor(savedHumanSelectedPiece.pieceColor);
            bool isPieceStillInTray = (savedPieceTray != null && savedHumanSelectedPiece.transform.IsChildOf(savedPieceTray.transform));
            
            if (isPieceStillInTray)
            {
                Debug.Log($"🔓 PlacementManager: Restoring human selection - {savedHumanSelectedPiece.pieceColor} {savedHumanSelectedPiece.pieceType}");
                
                // Restore selection without going through OnTrayPieceClicked to avoid interference
                selectedTrayPiece = savedHumanSelectedPiece;
                
                // Restore visual selection glow
                savedHumanSelectedPiece.OnSelected();
                
                // Restore placement indicators
                ShowValidPlacementPositions();
            }
            else
            {
                Debug.Log($"🔓 PlacementManager: Saved human piece was placed, clearing selection");
                selectedTrayPiece = null;
                ClearPlacementIndicators();
            }
            
            savedHumanSelectedPiece = null;
        }
    }
    
    /// <summary>
    /// Check if the current selection is from a human player
    /// </summary>
    private bool IsCurrentSelectionFromHuman()
    {
        if (selectedTrayPiece == null || TurnManager.Instance == null)
        {
            return false;
        }
        
        // Check if the piece's color corresponds to a human player
        PieceColor pieceColor = selectedTrayPiece.pieceColor;
        bool isPlayerHuman = !TurnManager.Instance.IsPlayerAI(pieceColor);
        
        Debug.Log($"🔍 PlacementManager: Selection analysis - {pieceColor} piece, isPlayerHuman: {isPlayerHuman}");
        
        return isPlayerHuman;
    }
    
    /// <summary>
    /// Execute AI placement without interfering with human selection
    /// This is the protected version that should be called by AI
    /// </summary>
    /// <summary>
    /// Place every piece still in a player's tray, using the AI placement engine to
    /// pick sensible squares. Lets a human skip placing pieces one at a time.
    /// </summary>
    public void AutoPlaceRemaining(PieceColor playerColor)
    {
        if (!OnlineSession.CanControl(playerColor)) return;
        if (isAutoPlacing) return;
        StartCoroutine(AutoPlaceRemainingRoutine(playerColor));
    }

    public bool IsAutoPlacing => isAutoPlacing;
    private bool isAutoPlacing;

    private System.Collections.IEnumerator AutoPlaceRemainingRoutine(PieceColor playerColor)
    {
        isAutoPlacing = true;
        if (selectedTrayPiece != null && selectedTrayPiece.pieceColor == playerColor)
        {
            DeselectTrayPiece();
        }

        var engine = new AIPlacementEngine(AIDifficulty.Hard);
        // Guard against looping forever if a placement is rejected
        for (int i = 0; i < 64 && !IsPlayerPlacementCompleted(playerColor); i++)
        {
            AIPlacement placement = engine.FindBestPlacement(playerColor);
            if (placement == null || placement.piece == null) break;
            ExecuteAIPlacement(placement.piece, placement.position);
            yield return new WaitForSeconds(0.12f); // let each piece land visibly
        }
        isAutoPlacing = false;
    }

    public void ExecuteAIPlacement(ChessPiece aiPiece, BoardPosition targetPosition)
    {
        Debug.Log($"🤖 PlacementManager: ExecuteAIPlacement ENTRY - {aiPiece.pieceColor} {aiPiece.pieceType} at {targetPosition}");
        
        // Save current human selection state
        SaveHumanSelectionState();
        
        try
        {
            // Temporarily override selectedTrayPiece for AI placement
            ChessPiece previousSelection = selectedTrayPiece;
            selectedTrayPiece = aiPiece;
            
            // Execute the placement directly without going through human selection path
            PlacePieceOnBoard(aiPiece, targetPosition);
            
            Debug.Log($"🤖 PlacementManager: AI placement completed successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"🤖 PlacementManager: AI placement failed - {e.Message}");
        }
        finally
        {
            // Always restore human selection state
            RestoreHumanSelectionState();
        }
        
        Debug.Log($"🤖 PlacementManager: ExecuteAIPlacement EXIT");
    }
    
    /// <summary>
    /// Show feedback when human tries to interact with AI player pieces
    /// </summary>
    /// <param name="aiPlayerColor">Color of the AI player's piece that was clicked</param>
    private void ShowHumanAIInteractionFeedback(PieceColor aiPlayerColor)
    {
        Debug.LogWarning($"🚫 PlacementManager: Human-AI interaction blocked - cannot interact with {aiPlayerColor} AI pieces");
        
        // Visual feedback - similar to invalid turn feedback during gameplay
        // This could be enhanced with UI notifications, sound effects, etc.
        
        // For now, provide console feedback and potentially trigger UI warnings
        string playerTypeDescription = TurnManager.Instance?.GetPlayerType(aiPlayerColor).ToString() ?? "Unknown";
        Debug.Log($"🔍 Interaction Details: {aiPlayerColor} is {playerTypeDescription} player, interaction blocked");
        
        // TODO: Add visual/audio feedback here
        // Examples:
        // - Play error sound
        // - Show temporary UI message
        // - Highlight valid pieces for human interaction
        // - Shake/flash the clicked piece briefly
    }
}