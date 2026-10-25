using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a tray that holds pieces before they are placed on the board
/// Each player has their own tray positioned beside the board
/// </summary>
public class PieceTray : MonoBehaviour
{
    [Header("Tray Configuration")]
    public PieceColor trayColor;
    public int maxPieces = 8; // Increased to accommodate all piece types: King, Queen, 2 Bishops, 2 Knights, 2 Rooks
    public float pieceSpacing = 2f;
    public bool isLeftSide = true; // true for left side of board, false for right
    
    [Header("Visual Elements")]
    public Material trayMaterial;
    public GameObject trayBase;
    
    private List<ChessPiece> piecesInTray = new List<ChessPiece>();
    private List<Vector3> piecePositions = new List<Vector3>();
    
    // Reorganization control - disable during placement phase to prevent unwanted piece shifting
    [Header("Reorganization Control")]
    public bool enableReorganization = false; // Default disabled to prevent shifting during placement
    
    public static PieceTray WhiteTray { get; private set; }
    public static PieceTray BlackTray { get; private set; }
    public static PieceTray GreenTray { get; private set; }
    public static PieceTray PurpleTray { get; private set; }
    public static PieceTray YellowTray { get; private set; }
    public static PieceTray OrangeTray { get; private set; }
    
    private void Awake()
    {
        Debug.Log($"PieceTray.Awake: Starting - component added but waiting for explicit initialization");
        Debug.Log($"  GameObject: '{gameObject.name}', trayColor: {trayColor} (may be default)");
        
        // DO NOT register here - wait for explicit Initialize() call
        // This fixes the Unity component lifecycle issue where Awake() runs before field assignment
    }
    
    /// <summary>
    /// Initialize the tray with explicit color and position settings
    /// This must be called after the component is added and fields are set
    /// </summary>
    public void Initialize(PieceColor color, bool leftSide)
    {
        Debug.Log($"PieceTray.Initialize: Starting initialization for {color} tray on {(leftSide ? "left" : "right")} side");
        
        // Set the properties explicitly
        trayColor = color;
        isLeftSide = leftSide;
        
        Debug.Log($"PieceTray.Initialize: Set trayColor = {trayColor}, isLeftSide = {isLeftSide}");
        
        // Register tray instances for easy access (prevent duplicates)
        switch (trayColor)
        {
            case PieceColor.White:
                if (WhiteTray == null)
                {
                    WhiteTray = this;
                    Debug.Log("✅ PieceTray: White tray registered successfully");
                }
                else if (WhiteTray != this)
                {
                    Debug.LogError($"❌ PieceTray: White tray already exists ({WhiteTray.name})! Cannot register duplicate.");
                    return;
                }
                else
                {
                    Debug.Log("PieceTray: White tray already registered to this object");
                }
                break;

            case PieceColor.Black:
                if (BlackTray == null)
                {
                    BlackTray = this;
                    Debug.Log("✅ PieceTray: Black tray registered successfully");
                }
                else if (BlackTray != this)
                {
                    Debug.LogError($"❌ PieceTray: Black tray already exists ({BlackTray.name})! Cannot register duplicate.");
                    return;
                }
                else
                {
                    Debug.Log("PieceTray: Black tray already registered to this object");
                }
                break;

            case PieceColor.Green:
                if (GreenTray == null)
                {
                    GreenTray = this;
                    Debug.Log("✅ PieceTray: Green tray registered successfully");
                }
                else if (GreenTray != this)
                {
                    Debug.LogError($"❌ PieceTray: Green tray already exists ({GreenTray.name})! Cannot register duplicate.");
                    return;
                }
                else
                {
                    Debug.Log("PieceTray: Green tray already registered to this object");
                }
                break;

            case PieceColor.Purple:
                if (PurpleTray == null)
                {
                    PurpleTray = this;
                    Debug.Log("✅ PieceTray: Purple tray registered successfully");
                }
                else if (PurpleTray != this)
                {
                    Debug.LogError($"❌ PieceTray: Purple tray already exists ({PurpleTray.name})! Cannot register duplicate.");
                    return;
                }
                else
                {
                    Debug.Log("PieceTray: Purple tray already registered to this object");
                }
                break;

            case PieceColor.Yellow:
                if (YellowTray == null)
                {
                    YellowTray = this;
                    Debug.Log("✅ PieceTray: Yellow tray registered successfully");
                }
                else if (YellowTray != this)
                {
                    Debug.LogError($"❌ PieceTray: Yellow tray already exists ({YellowTray.name})! Cannot register duplicate.");
                    return;
                }
                else
                {
                    Debug.Log("PieceTray: Yellow tray already registered to this object");
                }
                break;

            case PieceColor.Orange:
                if (OrangeTray == null)
                {
                    OrangeTray = this;
                    Debug.Log("✅ PieceTray: Orange tray registered successfully");
                }
                else if (OrangeTray != this)
                {
                    Debug.LogError($"❌ PieceTray: Orange tray already exists ({OrangeTray.name})! Cannot register duplicate.");
                    return;
                }
                else
                {
                    Debug.Log("PieceTray: Orange tray already registered to this object");
                }
                break;

            default:
                Debug.LogError($"PieceTray: Invalid tray color {trayColor}!");
                return;
        }

        // CRITICAL: Register with PieceTrayManager to ensure both tracking systems stay in sync
        // This fixes the bug where AIPlayer uses PieceTrayManager.GetTray() but PlacementManager uses GetTrayForColor()
        if (PieceTrayManager.Instance != null)
        {
            PieceTrayManager.Instance.RegisterTray(trayColor, this);
            Debug.Log($"✅ PieceTray: Registered {trayColor} tray with PieceTrayManager");
        }
        else
        {
            Debug.LogWarning($"⚠️ PieceTray: PieceTrayManager.Instance is null - cannot register {trayColor} tray");
            Debug.LogWarning($"   This may cause tray lookup inconsistencies between different code paths");
        }

        // Now set up the tray visuals and positions
        Debug.Log($"PieceTray.Initialize: About to call SetupTray for {trayColor}");
        SetupTray();
        
        Debug.Log($"✅ PieceTray.Initialize: Completed initialization for {trayColor} tray");
    }
    
