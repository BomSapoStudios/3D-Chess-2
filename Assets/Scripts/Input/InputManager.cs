using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class InputManager : MonoBehaviour
{
    [Header("Input Settings")]
    public Camera gameCamera;
    public LayerMask interactableLayer = -1;
    
    private ChessPiece selectedPiece;
    public ChessPiece SelectedPiece => selectedPiece;
    private bool isProcessingInput = false;
    
    public static InputManager Instance { get; private set; }
    
    private void Awake()
    {
        Debug.Log("🚀 InputManager.Awake: CALLED - InputManager starting up!");
        Debug.Log($"InputManager.Awake: GameObject name={gameObject.name}");
        Debug.Log($"InputManager.Awake: Time.time={Time.time}");
        
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("✅ InputManager.Awake: Set as singleton Instance");
        }
        else
        {
            Debug.LogWarning("⚠️ InputManager.Awake: Another instance already exists, destroying this one");
            Destroy(gameObject);
            return;
        }
        
        if (gameCamera == null)
        {
            gameCamera = Camera.main;
            Debug.Log($"InputManager.Awake: Camera set to {(gameCamera != null ? gameCamera.name : "NULL")}");
        }
        
        Debug.Log("✅ InputManager.Awake: Initialization complete, Update should begin next frame");
    }
    
    private void Start()
    {
        // Subscribe to turn change events to automatically deselect pieces when turn changes
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += OnTurnChanged;
            Debug.Log("InputManager: Subscribed to TurnManager.OnTurnChanged");
        }
        else
        {
            Debug.LogWarning("InputManager: TurnManager.Instance not found - cannot subscribe to turn changes");
        }
        
        // Subscribe to game state changes to clear selection when transitioning states
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
            Debug.Log("InputManager: Subscribed to GameStateManager.OnStateChanged");
        }
        else
        {
            Debug.LogWarning("InputManager: GameStateManager.Instance not found - cannot subscribe to state changes");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= OnTurnChanged;
        }
        
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
    }
    
    /// <summary>
    /// Handle turn change events - automatically deselect pieces from previous turn
    /// </summary>
    private void OnTurnChanged(PieceColor previousPlayer, PieceColor newCurrentPlayer)
    {
        Debug.Log($"InputManager.OnTurnChanged: Turn changed from {previousPlayer} to {newCurrentPlayer}");

        // If we have a piece selected and it's not the current player's turn, deselect it
        if (selectedPiece != null && selectedPiece.pieceColor != newCurrentPlayer)
        {
            Debug.Log($"InputManager.OnTurnChanged: Auto-deselecting {selectedPiece.pieceColor} {selectedPiece.pieceType} - not their turn");
            DeselectPiece();
        }
    }
    
    /// <summary>
    /// Handle game state changes - clear selection when transitioning between states
    /// </summary>
    private void OnGameStateChanged(GameState newState)
    {
        Debug.Log($"InputManager.OnGameStateChanged: Game state changed to {newState}");
        
        // Clear any selected piece when transitioning to Playing state to ensure clean start
        if (newState == GameState.Playing && selectedPiece != null)
        {
            Debug.Log($"InputManager.OnGameStateChanged: Clearing selection on transition to Playing state");
            DeselectPiece();
        }
        
        // Also clear selection when leaving Playing state
        if (newState != GameState.Playing && selectedPiece != null)
        {
            Debug.Log($"InputManager.OnGameStateChanged: Clearing selection on transition from Playing state");
            DeselectPiece();
        }
    }
    
    private void Update()
    {
        HandleInput();
    }
    
    private void HandleInput()
    {
        if (isProcessingInput) 
        {
            Debug.Log("HandleInput: Skipping - input processing in progress");
            return;
        }
        
        // Check if chaos animation is in progress and block input
        if (ChaosRotationManager.Instance != null && ChaosRotationManager.Instance.IsChaosAnimationInProgress())
        {
            Debug.Log("HandleInput: Skipping - chaos animation in progress");
            return;
        }
        
        // Handle forfeit and draw keyboard shortcuts (only during gameplay)
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanMovePieces())
        {
            HandleGameActionShortcuts();
        }
        
        // Process each tap or click exactly once. On iOS a touch also raises
        // simulated mouse events, so handling both would run the same tap 2-3 times.
        // A tap counts when the finger lifts, and only if it stayed put and no second finger
        // joined: putting a finger down to turn the board or pinch must never select or move.
        if (InputHelper.TouchCount > 0)
        {
            Touch touch = InputHelper.GetTouch(0);
            if (InputHelper.TouchCount > 1) tapPending = false;
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    tapPending = InputHelper.TouchCount == 1;
                    tapStart = touch.position;
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (Vector2.Distance(touch.position, tapStart) > TapSlop) tapPending = false;
                    break;
                case TouchPhase.Ended:
                    if (tapPending && Vector2.Distance(touch.position, tapStart) <= TapSlop) ProcessTouch(tapStart);
                    tapPending = false;
                    break;
                case TouchPhase.Canceled:
                    tapPending = false;
                    break;
            }
        }
        else if (InputHelper.GetMouseButtonDown(0))
        {
            mouseTapPending = true;
            tapStart = InputHelper.MousePosition;
        }
        else if (mouseTapPending && InputHelper.GetMouseButton(0))
        {
            if (Vector2.Distance(InputHelper.MousePosition, tapStart) > TapSlop) mouseTapPending = false;
        }
        else if (mouseTapPending)
        {
            mouseTapPending = false;
            if (Vector2.Distance(InputHelper.MousePosition, tapStart) <= TapSlop) ProcessTouch(tapStart);
        }
        
        // Keyboard testing
        if (InputHelper.GetKeyDown(KeyCode.Space))
        {
            // Select first piece for testing
            ChessPiece[] allPieces = FindObjectsByType<ChessPiece>(FindObjectsSortMode.None);
            if (allPieces.Length > 0)
            {
                OnPieceClicked(allPieces[0]);
            }
        }
    }
    
    // Tap tracking (see HandleInput)
    private bool tapPending, mouseTapPending;
    private Vector2 tapStart;

    /// <summary>How far, in pixels, a finger may wander and still be a tap (about 2 mm)</summary>
    private static float TapSlop => Mathf.Max(10f, (Screen.dpi > 0f ? Screen.dpi : 160f) * 0.08f);

    private void ProcessTouch(Vector2 screenPosition)
    {
        // Online: during play, input only works on your own turn
        if (OnlineSession.IsActive && GameStateManager.Instance != null && GameStateManager.Instance.CanMovePieces()
            && TurnManager.Instance != null && TurnManager.Instance.GetCurrentPlayer() != OnlineSession.LocalColor)
        {
            return;
        }

        // A tap on an on-screen button must not also select or place on the board behind it
        if (TouchGUI.IsOverUI(screenPosition))
        {
            return;
        }

        // While the Turn Planner is open, board taps only pick which piece to plan; they never move anything
        if (TurnPlannerUI.IsOpen)
        {
            if (gameCamera != null) TurnPlannerUI.Instance.OnBoardTap(gameCamera.ScreenPointToRay(screenPosition));
            return;
        }

        // Check if game state allows any interaction
        if (GameStateManager.Instance == null)
        {
            Debug.LogWarning("ProcessTouch: GameStateManager not available, ignoring input");
            Debug.LogWarning("🔧 Attempting GameStateManager recovery...");
            AttemptSingletonRecovery();
            return;
        }
        
        if (!GameStateManager.Instance.IsGameActive())
        {
            return;
        }
        
        if (gameCamera == null) 
        {
            Debug.LogError("ProcessTouch: gameCamera is NULL! Cannot perform raycast.");
            return;
        }
        
        Ray ray = gameCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;
        bool anyHit = RaycastForInteractable(ray, out hit);

        // Queen Blitz with the queen picked up: a tap that isn't on a dot or a piece of hers may be on a
        // portal, its pad, or a square she slides past (QueenBlitzTrails)
        if (QueenBlitz.Active && QueenBlitzTrails.Instance != null
            && !(anyHit && (hit.collider.GetComponent<MoveTargetData>() != null || hit.collider.GetComponent<ChessPiece>() == selectedPiece))
            && QueenBlitzTrails.Instance.OnTap(screenPosition, gameCamera))
            return;

        if (anyHit)
        {
            // Check for placement target clicks first (during placement phase)
            PlacementTargetData placementData = hit.collider.GetComponent<PlacementTargetData>();
            if (placementData != null && GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
            {
                if (PlacementManager.Instance != null)
                {
                    PlacementManager.Instance.OnPlacementTargetClicked(placementData.targetPosition);
                }
                return;
            }
            
            // Check for move target clicks (during play phase) AND placement targets (during placement phase)
            MoveTargetData moveData = hit.collider.GetComponent<MoveTargetData>();
            if (moveData != null && GameStateManager.Instance != null)
            {
                if (GameStateManager.Instance.CanMovePieces())
                {
                    // During play phase - handle as move target
                    OnMoveTargetClicked(moveData.targetPosition);
                    return;
                }
                else if (GameStateManager.Instance.CanPlacePieces())
                {
                    // During placement phase - handle as placement target
                    Debug.Log($"InputManager: PLACEMENT TARGET clicked at {moveData.targetPosition}");
                    if (PlacementManager.Instance != null)
                    {
                        PlacementManager.Instance.OnPlacementTargetClicked(moveData.targetPosition);
                    }
                    return;
                }
            }
            
            // Handle piece clicks
            ChessPiece hitPiece = hit.collider.GetComponent<ChessPiece>();
            if (hitPiece != null)
            {
                
                // Handle piece clicks based on game state
                if (GameStateManager.Instance.CanPlacePieces())
                {
                    // Local games: only the pieces of the player in view can be picked up. A tap on
                    // anyone else's piece (often the end of turning the cube) is ignored.
                    PieceColor? inFocus = PlacementUI.InView ?? PlacementUI.WorkingOn;
                    if (!OnlineSession.IsActive && inFocus.HasValue && hitPiece.pieceColor != inFocus.Value)
                    {
                        return;
                    }

                    // During placement phase - allow both tray piece selection and board piece repositioning
                    Debug.Log($"InputManager: PLACEMENT PHASE - Handling piece click for {hitPiece.pieceColor} {hitPiece.pieceType}");

                    // Check if this piece is in a tray or on the board (supports all 6 player colors)
                    PieceTray playerTray = PieceTray.GetTrayForColor(hitPiece.pieceColor);
                    bool pieceInTray = (playerTray != null && hitPiece.transform.IsChildOf(playerTray.transform));
                    
                    // Additional validation: check for position sync issues
                    bool hasPositionIssues = !hitPiece.ValidatePosition();
                    
                    if (pieceInTray)
                    {
                        Debug.Log($"InputManager: Piece is in tray - allowing selection for placement");
                        if (PlacementManager.Instance != null)
                        {
                            PlacementManager.Instance.OnTrayPieceClicked(hitPiece);
                        }
                    }
                    else if (hasPositionIssues)
                    {
                        Debug.LogWarning($"🔧 InputManager: Piece {hitPiece.pieceColor} {hitPiece.pieceType} has position sync issues - attempting repair before repositioning");
                        
                        // Try to repair position first
                        if (hitPiece.RepairPosition())
                        {
                            Debug.Log($"🔧 Position repair successful - proceeding with repositioning");
                            if (PlacementManager.Instance != null)
                            {
                                PlacementManager.Instance.OnBoardPieceClickedForRepositioning(hitPiece);
                            }
                        }
                        else
                        {
                            Debug.LogError($"🔧 ❌ Position repair failed for {hitPiece.pieceColor} {hitPiece.pieceType} - cannot process click");
                            Debug.LogError($"🔧 Piece thinks it's at {hitPiece.CurrentPosition}, but position validation failed");
                        }
                    }
                    else
                    {
                        Debug.Log($"InputManager: Piece is on board with valid position - routing to PlacementManager for repositioning");
                        if (PlacementManager.Instance != null)
                        {
                            PlacementManager.Instance.OnBoardPieceClickedForRepositioning(hitPiece);
                        }
                    }
                }
                else if (GameStateManager.Instance.CanMovePieces())
                {
                    // During play phase - ONLY allow piece movement, enforce chess rules
                    Debug.Log($"InputManager: PLAY PHASE - Handling gameplay piece click for {hitPiece.pieceColor} {hitPiece.pieceType}");
                    OnPieceClicked(hitPiece);
                }
                else
                {
                    // Invalid game state for piece interaction
                    Debug.LogWarning($"InputManager: Cannot interact with pieces in current game state: {GameStateManager.Instance.currentState}");
                }
                return;
            }
            
            Debug.Log($"❌ Hit object '{hit.collider.name}' has no ChessPiece or MoveTargetData component");
            
            // Check what components the hit object does have
            Component[] components = hit.collider.GetComponents<Component>();
            Debug.Log($"   Hit object components: {string.Join(", ", System.Array.ConvertAll(components, c => c.GetType().Name))}");
        }
        // A tap that hits nothing is off the board: it does nothing (it used to guess a square
        // 10 units in front of the camera and move there)
    }
    
    public void OnPieceClicked(ChessPiece piece)
    {
        Debug.Log($"🎯 OnPieceClicked: Called with piece {(piece != null ? $"{piece.pieceColor} {piece.pieceType}" : "NULL")}");
        
        // During placement phase, allow selecting placed pieces for repositioning
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            Debug.Log($"OnPieceClicked: Placement phase - allowing piece selection for repositioning {piece.pieceColor} {piece.pieceType}");
        }
        
        if (isProcessingInput) 
        {
            Debug.Log("OnPieceClicked: Skipping - input processing in progress");
            return;
        }
        
        if (piece == null) 
        {
            Debug.LogError("OnPieceClicked: Piece is NULL!");
            return;
        }
        
        // FIXED TURN VALIDATION: Check turn order BEFORE processing any moves (capture or selection)
        PieceColor currentPlayer = GameStateManager.Instance?.GetCurrentPlayer() ?? PieceColor.White;
        
        // If this is an enemy piece and we have a piece selected, treat it as a capture attempt
        // BUT FIRST: Validate that the selected piece can move (it's their turn)
        if (selectedPiece != null && piece.pieceColor != selectedPiece.pieceColor)
        {
            // Check if the selected piece's color is allowed to move
            if (GameStateManager.Instance != null && !GameStateManager.Instance.CanMovePiece(selectedPiece))
            {
                Debug.LogWarning($"🚫 OnPieceClicked: TURN VIOLATION - {selectedPiece.pieceColor} {selectedPiece.pieceType} cannot capture because it's {currentPlayer}'s turn!");
                ShowInvalidTurnFeedback(selectedPiece.pieceColor, currentPlayer);
                return;
            }
            
            Debug.Log($"OnPieceClicked: Enemy piece clicked - attempting capture of {piece.pieceColor} {piece.pieceType} with {selectedPiece.pieceColor} {selectedPiece.pieceType}");
            TryMovePiece(piece.CurrentPosition);
            return;
        }
        
        // For piece selection, check turn validation
        bool canMovePiece = GameStateManager.Instance?.CanMovePiece(piece) ?? false;
        Debug.Log($"🔍 OnPieceClicked: Turn validation - {piece.pieceColor} piece, current turn: {currentPlayer}, can move: {canMovePiece}");
        
        if (GameStateManager.Instance != null && !canMovePiece)
        {
            Debug.LogWarning($"🚫 OnPieceClicked: TURN VIOLATION - {piece.pieceColor} tried to move but it's {currentPlayer}'s turn!");
            
            // Show visual feedback for invalid turn attempt
            ShowInvalidTurnFeedback(piece.pieceColor, currentPlayer);
            return;
        }
        
        // If no piece is selected, select this piece
        if (selectedPiece == null)
        {
            Debug.Log("OnPieceClicked: No piece currently selected - selecting this piece");
            SelectPiece(piece);
        }
        // If the same piece is clicked, deselect it
        else if (selectedPiece == piece)
        {
            DeselectPiece();
        }
        // If a different piece is clicked
        else
        {
            // If it's the same color, select the new piece (only if it's their turn)
            if (selectedPiece.pieceColor == piece.pieceColor)
            {
                DeselectPiece();
                SelectPiece(piece);
            }
            // If it's a different color, try to capture
            else
            {
                TryMovePiece(piece.CurrentPosition);
            }
        }
    }
    
    public void OnMoveTargetClicked(BoardPosition targetPosition)
    {
        Debug.Log($"🎯 OnMoveTargetClicked: Called with position {targetPosition}");
        Debug.Log($"OnMoveTargetClicked: Current time = {Time.time:F2}");
        
        if (isProcessingInput) 
        {
            Debug.LogWarning("OnMoveTargetClicked: Skipping - input processing in progress");
            return;
        }
        
        if (selectedPiece == null)
        {
            Debug.LogError("OnMoveTargetClicked: No piece selected, cannot move to target");
            return;
        }
        
        Debug.Log($"OnMoveTargetClicked: Selected piece = {selectedPiece.pieceColor} {selectedPiece.pieceType} at {selectedPiece.CurrentPosition}");
        Debug.Log($"OnMoveTargetClicked: Target position = {targetPosition}");
        Debug.Log($"OnMoveTargetClicked: About to call TryMovePiece({targetPosition})");
        
        TryMovePiece(targetPosition);
        
        Debug.Log($"OnMoveTargetClicked: TryMovePiece call completed");
    }
    
    public void OnCellClicked(BoardPosition position)
    {
        if (isProcessingInput) return;
        
        Debug.Log($"Cell clicked at {position}. Selected piece: {(selectedPiece != null ? $"{selectedPiece.pieceColor} {selectedPiece.pieceType} at {selectedPiece.CurrentPosition}" : "None")}");
        
        // If a piece is selected, try to move it to this position
        if (selectedPiece != null)
        {
            Debug.Log($"Attempting to move {selectedPiece.pieceType} to {position}");
            TryMovePiece(position);
        }
        else
        {
            // Check if there's a piece at this position to select
            ChessPiece pieceAtPosition = ChessBoard.Instance?.GetPieceAt(position);
            if (pieceAtPosition != null)
            {
                Debug.Log($"Found piece to select: {pieceAtPosition.pieceColor} {pieceAtPosition.pieceType}");
                SelectPiece(pieceAtPosition);
            }
            else
            {
                Debug.Log($"No piece at {position} and no piece selected");
            }
        }
    }
    
    /// <summary>Queen Blitz: play the selected queen's move to this square (a portal tap)</summary>
    public void PlayBlitzMove(BoardPosition to) => TryMovePiece(to);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>Pick up a piece as a tap would (BlitzDemo screenshots)</summary>
    public void DebugSelect(ChessPiece piece) => SelectPiece(piece);
