using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages visual indicators for pieces that are threatening the king during check conditions.
/// Shows orange/red indicators on attacking pieces to distinguish them from the threatened king.
/// Integrates with CheckDetectionManager and CheckVisualFeedbackManager.
/// </summary>
public class ThreatIndicatorManager : MonoBehaviour
{
    [Header("Threat Indicator Settings")]
    public bool enableThreatIndicators = true;
    public bool showAttackPaths = true;
    
    [Header("Visual Settings")]
    [Range(0.5f, 3.0f)]
    public float attackerIndicatorSize = 1.5f;
    [Range(0.1f, 1.0f)]
    public float attackerIndicatorAlpha = 0.7f;
    [Range(1.0f, 5.0f)]
    public float attackerPulseSpeed = 2.0f;
    
    [Header("Attack Path Settings")]
    [Range(0.1f, 1.0f)]
    public float attackPathWidth = 0.2f;  // MODERATE: Thicker than default but not extreme
    [Range(0.1f, 1.0f)]
    public float attackPathAlpha = 0.6f;  // Semi-transparent red lines matching check indicator opacity (60%)
    
    public static ThreatIndicatorManager Instance { get; private set; }
    
    // Track active threat indicators
    private Dictionary<ChessPiece, GameObject> activeAttackerIndicators = new Dictionary<ChessPiece, GameObject>();

    /// <summary>Pieces marked as giving check right now (Color-Blind Symbols adds a target over each)</summary>
    public ICollection<ChessPiece> Attackers => activeAttackerIndicators.Keys;
    private List<GameObject> activeAttackPaths = new List<GameObject>();
    private CheckThreatInfo currentThreats;
    