    /// <summary>
    /// Set up the tray's visual elements and piece positions
    /// </summary>
    private void SetupTray()
    {
        Debug.Log($"PieceTray: Setting up {trayColor} tray on {(isLeftSide ? "left" : "right")} side");
        
        // Calculate tray position relative to board
        CalculateTrayPosition();
        
        // Create visual tray base
        CreateTrayBase();
        
        // Calculate positions for pieces in the tray
        CalculatePiecePositions();
        
        Debug.Log($"PieceTray: {trayColor} tray setup complete at {transform.position}");
    }
    
    /// <summary>
    /// Position the tray beside the board with proper coordinate system alignment
    /// Dynamically scales tray position based on board size (4x4x4, 6x6x6, or 8x8x8)
    /// Supports 2-player, 4-player, and 6-player layouts
    /// 6-player layout: Orange=top, Yellow=bottom, White=left, Black=right, Green=front, Purple=back
    /// </summary>
    private void CalculateTrayPosition()
    {
        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4

        Debug.Log($"PieceTray: Calculating tray position for {boardDims.x}x{boardDims.y}x{boardDims.z} board");
        Debug.Log($"PieceTray: This is {trayColor} tray");

        // Calculate board bounds dynamically based on actual board size
        float cellSize = 2.8f;
        float halfBoardSize = (boardDims.x * cellSize) / 2f;
        float boardLeftEdge = -halfBoardSize;   // X minimum
        float boardRightEdge = halfBoardSize;   // X maximum
        float boardFrontEdge = -halfBoardSize;  // Z minimum
        float boardBackEdge = halfBoardSize;    // Z maximum
        float boardBottomEdge = -halfBoardSize; // Y minimum
        float boardTopEdge = halfBoardSize;     // Y maximum

        Debug.Log($"PieceTray: Board edges - X:[{boardLeftEdge},{boardRightEdge}] Y:[{boardBottomEdge},{boardTopEdge}] Z:[{boardFrontEdge},{boardBackEdge}]");

        // Tray spacing: 6 units from board edge
        float traySpacing = 6.0f;

        // Default tray height (for left/right trays)
        float trayHeight = -1.0f;

        Vector3 trayPosition;

        // Position trays based on player color
        switch (trayColor)
        {
            case PieceColor.White:
                // Left side (negative X)
                trayPosition = new Vector3(boardLeftEdge - traySpacing, trayHeight, 0f);
                Debug.Log($"PieceTray: White tray positioned on LEFT at {trayPosition}");
                break;

            case PieceColor.Black:
                // Right side (positive X)
                trayPosition = new Vector3(boardRightEdge + traySpacing, trayHeight, 0f);
                Debug.Log($"PieceTray: Black tray positioned on RIGHT at {trayPosition}");
                break;

            case PieceColor.Green:
                // Front side (negative Z)
                trayPosition = new Vector3(0f, trayHeight, boardFrontEdge - traySpacing);
                Debug.Log($"PieceTray: Green tray positioned on FRONT at {trayPosition}");
                break;

            case PieceColor.Purple:
                // Back side (positive Z)
                trayPosition = new Vector3(0f, trayHeight, boardBackEdge + traySpacing);
                Debug.Log($"PieceTray: Purple tray positioned on BACK at {trayPosition}");
                break;

            case PieceColor.Yellow:
                // Bottom (negative Y)
                trayPosition = new Vector3(0f, boardBottomEdge - traySpacing, 0f);
                Debug.Log($"PieceTray: Yellow tray positioned on BOTTOM at {trayPosition}");
                break;

            case PieceColor.Orange:
                // Top (positive Y)
                trayPosition = new Vector3(0f, boardTopEdge + traySpacing, 0f);
                Debug.Log($"PieceTray: Orange tray positioned on TOP at {trayPosition}");
                break;

            default:
                Debug.LogError($"PieceTray: Unknown color {trayColor}, defaulting to origin");
                trayPosition = Vector3.zero;
                break;
        }
        
        // Find the Board Rotator to ensure trays are children and rotate with the board
        GameObject boardRotator = GameObject.Find("Board Rotator");
        if (boardRotator != null)
        {
            Debug.Log($"PieceTray: Found Board Rotator at position {boardRotator.transform.position}");
            Debug.Log($"PieceTray: Board Rotator rotation: {boardRotator.transform.rotation}");
            Debug.Log($"PieceTray: Board Rotator scale: {boardRotator.transform.localScale}");
            
            transform.SetParent(boardRotator.transform);
            transform.localPosition = trayPosition;
            
            // SAFETY CHECK: Verify parenting worked correctly
            Vector3 finalWorldPos = transform.position;
            Debug.Log($"PieceTray: {trayColor} tray parented to Board Rotator and positioned at local {trayPosition}");
            Debug.Log($"PieceTray: {trayColor} tray final world position after parenting: {finalWorldPos}");
            
            // Validate that world position is reasonable
            if (finalWorldPos.magnitude > 100f || float.IsNaN(finalWorldPos.x))
            {
                Debug.LogError($"🚨 PieceTray: {trayColor} tray has INVALID final world position: {finalWorldPos}");
                Debug.LogError($"    Local position: {transform.localPosition}");
                Debug.LogError($"    Board Rotator position: {boardRotator.transform.position}");
            }
        }
        else
        {
            // Fallback to world positioning if Board Rotator not found
            transform.position = trayPosition;
            Debug.LogWarning($"PieceTray: Board Rotator not found, using world position {trayPosition} for {trayColor} tray");
        }
        
        Debug.Log($"PieceTray: {trayColor} tray CORRECTED position - world: {transform.position}, local: {transform.localPosition}");
        Debug.Log($"PieceTray: Board world bounds X[{boardLeftEdge} to {boardRightEdge}], trays at X={trayPosition.x}");
        Debug.Log($"PieceTray: Symmetric distances - White: {Mathf.Abs(trayPosition.x)}, Black: {Mathf.Abs(trayPosition.x)} (should be equal when both trays created)");
    }
    
