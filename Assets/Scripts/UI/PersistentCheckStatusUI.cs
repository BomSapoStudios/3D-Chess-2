using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Enhanced status bar UI component that displays comprehensive game status
/// Shows check status, current turn, and game phase in a persistent top bar
/// Features: Turn indicators, check status icons, responsive design for mobile
/// Uses event-driven updates for instant response to all game state changes
/// </summary>
public class PersistentCheckStatusUI : MonoBehaviour
{
    [Header("Persistent Status Settings")]
    public bool enablePersistentStatus = true;
    public bool enableEventDrivenUpdates = true; // Use events instead of polling
    
    [Header("Visual Settings")]
    public int fontSize = 18;
    public Color safeColor = Color.green;
    public Color checkColor = Color.red;
    public Color currentTurnColor = new Color(0.3f, 0.6f, 1f, 1f); // Blue for current turn
    public Color backgroundColor = new Color(0.078f, 0f, 0.18f, 0.45f); // neon ground (#14002E), see-through so backdrops show
    
    [Header("Status Icons")]
    // Plain text: the game's font has no glyphs for check marks, warning signs or emoji
    public string safeIcon = "ok";
    public string checkIcon = "CHECK!";
    public string checkmateIcon = "MATE";
    public string turnIcon = ">";
    
    // UI Components
    private Canvas statusCanvas;
    private GameObject statusPanel;
    private Text statusText;
    private Image backgroundImage;
    
    // New Game Button Components
    private GameObject newGameButton;
    private UnityEngine.UI.Button newGameButtonComponent;
    private UnityEngine.UI.Image newGameButtonImage;
    private TMPro.TextMeshProUGUI newGameButtonText;
    
    // Status tracking - MULTI-PLAYER SUPPORT: Track check status for all active players
    private Dictionary<PieceColor, bool> playerCheckStatus = new Dictionary<PieceColor, bool>();
    private Dictionary<PieceColor, bool> lastPlayerCheckStatus = new Dictionary<PieceColor, bool>();

    // Turn tracking
    private PieceColor currentTurn = PieceColor.White;
    private GameState currentGameState = GameState.PiecePlacement;

    // Last move tracking for chess notation display
    private string lastMoveNotation = "";
    private bool hasLastMove = false;
    
    // Game end tracking
    private bool gameEnded = false;
    private string gameEndMessage = "";
    
    // Chaos mode tracking
    private bool chaosEventActive = false;
    private string chaosMessage = "";
    private float chaosMessageTimestamp = 0f;
    private const float chaosMessageDuration = 3f;
    
    // Update coroutine
    private Coroutine statusUpdateCoroutine;

    // Animation state management for delayed status updates
    private bool isWaitingForAnimationBeforeStatusUpdate = false;
    private (PieceColor previousPlayer, PieceColor newCurrentPlayer)? pendingTurnUpdate = null;

