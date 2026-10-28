using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Simple UI for the placement phase
/// Provides buttons to control the placement system
/// </summary>
public class PlacementUI : MonoBehaviour
{
    [Header("UI Settings")]
    public bool showUI = true;
    public float buttonWidth = 150f;
    public float buttonHeight = 50f;

    private Dictionary<PieceColor, Rect> playerReadyButtonRects = new Dictionary<PieceColor, Rect>();
    private Rect deselectButtonRect;
    private Rect stateInfoRect;
    private string currentStateText = "";
    
    private void Start()
    {
        // Calculate UI positions
        deselectButtonRect = new Rect(20, 200, buttonWidth, buttonHeight);
        stateInfoRect = new Rect(20, 80, buttonWidth * 3, 55);

        // Calculate ready button positions dynamically based on player count
        CalculatePlayerReadyButtonPositions();

        // Subscribe to game state changes
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
            UpdateStateText(GameStateManager.Instance.currentState);
        }

        // Subscribe to placement manager events
        if (PlacementManager.Instance != null)
        {
            PlacementManager.Instance.OnPlayerTurnChanged += OnPlayerTurnChanged;
            PlacementManager.Instance.OnPlacementPhaseComplete += OnPlacementPhaseComplete;
        }

        // Subscribe to turn manager events for gameplay turn changes
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged += OnGameplayTurnChanged;
        }
    }

    /// <summary>
    /// Calculate ready button positions based on active player count
    /// MULTI-PLAYER SUPPORT: Dynamically position buttons for 2-6 players
    /// </summary>
    private void CalculatePlayerReadyButtonPositions()
    {
        // Get all active players
        List<PieceColor> activePlayers = new List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetActivePlayers();
        }
        else
        {
            // Fallback to 2-player mode
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
        }

        int playerCount = activePlayers.Count;

        // Calculate button dimensions based on player count
        float btnWidth = buttonWidth;
        float btnGap = 10f;

        if (playerCount == 4)
        {
            btnWidth = 120f; // Slightly narrower for 4 players
            btnGap = 5f;
        }
        else if (playerCount >= 6)
        {
            btnWidth = 100f; // Compact for 6 players
            btnGap = 5f;
        }

        // Position buttons horizontally starting at x=20, y=20
        float startX = 20f;
        float startY = 20f;

        for (int i = 0; i < activePlayers.Count; i++)
        {
            PieceColor player = activePlayers[i];
            float xPos = startX + (i * (btnWidth + btnGap));
            playerReadyButtonRects[player] = new Rect(xPos, startY, btnWidth, buttonHeight);
        }

        Debug.Log($"PlacementUI: Calculated {playerCount} ready button positions");
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
        
        if (PlacementManager.Instance != null)
        {
            PlacementManager.Instance.OnPlayerTurnChanged -= OnPlayerTurnChanged;
            PlacementManager.Instance.OnPlacementPhaseComplete -= OnPlacementPhaseComplete;
        }
        
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= OnGameplayTurnChanged;
        }
    }
    
    private void OnGUI()
    {
        if (!showUI) return;

        // Hide PlacementUI when game is over (status bar already shows game over message)
        if (GameStateManager.Instance != null &&
            GameStateManager.Instance.currentState == GameState.GameOver)
        {
            return;
        }

        // Layout is authored at 160 dpi; scale it up on high-density screens (phones)
        // and keep it inside the safe area so the notch and home indicator don't cover it
        Vector2 uiSize = TouchGUI.Begin();
        float uiWidth = uiSize.x;
        float uiHeight = uiSize.y;

        // Set GUI style
        GUI.skin.button.fontSize = 14; // Reduced from 16 for better fit in narrow buttons
        GUI.skin.label.fontSize = 14;
        GUI.skin.label.normal.textColor = NeonTheme.Lavender;

        // Game state info
        NeonTheme.SceneLabel(stateInfoRect, currentStateText, GUI.skin.label);
        
        // Get camera controller once for all camera-related operations
        CameraController cameraController = FindFirstObjectByType<CameraController>();
        
        // Camera debug info - removed to reduce UI clutter
        
        // Only show buttons during placement phase
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            // MULTI-PLAYER SUPPORT: Always show individual ready buttons for all active players
            ShowMultiPlayerReadyButtons();

            int autoRows = ShowAutoPlaceButtons();
            ShowOnScreenBench(uiWidth, uiHeight);

            // Deselect button - only show if a piece is selected (below any auto-place buttons)
            if (PlacementManager.Instance != null && HasSelectedTrayPiece())
            {
                Rect deselect = deselectButtonRect;
                deselect.y = Mathf.Max(deselect.y, 140 + autoRows * (buttonHeight + 8f));
                if (TouchGUI.NeonButton(deselect, "Deselect Piece", NeonTheme.Cyan, NeonTheme.Pink, outlined: true))
                {
                    OnDeselectButtonClicked();
                }
                // A piece picked up off the board can go back to the bench
                if (PlacementManager.Instance.CanReturnSelectedToTray)
                {
                    Rect back = new Rect(deselect.x, deselect.yMax + 8f, deselect.width, deselect.height);
                    if (TouchGUI.NeonButton(back, "Back to bench", NeonTheme.Pink, NeonTheme.Cyan))
                    {
                        Haptics.Selection();
                        PlacementManager.Instance.ReturnSelectedToTray();
                    }
                }
            }
        }

        // Camera control buttons removed - users can use I/O keyboard shortcuts or mouse wheel/pinch zoom instead

        // Show instructions
        string instructions = GetInstructions();
        if (!string.IsNullOrEmpty(instructions))
        {
            Rect instructionsRect = new Rect(20, uiHeight - 70, uiWidth - 40, 60);
            NeonTheme.SceneLabel(instructionsRect, instructions, GUI.skin.label);
        }

        TouchGUI.End();
    }

    /// <summary>
    /// One "Auto-place" button, always in the same spot, for the player you're working on:
    /// the colour of the tray piece you last tapped, else the side you last viewed from, else
    /// the first player still placing. A small arrow beside it switches to the next player.
    /// </summary>
    private PieceColor? autoPlaceFor;

    /// <summary>The player whose pieces the setup tools are showing (null when nobody places by hand)</summary>
    public static PieceColor? WorkingOn { get; private set; }

    /// <summary>Local games: the not-yet-ready player whose face is toward the camera</summary>
    public static PieceColor? InView { get; private set; }

    private static readonly ChessPieceType[] BenchOrder =
        { ChessPieceType.King, ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Pawn };
    private readonly Dictionary<string, Texture2D> benchIcons = new Dictionary<string, Texture2D>();

    /// <summary>
    /// The player's remaining pieces as a row of pictures above the View buttons: tap one,
    /// then tap a glowing square. Works from any camera angle (the 3D benches of the top and
    /// bottom players are hard to see together with the board).
    /// </summary>
    private void ShowOnScreenBench(float uiWidth, float uiHeight)
    {
        if (!WorkingOn.HasValue || PlacementManager.Instance == null || PlacementManager.Instance.IsAutoPlacing) return;
        PieceColor color = WorkingOn.Value;
        PieceTray tray = PieceTray.GetTrayForColor(color);
        if (tray == null) return;

        // Pieces still on the bench, grouped by type
        var byType = new Dictionary<ChessPieceType, List<ChessPiece>>();
        foreach (ChessPiece p in tray.GetComponentsInChildren<ChessPiece>())
        {
            if (!byType.TryGetValue(p.pieceType, out var list)) byType[p.pieceType] = list = new List<ChessPiece>();
            list.Add(p);
        }
        if (byType.Count == 0) return;
        if (Event.current.type == EventType.Repaint) benchShown = true;

        var types = new List<ChessPieceType>();
        foreach (ChessPieceType t in BenchOrder) if (byType.ContainsKey(t)) types.Add(t);

        const float size = 58f, gap = 8f;
        float total = types.Count * size + (types.Count - 1) * gap;
        float x = (uiWidth - total) / 2f;
        float y = uiHeight - 52f - 12f - size - 10f; // just above the View buttons
        Color tint = color == PieceColor.White ? NeonTheme.Cyan : PieceSets.Tint(PieceSets.Kind.NeonGlow, color);
        PieceColor? selectedColor = PlacementManager.Instance.SelectedTrayColor;
        ChessPieceType? selectedType = PlacementManager.Instance.SelectedTrayType;

        NeonTheme.SceneLabel(new Rect(x - 120f, y, 112f, size), $"{color}'s\nbench", NeonTheme.GUILabelStyle(tint, 14, true, TextAnchor.MiddleRight));
        PlayerSymbols.DrawGUI(new Rect(x - 146f, y + size / 2f - 12f, 24f, 24f), color); // Color-Blind Symbols
        foreach (ChessPieceType t in types)
        {
            Rect r = new Rect(x, y, size, size);
            bool chosen = selectedColor == color && selectedType == t;
            if (TouchGUI.NeonButton(r, "", tint, NeonTheme.Ground, outlined: !chosen))
            {
                Haptics.Selection();
                PlacementManager.Instance.OnTrayPieceClicked(byType[t][0]);
            }
            Texture2D icon = Icon(color, t);
            if (icon != null)
            {
                Color old = GUI.color;
                GUI.color = chosen ? Color.black : Color.white;
                GUI.DrawTexture(new Rect(r.x + 7f, r.y + 5f, size - 14f, size - 14f), icon, ScaleMode.ScaleToFit, true);
                GUI.color = old;
            }
            PlayerSymbols.DrawGUI(new Rect(r.x + 3f, r.y + 3f, 15f, 15f), color);
            int count = byType[t].Count;
            if (count > 1)
                GUI.Label(new Rect(r.xMax - 26f, r.yMax - 20f, 24f, 18f), $"x{count}", NeonTheme.GUILabelStyle(chosen ? NeonTheme.Ground : NeonTheme.White, 12, true, TextAnchor.MiddleRight));
            x += size + gap;
        }
    }

    // While the on-screen bench is up, the 3D view is nudged up so the board and its glowing
    // squares sit above the bench instead of behind it
    private bool benchShown;
    private float viewShift;
    private const float BenchShift = 0.28f; // in screen heights/2 (normalized device units)

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float target = benchShown && GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces() ? BenchShift : 0f;
        viewShift = Mathf.MoveTowards(viewShift, target, Time.unscaledDeltaTime * 0.8f);
        cam.ResetProjectionMatrix();
        if (viewShift > 0.0001f)
        {
            Matrix4x4 m = cam.projectionMatrix;
            m[1, 2] = -viewShift; // shifts the whole picture up
            cam.projectionMatrix = m;
        }
        benchShown = false; // set again each frame the bench is drawn
    }

    private Texture2D Icon(PieceColor color, ChessPieceType type)
    {
        // Icons match the chosen piece set (the other sets have theirs in PieceIcons/<Folder>)
        string key = PieceShapes.IconFolder(PieceShapes.Current) + (color == PieceColor.White ? "Light_" : "Dark_") + type;
        if (!benchIcons.TryGetValue(key, out Texture2D tex))
        {
            tex = Resources.Load<Texture2D>("PieceIcons/" + key);
            benchIcons[key] = tex;
        }
        return tex;
    }

    private int ShowAutoPlaceButtons()
    {
        if (PlacementManager.Instance == null || PlacementManager.Instance.IsAutoPlacing) { WorkingOn = null; InView = null; return 0; }

        // Players who can still place by hand on this device, in seat order
        var placing = new List<PieceColor>();
        var editable = new List<PieceColor>(); // still before Ready (may have placed everything)
        float x = float.MaxValue, width = 0f;
        foreach (var entry in playerReadyButtonRects)
        {
            if (entry.Value.x < x) { x = entry.Value.x; width = entry.Value.width; }
            PieceColor player = entry.Key;
            bool isHuman = TurnManager.Instance == null || !TurnManager.Instance.IsPlayerAI(player);
            if (!isHuman || !OnlineSession.CanControl(player) || PlacementManager.Instance.IsPlayerReady(player)) continue;
            editable.Add(player);
            if (!PlacementManager.Instance.IsPlayerPlacementCompleted(player)) placing.Add(player);
        }
        placing.Sort();
        editable.Sort();
        // The player whose face is toward the camera can still move their placed pieces around
        InView = OnlineSession.IsActive ? (PieceColor?)null : FacingPlayer(editable);
        if (placing.Count == 0) { WorkingOn = null; return 0; }

        // Follow what the player is working on. Local games: the face of the cube toward the
        // camera decides, and turning to another face puts down a piece held for someone else.
        // (Online, this device only ever places its own pieces.)
        bool faceChanged = false;
        if (!OnlineSession.IsActive)
        {
            PieceColor? facing = FacingPlayer(placing);
            if (facing.HasValue && facing != lastFacing)
            {
                autoPlaceFor = facing;
                faceChanged = true;
                PieceColor? held = PlacementManager.Instance.SelectedTrayColor;
                if (held.HasValue && held != facing) PlacementManager.Instance.DeselectTrayPiece();
            }
            lastFacing = facing;
        }
        // Tapping a piece on a 3D bench picks that player until the view moves on
        PieceColor? tapped = PlacementManager.Instance.SelectedTrayColor;
        if (!faceChanged && tapped.HasValue && placing.Contains(tapped.Value)) autoPlaceFor = tapped;
        else if (PlayerViewUI.LastViewed.HasValue && placing.Contains(PlayerViewUI.LastViewed.Value) && lastViewSeen != PlayerViewUI.LastViewed)
            autoPlaceFor = PlayerViewUI.LastViewed;
        lastViewSeen = PlayerViewUI.LastViewed;
        if (!autoPlaceFor.HasValue || !placing.Contains(autoPlaceFor.Value)) autoPlaceFor = placing[0];
        PieceColor current = autoPlaceFor.Value;
        WorkingOn = current;

        width = Mathf.Max(width, 200f); // 6 player ready buttons are narrow; this one needs room for its label
        bool many = placing.Count > 1;
        float arrow = many ? 44f : 0f;
        Rect rect = new Rect(x, 140, width - (many ? arrow + 6f : 0f), buttonHeight);
        if (QueenBlitz.Active)
        {
            // Puzzles: the queen goes on a best starting square, for a pawn
            GUI.enabled = QueenBlitz.CanAutoPlace;
            string label = QueenBlitz.CanAutoPlace ? $"Auto-place (costs {PawnBank.AutoPlaceCost} pawn)" : "Auto-place (need a pawn)";
            if (TouchGUI.NeonButton(new Rect(x, 140, Mathf.Max(width, 240f), buttonHeight), label, NeonTheme.Yellow, NeonTheme.Pink, fontSize: 15))
                QueenBlitz.AutoPlaceQueen();
            GUI.enabled = true;
            return 1;
        }
        if (TouchGUI.NeonButton(rect, $"Auto-place {current}", NeonTheme.Yellow, NeonTheme.Pink))
        {
            PlacementManager.Instance.AutoPlaceRemaining(current);
        }
        if (many && TouchGUI.NeonButton(new Rect(rect.xMax + 6f, 140, arrow, buttonHeight), ">", NeonTheme.Yellow, NeonTheme.Pink, outlined: true, fontSize: 18))
        {
            autoPlaceFor = placing[(placing.IndexOf(current) + 1) % placing.Count];
            if (PlacementManager.Instance.SelectedTrayColor.HasValue && PlacementManager.Instance.SelectedTrayColor != autoPlaceFor)
                PlacementManager.Instance.DeselectTrayPiece();
        }
        return 1;
    }

    private PieceColor? lastViewSeen, lastFacing;

    /// <summary>
    /// The placing player whose side of the cube faces the camera most squarely. Uses each
    /// player's home face direction (the benches aren't all centred on their face, which made
    /// the top and bottom players switch late). Sticks with the current player until another
    /// is clearly more in view, so it doesn't flicker on the diagonals.
    /// </summary>
    private PieceColor? FacingPlayer(List<PieceColor> placing)
    {
        Camera cam = Camera.main;
        if (cam == null || ChessBoard.Instance == null) return null;
        Vector3 toCamera = (cam.transform.position - PlayerViewUI.BoardCenter()).normalized;
        PieceColor? best = null;
        float bestScore = float.NegativeInfinity, currentScore = float.NegativeInfinity;
        foreach (PieceColor c in placing)
        {
            float score = Vector3.Dot(PlayerViewUI.HomeSideDirection(c, out _), toCamera);
            if (score > bestScore) { bestScore = score; best = c; }
            if (lastFacing == c) currentScore = score;
        }
        if (bestScore < 0.2f) return null;
        if (lastFacing.HasValue && placing.Contains(lastFacing.Value) && currentScore > bestScore - 0.05f) return lastFacing;
        return best;
    }

    
    /// <summary>
    /// Get color-tinted grey background color for a player (not ready state)
    /// MULTI-PLAYER SUPPORT: Each player has a unique tinted grey
    /// </summary>
    private Color GetPlayerGreyColor(PieceColor player)
    {
        switch (player)
        {
            case PieceColor.White:
                return new Color(0.7f, 0.7f, 0.7f); // Light grey
            case PieceColor.Black:
                return new Color(0.3f, 0.3f, 0.3f); // Dark grey
            case PieceColor.Green:
                return new Color(0.4f, 0.7f, 0.4f); // Green-grey
            case PieceColor.Purple:
                return new Color(0.6f, 0.4f, 0.7f); // Purple-grey
            case PieceColor.Yellow:
                return new Color(0.7f, 0.7f, 0.4f); // Yellow-grey
            case PieceColor.Orange:
                return new Color(0.7f, 0.5f, 0.3f); // Orange-grey
            default:
                return Color.grey;
        }
    }

    /// <summary>
    /// Get medium-saturation color for a player (pieces placed, ready to click)
    /// MULTI-PLAYER SUPPORT: Each player has a medium-saturation color indicating button is active
    /// </summary>
    private Color GetPlayerMediumColor(PieceColor player)
    {
        switch (player)
        {
            case PieceColor.White:
                return new Color(0.9f, 0.9f, 0.9f); // Medium-bright white
            case PieceColor.Black:
                return new Color(0.4f, 0.4f, 0.4f); // Medium grey
            case PieceColor.Green:
                return new Color(0.3f, 0.85f, 0.3f); // Medium green
            case PieceColor.Purple:
                return new Color(0.7f, 0.3f, 0.85f); // Medium purple
            case PieceColor.Yellow:
                return new Color(0.85f, 0.85f, 0.3f); // Medium yellow
            case PieceColor.Orange:
                return new Color(0.85f, 0.55f, 0.3f); // Medium orange
            default:
                return new Color(0.5f, 0.7f, 0.5f); // Medium green-grey
        }
    }

    /// <summary>
    /// Get bright color for a player (ready state)
    /// MULTI-PLAYER SUPPORT: Each player transitions to their bright color when ready
    /// </summary>
    private Color GetPlayerBrightColor(PieceColor player)
    {
        switch (player)
        {
            case PieceColor.White:
                return new Color(1.0f, 1.0f, 1.0f); // Bright white
            case PieceColor.Black:
                return new Color(0.5f, 0.5f, 0.5f); // Medium grey
            case PieceColor.Green:
                return new Color(0.2f, 1.0f, 0.2f); // Bright green
            case PieceColor.Purple:
                return new Color(0.8f, 0.2f, 1.0f); // Bright purple
            case PieceColor.Yellow:
                return new Color(1.0f, 1.0f, 0.2f); // Bright yellow
            case PieceColor.Orange:
                return new Color(1.0f, 0.6f, 0.2f); // Bright orange
            default:
                return Color.green;
        }
    }

    /// <summary>
    /// Show separate ready buttons for each player in human vs human mode
    /// MULTI-PLAYER SUPPORT: Dynamically shows buttons for all active players (2-6)
    /// </summary>
    private void ShowMultiPlayerReadyButtons()
    {
        bool canStartGame = CanStartGame();

        // Get all active players
        List<PieceColor> activePlayers = new List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetActivePlayers();
        }
        else
        {
            // Fallback to 2-player mode
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
        }

        // Debug logging for button state diagnosis
        if (PlacementManager.Instance != null && Time.frameCount % 60 == 0) // Log once per second
        {
            string readyStates = "PlacementUI: Ready button states - canStartGame=" + canStartGame;
            foreach (PieceColor player in activePlayers)
            {
                bool playerReady = PlacementManager.Instance.IsPlayerReady(player);
                readyStates += $", {player}={playerReady}";
            }
            Debug.Log(readyStates);
        }

        Color originalColor = GUI.backgroundColor;

        // Show button for each active player
        foreach (PieceColor player in activePlayers)
        {
            if (QueenBlitz.Active && player == QueenBlitz.PawnColor) continue; // the puzzle's pawns have nothing to ready
            if (ZombieMode.Active && player == ZombieMode.ZombieColor) continue; // nor do the zombies
            if (!playerReadyButtonRects.ContainsKey(player))
            {
                Debug.LogWarning($"PlacementUI: No button rect found for {player}");
                continue;
            }

            bool playerReady = PlacementManager.Instance != null && PlacementManager.Instance.IsPlayerReady(player);
            bool allPiecesPlaced = PlacementManager.Instance != null &&
                                   PlacementManager.Instance.IsPlayerPlacementCompleted(player);

            // Only enable your own button once your pieces are down (never a robot's or another device's)
            bool yours = (TurnManager.Instance == null || !TurnManager.Instance.IsPlayerAI(player)) && OnlineSession.CanControl(player);
            GUI.enabled = allPiecesPlaced && yours && !playerReady;

            // Determine button text - two-line format with full color name
            string colorName = player.ToString(); // White, Black, Green, Purple, Yellow, Orange
            string buttonText;
            if (playerReady)
            {
                buttonText = $"{colorName}\nReady";  // e.g., "White\nReady"
            }
            else if (allPiecesPlaced)
            {
                buttonText = $"{colorName}\nReady to Play";  // e.g., "White\nReady to Play"
            }
            else
            {
                buttonText = $"{colorName}\nPlace Pieces";  // e.g., "White\nPlace Pieces"
            }

            // Set button color with three-state progression:
            // 1. Grey tint (pieces not all placed) - desaturated, "disabled" appearance
            // 2. Bright/vibrant (all pieces placed, ready to click) - active, prominent, clickable appearance
            // 3. Medium saturation (player marked ready) - confirmed but dimmed, "ready" appearance
            Color buttonColor;

            if (playerReady)
            {
                buttonColor = GetPlayerMediumColor(player); // State 3: Clicked ready (dimmed)
            }
            else if (allPiecesPlaced)
            {
                buttonColor = GetPlayerBrightColor(player); // State 2: All pieces placed, clickable (bright)
            }
            else
            {
                buttonColor = GetPlayerGreyColor(player); // State 1: Still placing pieces
            }
            GUI.backgroundColor = buttonColor;

            // Draw button
            Color fill = playerReady ? NeonTheme.Lime : allPiecesPlaced ? NeonTheme.Pink : NeonTheme.Lavender;
            Rect readyRect = playerReadyButtonRects[player];
            int readyFont = readyRect.width < 110f ? 12 : 15; // narrower buttons with 4-6 players
            if (TouchGUI.NeonButton(readyRect, buttonText, fill, NeonTheme.Cyan, outlined: !playerReady && !allPiecesPlaced, fontSize: readyFont))
            {
                OnPlayerReadyButtonClicked(player);
            }
            PlayerSymbols.DrawGUI(new Rect(readyRect.x + 4f, readyRect.y + 4f, 16f, 16f), player); // Color-Blind Symbols
        }

        GUI.backgroundColor = originalColor; // Restore original color
        GUI.enabled = true; // Re-enable GUI
    }
    
    /// <summary>
    /// Check if the game can be started (enough pieces placed)
    /// MULTI-PLAYER SUPPORT: Checks that all active players have placed at least one piece
    /// </summary>
    private bool CanStartGame()
    {
        // Check that all active players have at least one piece on the board
        if (ChessBoard.Instance == null) return false;

        // Get all active players
        List<PieceColor> activePlayers = new List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetActivePlayers();
        }
        else
        {
            // Fallback to 2-player mode
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
        }

        // Get dynamic board dimensions
        Vector3Int dims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4

        // Track which players have pieces on the board
        Dictionary<PieceColor, bool> playerHasPieces = new Dictionary<PieceColor, bool>();
        foreach (PieceColor player in activePlayers)
        {
            playerHasPieces[player] = false;
        }

        // Check all board positions for pieces using dynamic dimensions
        for (int x = 0; x < dims.x; x++)
        {
            for (int y = 0; y < dims.y; y++)
            {
                for (int z = 0; z < dims.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null && playerHasPieces.ContainsKey(piece.pieceColor))
                    {
                        playerHasPieces[piece.pieceColor] = true;
                    }
                }
            }
        }

        // Require at least one piece from each active player
        foreach (var kvp in playerHasPieces)
        {
            if (!kvp.Value)
            {
                return false; // This player has no pieces placed
            }
        }

        return true; // All players have at least one piece
    }
    
    /// <summary>
    /// Get instructions text based on current game state
    /// </summary>
    /// <summary>A player on this device still setting up has pieces on the board</summary>
    private static bool OwnPiecesOnBoard()
    {
        if (ChessBoard.Instance == null || BoardDimensionsManager.Instance == null || TurnManager.Instance == null) return false;
        // Which colours count, worked out once (IsPlayerAI is slow and chatty)
        var mine = new System.Collections.Generic.HashSet<PieceColor>();
        if (PlayerManager.Instance != null)
            foreach (PieceColor c in PlayerManager.Instance.GetActivePlayers())
                if (!TurnManager.Instance.IsPlayerAI(c) && OnlineSession.CanControl(c) && !PlacementManager.Instance.IsPlayerReady(c)) mine.Add(c);
        if (mine.Count == 0) return false;
        Vector3Int d = BoardDimensionsManager.Instance.GetDimensions();
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null && mine.Contains(p.pieceColor)) return true;
        }
        return false;
    }

    private string GetInstructions()
    {
        if (GameStateManager.Instance == null) return "";
        
        switch (GameStateManager.Instance.currentState)
        {
            case GameState.PiecePlacement:
                // Once some pieces are down, say how to change your mind
                if (PlacementManager.Instance != null && !HasSelectedTrayPiece() && OwnPiecesOnBoard())
                    return "Tap one of your placed pieces to move it, or send it back to the bench";
                return "";
            
            case GameState.Playing:
                return "";
            
            case GameState.GameOver:
                return "GAME OVER";
            
            default:
                return "";
        }
    }
    
    /// <summary>
    /// Handle individual player ready button click
    /// </summary>
    private void OnPlayerReadyButtonClicked(PieceColor playerColor)
    {
        Debug.Log($"PlacementUI: {playerColor} ready button clicked");
        
        // Each player can say Ready once their own pieces are down, whatever the others are doing;
        // the game starts when everyone is ready
        
        // Only your own button: never a robot's, and never another device's player online
        if (TurnManager.Instance != null && TurnManager.Instance.IsPlayerAI(playerColor)) return;
        if (!OnlineSession.CanControl(playerColor)) return;

        if (PlacementManager.Instance != null)
        {
            // Ready is final (the bench goes away); a second tap does nothing
            bool currentReadiness = PlacementManager.Instance.IsPlayerReady(playerColor);
            if (currentReadiness) return;
            Haptics.Selection();
            Sfx.Play("ready");
            PlacementManager.Instance.SetPlayerReady(playerColor, true);
            // Only robots left setting up: they finish straight away
            if (AIPlayer.Instance != null) AIPlayer.Instance.FinishPlacementNow();
            
            Debug.Log($"PlacementUI: {playerColor} readiness set to {!currentReadiness}");
        }
    }
    
    /// <summary>
    /// Handle Deselect button click
    /// </summary>
    private void OnDeselectButtonClicked()
    {
        Debug.Log("PlacementUI: Deselect button clicked");
        
        if (PlacementManager.Instance != null)
        {
            PlacementManager.Instance.DeselectTrayPiece();
        }
    }
    
    /// <summary>
    /// Check if a tray piece is currently selected
    /// </summary>
    private bool HasSelectedTrayPiece()
    {
        return PlacementManager.Instance != null && PlacementManager.Instance.HasSelectedTrayPiece;
    }
    
    
    /// <summary>
    /// Handle game state changes
    /// </summary>
    private void OnGameStateChanged(GameState newState)
    {
        UpdateStateText(newState);
        
    }
    
    /// <summary>
    /// Handle player turn changes during placement
    /// </summary>
    private void OnPlayerTurnChanged(PieceColor currentPlayer)
    {
        Debug.Log($"PlacementUI: Turn changed to {currentPlayer}");
        UpdateStateText(GameStateManager.Instance.currentState);
    }
    
    /// <summary>
    /// Handle placement phase completion
    /// </summary>
    private void OnPlacementPhaseComplete()
    {
        Debug.Log("PlacementUI: Placement phase completed");
    }
    
    /// <summary>
    /// Handle turn changes during gameplay
    /// </summary>
    private void OnGameplayTurnChanged(PieceColor previousPlayer, PieceColor newCurrentPlayer)
    {
        Debug.Log($"PlacementUI: Gameplay turn changed from {previousPlayer} to {newCurrentPlayer}");

        // Update the UI display to reflect the new current player
        if (GameStateManager.Instance != null)
        {
            UpdateStateText(GameStateManager.Instance.currentState);
        }
    }
    
    /// <summary>
    /// Update the state text display
    /// MULTI-PLAYER SUPPORT: Shows readiness for all active players
    /// </summary>
    private void UpdateStateText(GameState state)
    {
        switch (state)
        {
            case GameState.PiecePlacement:
                // Add readiness info for human vs human mode
                bool isHumanVsHuman = TurnManager.Instance != null && TurnManager.Instance.IsHumanVsHuman();
                string readinessInfo = "";
                if (isHumanVsHuman && PlacementManager.Instance != null)
                {
                    // Get all active players
                    List<PieceColor> activePlayers = new List<PieceColor>();
                    if (PlayerManager.Instance != null)
                    {
                        activePlayers = PlayerManager.Instance.GetActivePlayers();
                    }
                    else
                    {
                        // Fallback to 2-player mode
                        activePlayers.Add(PieceColor.White);
                        activePlayers.Add(PieceColor.Black);
                    }

                    // Build readiness display for all players
                    List<string> playerReadiness = new List<string>();
                    foreach (PieceColor player in activePlayers)
                    {
                        bool playerReady = PlacementManager.Instance.IsPlayerReady(player);
                        string initial = player.ToString().Substring(0, 1); // W, B, G, P, Y, O
                        playerReadiness.Add($"{initial}:{(playerReady ? "✓" : "✗")}");
                    }
                    readinessInfo = string.Join(" ", playerReadiness);
                }

                // Remove "PLACEMENT PHASE" text to avoid duplication with status bar
                currentStateText = readinessInfo;
                break;

            case GameState.Playing:
                currentStateText = "";
                break;

            case GameState.GameOver:
                currentStateText = "GAME OVER";
                break;

            default:
                currentStateText = $"STATE: {state}";
                break;
        }
    }
}