    // DEBOUNCE: Prevent duplicate threat indicator updates
    private Dictionary<PieceColor, float> lastUpdateTime = new Dictionary<PieceColor, float>();
    private const float UPDATE_DEBOUNCE_TIME = 0.05f; // 50ms debounce (minimal delay to match red check indicator speed)
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("ThreatIndicatorManager: Instance created");
        }
        else
        {
            Debug.LogWarning("ThreatIndicatorManager: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        // Subscribe to check detection events
        if (CheckDetectionManager.Instance != null)
        {
            Debug.Log("ThreatIndicatorManager: Subscribing to CheckDetectionManager events");
            // We'll integrate with CheckVisualFeedbackManager events instead
        }
        else
        {
            Debug.LogWarning("ThreatIndicatorManager: CheckDetectionManager.Instance not found");
        }
        
        // Subscribe to visual feedback events for coordination
        if (CheckVisualFeedbackManager.Instance != null)
        {
            Debug.Log("ThreatIndicatorManager: CheckVisualFeedbackManager found");
        }
        else
        {
            Debug.LogWarning("ThreatIndicatorManager: CheckVisualFeedbackManager not found");
        }
    }
    
    /// <summary>
    /// Show threat indicators for a king in check
    /// </summary>
    /// <param name="kingColor">Color of the king being threatened</param>
    public void ShowThreatIndicators(PieceColor kingColor)
    {
        Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: ENTRY - called for {kingColor} king");
        Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: enableThreatIndicators={enableThreatIndicators}");
        Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: CheckDetectionManager.Instance={(CheckDetectionManager.Instance != null ? "EXISTS" : "NULL")}");
        
        if (!enableThreatIndicators || CheckDetectionManager.Instance == null)
        {
            Debug.Log("🟠 ThreatIndicatorManager.ShowThreatIndicators: Disabled or CheckDetectionManager not available - EXITING");
            return;
        }

        // REMOVED: Animation state blocking to match red check indicator's instant appearance
        // Orange indicators now appear immediately, just like red check indicator

        // DEBOUNCE: Prevent rapid duplicate updates for the same king
        float currentTime = Time.time;
        if (lastUpdateTime.ContainsKey(kingColor))
        {
            float timeSinceLastUpdate = currentTime - lastUpdateTime[kingColor];
            if (timeSinceLastUpdate < UPDATE_DEBOUNCE_TIME)
            {
                Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Debouncing {kingColor} update ({timeSinceLastUpdate:F3}s < {UPDATE_DEBOUNCE_TIME}s) - EXITING");
                return;
            }
        }
        lastUpdateTime[kingColor] = currentTime;
        
        Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Showing threats for {kingColor} king");
        
        // CUMULATIVE APPROACH: Don't clear all indicators, only clear invalid ones
        Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Using cumulative approach - preserving existing valid indicators");
        
        // Get threat information
        currentThreats = CheckDetectionManager.Instance.GetCheckThreats(kingColor);
        
        if (!currentThreats.HasThreats)
        {
            Debug.Log($"ThreatIndicatorManager.ShowThreatIndicators: No threats found for {kingColor} king");
            return;
        }
        
        Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Found {currentThreats.attackingPieces.Count} attacking pieces");
        
        // TIMING FIX: Check if any pieces are still moving before creating indicators/rays
        if (AnyPiecesStillMoving(currentThreats.attackingPieces))
        {
            Debug.Log("🟠 ThreatIndicatorManager: Pieces still moving, delaying threat indicators...");
            StartCoroutine(DelayedShowThreatIndicators(kingColor));
            return;
        }
        
        // Create indicators for each attacking piece (cumulative approach)
        foreach (ChessPiece attacker in currentThreats.attackingPieces)
        {
            // Check if this attacker already has an indicator
            if (activeAttackerIndicators.ContainsKey(attacker))
            {
                Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Indicator already exists for {attacker.pieceColor} {attacker.pieceType} at {attacker.CurrentPosition} - skipping");
                continue;
            }
            
            Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Creating NEW indicator for {attacker.pieceColor} {attacker.pieceType} at {attacker.CurrentPosition}");
            CreateAttackerIndicator(attacker);
        }
        
        // Create attack path lines if enabled
        if (showAttackPaths)
        {
            Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Creating attack paths...");
            // FIXED: Clear existing paths for this king before creating new ones
            ClearAttackPathsForKing(kingColor);
            CreateAttackPaths();
        }
        
        Debug.Log($"🟠 ThreatIndicatorManager.ShowThreatIndicators: Created {activeAttackerIndicators.Count} attacker indicators and {activeAttackPaths.Count} attack paths");
        
        // Validate that indicators were actually created
        if (currentThreats.attackingPieces.Count > 0 && activeAttackerIndicators.Count == 0)
        {
            Debug.LogError($"🟠 ❌ ThreatIndicatorManager.ShowThreatIndicators: PROBLEM - {currentThreats.attackingPieces.Count} attacking pieces found but NO indicators created!");
        }
        else if (activeAttackerIndicators.Count != currentThreats.attackingPieces.Count)
        {
            Debug.LogWarning($"🟠 ⚠️ ThreatIndicatorManager.ShowThreatIndicators: MISMATCH - {currentThreats.attackingPieces.Count} attacking pieces but {activeAttackerIndicators.Count} indicators created");
        }
        else
        {
            Debug.Log($"🟠 ✅ ThreatIndicatorManager.ShowThreatIndicators: SUCCESS - All {currentThreats.attackingPieces.Count} attacking pieces have indicators");
        }
    }
    
    /// <summary>
    /// Hide all threat indicators
    /// </summary>
    public void ClearThreatIndicators()
    {
        Debug.Log($"ThreatIndicatorManager.ClearThreatIndicators: Clearing {activeAttackerIndicators.Count} attacker indicators and {activeAttackPaths.Count} attack paths");
        
        // Clear attacker indicators
        foreach (var kvp in activeAttackerIndicators)
        {
            if (kvp.Value != null)
            {
                DestroyImmediate(kvp.Value);
            }
        }
        activeAttackerIndicators.Clear();
        
        // Clear attack paths
        foreach (GameObject path in activeAttackPaths)
        {
            if (path != null)
            {
                DestroyImmediate(path);
            }
        }
        activeAttackPaths.Clear();
        
        currentThreats = null;
        
        Debug.Log("ThreatIndicatorManager.ClearThreatIndicators: All threat indicators cleared");
    }
    
    /// <summary>
    /// Clear threat indicators only for pieces that are no longer attacking the specified king
    /// </summary>
    /// <param name="resolvedKingColor">The king whose check was resolved</param>
    public void ClearThreatIndicatorsForResolvedKing(PieceColor resolvedKingColor)
    {
        if (CheckDetectionManager.Instance == null)
        {
            Debug.LogWarning("ThreatIndicatorManager.ClearThreatIndicatorsForResolvedKing: CheckDetectionManager not available");
            return;
        }

        Debug.Log($"🟢 ThreatIndicatorManager.ClearThreatIndicatorsForResolvedKing: Clearing indicators for {resolvedKingColor} king resolution");

        // Get current threats for the resolved king (should be empty now)
        CheckThreatInfo resolvedThreats = CheckDetectionManager.Instance.GetCheckThreats(resolvedKingColor);

        // Find indicators to remove: pieces that were attacking this king but no longer are
        List<ChessPiece> indicatorsToRemove = new List<ChessPiece>();

        foreach (var kvp in activeAttackerIndicators)
        {
            ChessPiece attacker = kvp.Key;

            // If this piece is no longer attacking the resolved king, remove its indicator
            if (!resolvedThreats.attackingPieces.Contains(attacker))
            {
                // MULTI-PLAYER FIX: Check if this piece is still attacking ANY other king
                bool stillAttackingAnyKing = false;

                // Get all player colors that have kings in the game
                List<PieceColor> allPlayers = new List<PieceColor>();
                if (PlayerManager.Instance != null)
                {
                    allPlayers = PlayerManager.Instance.GetActivePlayers();
                }
                else
                {
                    // Fallback for 2-player games
                    allPlayers.Add(PieceColor.White);
                    allPlayers.Add(PieceColor.Black);
                }

                // Check threats against all kings except the resolved one
                foreach (PieceColor kingColor in allPlayers)
                {
                    if (kingColor == resolvedKingColor)
                        continue; // Skip the king we already know is resolved

                    CheckThreatInfo kingThreats = CheckDetectionManager.Instance.GetCheckThreats(kingColor);
                    if (kingThreats.attackingPieces.Contains(attacker))
                    {
                        stillAttackingAnyKing = true;
                        Debug.Log($"🟢 ThreatIndicatorManager.ClearThreatIndicatorsForResolvedKing: Keeping indicator for {attacker.pieceColor} {attacker.pieceType} (still attacking {kingColor} king)");
                        break;
                    }
                }

                if (!stillAttackingAnyKing)
                {
                    // This piece is not attacking any king, remove its indicator
                    indicatorsToRemove.Add(attacker);
                    Debug.Log($"🟢 ThreatIndicatorManager.ClearThreatIndicatorsForResolvedKing: Removing indicator for {attacker.pieceColor} {attacker.pieceType} (no longer attacking any king)");
                }
            }
        }
        
        // Remove the identified indicators
        foreach (ChessPiece attacker in indicatorsToRemove)
        {
            if (activeAttackerIndicators.ContainsKey(attacker))
            {
                GameObject indicator = activeAttackerIndicators[attacker];
                if (indicator != null)
                {
                    DestroyImmediate(indicator);
                }
                activeAttackerIndicators.Remove(attacker);
            }
        }
        
        Debug.Log($"🟢 ThreatIndicatorManager.ClearThreatIndicatorsForResolvedKing: Removed {indicatorsToRemove.Count} indicators, {activeAttackerIndicators.Count} remain");
        
        // Clear attack paths that are no longer valid
        ClearInvalidAttackPaths();
        
        Debug.Log($"🟢 ThreatIndicatorManager.ClearThreatIndicatorsForResolvedKing: Attack paths cleared, {activeAttackPaths.Count} paths remain");
    }
    
    /// <summary>
    /// Create a visual indicator for an attacking piece
    /// </summary>
    /// <param name="attacker">The piece threatening the king</param>
    private void CreateAttackerIndicator(ChessPiece attacker)
    {
        if (attacker == null || ChessBoard.Instance == null)
        {
            Debug.LogError("ThreatIndicatorManager.CreateAttackerIndicator: Null attacker or ChessBoard");
            return;
        }
        
        Debug.Log($"🟠 ThreatIndicatorManager.CreateAttackerIndicator: Creating indicator for {attacker.pieceColor} {attacker.pieceType} at {attacker.CurrentPosition}");
        
        // Create indicator sphere
        GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        indicator.name = $"ThreatIndicator_{attacker.pieceColor}_{attacker.pieceType}_{attacker.CurrentPosition}";
        
        // Make it a child of the rotating board so it rotates with the board
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        if (piecesContainer != null)
        {
            indicator.transform.SetParent(piecesContainer.transform);
        }
        
        // Position at the attacking piece's location (slightly above)
        Vector3 localPos = ChessBoard.Instance.BoardToLocalPosition(attacker.CurrentPosition);
        indicator.transform.localPosition = localPos + Vector3.up * 0.3f; // Lower than king indicator
        
        // Scale for attacker indicator (smaller than king's check indicator)
        indicator.transform.localScale = Vector3.one * attackerIndicatorSize;
        
        // Remove collider so it doesn't interfere with input
        Collider indicatorCollider = indicator.GetComponent<Collider>();
        if (indicatorCollider != null)
        {
            DestroyImmediate(indicatorCollider);
        }
        
        // Create orange/red material for attacking piece
        Renderer renderer = indicator.GetComponent<Renderer>();
        Material threatMaterial = CreateAttackerMaterial();
        renderer.material = threatMaterial;
        
        // Add pulsing animation
        ThreatIndicatorPulse pulseScript = indicator.AddComponent<ThreatIndicatorPulse>();
        pulseScript.pulseSpeed = attackerPulseSpeed;
        pulseScript.minScale = attackerIndicatorSize * 0.8f;
        pulseScript.maxScale = attackerIndicatorSize * 1.2f;
        pulseScript.minAlpha = attackerIndicatorAlpha * 0.5f;
        pulseScript.maxAlpha = attackerIndicatorAlpha;
        
        // Track the indicator
        activeAttackerIndicators[attacker] = indicator;
        
        Debug.Log($"🟠 ThreatIndicatorManager.CreateAttackerIndicator: Successfully created orange indicator '{indicator.name}' at {indicator.transform.localPosition}");
    }
    
    /// <summary>
    /// Create visual attack path lines from attackers to king
    /// </summary>
    private void CreateAttackPaths()
    {
        if (currentThreats == null || !currentThreats.HasThreats || ChessBoard.Instance == null)
        {
            return;
        }
        
        Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: Creating {currentThreats.attackingPieces.Count} attack paths");
        Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: King at board position {currentThreats.kingPosition}");
        
        // VALIDATION: Find and validate the king piece at the threat position
        ChessPiece kingPiece = ChessBoard.Instance.GetPieceAt(currentThreats.kingPosition);
        if (kingPiece == null)
        {
            Debug.LogError($"ThreatIndicatorManager.CreateAttackPaths: No king found at position {currentThreats.kingPosition}");
            return;
        }
        
        // VALIDATION: Ensure king position is synchronized and valid
        if (!ValidatePiecePosition(kingPiece, currentThreats.kingPosition))
        {
            Debug.LogError($"ThreatIndicatorManager.CreateAttackPaths: King position validation failed, skipping path creation");
            return;
        }
        
        // FIXED: Use logical board position instead of transform position to avoid animation race condition
        Vector3 kingLocalPos = ChessBoard.Instance.BoardToLocalPosition(kingPiece.CurrentPosition);
        
        // Convert to world space by getting the Pieces Container transform
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        if (piecesContainer == null)
        {
            Debug.LogError("ThreatIndicatorManager.CreateAttackPaths: Pieces Container not found! Rays may be positioned incorrectly.");
        }
        
        Vector3 kingWorldPos = piecesContainer != null ? 
            piecesContainer.transform.TransformPoint(kingLocalPos) : kingLocalPos;
        Vector3 kingRayPos = kingWorldPos + Vector3.up * 0.5f;
        
        Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: King at board {kingPiece.CurrentPosition}, local: {kingLocalPos}, world: {kingWorldPos}, ray: {kingRayPos}");
        
        foreach (ChessPiece attacker in currentThreats.attackingPieces)
        {
            // VALIDATION: Skip null or invalid attackers
            if (attacker == null)
            {
                Debug.LogWarning($"ThreatIndicatorManager.CreateAttackPaths: Null attacker found, skipping");
                continue;
            }
            
            // VALIDATION: Ensure attacker position is synchronized and valid
            if (!ValidatePiecePosition(attacker, attacker.CurrentPosition))
            {
                Debug.LogWarning($"ThreatIndicatorManager.CreateAttackPaths: Attacker {attacker.pieceColor} {attacker.pieceType} position validation failed, skipping ray");
                continue;
            }
            
            // VALIDATION: Ensure attacker is not moving
            if (attacker.IsMoving)
            {
                Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: Attacker {attacker.pieceColor} {attacker.pieceType} still moving, skipping ray");
                continue;
            }
            
            // FIXED: Use logical board position instead of transform position to avoid animation race condition  
            Vector3 attackerLocalPos = ChessBoard.Instance.BoardToLocalPosition(attacker.CurrentPosition);
            
            // Convert to world space by getting the Pieces Container transform
            Vector3 attackerWorldPos = piecesContainer != null ? 
                piecesContainer.transform.TransformPoint(attackerLocalPos) : attackerLocalPos;
            Vector3 attackerRayPos = attackerWorldPos + Vector3.up * 0.5f;
            
            Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: Attacker {attacker.pieceColor} {attacker.pieceType} at board {attacker.CurrentPosition}");
            Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: Attacker local: {attackerLocalPos}, world: {attackerWorldPos}, ray: {attackerRayPos}");
            
            // VALIDATION: Ensure ray endpoints are different (avoid zero-length rays)
            if (Vector3.Distance(attackerRayPos, kingRayPos) < 0.01f)
            {
                Debug.LogWarning($"ThreatIndicatorManager.CreateAttackPaths: Ray endpoints too close, skipping ray from {attacker.pieceType}");
                continue;
            }
            
            GameObject attackLine = CreateAttackLine(attackerRayPos, kingRayPos);
            if (attackLine != null)
            {
                activeAttackPaths.Add(attackLine);
                Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: Created attack line '{attackLine.name}' from {attackerRayPos} to {kingRayPos}");
            }
        }
        
        Debug.Log($"ThreatIndicatorManager.CreateAttackPaths: Created {activeAttackPaths.Count} attack path lines");
    }
    
    /// <summary>
    /// Create a line renderer for attack path visualization
    /// </summary>
    /// <param name="start">Start position (attacker)</param>
    /// <param name="end">End position (king)</param>
    /// <returns>GameObject with LineRenderer</returns>
    private GameObject CreateAttackLine(Vector3 start, Vector3 end)
    {
        GameObject lineObject = new GameObject("AttackPath");
        
        // Make it a child of the rotating board
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        if (piecesContainer != null)
        {
            lineObject.transform.SetParent(piecesContainer.transform);
        }
        
        // Add LineRenderer component
        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();
        
        // Configure line renderer
        lineRenderer.material = CreateAttackLineMaterial();
        lineRenderer.startWidth = attackPathWidth;
        lineRenderer.endWidth = attackPathWidth;
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true; // FIXED: Use world space to avoid coordinate system issues
        
        // Use world space positions directly - no coordinate transformation needed
        Vector3 worldStart = start;
        Vector3 worldEnd = end;
        
        Debug.Log($"ThreatIndicatorManager.CreateAttackLine: Setting LineRenderer world positions - Start: {worldStart}, End: {worldEnd}");
        Debug.Log($"ThreatIndicatorManager.CreateAttackLine: LineRenderer useWorldSpace: {lineRenderer.useWorldSpace}");
        Debug.Log($"ThreatIndicatorManager.CreateAttackLine: Parent container: {(piecesContainer != null ? piecesContainer.name : "NULL")}");
        
        lineRenderer.SetPosition(0, worldStart);
        lineRenderer.SetPosition(1, worldEnd);
        
        // ENHANCED: Material already has optimized render queue (3100) for high visibility
        // No need to override here since CreateAttackLineMaterial sets the appropriate queue
        
        return lineObject;
    }
    
    /// <summary>
    /// Create material for attacking piece indicators (orange/red)
    /// </summary>
    /// <returns>Material for attacker indicators</returns>
    private Material CreateAttackerMaterial()
    {
        // Use same transparent shader chain as red check indicator for consistency
        Shader shader = Shader.Find("Transparent/Diffuse");
        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            Debug.LogWarning("ThreatIndicatorManager: Transparent/Diffuse not found, using Legacy version");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
            Debug.LogWarning("ThreatIndicatorManager: Legacy transparent shader not found, using Sprites/Default");
        }

        Material material = new Material(shader);

        // Orange color to distinguish from pure red king indicator - match alpha to red indicator (0.6f)
        Color attackerColor = new Color(1.0f, 0.4f, 0.0f, 0.6f); // Semi-transparent orange matching red indicator (60% opacity)
        material.color = attackerColor;
        material.renderQueue = 3000; // Render after other transparent objects

        Debug.Log($"ThreatIndicatorManager: Created attacker material (ORANGE) using shader {shader.name}");
        return material;
    }
    
    /// <summary>
    /// Create material for attack path lines - using same transparent shader as indicators for consistency
    /// </summary>
    /// <returns>Material for attack lines</returns>
    private Material CreateAttackLineMaterial()
    {
        // Use same transparent shader chain as orange/red indicators for consistency
        Shader shader = Shader.Find("Transparent/Diffuse");
        if (shader == null)
        {
            shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            Debug.LogWarning("ThreatIndicatorManager: Transparent/Diffuse not found, using Legacy version for attack lines");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
            Debug.LogWarning("ThreatIndicatorManager: Legacy transparent shader not found, using Sprites/Default for attack lines");
        }

        Material material = new Material(shader);

        // Bright red color matching check indicator style with 60% opacity for visibility
        Color lineColor = new Color(1.0f, 0.0f, 0.0f, attackPathAlpha); // Bright red at 60% opacity
        material.color = lineColor;
        material.renderQueue = 3000; // Render after other transparent objects, same as threat indicators

        Debug.Log($"ThreatIndicatorManager: Created attack line material (RED) using shader {shader.name}");
        return material;
    }
    
    /// <summary>
    /// Update threat indicators when pieces move (cleanup invalid references)
    /// </summary>
    public void ValidateThreatIndicators()
    {
        List<ChessPiece> invalidPieces = new List<ChessPiece>();
        
        foreach (var kvp in activeAttackerIndicators)
        {
            if (kvp.Key == null || kvp.Value == null)
            {
                invalidPieces.Add(kvp.Key);
            }
        }
        
        foreach (ChessPiece invalidPiece in invalidPieces)
        {
            activeAttackerIndicators.Remove(invalidPiece);
        }
        
        if (invalidPieces.Count > 0)
        {
            Debug.Log($"ThreatIndicatorManager.ValidateThreatIndicators: Cleaned up {invalidPieces.Count} invalid indicators");
        }
    }
    
    /// <summary>
    /// Clear attack paths that are no longer valid (no longer represent active threats)
    /// </summary>
    private void ClearInvalidAttackPaths()
    {
        if (CheckDetectionManager.Instance == null)
        {
            return;
        }

        // MULTI-PLAYER FIX: Get threats for ALL active players, not just White and Black
        // Create a set of valid attacker positions that should have attack paths
        HashSet<BoardPosition> validAttackerPositions = new HashSet<BoardPosition>();

        // Get all player colors that have kings in the game
        List<PieceColor> allPlayers = new List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            allPlayers = PlayerManager.Instance.GetActivePlayers();
        }
        else
        {
            // Fallback for 2-player games
            allPlayers.Add(PieceColor.White);
            allPlayers.Add(PieceColor.Black);
        }

        // Gather all attacking pieces across all kings
        foreach (PieceColor kingColor in allPlayers)
        {
            CheckThreatInfo kingThreats = CheckDetectionManager.Instance.GetCheckThreats(kingColor);
            foreach (ChessPiece attacker in kingThreats.attackingPieces)
            {
                if (attacker != null)
                {
                    validAttackerPositions.Add(attacker.CurrentPosition);
                }
            }
        }

        Debug.Log($"🟢 ThreatIndicatorManager.ClearInvalidAttackPaths: Found {validAttackerPositions.Count} valid attacker positions across {allPlayers.Count} players");
        
        // Clear all attack paths since they don't have position tracking
        // This is a simpler approach: clear all and let them be recreated as needed
        List<GameObject> pathsToRemove = new List<GameObject>(activeAttackPaths);
        
        foreach (GameObject path in pathsToRemove)
        {
            if (path != null)
            {
                Debug.Log($"🟢 ThreatIndicatorManager.ClearInvalidAttackPaths: Clearing attack path '{path.name}'");
                DestroyImmediate(path);
            }
        }
        
        activeAttackPaths.Clear();
        
        Debug.Log($"🟢 ThreatIndicatorManager.ClearInvalidAttackPaths: Cleared {pathsToRemove.Count} attack paths");
    }
    
    /// <summary>
    /// Check if any pieces in the list are still moving (have animations in progress)
    /// </summary>
    /// <param name="pieces">List of pieces to check</param>
    /// <returns>True if any piece is still moving</returns>
    private bool AnyPiecesStillMoving(List<ChessPiece> pieces)
    {
        if (pieces == null || pieces.Count == 0)
        {
            return false;
        }
        
        foreach (ChessPiece piece in pieces)
        {
            if (piece != null && piece.IsMoving)
            {
                Debug.Log($"🟠 ThreatIndicatorManager.AnyPiecesStillMoving: {piece.pieceColor} {piece.pieceType} at {piece.CurrentPosition} is still moving");
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Check if ANY pieces on the entire board are still moving (enhanced animation state blocking)
    /// </summary>
    /// <returns>True if any piece on the board is still moving</returns>
    private bool AnyPiecesMovingOnBoard()
    {
        if (ChessBoard.Instance == null)
        {
            Debug.LogWarning("🟠 ThreatIndicatorManager.AnyPiecesMovingOnBoard: ChessBoard.Instance is null");
            return false;
        }

        // Get dynamic board dimensions to support 4x4x4, 6x6x6, and 8x8x8 boards
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4

        // Iterate through all positions on the board to check for moving pieces
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
                        Debug.Log($"🟠 ThreatIndicatorManager.AnyPiecesMovingOnBoard: {piece.pieceColor} {piece.pieceType} at {piece.CurrentPosition} is still moving - BLOCKING threat indicators");
                        return true;
                    }
                }
            }
        }

        return false;
    }
    
    /// <summary>
    /// Coroutine to delay threat indicator creation until pieces finish moving
    /// </summary>
    /// <param name="kingColor">Color of the king being threatened</param>
    /// <returns>Coroutine enumerator</returns>
    private System.Collections.IEnumerator DelayedShowThreatIndicators(PieceColor kingColor)
    {
        Debug.Log($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: ENTRY - waiting for {kingColor} king threats to stabilize");

        // REMOVED: Initial delays to match red check indicator speed
        // Wait just one frame to allow position updates
        yield return null;

        // Keep checking until all attacking pieces have stopped moving
        int maxWaitFrames = 180; // 3 seconds at 60fps safety limit (reduced for better responsiveness)
        int frameCount = 0;
        
        while (frameCount < maxWaitFrames)
        {
            // Re-get current threats in case they changed during movement
            if (CheckDetectionManager.Instance == null)
            {
                Debug.LogError("🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: CheckDetectionManager.Instance is null, aborting");
                yield break;
            }
            
            CheckThreatInfo currentThreats = CheckDetectionManager.Instance.GetCheckThreats(kingColor);
            
            if (!currentThreats.HasThreats)
            {
                Debug.Log($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: No threats found for {kingColor} king after waiting, aborting");
                yield break;
            }
            
            // Check if pieces are still moving
            bool anyStillMoving = AnyPiecesStillMoving(currentThreats.attackingPieces);
            
            if (!anyStillMoving)
            {
                Debug.Log($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: All pieces stopped moving after {frameCount} frames, creating indicators now");
                
                // Update current threats and show indicators
                this.currentThreats = currentThreats;
                
                // Create indicators for each attacking piece
                foreach (ChessPiece attacker in currentThreats.attackingPieces)
                {
                    // Check if this attacker already has an indicator
                    if (activeAttackerIndicators.ContainsKey(attacker))
                    {
                        Debug.Log($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: Indicator already exists for {attacker.pieceColor} {attacker.pieceType} at {attacker.CurrentPosition} - skipping");
                        continue;
                    }
                    
                    Debug.Log($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: Creating delayed indicator for {attacker.pieceColor} {attacker.pieceType} at {attacker.CurrentPosition}");
                    CreateAttackerIndicator(attacker);
                }
                
                // Create attack path lines if enabled
                if (showAttackPaths)
                {
                    Debug.Log($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: Creating delayed attack paths...");
                    // FIXED: Clear existing paths for this king before creating new ones
                    ClearAttackPathsForKing(kingColor);
                    CreateAttackPaths();
                }
                
                Debug.Log($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: SUCCESS - Created {activeAttackerIndicators.Count} indicators and {activeAttackPaths.Count} paths after movement delay");
                yield break;
            }
            
            frameCount++;
            yield return null; // Wait one frame
        }
        
        // Safety timeout - create indicators anyway
        Debug.LogWarning($"🟠 ThreatIndicatorManager.DelayedShowThreatIndicators: Timeout after {maxWaitFrames} frames, creating indicators anyway");
        
        // Get final threat state and create indicators
        CheckThreatInfo finalThreats = CheckDetectionManager.Instance.GetCheckThreats(kingColor);
        if (finalThreats.HasThreats)
        {
            this.currentThreats = finalThreats;
            
            foreach (ChessPiece attacker in finalThreats.attackingPieces)
            {
                if (!activeAttackerIndicators.ContainsKey(attacker))
                {
                    CreateAttackerIndicator(attacker);
                }
            }
            
            if (showAttackPaths)
            {
                // FIXED: Clear existing paths for this king before creating new ones
                ClearAttackPathsForKing(kingColor);
                CreateAttackPaths();
            }
        }
    }
    
    /// <summary>
    /// Clear attack paths specifically for threats against a particular king
    /// This prevents accumulation of old paths when threats are updated
    /// </summary>
    /// <param name="kingColor">Color of the king whose threat paths should be cleared</param>
    private void ClearAttackPathsForKing(PieceColor kingColor)
    {
        Debug.Log($"🟠 ThreatIndicatorManager.ClearAttackPathsForKing: Clearing attack paths for {kingColor} king threats");
        Debug.Log($"🟠 Current attack paths before clearing: {activeAttackPaths.Count}");
        
        // For now, clear all attack paths since we don't have per-king tracking
        // This ensures no stale rays remain when updating threats
        List<GameObject> pathsToRemove = new List<GameObject>(activeAttackPaths);
        
        foreach (GameObject path in pathsToRemove)
        {
            if (path != null)
            {
                Debug.Log($"🟠 ThreatIndicatorManager.ClearAttackPathsForKing: Destroying attack path '{path.name}'");
                DestroyImmediate(path);
            }
        }
        
        activeAttackPaths.Clear();
        Debug.Log($"🟠 ThreatIndicatorManager.ClearAttackPathsForKing: Cleared {pathsToRemove.Count} attack paths for {kingColor} king");
    }
    
    /// <summary>
    /// Validate that a piece's position is synchronized and correct for ray drawing
    /// </summary>
    /// <param name="piece">Piece to validate</param>
    /// <param name="expectedPosition">Expected board position</param>
    /// <returns>True if piece position is valid for ray drawing</returns>
    private bool ValidatePiecePosition(ChessPiece piece, BoardPosition expectedPosition)
    {
        if (piece == null)
        {
            Debug.LogWarning($"ThreatIndicatorManager.ValidatePiecePosition: Piece is null");
            return false;
        }
        
        if (!expectedPosition.IsValid())
        {
            Debug.LogWarning($"ThreatIndicatorManager.ValidatePiecePosition: Expected position {expectedPosition} is invalid");
            return false;
        }
        
        // Check if piece's current position matches expected position
        BoardPosition piecePosition = piece.CurrentPosition;
        if (piecePosition != expectedPosition)
        {
            Debug.LogWarning($"ThreatIndicatorManager.ValidatePiecePosition: Position mismatch - piece at {piecePosition}, expected {expectedPosition}");
            return false;
        }
        
        // Check if ChessBoard agrees with piece position
        if (ChessBoard.Instance != null)
        {
            ChessPiece boardPiece = ChessBoard.Instance.GetPieceAt(expectedPosition);
            if (boardPiece != piece)
            {
                Debug.LogWarning($"ThreatIndicatorManager.ValidatePiecePosition: Board sync mismatch - board has {(boardPiece != null ? $"{boardPiece.pieceColor} {boardPiece.pieceType}" : "NULL")} at {expectedPosition}, expected {piece.pieceColor} {piece.pieceType}");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Get debug information about current threat indicators
    /// </summary>
    /// <returns>Debug string</returns>
    public string GetDebugInfo()
    {
        return $"ThreatIndicators: {activeAttackerIndicators.Count} attackers, {activeAttackPaths.Count} paths, Enabled: {enableThreatIndicators}";
    }
}