    /// <summary>
    /// Create the visual base of the tray
    /// </summary>
    private void CreateTrayBase()
    {
        trayBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trayBase.name = $"{trayColor} Tray Base";
        trayBase.transform.SetParent(transform);
        trayBase.transform.localPosition = Vector3.zero;
        
        // Size the tray to hold all pieces
        float trayWidth = 4f;
        float trayHeight = 0.2f;
        float trayDepth = (maxPieces * pieceSpacing) + 2f;
        
        trayBase.transform.localScale = new Vector3(trayWidth, trayHeight, trayDepth);

        // Rotate trays that are positioned on front/back sides
        if (trayColor == PieceColor.Green || trayColor == PieceColor.Purple)
        {
            // Rotate 90 degrees around Y-axis so tray extends along X-axis instead of Z-axis
            trayBase.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Debug.Log($"PieceTray: Rotated {trayColor} tray base 90° for front/back positioning");
        }

        // Apply tray material
        Renderer renderer = trayBase.GetComponent<Renderer>();
        if (trayMaterial == null)
        {
            trayMaterial = CreateTrayMaterial();
        }
        renderer.material = trayMaterial;
        
        Debug.Log($"PieceTray: Created tray base with size {trayBase.transform.localScale}");
    }
    
    /// <summary>
    /// Create a material for the tray
    /// </summary>
    private Material CreateTrayMaterial()
    {
        // Try URP Lit shader first, fallback to Standard if not available
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
            Debug.LogWarning("PieceTray: URP Lit shader not found, using Standard shader");
        }

        // If no shader found, use default material
        if (shader == null)
        {
            Debug.LogError("PieceTray: No valid shader found, using default material");
            return new Material(Shader.Find("Diffuse"));
        }

        Material material = new Material(shader);

