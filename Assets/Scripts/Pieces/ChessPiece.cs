using System.Collections.Generic;
using UnityEngine;

public enum PieceColor
{
    White,
    Black,
    Green,    // 4-player (6x6x6 or 8x8x8 boards)
    Purple,   // 4-player (6x6x6 or 8x8x8 boards)
    Yellow,   // 6-player (8x8x8 board only)
    Orange    // 6-player (8x8x8 board only)
}

public enum PlayerType
{
    Human,
    Computer
}

public enum ChessPieceType
{
    Pawn,
    Rook,
    Knight,
    Bishop,
    Queen,
    King
}

public enum AnimationContext
{
    UserMove,       // Human player moving piece - slowest, most visible
    AIMove,         // AI moving piece - moderate speed, deliberate
    Placement,      // Initial piece placement - faster
    Instant         // No animation - immediate movement
}

public abstract class ChessPiece : MonoBehaviour
{
    [Header("Piece Properties")]
    public PieceColor pieceColor;
    public ChessPieceType pieceType;
    
    [Header("Visual Settings")]
    public Material whiteMaterial;
    public Material blackMaterial;
    
    [Header("Animation Settings")]
    public float userMoveSpeed = 1.33f;     // 0.75 second duration - clearly visible for human moves (33% slower)
    public float aiMoveSpeed = 1.0f;        // 1.0 second duration - deliberate AI moves (33% slower)
    public float placementSpeed = 2.0f;     // 0.5 second duration - faster for placement (33% slower)
    public AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f); // Smooth easing curve
    public float liftHeight = 0.5f;         // How high to lift pieces during movement
    public bool useLiftAnimation = true;    // Whether to lift pieces during movement
    
    // Animation completion event for sequential move management
    public static event System.Action<ChessPiece> OnMoveAnimationComplete;
    
    protected BoardPosition currentPosition = new BoardPosition(-1, -1, -1); // Invalid position by default
    protected bool hasMoved = false;
    protected MeshRenderer meshRenderer;
    protected bool isMoving = false;
    
    public BoardPosition CurrentPosition => currentPosition;
    public bool HasMoved => hasMoved;
    public bool IsMoving => isMoving;
    
    protected virtual void Awake()
    {
        // Swap the placeholder shapes for the modeled piece, if there is one
        bool modeled = PieceModels.Swap(this);
        meshRenderer = modeled ? PieceModels.Body(this) : GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = GetComponentInChildren<MeshRenderer>();
        }
        
        ApplyMaterial();
    }
    
    protected virtual void Start()
    {
        // All pieces should only be registered through explicit Initialize() calls
        if (!currentPosition.IsValid())
        {
            Debug.LogWarning($"ChessPiece: Uninitialized {pieceColor} {pieceType} detected");
        }
    }
    
    public virtual void Initialize(BoardPosition position, PieceColor color)
    {
        // ANTI-CORRUPTION: Allow re-initialization for pawn spawning and repositioning, but prevent other duplicate initialization
        if (currentPosition.IsValid() && !IsInTray())
        {
            // Allow repositioning by checking if this is a position change
            if (currentPosition == position)
            {
                Debug.LogWarning($"ChessPiece.Initialize: {color} {pieceType} already initialized at {currentPosition}, ignoring re-initialization to same position");
                return;
            }
            else
            {
                Debug.Log($"ChessPiece.Initialize: Allowing repositioning of {color} {pieceType} from {currentPosition} to {position}");
            }
        }
        
        Debug.Log($"ChessPiece.Initialize: Initializing {color} {pieceType} at {position}");
        
        // SPECIAL CASE: Allow tray pieces to be initialized on board (this is legitimate placement)
        bool wasInTray = IsInTray();
        if (wasInTray)
        {
            Debug.Log($"ChessPiece.Initialize: Transitioning tray piece to board position {position}");
        }
        
        currentPosition = position;
        pieceColor = color;
        hasMoved = false;
        ApplyMaterial();
        
        if (ChessBoard.Instance != null)
        {
            Vector3 localPos = ChessBoard.Instance.BoardToLocalPosition(position);
            transform.localPosition = localPos;
            
            bool registrationResult = ChessBoard.Instance.SetPieceAt(position, this);
            if (!registrationResult)
            {
                Debug.LogError($"ChessPiece.Initialize: Failed to register {color} {pieceType} at {position}");
                // Clear the position if registration failed to prevent desync
                currentPosition = new BoardPosition(-1, -1, -1);
            }
            else
            {
                Debug.Log($"🔧 SetPieceAt: Synchronized {color} {pieceType} at board[{position}] with piece.CurrentPosition={currentPosition}");
                Debug.Log($"ChessPiece.Initialize: Successfully registered {color} {pieceType} at {position}");
            }
        }
        else
        {
            Debug.LogError($"ChessPiece.Initialize: ChessBoard.Instance is NULL!");
            // Clear the position if board is not available
            currentPosition = new BoardPosition(-1, -1, -1);
        }
    }
    
    public virtual void ApplyMaterial()
    {
        if (meshRenderer == null) return;

        // Inspector-assigned materials win only for the Classic finish
        Material targetMaterial = null;
        if (PieceSets.Effective == PieceSets.Kind.Classic)
        {
            if (pieceColor == PieceColor.White) targetMaterial = whiteMaterial;
            else if (pieceColor == PieceColor.Black) targetMaterial = blackMaterial;
        }
        bool modeled = transform.Find(PieceModels.ModelName) != null;
        if (targetMaterial == null)
        {
            targetMaterial = modeled ? PieceSets.CreateBodyMaterial(pieceColor) : PieceSets.CreateMaterial(pieceColor);
        }
        // The textured sets: the side's colour in the chosen finish, with the texture only shading
        // in detail (eyes, stripes, spots)
        if (modeled && PieceShapes.Textured(PieceShapes.Active))
        {
            targetMaterial = PieceSets.CreateMaterial(pieceColor); // glowing all over: these models have no light strips
            Texture2D shade = PieceModels.FinishDetail(PieceModels.TypeOf(this));
            if (shade != null)
            {
                targetMaterial.mainTexture = shade;
                if (targetMaterial.HasProperty("_EmissionMap") && targetMaterial.IsKeywordEnabled("_EMISSION")) targetMaterial.SetTexture("_EmissionMap", shade);
            }
        }
        Material stripMaterial = modeled ? PieceSets.CreateStripMaterial(pieceColor) : null;
        PieceModels.Face(this);

        // Every part of the piece (knights and rooks are made of two blocks), but not
        // the selection or check indicators added later. Model light strips glow in the team color.
        foreach (MeshRenderer part in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (part.name.Contains("Glow") || part.name.Contains("Indicator")) continue;
            part.sharedMaterial = part.name == PieceModels.StripName ? stripMaterial : targetMaterial;
        }
        meshRenderer.material = targetMaterial;
    }
    
    protected Material CreateDefaultMaterial()
    {
        // Try URP Lit shader first, fallback to Standard if not available
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
            Debug.LogWarning("ChessPiece: URP Lit shader not found, using Standard shader");
        }

        // If no shader found, use default Diffuse material
        if (shader == null)
        {
            Debug.LogError("ChessPiece: No valid shader found, using Diffuse shader");
            shader = Shader.Find("Diffuse");
        }

        Material material = new Material(shader);

        // Set color and material properties based on piece color
        switch (pieceColor)
        {
            case PieceColor.White:
                // Bright white with slight off-white tint for better visibility
                material.color = new Color(0.95f, 0.95f, 0.95f, 1f);
                if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
                {
                    material.SetFloat("_Metallic", 0.1f);      // Slight metallic look
                    material.SetFloat("_Smoothness", 0.6f);    // Semi-glossy finish
                }
                break;

            case PieceColor.Black:
                // Dark gray instead of pure black for much better visibility
                material.color = new Color(0.25f, 0.25f, 0.25f, 1f);
                if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
                {
                    material.SetFloat("_Metallic", 0.2f);      // Slightly more metallic for distinction
                    material.SetFloat("_Smoothness", 0.4f);    // Less glossy than white for contrast
                }
                break;

            case PieceColor.Green:
                // Darker forest green for better contrast
                material.color = new Color(0.15f, 0.5f, 0.15f, 1f);
                if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
                {
                    material.SetFloat("_Metallic", 0.15f);
                    material.SetFloat("_Smoothness", 0.5f);
                }
                break;

            case PieceColor.Purple:
                // Darker purple for better contrast
                material.color = new Color(0.45f, 0.15f, 0.6f, 1f);
                if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
                {
                    material.SetFloat("_Metallic", 0.15f);
                    material.SetFloat("_Smoothness", 0.5f);
                }
                break;

            case PieceColor.Yellow:
                // Bright yellow for visibility
                material.color = new Color(0.9f, 0.9f, 0.2f, 1f);
                if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
                {
                    material.SetFloat("_Metallic", 0.1f);
                    material.SetFloat("_Smoothness", 0.5f);
                }
                break;

            case PieceColor.Orange:
                // Bright orange for visibility
                material.color = new Color(1.0f, 0.5f, 0.0f, 1f);
                if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
                {
                    material.SetFloat("_Metallic", 0.15f);
                    material.SetFloat("_Smoothness", 0.5f);
                }
                break;

            default:
                // Fallback to gray
                material.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                if (shader.name.Contains("Lit") || shader.name.Contains("Standard"))
                {
                    material.SetFloat("_Metallic", 0.15f);
                    material.SetFloat("_Smoothness", 0.5f);
                }
                break;
        }

        Debug.Log($"ChessPiece: Created default material for {pieceColor} piece using shader {shader.name}");

        return material;
    }
    
    public abstract List<BoardPosition> GetValidMoves();
    
    /// <summary>
    /// Get squares that this piece can attack. For most pieces, this is the same as GetValidMoves(),
    /// but some pieces (like pawns) have different attack patterns vs movement patterns.
    /// This method is used by the check detection system.
    /// </summary>
    /// <returns>List of squares this piece can attack</returns>
    public virtual List<BoardPosition> GetAttackSquares()
    {
        // Default implementation: attack squares = valid moves
        // Override in specific piece classes if attack pattern differs from movement
        return GetValidMoves();
    }
    
    /// <summary>
    /// Get valid moves that don't leave the king in check. This is the main method
    /// that should be called for legal move validation during gameplay.
    /// </summary>
    /// <returns>List of legal moves that don't leave king in check</returns>
    public List<BoardPosition> GetLegalMoves()
    {
        List<BoardPosition> validMoves = GetValidMoves();
        
        // During gameplay, filter out moves that would leave king in check
        if (CheckDetectionManager.Instance != null && 
            GameStateManager.Instance != null && 
            GameStateManager.Instance.CanMovePieces())
        {
            List<BoardPosition> legalMoves = new List<BoardPosition>();
            
            foreach (BoardPosition move in validMoves)
            {
                // Check if move would leave king in check
                bool wouldLeaveInCheck = CheckDetectionManager.Instance.WouldMoveLeaveKingInCheck(this, currentPosition, move);
                
                if (wouldLeaveInCheck)
                {
                    continue; // Skip this move
                }
                
                // Opening move protection: prevent king captures on first turn
                if (TurnManager.Instance != null && TurnManager.Instance.ShouldBlockKingCapture())
                {
                    ChessPiece targetPiece = ChessBoard.Instance?.GetPieceAt(move);
                    if (targetPiece != null && targetPiece.pieceType == ChessPieceType.King)
                    {
                        continue; // Skip king captures on first turn
                    }
                }
                
                legalMoves.Add(move);
            }
            
            return legalMoves;
        }
        
        // During placement phase or when check detection is disabled, return all valid moves
        return validMoves;
    }
    
    protected virtual bool IsValidMove(BoardPosition targetPosition)
    {
        if (!targetPosition.IsValid()) return false;
        if (targetPosition == currentPosition) return false;
        
        ChessPiece targetPiece = ChessBoard.Instance?.GetPieceAt(targetPosition);
        if (targetPiece != null && targetPiece.pieceColor == pieceColor) return false;
        
        return true;
    }
    
    protected List<BoardPosition> GetValidMovesInDirection(Vector3Int direction, int maxDistance = int.MaxValue)
    {
        List<BoardPosition> validMoves = new List<BoardPosition>();

        // Never further than the board's longest side (was a fixed 4, which cut moves short on 6³ and 8³ boards)
        Vector3Int dims = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        maxDistance = Mathf.Min(maxDistance, Mathf.Max(dims.x, Mathf.Max(dims.y, dims.z)));
        
        for (int distance = 1; distance <= maxDistance; distance++)
        {
            BoardPosition newPosition = new BoardPosition(
                currentPosition.x + direction.x * distance,
                currentPosition.y + direction.y * distance,
                currentPosition.z + direction.z * distance
            );
            
            if (!newPosition.IsValid()) break;
            
            ChessPiece pieceAtPosition = ChessBoard.Instance?.GetPieceAt(newPosition);
            
            if (pieceAtPosition == null)
            {
                validMoves.Add(newPosition);
            }
            else
            {
                if (pieceAtPosition.pieceColor != pieceColor)
                {
                    validMoves.Add(newPosition);
                }
                break;
            }
        }
        
        return validMoves;
    }
    
    public virtual bool CanMoveTo(BoardPosition targetPosition)
    {
        List<BoardPosition> legalMoves = GetLegalMoves();
        return legalMoves.Contains(targetPosition);
    }
    
    public virtual bool MoveTo(BoardPosition newPosition)
    {
        if (isMoving)
        {
            return false;
        }
        
        // CRITICAL FIX: Ensure position sync with board array
        Debug.Log($"🔧 ChessPiece.MoveTo: {pieceColor} {pieceType} moving from {currentPosition} to {newPosition}");
        currentPosition = newPosition;
        hasMoved = true;
        
        // Validate that board array is consistent with this move
        if (ChessBoard.Instance != null)
        {
            ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(newPosition);
            if (boardPiece != this)
            {
                Debug.LogWarning($"⚠️ ChessPiece.MoveTo: Position sync warning - board[{newPosition}] doesn't contain this piece");
            }
        }
        
        // Determine animation context and start appropriate movement animation
        AnimationContext context = OnlineSession.Replaying ? AnimationContext.Instant : DetermineAnimationContext();
        StartCoroutine(MoveCoroutine(newPosition, context));
        return true;
    }
    
    /// <summary>
    /// Determine the appropriate animation context based on current game state
    /// </summary>
    private AnimationContext DetermineAnimationContext()
    {
        // Check if this is during placement phase
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            return AnimationContext.Placement;
        }
        
        // Check if current player is AI
        if (TurnManager.Instance != null && GameStateManager.Instance != null)
        {
            PieceColor currentPlayer = GameStateManager.Instance.GetCurrentPlayer();
            if (TurnManager.Instance.IsPlayerAI(currentPlayer) && currentPlayer == pieceColor)
            {
                return AnimationContext.AIMove;
            }
        }
        
        // Default to user move for human players
        return AnimationContext.UserMove;
    }
    
    /// <summary>
    /// Get the appropriate movement speed based on animation context
    /// </summary>
    private float GetSpeedForContext(AnimationContext context)
    {
        switch (context)
        {
            case AnimationContext.UserMove:
                return userMoveSpeed;
            case AnimationContext.AIMove:
                return aiMoveSpeed;
            case AnimationContext.Placement:
                return placementSpeed;
            case AnimationContext.Instant:
                return float.MaxValue; // Instant movement
            default:
                return userMoveSpeed;
        }
    }
    
    protected System.Collections.IEnumerator MoveCoroutine(BoardPosition targetPosition, AnimationContext context = AnimationContext.UserMove)
    {
        isMoving = true;
        Vector3 startPos = transform.localPosition;
        Vector3 endPos = ChessBoard.Instance.BoardToLocalPosition(targetPosition);
        
        float speed = GetSpeedForContext(context);
        float duration = 1f / speed;
        
        // Log animation details for debugging
        Debug.Log($"🎬 Animation: {pieceColor} {pieceType} {context} - Duration: {duration:F2}s (Speed: {speed:F1})");
        
        if (Vector3.Distance(startPos, endPos) < 0.01f)
        {
            Debug.LogWarning($"⚠️ Animation: Start and end positions are nearly identical (distance: {Vector3.Distance(startPos, endPos):F4})");
            isMoving = false;
            
            // Clear GameStateManager animation cache to prevent infinite polling
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ClearAnimationCache();
            }
            
            OnMoveComplete();
            yield break;
        }
        
        // Handle instant movement
        if (context == AnimationContext.Instant)
        {
            transform.localPosition = endPos;
            isMoving = false;
            
            // Clear GameStateManager animation cache to prevent infinite polling
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ClearAnimationCache();
            }
            
            OnMoveComplete();
            yield break;
        }
        
        // Queen Blitz: the queen slides along the floor instead, kicking up dust (or ice) behind her
        bool boardMove = context == AnimationContext.UserMove || context == AnimationContext.AIMove;
        if (boardMove && QueenBlitz.Active && pieceType == ChessPieceType.Queen)
        {
            yield return SlideCoroutine(startPos, endPos);
            transform.localPosition = endPos;
            isMoving = false;
            if (GameStateManager.Instance != null) GameStateManager.Instance.ClearAnimationCache();
            OnMoveComplete();
            yield break;
        }

        // Board moves hop: a quick crouch, an arc through the air, then a squash on landing
        bool hop = boardMove;
        float cell = ChessBoard.Instance != null ? ChessBoard.Instance.cellSize : 2.8f;
        if (hop)
        {
            const float crouch = 0.08f;
            for (float c = 0f; c < crouch; c += Time.deltaTime)
            {
                float k = c / crouch;
                PieceEffects.SetSquash(transform, 1f + 0.08f * k, 1f - 0.14f * k);
                yield return null;
            }
        }

        float elapsedTime = 0f;
        float distance = Vector3.Distance(startPos, endPos);
        float hopHeight = Mathf.Max(cell * 0.45f, distance * 0.35f) * (pieceType == ChessPieceType.Knight ? 1.5f : 1f);
        
        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);
            
            // Apply animation curve for smooth easing
            float curveValue = movementCurve.Evaluate(t);
            
            Vector3 currentPos;
            
            if (hop)
            {
                // Arc up and down along the board's own up (the cube can be turned)
                Vector3 basePos = Vector3.Lerp(startPos, endPos, curveValue);
                currentPos = basePos + Vector3.up * Mathf.Sin(curveValue * Mathf.PI) * hopHeight;
                // Stretch on take-off, back to normal at the top
                float stretch = Mathf.Clamp01(1f - t * 3f);
                PieceEffects.SetSquash(transform, 1f - 0.06f * stretch, 1f + 0.14f * stretch);
            }
            else if (useLiftAnimation && liftHeight > 0f)
            {
                // Create arc movement - lift piece up and then down
                Vector3 basePos = Vector3.Lerp(startPos, endPos, curveValue);
                float height = Mathf.Sin(t * Mathf.PI) * liftHeight; // Sin wave for smooth arc
                currentPos = basePos + Vector3.up * height;
            }
            else
            {
                // Simple linear interpolation with curve smoothing
                currentPos = Vector3.Lerp(startPos, endPos, curveValue);
            }
            
            transform.localPosition = currentPos;
            
            yield return null;
        }
        
        // Ensure final position is exact
        transform.localPosition = endPos;
        isMoving = false;
        if (hop) PieceEffects.Land(this);
        
        Debug.Log($"✅ Animation Complete: {pieceColor} {pieceType} reached {targetPosition}");
        
        // Clear GameStateManager animation cache to prevent infinite polling
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ClearAnimationCache();
        }
        
        OnMoveComplete();
    }
    
    /// <summary>
    /// Queen Blitz: slide from one square to another along the floor, speeding up and easing to
    /// a stop, leaning into the slide, with a dust trail (ice spray on an ice floor).
    /// </summary>
    private System.Collections.IEnumerator SlideCoroutine(Vector3 startPos, Vector3 endPos)
    {
        float cell = ChessBoard.Instance != null ? ChessBoard.Instance.cellSize : 2.8f;
        float cells = Vector3.Distance(startPos, endPos) / cell;
        float duration = Mathf.Clamp(0.25f + cells * 0.14f, 0.35f, 1.1f);
        bool ice = QueenBlitz.Current.ice;
        Transform parent = transform.parent;
        Vector3 worldDir = parent != null ? parent.TransformDirection(endPos - startPos) : endPos - startPos;
        float nextTrail = 0f;

        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float t = Mathf.Clamp01(time / duration);
            // Ice: a fast push that glides on; floor: ease in and out
            float k = ice ? 1f - (1f - t) * (1f - t) : t * t * (3f - 2f * t);
            transform.localPosition = Vector3.Lerp(startPos, endPos, k);
            float speed = ice ? 2f * (1f - t) : 6f * t * (1f - t);
            // Squash low and long while moving fast
            PieceEffects.SetSquash(transform, 1f + 0.06f * speed, 1f - 0.08f * speed);
            if (time >= nextTrail && speed > 0.25f)
            {
                nextTrail = time + 0.035f;
                PieceEffects.SlideTrail(transform.position, worldDir, ice);
            }
            yield return null;
        }
        PieceEffects.SetSquash(transform, 1f, 1f);
    }

    protected virtual void OnMoveComplete()
    {
        // Start delayed UI update to ensure visual feedback updates after move completes
        if (CheckDetectionManager.Instance != null && 
            GameStateManager.Instance != null && 
            GameStateManager.Instance.CanMovePieces())
        {
            StartCoroutine(DelayedUIUpdate());
        }
        
        // Update threat indicators after move completion to ensure rays are drawn from correct positions
        if (ThreatIndicatorManager.Instance != null)
        {
            StartCoroutine(DelayedThreatIndicatorUpdate());
        }
        
        // Notify listeners that move animation has completed (for sequential move management)
        OnMoveAnimationComplete?.Invoke(this);
        Debug.Log($"🎬 Animation Event: Fired OnMoveAnimationComplete for {pieceColor} {pieceType}");
        // Override in derived classes for piece-specific move completion logic
    }
    
    /// <summary>
    /// Coroutine to update visual feedback after move completion with a small delay
    /// This ensures the move is fully complete before updating UI
    /// </summary>
    private System.Collections.IEnumerator DelayedUIUpdate()
    {
        // Small delay to ensure move is completely processed
        yield return new UnityEngine.WaitForSeconds(0.1f);
        
        // Safely update visual feedback without interfering with move validation
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
        }
    }
    
    /// <summary>
    /// Coroutine to update threat indicators after move completion
    /// This ensures attack rays are drawn from correct positions after animations complete
    /// </summary>
    private System.Collections.IEnumerator DelayedThreatIndicatorUpdate()
    {
        // Small delay to ensure move is completely processed and position is stable
        yield return new UnityEngine.WaitForSeconds(0.15f);
        
        // Update threat indicators for both kings to ensure rays are positioned correctly
        if (CheckDetectionManager.Instance != null && ThreatIndicatorManager.Instance != null)
        {
            bool whiteInCheck = CheckDetectionManager.Instance.IsKingInCheck(PieceColor.White);
            bool blackInCheck = CheckDetectionManager.Instance.IsKingInCheck(PieceColor.Black);
            
            Debug.Log($"🔄 ChessPiece.DelayedThreatIndicatorUpdate: Check states - White: {whiteInCheck}, Black: {blackInCheck}");
            
            // Only update threat indicators for kings that are actually in check
            if (whiteInCheck)
            {
                Debug.Log("🔄 ChessPiece.DelayedThreatIndicatorUpdate: Updating white king threat indicators after move completion");
                ThreatIndicatorManager.Instance.ShowThreatIndicators(PieceColor.White);
            }
            
            if (blackInCheck)
            {
                Debug.Log("🔄 ChessPiece.DelayedThreatIndicatorUpdate: Updating black king threat indicators after move completion");
                ThreatIndicatorManager.Instance.ShowThreatIndicators(PieceColor.Black);
            }
            
            if (!whiteInCheck && !blackInCheck)
            {
                Debug.Log("🔄 ChessPiece.DelayedThreatIndicatorUpdate: No kings in check, skipping threat indicator updates");
            }
        }
    }
    
    public virtual void OnSelected()
    {
        // Add selection glow to the piece itself
        // Note: Move indicators are handled by InputManager
        AddSelectionGlow();
    }
    
    public virtual void OnDeselected()
    {
        // Remove selection glow from the piece
        // Note: Move indicators are cleared by InputManager
        RemoveSelectionGlow();
    }
    
    protected virtual void OnMouseDown()
    {
        // Clicks and taps are handled by InputManager's raycast. Calling it here too
        // made every click on a piece run twice (and three times on touch screens).
    }
    
    private GameObject selectionGlow;
    private GameObject capturableGlow;
    private GameObject checkIndicator;
    private Material originalMaterial; // Backup of original material for restoring when deselected

    /// <summary>
    /// Helper method to find a suitable shader with fallback
    /// </summary>
    private Shader FindShaderWithFallback()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }
        }
        return shader;
    }

    /// <summary>
    /// A see-through glassy bubble for highlighting pieces. Uses the Crystal piece set's
    /// template (Standard shader in Fade mode), whose shader variant is always in the build.
    /// </summary>
    protected static Material BubbleMaterial(Color color, float alpha, int renderQueue)
    {
        Material template = Resources.Load<Material>("PieceSets/Crystal");
        Material bubble = template != null ? new Material(template) : new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
        bubble.color = new Color(color.r, color.g, color.b, alpha);
        bubble.SetFloat("_Metallic", 0f);
        bubble.SetFloat("_Glossiness", 0.85f);
        bubble.renderQueue = renderQueue;
        // A touch of glow in the bubble's color so it stands out against busy backdrops
        bubble.EnableKeyword("_EMISSION");
        bubble.SetColor("_EmissionColor", color * 0.35f);
        return bubble;
    }

    protected virtual void AddSelectionGlow()
    {
        if (selectionGlow != null) return; // Already has glow

        // TRANSPARENCY FIX: Make the piece itself transparent when selected
        if (meshRenderer != null && meshRenderer.material != null)
        {
            // Store the original material for restoration
            originalMaterial = meshRenderer.material;

            // Use transparent shader for proper alpha blending (same as check indicators)
            Shader transparentShader = Shader.Find("Transparent/Diffuse");
            if (transparentShader == null)
            {
                transparentShader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
                Debug.LogWarning("ChessPiece: Transparent/Diffuse not found, using Legacy version");
            }
            if (transparentShader == null)
            {
                transparentShader = Shader.Find("Sprites/Default");
                Debug.LogWarning("ChessPiece: Legacy transparent shader not found, using Sprites/Default");
            }

            // Create a transparent copy of the piece's material
            Material transparentMaterial = new Material(transparentShader);

            // Preserve the piece's original color but make it semi-transparent
            Color originalColor = originalMaterial.color;
            Color transparentColor = new Color(originalColor.r, originalColor.g, originalColor.b, 0.3f); // 30% opacity
            transparentMaterial.color = transparentColor;
            transparentMaterial.renderQueue = 3000; // Render after opaque objects

            // Apply the transparent material to the piece
            meshRenderer.material = transparentMaterial;

            Debug.Log($"ChessPiece: Applied transparency to selected {pieceColor} {pieceType} (alpha: 0.3)");
        }

        // Create a slightly larger sphere around the piece
        selectionGlow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        selectionGlow.name = "SelectionGlow";
        selectionGlow.transform.SetParent(transform);
        selectionGlow.transform.localPosition = new Vector3(0, 1.05f, 0); // Same as piece center
        selectionGlow.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f); // Moderately larger than piece

        // Remove collider so it doesn't interfere with input
        Collider glowCollider = selectionGlow.GetComponent<Collider>();
        if (glowCollider != null)
        {
            DestroyImmediate(glowCollider);
        }

        // Create glowing material with shader fallback
        selectionGlow.GetComponent<Renderer>().material = BubbleMaterial(NeonTheme.Yellow, 0.34f, 3000);

    }
    
    protected virtual void RemoveSelectionGlow()
    {
        // TRANSPARENCY FIX: Restore the original opaque material
        if (originalMaterial != null && meshRenderer != null)
        {
            meshRenderer.material = originalMaterial;
            originalMaterial = null;
            Debug.Log($"ChessPiece: Restored original material for deselected {pieceColor} {pieceType}");
        }

        if (selectionGlow != null)
        {
            DestroyImmediate(selectionGlow);
            selectionGlow = null;
        }
    }
    
    /// <summary>
    /// Add a blue glow to indicate this piece can be captured
    /// </summary>
    public virtual void AddCapturableGlow()
    {
        if (capturableGlow != null) return; // Already has capturable glow

        // Create a slightly larger sphere around the piece
        capturableGlow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        capturableGlow.name = "CapturableGlow";
        capturableGlow.transform.SetParent(transform);
        capturableGlow.transform.localPosition = new Vector3(0, 1.05f, 0); // Same as piece center
        capturableGlow.transform.localScale = new Vector3(2.2f, 2.2f, 2.2f); // Slightly larger than selection

        // Remove collider so it doesn't interfere with input
        Collider glowCollider = capturableGlow.GetComponent<Collider>();
        if (glowCollider != null)
        {
            DestroyImmediate(glowCollider);
        }

        // Create glowing blue material with emission and shader fallback
        capturableGlow.GetComponent<Renderer>().material = BubbleMaterial(NeonTheme.Cyan, 0.36f, 3000);

        Debug.Log($"🔵 AddCapturableGlow: Created blue glow for {pieceColor} {pieceType} at {CurrentPosition}");
    }
    
    /// <summary>
    /// Remove the blue capturable glow
    /// </summary>
    public virtual void RemoveCapturableGlow()
    {
        if (capturableGlow != null)
        {
            DestroyImmediate(capturableGlow);
            capturableGlow = null;
            Debug.Log($"🔵 RemoveCapturableGlow: Removed blue glow from {pieceColor} {pieceType}");
        }
    }
    
    /// <summary>
    /// Add a red check indicator to show this king is in check
    /// </summary>
    public virtual void ShowCheckIndicator()
    {
        if (pieceType != ChessPieceType.King) return; // Only kings show check indicators
        if (checkIndicator != null) return; // Already has indicator

        // Create a red pulsing sphere around the king
        checkIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        checkIndicator.name = "CheckIndicator";
        checkIndicator.transform.SetParent(transform);
        checkIndicator.transform.localPosition = new Vector3(0, 1.05f, 0); // Same as piece center
        checkIndicator.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f); // Larger than selection glow

        // Remove collider so it doesn't interfere with input
        Collider checkCollider = checkIndicator.GetComponent<Collider>();
        if (checkCollider != null)
        {
            DestroyImmediate(checkCollider);
        }

        // Create pulsing red material using transparent shader
        Renderer checkRenderer = checkIndicator.GetComponent<Renderer>();

        // Use shaders that are BUILT for transparency, not opaque shaders with properties
        Material checkMaterial = BubbleMaterial(new Color(1f, 0.15f, 0.25f), 0.3f, 3001);

        checkRenderer.material = checkMaterial;

        // Add pulsing animation
        CheckIndicatorPulse pulseScript = checkIndicator.AddComponent<CheckIndicatorPulse>();
        pulseScript.minAlpha = 0.22f;
        pulseScript.maxAlpha = 0.45f;

        Debug.Log($"ChessPiece: Added check indicator to {pieceColor} king");
    }
    
    /// <summary>
    /// Remove the check indicator
    /// </summary>
    public virtual void HideCheckIndicator()
    {
        if (checkIndicator != null)
        {
            DestroyImmediate(checkIndicator);
            checkIndicator = null;
            Debug.Log($"ChessPiece: Removed check indicator from {pieceColor} king");
        }
    }
    
    /// <summary>
    /// Reset piece position to invalid state for returning to tray
    /// </summary>
    public virtual void ResetPosition()
    {
        currentPosition = new BoardPosition(-1, -1, -1); // Invalid position
        hasMoved = false;
    }
    
    /// <summary>
    /// Set the current position of the piece (for synchronization purposes)
    /// </summary>
    public virtual void SetCurrentPosition(BoardPosition position)
    {
        // ANTI-CORRUPTION: Prevent tray pieces from getting board positions
        bool isInTray = IsInTray();
        
        if (isInTray && position.IsValid())
        {
            Debug.LogWarning($"🚨 BLOCKED: Attempted to assign board position {position} to tray piece {pieceColor} {pieceType}");
            Debug.LogWarning($"  Piece parent: {(transform.parent != null ? transform.parent.name : "None")}");
            Debug.LogWarning($"  This is likely caused by board synchronization attempting to repair tray pieces");
            return; // Don't allow tray pieces to get board positions
        }
        
        if (!isInTray && !position.IsValid())
        {
            Debug.Log($"ChessPiece: Setting {pieceColor} {pieceType} to invalid position (removed from board)");
        }
        
        currentPosition = position;
    }
    
    /// <summary>
    /// Validate that this piece's position is synchronized with the board array
    /// </summary>
    public bool ValidatePosition()
    {
        // CRITICAL FIX: Never validate tray pieces - they should have invalid positions
        if (IsInTray())
        {
            Debug.Log($"🛡️ ValidatePosition: Skipping validation for tray piece {pieceColor} {pieceType} - tray pieces should have invalid positions");
            return true; // Tray pieces are always "valid" in their context
        }
        
        if (ChessBoard.Instance == null)
        {
            Debug.LogWarning($"ChessPiece.ValidatePosition: ChessBoard.Instance is null for {pieceColor} {pieceType}");
            return false;
        }
        
        // Check if position is invalid
        if (!currentPosition.IsValid())
        {
            Debug.LogError($"🚨 POSITION VALIDATION FAILED: {pieceColor} {pieceType} has invalid position {currentPosition}");
            return false;
        }
        
        // Check if board array matches piece position
        ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(currentPosition);
        if (boardPiece != this)
        {
            Debug.LogError($"🚨 POSITION SYNC MISMATCH: {pieceColor} {pieceType} thinks it's at {currentPosition}, but board has {(boardPiece != null ? $"{boardPiece.pieceColor} {boardPiece.pieceType}" : "NULL")}");
            return false;
        }
        
        Debug.Log($"✅ POSITION VALIDATED: {pieceColor} {pieceType} correctly positioned at {currentPosition}");
        return true;
    }
    
    /// <summary>
    /// Attempt to repair position synchronization issues
    /// </summary>
    public bool RepairPosition()
    {
        // CRITICAL FIX: Never repair tray pieces - they should have invalid positions
        if (IsInTray())
        {
            Debug.Log($"🛡️ RepairPosition: Skipping repair for tray piece {pieceColor} {pieceType} - tray pieces should remain with invalid positions");
            return true; // Tray pieces don't need "repair"
        }
        
        if (ChessBoard.Instance == null)
        {
            Debug.LogError($"ChessPiece.RepairPosition: ChessBoard.Instance is null for {pieceColor} {pieceType}");
            return false;
        }
        
        Debug.Log($"🔧 ATTEMPTING POSITION REPAIR for {pieceColor} {pieceType} at {currentPosition}");
        
        // If position is invalid, try to find where this piece is in the board array
        if (!currentPosition.IsValid())
        {
            Debug.Log($"🔍 Searching board array for misplaced {pieceColor} {pieceType}...");

            // Get dynamic board dimensions
            Vector3Int dims = BoardDimensionsManager.Instance != null
                ? BoardDimensionsManager.Instance.GetDimensions()
                : new Vector3Int(4, 4, 4);

            // Search entire board for this piece
            for (int x = 0; x < dims.x; x++)
            {
                for (int y = 0; y < dims.y; y++)
                {
                    for (int z = 0; z < dims.z; z++)
                    {
                        BoardPosition searchPos = new BoardPosition(x, y, z);
                        ChessPiece foundPiece = ChessBoard.Instance.GetPieceAt(searchPos);

                        if (foundPiece == this)
                        {
                            Debug.Log($"🎯 FOUND: {pieceColor} {pieceType} located at {searchPos} in board array");
                            currentPosition = searchPos;
                            Debug.Log($"✅ REPAIR COMPLETE: Updated position to {currentPosition}");
                            return true;
                        }
                    }
                }
            }

            Debug.LogError($"❌ REPAIR FAILED: {pieceColor} {pieceType} not found in board array");
            return false;
        }
        
        // Position is valid, check if board array is correct
        ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(currentPosition);
        if (boardPiece != this)
        {
            // CRITICAL FIX: Check if there's already another piece at this position
            if (boardPiece != null)
            {
                Debug.LogError($"🚨 CRITICAL: RepairPosition BLOCKED! {pieceColor} {pieceType} wants position {currentPosition} but {boardPiece.pieceColor} {boardPiece.pieceType} is already there!");
                Debug.LogError($"🚨 This indicates a serious position desync bug. Both pieces think they own the same position.");
                Debug.LogError($"🚨 Piece position: {currentPosition}, Board piece: {(boardPiece != null ? $"{boardPiece.pieceColor} {boardPiece.pieceType} at {boardPiece.CurrentPosition}" : "NULL")}");
                
                // DO NOT overwrite! This would delete the existing piece
                // Instead, find where this piece actually belongs
                BoardPosition actualPosition = FindActualBoardPosition();
                if (actualPosition.IsValid())
                {
                    Debug.LogWarning($"🔧 POSITION CORRECTION: Moving {pieceColor} {pieceType} from {currentPosition} to actual position {actualPosition}");
                    SetCurrentPosition(actualPosition);
                    return true;
                }
                else
                {
                    Debug.LogError($"🚨 POSITION REPAIR FAILED: Could not find valid position for {pieceColor} {pieceType}");
                    return false;
                }
            }
            else
            {
                // Position is empty, safe to claim it
                Debug.Log($"🔧 BOARD SYNC: Updating board array at {currentPosition} to contain this piece");
                ChessBoard.Instance.SetPieceAt(currentPosition, this);
                return true;
            }
        }
        
        Debug.Log($"✅ NO REPAIR NEEDED: {pieceColor} {pieceType} position is already correct");
        return true;
    }
    
    /// <summary>
    /// Find the actual position of this piece on the board by searching the board array
    /// </summary>
    private BoardPosition FindActualBoardPosition()
    {
        if (ChessBoard.Instance == null)
        {
            return new BoardPosition(-1, -1, -1); // Invalid position
        }

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Search the entire board array to find where this piece actually is
        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece pieceAtPosition = ChessBoard.Instance.GetPieceAt(pos);

                    if (pieceAtPosition == this)
                    {
                        Debug.Log($"🔍 FindActualBoardPosition: Found {pieceColor} {pieceType} at actual position {pos}");
                        return pos;
                    }
                }
            }
        }

        Debug.LogWarning($"🔍 FindActualBoardPosition: {pieceColor} {pieceType} not found anywhere on the board!");
        return new BoardPosition(-1, -1, -1); // Invalid position
    }
    
    /// <summary>
    /// Convert this piece to a new color (for conquest system in multi-player games)
    /// Changes the piece's color and updates its visual appearance
    /// </summary>
    public virtual void ConvertToColor(PieceColor newColor)
    {
        if (pieceColor == newColor)
        {
            Debug.LogWarning($"ChessPiece.ConvertToColor: {pieceColor} {pieceType} already has color {newColor}, skipping conversion");
            return;
        }

        PieceColor oldColor = pieceColor;
        pieceColor = newColor;

        // Update visual appearance to match new color
        ApplyMaterial();

        Debug.Log($"🎨 CONQUEST: Converted {oldColor} {pieceType} at {currentPosition} to {newColor}");

        // Fire event for UI updates (if needed in future)
        // OnPieceColorChanged?.Invoke(this, oldColor, newColor);
    }

    /// <summary>
    /// Check if this piece is currently in a tray (not on the board)
    /// </summary>
    public bool IsInTray()
    {
        // Check if piece is a child of any tray
        bool isInWhiteTray = PieceTray.WhiteTray != null && transform.IsChildOf(PieceTray.WhiteTray.transform);
        bool isInBlackTray = PieceTray.BlackTray != null && transform.IsChildOf(PieceTray.BlackTray.transform);

        return isInWhiteTray || isInBlackTray;
    }
}