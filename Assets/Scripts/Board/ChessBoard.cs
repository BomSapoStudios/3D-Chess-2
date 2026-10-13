using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ChessBoard : MonoBehaviour
{
    [Header("Board Configuration")]
    public float cellSize = 2.8f;
    public Material lightSquareMaterial;
    public Material darkSquareMaterial;
    public Material highlightMaterial;
    
    [Header("Board Structure")]
    public GameObject boardCellPrefab;
    public Transform boardContainer;

    private GameObject[,,] boardCells;
    private ChessPiece[,,] pieces;
    private List<BoardPosition> highlightedPositions = new List<BoardPosition>();

    // Cached board dimensions for performance
    private Vector3Int boardDimensions;
    
    // Simulation mode tracking - prevents corruption checks during temporary move testing
    private bool _isSimulationMode = false;
    
    public static ChessBoard Instance { get; private set; }
    
    /// <summary>
    /// Enable simulation mode to skip corruption checks during temporary move testing
    /// </summary>
    public void EnableSimulationMode()
    {
        _isSimulationMode = true;
    }
    
    /// <summary>
    /// Disable simulation mode to re-enable corruption checks
    /// </summary>
    public void DisableSimulationMode()
    {
        _isSimulationMode = false;
    }
    
    /// <summary>
    /// Check if currently in simulation mode
    /// </summary>
    public bool IsSimulationMode()
    {
        return _isSimulationMode;
    }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // SINGLETON STABILITY: Persist through scene changes
            DontDestroyOnLoad(gameObject);
            Debug.Log("ChessBoard: Singleton created with DontDestroyOnLoad protection");
            InitializeBoard();
        }
        else if (Instance != this)
        {
            Debug.LogWarning("ChessBoard: Duplicate instance detected, destroying...");
            Destroy(gameObject);
        }
        else
        {
            // This is the existing singleton, ensure it's still protected
            DontDestroyOnLoad(gameObject);
            Debug.Log("ChessBoard: Existing singleton reaffirmed");
        }
    }
    
    private void InitializeBoard()
    {
        Debug.Log("ChessBoard: Starting board initialization...");

        // Get board dimensions from BoardDimensionsManager
        if (BoardDimensionsManager.Instance != null)
        {
            boardDimensions = BoardDimensionsManager.Instance.GetDimensions();
            Debug.Log($"ChessBoard: Using board dimensions {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z}");
        }
        else
        {
            // Fallback to default 4x4x4 if manager not available
            boardDimensions = new Vector3Int(4, 4, 4);
            Debug.LogWarning("ChessBoard: BoardDimensionsManager not available, using default 4x4x4");
        }

        if (boardContainer == null)
        {
            GameObject containerObject = new GameObject("Board Container");
            containerObject.transform.SetParent(transform);
            boardContainer = containerObject.transform;
            Debug.Log("ChessBoard: Created board container");
        }

        CreateDefaultMaterials();
        // Skip visual board creation - using EmergencyChessBoard for visuals
        // CreateBoardStructure();
        InitializeLogicalBoard();

        // Clear any existing pieces from the board during initialization
        ClearAllPieces();

        // Clean up any orphaned King GameObjects from the scene
        CleanUpOrphanedPieces();

        Debug.Log("ChessBoard: Board initialization complete!");
    }
    
    private void CreateBoardStructure()
    {
        Debug.Log($"ChessBoard: Creating {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z} board structure...");

        int cellCount = 0;
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    CreateBoardCell(new BoardPosition(x, y, z));
                    cellCount++;
                }
            }
        }

        Debug.Log($"ChessBoard: Created {cellCount} board cells");
        CenterBoard();
        Debug.Log("ChessBoard: Board centered and positioned");
    }
    
    private void InitializeLogicalBoard()
    {
        Debug.Log("ChessBoard: Initializing logical board structure (no visuals)...");

        // Initialize arrays based on board dimensions
        boardCells = new GameObject[boardDimensions.x, boardDimensions.y, boardDimensions.z];
        pieces = new ChessPiece[boardDimensions.x, boardDimensions.y, boardDimensions.z];

        // Position board container to match EmergencyChessBoard coordinate system
        float halfSize = (boardDimensions.x - 1) * 0.5f;
        Vector3 centerOffset = new Vector3(-halfSize * cellSize, -halfSize * cellSize, -halfSize * cellSize);
        boardContainer.localPosition = centerOffset;

        Debug.Log("ChessBoard: Logical board initialized with matching coordinate system");
    }
    
    private void CreateBoardCell(BoardPosition position)
    {
        GameObject cell;
        
        if (boardCellPrefab != null)
        {
            cell = Instantiate(boardCellPrefab, boardContainer);
        }
        else
        {
            cell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cell.transform.SetParent(boardContainer);
            
            MeshRenderer renderer = cell.GetComponent<MeshRenderer>();
            renderer.material = GetCellMaterial(position);
            
            BoxCollider collider = cell.GetComponent<BoxCollider>();
            collider.isTrigger = true;
        }
        
        cell.name = $"Cell_{position}";
        cell.transform.localPosition = position.ToWorldPosition(cellSize);
        cell.transform.localScale = new Vector3(cellSize * 0.5f, cellSize * 0.1f, cellSize * 0.5f);
        
        Debug.Log($"ChessBoard: Created cell at {position} with world position {cell.transform.localPosition}");
        
        BoardCell cellComponent = cell.GetComponent<BoardCell>();
        if (cellComponent == null)
        {
            cellComponent = cell.AddComponent<BoardCell>();
        }
        cellComponent.Initialize(position);
        
        boardCells[position.x, position.y, position.z] = cell;
    }
    
    private Material GetCellMaterial(BoardPosition position)
    {
        bool isLight = (position.x + position.y + position.z) % 2 == 0;
        return isLight ? lightSquareMaterial : darkSquareMaterial;
    }
    
    private void CreateDefaultMaterials()
    {
        Debug.Log("ChessBoard: Creating default materials...");
        
        if (lightSquareMaterial == null)
        {
            lightSquareMaterial = CreateMaterial(Color.white, 0.8f);
            Debug.Log("ChessBoard: Created light square material");
        }
        
        if (darkSquareMaterial == null)
        {
            darkSquareMaterial = CreateMaterial(new Color(0.15f, 0.15f, 0.15f), 0.8f);
            Debug.Log("ChessBoard: Created dark square material");
        }
        
        if (highlightMaterial == null)
        {
            highlightMaterial = CreateMaterial(Color.yellow, 0.9f);
            Debug.Log("ChessBoard: Created highlight material");
        }
    }
    
    private Material CreateMaterial(Color color, float alpha)
    {
        // Try URP Lit shader first, fallback to Standard if not available
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
            Debug.LogWarning("ChessBoard: URP Lit shader not found, using Standard shader");
        }
        
        Material material = new Material(shader);
        color.a = alpha;
        material.color = color;
        
        // Set up transparency for both URP and Standard shaders
        if (alpha < 1.0f)
        {
            // Try URP transparency settings
            try 
            {
                material.SetFloat("_Surface", 1); // Transparent
                material.SetFloat("_Blend", 0); // Alpha
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            catch
            {
                // Fallback for Standard shader
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }
        }
        
        Debug.Log($"ChessBoard: Created material with color {color} using shader {shader.name}");
        return material;
    }
    
    private void CenterBoard()
    {
        float halfSize = (boardDimensions.x - 1) * 0.5f;
        Vector3 centerOffset = new Vector3(-halfSize * cellSize, -halfSize * cellSize, -halfSize * cellSize);
        boardContainer.localPosition = centerOffset;
    }
    
    public bool IsValidPosition(BoardPosition position)
    {
        return position.IsValid();
    }
    
    public ChessPiece GetPieceAt(BoardPosition position)
    {
        if (!IsValidPosition(position)) 
        {
            Debug.LogError($"GetPieceAt: Invalid position {position}");
            return null;
        }
        
        ChessPiece piece = pieces[position.x, position.y, position.z];
        
        // CRITICAL FIX: Skip sync repair during simulation mode to prevent infinite loops
        // ANTI-CORRUPTION: Also skip sync repair for tray pieces to prevent corruption
        if (!_isSimulationMode && piece != null && piece.CurrentPosition != position)
        {
            // IMPORTANT: Check if this is a tray piece before attempting repair
            if (piece.IsInTray())
            {
                Debug.LogWarning($"🚨 TRAY PIECE CORRUPTION DETECTED: board[{position}] contains tray piece {piece.pieceColor} {piece.pieceType}");
                Debug.LogWarning($"  Piece parent: {(piece.transform.parent != null ? piece.transform.parent.name : "None")}");
                Debug.LogWarning($"  Clearing board position to prevent corruption");
                pieces[position.x, position.y, position.z] = null;
                return null; // This position should be empty
            }
            
            Debug.LogWarning($"⚠️ Position sync issue detected: board[{position}] has piece that thinks it's at {piece.CurrentPosition}");
            
            // Try to find where this piece actually belongs
            BoardPosition actualPosition = piece.CurrentPosition;
            if (actualPosition.IsValid())
            {
                // Check if the piece's claimed position matches reality
                ChessPiece pieceAtClaimedPosition = pieces[actualPosition.x, actualPosition.y, actualPosition.z];
                
                if (pieceAtClaimedPosition == piece)
                {
                    // Piece is correctly registered at its claimed position, clear the wrong slot
                    Debug.Log($"🔧 SYNC REPAIR: Clearing incorrect entry at board[{position}]");
                    pieces[position.x, position.y, position.z] = null;
                    return null; // This position is actually empty
                }
                else
                {
                    // Piece's claimed position doesn't match - update piece to match board array
                    Debug.Log($"🔧 SYNC REPAIR: Updating piece position from {piece.CurrentPosition} to {position}");
                    piece.SetCurrentPosition(position);
                }
            }
            else
            {
                // Piece has invalid position, update it to match board array
                Debug.Log($"🔧 SYNC REPAIR: Setting piece position to {position} (was invalid)");
                piece.SetCurrentPosition(position);
            }
        }
        
        return piece;
    }
    
    public bool SetPieceAt(BoardPosition position, ChessPiece piece)
    {
        if (!IsValidPosition(position)) 
        {
            Debug.LogError($"SetPieceAt: Invalid position {position}");
            return false;
        }
        
        // ANTI-CORRUPTION: Only validate during non-simulation operations
        // Skip duplicate checking during temporary move simulations to avoid interfering with normal gameplay
        if (piece != null && !_isSimulationMode)
        {
            // CRITICAL: Prevent tray pieces from being registered on the board (except during legitimate placement)
            // Check if this is still in a tray AND has a valid position (corruption case)
            if (piece.IsInTray() && piece.CurrentPosition.IsValid() && piece.CurrentPosition != position)
            {
                Debug.LogError($"🚨 BLOCKED: Attempted to register corrupted tray piece {piece.pieceColor} {piece.pieceType} at board position {position}");
                Debug.LogError($"  Piece parent: {(piece.transform.parent != null ? piece.transform.parent.name : "None")}");
                Debug.LogError($"  Piece current position: {piece.CurrentPosition}");
                Debug.LogError($"  This indicates position corruption - tray pieces should have invalid positions");
                return false;
            }
            // Check for duplicate piece attempts (especially Kings/Queens) only during actual piece placement
            if (piece.pieceType == ChessPieceType.King || piece.pieceType == ChessPieceType.Queen)
            {
                // Check if this piece is already registered somewhere else
                bool foundDuplicate = false;
                for (int x = 0; x < boardDimensions.x && !foundDuplicate; x++)
                {
                    for (int y = 0; y < boardDimensions.y && !foundDuplicate; y++)
                    {
                        for (int z = 0; z < boardDimensions.z && !foundDuplicate; z++)
                        {
                            BoardPosition checkPos = new BoardPosition(x, y, z);
                            if (checkPos != position && pieces[x, y, z] == piece)
                            {
                                Debug.LogWarning($"🚨 DUPLICATE PIECE REGISTRATION BLOCKED: {piece.pieceColor} {piece.pieceType} already at {checkPos}, cannot register at {position}");
                                foundDuplicate = true;
                            }
                        }
                    }
                }
                
                if (foundDuplicate)
                {
                    Debug.LogError($"🚨 SetPieceAt: BLOCKED duplicate registration of {piece.pieceColor} {piece.pieceType}");
                    return false;
                }
            }
            
            // Clear any old position this piece was registered at
            BoardPosition oldPosition = piece.CurrentPosition;
            if (oldPosition.IsValid() && oldPosition != position)
            {
                ChessPiece oldPiece = pieces[oldPosition.x, oldPosition.y, oldPosition.z];
                if (oldPiece == piece)
                {
                    Debug.Log($"🔧 SetPieceAt: Clearing old position {oldPosition} for piece moving to {position}");
                    pieces[oldPosition.x, oldPosition.y, oldPosition.z] = null;
                }
            }
            
            // Update piece's internal position to match board array
            piece.SetCurrentPosition(position);
            
            // Position piece using local coordinates relative to rotating board
            Vector3 localPiecePosition = BoardToLocalPosition(position);
            piece.transform.localPosition = localPiecePosition;
            
            Debug.Log($"🔧 SetPieceAt: Synchronized {piece.pieceColor} {piece.pieceType} at board[{position}] with piece.CurrentPosition={piece.CurrentPosition}");
        }
        
        // Check if target position already has a different piece (only warn during normal operations)
        ChessPiece existingPiece = pieces[position.x, position.y, position.z];
        if (existingPiece != null && existingPiece != piece && !_isSimulationMode)
        {
            Debug.LogWarning($"🚨 SetPieceAt: Replacing {existingPiece.pieceColor} {existingPiece.pieceType} at {position} with {(piece != null ? $"{piece.pieceColor} {piece.pieceType}" : "NULL")}");
        }
        
        pieces[position.x, position.y, position.z] = piece;
        
        return true;
    }
    
    /// <summary>
    /// Safely remove a piece from the board and destroy its GameObject
    /// </summary>
    public void RemovePieceFromBoard(ChessPiece piece, BoardPosition position)
    {
        if (piece == null)
        {
            Debug.LogWarning("RemovePieceFromBoard: Attempted to remove null piece");
            return;
        }
        
        Debug.Log($"🗑️ RemovePieceFromBoard: Removing {piece.pieceColor} {piece.pieceType} from {position}");
        
        // Clear the board array immediately
        if (IsValidPosition(position))
        {
            ChessPiece boardPiece = pieces[position.x, position.y, position.z];
            if (boardPiece == piece)
            {
                pieces[position.x, position.y, position.z] = null;
                Debug.Log($"🗑️ RemovePieceFromBoard: Cleared board array at {position}");
            }
            else
            {
                Debug.LogWarning($"⚠️ RemovePieceFromBoard: Board array mismatch - expected {piece.pieceColor} {piece.pieceType} at {position} but found {(boardPiece != null ? $"{boardPiece.pieceColor} {boardPiece.pieceType}" : "NULL")}");
            }
        }
        
        // Mark piece as captured to prevent it from trying to update its position
        piece.SetCurrentPosition(new BoardPosition(-1, -1, -1)); // Invalid position
        
        // Destroy the GameObject
        Destroy(piece.gameObject);
        
        Debug.Log($"✅ RemovePieceFromBoard: Successfully removed {piece.pieceColor} {piece.pieceType}");
    }
    
    /// <summary>
    /// Validate board consistency - check that all pieces on the board have correct positions
    /// </summary>
    public void ValidateBoardConsistency()
    {
        Debug.Log("🔍 ChessBoard.ValidateBoardConsistency: Starting board validation...");

        int inconsistencies = 0;

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition boardPos = new BoardPosition(x, y, z);
                    ChessPiece piece = pieces[x, y, z];

                    if (piece != null)
                    {
                        // Check if piece thinks it's at this position
                        if (piece.CurrentPosition != boardPos)
                        {
                            Debug.LogError($"🚨 BOARD INCONSISTENCY: {piece.pieceColor} {piece.pieceType} at board[{boardPos}] thinks it's at {piece.CurrentPosition}");
                            inconsistencies++;
                        }
                    }
                }
            }
        }
        
        if (inconsistencies == 0)
        {
            Debug.Log("✅ ChessBoard.ValidateBoardConsistency: Board is consistent");
        }
        else
        {
            Debug.LogError($"🚨 ChessBoard.ValidateBoardConsistency: Found {inconsistencies} inconsistencies!");
        }
    }
    
    
    public bool MovePiece(BoardPosition from, BoardPosition to)
    {
        Debug.Log($"🚨 ChessBoard.MovePiece: ENTRY - Moving piece from {from} to {to}");
        
        if (!IsValidPosition(from) || !IsValidPosition(to)) 
        {
            Debug.LogError($"ChessBoard.MovePiece: Invalid positions - from {from} valid: {IsValidPosition(from)}, to {to} valid: {IsValidPosition(to)}");
            return false;
        }
        
        ChessPiece piece = GetPieceAt(from);
        if (piece == null) 
        {
            Debug.LogError($"ChessBoard.MovePiece: No piece found at {from}");
            
            // RECOVERY MECHANISM: Try to find the piece anywhere on the board
            ChessPiece foundPiece = null;
            BoardPosition actualPosition = new BoardPosition(-1, -1, -1);

            for (int x = 0; x < boardDimensions.x && foundPiece == null; x++)
            {
                for (int y = 0; y < boardDimensions.y && foundPiece == null; y++)
                {
                    for (int z = 0; z < boardDimensions.z && foundPiece == null; z++)
                    {
                        ChessPiece candidatePiece = pieces[x, y, z];
                        if (candidatePiece != null && candidatePiece.CurrentPosition == from)
                        {
                            foundPiece = candidatePiece;
                            actualPosition = new BoardPosition(x, y, z);
                        }
                    }
                }
            }
            
            if (foundPiece != null)
            {
                // Fix the board array to match piece's expectation
                pieces[actualPosition.x, actualPosition.y, actualPosition.z] = null;
                pieces[from.x, from.y, from.z] = foundPiece;
                piece = foundPiece;
            }
            else
            {
                return false;
            }
        }
        
        // PRE-MOVE CHECK VALIDATION: Ensure move doesn't leave king in check
        if (CheckDetectionManager.Instance != null && 
            GameStateManager.Instance != null && 
            GameStateManager.Instance.CanMovePieces())
        {
            if (CheckDetectionManager.Instance.WouldMoveLeaveKingInCheck(piece, from, to))
            {
                Debug.LogWarning($"ChessBoard.MovePiece: Move from {from} to {to} would leave {piece.pieceColor} king in check - move blocked");
                return false;
            }
        }
        
        // Check for capture before updating board array
        ChessPiece targetPiece = GetPieceAt(to);
        
        if (targetPiece != null)
        {
            if (targetPiece.pieceColor != piece.pieceColor)
            {
                Debug.Log($"🎯 CAPTURE: {piece.pieceColor} {piece.pieceType} captures {targetPiece.pieceColor} {targetPiece.pieceType}");
                
                // The captured piece shatters when the attacker reaches it (a copy; the real one goes now)
                if (!_isSimulationMode) PieceEffects.Shatter(targetPiece, piece);

                // CRITICAL FIX: Clean removal of captured piece
                RemovePieceFromBoard(targetPiece, to);
            }
            else
            {
                Debug.LogError($"ChessBoard.MovePiece: Cannot move to {to} - occupied by same color piece ({targetPiece.pieceColor} {targetPiece.pieceType})");
                return false;
            }
        }
        
        // PAWN PROMOTION: Check if this is a pawn promotion before completing the move
        Pawn pawn = piece as Pawn;
        bool isPromotion = false;
        
        Debug.Log($"ChessBoard.MovePiece: Checking for pawn promotion - piece type: {piece.pieceType}");
        
        if (pawn != null)
        {
            Debug.Log($"ChessBoard.MovePiece: Piece is a pawn, checking IsPromotionMove({to})");
            bool promotionMove = pawn.IsPromotionMove(to);
            Debug.Log($"ChessBoard.MovePiece: IsPromotionMove result: {promotionMove}");
            
            if (promotionMove)
            {
                Debug.Log($"🚨 ChessBoard.MovePiece: PAWN PROMOTION DETECTED for {piece.pieceColor} pawn from {from} to {to}");
                
                // Handle promotion through PawnPromotionManager
                if (PawnPromotionManager.Instance != null)
                {
                    Debug.Log($"ChessBoard.MovePiece: PawnPromotionManager.Instance found, calling HandlePotentialPromotion");
                    isPromotion = PawnPromotionManager.Instance.HandlePotentialPromotion(pawn, from, to);
                    Debug.Log($"ChessBoard.MovePiece: PawnPromotionManager.HandlePotentialPromotion returned: {isPromotion}");
                }
                else
                {
                    Debug.LogError($"ChessBoard.MovePiece: PawnPromotionManager.Instance is NULL! Cannot handle promotion.");
                    Debug.LogError($"ChessBoard.MovePiece: Falling back to automatic Queen promotion");
                    
                    // FALLBACK: Automatic Queen promotion when PawnPromotionManager is missing
                    isPromotion = true;
                    StartCoroutine(HandleFallbackPromotion(pawn, from, to, ChessPieceType.Queen));
                }
            }
            else
            {
                Debug.Log($"ChessBoard.MovePiece: Not a promotion move");
            }
        }
        else
        {
            Debug.Log($"ChessBoard.MovePiece: Piece is not a pawn, no promotion check needed");
        }
        
        bool moveResult = true;
        
        if (isPromotion)
        {
            // For promotion moves, the PawnPromotionManager handles piece placement
            // Don't update the board array here as it will be handled by the promotion system
            Debug.Log($"ChessBoard.MovePiece: Promotion in progress, deferring board update");
        }
        else
        {
            // Normal move: Update the board array and move the piece
            Debug.Log($"ChessBoard.MovePiece: Updating board array - clearing {from}, setting {to}");
            
            // VALIDATION: Ensure piece position matches board before move
            if (piece.CurrentPosition != from)
            {
                Debug.LogError($"🚨 CRITICAL: Piece position desync detected! Piece {piece.pieceColor} {piece.pieceType} thinks it's at {piece.CurrentPosition} but board move is from {from}");
                // Force synchronization before move
                piece.SetCurrentPosition(from);
            }
            
            pieces[from.x, from.y, from.z] = null;
            pieces[to.x, to.y, to.z] = piece;
            
            // Update piece position BEFORE MoveTo to ensure sync
            Debug.Log($"ChessBoard.MovePiece: Updating piece position from {piece.CurrentPosition} to {to}");
            moveResult = piece.MoveTo(to);
            Debug.Log($"ChessBoard.MovePiece: MoveTo completed, piece position is now {piece.CurrentPosition}");
            
            // VALIDATION: Ensure piece position matches board after move
            if (piece.CurrentPosition != to)
            {
                Debug.LogError($"🚨 CRITICAL: Piece position desync after move! Piece {piece.pieceColor} {piece.pieceType} should be at {to} but thinks it's at {piece.CurrentPosition}");
                piece.SetCurrentPosition(to);
            }
            
            // VALIDATION: Double-check board array consistency
            ChessPiece verifyPiece = pieces[to.x, to.y, to.z];
            if (verifyPiece != piece)
            {
                Debug.LogError($"🚨 CRITICAL: Board array corruption! Expected {piece.pieceColor} {piece.pieceType} at {to} but found {(verifyPiece != null ? $"{verifyPiece.pieceColor} {verifyPiece.pieceType}" : "NULL")}");
                pieces[to.x, to.y, to.z] = piece; // Force correction
            }
        }
        
        // Online: tell the server about our own moves (it re-checks them)
        if (moveResult && OnlineSession.ShouldSend(piece.pieceColor))
        {
            OnlineClient.Instance.SendMove(piece.pieceColor, from, to);
        }

        // VALIDATION: Check board consistency after move
        ValidateBoardConsistency();
        
        // POST-MOVE CHECK DETECTION: Evaluate check state after move completion
        if (CheckDetectionManager.Instance != null && 
            GameStateManager.Instance != null && 
            GameStateManager.Instance.CanMovePieces())
        {
            // Invalidate cache and safely update visual feedback
            CheckDetectionManager.Instance.InvalidateCache();
            CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
        }
        
        // TURN SYSTEM: Switch turns after successful move (only during Playing phase)
        // Skip turn switching for promotion moves as PawnPromotionManager handles it
        // CRITICAL FIX: Don't check CanMovePieces() here as it checks for animations
        // The piece might already be animating, which would block NextTurn() from being called
        // NextTurn() has its own guard conditions to ensure we're in the right state
        if (!isPromotion && !ZombieMode.HoldTurn && GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.Playing)
        {
            if (TurnManager.Instance != null)
            {
                PieceColor movingPlayer = piece.pieceColor;
                TurnManager.Instance.NextTurn();
                Debug.Log($"🔄 Turn switched after {movingPlayer} move");
            }
            else
            {
                Debug.LogWarning("ChessBoard.MovePiece: TurnManager not available for turn switching");
            }
        }

        // CHESS NOTATION UI: Notify UI systems about the move
        if (!isPromotion)
        {
            bool wasCapture = (targetPiece != null);
            bool causedCheck = CheckDetectionManager.Instance != null && CheckDetectionManager.Instance.IsAnyKingInCheck();
            bool causedCheckmate = false; // Will be updated by GameEndDetectionManager events

            // Update status bar with last move
            if (PersistentCheckStatusUI.Instance != null)
            {
                PersistentCheckStatusUI.Instance.SetLastMove(piece, from, to, wasCapture);
            }

            // Record move in move history
            if (MoveHistoryUI.Instance != null)
            {
                MoveHistoryUI.Instance.RecordMove(piece, from, to, wasCapture, causedCheck, causedCheckmate);
            }
        }

        return true;
    }
    
    public void HighlightPosition(BoardPosition position)
    {
        if (!IsValidPosition(position)) return;
        
        GameObject cell = boardCells[position.x, position.y, position.z];
        if (cell != null)
        {
            MeshRenderer renderer = cell.GetComponent<MeshRenderer>();
            renderer.material = highlightMaterial;
            highlightedPositions.Add(position);
        }
    }
    
    public void ClearHighlights()
    {
        foreach (BoardPosition position in highlightedPositions)
        {
            GameObject cell = boardCells[position.x, position.y, position.z];
            if (cell != null)
            {
                MeshRenderer renderer = cell.GetComponent<MeshRenderer>();
                renderer.material = GetCellMaterial(position);
            }
        }
        highlightedPositions.Clear();
    }
    
    public BoardPosition WorldToBoard(Vector3 worldPosition)
    {
        // Convert world position back to board position using EmergencyChessBoard coordinate system
        return new BoardPosition(
            Mathf.RoundToInt((worldPosition.x + 4.2f) / cellSize),
            Mathf.RoundToInt((worldPosition.y + 5.6f) / cellSize),
            Mathf.RoundToInt((worldPosition.z + 4.2f) / cellSize)
        );
    }
    
    public Vector3 BoardToWorld(BoardPosition boardPosition)
    {
        // Match EmergencyChessBoard coordinate system exactly
        // Floor planes are at: (x * 2.8f - 4.2f, y * 2.8f - 5.6f, z * 2.8f - 4.2f)
        return new Vector3(
            boardPosition.x * cellSize - 4.2f,
            boardPosition.y * cellSize - 5.6f,  // Floor plane Y position
            boardPosition.z * cellSize - 4.2f
        );
    }
    
    public Vector3 BoardToLocalPosition(BoardPosition boardPosition)
    {
        // Convert board position to local coordinates within the Pieces Container
        // The container is already centered at (-4.2, -4.2, -4.2) to match the board
        // Floor planes are at: (x * 2.8, y * 2.8 - 1.4, z * 2.8) relative to board parent
        // Position piece GameObject at floor level; piece visual has +1.05 offset to rest on floor
        return new Vector3(
            boardPosition.x * cellSize,                    // X: match floor plane X
            (boardPosition.y * cellSize) - 1.4f,          // Y: exactly at floor plane level
            boardPosition.z * cellSize                     // Z: match floor plane Z
        );
    }
    
    public List<BoardPosition> GetAllPositions()
    {
        List<BoardPosition> positions = new List<BoardPosition>();
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    positions.Add(new BoardPosition(x, y, z));
                }
            }
        }
        return positions;
    }
    
    /// <summary>
    /// Clear all pieces from the board
    /// </summary>
    public void ClearAllPieces()
    {
        Debug.Log("ChessBoard: Clearing all pieces from board...");

        int clearedCount = 0;
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    if (pieces[x, y, z] != null)
                    {
                        ChessPiece piece = pieces[x, y, z];
                        Debug.Log($"ChessBoard: Destroying orphaned piece {piece.pieceColor} {piece.pieceType} at ({x},{y},{z})");
                        pieces[x, y, z] = null;
                        if (piece.gameObject != null)
                        {
                            DestroyImmediate(piece.gameObject);
                        }
                        clearedCount++;
                    }
                }
            }
        }

        Debug.Log($"ChessBoard: Cleared {clearedCount} orphaned pieces from board");
    }
    
    /// <summary>
    /// Clean up any orphaned King GameObjects that might be in the scene
    /// </summary>
    public void CleanUpOrphanedPieces()
    {
        Debug.Log("ChessBoard: Searching for orphaned King GameObjects in scene...");
        
        // Find all King components in the scene
        King[] allKings = FindObjectsByType<King>(FindObjectsSortMode.None);
        
        int destroyedCount = 0;
        foreach (King king in allKings)
        {
            // Check if this King is uninitialized (orphaned)
            if (!king.CurrentPosition.IsValid())
            {
                Debug.Log($"ChessBoard: Found orphaned King {king.pieceColor} {king.pieceType} at {king.transform.position}");
                Debug.Log($"  Parent: {(king.transform.parent != null ? king.transform.parent.name : "None")}");
                Debug.Log($"  GameObject: {king.gameObject.name}");
                
                // Check if it's not in a tray (pieces in trays are valid)
                bool inTray = king.transform.IsChildOf(PieceTray.WhiteTray?.transform) || 
                             king.transform.IsChildOf(PieceTray.BlackTray?.transform);
                
                if (!inTray)
                {
                    Debug.Log($"ChessBoard: Destroying orphaned King GameObject '{king.gameObject.name}'");
                    DestroyImmediate(king.gameObject);
                    destroyedCount++;
                }
                else
                {
                    Debug.Log($"ChessBoard: King is in tray, keeping it");
                }
            }
            else
            {
                Debug.Log($"ChessBoard: King {king.pieceColor} {king.pieceType} is properly initialized at {king.CurrentPosition}");
            }
        }
        
        Debug.Log($"ChessBoard: Destroyed {destroyedCount} orphaned King GameObjects");
    }
    
    public List<BoardPosition> GetEmptyPositions()
    {
        List<BoardPosition> emptyPositions = new List<BoardPosition>();
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    if (GetPieceAt(pos) == null)
                    {
                        emptyPositions.Add(pos);
                    }
                }
            }
        }
        return emptyPositions;
    }
    
    /// <summary>
    /// Check if a piece can be placed at the specified position during placement phase.
    /// This method enforces strict placement rules: pieces can ONLY be placed on empty squares.
    /// </summary>
    /// <param name="position">The board position to check</param>
    /// <returns>True if the position is valid and empty, false otherwise</returns>
    public bool CanPlacePieceAt(BoardPosition position)
    {
        // Validate position bounds
        if (!IsValidPosition(position))
        {
            Debug.Log($"ChessBoard.CanPlacePieceAt: Position {position} is out of bounds");
            return false;
        }
        
        // Check if position is empty (this is the ONLY requirement for basic placement)
        ChessPiece existingPiece = GetPieceAt(position);
        bool isEmpty = existingPiece == null;
        
        if (!isEmpty)
        {
            Debug.Log($"ChessBoard.CanPlacePieceAt: Position {position} is occupied by {existingPiece.pieceColor} {existingPiece.pieceType} - placement blocked");
        }
        else
        {
            Debug.Log($"ChessBoard.CanPlacePieceAt: Position {position} is empty - placement allowed");
        }
        
        return isEmpty;
    }
    
    /// <summary>
    /// Check if a piece can be placed at the specified position during placement phase with zone validation.
    /// This method enforces placement zone rules with Y/Z restrictions on larger boards:
    /// - 4x4x4: White X=0, Black X=3 (full YZ plane)
    /// - 6x6x6: Players 1&2 in top 3 Y-rows (Y=3-5), Players 3&4 in bottom 3 Y-rows (Y=0-2), full Z
    /// - 8x8x8: All players in middle 4x4 grid (Y=2-5, Z=2-5)
    /// </summary>
    /// <param name="position">The board position to check</param>
    /// <param name="pieceColor">The color of the piece being placed</param>
    /// <returns>True if the position is valid, empty, and in the correct placement zone for the piece color</returns>
    public bool CanPlacePieceAt(BoardPosition position, PieceColor pieceColor)
    {
        // First check basic placement rules (bounds and emptiness)
        if (!CanPlacePieceAt(position))
        {
            return false; // Already logged in base method
        }

        // Check placement zone restrictions during placement phase
        if (GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.PiecePlacement)
        {
            if (!PlacementManager.IsPositionInColorZone(position, pieceColor))
            {
                PlacementZone zone = PlacementManager.GetPlacementZoneForColor(pieceColor);
                Debug.Log($"ChessBoard.CanPlacePieceAt: {pieceColor} pieces must be placed in zone X=[{zone.xMin}-{zone.xMax}], Y=[{zone.yMin}-{zone.yMax}], Z=[{zone.zMin}-{zone.zMax}], but position {position} is outside zone - placement blocked");
                return false;
            }
        }

        Debug.Log($"ChessBoard.CanPlacePieceAt: Position {position} is valid for {pieceColor} piece placement");
        return true;
    }
    
    // DEBUG METHOD: Dump complete board state for debugging registration issues
    public void DebugDumpBoardState(string context = "")
    {
        Debug.Log($"🗺️ === BOARD STATE DUMP {context} ===");
        
        int totalPieces = 0;
        int nullSlots = 0;
        int syncIssues = 0;

        for (int y = boardDimensions.y - 1; y >= 0; y--) // Top to bottom
        {
            for (int x = 0; x < boardDimensions.x; x++)
            {
                string line = $"Y={y}: ";
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = pieces[x, y, z];
                    if (piece != null)
                    {
                        string colorCode = piece.pieceColor == PieceColor.White ? "W" : "B";
                        string typeCode = piece.pieceType.ToString().Substring(0, 1);
                        line += $"[{colorCode}{typeCode}({x},{y},{z})] ";
                        totalPieces++;

                        // Check if piece's currentPosition matches array position
                        if (piece.CurrentPosition != new BoardPosition(x, y, z))
                        {
                            Debug.LogError($"🚨 DESYNC: Piece at array[{x},{y},{z}] thinks it's at {piece.CurrentPosition}");
                            syncIssues++;
                        }
                    }
                    else
                    {
                        line += "[ -- ] ";
                        nullSlots++;
                    }
                }
                Debug.Log(line);
            }
        }
        
        Debug.Log($"📊 SUMMARY: {totalPieces} pieces found, {nullSlots} empty slots, {syncIssues} sync issues");
        
        if (syncIssues > 0)
        {
            Debug.LogError($"🚨 CRITICAL: {syncIssues} position synchronization issues detected! Running repair...");
            RepairBoardSynchronization();
        }
        
        Debug.Log($"🗺️ === END BOARD STATE DUMP ===");
    }
    
    /// <summary>
    /// Comprehensive repair system for position synchronization issues
    /// </summary>
    public void RepairBoardSynchronization()
    {
        Debug.Log("🔧 === STARTING BOARD SYNCHRONIZATION REPAIR ===");

        int repairedPieces = 0;
        int clearedSlots = 0;

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition boardPos = new BoardPosition(x, y, z);
                    ChessPiece piece = pieces[x, y, z];

                    if (piece != null)
                    {
                        if (piece.CurrentPosition != boardPos)
                        {
                            Debug.Log($"🔧 REPAIR: Fixing piece at board[{boardPos}] (thinks it's at {piece.CurrentPosition})");

                            // Check if piece's claimed position conflicts with another piece
                            BoardPosition claimedPos = piece.CurrentPosition;
                            if (claimedPos.IsValid())
                            {
                                ChessPiece conflictPiece = pieces[claimedPos.x, claimedPos.y, claimedPos.z];
                                if (conflictPiece != null && conflictPiece != piece)
                                {
                                    Debug.Log($"🔧 REPAIR: Conflict detected - updating piece position to match board array");
                                    piece.SetCurrentPosition(boardPos);
                                    repairedPieces++;
                                }
                                else if (conflictPiece == piece)
                                {
                                    Debug.Log($"🔧 REPAIR: Clearing duplicate entry at board[{boardPos}]");
                                    pieces[x, y, z] = null;
                                    clearedSlots++;
                                }
                                else
                                {
                                    // Piece's claimed position is empty - should move it there
                                    Debug.Log($"🔧 REPAIR: Moving piece from board[{boardPos}] to correct position {claimedPos}");
                                    pieces[x, y, z] = null;
                                    pieces[claimedPos.x, claimedPos.y, claimedPos.z] = piece;
                                    repairedPieces++;
                                }
                            }
                            else
                            {
                                // Piece has invalid position - fix it
                                Debug.Log($"🔧 REPAIR: Setting invalid piece position to {boardPos}");
                                piece.SetCurrentPosition(boardPos);
                                repairedPieces++;
                            }
                        }
                    }
                }
            }
        }
        
        Debug.Log($"🔧 === REPAIR COMPLETE: {repairedPieces} pieces repaired, {clearedSlots} slots cleared ===");
    }
    
    /// <summary>
    /// Diagnostic method to help troubleshoot specific Black Rook issues
    /// </summary>
    public void DiagnoseBlackRooks()
    {
        Debug.Log("🔍 === BLACK ROOK DIAGNOSTIC ===" );
        
        // Find all Rook pieces in the scene
        Rook[] allRooks = FindObjectsByType<Rook>(FindObjectsSortMode.None);
        
        int blackRookCount = 0;
        int validBlackRooks = 0;
        int invalidBlackRooks = 0;
        
        foreach (Rook rook in allRooks)
        {
            if (rook.pieceColor == PieceColor.Black)
            {
                blackRookCount++;
                Debug.Log($"🏰 Black Rook #{blackRookCount}:");
                Debug.Log($"   CurrentPosition: {rook.CurrentPosition}");
                Debug.Log($"   GameObject: {rook.gameObject.name}");
                Debug.Log($"   World Position: {rook.transform.position}");
                Debug.Log($"   Parent: {(rook.transform.parent != null ? rook.transform.parent.name : "None")}");
                
                // Check if position is valid
                if (rook.CurrentPosition.IsValid())
                {
                    validBlackRooks++;
                    
                    // Check if board array contains this rook at its claimed position
                    ChessPiece boardPiece = GetPieceAt(rook.CurrentPosition);
                    if (boardPiece == rook)
                    {
                        Debug.Log($"   ✅ Position sync: GOOD - board array matches piece position");
                        
                        // Test legal moves
                        var legalMoves = rook.GetLegalMoves();
                        Debug.Log($"   Legal moves: {legalMoves.Count} found");
                        if (legalMoves.Count > 0)
                        {
                            int sampleCount = Mathf.Min(3, legalMoves.Count);
                            var sampleMoves = legalMoves.Take(sampleCount);
                            Debug.Log($"   Sample moves: {string.Join(", ", sampleMoves)}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"   🚨 Position sync: BROKEN - board[{rook.CurrentPosition}] contains {(boardPiece != null ? $"{boardPiece.pieceColor} {boardPiece.pieceType}" : "NULL")}");
                        invalidBlackRooks++;
                    }
                }
                else
                {
                    Debug.LogError($"   🚨 INVALID POSITION: {rook.CurrentPosition}");
                    invalidBlackRooks++;

                    // Try to find where this rook is in the board array
                    for (int x = 0; x < boardDimensions.x; x++)
                    {
                        for (int y = 0; y < boardDimensions.y; y++)
                        {
                            for (int z = 0; z < boardDimensions.z; z++)
                            {
                                ChessPiece foundPiece = pieces[x, y, z];
                                if (foundPiece == rook)
                                {
                                    Debug.Log($"   🎯 FOUND in board array at [{x},{y},{z}]");
                                }
                            }
                        }
                    }
                }
                
                Debug.Log(""); // Blank line for readability
            }
        }
        
        Debug.Log($"📊 SUMMARY: {blackRookCount} Black Rooks found");
        Debug.Log($"   Valid: {validBlackRooks}");
        Debug.Log($"   Invalid: {invalidBlackRooks}");
        Debug.Log("🔍 === END BLACK ROOK DIAGNOSTIC ===");
    }
    
    /// <summary>
    /// Fallback method to handle pawn promotion when PawnPromotionManager is missing
    /// </summary>
    private System.Collections.IEnumerator HandleFallbackPromotion(Pawn pawn, BoardPosition from, BoardPosition to, ChessPieceType promotionType)
    {
        Debug.Log($"ChessBoard.HandleFallbackPromotion: Starting fallback {promotionType} promotion for {pawn.pieceColor} pawn");
        
        // Wait a frame to ensure move processing is complete
        yield return null;
        
        // Store pawn info before destruction
        PieceColor pawnColor = pawn.pieceColor;
        
        // Move the pawn to the target position first
        SetPieceAt(from, null);
        SetPieceAt(to, pawn);
        pawn.MoveTo(to);
        
        // Wait for pawn movement to complete
        while (pawn.IsMoving)
        {
            yield return null;
        }
        
        // Create the promoted piece
        GameManager gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            GameObject prefab = null;
            
            switch (promotionType)
            {
                case ChessPieceType.Queen:
                    prefab = gameManager.queenPiecePrefab;
                    break;
                case ChessPieceType.Rook:
                    prefab = gameManager.rookPiecePrefab;
                    break;
                case ChessPieceType.Bishop:
                    prefab = gameManager.bishopPiecePrefab;
                    break;
                case ChessPieceType.Knight:
                    prefab = gameManager.knightPiecePrefab;
                    break;
            }
            
            if (prefab != null)
            {
                // Create the new piece
                GameObject newPieceObject = Instantiate(prefab);
                newPieceObject.name = $"{pawnColor} {promotionType} (Fallback Promoted)";
                
                // Parent to pieces container
                GameObject piecesContainer = GameObject.Find("Pieces Container");
                if (piecesContainer != null)
                {
                    newPieceObject.transform.SetParent(piecesContainer.transform);
                }
                
                // Initialize the new piece
                ChessPiece newPiece = newPieceObject.GetComponent<ChessPiece>();
                if (newPiece != null)
                {
                    newPiece.Initialize(to, pawnColor);
                    
                    // Update board state
                    SetPieceAt(to, newPiece);
                    
                    Debug.Log($"ChessBoard.HandleFallbackPromotion: Successfully created {pawnColor} {promotionType} at {to}");
                }
                
                // Destroy the old pawn
                Destroy(pawn.gameObject);
            }
            else
            {
                Debug.LogError($"ChessBoard.HandleFallbackPromotion: No prefab found for {promotionType}");
            }
        }
        else
        {
            Debug.LogError("ChessBoard.HandleFallbackPromotion: GameManager not found!");
        }
        
        // Switch turns after promotion
        if (TurnManager.Instance != null && GameStateManager.Instance != null && GameStateManager.Instance.CanMovePieces())
        {
            TurnManager.Instance.NextTurn();
            Debug.Log($"🔄 Turn switched after {pawnColor} fallback promotion");
        }
    }
    
    /// <summary>
    /// Comprehensive method to detect and remove duplicate pieces that cause board state corruption
    /// </summary>
    public void DetectAndRemoveDuplicatePieces()
    {
        Debug.Log("🧹 === STARTING DUPLICATE PIECE DETECTION AND CLEANUP ===");
        
        // Dictionary to track legitimate pieces by type and color
        Dictionary<string, ChessPiece> legitimatePieces = new Dictionary<string, ChessPiece>();
        List<ChessPiece> duplicatePieces = new List<ChessPiece>();
        
        // Scan all pieces in the scene
        ChessPiece[] allPieces = FindObjectsByType<ChessPiece>(FindObjectsSortMode.None);
        
        Debug.Log($"🧹 Found {allPieces.Length} total ChessPiece objects in scene");
        
        foreach (ChessPiece piece in allPieces)
        {
            if (piece == null) continue;
            
            // Create unique key for piece type and color
            string pieceKey = $"{piece.pieceColor}_{piece.pieceType}";
            
            // For pieces that should be unique (King, Queen), only keep one
            if (piece.pieceType == ChessPieceType.King || piece.pieceType == ChessPieceType.Queen)
            {
                if (legitimatePieces.ContainsKey(pieceKey))
                {
                    // This is a duplicate
                    Debug.LogWarning($"🧹 DUPLICATE DETECTED: {piece.pieceColor} {piece.pieceType} at {piece.transform.position}");
                    Debug.LogWarning($"   Current position: {piece.CurrentPosition}");
                    Debug.LogWarning($"   Parent: {(piece.transform.parent != null ? piece.transform.parent.name : "None")}");
                    
                    duplicatePieces.Add(piece);
                }
                else
                {
                    // This is the legitimate piece
                    legitimatePieces[pieceKey] = piece;
                    Debug.Log($"🧹 LEGITIMATE: {piece.pieceColor} {piece.pieceType} at {piece.CurrentPosition}");
                }
            }
            else
            {
                // For other pieces (Rook, Bishop, Knight, Pawn), check if they're in valid positions
                // or if they're duplicates in the same position
                bool isInTray = piece.transform.IsChildOf(PieceTray.WhiteTray?.transform) || 
                               piece.transform.IsChildOf(PieceTray.BlackTray?.transform);
                               
                if (!isInTray && !piece.CurrentPosition.IsValid())
                {
                    Debug.LogWarning($"🧹 ORPHANED PIECE: {piece.pieceColor} {piece.pieceType} with invalid position");
                    duplicatePieces.Add(piece);
                }
            }
        }
        
        // Remove duplicate pieces
        int removedCount = 0;
        foreach (ChessPiece duplicate in duplicatePieces)
        {
            if (duplicate != null && duplicate.gameObject != null)
            {
                Debug.Log($"🧹 REMOVING: {duplicate.pieceColor} {duplicate.pieceType} GameObject '{duplicate.gameObject.name}'");
                
                // Clear from board array if it's registered there
                if (duplicate.CurrentPosition.IsValid())
                {
                    BoardPosition pos = duplicate.CurrentPosition;
                    if (pieces[pos.x, pos.y, pos.z] == duplicate)
                    {
                        pieces[pos.x, pos.y, pos.z] = null;
                        Debug.Log($"🧹   Cleared from board array at {pos}");
                    }
                }
                
                DestroyImmediate(duplicate.gameObject);
                removedCount++;
            }
        }
        
        // Validate board consistency after cleanup
        int positionRepairs = 0;
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = pieces[x, y, z];
                    if (piece != null)
                    {
                        BoardPosition expectedPos = new BoardPosition(x, y, z);
                        if (piece.CurrentPosition != expectedPos)
                        {
                            Debug.LogWarning($"🧹 POSITION REPAIR: Fixing {piece.pieceColor} {piece.pieceType} position sync");
                            piece.SetCurrentPosition(expectedPos);
                            positionRepairs++;
                        }
                    }
                }
            }
        }
        
        Debug.Log($"🧹 === CLEANUP COMPLETE ===");
        Debug.Log($"   Removed {removedCount} duplicate/orphaned pieces");
        Debug.Log($"   Repaired {positionRepairs} position synchronization issues");
        Debug.Log($"   Kept {legitimatePieces.Count} legitimate unique pieces");
    }
    
    // ===== CHAOS MODE ROTATION METHODS =====
    
    /// <summary>
    /// Apply a chaos rotation to the board by moving pieces according to rotation mapping
    /// </summary>
    /// <param name="rotationMap">Dictionary mapping old positions to new positions</param>
    /// <param name="animationDuration">How long the animation should take</param>
    /// <returns>Coroutine for animation</returns>
    public System.Collections.IEnumerator ApplyChaosRotation(Dictionary<BoardPosition, BoardPosition> rotationMap, float animationDuration = 2.0f)
    {
        Debug.Log($"🌪️ ChessBoard.ApplyChaosRotation: Starting rotation with {rotationMap.Count} piece movements");
        
        // Validate the rotation mapping
        if (!ChaosMath.ValidateRotationMap(rotationMap))
        {
            Debug.LogError("ChessBoard.ApplyChaosRotation: Invalid rotation mapping, aborting");
            yield break;
        }
        
        // Store pieces that need to move
        Dictionary<BoardPosition, ChessPiece> movingPieces = new Dictionary<BoardPosition, ChessPiece>();
        
        // Collect all pieces that will be affected
        foreach (var kvp in rotationMap)
        {
            BoardPosition oldPos = kvp.Key;
            ChessPiece piece = GetPieceAt(oldPos);
            
            if (piece != null)
            {
                movingPieces[oldPos] = piece;
                Debug.Log($"🌪️ Will move {piece.pieceColor} {piece.pieceType} from {oldPos} to {kvp.Value}");
            }
        }
        
        // Clear old positions first (to prevent conflicts)
        foreach (var kvp in movingPieces)
        {
            BoardPosition oldPos = kvp.Key;
            pieces[oldPos.x, oldPos.y, oldPos.z] = null;
        }
        
        // Start all piece animations simultaneously
        List<Coroutine> animations = new List<Coroutine>();
        
        foreach (var kvp in rotationMap)
        {
            BoardPosition oldPos = kvp.Key;
            BoardPosition newPos = kvp.Value;
            
            if (movingPieces.ContainsKey(oldPos))
            {
                ChessPiece piece = movingPieces[oldPos];
                
                // Set new position in board array
                pieces[newPos.x, newPos.y, newPos.z] = piece;
                
                // Start piece animation
                Coroutine animation = StartCoroutine(AnimateChaosMove(piece, oldPos, newPos, animationDuration));
                animations.Add(animation);
            }
        }
        
        // Wait for all animations to complete
        foreach (Coroutine animation in animations)
        {
            yield return animation;
        }
        
        // Validate board state after rotation
        ValidateBoardConsistency();

        // Note: Check detection is now handled by ChaosRotationManager after ALL rotations complete
        // (not here, since this is called multiple times for multi-spin chaos events)

        Debug.Log("🌪️ ChessBoard.ApplyChaosRotation: Rotation completed successfully");
    }
    
    /// <summary>
    /// Animate a single piece during chaos rotation
    /// </summary>
    private System.Collections.IEnumerator AnimateChaosMove(ChessPiece piece, BoardPosition oldPos, BoardPosition newPos, float duration)
    {
        if (piece == null) yield break;
        
        // Validate positions before starting animation
        if (!newPos.IsValid())
        {
            Debug.LogError($"🚨 AnimateChaosMove: Invalid target position {newPos} for {piece.pieceColor} {piece.pieceType}. Skipping animation.");
            yield break;
        }
        
        Debug.Log($"🎬 Animating {piece.pieceColor} {piece.pieceType} from {oldPos} to {newPos}");
        
        // Get LOCAL positions for animation (relative to pieces container)
        Vector3 startLocalPos = BoardToLocalPosition(oldPos);
        Vector3 endLocalPos = BoardToLocalPosition(newPos);
        Vector3 originalPos = piece.transform.localPosition;
        
        // Update piece's internal position first
        piece.SetCurrentPosition(newPos);

        // Calculate bounds once for use throughout animation
        float maxBound = cellSize * (boardDimensions.x - 1);
        float minY = -1.4f;
        float maxY = ((boardDimensions.y - 1) * cellSize - 1.4f) + 1f; // +1f for animation arc

        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;

            // Interpolate between start and end local positions
            Vector3 currentLocalPos = Vector3.Lerp(startLocalPos, endLocalPos, t);

            // Add visual flair - slight upward arc during movement
            currentLocalPos.y += Mathf.Sin(t * Mathf.PI) * 0.5f;

            // Bounds checking - ensure position stays within board bounds
            currentLocalPos.x = Mathf.Clamp(currentLocalPos.x, 0f, maxBound);
            currentLocalPos.z = Mathf.Clamp(currentLocalPos.z, 0f, maxBound);
            currentLocalPos.y = Mathf.Clamp(currentLocalPos.y, minY, maxY);

            // Apply LOCAL position to piece (relative to board container)
            piece.transform.localPosition = currentLocalPos;

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Ensure final position is exact using local coordinates
        piece.transform.localPosition = endLocalPos;

        // Validate final position and recover if needed
        Vector3 finalPos = piece.transform.localPosition;
        bool needsRecovery = finalPos.x < 0 || finalPos.x > maxBound ||
                           finalPos.z < 0 || finalPos.z > maxBound ||
                           finalPos.y < -2.0f || finalPos.y > ((boardDimensions.y - 1) * cellSize + 1.0f);
        
        if (needsRecovery)
        {
            Debug.LogWarning($"🚨 Animation recovery needed for {piece.pieceColor} {piece.pieceType}. Resetting to target position.");
            piece.transform.localPosition = endLocalPos; // Reset to calculated position
        }
        
        Debug.Log($"🎬 Animation complete: {piece.pieceColor} {piece.pieceType} now at {newPos}");
    }
    
    /// <summary>
    /// Apply a face rotation to the board
    /// </summary>
    /// <param name="face">Which face to rotate</param>
    /// <param name="clockwise">Direction of rotation</param>
    /// <param name="animationDuration">Animation duration</param>
    /// <returns>Coroutine for animation</returns>
    public System.Collections.IEnumerator ApplyFaceRotation(CubeFace face, bool clockwise, float animationDuration = 2.0f)
    {
        Debug.Log($"🌪️ ChessBoard.ApplyFaceRotation: Rotating {face} face {(clockwise ? "clockwise" : "counter-clockwise")} on {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z} board");

        // Get positions on this face
        List<BoardPosition> facePositions = GetFacePositions(face);

        // Calculate rotation mapping with board dimensions for multi-board support
        Dictionary<BoardPosition, BoardPosition> rotationMap = ChaosMath.RotateFacePositions(facePositions, face, clockwise, boardDimensions);

        // Apply the rotation
        yield return StartCoroutine(ApplyChaosRotation(rotationMap, animationDuration));
    }
    
    /// <summary>
    /// Apply a layer rotation to the board
    /// </summary>
    /// <param name="axis">Rotation axis</param>
    /// <param name="layer">Which layer (0-3)</param>
    /// <param name="clockwise">Direction of rotation</param>
    /// <param name="animationDuration">Animation duration</param>
    /// <returns>Coroutine for animation</returns>
    public System.Collections.IEnumerator ApplyLayerRotation(RotationAxis axis, int layer, bool clockwise, float animationDuration = 2.0f)
    {
        Debug.Log($"🌪️ ChessBoard.ApplyLayerRotation: Rotating {axis}-axis layer {layer} {(clockwise ? "clockwise" : "counter-clockwise")} on {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z} board");

        // Get positions on this layer
        List<BoardPosition> layerPositions = GetLayerPositions(axis, layer);

        // Calculate rotation mapping with board dimensions for multi-board support
        Dictionary<BoardPosition, BoardPosition> rotationMap = ChaosMath.RotateLayerPositions(layerPositions, axis, layer, clockwise, boardDimensions);

        // Apply the rotation
        yield return StartCoroutine(ApplyChaosRotation(rotationMap, animationDuration));
    }
    
    /// <summary>
    /// Get all positions on a cube face
    /// </summary>
    private List<BoardPosition> GetFacePositions(CubeFace face)
    {
        List<BoardPosition> positions = new List<BoardPosition>();
        int maxCoord = boardDimensions.x - 1; // Assuming cubic board

        switch (face)
        {
            case CubeFace.Front: // Z = 0
                for (int x = 0; x < boardDimensions.x; x++)
                    for (int y = 0; y < boardDimensions.y; y++)
                        positions.Add(new BoardPosition(x, y, 0));
                break;

            case CubeFace.Back: // Z = max
                for (int x = 0; x < boardDimensions.x; x++)
                    for (int y = 0; y < boardDimensions.y; y++)
                        positions.Add(new BoardPosition(x, y, maxCoord));
                break;

            case CubeFace.Left: // X = 0
                for (int y = 0; y < boardDimensions.y; y++)
                    for (int z = 0; z < boardDimensions.z; z++)
                        positions.Add(new BoardPosition(0, y, z));
                break;

            case CubeFace.Right: // X = max
                for (int y = 0; y < boardDimensions.y; y++)
                    for (int z = 0; z < boardDimensions.z; z++)
                        positions.Add(new BoardPosition(maxCoord, y, z));
                break;

            case CubeFace.Top: // Y = max
                for (int x = 0; x < boardDimensions.x; x++)
                    for (int z = 0; z < boardDimensions.z; z++)
                        positions.Add(new BoardPosition(x, maxCoord, z));
                break;

            case CubeFace.Bottom: // Y = 0
                for (int x = 0; x < boardDimensions.x; x++)
                    for (int z = 0; z < boardDimensions.z; z++)
                        positions.Add(new BoardPosition(x, 0, z));
                break;
        }

        return positions;
    }
    
    /// <summary>
    /// Get all positions in a layer/slice
    /// </summary>
    private List<BoardPosition> GetLayerPositions(RotationAxis axis, int layer)
    {
        List<BoardPosition> positions = new List<BoardPosition>();

        switch (axis)
        {
            case RotationAxis.X: // X = layer
                for (int y = 0; y < boardDimensions.y; y++)
                    for (int z = 0; z < boardDimensions.z; z++)
                        positions.Add(new BoardPosition(layer, y, z));
                break;

            case RotationAxis.Y: // Y = layer
                for (int x = 0; x < boardDimensions.x; x++)
                    for (int z = 0; z < boardDimensions.z; z++)
                        positions.Add(new BoardPosition(x, layer, z));
                break;

            case RotationAxis.Z: // Z = layer
                for (int x = 0; x < boardDimensions.x; x++)
                    for (int y = 0; y < boardDimensions.y; y++)
                        positions.Add(new BoardPosition(x, y, layer));
                break;
        }

        return positions;
    }
    
    /// <summary>
    /// Test method to trigger a random chaos rotation
    /// </summary>
    [ContextMenu("Test Chaos Rotation")]
    public void TestChaosRotation()
    {
        if (ChaosRotationManager.Instance != null)
        {
            Debug.Log("🎲 Testing chaos rotation from ChessBoard");
            ChaosRotationManager.Instance.ForceChaosRotation();
        }
        else
        {
            Debug.LogWarning("ChaosRotationManager not available for testing");
        }
    }
}