    public static PersistentCheckStatusUI Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("PersistentCheckStatusUI: Instance created");
            CreatePersistentStatusUI();
        }
        else
        {
            Debug.LogWarning("PersistentCheckStatusUI: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        if (enablePersistentStatus)
        {
            if (enableEventDrivenUpdates)
            {
                StartEventDrivenMonitoring();
            }
            else
            {
                StartStatusMonitoring();
            }
        }
    }
    
    private void OnDestroy()
    {
        // Clean up polling coroutine if used
        if (statusUpdateCoroutine != null)
        {
            StopCoroutine(statusUpdateCoroutine);
        }

        // Clean up animation waiting state
        if (isWaitingForAnimationBeforeStatusUpdate)
        {
            CleanupAnimationWaiting();
        }

        // Clean up event subscriptions
        if (enableEventDrivenUpdates && CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.OnKingInCheck -= OnKingInCheckEvent;
            CheckDetectionManager.Instance.OnCheckResolved -= OnCheckResolvedEvent;
        }

        // Clean up game end event subscriptions
        if (GameEndDetectionManager.Instance != null)
        {
            GameEndDetectionManager.Instance.OnCheckmate -= OnCheckmateEvent;
            GameEndDetectionManager.Instance.OnConquest -= OnConquestEvent;
            GameEndDetectionManager.Instance.OnGameEnd -= OnGameEndEvent;
            GameEndDetectionManager.Instance.OnStalemate -= OnStalemateEvent;
            GameEndDetectionManager.Instance.OnDrawAccepted -= OnDrawAcceptedEvent;
            GameEndDetectionManager.Instance.OnForfeit -= OnForfeitEvent;
        }

        // Clean up turn manager event subscriptions
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= OnTurnChangedEvent;
        }

        // Clean up game state manager event subscriptions
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChangedEvent;
        }

        // Clean up chaos mode event subscriptions
        if (ChaosRotationManager.Instance != null)
        {
            ChaosRotationManager.Instance.OnChaosEvent -= OnChaosEvent;
            ChaosRotationManager.Instance.OnChaosRotationCompleted -= OnChaosRotationCompleted;
        }
    }
    
    /// <summary>
    /// Create the persistent status UI elements
    /// </summary>
    private static Sprite fadeDown;

    // Opaque at the top, easing to fully clear at the bottom
    private static Sprite FadeDownSprite()
    {
        if (fadeDown != null) return fadeDown;
        const int h = 64;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        {
            float t = (float)y / (h - 1);              // 0 at the bottom row, 1 at the top
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.6f - 0.1f));
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        fadeDown = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
        return fadeDown;
    }

    private void CreatePersistentStatusUI()
    {
        Debug.Log("PersistentCheckStatusUI: Creating persistent status UI");
        
        // Create canvas for status display
        GameObject canvasObject = new GameObject("PersistentCheckStatusCanvas");
        canvasObject.transform.SetParent(transform);
        
        statusCanvas = canvasObject.AddComponent<Canvas>();
        statusCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        statusCanvas.sortingOrder = 110; // Higher than other UI elements
        
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(375f, 812f); // iPhone X reference
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        
        canvasObject.AddComponent<GraphicRaycaster>();
        
        // Create background panel
        statusPanel = new GameObject("StatusPanel");
        statusPanel.transform.SetParent(canvasObject.transform);
        
        backgroundImage = statusPanel.AddComponent<Image>();
        backgroundImage.color = backgroundColor;
        // Fade out toward the bottom so the bar has no hard edge across the backdrop
        backgroundImage.sprite = FadeDownSprite();
        backgroundImage.type = Image.Type.Simple;
        
        RectTransform panelRect = statusPanel.GetComponent<RectTransform>();
        
        // Position at top-center of screen as full-width status bar
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(0, -35f); // 35 pixels from top
        panelRect.sizeDelta = new Vector2(0f, 70f); // Full width, taller for better visibility
        
        // Create status text
        GameObject textObject = new GameObject("StatusText");
        textObject.transform.SetParent(statusPanel.transform);
        
        statusText = textObject.AddComponent<Text>();
        statusText.text = "✓ White: Safe | Turn: White ▶ | Black: Safe ✓";
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = fontSize;
        statusText.fontStyle = FontStyle.Bold;
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.color = NeonTheme.Lime;
        if (NeonTheme.BodyFont != null) statusText.font = NeonTheme.BodyFont;
        
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        Debug.Log("PersistentCheckStatusUI: UI creation complete");
        
        // Create the new game button (initially hidden)
        CreateNewGameButton();
    }
    
    /// <summary>
    /// Create the new game button that appears on game over
    /// </summary>
    private void CreateNewGameButton()
    {
        Debug.Log("PersistentCheckStatusUI: Creating new game button");
        
        // Create the button GameObject
        newGameButton = new GameObject("NewGameButton");
        newGameButton.transform.SetParent(statusCanvas.transform, false);
        
        // Add UI components
        newGameButtonComponent = newGameButton.AddComponent<UnityEngine.UI.Button>();
        newGameButtonImage = newGameButton.AddComponent<UnityEngine.UI.Image>();
        
        // Position the button on the left side of the screen to mirror menu button
        RectTransform buttonRect = newGameButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0, 1);
        buttonRect.anchorMax = new Vector2(0, 1);
        buttonRect.anchoredPosition = new Vector2(70, -100); // Left side, same vertical position as menu button
        buttonRect.sizeDelta = new Vector2(120, 50); // Same size as menu button
        
        // Create button text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(newGameButton.transform, false);
        
        newGameButtonText = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        newGameButtonText.text = "New Game";
        newGameButtonText.fontSize = 18;
        newGameButtonText.color = Color.white;
        newGameButtonText.alignment = TMPro.TextAlignmentOptions.Center;
        
        // Position the text
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;
        
        // Style the button with a green tint to distinguish from menu button
        newGameButtonImage.color = new Color(0.1f, 0.4f, 0.1f, 0.9f); // Dark green
        
        // Add button states for better feedback
        UnityEngine.UI.ColorBlock colors = newGameButtonComponent.colors;
        colors.normalColor = new Color(0.1f, 0.4f, 0.1f, 0.9f); // Dark green
        colors.highlightedColor = new Color(0.2f, 0.6f, 0.2f, 1f); // Lighter green on hover
        colors.pressedColor = new Color(0.05f, 0.3f, 0.05f, 1f); // Darker green when pressed
        colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        newGameButtonComponent.colors = colors;
        
        // Add click listener
        newGameButtonComponent.onClick.AddListener(() => {
            Debug.Log("PersistentCheckStatusUI: New Game button clicked");
            HandleNewGame();
        });
        
        // Drawn in the neon style by OnGUI; this object only tracks when it's showing
        newGameButtonImage.enabled = false;
        newGameButtonText.enabled = false;

        // Initially hide the button
        newGameButton.SetActive(false);
        
        Debug.Log("PersistentCheckStatusUI: New Game button created and hidden");
    }
    
    /// <summary>
    /// Show the new game button when game ends
    /// </summary>
    private void ShowNewGameButton()
    {
        if (newGameButton != null)
        {
            newGameButton.SetActive(true);
            Debug.Log("PersistentCheckStatusUI: New Game button shown");
        }
        else
        {
            Debug.LogWarning("PersistentCheckStatusUI: Cannot show New Game button - button is null");
        }
    }
    
    /// <summary>
    /// Hide the new game button when starting a new game
    /// </summary>
    private void HideNewGameButton()
    {
        if (newGameButton != null)
        {
            newGameButton.SetActive(false);
            Debug.Log("PersistentCheckStatusUI: New Game button hidden");
        }
        else
        {
            Debug.LogWarning("PersistentCheckStatusUI: Cannot hide New Game button - button is null");
        }
    }
    
    /// <summary>
    /// Handle new game button click - return to main menu for clean restart
    /// </summary>
    /// <summary>The New Game button (shown at game over), in the same place and style as Turn Planner</summary>
    private void OnGUI()
    {
        if (newGameButton == null || !newGameButton.activeSelf || RematchUI.Showing) return; // the result panel has its own Home
        TouchGUI.Begin();
        if (TouchGUI.NeonButton(new Rect(16, 76, 150, 44), "New Game", NeonTheme.Pink, NeonTheme.Cyan, fontSize: 16)) HandleNewGame();
        TouchGUI.End();
    }

    private void HandleNewGame()
    {
        Debug.Log("PersistentCheckStatusUI: Handling new game request");
        
        // Hide the button immediately to prevent multiple clicks
        HideNewGameButton();
        
        // Use SceneController to return to main menu for clean restart
        if (SceneController.Instance != null)
        {
            Debug.Log("PersistentCheckStatusUI: Loading main menu for new game");
            SceneController.Instance.LoadMainMenu();
        }
        else
        {
            Debug.LogError("PersistentCheckStatusUI: Cannot start new game - SceneController.Instance not available");
            // Show the button again if we failed to start new game
            ShowNewGameButton();
        }
    }
    
    /// <summary>
    /// Start event-driven status monitoring (recommended)
    /// </summary>
    private void StartEventDrivenMonitoring()
    {
        Debug.Log("PersistentCheckStatusUI: Starting event-driven status monitoring");
        
        // Wait for CheckDetectionManager to be available
        StartCoroutine(SubscribeToEventsWithRetry());
        
        // Set initial status
        UpdateInitialStatus();
    }
    
    /// <summary>
    /// Start continuous status monitoring (legacy fallback)
    /// </summary>
    private void StartStatusMonitoring()
    {
        Debug.Log("PersistentCheckStatusUI: Starting continuous status monitoring");
        
        if (statusUpdateCoroutine != null)
        {
            StopCoroutine(statusUpdateCoroutine);
        }
        
        statusUpdateCoroutine = StartCoroutine(ContinuousStatusUpdate());
    }
    
    /// <summary>
    /// Subscribe to CheckDetectionManager events with retry mechanism
    /// </summary>
    private System.Collections.IEnumerator SubscribeToEventsWithRetry()
    {
        int retryCount = 0;
        const int maxRetries = 10;
        const float retryDelay = 0.5f;
        
        while (retryCount < maxRetries)
        {
            if (CheckDetectionManager.Instance != null)
            {
                try
                {
                    CheckDetectionManager.Instance.OnKingInCheck += OnKingInCheckEvent;
                    CheckDetectionManager.Instance.OnCheckResolved += OnCheckResolvedEvent;
                    Debug.Log($"PersistentCheckStatusUI: Successfully subscribed to check detection events (attempt {retryCount + 1})");
                    
                    // Also subscribe to game end events from GameEndDetectionManager
                    if (GameEndDetectionManager.Instance != null)
                    {
                        GameEndDetectionManager.Instance.OnCheckmate += OnCheckmateEvent;
                        GameEndDetectionManager.Instance.OnConquest += OnConquestEvent;
                        GameEndDetectionManager.Instance.OnGameEnd += OnGameEndEvent;
                        GameEndDetectionManager.Instance.OnStalemate += OnStalemateEvent;
                        GameEndDetectionManager.Instance.OnDrawAccepted += OnDrawAcceptedEvent;
                        GameEndDetectionManager.Instance.OnForfeit += OnForfeitEvent;
                        Debug.Log("PersistentCheckStatusUI: Successfully subscribed to game end, conquest, and draw/forfeit events");
                    }
                    
                    // Subscribe to turn manager events for current player tracking
                    if (TurnManager.Instance != null)
                    {
                        TurnManager.Instance.OnTurnChanged += OnTurnChangedEvent;
                        currentTurn = TurnManager.Instance.GetCurrentPlayer();
                        Debug.Log($"PersistentCheckStatusUI: Successfully subscribed to turn events, current turn: {currentTurn}");
                    }
                    
                    // Subscribe to game state manager events for phase tracking
                    if (GameStateManager.Instance != null)
                    {
                        GameStateManager.Instance.OnStateChanged += OnGameStateChangedEvent;
                        currentGameState = GameStateManager.Instance.currentState;
                        Debug.Log($"PersistentCheckStatusUI: Successfully subscribed to game state events, current state: {currentGameState}");
                    }
                    
                    // Subscribe to chaos mode events for visual feedback
                    if (ChaosRotationManager.Instance != null)
                    {
                        ChaosRotationManager.Instance.OnChaosEvent += OnChaosEvent;
                        ChaosRotationManager.Instance.OnChaosRotationCompleted += OnChaosRotationCompleted;
                        Debug.Log("PersistentCheckStatusUI: Successfully subscribed to chaos events");
                    }
                    
                    // Validate subscription
                    int kingInCheckHandlers = CheckDetectionManager.Instance.OnKingInCheck?.GetInvocationList()?.Length ?? 0;
                    Debug.Log($"PersistentCheckStatusUI: OnKingInCheck now has {kingInCheckHandlers} handlers");
                    
                    yield break; // Success, exit retry loop
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"PersistentCheckStatusUI: Exception subscribing to events: {e.Message}");
                }
            }
            
            retryCount++;
            Debug.LogWarning($"PersistentCheckStatusUI: CheckDetectionManager not ready, retry {retryCount}/{maxRetries}");
            yield return new WaitForSeconds(retryDelay);
        }
        
        Debug.LogError("PersistentCheckStatusUI: Failed to subscribe to check detection events after maximum retries");
    }
    
    /// <summary>
    /// Set initial status display
    /// MULTI-PLAYER SUPPORT: Initialize dictionary from active players
    /// </summary>
    private void UpdateInitialStatus()
    {
        Debug.Log("PersistentCheckStatusUI: Setting initial status display");

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

        if (CheckDetectionManager.Instance != null &&
            GameStateManager.Instance != null &&
            GameStateManager.Instance.CanMovePieces())
        {
            // Check actual status during gameplay for all active players
            foreach (PieceColor player in activePlayers)
            {
                bool currentStatus = CheckDetectionManager.Instance.IsKingInCheck(player);
                playerCheckStatus[player] = currentStatus;
            }

            UpdateStatusDisplay();

            // Log status for all players
            string statusLog = "PersistentCheckStatusUI: Initial status set - ";
            foreach (var kvp in playerCheckStatus)
            {
                statusLog += $"{kvp.Key}: {(kvp.Value ? "CHECK" : "Safe")} | ";
            }
            Debug.Log(statusLog.TrimEnd(' ', '|'));
        }
        else
        {
            // During placement phase or when manager not ready, show safe for all players
            foreach (PieceColor player in activePlayers)
            {
                playerCheckStatus[player] = false;
            }

            UpdateStatusDisplay();
            Debug.Log($"PersistentCheckStatusUI: Initial status set to Safe for all {activePlayers.Count} players");
        }
    }
    
    /// <summary>
    /// Force synchronization with actual check detection state
    /// MULTI-PLAYER SUPPORT: Synchronize all active players
    /// </summary>
    private void SynchronizeWithCheckDetection()
    {
        if (CheckDetectionManager.Instance == null ||
            GameStateManager.Instance == null ||
            !GameStateManager.Instance.CanMovePieces())
        {
            return;
        }

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

        // Check for discrepancies
        bool needsUpdate = false;
        Dictionary<PieceColor, bool> actualCheckStates = new Dictionary<PieceColor, bool>();

        foreach (PieceColor player in activePlayers)
        {
            bool actualInCheck = CheckDetectionManager.Instance.IsKingInCheck(player);
            actualCheckStates[player] = actualInCheck;

            if (!playerCheckStatus.ContainsKey(player) || playerCheckStatus[player] != actualInCheck)
            {
                needsUpdate = true;
            }
        }

        // Update UI state to match actual detection
        if (needsUpdate)
        {
            string beforeLog = "PersistentCheckStatusUI: Synchronizing state - UI had ";
            foreach (var kvp in playerCheckStatus)
            {
                beforeLog += $"{kvp.Key}:{kvp.Value} ";
            }

            string afterLog = "Detection has ";
            foreach (var kvp in actualCheckStates)
            {
                afterLog += $"{kvp.Key}:{kvp.Value} ";
            }

            Debug.Log(beforeLog + "| " + afterLog);

            // Update to actual states
            playerCheckStatus = new Dictionary<PieceColor, bool>(actualCheckStates);
            UpdateStatusDisplay();

            // Log final status
            string statusLog = "PersistentCheckStatusUI: Synchronized - ";
            foreach (var kvp in playerCheckStatus)
            {
                statusLog += $"{kvp.Key}: {(kvp.Value ? "CHECK" : "Safe")} | ";
            }
            Debug.Log(statusLog.TrimEnd(' ', '|'));
        }
    }
    
    /// <summary>
    /// Event handler for when a king is in check
    /// MULTI-PLAYER SUPPORT: Now supports all player colors dynamically
    /// </summary>
    private void OnKingInCheckEvent(PieceColor kingColor)
    {
        Debug.Log($"🚨 PersistentCheckStatusUI.OnKingInCheckEvent: {kingColor} king is in check!");

        // Update check status for this specific player
        playerCheckStatus[kingColor] = true;

        // Only update display if in Playing state - prevents interference during state transitions
        if (currentGameState == GameState.Playing)
        {
            UpdateStatusDisplay();
        }
        else
        {
            Debug.Log($"PersistentCheckStatusUI: Skipping display update - not in Playing state (current: {currentGameState})");
        }

        // Log status for all players
        string statusLog = "PersistentCheckStatusUI: Event-driven update completed - ";
        foreach (var kvp in playerCheckStatus)
        {
            statusLog += $"{kvp.Key}: {(kvp.Value ? "CHECK" : "Safe")} | ";
        }
        Debug.Log(statusLog.TrimEnd(' ', '|'));
    }
    
    /// <summary>
    /// Event handler for when check is resolved
    /// MULTI-PLAYER SUPPORT: Now supports all player colors dynamically
    /// </summary>
    private void OnCheckResolvedEvent(PieceColor kingColor)
    {
        Debug.Log($"✅ PersistentCheckStatusUI.OnCheckResolvedEvent: {kingColor} check resolved!");

        // Update check status for this specific player
        playerCheckStatus[kingColor] = false;

        // Only update display if in Playing state - prevents interference during state transitions
        if (currentGameState == GameState.Playing)
        {
            UpdateStatusDisplay();
        }
        else
        {
            Debug.Log($"PersistentCheckStatusUI: Skipping display update - not in Playing state (current: {currentGameState})");
        }

        // Log status for all players
        string statusLog = "PersistentCheckStatusUI: Event-driven update completed - ";
        foreach (var kvp in playerCheckStatus)
        {
            statusLog += $"{kvp.Key}: {(kvp.Value ? "CHECK" : "Safe")} | ";
        }
        Debug.Log(statusLog.TrimEnd(' ', '|'));
    }
    
    /// <summary>
    /// Event handler for checkmate - now only logs, actual game end handled by OnGameEnd
    /// </summary>
    private void OnCheckmateEvent(PieceColor checkmatedPlayer)
    {
        // Don't set gameEnded here - let OnGameEnd handle it for final checkmate
        // For conquest scenarios, OnConquest will handle the UI update
        Debug.Log($"🏁 PersistentCheckStatusUI.OnCheckmateEvent: Checkmate detected for {checkmatedPlayer}");
    }

    /// <summary>
    /// Event handler for conquest - shows temporary message when player conquered in multiplayer
    /// </summary>
    private void OnConquestEvent(PieceColor conqueror, PieceColor defeated, int piecesTransferred)
    {
        int remainingPlayers = PlayerManager.Instance != null ? PlayerManager.Instance.GetActivePlayerCount() : 0;

        string conquestMessage = $"CONQUEST! {conqueror.ToString().ToUpper()} CAPTURED {defeated.ToString().ToUpper()}'S PIECES! {remainingPlayers} PLAYERS REMAIN";

        Debug.Log($"🎨 PersistentCheckStatusUI.OnConquestEvent: {conquestMessage}");

        // Show conquest message temporarily (3 seconds), then return to normal status
        StartCoroutine(ShowTemporaryConquestMessage(conquestMessage));
    }

    /// <summary>
    /// Coroutine to show conquest message temporarily then return to normal status
    /// </summary>
    private System.Collections.IEnumerator ShowTemporaryConquestMessage(string message)
    {
        // Save current status to restore later
        bool wasGameEnded = gameEnded;
        string previousMessage = gameEndMessage;

        // Temporarily show conquest message
        gameEnded = true; // Temporarily set to show message instead of turn status
        gameEndMessage = message;
        UpdateStatusDisplay();

        // Wait 3 seconds
        yield return new UnityEngine.WaitForSeconds(3f);

        // Restore normal status
        gameEnded = wasGameEnded; // Should be false for conquest
        gameEndMessage = previousMessage;
        UpdateStatusDisplay(); // Will show normal turn/check status again

        Debug.Log("🎨 PersistentCheckStatusUI: Conquest message cleared, returning to normal status");
    }

    /// <summary>
    /// Event handler for game end - sets final game over state
    /// </summary>
    private void OnGameEndEvent(string reason)
    {
        gameEnded = true;
        gameEndMessage = reason.ToUpper();

        Debug.Log($"🏁 PersistentCheckStatusUI.OnGameEndEvent: {gameEndMessage}");
        UpdateStatusDisplay();
        ShowNewGameButton();
    }

    /// <summary>
    /// Event handler for stalemate
    /// </summary>
    private void OnStalemateEvent(PieceColor stalematedPlayer)
    {
        gameEnded = true;
        gameEndMessage = "STALEMATE! GAME IS A DRAW!";
        
        Debug.Log($"🏁 PersistentCheckStatusUI.OnStalemateEvent: {gameEndMessage}");
        UpdateStatusDisplay();
        ShowNewGameButton();
    }
    
    /// <summary>
    /// Event handler for draw accepted
    /// </summary>
    private void OnDrawAcceptedEvent(PieceColor acceptingPlayer)
    {
        gameEnded = true;
        gameEndMessage = "DRAW ACCEPTED! GAME IS A DRAW!";
        
        Debug.Log($"🏁 PersistentCheckStatusUI.OnDrawAcceptedEvent: {gameEndMessage}");
        UpdateStatusDisplay();
        ShowNewGameButton();
    }
    
    /// <summary>
    /// Event handler for forfeit
    /// </summary>
    private void OnForfeitEvent(PieceColor forfeitingPlayer)
    {
        PieceColor winner = GameEndDetectionManager.ForfeitWinner(forfeitingPlayer);
        gameEnded = true;
        gameEndMessage = $"{winner.ToString().ToUpper()} WINS! ({forfeitingPlayer.ToString().ToUpper()} FORFEITED)";
        
        Debug.Log($"🏁 PersistentCheckStatusUI.OnForfeitEvent: {gameEndMessage}");
        UpdateStatusDisplay();
        ShowNewGameButton();
    }
    
    /// <summary>
    /// Event handler for turn changes
    /// Delays status update until piece animations complete for smooth visual transitions
    /// </summary>
    private void OnTurnChangedEvent(PieceColor previousPlayer, PieceColor newCurrentPlayer)
    {
        Debug.Log($"🔄 PersistentCheckStatusUI.OnTurnChangedEvent: Turn changed from {previousPlayer} to {newCurrentPlayer}");

        // Check if any pieces are still animating from the previous turn
        bool piecesAnimating = AnyPiecesStillAnimating();
        Debug.Log($"🔄 PersistentCheckStatusUI: AnyPiecesStillAnimating() = {piecesAnimating}");

        if (piecesAnimating)
        {
            // Pieces still animating - defer status update until animations complete
            Debug.Log($"🎬 PersistentCheckStatusUI: Pieces still animating, deferring status update for turn change to {newCurrentPlayer}");
            isWaitingForAnimationBeforeStatusUpdate = true;
            pendingTurnUpdate = (previousPlayer, newCurrentPlayer);
            SubscribeToAnimationCompletion();
        }
        else
        {
            // No animations - update status immediately
            Debug.Log($"🔄 PersistentCheckStatusUI: No animations active, updating status immediately");
            currentTurn = newCurrentPlayer;
            UpdateStatusDisplay();
        }
    }
    
    /// <summary>
    /// Event handler for game state changes
    /// </summary>
    private void OnGameStateChangedEvent(GameState newState)
    {
        Debug.Log($"🎮 PersistentCheckStatusUI.OnGameStateChangedEvent: Game state changed to {newState}");

        currentGameState = newState;

        // Reset game end status when transitioning to new game
        if (newState == GameState.PiecePlacement || newState == GameState.Playing)
        {
            gameEnded = false;
            gameEndMessage = "";
            HideNewGameButton();
        }

        // TURN SYNC: Force sync with TurnManager when entering Playing state
        // This ensures turn indicator shows correct player even if events fired out of order
        if (newState == GameState.Playing && TurnManager.Instance != null)
        {
            PieceColor actualCurrentPlayer = TurnManager.Instance.GetCurrentPlayer();
            if (currentTurn != actualCurrentPlayer)
            {
                Debug.Log($"🔄 PersistentCheckStatusUI: Syncing turn on state transition - was {currentTurn}, now {actualCurrentPlayer}");
                currentTurn = actualCurrentPlayer;
            }
        }

        UpdateStatusDisplay();
    }
    
    /// <summary>
    /// Event handler for chaos mode events
    /// </summary>
    private void OnChaosEvent(string message)
    {
        Debug.Log($"🌪️ PersistentCheckStatusUI.OnChaosEvent: {message}");

        chaosEventActive = true;
        chaosMessage = message;
        chaosMessageTimestamp = Time.time;

        UpdateStatusDisplay();
    }

    /// <summary>
    /// Event handler for chaos rotation completion
    /// Clears the chaos message when animation finishes
    /// </summary>
    private void OnChaosRotationCompleted(ChaosRotationType rotationType)
    {
        Debug.Log($"🌪️ PersistentCheckStatusUI.OnChaosRotationCompleted: Clearing chaos message after {rotationType} completion");

        chaosEventActive = false;
        chaosMessage = "";

        UpdateStatusDisplay();
    }

    /// <summary>
    /// Stop status monitoring
    /// </summary>
    private void StopStatusMonitoring()
    {
        Debug.Log("PersistentCheckStatusUI: Stopping status monitoring");
        
        if (statusUpdateCoroutine != null)
        {
            StopCoroutine(statusUpdateCoroutine);
            statusUpdateCoroutine = null;
        }
    }
    
    /// <summary>
    /// Continuous status monitoring coroutine
    /// MULTI-PLAYER SUPPORT: Track all active players dynamically
    /// </summary>
    private System.Collections.IEnumerator ContinuousStatusUpdate()
    {
        while (enablePersistentStatus)
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

            // Wait for CheckDetectionManager to be available
            if (CheckDetectionManager.Instance != null &&
                GameStateManager.Instance != null &&
                GameStateManager.Instance.CanMovePieces())
            {
                // Get current check status for all active players
                bool statusChanged = false;
                Dictionary<PieceColor, bool> currentStatuses = new Dictionary<PieceColor, bool>();

                foreach (PieceColor player in activePlayers)
                {
                    bool currentStatus = CheckDetectionManager.Instance.IsKingInCheck(player);
                    currentStatuses[player] = currentStatus;

                    // Check if this player's status changed
                    if (!lastPlayerCheckStatus.ContainsKey(player) || lastPlayerCheckStatus[player] != currentStatus)
                    {
                        statusChanged = true;
                    }
                }

                // Update status if changed
                if (statusChanged)
                {
                    playerCheckStatus = new Dictionary<PieceColor, bool>(currentStatuses);
                    UpdateStatusDisplay();
                    lastPlayerCheckStatus = new Dictionary<PieceColor, bool>(currentStatuses);

                    // Log status for all players
                    string statusLog = "PersistentCheckStatusUI: Status updated - ";
                    foreach (var kvp in playerCheckStatus)
                    {
                        statusLog += $"{kvp.Key}: {(kvp.Value ? "CHECK" : "Safe")} | ";
                    }
                    Debug.Log(statusLog.TrimEnd(' ', '|'));
                }
            }
            else if (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
            {
                // During placement phase, show "Safe" for all players
                bool anyPlayerInCheck = false;
                foreach (var kvp in playerCheckStatus)
                {
                    if (kvp.Value)
                    {
                        anyPlayerInCheck = true;
                        break;
                    }
                }

                if (anyPlayerInCheck)
                {
                    foreach (PieceColor player in activePlayers)
                    {
                        playerCheckStatus[player] = false;
                    }
                    UpdateStatusDisplay();
                    Debug.Log($"PersistentCheckStatusUI: Placement phase - all {activePlayers.Count} players safe");
                }
            }

            yield return new WaitForSeconds(1.0f); // Fallback polling interval
        }

        Debug.Log("PersistentCheckStatusUI: Status monitoring stopped");
    }
    
    /// <summary>
    /// Update the enhanced status display with check states, turn indicator, and game phase
    /// </summary>
    private void UpdateStatusDisplay()
    {
        if (statusText == null) return;

        string statusMessage;

        // CHAOS MODE FIX: Removed timeout check - chaos message now clears via OnChaosRotationCompleted event
        // This ensures the message displays for the full animation duration (2-6+ seconds)
        // The old 3-second timeout was too short and caused the message to flicker

        // Show chaos message if active (highest priority)
        if (chaosEventActive)
        {
            statusMessage = chaosMessage;
            statusText.text = statusMessage;
            
            // Chaos spins: yellow neon on the dark bar
            SetBannerStyle(NeonTheme.Yellow, NeonTheme.Pink, display: false);
        }
        // Show game end message if game is over
        else if (gameEnded)
        {
            statusMessage = gameEndMessage;
            statusText.text = statusMessage;
            
            // Neon banner for the result: display lettering with a hard drop, colored by outcome
            if (gameEndMessage.Contains("CHECKMATE") || gameEndMessage.Contains("OVERRUN"))
                SetBannerStyle(NeonTheme.Pink, NeonTheme.Cyan, display: true);
            else if (gameEndMessage.Contains("STALEMATE") || gameEndMessage.Contains("DRAW"))
                SetBannerStyle(NeonTheme.Yellow, NeonTheme.Pink, display: true);
            else
                SetBannerStyle(NeonTheme.Lime, NeonTheme.Pink, display: true);
            
            // Add pulsing effect for game end
            StartCoroutine(PulseEffect());
        }
        else
        {
            // MULTI-PLAYER SUPPORT: Dynamic status format based on player count
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

            // Ensure all active players are in the status dictionary
            foreach (PieceColor player in activePlayers)
            {
                if (!playerCheckStatus.ContainsKey(player))
                {
                    playerCheckStatus[player] = false; // Default to safe
                }
            }

            int playerCount = activePlayers.Count;

            // Add turn indicator highlighting
            string turnIndicator = "";
            if (currentGameState == GameState.Playing)
            {
                // DEFENSIVE SYNC: Verify currentTurn matches TurnManager to prevent display desync
                if (TurnManager.Instance != null)
                {
                    PieceColor actualCurrentPlayer = TurnManager.Instance.GetCurrentPlayer();
                    if (currentTurn != actualCurrentPlayer)
                    {
                        Debug.LogWarning($"⚠️ PersistentCheckStatusUI: Turn indicator out of sync! UI shows {currentTurn}, TurnManager shows {actualCurrentPlayer}. Syncing...");
                        currentTurn = actualCurrentPlayer;
                    }
                }

                turnIndicator = $"Turn: {currentTurn} {turnIcon}";
            }
            else if (currentGameState == GameState.PiecePlacement)
            {
                turnIndicator = ""; // Empty - "place pieces" buttons already indicate placement phase
            }
            else
            {
                turnIndicator = $"State: {currentGameState}";
            }

            // Build status message using chess notation
            // Format: "Turn: WHITE ▶ | Last: WQ B3c→B3d | Check: BK@A1a ⚠️ ← WQ@B3d"

            List<string> statusParts = new List<string>();

            // Part 1: Turn indicator
            statusParts.Add(turnIndicator);

            // Part 2: Last move (if available)
            if (hasLastMove && !string.IsNullOrEmpty(lastMoveNotation))
            {
                statusParts.Add($"Last: {lastMoveNotation}");
            }

            // Part 3: Check status with attacker positions
            List<string> checkMessages = new List<string>();
            foreach (PieceColor player in activePlayers)
            {
                if (playerCheckStatus[player])
                {
                    // Get the king that's in check
                    ChessPiece threatenedKing = GetKingForPlayer(player);

                    if (threatenedKing != null && CheckDetectionManager.Instance != null)
                    {
                        // Get all attackers threatening this king
                        CheckThreatInfo threatInfo = CheckDetectionManager.Instance.GetCheckThreats(player);

                        if (threatInfo != null && threatInfo.HasThreats)
                        {
                            if (threatInfo.attackingPieces.Count == 1)
                            {
                                // Single attacker format
                                string checkNotation = ChessNotationConverter.FormatCheckStatus(threatenedKing, threatInfo.attackingPieces[0]);
                                checkMessages.Add(checkNotation);
                            }
                            else
                            {
                                // Multiple attackers format (double check)
                                string checkNotation = ChessNotationConverter.FormatCheckStatusMultipleAttackers(threatenedKing, threatInfo.attackingPieces);
                                checkMessages.Add(checkNotation);
                            }
                        }
                    }
                }
            }

            // Add check messages to status
            if (checkMessages.Count > 0)
            {
                statusParts.Add($"Check: {string.Join(" | ", checkMessages)}");
            }

            // Combine all parts
            statusMessage = string.Join(" | ", statusParts);

            // Adjust font size based on player count for better fit
            if (playerCount == 2)
            {
                statusText.fontSize = 18; // Full size for 2 players
            }
            else if (playerCount == 4)
            {
                statusText.fontSize = 16; // Slightly smaller for 4 players
            }
            else // 6-player mode
            {
                statusText.fontSize = 14; // Smallest for 6 players
            }

            statusText.text = statusMessage;

            // Update background color based on game state and check status
            bool anyPlayerInCheck = false;
            foreach (var kvp in playerCheckStatus)
            {
                if (kvp.Value)
                {
                    anyPlayerInCheck = true;
                    break;
                }
            }

            SetBannerStyle(NeonTheme.Lime, Color.clear, display: false);
            if (anyPlayerInCheck)
            {
                // Red background if anyone is in check
                backgroundImage.color = new Color(NeonTheme.Pink.r, NeonTheme.Pink.g, NeonTheme.Pink.b, 0.9f);

                // Add pulsing effect for check status
                StartCoroutine(PulseEffect());
            }
            else if (currentGameState == GameState.PiecePlacement)
            {
                // Blue-tinted background for placement phase
                backgroundImage.color = backgroundColor;
            }
            else
            {
                // Default dark background for normal gameplay
                backgroundImage.color = backgroundColor;
            }
        }
        
        Debug.Log($"PersistentCheckStatusUI: Enhanced display updated - {statusMessage}");
    }
    
    /// <summary>
    /// Pulse effect for check status
    /// MULTI-PLAYER SUPPORT: Check any player in check
    /// </summary>
    private Shadow bannerDrop;

    /// <summary>The bar's look: dark see-through neon ground, text in a neon color, and for results the display font with a hard drop</summary>
    private void SetBannerStyle(Color text, Color drop, bool display)
    {
        if (statusText == null) return;
        backgroundImage.color = display ? new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0.85f) : backgroundImage.color;
        statusText.color = text;
        Font font = display ? NeonTheme.DisplayFont : NeonTheme.BodyFont;
        if (font != null) statusText.font = font;
        statusText.resizeTextForBestFit = display; // long result lines shrink to fit rather than clip
        statusText.resizeTextMaxSize = fontSize + 6;
        statusText.resizeTextMinSize = 12;
        // Results keep clear of the corner buttons (help, menu) and shrink to fit the bar
        RectTransform tr = statusText.rectTransform;
        tr.offsetMin = new Vector2(display ? 110f : 0f, tr.offsetMin.y);
        tr.offsetMax = new Vector2(display ? -110f : 0f, tr.offsetMax.y);
        if (bannerDrop == null) bannerDrop = statusText.gameObject.AddComponent<Shadow>();
        bannerDrop.enabled = drop.a > 0f;
        bannerDrop.effectColor = drop;
        bannerDrop.effectDistance = new Vector2(3f, -3f);
    }

    private System.Collections.IEnumerator PulseEffect()
    {
        // Check if any player is in check
        bool anyPlayerInCheck = false;
        foreach (var kvp in playerCheckStatus)
        {
            if (kvp.Value)
            {
                anyPlayerInCheck = true;
                break;
            }
        }

        if (!anyPlayerInCheck && !gameEnded) yield break;

        float pulseSpeed = 2f;
        float elapsed = 0f;

        while ((anyPlayerInCheck || gameEnded) && elapsed < 2f) // Pulse for 2 seconds max
        {
            // Re-check in case status changed during animation
            anyPlayerInCheck = false;
            foreach (var kvp in playerCheckStatus)
            {
                if (kvp.Value)
                {
                    anyPlayerInCheck = true;
                    break;
                }
            }

            float pulse = (Mathf.Sin(elapsed * pulseSpeed) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.6f, 0.9f, pulse);

            if (backgroundImage != null)
            {
                Color currentColor = backgroundImage.color;
                currentColor.a = alpha;
                backgroundImage.color = currentColor;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Reset to normal alpha
        if (backgroundImage != null && (anyPlayerInCheck || gameEnded))
        {
            Color currentColor = backgroundImage.color;
            currentColor.a = 0.8f;
            backgroundImage.color = currentColor;
        }
    }
    
    /// <summary>
    /// Manually set check status (for testing or external control)
    /// MULTI-PLAYER SUPPORT: Set status for specific player
    /// </summary>
    public void SetCheckStatus(PieceColor player, bool inCheck)
    {
        Debug.Log($"PersistentCheckStatusUI.SetCheckStatus: {player}={inCheck}");

        playerCheckStatus[player] = inCheck;
        UpdateStatusDisplay();
    }

    /// <summary>
    /// Legacy method for 2-player compatibility (for testing or external control)
    /// </summary>
    public void SetCheckStatus(bool whiteCheck, bool blackCheck)
    {
        Debug.Log($"PersistentCheckStatusUI.SetCheckStatus: White={whiteCheck}, Black={blackCheck}");

        playerCheckStatus[PieceColor.White] = whiteCheck;
        playerCheckStatus[PieceColor.Black] = blackCheck;
        UpdateStatusDisplay();
    }
    
    /// <summary>
    /// Manually set turn status (for testing or external control)
    /// </summary>
    public void SetCurrentTurn(PieceColor turn)
    {
        Debug.Log($"PersistentCheckStatusUI.SetCurrentTurn: {turn}");
        
        currentTurn = turn;
        UpdateStatusDisplay();
    }
    
    /// <summary>
    /// Enable or disable the persistent status display
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        enablePersistentStatus = enabled;
        
        if (statusPanel != null)
        {
            statusPanel.SetActive(enabled);
        }
        
        if (enabled)
        {
            if (enableEventDrivenUpdates)
            {
                StartEventDrivenMonitoring();
            }
            else
            {
                StartStatusMonitoring();
            }
        }
        else
        {
            StopStatusMonitoring();
            
            // Clean up event subscriptions
            if (enableEventDrivenUpdates && CheckDetectionManager.Instance != null)
            {
                CheckDetectionManager.Instance.OnKingInCheck -= OnKingInCheckEvent;
                CheckDetectionManager.Instance.OnCheckResolved -= OnCheckResolvedEvent;
            }
        }
        
        Debug.Log($"PersistentCheckStatusUI: Set enabled = {enabled} (event-driven = {enableEventDrivenUpdates})");
    }
    
    /// <summary>
    /// Get current check status for all players
    /// MULTI-PLAYER SUPPORT: Returns dictionary of all player statuses
    /// </summary>
    public Dictionary<PieceColor, bool> GetCurrentStatus()
    {
        return new Dictionary<PieceColor, bool>(playerCheckStatus);
    }

    /// <summary>
    /// Get current check status for specific player
    /// </summary>
    public bool GetPlayerCheckStatus(PieceColor player)
    {
        return playerCheckStatus.ContainsKey(player) ? playerCheckStatus[player] : false;
    }

    /// <summary>
    /// Legacy method for 2-player compatibility
    /// </summary>
    public (bool whiteInCheck, bool blackInCheck) GetCurrentStatusLegacy()
    {
        bool white = playerCheckStatus.ContainsKey(PieceColor.White) ? playerCheckStatus[PieceColor.White] : false;
        bool black = playerCheckStatus.ContainsKey(PieceColor.Black) ? playerCheckStatus[PieceColor.Black] : false;
        return (white, black);
    }
    
    /// <summary>
    /// Force an immediate status update with direct synchronization
    /// MULTI-PLAYER SUPPORT: Synchronize all active players
    /// </summary>
    public void ForceUpdate()
    {
        if (CheckDetectionManager.Instance != null &&
            GameStateManager.Instance != null &&
            GameStateManager.Instance.CanMovePieces())
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

            // Get current check status for all active players
            Dictionary<PieceColor, bool> currentStatuses = new Dictionary<PieceColor, bool>();
            bool stateChanged = false;

            foreach (PieceColor player in activePlayers)
            {
                bool currentStatus = CheckDetectionManager.Instance.IsKingInCheck(player);
                currentStatuses[player] = currentStatus;

                // Check if this player's status changed
                if (!playerCheckStatus.ContainsKey(player) || playerCheckStatus[player] != currentStatus)
                {
                    stateChanged = true;
                }
            }

            // Direct synchronization - bypass event system
            if (stateChanged)
            {
                string beforeLog = "PersistentCheckStatusUI.ForceUpdate: Direct sync - UI had ";
                foreach (var kvp in playerCheckStatus)
                {
                    beforeLog += $"{kvp.Key}:{kvp.Value} ";
                }

                string afterLog = "Detection has ";
                foreach (var kvp in currentStatuses)
                {
                    afterLog += $"{kvp.Key}:{kvp.Value} ";
                }

                Debug.Log(beforeLog + "| " + afterLog);

                playerCheckStatus = new Dictionary<PieceColor, bool>(currentStatuses);
                UpdateStatusDisplay();

                // Log final status
                string statusLog = "PersistentCheckStatusUI.ForceUpdate: Synchronized - ";
                foreach (var kvp in playerCheckStatus)
                {
                    statusLog += $"{kvp.Key}: {(kvp.Value ? "CHECK" : "Safe")} | ";
                }
                Debug.Log(statusLog.TrimEnd(' ', '|'));
            }
            else
            {
                string statusLog = "PersistentCheckStatusUI.ForceUpdate: No changes needed - ";
                foreach (var kvp in playerCheckStatus)
                {
                    statusLog += $"{kvp.Key}={kvp.Value} ";
                }
                Debug.Log(statusLog);
            }
        }
        else
        {
            Debug.Log("PersistentCheckStatusUI.ForceUpdate: Cannot update - managers not ready or not in playing state");
        }
    }
    
    /// <summary>
    /// Update the last move notation for display in status bar
    /// Should be called whenever a move is made
    /// </summary>
    public void SetLastMove(ChessPiece piece, BoardPosition from, BoardPosition to, bool isCapture = false)
    {
        if (piece == null)
        {
            lastMoveNotation = "";
            hasLastMove = false;
            return;
        }

        lastMoveNotation = ChessNotationConverter.FormatMoveWithCapture(piece, from, to, isCapture);
        hasLastMove = true;

        Debug.Log($"PersistentCheckStatusUI: Last move set to {lastMoveNotation}");

        // Update display immediately
        UpdateStatusDisplay();
    }

    /// <summary>
    /// Clear the last move notation
    /// </summary>
    public void ClearLastMove()
    {
        lastMoveNotation = "";
        hasLastMove = false;
        UpdateStatusDisplay();
    }

    /// <summary>
    /// Get the king piece for a specific player
    /// </summary>
    private ChessPiece GetKingForPlayer(PieceColor player)
    {
        if (ChessBoard.Instance == null) return null;

        // Get board dimensions
        Vector3Int boardDims = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Search for the king of the specified color
        for (int x = 0; x < boardDims.x; x++)
        {
            for (int y = 0; y < boardDims.y; y++)
            {
                for (int z = 0; z < boardDims.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);
                    if (piece != null && piece.pieceType == ChessPieceType.King && piece.pieceColor == player)
                    {
                        return piece;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Test method for manual verification
    /// </summary>
    [ContextMenu("Test Check Status")]
    public void TestCheckStatus()
    {
        Debug.Log("PersistentCheckStatusUI: Testing check status display");

        // Test sequence: Safe -> White Check -> Both Check -> Black Check -> Safe
        StartCoroutine(TestSequence());
    }
    
    private System.Collections.IEnumerator TestSequence()
    {
        Debug.Log("PersistentCheckStatusUI: Starting enhanced status bar test sequence");

        // Get all active players
        List<PieceColor> activePlayers = new List<PieceColor>();
        if (PlayerManager.Instance != null)
        {
            activePlayers = PlayerManager.Instance.GetActivePlayers();
        }
        else
        {
            // Fallback to 2-player mode for testing
            activePlayers.Add(PieceColor.White);
            activePlayers.Add(PieceColor.Black);
        }

        // Initialize all players to safe
        foreach (PieceColor player in activePlayers)
        {
            playerCheckStatus[player] = false;
        }

        // Test normal gameplay with turn changes
        currentGameState = GameState.Playing;

        // All safe
        SetCurrentTurn(PieceColor.White);
        UpdateStatusDisplay();
        yield return new WaitForSeconds(2f);

        // First player in check
        if (activePlayers.Count > 0)
        {
            SetCheckStatus(activePlayers[0], true);
            yield return new WaitForSeconds(2f);
        }

        // Turn change
        if (activePlayers.Count > 1)
        {
            SetCurrentTurn(activePlayers[1]);
            yield return new WaitForSeconds(2f);

            // Second player in check
            SetCheckStatus(activePlayers[0], false);
            SetCheckStatus(activePlayers[1], true);
            yield return new WaitForSeconds(2f);
        }

        // Multiple players in check (if 4+ players)
        if (activePlayers.Count >= 4)
        {
            SetCheckStatus(activePlayers[2], true);
            SetCheckStatus(activePlayers[3], true);
            yield return new WaitForSeconds(2f);
        }

        // All safe again
        foreach (PieceColor player in activePlayers)
        {
            playerCheckStatus[player] = false;
        }
        UpdateStatusDisplay();
        yield return new WaitForSeconds(2f);

        // Test placement phase
        currentGameState = GameState.PiecePlacement;
        UpdateStatusDisplay();
        yield return new WaitForSeconds(2f);

        // Back to normal
        currentGameState = GameState.Playing;
        UpdateStatusDisplay();

        Debug.Log("PersistentCheckStatusUI: Enhanced test sequence complete");
    }

    // ===== ANIMATION MANAGEMENT FOR DELAYED STATUS UPDATES =====

    /// <summary>
    /// Check if any pieces on the board are still animating
    /// </summary>
    private bool AnyPiecesStillAnimating()
    {
        if (ChessBoard.Instance == null) return false;

        // Get dynamic board dimensions
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        // Check all positions on the board for animating pieces
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
                        Debug.Log($"🎬 PersistentCheckStatusUI: Found animating piece - {piece.pieceColor} {piece.pieceType} at {position}");
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Subscribe to animation completion events to trigger delayed status updates
    /// </summary>
    private void SubscribeToAnimationCompletion()
    {
        ChessPiece.OnMoveAnimationComplete += OnAnimationCompletedForStatus;
        Debug.Log($"🎬 PersistentCheckStatusUI: Subscribed to animation completion events for status update");
    }

    /// <summary>
    /// Handle animation completion event for status updates
    /// </summary>
    private void OnAnimationCompletedForStatus(ChessPiece animatedPiece)
    {
        if (!isWaitingForAnimationBeforeStatusUpdate)
        {
            Debug.Log($"🎬 PersistentCheckStatusUI: Ignoring animation completion - not waiting for animations");
            return;
        }

        Debug.Log($"🎬 PersistentCheckStatusUI: Animation completed for {animatedPiece.pieceColor} {animatedPiece.pieceType}");

        // Start coroutine to wait one frame before checking animations (ensures all state updates complete)
        StartCoroutine(CheckAndApplyPendingTurnUpdate());
    }

    /// <summary>
    /// Coroutine that waits one frame then checks if animations are complete before applying turn update
    /// </summary>
    private System.Collections.IEnumerator CheckAndApplyPendingTurnUpdate()
    {
        // Wait one frame to ensure all piece movement state updates have completed
        yield return null;

        // Double-check that no pieces are still animating
        if (!AnyPiecesStillAnimating())
        {
            Debug.Log($"🎬 PersistentCheckStatusUI: All animations complete (after safety frame), applying pending turn update");

            if (pendingTurnUpdate.HasValue)
            {
                var (previousPlayer, newCurrentPlayer) = pendingTurnUpdate.Value;
                Debug.Log($"🔄 PersistentCheckStatusUI: Applying delayed turn update from {previousPlayer} to {newCurrentPlayer}");

                // Update the current turn
                currentTurn = newCurrentPlayer;
                UpdateStatusDisplay();

                // Clean up
                CleanupAnimationWaiting();
            }
            else
            {
                Debug.LogWarning($"⚠️ PersistentCheckStatusUI: No pending turn update found");
                CleanupAnimationWaiting();
            }
        }
        else
        {
            Debug.Log($"🎬 PersistentCheckStatusUI: Animation completed but other pieces still animating (after safety frame), continuing to wait");
        }
    }

    /// <summary>
    /// Clean up animation waiting state and unsubscribe from events
    /// </summary>
    private void CleanupAnimationWaiting()
    {
        isWaitingForAnimationBeforeStatusUpdate = false;
        pendingTurnUpdate = null;

        // Unsubscribe from animation events to prevent memory leaks
        ChessPiece.OnMoveAnimationComplete -= OnAnimationCompletedForStatus;
        Debug.Log($"🎬 PersistentCheckStatusUI: Cleaned up animation waiting state and unsubscribed from events");
    }
}