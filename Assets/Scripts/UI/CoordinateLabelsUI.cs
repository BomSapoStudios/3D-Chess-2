using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Displays chess notation coordinate labels along the edges of the 3D chess board.
/// Shows file labels (A-H), level labels (1-8), and rank labels (a-h) at 25% opacity.
/// Labels are positioned in world space and billboard to face the camera.
/// </summary>
public class CoordinateLabelsUI : MonoBehaviour
{
    [Header("Label Settings")]
    public bool enableLabels = true;
    public int fontSize = 18;
    public float labelOpacity = 0.35f; // see-through, but readable over the backdrops
    public Color labelColor = new Color(0.2f, 0.2f, 0.2f); // Dark grey
    public float labelDistance = 2.0f; // Distance from board edge

    [Header("Label Positioning")]
    public float fileLabelsYOffset = -0.5f; // Y offset for file labels (matches rank labels)
    public float levelLabelsYOffset = 1.4f; // Y offset for level labels (centers in cells)
    public float rankLabelsYOffset = -0.5f; // Y offset for rank labels (right edge)

    // Label containers
    private GameObject labelContainer;
    private List<GameObject> fileLabels = new List<GameObject>();   // A-H (X-axis, left edge)
    private List<GameObject> levelLabels = new List<GameObject>();  // 1-8 (Y-axis, bottom edge)
    private List<GameObject> rankLabels = new List<GameObject>();   // a-h (Z-axis, right edge)

    // Camera reference for billboarding
    private Camera mainCamera;