#endif

    private void SelectPiece(ChessPiece piece)
    {
        if (piece == null) return;
        
        // CRITICAL NULL CHECK: Validate core systems before any operations
        if (ChessBoard.Instance == null)
        {
            Debug.LogError($"🚨 CRITICAL: Cannot select piece - ChessBoard.Instance is null!");
            AttemptSingletonRecovery();
            return;
        }
        
        // POSITION SYNC FIX: Validate and repair position before selection
        if (!piece.ValidatePosition())
        {
            Debug.LogWarning($"🔧 Position validation failed for {piece.pieceColor} {piece.pieceType}, attempting repair...");
            if (piece.RepairPosition())
            {
                Debug.Log($"✅ Position repair successful for {piece.pieceColor} {piece.pieceType}");
            }
            else
            {
                Debug.LogError($"❌ Position repair failed for {piece.pieceColor} {piece.pieceType}");
                // Even if repair fails, allow selection but with warning
                Debug.LogWarning($"⚠️ Allowing selection of corrupted piece for debugging purposes");
            }
        }
        
        selectedPiece = piece;
        selectedPiece.OnSelected();
        Haptics.Light(); // picked up
        Sfx.Play("piece_pick");
        
        // Add visual feedback for mobile users
        Debug.Log($"🔍 SelectPiece: About to call ShowValidMoveIndicators for {piece.pieceColor} {piece.pieceType}");
        ShowValidMoveIndicators();
        Debug.Log($"🔍 SelectPiece: ShowValidMoveIndicators completed for {piece.pieceColor} {piece.pieceType}");
        
        Debug.Log($"Selected {piece.pieceColor} {piece.pieceType} at {piece.CurrentPosition}");
    }
    
    /// <summary>Make a move chosen in the Turn Planner, through the same path as tapping it</summary>
    public bool PlannerMove(ChessPiece piece, BoardPosition target)
    {
        if (piece == null || isProcessingInput) return false;
        if (GameStateManager.Instance == null || !GameStateManager.Instance.CanMovePiece(piece)) return false;
        DeselectPiece();
        SelectPiece(piece);
        TryMovePiece(target, confirmed: true);
        return true;
    }

    /// <summary>True while a planner should not interfere (a piece is selected or moving)</summary>
    public bool IsBusy => isProcessingInput;

    /// <summary>A Deselect button under Turn Planner while a piece is picked up during play</summary>
    private void OnGUI()
    {
        if (selectedPiece == null || TurnPlannerUI.IsOpen) return;
        if (GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing) return;
        TouchGUI.Begin();
        if (TouchGUI.NeonButton(new Rect(16, 130, 150, 44), "Deselect", NeonTheme.Cyan, NeonTheme.Pink, fontSize: 16)) DeselectPiece();
        // Move! goes under Deselect, or under Queen Blitz's pawn tracker, which has that spot in a puzzle
        float moveY = QueenBlitzDirector.Instance != null ? Mathf.Max(182f, QueenBlitzDirector.Instance.TrackerBottom + 10f) : 182f;
        if (armedTarget.HasValue
            && TouchGUI.NeonButton(new Rect(16, moveY, 150, 44), "Move!", NeonTheme.Lime, NeonTheme.Pink, fontSize: 16))
            TryMovePiece(armedTarget.Value, confirmed: true);
        TouchGUI.End();
    }

    private void DeselectPiece()
    {
        if (selectedPiece != null)
        {
            selectedPiece.OnDeselected();
            selectedPiece = null;
            HideValidMoveIndicators();
            
            Debug.Log("Piece deselected");
        }
    }
    
    private List<GameObject> moveIndicators = new List<GameObject>();
    private List<ChessPiece> capturablePieces = new List<ChessPiece>();
    
    /// <summary>
    /// Add blue glow effect to a capturable enemy piece
    /// </summary>
    /// <summary>
    /// Pick what a tap meant, looking along the whole ray rather than just the first thing hit.
    /// On the big cubes, board cells, benches and other pieces' (deliberately large) touch areas
    /// often sit in front of the dot you aimed at, which made some captures impossible to tap.
    /// Priority: the nearest placement/move dot or piece of the side to move; then any piece;
    /// then the nearest hit of any kind.
    /// </summary>
    private bool RaycastForInteractable(Ray ray, out RaycastHit result)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, interactableLayer);
        result = default;
        if (hits.Length == 0) return false;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        PieceColor? toMove = GameStateManager.Instance != null ? GameStateManager.Instance.GetCurrentPlayer() : (PieceColor?)null;
        foreach (RaycastHit h in hits)
        {
            ChessPiece piece = h.collider.GetComponent<ChessPiece>();
            bool isTarget = h.collider.GetComponent<PlacementTargetData>() != null || h.collider.GetComponent<MoveTargetData>() != null;
            // Setup: any placed piece of a person on this device can be picked up again, so it has to
            // win over the placement dots behind it (the nearest of the two is taken)
            bool placing = GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces();
            bool ownPlaced = placing && piece != null && !piece.IsInTray() && piece.CurrentPosition.IsValid()
                && OnlineSession.CanControl(piece.pieceColor) && (TurnManager.Instance == null || !TurnManager.Instance.IsPlayerAI(piece.pieceColor));
            bool isOwnPiece = ownPlaced || (piece != null && toMove.HasValue && piece.pieceColor == toMove.Value && !piece.IsInTray());
            if (isTarget || isOwnPiece)
            {
                result = h;
                return true;
            }
        }
        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponent<ChessPiece>() != null)
            {
                result = h;
                return true;
            }
        }
        result = hits[0];
        return true;
    }

    private void AddCapturableGlow(ChessPiece piece)
    {
        if (piece == null) return;
        
        // Avoid duplicate glows
        if (capturablePieces.Contains(piece)) return;
        
        piece.AddCapturableGlow();
        capturablePieces.Add(piece);
        Debug.Log($"🔵 Added capturable glow to {piece.pieceColor} {piece.pieceType} at {piece.CurrentPosition}");
    }
    
    /// <summary>
    /// Emergency singleton recovery when ChessBoard.Instance becomes null
    /// </summary>
    private void AttemptSingletonRecovery()
    {
        Debug.LogWarning("🔧 EMERGENCY: Attempting singleton recovery...");
        
        // Try to find ChessBoard in scene
        ChessBoard foundBoard = FindFirstObjectByType<ChessBoard>();
        if (foundBoard != null)
        {
            Debug.Log("🔧 Found ChessBoard in scene, but Instance is null - possible singleton corruption");
            // The ChessBoard.Awake() should reset the instance, but force a recheck
            foundBoard.gameObject.SetActive(false);
            foundBoard.gameObject.SetActive(true);
            
            // After reactivation, validate board consistency
            if (ChessBoard.Instance != null)
            {
                StartCoroutine(ValidateBoardStateAfterRecovery());
            }
        }
        else
        {
            Debug.LogError("🚨 FATAL: No ChessBoard found in scene! Game state is completely corrupted.");
        }
        
        // Also check GameStateManager
        if (GameStateManager.Instance == null)
        {
            GameStateManager foundGSM = FindFirstObjectByType<GameStateManager>();
            if (foundGSM != null)
            {
                Debug.Log("🔧 Found GameStateManager in scene, forcing reactivation");
                foundGSM.gameObject.SetActive(false);
                foundGSM.gameObject.SetActive(true);
            }
        }
        
        // Check other critical singletons
        if (TurnManager.Instance == null)
        {
            TurnManager foundTM = FindFirstObjectByType<TurnManager>();
            if (foundTM != null)
            {
                Debug.Log("🔧 Found TurnManager in scene, forcing reactivation");
                foundTM.gameObject.SetActive(false);
                foundTM.gameObject.SetActive(true);
            }
        }
    }
    
    /// <summary>
    /// Validate board state after emergency recovery
    /// </summary>
    private System.Collections.IEnumerator ValidateBoardStateAfterRecovery()
    {
        yield return new UnityEngine.WaitForEndOfFrame(); // Wait for all Awake calls to complete
        
        Debug.Log("🔧 RECOVERY: Validating board state after singleton recovery...");
        
        if (ChessBoard.Instance != null)
        {
            // Find all pieces in the scene
            ChessPiece[] allPieces = FindObjectsByType<ChessPiece>(FindObjectsSortMode.None);
            int repairedPieces = 0;
            int corruptedPieces = 0;
            
            foreach (ChessPiece piece in allPieces)
            {
                if (!piece.IsInTray()) // Only validate board pieces
                {
                    if (!piece.ValidatePosition())
                    {
                        Debug.LogWarning($"🔧 RECOVERY: Attempting to repair {piece.pieceColor} {piece.pieceType}");
                        if (piece.RepairPosition())
                        {
                            repairedPieces++;
                            Debug.Log($"✅ RECOVERY: Repaired {piece.pieceColor} {piece.pieceType}");
                        }
                        else
                        {
                            corruptedPieces++;
                            Debug.LogError($"❌ RECOVERY: Failed to repair {piece.pieceColor} {piece.pieceType}");
                        }
                    }
                }
            }
            
            Debug.Log($"🔧 RECOVERY COMPLETE: {repairedPieces} pieces repaired, {corruptedPieces} remain corrupted");
            
            if (corruptedPieces == 0)
            {
                Debug.Log("✅ RECOVERY SUCCESS: All systems restored and validated");
            }
            else
            {
                Debug.LogWarning($"⚠️ RECOVERY PARTIAL: {corruptedPieces} pieces remain corrupted");
            }
        }
        else
        {
            Debug.LogError("🚨 RECOVERY FAILED: ChessBoard.Instance still null after recovery attempt");
        }
    }
    
    private void ShowValidMoveIndicators()
    {
        if (selectedPiece == null) 
        {
            Debug.Log("ShowValidMoveIndicators: selectedPiece is null");
            return;
        }
        
        // CRITICAL NULL CHECK: Prevent crashes when core systems are corrupted
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("🚨 CRITICAL: ChessBoard.Instance is null! Cannot show move indicators.");
            Debug.LogError("🚨 This indicates severe singleton corruption. Attempting emergency recovery...");
            AttemptSingletonRecovery();
            return;
        }
        
        Debug.Log($"ShowValidMoveIndicators: Creating indicators for {selectedPiece.pieceColor} {selectedPiece.pieceType}");
        
        // POSITION CORRUPTION FIX: Validate and repair piece position before getting legal moves
        Debug.Log($"🔍 POSITION VALIDATION: Checking {selectedPiece.pieceColor} {selectedPiece.pieceType} at position {selectedPiece.CurrentPosition}");
        
        if (!selectedPiece.CurrentPosition.IsValid())
        {
            Debug.LogError($"🚨 INVALID POSITION DETECTED: {selectedPiece.pieceColor} {selectedPiece.pieceType} has invalid position {selectedPiece.CurrentPosition}");
            Debug.LogError($"🚨 This will result in zero legal moves and no blue capture dots!");
            
            // Attempt immediate position repair
            Debug.Log($"🔧 ATTEMPTING EMERGENCY POSITION REPAIR for {selectedPiece.pieceColor} {selectedPiece.pieceType}...");
            bool repairSuccess = selectedPiece.RepairPosition();
            
            if (repairSuccess)
            {
                Debug.Log($"✅ POSITION REPAIR SUCCESS: {selectedPiece.pieceColor} {selectedPiece.pieceType} now at {selectedPiece.CurrentPosition}");
            }
            else
            {
                Debug.LogError($"❌ POSITION REPAIR FAILED: {selectedPiece.pieceColor} {selectedPiece.pieceType} still has invalid position");
                
                // Try fallback: search board array directly
                Debug.Log($"🔍 FALLBACK: Searching board array for {selectedPiece.pieceColor} {selectedPiece.pieceType}...");
                BoardPosition foundPosition = FindPieceInBoardArray(selectedPiece);
                
                if (foundPosition.IsValid())
                {
                    Debug.Log($"🎯 FALLBACK SUCCESS: Found {selectedPiece.pieceColor} {selectedPiece.pieceType} at {foundPosition}");
                    selectedPiece.SetCurrentPosition(foundPosition);
                }
                else
                {
                    Debug.LogError($"💀 TOTAL FAILURE: Cannot locate {selectedPiece.pieceColor} {selectedPiece.pieceType} anywhere on board");
                    Debug.LogError($"💀 Game state is corrupted. Blue dots will not appear. Please restart the game.");
                    return;
                }
            }
        }
        else
        {
            Debug.Log($"✅ POSITION VALID: {selectedPiece.pieceColor} {selectedPiece.pieceType} at {selectedPiece.CurrentPosition}");
        }

        // Clear any existing indicators before creating new ones
        // This acts as a safety net to ensure clean state, even if DeselectPiece() wasn't called
        HideValidMoveIndicators();

        var validMoves = selectedPiece.GetLegalMoves();
        Debug.Log($"🎯 LEGAL MOVES RESULT: {selectedPiece.pieceColor} {selectedPiece.pieceType} at {selectedPiece.CurrentPosition} has {validMoves.Count} legal moves");
        
        if (validMoves.Count == 0)
        {
            Debug.LogWarning($"⚠️ ZERO LEGAL MOVES: {selectedPiece.pieceColor} {selectedPiece.pieceType} has no legal moves!");
            Debug.LogWarning($"⚠️ This means NO indicators (including blue capture dots) will be created!");
            Debug.LogWarning($"⚠️ Position: {selectedPiece.CurrentPosition}, IsValid: {selectedPiece.CurrentPosition.IsValid()}");
        }
        else
        {
            // ENHANCED DEBUG: Preview all moves and potential captures
            Debug.Log($"🔍 MOVE PREVIEW: Analyzing {validMoves.Count} moves for {selectedPiece.pieceColor} {selectedPiece.pieceType}:");
            int captureCount = 0;
            for (int i = 0; i < validMoves.Count; i++)
            {
                BoardPosition move = validMoves[i];
                ChessPiece targetPiece = ChessBoard.Instance?.GetPieceAt(move);
                bool isCapture = targetPiece != null && targetPiece.pieceColor != selectedPiece.pieceColor;
                if (isCapture) captureCount++;
                
                string moveType = targetPiece == null ? "EMPTY" : (isCapture ? "CAPTURE" : "BLOCKED");
                string targetInfo = targetPiece != null ? $"{targetPiece.pieceColor} {targetPiece.pieceType}" : "none";
                Debug.Log($"🔍   Move {i+1}: {move} → {moveType} (target: {targetInfo})");
            }
            Debug.Log($"🎯 CAPTURE SUMMARY: Found {captureCount} potential captures out of {validMoves.Count} total moves");
        }
        
        // During placement phase, filter moves to respect placement zone restrictions
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            var placementValidMoves = new List<BoardPosition>();
            foreach (var move in validMoves)
            {
                if (PlacementManager.IsPositionInColorZone(move, selectedPiece.pieceColor))
                {
                    placementValidMoves.Add(move);
                }
            }
            validMoves = placementValidMoves;
            Debug.Log($"ShowValidMoveIndicators: After placement zone filtering: {validMoves.Count} moves");
        }
        
        foreach (var move in validMoves)
        {
            // ENHANCED INDICATOR CREATION: Better error handling and positioning
            try
            {
                // Create a glowing sphere at each legal move position
                GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                indicator.name = $"MoveIndicator_{move.x}_{move.y}_{move.z}";
                
                // Note: Using MoveTargetData component for identification instead of tags
                
                // Make indicator a child of the rotating board so it rotates with the board
                GameObject piecesContainer = GameObject.Find("Pieces Container");
                if (piecesContainer != null)
                {
                    indicator.transform.SetParent(piecesContainer.transform);
                    Debug.Log($"ShowValidMoveIndicators: Parented indicator {move} to Pieces Container");
                }
                else
                {
                    Debug.LogWarning($"ShowValidMoveIndicators: Pieces Container not found! Indicator {move} will be world-space");
                }
                
                // Position at the center of the target cell using local coordinates
                Vector3 localPos = ChessBoard.Instance.BoardToLocalPosition(move);
                indicator.transform.localPosition = localPos + Vector3.up * 0.5f; // Slightly above floor
                Debug.Log($"ShowValidMoveIndicators: Created indicator for {move} at local position {localPos}");
            
            // Same size as placement indicators so they are easy to see and tap
            indicator.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            
            // Keep collider but make it larger for mobile-friendly clicking
            SphereCollider indicatorCollider = indicator.GetComponent<SphereCollider>();
            if (indicatorCollider != null)
            {
                indicatorCollider.radius = 1.0f; // Larger than visual for easier touch targeting
                Debug.Log($"ShowValidMoveIndicators: Set collider radius to {indicatorCollider.radius} for move {move}");
            }
            
            // Add a component to store board position data
            MoveTargetData moveData = indicator.AddComponent<MoveTargetData>();
            moveData.targetPosition = move;
            
            // Make it glow with a bright material
            Renderer renderer = indicator.GetComponent<Renderer>();

            // FIXED: Use shader fallback system (same as check indicators)
            Shader indicatorShader = GetIndicatorShader();
            if (indicatorShader == null)
            {
                Debug.LogError($"🚨 FAILED to create move indicator for {move}: No suitable shader found!");
                Destroy(indicator);
                continue;
            }

            Material glowMaterial = new Material(indicatorShader);
            
            // SIMPLIFIED CAPTURE CHECK: Streamlined logic with fallback validation
            bool isCapture = false;
            ChessPiece targetPiece = null;
            
            // Primary capture detection
            if (ChessBoard.Instance != null)
            {
                targetPiece = ChessBoard.Instance.GetPieceAt(move);
                
                if (targetPiece != null && targetPiece.pieceColor != selectedPiece.pieceColor)
                {
                    isCapture = true;
                    Debug.Log($"✅ CAPTURE CONFIRMED: {selectedPiece.pieceColor} {selectedPiece.pieceType} can capture {targetPiece.pieceColor} {targetPiece.pieceType} at {move}");
                }
                else if (targetPiece != null)
                {
                    Debug.Log($"❌ SAME COLOR: Cannot capture own {targetPiece.pieceColor} {targetPiece.pieceType} at {move}");
                }
                else
                {
                    Debug.Log($"📍 EMPTY MOVE: {move} is empty - showing yellow indicator");
                }
            }
            else
            {
                Debug.LogError("🚨 CRITICAL: ChessBoard.Instance is null during capture check!");
            }
            
            // FALLBACK VALIDATION: Double-check capture status
            if (isCapture && targetPiece != null)
            {
                Debug.Log($"🔍 CAPTURE VALIDATION: {selectedPiece.pieceColor} vs {targetPiece.pieceColor} = {isCapture}");
            }
            
            // Choose color based on game phase and move type
            string colorChoice = "UNKNOWN";
            if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
            {
                glowMaterial.color = Color.green; // GREEN during placement phase
                colorChoice = "GREEN (placement phase)";
                Debug.Log($"🟢 INDICATOR COLOR: Using GREEN for placement phase indicator at {move}");
            }
            else if (isCapture)
            {
                // ENHANCED BLUE MATERIAL: Brighter blue with emission for better visibility
                Color brightBlue = new Color(0.3f, 0.6f, 1.0f, 0.9f); // Bright cyan-blue, slightly transparent
                glowMaterial.color = brightBlue;
                
                // Add emission for glow effect
                glowMaterial.EnableKeyword("_EMISSION");
                Color emissionColor = new Color(0.0f, 0.4f, 1.0f, 1.0f); // Bright blue emission
                glowMaterial.SetColor("_EmissionColor", emissionColor);
                
                colorChoice = "ENHANCED_BLUE (capture)";
                Debug.Log($"🔵 INDICATOR COLOR: Using ENHANCED BLUE with emission for CAPTURE indicator at {move}");
                
                // ADDITIONAL: Highlight the capturable piece itself with blue glow
                if (targetPiece != null)
                {
                    Debug.Log($"🔵 PIECE HIGHLIGHTING: Adding blue glow to capturable {targetPiece.pieceColor} {targetPiece.pieceType} at {move}");
                    AddCapturableGlow(targetPiece);
                }
            }
            else
            {
                glowMaterial.color = Color.yellow; // YELLOW for regular moves during play phase
                colorChoice = "YELLOW (regular move)";
                Debug.Log($"🟡 INDICATOR COLOR: Using YELLOW for regular move indicator at {move}");
            }
            
            Debug.Log($"🎨 FINAL INDICATOR: {move} → {colorChoice} (isCapture={isCapture}, GameState={GameStateManager.Instance?.currentState})");
            
            // Enhanced material properties for all indicators
            glowMaterial.SetFloat("_Smoothness", 0.9f);
            glowMaterial.SetFloat("_Metallic", 0.1f);
            
            // Set transparency properties for better blending
            glowMaterial.SetFloat("_Surface", 1); // Transparent surface
            glowMaterial.SetFloat("_Blend", 0); // Alpha blend
            glowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            glowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glowMaterial.SetInt("_ZWrite", 0);
            glowMaterial.renderQueue = 3000; // Render after opaque objects

            // A capture marker sits on the enemy piece, so make it see-through glass
            if (isCapture && !(GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces()))
            {
                Material template = Resources.Load<Material>("PieceSets/Crystal");
                if (template != null)
                {
                    glowMaterial = new Material(template);
                    glowMaterial.color = new Color(NeonTheme.Cyan.r, NeonTheme.Cyan.g, NeonTheme.Cyan.b, AccessibilityPrefs.HighContrast ? 0.6f : 0.36f);
                    glowMaterial.SetFloat("_Glossiness", 0.85f);
                    glowMaterial.renderQueue = 3000;
                }
            }

            renderer.material = glowMaterial;
            
            moveIndicators.Add(indicator);
            Debug.Log($"ShowValidMoveIndicators: Successfully created indicator {moveIndicators.Count} for {move}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"🚨 FAILED to create move indicator for {move}: {ex.Message}");
                Debug.LogError($"Exception: {ex}");
            }
        }
        
        Debug.Log($"ShowValidMoveIndicators: Total indicators created: {moveIndicators.Count} out of {validMoves.Count} moves");
    }
    
    // ── Tap Twice to Move ──
    // The marked square (null when none) and its dot as it looked before it was marked
    private BoardPosition? armedTarget;
    private GameObject armedIndicator;
    private Vector3 armedScale;
    private Color armedColor;

    /// <summary>Mark a legal square: its dot grows and turns lime until tapped again or Move! is pressed</summary>
    private void ArmMove(BoardPosition target)
    {
        DisarmMove();
        armedTarget = target;
        foreach (GameObject indicator in moveIndicators)
        {
            MoveTargetData data = indicator != null ? indicator.GetComponent<MoveTargetData>() : null;
            if (data == null || data.targetPosition != target) continue;
            armedIndicator = indicator;
            armedScale = indicator.transform.localScale;
            Renderer r = indicator.GetComponent<Renderer>();
            if (r != null)
            {
                armedColor = r.material.color;
                r.material.color = new Color(NeonTheme.Lime.r, NeonTheme.Lime.g, NeonTheme.Lime.b, Mathf.Max(armedColor.a, 0.75f));
            }
            indicator.transform.localScale = armedScale * 1.35f;
            break;
        }
        Haptics.Selection();
        Sfx.Play("piece_pick", 0.7f);
    }

    private void DisarmMove()
    {
        if (armedIndicator != null)
        {
            armedIndicator.transform.localScale = armedScale;
            Renderer r = armedIndicator.GetComponent<Renderer>();
            if (r != null) r.material.color = armedColor;
        }
        armedIndicator = null;
        armedTarget = null;
    }

    private void HideValidMoveIndicators()
    {
        DisarmMove();
        Debug.Log($"HideValidMoveIndicators: Cleaning up {moveIndicators.Count} indicators");
        
        int destroyedCount = 0;
        int nullCount = 0;
        
        foreach (var indicator in moveIndicators)
        {
            if (indicator != null)
            {
                try
                {
                    // Immediately disable the indicator to make it invisible this frame
                    // Destroy() schedules destruction at end of frame, so disable ensures instant visual clearing
                    indicator.SetActive(false);
                    Destroy(indicator);
                    destroyedCount++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Failed to destroy indicator: {ex.Message}");
                }
            }
            else
            {
                nullCount++;
            }
        }
        
        moveIndicators.Clear();
        
        // Clear capturable piece glows
        int glowsCleared = 0;
        foreach (var piece in capturablePieces)
        {
            if (piece != null)
            {
                piece.RemoveCapturableGlow();
                glowsCleared++;
                Debug.Log($"🔵 Removed capturable glow from {piece.pieceColor} {piece.pieceType}");
            }
        }
        
        capturablePieces.Clear();
        
        Debug.Log($"HideValidMoveIndicators: Destroyed {destroyedCount} indicators, cleared {glowsCleared} capturable glows, {nullCount} were already null");
    }
    
    /// <summary>
    /// Public method to clear move indicators (called by PlacementManager to avoid conflicts)
    /// </summary>
    public void ClearMoveIndicators()
    {
        Debug.Log("InputManager: Clearing move indicators on external request");
        HideValidMoveIndicators();
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
                Debug.Log($"✅ InputManager: Found working shader: {shaderName}");
                return shader;
            }
            else
            {
                Debug.LogWarning($"InputManager: Shader '{shaderName}' not found, trying next fallback...");
            }
        }

        Debug.LogError("❌ InputManager: No suitable shader found! Indicators will fail to render.");
        return null;
    }

    private void TryMovePiece(BoardPosition targetPosition, bool confirmed = false)
    {
        Debug.Log($"🚀 TryMovePiece: Called with targetPosition {targetPosition}");
        
        // During placement phase, allow piece movement for repositioning
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            Debug.Log($"TryMovePiece: Placement phase - allowing piece repositioning to {targetPosition}");
        }
        
        if (selectedPiece == null)
        {
            Debug.LogError("TryMovePiece: selectedPiece is NULL!");
            return;
        }
        
        // ANTI-CORRUPTION: Prevent moving pieces that are in trays
        if (selectedPiece.IsInTray())
        {
            Debug.LogError($"🚨 TryMovePiece: BLOCKED - Cannot move tray piece {selectedPiece.pieceColor} {selectedPiece.pieceType}");
            Debug.LogError($"  Piece parent: {(selectedPiece.transform.parent != null ? selectedPiece.transform.parent.name : "None")}");
            Debug.LogError($"  Tray pieces should be placed through PlacementManager, not moved directly");
            return;
        }
        
        if (isProcessingInput)
        {
            Debug.LogWarning("TryMovePiece: Already processing input, skipping move");
            return;
        }
        
        Debug.Log($"TryMovePiece: Selected piece = {selectedPiece.pieceColor} {selectedPiece.pieceType} at {selectedPiece.CurrentPosition}");
        Debug.Log($"TryMovePiece: Attempting move from {selectedPiece.CurrentPosition} to {targetPosition}");
        
        // During placement phase, enforce placement zone restrictions
        bool canMove = true;
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            if (!PlacementManager.IsPositionInColorZone(targetPosition, selectedPiece.pieceColor))
            {
                Debug.LogWarning($"TryMovePiece: Invalid placement zone - {selectedPiece.pieceColor} pieces must stay in X={PlacementManager.GetValidXForColor(selectedPiece.pieceColor)} layer");
                canMove = false;
            }
            else
            {
                Debug.Log($"TryMovePiece: Placement zone valid for {selectedPiece.pieceColor} piece");
            }
        }
        
        // Check piece-specific movement rules
        if (canMove)
        {
            canMove = selectedPiece.CanMoveTo(targetPosition);
        }
        Debug.Log($"TryMovePiece: CanMoveTo({targetPosition}) returned {canMove}");
        
        // Tap Twice to Move: the first tap on a legal square only marks it
        if (canMove && !confirmed && AccessibilityPrefs.TapTwiceToMove
            && GameStateManager.Instance != null && GameStateManager.Instance.CanMovePieces()
            && !(armedTarget.HasValue && armedTarget.Value == targetPosition))
        {
            ArmMove(targetPosition);
            return;
        }

        if (canMove)
        {
            DisarmMove();
            Debug.Log($"✅ TryMovePiece: Move is valid, starting ProcessMoveCoroutine");
            StartCoroutine(ProcessMoveCoroutine(targetPosition));
        }
        else
        {
            Debug.LogWarning($"❌ TryMovePiece: Invalid move to {targetPosition}");
            
            // Debug: Show what moves ARE valid for comparison
            var validMoves = selectedPiece.GetValidMoves();
            Debug.Log($"TryMovePiece: Valid moves for this piece: [{string.Join(", ", validMoves)}]");
            
            // Check if target position is in the valid moves list
            bool inList = validMoves.Contains(targetPosition);
            Debug.Log($"TryMovePiece: Target {targetPosition} in valid moves list: {inList}");
        }
    }
    
    private System.Collections.IEnumerator ProcessMoveCoroutine(BoardPosition targetPosition)
    {
        Debug.Log($"🎬 ProcessMoveCoroutine: Started for move to {targetPosition}");
        isProcessingInput = true;
        
        Debug.Log($"ProcessMoveCoroutine: Set isProcessingInput = true");
        
        // Store piece info before move (in case it gets destroyed during promotion)
        PieceColor movingColor = selectedPiece.pieceColor;
        ChessPieceType movingType = selectedPiece.pieceType;
        BoardPosition oldPosition = selectedPiece.CurrentPosition;
        
        // Check if this is a pawn promotion move
        Pawn pawn = selectedPiece as Pawn;
        bool isPawnPromotion = (pawn != null && pawn.IsPromotionMove(targetPosition));
        
        Debug.Log($"ProcessMoveCoroutine: Moving {movingColor} {movingType}, promotion={isPawnPromotion}");
        
        // Move the piece (ChessBoard.MovePiece will handle capture and promotion logic)
        if (ChessBoard.Instance != null)
        {
            ChessBoard.Instance.MovePiece(oldPosition, targetPosition);
        }
        
        Debug.Log($"Moved {movingColor} {movingType} from {oldPosition} to {targetPosition}");
        
        if (isPawnPromotion)
        {
            // For pawn promotion, don't wait for animation since piece may be destroyed
            Debug.Log($"ProcessMoveCoroutine: Pawn promotion detected, waiting for completion");
            
            // Wait for promotion to complete if PawnPromotionManager exists
            if (PawnPromotionManager.Instance != null)
            {
                Debug.Log($"ProcessMoveCoroutine: PawnPromotionManager found, waiting for promotion to complete");
                
                float promotionTimeout = 0f;
                bool promotionComplete = false;
                
                while (!promotionComplete)
                {
                    bool stillInProgress = PawnPromotionManager.Instance.IsPromotionInProgress();
                    
                    // Only log every 2 seconds to reduce spam
                    if (Time.frameCount % 120 == 0)
                    {
                        Debug.Log($"ProcessMoveCoroutine: Waiting for player to select promotion piece - elapsed: {promotionTimeout:F2}s");
                    }
                    
                    if (!stillInProgress)
                    {
                        promotionComplete = true;
                        Debug.Log("ProcessMoveCoroutine: Promotion completed successfully");
                        break;
                    }
                    
                    promotionTimeout += Time.deltaTime;
                    yield return null;
                }
                
                Debug.Log($"ProcessMoveCoroutine: Promotion completed in {promotionTimeout:F2} seconds");
            }
            else
            {
                Debug.LogWarning("ProcessMoveCoroutine: PawnPromotionManager not found, skipping promotion wait");
            }
        }
        else
        {
            // Normal move: Wait for move animation to complete
            float animationTimeout = 0f;
            while (selectedPiece != null && selectedPiece.IsMoving && animationTimeout < 10f)
            {
                animationTimeout += Time.deltaTime;
                yield return null;
            }
            
            if (animationTimeout >= 10f)
            {
                Debug.LogWarning("ProcessMoveCoroutine: Animation timeout reached!");
            }
        }
        
        // CRITICAL: Always reset input processing state
        DeselectPiece();
        
        Debug.Log($"ProcessMoveCoroutine: Setting isProcessingInput = false");
        isProcessingInput = false;
        Debug.Log($"ProcessMoveCoroutine: Input processing unlocked");
    }
    
    public void ClearSelection()
    {
        DeselectPiece();
    }
    
    /// <summary>
    /// Show visual feedback when a player tries to move out of turn
    /// </summary>
    private void ShowInvalidTurnFeedback(PieceColor attemptedPlayer, PieceColor currentPlayer)
    {
        Debug.LogWarning($"⚠️ INVALID TURN: {attemptedPlayer} attempted to move, but it's {currentPlayer}'s turn!");
        
        // TODO: Add visual/audio feedback here
        // For now, just log the violation - UI will be enhanced later
        
        // Could add:
        // - Screen flash or color overlay
        // - Sound effect
        // - Temporary message display
        // - Highlight current player's pieces
    }
    
    /// <summary>
    /// Fallback method to find a piece's actual position by searching the entire ChessBoard array
    /// Used when a piece has an invalid position but might still exist on the board
    /// </summary>
    /// <param name="piece">The piece to search for</param>
    /// <returns>Valid BoardPosition if found, invalid position if not found</returns>
    private BoardPosition FindPieceInBoardArray(ChessPiece piece)
    {
        if (piece == null || ChessBoard.Instance == null)
        {
            Debug.LogError("FindPieceInBoardArray: piece or ChessBoard.Instance is null");
            return new BoardPosition(-1, -1, -1);
        }
        
        Debug.Log($"🔍 FALLBACK SEARCH: Scanning entire board for {piece.pieceColor} {piece.pieceType}...");
        
        // Search the entire 4x4x4 board array
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int z = 0; z < 4; z++)
                {
                    BoardPosition searchPos = new BoardPosition(x, y, z);
                    ChessPiece foundPiece = ChessBoard.Instance.GetPieceAt(searchPos);
                    
                    if (foundPiece == piece)
                    {
                        Debug.Log($"🎯 FOUND IN FALLBACK: {piece.pieceColor} {piece.pieceType} located at {searchPos}");
                        return searchPos;
                    }
                }
            }
        }
        
        Debug.LogError($"❌ FALLBACK FAILED: {piece.pieceColor} {piece.pieceType} not found anywhere in board array");
        return new BoardPosition(-1, -1, -1); // Invalid position
    }
    
    /// <summary>
    /// Handle keyboard shortcuts for forfeit and draw actions
    /// </summary>
    private void HandleGameActionShortcuts()
    {
        // Skip if confirmation dialog is active
        if (ConfirmationDialog.Instance != null && ConfirmationDialog.Instance.IsDialogActive())
        {
            return;
        }
        
        // Only process shortcuts for human players
        if (TurnManager.Instance == null) return;
        
        PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
        bool isCurrentPlayerHuman = !TurnManager.Instance.IsPlayerAI(currentPlayer);
        
        if (!isCurrentPlayerHuman) return;
        
        // Forfeit shortcut (F key) - now integrated with GameMenuUI
        if (Input.GetKeyDown(KeyCode.F))
        {
            Debug.Log("InputManager: F key pressed - attempting forfeit");
            if (TurnManager.Instance.CanCurrentPlayerForfeit())
            {
                // Handle forfeit through TurnManager with confirmation
                PieceColor forfeitingPlayer = TurnManager.Instance.GetCurrentPlayer();
                
                if (ConfirmationDialog.Instance != null)
                {
                    ConfirmationDialog.Instance.ShowForfeitDialog(forfeitingPlayer, () => {
                        Debug.Log($"InputManager: Forfeit confirmed for {forfeitingPlayer} via F key");
                        TurnManager.Instance.RequestForfeit();
                    });
                }
                else
                {
                    Debug.LogWarning("InputManager: No confirmation dialog available, forfeiting directly");
                    TurnManager.Instance.RequestForfeit();
                }
            }
            else
            {
                Debug.LogWarning("InputManager: Cannot forfeit - conditions not met");
            }
        }
        
        // Draw offer shortcut (D key)
        else if (Input.GetKeyDown(KeyCode.D))
        {
            Debug.Log("InputManager: D key pressed - attempting draw offer");
            if (TurnManager.Instance.CanCurrentPlayerOfferDraw())
            {
                PieceColor offeringPlayer = TurnManager.Instance.GetCurrentPlayer();
                
                if (ConfirmationDialog.Instance != null)
                {
                    ConfirmationDialog.Instance.ShowDrawOfferDialog(offeringPlayer, () => {
                        Debug.Log($"InputManager: Draw offer confirmed for {offeringPlayer} via D key");
                        TurnManager.Instance.RequestDrawOffer();
                    });
                }
                else
                {
                    Debug.LogWarning("InputManager: No confirmation dialog available, offering draw directly");
                    TurnManager.Instance.RequestDrawOffer();
                }
            }
            else
            {
                Debug.LogWarning("InputManager: Cannot offer draw - conditions not met");
            }
        }
        
        // Draw accept shortcut (Y key)
        else if (Input.GetKeyDown(KeyCode.Y))
        {
            Debug.Log("InputManager: Y key pressed - attempting draw accept");
            if (TurnManager.Instance.CanCurrentPlayerRespondToDraw())
            {
                TurnManager.Instance.RespondToDrawOffer(true);
                Debug.Log("InputManager: Draw accepted via Y key");
            }
            else
            {
                Debug.LogWarning("InputManager: Cannot accept draw - conditions not met");
            }
        }
        
        // Draw decline shortcut (N key)
        else if (Input.GetKeyDown(KeyCode.N))
        {
            Debug.Log("InputManager: N key pressed - attempting draw decline");
            if (TurnManager.Instance.CanCurrentPlayerRespondToDraw())
            {
                TurnManager.Instance.RespondToDrawOffer(false);
                Debug.Log("InputManager: Draw declined via N key");
            }
            else
            {
                Debug.LogWarning("InputManager: Cannot decline draw - conditions not met");
            }
        }
    }
}