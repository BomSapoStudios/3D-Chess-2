using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Types of chaos rotations that can be applied to the board
/// </summary>
public enum ChaosRotationType
{
    FaceRotation,     // Rotate one face of the cube (6 possible faces)
    LayerRotation,    // Rotate one layer/slice of the cube (12 possible layers)
    DiagonalRotation  // Rotate around diagonal axis (more complex)
}

/// <summary>
/// Face identifiers for Rubik's cube style rotations
/// </summary>
public enum CubeFace
{
    Front,   // Z = 0
    Back,    // Z = 3
    Left,    // X = 0 
    Right,   // X = 3
    Top,     // Y = 3
    Bottom   // Y = 0
}

/// <summary>
/// Axis for layer rotations
/// </summary>
public enum RotationAxis
{
    X, Y, Z
}

/// <summary>
/// Manages random Rubik's cube-style rotations for Chaos Mode in 3D Chess.
/// Applies rotations at random intervals during gameplay to create unpredictable
/// board configurations while maintaining game integrity.
/// </summary>
public class ChaosRotationManager : MonoBehaviour
{
    [Header("Chaos Settings")]
    public bool enableChaosMode = false;
    public float animationDuration = 2.0f;
    public AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Rotation Weights")]
    [Range(0, 100)] public int faceRotationWeight = 60;
    [Range(0, 100)] public int layerRotationWeight = 35;
    [Range(0, 100)] public int diagonalRotationWeight = 5;
    
    [Header("Debug")]
    public bool debugMode = false;
    public bool showRotationPreview = false;
    
    // Chaos timing - configurable interval (default 9 turns)
    private int movesSinceLastChaos = 0;
    public int MovesSinceLastChaos { get => movesSinceLastChaos; set => movesSinceLastChaos = value; }
    private int chaosInterval = 9; // Default interval, configurable via GameConfiguration

    // Animation state
    private bool chaosAnimationInProgress = false;
    private Coroutine currentChaosAnimation;

    // Board dimensions - MULTI-BOARD SUPPORT: Dynamically adapt to 4x4x4, 6x6x6, or 8x8x8
    private Vector3Int boardDimensions;
    
    public static ChaosRotationManager Instance { get; private set; }
    