    public static CoordinateLabelsUI Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("CoordinateLabelsUI: Instance created");
        }
        else
        {
            Debug.LogWarning("CoordinateLabelsUI: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("CoordinateLabelsUI: Main camera not found!");
            return;
        }

        if (enableLabels)
        {
            StartCoroutine(CreateWhenBoardReady());
        }
    }

    // The board (and its "Pieces Container") is built a frame or two after Start;
    // labels placed before then landed in the wrong spot
    private System.Collections.IEnumerator CreateWhenBoardReady()
    {
        float waited = 0f;
        while ((ChessBoard.Instance == null || GameObject.Find("Pieces Container") == null) && waited < 5f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        CreateCoordinateLabels();
    }

    private void LateUpdate()
    {
        // Billboard labels to always face camera
        if (enableLabels && mainCamera != null)
        {
            BillboardLabels();
        }
    }

    /// <summary>
    /// Create all coordinate labels around the board edges
    /// </summary>
    private void CreateCoordinateLabels()
    {
        Debug.Log("CoordinateLabelsUI: Creating coordinate labels");

        // Create parent container
        labelContainer = new GameObject("CoordinateLabels");
        GameObject piecesContainer = GameObject.Find("Pieces Container");
        // Live in the board's own space so labels stay lined up when the board turns
        labelContainer.transform.SetParent(piecesContainer != null ? piecesContainer.transform : transform, false);
        labelContainer.transform.localPosition = Vector3.zero;
        labelContainer.transform.localRotation = Quaternion.identity;

        // Get board dimensions
        Vector3Int boardDims = GetBoardDimensions();
        Debug.Log($"CoordinateLabelsUI: Board dimensions: {boardDims.x}x{boardDims.y}x{boardDims.z}");

        // Create file labels (A-H) along left edge (X-axis)
        CreateFileLabels(boardDims);

        // Create level labels (1-8) along bottom edge (Y-axis)
        CreateLevelLabels(boardDims);

        // Create rank labels (a-h) along right edge (Z-axis)
        CreateRankLabels(boardDims);

        Debug.Log($"CoordinateLabelsUI: Created {fileLabels.Count} file labels, {levelLabels.Count} level labels, {rankLabels.Count} rank labels");
    }

    // Board-local geometry: cell centres sit at index * cell, floors at y * cell - half
    private const float Cell = 2.8f;
    private const float Half = Cell / 2f;
    private const float Margin = 0.7f; // gap between the board edge and a label

    /// <summary>
    /// Files (A, B, C, D… uppercase) along the bottom front edge, one under each column
    /// </summary>
    private void CreateFileLabels(Vector3Int boardDims)
    {
        string[] labels = ChessNotationConverter.GetFileLabels();
        for (int x = 0; x < boardDims.x; x++)
        {
            string text = labels[x].ToUpperInvariant();
            Vector3 local = new Vector3(x * Cell, -Half, -Half - Margin); // files stay along the bottom front edge, tipped or not
            GameObject label = CreateLabel($"FileLabel_{text}", text, local, display: true);
            fileLabels.Add(label);
        }
    }

    /// <summary>
    /// Levels (1, 2, 3…) up the front left vertical edge, level with each floor's middle
    /// </summary>
    private void CreateLevelLabels(Vector3Int boardDims)
    {
        string[] labels = ChessNotationConverter.GetLevelLabels();
        for (int y = 0; y < boardDims.y; y++)
        {
            Vector3 local = BoardView.Tilted
                ? new Vector3(-Half - Margin, -Half, BoardView.ToView(new BoardPosition(0, y, 0)).z * Cell) // tipped (BoardView): floors run into the depth, along the bottom left edge
                : new Vector3(-Half - Margin, y * Cell, -Half - Margin);
            GameObject label = CreateLabel($"LevelLabel_{labels[y]}", labels[y], local, display: true);
            levelLabels.Add(label);
        }
    }

    /// <summary>
    /// Ranks (a, b, c, d… lowercase) along the bottom left edge, one beside each row
    /// </summary>
    private void CreateRankLabels(Vector3Int boardDims)
    {
        string[] labels = ChessNotationConverter.GetRankLabels();
        for (int z = 0; z < boardDims.z; z++)
        {
            string text = labels[z].ToLowerInvariant();
            Vector3 local = BoardView.Tilted
                ? new Vector3(-Half - Margin, BoardView.ToView(new BoardPosition(0, 0, z)).y * Cell, -Half - Margin) // tipped (BoardView): ranks run up the front left edge
                : new Vector3(-Half - Margin, -Half, z * Cell);
            // Bungee has no lowercase letters, so the ranks use the rounded body font
            GameObject label = CreateLabel($"RankLabel_{text}", text, local, display: false);
            rankLabels.Add(label);
        }
    }

    /// <summary>
    /// Create a single text label
    /// </summary>
    private GameObject CreateLabel(string name, string text, Vector3 localPosition, bool display)
    {
        GameObject labelObj = new GameObject(name);
        labelObj.transform.SetParent(labelContainer.transform, false);
        labelObj.transform.localPosition = localPosition;

        // Add TextMeshPro component
        TextMeshPro textMesh = labelObj.AddComponent<TextMeshPro>();
        textMesh.text = text;
        textMesh.fontSize = fontSize;
        textMesh.alignment = TextAlignmentOptions.Center;

        // Neon style: the game's display font in cyan, still see-through so it never hides pieces
        Color colorWithOpacity = NeonTheme.Cyan;
        colorWithOpacity.a = labelOpacity;
        textMesh.color = colorWithOpacity;

        TMP_FontAsset neonFont = display ? NeonTheme.DisplayTMP : NeonTheme.BodyTMP;
        if (neonFont != null)
        {
            textMesh.font = neonFont;
        }
        else
        {
            textMesh.font = Resources.Load<TMP_FontAsset>("LiberationSans SDF");
        }

        // Configure for world space
        RectTransform rectTransform = labelObj.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.sizeDelta = new Vector2(2, 2);
        }

        return labelObj;
    }

    /// <summary>
    /// Billboard all labels to face the camera
    /// </summary>
    private void BillboardLabels()
    {
        if (labelContainer == null || mainCamera == null) return;

        // Billboard all file labels
        foreach (GameObject label in fileLabels)
        {
            if (label != null)
            {
                label.transform.LookAt(mainCamera.transform);
                label.transform.Rotate(0, 180, 0); // Flip to face camera correctly
            }
        }

        // Billboard all level labels
        foreach (GameObject label in levelLabels)
        {
            if (label != null)
            {
                label.transform.LookAt(mainCamera.transform);
                label.transform.Rotate(0, 180, 0);
            }
        }

        // Billboard all rank labels
        foreach (GameObject label in rankLabels)
        {
            if (label != null)
            {
                label.transform.LookAt(mainCamera.transform);
                label.transform.Rotate(0, 180, 0);
            }
        }
    }

    /// <summary>
    /// Get world position for a board position (used for label placement)
    /// </summary>
    private Vector3 GetWorldPositionForLabel(BoardPosition pos)
    {
        if (ChessBoard.Instance != null)
        {
            // Get local position from ChessBoard
            Vector3 localPos = ChessBoard.Instance.BoardToLocalPosition(pos);

            // Find Pieces Container to convert to world space
            GameObject piecesContainer = GameObject.Find("Pieces Container");
            if (piecesContainer != null)
            {
                return piecesContainer.transform.TransformPoint(localPos);
            }

            return localPos;
        }

        // Fallback: simple grid positioning
        return new Vector3(pos.x * 2f, pos.y * 2f, pos.z * 2f);
    }

    /// <summary>
    /// Get board dimensions from BoardDimensionsManager
    /// </summary>
    private Vector3Int GetBoardDimensions()
    {
        if (BoardDimensionsManager.Instance != null)
        {
            return BoardDimensionsManager.Instance.GetDimensions();
        }

        // Fallback to default 4x4x4
        return new Vector3Int(4, 4, 4);
    }

    /// <summary>
    /// Enable or disable coordinate labels
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        enableLabels = enabled;

        if (labelContainer != null)
        {
            labelContainer.SetActive(enabled);
        }

        Debug.Log($"CoordinateLabelsUI: Labels {(enabled ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Update labels when board rotates (chaos mode)
    /// </summary>
    public void RefreshLabels()
    {
        // Labels automatically billboard to camera in LateUpdate
        // This method is a hook for future enhancements if needed
        Debug.Log("CoordinateLabelsUI: Refreshing label positions");
    }

    /// <summary>
    /// Clean up labels
    /// </summary>
    private void OnDestroy()
    {
        if (labelContainer != null)
        {
            Destroy(labelContainer);
        }
    }
}
