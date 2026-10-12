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
        GUI.Label(stateInfoRect, currentStateText);
        
        // Get camera controller once for all camera-related operations
        CameraController cameraController = FindFirstObjectByType<CameraController>();
        
        // Camera debug info - removed to reduce UI clutter
        
        // Only show buttons during placement phase
        if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
        {
            // MULTI-PLAYER SUPPORT: Always show individual ready buttons for all active players
            ShowMultiPlayerReadyButtons();

            int autoRows = ShowAutoPlaceButtons();

            // Deselect button - only show if a piece is selected (below any auto-place buttons)
            if (PlacementManager.Instance != null && HasSelectedTrayPiece())
            {
                Rect deselect = deselectButtonRect;
                deselect.y = Mathf.Max(deselect.y, 140 + autoRows * (buttonHeight + 8f));
                if (TouchGUI.NeonButton(deselect, "Deselect Piece", NeonTheme.Cyan, NeonTheme.Pink, outlined: true))
                {
                    OnDeselectButtonClicked();
                }
            }
        }

        // Camera control buttons removed - users can use I/O keyboard shortcuts or mouse wheel/pinch zoom instead

        // Show instructions
        string instructions = GetInstructions();
        if (!string.IsNullOrEmpty(instructions))
        {
            Rect instructionsRect = new Rect(20, uiHeight - 70, uiWidth - 40, 60);
            GUI.Label(instructionsRect, instructions);
        }

        TouchGUI.End();
    }

    /// <summary>
    /// One "Auto-place" button per human player who still has pieces in their tray
    /// </summary>
    private int ShowAutoPlaceButtons()
    {
        if (PlacementManager.Instance == null || PlacementManager.Instance.IsAutoPlacing) return 0;

        // All in one spot on the left (under the first ready button), stacked if several players
        // place by hand, so they never sit over the board
        float x = float.MaxValue, width = 0f;
        foreach (var entry in playerReadyButtonRects)
        {
            if (entry.Value.x < x) { x = entry.Value.x; width = entry.Value.width; }
        }
        int row = 0;
        foreach (var entry in playerReadyButtonRects)
        {
            PieceColor player = entry.Key;
            bool isHuman = TurnManager.Instance == null || !TurnManager.Instance.IsPlayerAI(player);
            if (!isHuman || !OnlineSession.CanControl(player) || PlacementManager.Instance.IsPlayerPlacementCompleted(player)) continue;

            Rect rect = new Rect(x, 140 + row * (buttonHeight + 8f), width, buttonHeight);
            row++;
            if (TouchGUI.NeonButton(rect, $"Auto-place {player}", NeonTheme.Yellow, NeonTheme.Pink))
            {
                PlacementManager.Instance.AutoPlaceRemaining(player);
            }
        }
        return row;
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
            if (!playerReadyButtonRects.ContainsKey(player))
            {
                Debug.LogWarning($"PlacementUI: No button rect found for {player}");
                continue;
            }

            bool playerReady = PlacementManager.Instance != null && PlacementManager.Instance.IsPlayerReady(player);
            bool allPiecesPlaced = PlacementManager.Instance != null &&
                                   PlacementManager.Instance.IsPlayerPlacementCompleted(player);

            GUI.enabled = allPiecesPlaced; // Only enable if all pieces are placed

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
    private string GetInstructions()
    {
        if (GameStateManager.Instance == null) return "";
        
        switch (GameStateManager.Instance.currentState)
        {
            case GameState.PiecePlacement:
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
        
        if (!CanStartGame())
        {
            Debug.LogWarning($"PlacementUI: Cannot set {playerColor} ready - not enough pieces placed");
            return;
        }
        
        if (PlacementManager.Instance != null)
        {
            // Toggle player readiness
            bool currentReadiness = PlacementManager.Instance.IsPlayerReady(playerColor);
            PlacementManager.Instance.SetPlayerReady(playerColor, !currentReadiness);
            
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