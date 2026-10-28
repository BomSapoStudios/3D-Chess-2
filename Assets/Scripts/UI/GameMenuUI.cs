using UnityEngine;

/// <summary>
/// Game menu popup that appears when the Menu button is clicked during gameplay.
/// Provides access to forfeit, draw, and return to menu functionality.
/// Uses immediate mode GUI for consistency with ConfirmationDialog.
/// </summary>
public class GameMenuUI : MonoBehaviour
{
    [Header("Menu Settings")]
    public bool enableGameMenu = true;
    public KeyCode menuToggleKey = KeyCode.Escape;
    
    public static GameMenuUI Instance { get; private set; }
    
    // Menu state
    private bool isMenuVisible = false;
    
    // GUI styling
    private GUIStyle menuBoxStyle;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle overlayStyle;
    private GUIStyle infoStyle;
    private GUIStyle humanDrawOfferStyle;
    private GUIStyle aiDrawOfferStyle;
    
    // Menu dimensions
    private Rect menuRect;
    private Rect overlayRect;
    private bool stylesInitialized = false;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("GameMenuUI: Instance created");
        }
        else
        {
            Debug.LogWarning("GameMenuUI: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        InitializeStyles();
    }
    
    private void Update()
    {
        // Handle menu toggle key
        if (enableGameMenu && Input.GetKeyDown(menuToggleKey))
        {
            ToggleMenu();
        }
    }
    
    /// <summary>
    /// Initialize GUI styles for the menu
    /// </summary>
    private void InitializeStyles()
    {
        // Neon look, sized in TouchGUI's scaled units so it is large on phones
        Color ground = NeonTheme.Ground;

        menuBoxStyle = new GUIStyle();
        menuBoxStyle.normal.background = CreateColorTexture(new Color(ground.r, ground.g, ground.b, 0.97f));
        menuBoxStyle.border = new RectOffset(10, 10, 10, 10);
        menuBoxStyle.padding = new RectOffset(24, 24, 18, 18);

        overlayStyle = new GUIStyle();
        overlayStyle.normal.background = CreateColorTexture(new Color(ground.r, ground.g, ground.b, 0.7f));

        titleStyle = new GUIStyle();
        titleStyle.font = NeonTheme.DisplayFont;
        titleStyle.fontSize = 26;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = NeonTheme.Lime;
        titleStyle.wordWrap = true;

        buttonStyle = new GUIStyle();
        buttonStyle.font = NeonTheme.BodyFont;
        buttonStyle.fontSize = 19;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
        buttonStyle.normal.textColor = ground;
        buttonStyle.hover.textColor = ground;
        buttonStyle.active.textColor = ground;
        buttonStyle.normal.background = CreateColorTexture(NeonTheme.Pink);
        buttonStyle.hover.background = CreateColorTexture(NeonTheme.Pink);
        buttonStyle.active.background = CreateColorTexture(NeonTheme.PinkSoft);
        buttonStyle.border = new RectOffset(5, 5, 5, 5);
        buttonStyle.padding = new RectOffset(15, 15, 10, 10);
        buttonStyle.margin = new RectOffset(0, 0, 6, 6);

        infoStyle = new GUIStyle();
        infoStyle.font = NeonTheme.BodyFont;
        infoStyle.fontSize = 15;
        infoStyle.alignment = TextAnchor.MiddleCenter;
        infoStyle.normal.textColor = NeonTheme.Lavender;
        infoStyle.wordWrap = true;

        humanDrawOfferStyle = new GUIStyle(infoStyle);
        humanDrawOfferStyle.fontSize = 16;
        humanDrawOfferStyle.normal.textColor = NeonTheme.Cyan;

        aiDrawOfferStyle = new GUIStyle(infoStyle);
        aiDrawOfferStyle.fontSize = 16;
        aiDrawOfferStyle.normal.textColor = NeonTheme.Yellow;

        stylesInitialized = true;
    }
    
    /// <summary>
    /// Create a texture with a solid color
    /// </summary>
    private Texture2D CreateColorTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
    
    /// <summary>
    /// Show the game menu
    /// </summary>
    public void ShowMenu()
    {
        if (!enableGameMenu) return;
        
        // Available during setup (piece placement) as well as play; actions like forfeit
        // check their own conditions
        if (GameStateManager.Instance == null)
        {
            Debug.LogWarning("GameMenuUI: Cannot show menu - no game state");
            return;
        }
        
        isMenuVisible = true;
        Debug.Log("GameMenuUI: Menu shown");
    }
    
    /// <summary>
    /// Hide the game menu
    /// </summary>
    public void HideMenu()
    {
        isMenuVisible = false;
        Debug.Log("GameMenuUI: Menu hidden");
    }
    
    /// <summary>
    /// Toggle menu visibility
    /// </summary>
    public void ToggleMenu()
    {
        if (isMenuVisible)
        {
            HideMenu();
        }
        else
        {
            ShowMenu();
        }
    }
    
    /// <summary>
    /// Check if menu is currently visible
    /// </summary>
    public bool IsMenuVisible()
    {
        return isMenuVisible;
    }
    
    /// <summary>
    /// Handle forfeit with confirmation
    /// </summary>
    private void HandleForfeit()
    {
        if (TurnManager.Instance == null || !TurnManager.Instance.CanCurrentPlayerForfeit())
        {
            Debug.LogWarning("GameMenuUI: Cannot forfeit - conditions not met");
            return;
        }
        
        PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
        HideMenu();
        
        // Show confirmation dialog
        if (ConfirmationDialog.Instance != null)
        {
            ConfirmationDialog.Instance.ShowForfeitDialog(currentPlayer, () => {
                Debug.Log($"GameMenuUI: Forfeit confirmed for {currentPlayer}");
                TurnManager.Instance.RequestForfeit();
            });
        }
        else
        {
            // Fallback: direct forfeit
            Debug.LogWarning("GameMenuUI: No confirmation dialog available, forfeiting directly");
            TurnManager.Instance.RequestForfeit();
        }
    }
    
    /// <summary>
    /// Handle draw offer with confirmation
    /// </summary>
    private void HandleDrawOffer()
    {
        if (TurnManager.Instance == null || !TurnManager.Instance.CanCurrentPlayerOfferDraw())
        {
            Debug.LogWarning("GameMenuUI: Cannot offer draw - conditions not met");
            return;
        }
        
        PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
        HideMenu();
        
        // Show confirmation dialog
        if (ConfirmationDialog.Instance != null)
        {
            ConfirmationDialog.Instance.ShowDrawOfferDialog(currentPlayer, () => {
                Debug.Log($"GameMenuUI: Draw offer confirmed for {currentPlayer}");
                TurnManager.Instance.RequestDrawOffer();
            });
        }
        else
        {
            // Fallback: direct draw offer
            Debug.LogWarning("GameMenuUI: No confirmation dialog available, offering draw directly");
            TurnManager.Instance.RequestDrawOffer();
        }
    }
    
    /// <summary>
    /// Handle draw accept
    /// </summary>
    private void HandleDrawAccept()
    {
        if (TurnManager.Instance == null || !TurnManager.Instance.CanCurrentPlayerRespondToDraw())
        {
            Debug.LogWarning("GameMenuUI: Cannot accept draw - conditions not met");
            return;
        }
        
        HideMenu();
        TurnManager.Instance.RespondToDrawOffer(true);
        Debug.Log("GameMenuUI: Draw accepted");
    }
    
    /// <summary>
    /// Handle draw decline
    /// </summary>
    private void HandleDrawDecline()
    {
        if (TurnManager.Instance == null || !TurnManager.Instance.CanCurrentPlayerRespondToDraw())
        {
            Debug.LogWarning("GameMenuUI: Cannot decline draw - conditions not met");
            return;
        }
        
        HideMenu();
        TurnManager.Instance.RespondToDrawOffer(false);
        Debug.Log("GameMenuUI: Draw declined");
    }
    
    /// <summary>
    /// Handle new game request
    /// </summary>
    private void HandleNewGame()
    {
        HideMenu();
        
        // Start a new game by returning to main menu first
        // This ensures clean initialization of a new game
        Debug.Log("GameMenuUI: Starting new game by returning to main menu");
        
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ReturnToMainMenu();
        }
        else if (SceneController.Instance != null)
        {
            SceneController.Instance.LoadMainMenu();
        }
        else
        {
            Debug.LogError("GameMenuUI: Cannot start new game - no scene controller available");
        }
    }
    
    /// <summary>
    /// Handle return to main menu
    /// </summary>
    /// <summary>Queen Blitz: back to the puzzle picker, on this pack and chapter</summary>
    private void GoToBlitzPicker()
    {
        QueenBlitz.ReturnToPicker = true;
        HandleReturnToMenu();
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>Tap "Queen Blitz" (BlitzDemo)</summary>
    public void DebugBlitzPicker() => GoToBlitzPicker();
#endif

    private void HandleReturnToMenu()
    {
        HideMenu();
        
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ReturnToMainMenu();
        }
        else if (SceneController.Instance != null)
        {
            SceneController.Instance.LoadMainMenu();
        }
        else
        {
            Debug.LogError("GameMenuUI: Cannot return to menu - no scene controller available");
        }
    }
    
    private void OnGUI()
    {
        if (!isMenuVisible || !stylesInitialized || !enableGameMenu) return;
        
        // Skip if confirmation dialog is open
        if (ConfirmationDialog.Instance != null && ConfirmationDialog.Instance.IsDialogActive()) return;
        
        // Scaled for the screen's density and kept inside the safe area
        GUI.depth = -20; // above the other in-game buttons
        Vector2 ui = TouchGUI.Begin();
        float menuWidth = Mathf.Min(380f, ui.x - 40f);
        float menuHeight = Mathf.Min(380f, ui.y - 16f);
        menuRect = new Rect((ui.x - menuWidth) / 2f, (ui.y - menuHeight) / 2f, menuWidth, menuHeight);
        overlayRect = new Rect(-ui.x, -ui.y, ui.x * 3f, ui.y * 3f); // reaches past the safe area
        TouchGUI.Block(new Rect(0, 0, ui.x, ui.y));

        // Draw overlay
        GUI.Box(overlayRect, "", overlayStyle);
        
        // Handle clicking outside menu to close
        if (Event.current.type == EventType.MouseDown && !menuRect.Contains(Event.current.mousePosition))
        {
            HideMenu();
            TouchGUI.End();
            return;
        }
        
        // Begin menu area
        GUILayout.BeginArea(menuRect, menuBoxStyle);
        GUILayout.BeginVertical();
        
        // Check if game is over and show appropriate content
        bool gameOver = IsGameOver();

        if (QueenBlitz.Active && !gameOver)
        {
            // Puzzles: no draws or forfeits, just back to the puzzle, the puzzle picker or the menu
            GUILayout.Label("GAME MENU", titleStyle);
            GUILayout.Space(20);
            if (GUILayout.Button("Resume Game", buttonStyle, GUILayout.Height(45))) HideMenu();
            GUILayout.Space(10);
            if (GUILayout.Button("Queen Blitz", buttonStyle, GUILayout.Height(45))) GoToBlitzPicker();
            GUILayout.Space(10);
            if (GUILayout.Button("Main Menu", buttonStyle, GUILayout.Height(45))) HandleReturnToMenu();
            GUILayout.EndVertical();
            GUILayout.EndArea();
            NeonTheme.GUIPanel(menuRect, NeonTheme.Cyan, 0f);
            TouchGUI.End();
            return;
        }
        
        if (gameOver)
        {
            // Game Over Menu Layout
            GUILayout.Label("GAME OVER", titleStyle);
            GUILayout.Space(20);
            
            // Game over status info
            var (isOver, reason, winner) = GetGameOverInfo();
            if (!string.IsNullOrEmpty(reason))
            {
                GUILayout.Label(reason, infoStyle);
                GUILayout.Space(15);
            }
            
            // New Game button
            if (GUILayout.Button("New Game", buttonStyle, GUILayout.Height(45)))
            {
                HandleNewGame();
            }
            
            GUILayout.Space(10);
        }
        else
        {
            // Normal Game Menu Layout
            GUILayout.Label("GAME MENU", titleStyle);
            GUILayout.Space(20);
            
            // Game status info
            string statusText = GetGameStatusText();
            if (!string.IsNullOrEmpty(statusText))
            {
                GUILayout.Label(statusText, infoStyle);
                GUILayout.Space(15);
            }
            
            // Resume Game button
            if (GUILayout.Button("Resume Game", buttonStyle, GUILayout.Height(45)))
            {
                HideMenu();
            }
            
            GUILayout.Space(10);
        }
        
        // Draw-related buttons - ALWAYS SHOW (button slot always present for fixed menu size)
        if (!gameOver && TurnManager.Instance != null && GameEndDetectionManager.Instance != null)
        {
            bool canOfferDraw = TurnManager.Instance.CanCurrentPlayerOfferDraw();
            bool canRespondToDraw = TurnManager.Instance.CanCurrentPlayerRespondToDraw();
            bool isDrawPending = GameEndDetectionManager.Instance.IsDrawOfferPending();

            var (drawYes, drawNeeded) = GameEndDetectionManager.Instance.DrawAgreement();
            if (canRespondToDraw && isDrawPending)
            {
                // 3+ players: everyone must agree, so say who's answering and how many have said yes
                if (drawNeeded > 1)
                {
                    PieceColor? answering = TurnManager.Instance.DrawResponder();
                    GUILayout.Label($"{GameEndDetectionManager.Instance.GetDrawOfferingPlayer()} offers a draw ({drawYes} of {drawNeeded} agreed). {answering}, do you agree?", infoStyle);
                }
                // Show accept/decline buttons side by side when responding to draw offer
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Accept Draw (Y)", buttonStyle, GUILayout.Height(40)))
                {
                    HandleDrawAccept();
                }
                if (GUILayout.Button("Decline Draw (N)", buttonStyle, GUILayout.Height(40)))
                {
                    HandleDrawDecline();
                }
                GUILayout.EndHorizontal();
            }
            else if (isDrawPending)
            {
                // Draw pending but can't respond - show disabled button
                GUI.enabled = false;
                GUILayout.Button(drawNeeded > 1 ? $"⏳ Draw Pending ({drawYes} of {drawNeeded} agreed)" : "⏳ Draw Pending...", buttonStyle, GUILayout.Height(40));
                GUI.enabled = true;
            }
            else if (canOfferDraw)
            {
                // Show offer draw button
                if (GUILayout.Button(Application.isMobilePlatform ? "Offer Draw" : "Offer Draw (D)", buttonStyle, GUILayout.Height(40)))
                {
                    HandleDrawOffer();
                }
            }
            else
            {
                // Can't offer draw right now - show disabled button to maintain layout
                GUI.enabled = false;
                GUILayout.Button("Offer Draw", buttonStyle, GUILayout.Height(40));
                GUI.enabled = true;
            }

            GUILayout.Space(10);
        }
        else if (!gameOver)
        {
            // Game not over but managers not ready - show disabled placeholder
            GUI.enabled = false;
            GUILayout.Button("Offer Draw", buttonStyle, GUILayout.Height(40));
            GUI.enabled = true;
            GUILayout.Space(10);
        }
        
        // Forfeit button (only show during active game)
        if (!gameOver)
        {
            bool canForfeit = TurnManager.Instance != null && TurnManager.Instance.CanCurrentPlayerForfeit();
            GUI.enabled = canForfeit;
            
            Color originalColor = buttonStyle.normal.textColor;
            if (!canForfeit)
            {
                buttonStyle.normal.textColor = Color.gray;
            }
            
            if (GUILayout.Button(Application.isMobilePlatform ? "Forfeit" : "Forfeit (F)", buttonStyle, GUILayout.Height(40)))
            {
                HandleForfeit();
            }
            
            buttonStyle.normal.textColor = originalColor;
            GUI.enabled = true;
            
            GUILayout.Space(15);
        }
        
        // Return to Main Menu button
        // Local games are saved as they go: the mode offers Resume when tapped again
        if (GUILayout.Button(SavedGames.Saving ? "Main Menu (game saved)" : "Return to Main Menu", buttonStyle, GUILayout.Height(40)))
        {
            HandleReturnToMenu();
        }
        
        GUILayout.FlexibleSpace();
        
        // Instructions
        if (!Application.isMobilePlatform)
        {
            GUILayout.Label("Press ESC to toggle menu", infoStyle);
        }
        GUILayout.Space(10);
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
        NeonTheme.GUIPanel(menuRect, NeonTheme.Cyan, 0f); // neon border over the panel
        TouchGUI.End();
    }
    
    /// <summary>
    /// Check if current game mode is Human vs AI
    /// </summary>
    private bool IsHumanVsAIMode()
    {
        if (TurnManager.Instance == null) return false;
        return TurnManager.Instance.IsHumanVsAI();
    }
    
    /// <summary>
    /// Check if the game is over
    /// </summary>
    private bool IsGameOver()
    {
        if (GameStateManager.Instance == null) return false;
        return !GameStateManager.Instance.IsGameActive();
    }
    
    /// <summary>
    /// Get the game over reason and winner information
    /// </summary>
    private (bool isGameOver, string reason, PieceColor winner) GetGameOverInfo()
    {
        if (!IsGameOver()) return (false, "", PieceColor.White);
        
        // Try to get information from GameEndDetectionManager or other sources
        // For now, return basic game over info
        return (true, "Game Over", PieceColor.White);
    }
    
    /// <summary>
    /// Get the human player color in Human vs AI mode
    /// </summary>
    private PieceColor GetHumanPlayerColor()
    {
        if (TurnManager.Instance == null) return PieceColor.White;
        
        // In Human vs AI mode, find the human player
        bool whiteIsHuman = !TurnManager.Instance.IsPlayerAI(PieceColor.White);
        bool blackIsHuman = !TurnManager.Instance.IsPlayerAI(PieceColor.Black);
        
        if (whiteIsHuman && !blackIsHuman)
            return PieceColor.White;
        else if (blackIsHuman && !whiteIsHuman)
            return PieceColor.Black;
        
        // Default to White if unclear
        return PieceColor.White;
    }
    
    /// <summary>
    /// Get current game status text for display in menu
    /// </summary>
    private string GetGameStatusText()
    {
        if (TurnManager.Instance == null) return "";
        
        PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
        bool isAI = TurnManager.Instance.IsPlayerAI(currentPlayer);
        
        string status = $"Current Turn: {currentPlayer}";
        if (isAI)
        {
            status += " (AI)";
        }
        
        // Add check status
        if (CheckDetectionManager.Instance != null)
        {
            bool inCheck = CheckDetectionManager.Instance.IsKingInCheck(currentPlayer);
            if (inCheck)
            {
                status += " - IN CHECK!";
            }
        }
        
        return status;
    }
}