        // Different colors for different trays
        switch (trayColor)
        {
            case PieceColor.White:
                material.color = NeonTheme.Lavender; // neon style: soft lavender for White
                break;
            case PieceColor.Black:
                material.color = NeonTheme.Hex("5A3A8C"); // neon style: deep violet for Black (NeonTheme.Muted, which High Contrast lightens)
                break;
            case PieceColor.Green:
                material.color = new Color(0.15f, 0.5f, 0.15f, 0.8f); // Darker forest green
                break;
            case PieceColor.Purple:
                material.color = new Color(0.45f, 0.15f, 0.6f, 0.8f); // Darker purple
                break;
            case PieceColor.Yellow:
                material.color = new Color(0.9f, 0.9f, 0.2f, 0.8f); // Yellow
                break;
            case PieceColor.Orange:
                material.color = new Color(1.0f, 0.5f, 0.0f, 0.8f); // Orange
                break;
            default:
                material.color = new Color(0.5f, 0.5f, 0.5f, 0.8f); // Default gray
                break;
        }

        Debug.Log($"PieceTray: Created material with color {material.color} using shader {shader.name}");

        return material;
    }
    
    /// <summary>
    /// Calculate positions where pieces should be placed in the tray
    /// </summary>
    private void CalculatePiecePositions()
    {
        piecePositions.Clear();

        // Determine which axis to arrange pieces along based on tray orientation
        bool arrangeAlongX = (trayColor == PieceColor.Green || trayColor == PieceColor.Purple);

        // Arrange pieces in a line along the appropriate axis
        float startOffset = -(maxPieces - 1) * pieceSpacing * 0.5f;

        Debug.Log($"PieceTray: Calculating piece positions for {trayColor} tray:");
        Debug.Log($"  maxPieces: {maxPieces}, pieceSpacing: {pieceSpacing}");
        Debug.Log($"  Arranging along: {(arrangeAlongX ? "X-axis" : "Z-axis")}");
        Debug.Log($"  startOffset: {startOffset}");

        for (int i = 0; i < maxPieces; i++)
        {
            Vector3 localPos;
            if (arrangeAlongX)
            {
                // Green/Purple trays: arrange pieces along X-axis
                localPos = new Vector3(startOffset + (i * pieceSpacing), 1f, 0f);
            }
            else
            {
                // White/Black/Yellow/Orange trays: arrange pieces along Z-axis
                localPos = new Vector3(0f, 1f, startOffset + (i * pieceSpacing));
            }

            piecePositions.Add(localPos);
            Debug.Log($"  Slot {i}: local position {localPos}");
        }

        Debug.Log($"PieceTray: Calculated {piecePositions.Count} piece positions for {trayColor} tray");
    }
    
    /// <summary>
    /// Add a piece to this tray
    /// </summary>
    /// <summary>Put a piece back into the first empty bench slot (e.g. taken back off the board)</summary>
    public bool ReturnPiece(ChessPiece piece)
    {
        if (piece == null || piece.pieceColor != trayColor) return false;
        piecesInTray.RemoveAll(p => p == null || p.transform.parent != transform);
        for (int i = 0; i < piecePositions.Count; i++)
        {
            bool taken = false;
            foreach (ChessPiece p in piecesInTray)
                if (Vector3.Distance(p.transform.localPosition, piecePositions[i]) < 0.1f) { taken = true; break; }
            if (taken) continue;
            piece.transform.SetParent(transform);
            piece.transform.localPosition = piecePositions[i];
            piecesInTray.Add(piece);
            return true;
        }
        return AddPiece(piece);
    }

    public bool AddPiece(ChessPiece piece)
    {
        if (piecesInTray.Count >= maxPieces)
        {
            Debug.LogWarning($"PieceTray: {trayColor} tray is full! Cannot add piece.");
            return false;
        }
        
        if (piece.pieceColor != trayColor)
        {
            Debug.LogError($"PieceTray: Cannot add {piece.pieceColor} piece to {trayColor} tray");
            return false;
        }
        
        // Position the piece in the tray
        int slotIndex = piecesInTray.Count;
        
        // DIAGNOSTIC: Log BEFORE positioning
        Vector3 pieceOldPosition = piece.transform.position;
        Debug.Log($"🔍 BEFORE AddPiece positioning - {piece.pieceColor} {piece.pieceType}:");
        Debug.Log($"    Old piece position: {pieceOldPosition}");
        Debug.Log($"    Tray position: {transform.position}");
        Debug.Log($"    Target slot {slotIndex} local pos: {piecePositions[slotIndex]}");
        
        // SAFETY CHECK: Validate tray transform before calculating world position
        if (transform.position.magnitude > 1000f || float.IsNaN(transform.position.x) || float.IsInfinity(transform.position.x))
        {
            Debug.LogError($"🚨 PieceTray: INVALID TRAY TRANSFORM DETECTED for {trayColor} tray!");
            Debug.LogError($"    Tray position: {transform.position}");
            Debug.LogError($"    Tray local position: {transform.localPosition}");
            Debug.LogError($"    Tray parent: {(transform.parent != null ? transform.parent.name : "None")}");
            return false;
        }
        
        Vector3 worldPosition = transform.TransformPoint(piecePositions[slotIndex]);
        Debug.Log($"    Calculated world position: {worldPosition}");
        
        // SAFETY CHECK: Validate calculated world position  
        if (worldPosition.magnitude > 1000f || float.IsNaN(worldPosition.x) || float.IsInfinity(worldPosition.x))
        {
            Debug.LogError($"🚨 PieceTray: INVALID WORLD POSITION CALCULATED for {trayColor} tray!");
            Debug.LogError($"    Bad world position: {worldPosition}");
            Debug.LogError($"    Tray position: {transform.position}");
            Debug.LogError($"    Local slot position: {piecePositions[slotIndex]}");
            return false;
        }
        
        piece.transform.position = worldPosition;
        piece.transform.SetParent(transform);
        
        piecesInTray.Add(piece);
        
        Debug.Log($"🎯 PieceTray: Added {piece.pieceColor} {piece.pieceType} to {trayColor} tray at slot {slotIndex}");
        Debug.Log($"  Tray world position: {transform.position}");
        Debug.Log($"  Tray local position: {transform.localPosition}");
        Debug.Log($"  Piece local position in tray: {piecePositions[slotIndex]}");
        Debug.Log($"  Piece final world position: {worldPosition}");
        Debug.Log($"  Piece final transform position: {piece.transform.position}");
        Debug.Log($"  Tray parent: {(transform.parent != null ? transform.parent.name : "None")}");
        return true;
    }
    
    /// <summary>
    /// Remove a piece from this tray
    /// ROBUST VERSION: Handles list/GameObject hierarchy desync issues
    /// </summary>
    public bool RemovePiece(ChessPiece piece)
    {
        Debug.Log($"🔍 PieceTray.RemovePiece: Called for {piece.pieceColor} {piece.pieceType} on {trayColor} tray");
        Debug.Log($"🔍   piecesInTray.Count = {piecesInTray.Count}");
        Debug.Log($"🔍   piece.GetInstanceID() = {piece.GetInstanceID()}");

        bool foundInList = piecesInTray.Contains(piece);
        bool foundInHierarchy = (piece.transform.parent == this.transform);

        Debug.Log($"🔍   foundInList: {foundInList}");
        Debug.Log($"🔍   foundInHierarchy: {foundInHierarchy}");

        // Log all pieces in list for debugging
        Debug.Log($"🔍   Pieces in piecesInTray list:");
        for (int i = 0; i < piecesInTray.Count; i++)
        {
            if (piecesInTray[i] != null)
            {
                Debug.Log($"🔍     [{i}] {piecesInTray[i].pieceColor} {piecesInTray[i].pieceType} (ID: {piecesInTray[i].GetInstanceID()})");
            }
            else
            {
                Debug.Log($"🔍     [{i}] NULL");
            }
        }

        // Check if piece exists as child GameObject (even if not in list)
        ChessPiece[] childPieces = GetComponentsInChildren<ChessPiece>();
        Debug.Log($"🔍   Found {childPieces.Length} child pieces in GameObject hierarchy");

        bool foundAsChild = false;
        for (int i = 0; i < childPieces.Length; i++)
        {
            if (childPieces[i] == piece)
            {
                foundAsChild = true;
                Debug.Log($"🔍   Piece found in hierarchy at child index {i}");
                break;
            }
        }

        if (!foundInList && !foundInHierarchy && !foundAsChild)
        {
            Debug.LogWarning($"⚠️ PieceTray.RemovePiece: {piece.pieceColor} {piece.pieceType} not found in {trayColor} tray (neither list nor hierarchy)");
            return false;
        }

        // DESYNC DETECTED: Piece is in hierarchy but not in list
        if (!foundInList && (foundInHierarchy || foundAsChild))
        {
            Debug.LogError($"🚨 DESYNC DETECTED: {piece.pieceColor} {piece.pieceType} is in {trayColor} tray hierarchy but NOT in piecesInTray list!");
            Debug.LogError($"🚨 This is the bug causing Green/Purple to think they have no pieces left!");
            Debug.LogError($"🚨 Attempting to remove from hierarchy anyway...");

            // Try to find a matching piece in the list by color/type
            ChessPiece matchingPiece = null;
            for (int i = 0; i < piecesInTray.Count; i++)
            {
                if (piecesInTray[i] != null &&
                    piecesInTray[i].pieceColor == piece.pieceColor &&
                    piecesInTray[i].pieceType == piece.pieceType)
                {
                    matchingPiece = piecesInTray[i];
                    Debug.Log($"🔍 Found matching piece in list at index {i}: {matchingPiece.pieceColor} {matchingPiece.pieceType} (ID: {matchingPiece.GetInstanceID()})");
                    break;
                }
            }

            if (matchingPiece != null && matchingPiece != piece)
            {
                Debug.LogError($"🚨 Found different instance of same piece type in list!");
                Debug.LogError($"🚨   Requested piece ID: {piece.GetInstanceID()}");
                Debug.LogError($"🚨   List piece ID: {matchingPiece.GetInstanceID()}");
                Debug.LogError($"🚨 This suggests duplicate piece instances - removing both to be safe");

                piecesInTray.Remove(matchingPiece);
            }
        }

        // Remove from list if present (safe even if already checked)
        if (foundInList)
        {
            piecesInTray.Remove(piece);
            Debug.Log($"✅ Removed {piece.pieceColor} {piece.pieceType} from piecesInTray list");
        }

        // ALWAYS remove from GameObject hierarchy if present
        if (piece.transform.parent == this.transform)
        {
            piece.transform.SetParent(null); // Remove from tray hierarchy
            Debug.Log($"✅ Removed {piece.pieceColor} {piece.pieceType} from tray transform hierarchy");
        }

        // Verify removal succeeded
        Debug.Log($"🔍 After removal: piecesInTray.Count = {piecesInTray.Count}");
        Debug.Log($"🔍 After removal: GetComponentsInChildren<ChessPiece>().Length = {GetComponentsInChildren<ChessPiece>().Length}");

        // Attempt to reorganize remaining pieces (will be skipped if reorganization is disabled)
        Debug.Log($"🔄 PieceTray: Attempting to reorganize {trayColor} tray after removal (enableReorganization: {enableReorganization})");
        ReorganizePieces();

        Debug.Log($"✅ PieceTray: Successfully removed {piece.pieceColor} {piece.pieceType} from {trayColor} tray");
        return true;
    }
    
    /// <summary>
    /// Reorganize pieces in the tray after removal
    /// </summary>
    private void ReorganizePieces()
    {
        Debug.Log($"🔄 PieceTray.ReorganizePieces: CALLED for {trayColor} tray with {piecesInTray.Count} pieces");
        Debug.Log($"🔄 Tray transform position: {transform.position}");
        Debug.Log($"🔄 Tray transform local position: {transform.localPosition}");
        
        // CONTROL FLAG: Check if reorganization is disabled
        if (!enableReorganization)
        {
            Debug.Log($"🔄 ⚠️ ReorganizePieces: Skipping reorganization for {trayColor} tray - reorganization is disabled");
            Debug.Log($"🔄 This prevents unwanted piece shifting during placement phase");
            return;
        }
        
        // GUARD: Prevent reorganization during piece selection to avoid moving pieces during UI interaction
        if (PlacementManager.Instance != null && PlacementManager.Instance.HasSelectedTrayPiece)
        {
            Debug.Log($"🔄 ⚠️ ReorganizePieces: Skipping reorganization for {trayColor} tray - piece selection in progress");
            return;
        }
        
        // SAFETY CHECK: Validate tray transform before reorganizing
        if (transform.position.magnitude > 1000f || float.IsNaN(transform.position.x) || float.IsInfinity(transform.position.x))
        {
            Debug.LogError($"🚨 ReorganizePieces: INVALID TRAY TRANSFORM for {trayColor} tray - aborting reorganization!");
            Debug.LogError($"    Tray position: {transform.position}");
            Debug.LogError($"    Tray local position: {transform.localPosition}");
            return;
        }
        
        for (int i = 0; i < piecesInTray.Count; i++)
        {
            Vector3 oldPosition = piecesInTray[i].transform.position;
            Vector3 worldPosition = transform.TransformPoint(piecePositions[i]);
            
            // SAFETY CHECK: Validate calculated position
            if (worldPosition.magnitude > 1000f || float.IsNaN(worldPosition.x) || float.IsInfinity(worldPosition.x))
            {
                Debug.LogError($"🚨 ReorganizePieces: INVALID WORLD POSITION for {piecesInTray[i].pieceColor} {piecesInTray[i].pieceType} - skipping!");
                Debug.LogError($"    Bad world position: {worldPosition}");
                Debug.LogError($"    Tray position: {transform.position}");
                Debug.LogError($"    Local position: {piecePositions[i]}");
                continue;
            }
            
            piecesInTray[i].transform.position = worldPosition;
            
            Debug.Log($"🔄 Moved {piecesInTray[i].pieceColor} {piecesInTray[i].pieceType}:");
            Debug.Log($"    From: {oldPosition}");
            Debug.Log($"    To: {worldPosition}");
            Debug.Log($"    Local tray pos: {piecePositions[i]}");
        }
        
        Debug.Log($"🔄 PieceTray.ReorganizePieces: COMPLETED for {trayColor} tray");
    }
    
    /// <summary>
    /// Check if this tray is empty
    /// </summary>
    public bool IsEmpty()
    {
        return piecesInTray.Count == 0;
    }
    
    /// <summary>
    /// Check if this tray is full
    /// </summary>
    public bool IsFull()
    {
        return piecesInTray.Count >= maxPieces;
    }
    
    /// <summary>
    /// Get the number of pieces in this tray
    /// </summary>
    public int GetPieceCount()
    {
        return piecesInTray.Count;
    }
    
    /// <summary>
    /// Get all pieces in this tray
    /// </summary>
    public List<ChessPiece> GetPieces()
    {
        return new List<ChessPiece>(piecesInTray);
    }
    
    /// <summary>
    /// Set the visibility of this tray and all its pieces
    /// </summary>
    public void SetTrayVisibility(bool visible)
    {
        gameObject.SetActive(visible);
        Debug.Log($"PieceTray: {trayColor} tray visibility set to {visible}");
    }
    
    /// <summary>
    /// Hide this tray from view
    /// </summary>
    public void HideTray()
    {
        SetTrayVisibility(false);
    }

    /// <summary>
    /// Show this tray
    /// </summary>
    public void ShowTray()
    {
        SetTrayVisibility(true);
    }

    /// <summary>
    /// Get the tray for a specific player color
    /// CRITICAL FIX: Uses PieceTrayManager as single source of truth to prevent desync
    /// </summary>
    public static PieceTray GetTrayForColor(PieceColor color)
    {
        // CRITICAL FIX: Use PieceTrayManager as the single source of truth
        // This prevents desync between static properties and PieceTrayManager dictionary
        // Both AIPlayer (via PieceTrayManager.GetTray) and PlacementManager (via this method)
        // will now access the same tray instance, fixing the bug where Purple/Green AI
        // fail to place their last pieces because they see different tray piece counts
        if (PieceTrayManager.Instance != null)
        {
            PieceTray tray = PieceTrayManager.Instance.GetTray(color);
            if (tray != null)
            {
                return tray;
            }
        }

        // Fallback to static properties for backward compatibility
        // (e.g., if PieceTrayManager hasn't been created yet)
        return color switch
        {
            PieceColor.White => WhiteTray,
            PieceColor.Black => BlackTray,
            PieceColor.Green => GreenTray,
            PieceColor.Purple => PurpleTray,
            PieceColor.Yellow => YellowTray,
            PieceColor.Orange => OrangeTray,
            _ => null
        };
    }
    
    /// <summary>
    /// Hide all trays from view
    /// </summary>
    public static void HideAllTrays()
    {
        if (WhiteTray != null) WhiteTray.HideTray();
        if (BlackTray != null) BlackTray.HideTray();
        if (GreenTray != null) GreenTray.HideTray();
        if (PurpleTray != null) PurpleTray.HideTray();
        if (YellowTray != null) YellowTray.HideTray();
        if (OrangeTray != null) OrangeTray.HideTray();
        Debug.Log("PieceTray: All trays hidden");
    }

    /// <summary>
    /// Show all trays
    /// </summary>
    public static void ShowAllTrays()
    {
        if (WhiteTray != null) WhiteTray.ShowTray();
        if (BlackTray != null) BlackTray.ShowTray();
        if (GreenTray != null) GreenTray.ShowTray();
        if (PurpleTray != null) PurpleTray.ShowTray();
        if (YellowTray != null) YellowTray.ShowTray();
        if (OrangeTray != null) OrangeTray.ShowTray();
        Debug.Log("PieceTray: All trays shown");
    }

    /// <summary>
    /// Set visibility of all trays
    /// </summary>
    public static void SetAllTraysVisible(bool visible)
    {
        if (visible)
            ShowAllTrays();
        else
            HideAllTrays();
    }
    
    /// <summary>
    /// Emergency recovery method to find and fix pieces that are in invalid positions
    /// </summary>
    public static void ValidateAndRecoverAllTrayPieces()
    {
        Debug.Log("🚨 PieceTray.ValidateAndRecoverAllTrayPieces: Starting emergency piece recovery");

        if (WhiteTray != null) WhiteTray.ValidateAndRecoverTrayPieces();
        if (BlackTray != null) BlackTray.ValidateAndRecoverTrayPieces();
        if (GreenTray != null) GreenTray.ValidateAndRecoverTrayPieces();
        if (PurpleTray != null) PurpleTray.ValidateAndRecoverTrayPieces();
        if (YellowTray != null) YellowTray.ValidateAndRecoverTrayPieces();
        if (OrangeTray != null) OrangeTray.ValidateAndRecoverTrayPieces();

        Debug.Log("🚨 PieceTray.ValidateAndRecoverAllTrayPieces: Emergency recovery completed");
    }
    
    /// <summary>
    /// Validate and recover pieces in this specific tray
    /// </summary>
    private void ValidateAndRecoverTrayPieces()
    {
        Debug.Log($"🔧 PieceTray.ValidateAndRecoverTrayPieces: Checking {trayColor} tray");
        
        for (int i = 0; i < piecesInTray.Count; i++)
        {
            ChessPiece piece = piecesInTray[i];
            if (piece == null) continue;
            
            Vector3 currentPos = piece.transform.position;
            Vector3 expectedPos = transform.TransformPoint(piecePositions[i]);
            
            // Check if piece is too far from expected position
            float distance = Vector3.Distance(currentPos, expectedPos);
            if (distance > 5f) // 5 units tolerance
            {
                Debug.LogWarning($"🔧 Found misplaced {piece.pieceColor} {piece.pieceType} in {trayColor} tray:");
                Debug.LogWarning($"    Current position: {currentPos}");
                Debug.LogWarning($"    Expected position: {expectedPos}");
                Debug.LogWarning($"    Distance: {distance}");
                
                // GUARD: Don't move pieces during piece selection to prevent unwanted movement
                if (PlacementManager.Instance != null && PlacementManager.Instance.HasSelectedTrayPiece)
                {
                    Debug.Log($"🔄 ⚠️ ValidateAndRecoverTrayPieces: Skipping recovery - piece selection in progress");
                    Debug.Log($"    Piece will be recovered after selection completes if still needed");
                    continue;
                }
                
                // Recover the piece to correct position
                piece.transform.position = expectedPos;
                piece.transform.SetParent(transform);
                
                Debug.Log($"✅ Recovered {piece.pieceColor} {piece.pieceType} to correct position {expectedPos}");
            }
            
            // Check if piece has correct parent
            if (piece.transform.parent != transform)
            {
                Debug.LogWarning($"🔧 {piece.pieceColor} {piece.pieceType} has incorrect parent: {(piece.transform.parent != null ? piece.transform.parent.name : "None")}");
                piece.transform.SetParent(transform);
                Debug.Log($"✅ Fixed parent for {piece.pieceColor} {piece.pieceType}");
            }
        }
        
        Debug.Log($"🔧 PieceTray.ValidateAndRecoverTrayPieces: Completed validation for {trayColor} tray");
    }
    
    /// <summary>
    /// Manually trigger reorganization of pieces in the tray (ignores enableReorganization flag)
    /// </summary>
    public void ForceReorganizePieces()
    {
        Debug.Log($"🔄 PieceTray.ForceReorganizePieces: Manually triggered for {trayColor} tray (ignoring enableReorganization flag)");
        
        bool originalSetting = enableReorganization;
        enableReorganization = true; // Temporarily enable
        
        try
        {
            ReorganizePieces();
        }
        finally
        {
            enableReorganization = originalSetting; // Restore original setting
        }
        
        Debug.Log($"🔄 PieceTray.ForceReorganizePieces: Completed for {trayColor} tray");
    }
    
    /// <summary>
    /// Enable or disable automatic reorganization for all trays
    /// </summary>
    public static void SetReorganizationEnabled(bool enabled)
    {
        Debug.Log($"🔄 PieceTray.SetReorganizationEnabled: Setting reorganization to {enabled} for all trays");

        if (WhiteTray != null) WhiteTray.enableReorganization = enabled;
        if (BlackTray != null) BlackTray.enableReorganization = enabled;
        if (GreenTray != null) GreenTray.enableReorganization = enabled;
        if (PurpleTray != null) PurpleTray.enableReorganization = enabled;
        if (YellowTray != null) YellowTray.enableReorganization = enabled;
        if (OrangeTray != null) OrangeTray.enableReorganization = enabled;

        Debug.Log($"🔄 Reorganization set to {enabled} for all active trays");
    }

    /// <summary>
    /// Force reorganization of pieces in all trays
    /// </summary>
    public static void ForceReorganizeAllTrays()
    {
        Debug.Log($"🔄 PieceTray.ForceReorganizeAllTrays: Manually reorganizing all trays");

        if (WhiteTray != null) WhiteTray.ForceReorganizePieces();
        if (BlackTray != null) BlackTray.ForceReorganizePieces();
        if (GreenTray != null) GreenTray.ForceReorganizePieces();
        if (PurpleTray != null) PurpleTray.ForceReorganizePieces();
        if (YellowTray != null) YellowTray.ForceReorganizePieces();
        if (OrangeTray != null) OrangeTray.ForceReorganizePieces();

        Debug.Log($"🔄 PieceTray.ForceReorganizeAllTrays: Completed");
    }
}