    // Events
    public System.Action<ChaosRotationType> OnChaosRotationStarted;
    public System.Action<ChaosRotationType> OnChaosRotationCompleted;
    public System.Action<string> OnChaosEvent; // For UI notifications
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("ChaosRotationManager: Instance created");
        }
        else
        {
            Debug.LogWarning("ChaosRotationManager: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        // Get board dimensions from BoardDimensionsManager
        if (BoardDimensionsManager.Instance != null)
        {
            boardDimensions = BoardDimensionsManager.Instance.GetDimensions();
            Debug.Log($"ChaosRotationManager: Using board dimensions {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z}");
        }
        else
        {
            // Fallback to default 4x4x4 if manager not available
            boardDimensions = new Vector3Int(4, 4, 4);
            Debug.LogWarning("ChaosRotationManager: BoardDimensionsManager not available, using default 4x4x4");
        }

        // Initialize chaos settings from game configuration
        InitializeChaosSettings();

        Debug.Log($"ChaosRotationManager: Initialized with chaos mode {(enableChaosMode ? "ENABLED" : "DISABLED")}");
        Debug.Log($"ChaosRotationManager: Configurable chaos triggers every {chaosInterval} moves with 1-3 slice spins");
    }
    
    /// <summary>
    /// Initialize chaos settings from the current game configuration
    /// </summary>
    private void InitializeChaosSettings()
    {
        // Get configuration from SceneController if available
        if (SceneController.Instance != null)
        {
            GameConfiguration config = SceneController.Instance.GetCurrentGameConfiguration();
            if (config != null)
            {
                ApplyChaosConfiguration(config);
            }
        }
    }
    
    /// <summary>
    /// Apply chaos configuration settings
    /// </summary>
    public void ApplyChaosConfiguration(GameConfiguration config)
    {
        if (config == null)
        {
            Debug.LogWarning("ChaosRotationManager: Received null configuration");
            return;
        }
        
        enableChaosMode = config.enableChaosMode;
        chaosInterval = config.chaosTurnInterval;
        
        Debug.Log($"ChaosRotationManager: Applied configuration - Chaos Mode: {enableChaosMode}");
        Debug.Log($"  Configurable interval: Every {chaosInterval} moves");
        Debug.Log($"  Rotation type: Layer rotations only (1-3 spins per event)");
    }
    
    /// <summary>
    /// Check if chaos should trigger after a move - Configurable turn interval
    /// </summary>
    public void OnMoveCompleted(PieceColor movingPlayer)
    {
        if (!enableChaosMode || chaosAnimationInProgress)
            return;
        if (OnlineSession.IsActive) return; // online, the server picks the spins (ApplyRemoteRotation)
        
        movesSinceLastChaos++;
        
        if (debugMode)
        {
            Debug.Log($"ChaosRotationManager: Move #{movesSinceLastChaos} by {movingPlayer}, chaos triggers every {chaosInterval} moves");
        }
        
        // Trigger chaos at configurable interval
        if (movesSinceLastChaos >= chaosInterval)
        {
            Debug.Log($"🎲 CHAOS TRIGGERED! {chaosInterval}-move interval reached");
            TriggerChaosRotation();
            movesSinceLastChaos = 0; // Reset counter
        }
    }
    
    /// <summary>Online: play a spin the server chose (from → to for every moved cell)</summary>
    public void ApplyRemoteRotation(Dictionary<BoardPosition, BoardPosition> map)
    {
        StartCoroutine(RemoteRotationRoutine(map));
    }

    private System.Collections.IEnumerator RemoteRotationRoutine(Dictionary<BoardPosition, BoardPosition> map)
    {
        while (chaosAnimationInProgress) yield return null;
        chaosAnimationInProgress = true;
        OnChaosRotationStarted?.Invoke(ChaosRotationType.LayerRotation);
        if (ChessBoard.Instance != null) yield return StartCoroutine(ChessBoard.Instance.ApplyChaosRotation(map, animationDuration));
        chaosAnimationInProgress = false;
        if (GameStateManager.Instance != null) GameStateManager.Instance.ClearAnimationCache();
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.InvalidateCache();
            CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
        }
        OnChaosRotationCompleted?.Invoke(ChaosRotationType.LayerRotation);
    }

    /// <summary>
    /// Trigger a slice rotation chaos event
    /// </summary>
    public void TriggerChaosRotation()
    {
        if (chaosAnimationInProgress)
        {
            Debug.LogWarning("ChaosRotationManager: Cannot trigger chaos - animation in progress");
            return;
        }
        
        // Always use layer rotation (slice spinning)
        ChaosRotationType rotationType = ChaosRotationType.LayerRotation;
        
        Debug.Log($"🌪️ CHAOS EVENT: Triggering slice rotation (1-3 spins)");
        OnChaosEvent?.Invoke($"🌪️ Chaos Mode Initiated");
        
        // Start the rotation animation
        currentChaosAnimation = StartCoroutine(ExecuteChaosRotation(rotationType));
    }
    
    
    /// <summary>
    /// Execute a chaos rotation with animation
    /// </summary>
    private System.Collections.IEnumerator ExecuteChaosRotation(ChaosRotationType rotationType)
    {
        chaosAnimationInProgress = true;
        OnChaosRotationStarted?.Invoke(rotationType);
        
        // Execute rotation without try-catch to avoid yield issues
        switch (rotationType)
        {
            case ChaosRotationType.FaceRotation:
                yield return StartCoroutine(ExecuteFaceRotation());
                break;
            case ChaosRotationType.LayerRotation:
                yield return StartCoroutine(ExecuteLayerRotation());
                break;
            case ChaosRotationType.DiagonalRotation:
                yield return StartCoroutine(ExecuteDiagonalRotation());
                break;
        }
        
        Debug.Log($"🌪️ CHAOS COMPLETE: Slice rotation finished successfully");
        // OnChaosEvent?.Invoke($"🌪️ Slice rotation complete!"); // Removed: UI notification no longer needed

        chaosAnimationInProgress = false;

        // Clear GameStateManager animation cache to ensure immediate state updates
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.ClearAnimationCache();
            Debug.Log($"🌪️ ChaosRotationManager: Cleared GameStateManager animation cache after chaos completion");
        }

        // CHAOS MODE FIX: Add 1-second delay after animation completes before allowing turns to resume
        // This provides the "breathing room" between chaos completion and next player's turn
        Debug.Log($"🌪️ ChaosRotationManager: Waiting 1 second before resuming gameplay...");
        yield return new UnityEngine.WaitForSeconds(1f);
        Debug.Log($"🌪️ ChaosRotationManager: 1-second delay complete - resuming gameplay");

        // Update check detection AFTER all rotations complete and pieces are in final positions
        if (CheckDetectionManager.Instance != null)
        {
            Debug.Log($"🌪️ ChaosRotationManager: Updating check detection after chaos completion");
            CheckDetectionManager.Instance.InvalidateCache();
            CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
        }

        OnChaosRotationCompleted?.Invoke(rotationType);
    }
    
    /// <summary>
    /// Execute a face rotation (Rubik's cube style)
    /// </summary>
    private System.Collections.IEnumerator ExecuteFaceRotation()
    {
        // Choose random face and direction
        CubeFace face = (CubeFace)Random.Range(0, 6);
        bool clockwise = Random.Range(0, 2) == 0;
        
        Debug.Log($"🎲 Rotating {face} face {(clockwise ? "clockwise" : "counter-clockwise")}");
        
        // Get positions to rotate
        List<BoardPosition> positions = GetFacePositions(face);
        
        // Apply rotation to ChessBoard
        if (ChessBoard.Instance != null)
        {
            yield return StartCoroutine(AnimatePositionRotation(positions, face, clockwise));
        }
    }
    
    /// <summary>
    /// Execute a layer rotation (slice rotation) - 1-3 consecutive rotations
    /// MULTI-BOARD SUPPORT: Selects layers based on actual board size
    /// </summary>
    private System.Collections.IEnumerator ExecuteLayerRotation()
    {
        // Choose random axis and layer for the entire chaos event
        RotationAxis axis = (RotationAxis)Random.Range(0, 3);
        int layer = Random.Range(0, boardDimensions.x); // Use dynamic board size
        bool clockwise = Random.Range(0, 2) == 0;

        // Choose number of rotations (1-3)
        int rotationCount = Random.Range(1, 4);

        Debug.Log($"🎲 Spinning {axis}-axis layer {layer} {rotationCount} time(s) {(clockwise ? "clockwise" : "counter-clockwise")} (board: {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z})");

        // Get positions to rotate
        List<BoardPosition> positions = GetLayerPositions(axis, layer);

        // Apply multiple rotations to ChessBoard
        if (ChessBoard.Instance != null)
        {
            for (int i = 0; i < rotationCount; i++)
            {
                Debug.Log($"   🎲 Rotation {i + 1}/{rotationCount}");
                yield return StartCoroutine(AnimatePositionRotation(positions, axis, layer, clockwise));

                // Brief pause between rotations for visual clarity
                if (i < rotationCount - 1)
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }
        }
    }
    
    /// <summary>
    /// Execute a diagonal rotation (more complex)
    /// </summary>
    private System.Collections.IEnumerator ExecuteDiagonalRotation()
    {
        Debug.Log("🎲 Executing diagonal rotation (complex)");
        
        // For now, implement as a combination of face and layer rotations
        yield return StartCoroutine(ExecuteFaceRotation());
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(ExecuteLayerRotation());
    }
    
    /// <summary>
    /// Get all positions on a cube face
    /// MULTI-BOARD SUPPORT: Works with 4x4x4, 6x6x6, or 8x8x8 boards
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
    /// Get all positions on a layer/slice
    /// MULTI-BOARD SUPPORT: Works with 4x4x4, 6x6x6, or 8x8x8 boards
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
    /// Animate rotation of positions (face rotation)
    /// </summary>
    private System.Collections.IEnumerator AnimatePositionRotation(List<BoardPosition> positions, CubeFace face, bool clockwise)
    {
        if (ChessBoard.Instance == null) yield break;
        
        Debug.Log($"🎬 Animating {face} face rotation ({positions.Count} positions)");
        
        // Use ChessBoard's face rotation method
        yield return StartCoroutine(ChessBoard.Instance.ApplyFaceRotation(face, clockwise, animationDuration));
    }
    
    /// <summary>
    /// Animate rotation of positions (layer rotation)
    /// </summary>
    private System.Collections.IEnumerator AnimatePositionRotation(List<BoardPosition> positions, RotationAxis axis, int layer, bool clockwise)
    {
        if (ChessBoard.Instance == null) yield break;
        
        Debug.Log($"🎬 Animating {axis}-axis layer {layer} rotation ({positions.Count} positions)");
        
        // Use ChessBoard's layer rotation method
        yield return StartCoroutine(ChessBoard.Instance.ApplyLayerRotation(axis, layer, clockwise, animationDuration));
    }
    
    
    /// <summary>
    /// Force trigger chaos (for testing)
    /// </summary>
    [ContextMenu("Force Chaos Rotation")]
    public void ForceChaosRotation()
    {
        if (enableChaosMode)
        {
            Debug.Log("🎲 FORCED CHAOS EVENT");
            TriggerChaosRotation();
        }
        else
        {
            Debug.Log("Chaos mode is disabled");
        }
    }
    
    /// <summary>
    /// Check if chaos animation is in progress
    /// </summary>
    public bool IsChaosAnimationInProgress()
    {
        // Add debug logging to help track chaos state issues
        if (chaosAnimationInProgress && Time.frameCount % 300 == 0) // Log every ~5 seconds at 60fps
        {
            Debug.Log($"🌪️ ChaosRotationManager: Chaos animation still in progress (frame {Time.frameCount}, time {Time.time:F1}s)");
        }
        return chaosAnimationInProgress;
    }
    
    /// <summary>
    /// Get debug information about chaos state
    /// </summary>
    public string GetDebugInfo()
    {
        if (!enableChaosMode) return "Chaos Mode: Disabled";
        
        string info = $"Chaos Mode: Enabled (Configurable)\n";
        info += $"Moves since last chaos: {movesSinceLastChaos}/{chaosInterval}\n";
        info += $"Moves until next chaos: {chaosInterval - movesSinceLastChaos}\n";
        info += $"Animation in progress: {chaosAnimationInProgress}\n";
        info += $"Type: Layer rotations only (1-3 spins per event)";
        
        return info;
    }
}