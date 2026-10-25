using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Displays countdown timers for timed play mode in the upper-left corner of the game screen.
/// Shows remaining time for both players with visual highlighting for the active player.
/// Features low-time warnings and color-coded feedback.
/// </summary>
public class TimerDisplayUI : MonoBehaviour
{
    [Header("Timer Display Settings")]
    public bool enableTimerDisplay = true;
    public int fontSize = 48; // Increased to 48 for maximum readability
    public Color normalTimeColor = Color.white;
    public Color activePlayerColor = new Color(0.3f, 0.8f, 1f, 1f); // Light blue for active
    public Color lowTimeColor = new Color(1f, 0.6f, 0.2f, 1f); // Orange warning
    public Color criticalTimeColor = Color.red; // Red for very low time
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.8f); // Slightly more opaque background
    
    [Header("Warning Thresholds")]
    public float lowTimeThreshold = 60f; // Show warning under 60 seconds
    public float criticalTimeThreshold = 30f; // Show critical warning under 30 seconds
    
    // UI Components
    private Canvas timerCanvas;
    private GameObject timerPanel;
    private Dictionary<PieceColor, TextMeshProUGUI> playerTimerTexts = new Dictionary<PieceColor, TextMeshProUGUI>(); // MULTI-PLAYER SUPPORT
    private Image backgroundImage;
    
    // State tracking
    private bool isVisible = false;
    private PieceColor currentActivePlayer = PieceColor.White;
    
    public static TimerDisplayUI Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("TimerDisplayUI: Instance created");
        }
        else
        {
            Debug.LogWarning("TimerDisplayUI: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        Debug.Log("🕒 TimerDisplayUI: Starting initialization...");
        
        CreateTimerUI();
        
        // Subscribe to timer events
        if (TimerManager.Instance != null)
        {
            Debug.Log("🕒 TimerDisplayUI: TimerManager.Instance found, subscribing to events...");
            TimerManager.Instance.OnTimerUpdated += OnTimerUpdated;
            TimerManager.Instance.OnTimerActiveChanged += OnTimerActiveChanged;
            TimerManager.Instance.OnLowTimeWarning += OnLowTimeWarning;
            TimerManager.Instance.OnTimerExpired += OnTimerExpired;
            
            Debug.Log($"🕒 TimerDisplayUI: Timer events subscribed. IsTimedPlayActive: {TimerManager.Instance.IsTimedPlayActive()}");
        }
        else
        {
            Debug.LogError("🕒 TimerDisplayUI: TimerManager.Instance is NULL - cannot subscribe to timer events!");
        }
        
        // Subscribe to turn changes to highlight active player
        if (TurnManager.Instance != null)
        {
            Debug.Log("🕒 TimerDisplayUI: TurnManager.Instance found, subscribing to turn events...");
            TurnManager.Instance.OnTurnChanged += OnTurnChanged;
        }
        else
        {
            Debug.LogError("🕒 TimerDisplayUI: TurnManager.Instance is NULL - cannot subscribe to turn events!");
        }
        
        // Initially hide the timer display
        Debug.Log("🕒 TimerDisplayUI: Setting initial timer visibility to false...");
        SetTimerVisibility(false);
    }
    
    /// <summary>
    /// Create the timer UI in the upper-left corner
    /// MULTI-PLAYER SUPPORT: Dynamically creates timer displays for 2-6 players
    /// </summary>
    private void CreateTimerUI()
    {
        Debug.Log("🕒 TimerDisplayUI: CreateTimerUI - Starting timer UI creation...");

        // Create canvas for timer display
        GameObject canvasObj = new GameObject("TimerCanvas");
        canvasObj.transform.SetParent(transform, false);

        timerCanvas = canvasObj.AddComponent<Canvas>();
        timerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        timerCanvas.sortingOrder = 130; // Highest priority - above check status UI (110) and menu UI (120)

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Create timer panel in upper-left corner
        timerPanel = new GameObject("TimerPanel");
        timerPanel.transform.SetParent(canvasObj.transform, false);

        RectTransform panelRect = timerPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0.85f); // Moved lower to avoid status bar overlap
        panelRect.anchorMax = new Vector2(0.16f, 0.98f);  // 16% width, minimal screen space
        panelRect.sizeDelta = Vector2.zero;
        panelRect.anchoredPosition = Vector2.zero;

        // Add background
        backgroundImage = timerPanel.AddComponent<Image>();
        backgroundImage.color = backgroundColor;

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
        Debug.Log($"🕒 TimerDisplayUI: Creating timer displays for {playerCount} players");

        // Determine layout based on player count
        int rows, cols;
        int timerFontSize;

        if (playerCount <= 2)
        {
            // 2-player: Vertical stack
            rows = 2;
            cols = 1;
            timerFontSize = 48;
        }
        else if (playerCount <= 4)
        {
            // 4-player: 2x2 grid
            rows = 2;
            cols = 2;
            timerFontSize = 36;
        }
        else
        {
            // 6-player: 2x3 grid (2 rows, 3 columns)
            rows = 2;
            cols = 3;
            timerFontSize = 28;
        }

        // Create timer text for each active player
        float marginX = 0.02f;
        float marginY = 0.02f;
        float cellWidth = (1f - (marginX * 2f)) / cols;
        float cellHeight = (1f - (marginY * 2f)) / rows;

        for (int i = 0; i < activePlayers.Count; i++)
        {
            PieceColor player = activePlayers[i];

            // Calculate grid position
            int row = i / cols;
            int col = i % cols;

            // Create timer object
            GameObject timerObj = new GameObject($"{player}Timer");
            timerObj.transform.SetParent(timerPanel.transform, false);

            RectTransform timerRect = timerObj.AddComponent<RectTransform>();

            // Calculate anchors for grid layout (top-to-bottom, left-to-right)
            float anchorMinX = marginX + (col * cellWidth);
            float anchorMaxX = anchorMinX + cellWidth;
            float anchorMinY = 1f - marginY - ((row + 1) * cellHeight); // Top to bottom
            float anchorMaxY = 1f - marginY - (row * cellHeight);

            timerRect.anchorMin = new Vector2(anchorMinX, anchorMinY);
            timerRect.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
            timerRect.sizeDelta = Vector2.zero;
            timerRect.anchoredPosition = Vector2.zero;

            // Create text component
            TextMeshProUGUI timerText = timerObj.AddComponent<TextMeshProUGUI>();
            timerText.text = $"{player}: 10:00";
            timerText.fontSize = timerFontSize;
            timerText.color = normalTimeColor;
            timerText.alignment = TextAlignmentOptions.Center;
            timerText.font = Resources.Load<TMP_FontAsset>("LiberationSans SDF");

            // Color-Blind Symbols: the player's shape at the start of their clock
            if (PlayerSymbols.On)
            {
                float symbolSize = timerFontSize * 0.8f;
                GameObject symbolObj = new GameObject("Symbol", typeof(RectTransform));
                symbolObj.transform.SetParent(timerObj.transform, false);
                Image symbol = symbolObj.AddComponent<Image>();
                symbol.sprite = PlayerSymbols.Sprite(player);
                symbol.color = PlayerSymbols.Tint(player);
                symbol.preserveAspect = true;
                symbol.raycastTarget = false;
                RectTransform symbolRect = symbol.rectTransform;
                symbolRect.anchorMin = symbolRect.anchorMax = new Vector2(0f, 0.5f);
                symbolRect.sizeDelta = new Vector2(symbolSize, symbolSize);
                symbolRect.anchoredPosition = new Vector2(symbolSize / 2f + 4f, 0f);
                timerText.margin = new Vector4(symbolSize + 8f, 0f, 0f, 0f);
                timerText.enableAutoSizing = true; // the shape takes some of the width
                timerText.fontSizeMin = timerFontSize * 0.6f;
                timerText.fontSizeMax = timerFontSize;
            }

            // Store in dictionary
            playerTimerTexts[player] = timerText;

            Debug.Log($"🕒 TimerDisplayUI: Created timer for {player} at grid position ({row},{col})");
        }

        Debug.Log("🕒 TimerDisplayUI: Timer UI created in upper-left corner");
        Debug.Log($"🕒 TimerDisplayUI: Canvas created with sortingOrder: {timerCanvas.sortingOrder}");
        Debug.Log($"🕒 TimerDisplayUI: Timer panel active: {timerPanel.activeSelf}");
        Debug.Log($"🕒 TimerDisplayUI: Created {playerTimerTexts.Count} timer displays");
    }
    
    /// <summary>
    /// Handle timer updates from TimerManager
    /// </summary>
    private void OnTimerUpdated(PieceColor player, float secondsRemaining)
    {
        UpdateTimerDisplay(player, secondsRemaining);
    }
    
    /// <summary>
    /// Handle timer active state changes
    /// </summary>
    private void OnTimerActiveChanged(bool isActive)
    {
        Debug.Log($"🕒 TimerDisplayUI: OnTimerActiveChanged - isActive={isActive}");
        Debug.Log($"🕒 TimerDisplayUI: TimerManager.IsTimedPlayActive()={TimerManager.Instance?.IsTimedPlayActive()}");
        
        // Show/hide timer display based on timed play status
        bool shouldShow = isActive && TimerManager.Instance.IsTimedPlayActive();
        Debug.Log($"🕒 TimerDisplayUI: Calculated shouldShow={shouldShow} (isActive={isActive} && IsTimedPlayActive={TimerManager.Instance?.IsTimedPlayActive()})");
        
        SetTimerVisibility(shouldShow);
    }
    
    /// <summary>
    /// Handle turn changes to highlight active player
    /// </summary>
    private void OnTurnChanged(PieceColor previousPlayer, PieceColor newPlayer)
    {
        currentActivePlayer = newPlayer;
        UpdateActivePlayerHighlight();
    }
    
    /// <summary>
    /// Handle low time warnings
    /// </summary>
    private void OnLowTimeWarning(PieceColor player)
    {
        Debug.Log($"⚠️ TimerDisplayUI: Low time warning for {player}");
        // Could add pulsing animation or sound here
    }
    
    /// <summary>
    /// Handle timer expiry
    /// </summary>
    private void OnTimerExpired(PieceColor player)
    {
        Debug.Log($"⏰ TimerDisplayUI: Timer expired for {player}");
        UpdateTimerDisplay(player, 0f);
    }
    
    /// <summary>
    /// Update the timer display for a specific player
    /// MULTI-PLAYER SUPPORT: Works for any player color
    /// </summary>
    private void UpdateTimerDisplay(PieceColor player, float secondsRemaining)
    {
        if (!isVisible) return;

        string timeString = FormatTime(secondsRemaining);
        Color timeColor = GetTimeColor(secondsRemaining, player == currentActivePlayer);

        if (playerTimerTexts.ContainsKey(player))
        {
            playerTimerTexts[player].text = $"{player}: {timeString}";
            playerTimerTexts[player].color = timeColor;
        }
    }
    
    /// <summary>
    /// Update active player highlighting
    /// MULTI-PLAYER SUPPORT: Updates all player timer colors
    /// </summary>
    private void UpdateActivePlayerHighlight()
    {
        if (!isVisible) return;

        // Update colors for all players based on current active player
        foreach (var kvp in playerTimerTexts)
        {
            PieceColor player = kvp.Key;
            TextMeshProUGUI timerText = kvp.Value;

            if (TimerManager.Instance != null)
            {
                float playerTime = TimerManager.Instance.GetRemainingTime(player);
                timerText.color = GetTimeColor(playerTime, currentActivePlayer == player);
            }
        }
    }
    
    /// <summary>
    /// Get appropriate color for timer text based on time remaining and active status
    /// </summary>
    private Color GetTimeColor(float secondsRemaining, bool isActivePlayer)
    {
        // Critical time (red)
        if (secondsRemaining <= criticalTimeThreshold)
        {
            return criticalTimeColor;
        }
        
        // Low time (orange)
        if (secondsRemaining <= lowTimeThreshold)
        {
            return lowTimeColor;
        }
        
        // Active player (blue highlight)
        if (isActivePlayer)
        {
            return activePlayerColor;
        }
        
        // Normal time (white)
        return normalTimeColor;
    }
    
    /// <summary>
    /// Format seconds as MM:SS string
    /// </summary>
    private string FormatTime(float seconds)
    {
        if (seconds < 0) seconds = 0;
        
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int remainingSeconds = Mathf.FloorToInt(seconds % 60f);
        return $"{minutes:D2}:{remainingSeconds:D2}";
    }
    
    /// <summary>
    /// Show or hide the timer display
    /// MULTI-PLAYER SUPPORT: Updates all player timers
    /// </summary>
    public void SetTimerVisibility(bool visible)
    {
        Debug.Log($"🕒 TimerDisplayUI: SetTimerVisibility called with visible={visible}");
        Debug.Log($"🕒 TimerDisplayUI: Previous isVisible state: {isVisible}");

        isVisible = visible;

        if (timerPanel != null)
        {
            timerPanel.SetActive(visible);
            Debug.Log($"🕒 TimerDisplayUI: Timer panel SetActive({visible}) called");
            Debug.Log($"🕒 TimerDisplayUI: Timer panel activeInHierarchy: {timerPanel.activeInHierarchy}");
        }
        else
        {
            Debug.LogError("🕒 TimerDisplayUI: Cannot set visibility - timerPanel is NULL!");
        }

        if (visible && TimerManager.Instance != null)
        {
            // Update display with current timer values for all players
            foreach (PieceColor player in playerTimerTexts.Keys)
            {
                float playerTime = TimerManager.Instance.GetRemainingTime(player);
                UpdateTimerDisplay(player, playerTime);
            }
            UpdateActivePlayerHighlight();
        }

        Debug.Log($"TimerDisplayUI: Timer display {(visible ? "shown" : "hidden")}");
    }
    
    /// <summary>
    /// Initialize timer display for a new game
    /// MULTI-PLAYER SUPPORT: Initializes all active player timers
    /// </summary>
    public void InitializeForGame(GameConfiguration config)
    {
        if (config == null) return;

        // Show timer display only if timed play is enabled
        bool shouldShow = config.enableTimedPlay;
        SetTimerVisibility(shouldShow);

        if (shouldShow)
        {
            // Update initial display for all active players
            float initialTime = config.timePerPlayerMinutes * 60f;

            foreach (PieceColor player in playerTimerTexts.Keys)
            {
                UpdateTimerDisplay(player, initialTime);
            }

            Debug.Log($"TimerDisplayUI: Initialized for timed play ({config.timePerPlayerMinutes} minutes per player, {playerTimerTexts.Count} players)");
        }
    }
    
    /// <summary>
    /// Clean up event subscriptions
    /// </summary>
    private void OnDestroy()
    {
        // Unsubscribe from timer events
        if (TimerManager.Instance != null)
        {
            TimerManager.Instance.OnTimerUpdated -= OnTimerUpdated;
            TimerManager.Instance.OnTimerActiveChanged -= OnTimerActiveChanged;
            TimerManager.Instance.OnLowTimeWarning -= OnLowTimeWarning;
            TimerManager.Instance.OnTimerExpired -= OnTimerExpired;
        }
        
        // Unsubscribe from turn events
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.OnTurnChanged -= OnTurnChanged;
        }
    }
}