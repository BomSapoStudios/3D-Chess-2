using UnityEngine;

/// <summary>
/// UI component for displaying check status notifications to players.
/// Shows clear text messages when a player is in check, with animated effects for visibility.
/// Designed for mobile-first with high contrast and clear typography.
/// </summary>
public class CheckStatusUI : MonoBehaviour
{
    [Header("UI Settings")]
    public bool showCheckStatus = true;
    public float messageDuration = 3f;
    public float pulseSpeed = 2f;
    
    [Header("Animation Settings")]
    public bool enablePulseAnimation = true;
    public bool enableColorAnimation = true;
    public float minAlpha = 0.7f;
    public float maxAlpha = 1f;
    
    public static CheckStatusUI Instance { get; private set; }
    
    // UI State
    private bool isShowingCheckMessage = false;
    private string currentCheckMessage = "";
    private PieceColor checkedPlayerColor;
    private float messageStartTime;
    
    // GUI Styles
    private GUIStyle checkMessageStyle;
    private GUIStyle backgroundStyle;
    private Rect messageRect;
    private Rect backgroundRect;
    
    // Animation
    private float animationTime = 0f;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("CheckStatusUI: Instance created");
        }
        else
        {
            Debug.LogWarning("CheckStatusUI: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        SetupGUIStyles();
        CalculateRects();
        
        // Subscribe to check detection events for proper event-driven updates
        SubscribeToCheckEvents();
        
        // Subscribe to game end events for forfeit and draw notifications
        SubscribeToGameEndEvents();
        
        // Start a delayed initialization check in case components aren't ready yet
        StartCoroutine(DelayedEventSubscription());
    }
    
    /// <summary>
    /// Subscribe to check detection events
    /// </summary>
    private void SubscribeToCheckEvents()
    {
        if (CheckDetectionManager.Instance != null)
        {
            Debug.Log("CheckStatusUI: Subscribing to CheckDetectionManager events");
            CheckDetectionManager.Instance.OnKingInCheck += OnKingInCheck;
            CheckDetectionManager.Instance.OnCheckResolved += OnCheckResolved;
            CheckDetectionManager.Instance.OnCheckmate += OnCheckmate;
            CheckDetectionManager.Instance.OnStalemate += OnStalemate;
        }
        else
        {
            Debug.LogWarning("CheckStatusUI: CheckDetectionManager.Instance not found for event subscription");
        }
    }
    
    /// <summary>
    /// Subscribe to game end events for forfeit and draw notifications
    /// </summary>
    private void SubscribeToGameEndEvents()
    {
        if (GameEndDetectionManager.Instance != null)
        {
            Debug.Log("CheckStatusUI: Subscribing to GameEndDetectionManager events for forfeit/draw");
            GameEndDetectionManager.Instance.OnForfeit += OnPlayerForfeit;
            GameEndDetectionManager.Instance.OnDrawOffered += OnDrawOffered;
            GameEndDetectionManager.Instance.OnDrawAccepted += OnDrawAccepted;
            GameEndDetectionManager.Instance.OnDrawDeclined += OnDrawDeclined;
        }
        else
        {
            Debug.LogWarning("CheckStatusUI: GameEndDetectionManager.Instance not found for event subscription");
        }
    }
    
    /// <summary>
    /// Delayed event subscription retry in case components initialize in different order
    /// </summary>
    private System.Collections.IEnumerator DelayedEventSubscription()
    {
        yield return new UnityEngine.WaitForSeconds(2f);
        
        // Retry subscription if we didn't get it the first time
        if (CheckDetectionManager.Instance != null)
        {
            // Check if we're already subscribed by testing if our methods are in the invocation list
            bool alreadySubscribed = CheckDetectionManager.Instance.OnKingInCheck?.GetInvocationList()?.Length > 0;
            
            if (!alreadySubscribed)
            {
                Debug.Log("CheckStatusUI: Delayed subscription attempt");
                SubscribeToCheckEvents();
            }
            else
            {
                Debug.Log("CheckStatusUI: Events already properly subscribed");
            }
        }
        else
        {
            Debug.LogError("CheckStatusUI: CheckDetectionManager.Instance still null after delay!");
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.OnKingInCheck -= OnKingInCheck;
            CheckDetectionManager.Instance.OnCheckResolved -= OnCheckResolved;
            CheckDetectionManager.Instance.OnCheckmate -= OnCheckmate;
            CheckDetectionManager.Instance.OnStalemate -= OnStalemate;
        }
        
        if (GameEndDetectionManager.Instance != null)
        {
            GameEndDetectionManager.Instance.OnForfeit -= OnPlayerForfeit;
            GameEndDetectionManager.Instance.OnDrawOffered -= OnDrawOffered;
            GameEndDetectionManager.Instance.OnDrawAccepted -= OnDrawAccepted;
            GameEndDetectionManager.Instance.OnDrawDeclined -= OnDrawDeclined;
        }
    }
    
    /// <summary>
    /// Event handler for when a king enters check
    /// </summary>
    private void OnKingInCheck(PieceColor kingColor)
    {
        Debug.Log($"CheckStatusUI.OnKingInCheck: Received event for {kingColor} king in check");
        ShowCheckMessage(kingColor);
    }
    
    /// <summary>
    /// Event handler for when check is resolved
    /// </summary>
    private void OnCheckResolved(PieceColor kingColor)
    {
        Debug.Log($"CheckStatusUI.OnCheckResolved: Received event for {kingColor} check resolved");
        Debug.Log($"CheckStatusUI.OnCheckResolved: Current message showing: {isShowingCheckMessage}, Message: '{currentCheckMessage}'");
        
        // Always hide check message when check is resolved - keep it simple
        if (isShowingCheckMessage)
        {
            Debug.Log($"CheckStatusUI.OnCheckResolved: Hiding check message for {kingColor}");
            HideCheckMessage();
            Debug.Log($"CheckStatusUI.OnCheckResolved: Message hidden successfully");
        }
        else
        {
            Debug.Log($"CheckStatusUI.OnCheckResolved: No message was showing, nothing to hide");
        }
    }
    
    /// <summary>
    /// Event handler for checkmate
    /// </summary>
    private void OnCheckmate(PieceColor checkmatedPlayer)
    {
        Debug.Log($"CheckStatusUI.OnCheckmate: Received event for {checkmatedPlayer} checkmate");
        ShowCheckmateMessage(checkmatedPlayer);
    }
    
    /// <summary>
    /// Event handler for stalemate
    /// </summary>
    private void OnStalemate(PieceColor stalematedPlayer)
    {
        Debug.Log($"CheckStatusUI.OnStalemate: Received event for {stalematedPlayer} stalemate");
        ShowStalemateMessage(stalematedPlayer);
    }
    
    private void Update()
    {
        // Update animation time for visual effects
        animationTime += Time.deltaTime;
        
        // Note: Auto-hide timer removed to prevent interference with state-driven display
        // Check messages should only be hidden by explicit OnCheckResolved events
    }
    
    private void SetupGUIStyles()
    {
        // Check message style - large, bold, high contrast
        checkMessageStyle = new GUIStyle();
        checkMessageStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.04f); // 4% of screen height
        checkMessageStyle.fontStyle = FontStyle.Bold;
        checkMessageStyle.alignment = TextAnchor.MiddleCenter;
        checkMessageStyle.normal.textColor = Color.white;
        checkMessageStyle.wordWrap = false;
        
        // Background style - semi-transparent for readability
        backgroundStyle = new GUIStyle();
        backgroundStyle.normal.background = CreateColorTexture(new Color(0f, 0f, 0f, 0.8f));
    }
    
    private void CalculateRects()
    {
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;
        
        // Message positioned at top-center of screen
        float messageWidth = screenWidth * 0.8f; // 80% of screen width
        float messageHeight = Screen.height * 0.08f; // 8% of screen height
        float messageX = (screenWidth - messageWidth) / 2f;
        float messageY = screenHeight * 0.05f; // 5% from top
        
        messageRect = new Rect(messageX, messageY, messageWidth, messageHeight);
        
        // Background slightly larger for padding
        float backgroundPadding = 10f;
        backgroundRect = new Rect(
            messageX - backgroundPadding,
            messageY - backgroundPadding,
            messageWidth + (backgroundPadding * 2),
            messageHeight + (backgroundPadding * 2)
        );
    }
    
    private Texture2D CreateColorTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
    
    /// <summary>
    /// Show a check message for the specified player
    /// </summary>
    /// <param name="playerColor">Color of the player in check</param>
    public void ShowCheckMessage(PieceColor playerColor)
    {
        if (!showCheckStatus) return;
        
        checkedPlayerColor = playerColor;
        currentCheckMessage = $"{playerColor.ToString().ToUpper()} IS IN CHECK!";
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        animationTime = 0f;
        
        Debug.Log($"CheckStatusUI: Showing check message - {currentCheckMessage}");
    }
    
    /// <summary>
    /// Show a checkmate message for the specified player
    /// </summary>
    /// <param name="checkmatedPlayer">Color of the checkmated player</param>
    public void ShowCheckmateMessage(PieceColor checkmatedPlayer)
    {
        if (!showCheckStatus) return;
        
        PieceColor winner = (checkmatedPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        checkedPlayerColor = checkmatedPlayer;
        currentCheckMessage = $"CHECKMATE! {winner.ToString().ToUpper()} WINS!";
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        animationTime = 0f;
        
        // Checkmate messages stay longer
        messageDuration = 10f;
        
        Debug.Log($"CheckStatusUI: Showing checkmate message - {currentCheckMessage}");
    }
    
    /// <summary>
    /// Show a stalemate message
    /// </summary>
    /// <param name="stalematedPlayer">Color of the stalemated player</param>
    public void ShowStalemateMessage(PieceColor stalematedPlayer)
    {
        if (!showCheckStatus) return;
        
        checkedPlayerColor = stalematedPlayer;
        currentCheckMessage = "STALEMATE! GAME IS A DRAW!";
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        animationTime = 0f;
        
        // Stalemate messages stay longer
        messageDuration = 10f;
        
        Debug.Log($"CheckStatusUI: Showing stalemate message - {currentCheckMessage}");
    }
    
    /// <summary>
    /// Hide the current check message
    /// </summary>
    public void HideCheckMessage()
    {
        if (!isShowingCheckMessage) return;
        
        isShowingCheckMessage = false;
        currentCheckMessage = "";
        
        // Reset message duration for next check
        messageDuration = 3f;
        
        Debug.Log("CheckStatusUI: Check message hidden");
    }
    
    /// <summary>
    /// Show a custom message (for other game events)
    /// </summary>
    /// <param name="message">Message to display</param>
    /// <param name="duration">How long to show the message</param>
    public void ShowCustomMessage(string message, float duration = 3f)
    {
        if (!showCheckStatus) return;
        
        currentCheckMessage = message.ToUpper();
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        messageDuration = duration;
        animationTime = 0f;
        
        Debug.Log($"CheckStatusUI: Showing custom message - {currentCheckMessage}");
    }
    
    private void OnGUI()
    {
        if (!isShowingCheckMessage || string.IsNullOrEmpty(currentCheckMessage)) return;
        
        // Ensure styles are set up
        if (checkMessageStyle == null)
        {
            SetupGUIStyles();
            CalculateRects();
        }
        
        // Calculate animation effects
        Color textColor = GetAnimatedTextColor();
        Color backgroundColor = GetAnimatedBackgroundColor();
        
        // Update styles with animated colors
        checkMessageStyle.normal.textColor = textColor;
        backgroundStyle.normal.background = CreateColorTexture(backgroundColor);
        
        // Draw background
        GUI.Box(backgroundRect, "", backgroundStyle);
        
        // Draw check message
        GUI.Label(messageRect, currentCheckMessage, checkMessageStyle);
        
        // Draw additional context for mobile users
        if (isShowingCheckMessage && currentCheckMessage.Contains("CHECK") && !currentCheckMessage.Contains("CHECKMATE"))
        {
            Rect subtitleRect = new Rect(
                messageRect.x,
                messageRect.y + messageRect.height - 10f,
                messageRect.width,
                messageRect.height * 0.4f
            );
            
            GUIStyle subtitleStyle = new GUIStyle(checkMessageStyle);
            subtitleStyle.fontSize = Mathf.RoundToInt(checkMessageStyle.fontSize * 0.6f);
            subtitleStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, textColor.a * 0.8f);
            
            GUI.Label(subtitleRect, "Move your king to safety!", subtitleStyle);
        }
    }
    
    /// <summary>
    /// Get animated text color with pulsing effect
    /// </summary>
    private Color GetAnimatedTextColor()
    {
        if (!enableColorAnimation && !enablePulseAnimation)
        {
            return Color.white;
        }
        
        // Base color depends on message type
        Color baseColor = Color.white;
        if (currentCheckMessage.Contains("CHECKMATE"))
        {
            baseColor = Color.red;
        }
        else if (currentCheckMessage.Contains("STALEMATE") || currentCheckMessage.Contains("DRAW ACCEPTED") || currentCheckMessage.Contains("DRAW"))
        {
            baseColor = Color.yellow;
        }
        else if (currentCheckMessage.Contains("FORFEIT"))
        {
            baseColor = Color.red;
        }
        else if (currentCheckMessage.Contains("DRAW OFFERED"))
        {
            // Blue color for draw offers
            baseColor = new Color(0.3f, 0.6f, 1.0f, 1f);
        }
        else if (currentCheckMessage.Contains("DRAW DECLINED"))
        {
            baseColor = Color.orange;
        }
        else if (currentCheckMessage.Contains("CHECK"))
        {
            // Alternate between red and white for urgency
            if (enableColorAnimation)
            {
                float colorPulse = (Mathf.Sin(animationTime * pulseSpeed * 2f) + 1f) * 0.5f;
                baseColor = Color.Lerp(Color.white, Color.red, colorPulse);
            }
            else
            {
                baseColor = Color.red;
            }
        }
        
        // Apply pulse alpha animation
        if (enablePulseAnimation)
        {
            float alphaPulse = Mathf.Lerp(minAlpha, maxAlpha, (Mathf.Sin(animationTime * pulseSpeed) + 1f) * 0.5f);
            baseColor.a = alphaPulse;
        }
        
        return baseColor;
    }
    
    /// <summary>
    /// Get animated background color
    /// </summary>
    private Color GetAnimatedBackgroundColor()
    {
        // Semi-transparent black background with slight pulsing
        float backgroundAlpha = 0.8f;
        if (enablePulseAnimation)
        {
            backgroundAlpha = Mathf.Lerp(0.7f, 0.9f, (Mathf.Sin(animationTime * pulseSpeed * 0.5f) + 1f) * 0.5f);
        }
        
        return new Color(0f, 0f, 0f, backgroundAlpha);
    }
    
    /// <summary>
    /// Get current check status for debugging
    /// </summary>
    public string GetStatusDebug()
    {
        return $"CheckStatusUI: Showing={isShowingCheckMessage}, Message='{currentCheckMessage}', Player={checkedPlayerColor}";
    }
    
    /// <summary>
    /// Check if currently showing a message
    /// </summary>
    public bool IsShowingMessage()
    {
        return isShowingCheckMessage;
    }
    
    /// <summary>
    /// Force hide any current message
    /// </summary>
    public void ForceHide()
    {
        HideCheckMessage();
    }
    
    /// <summary>
    /// Test method to manually trigger check UI (for debugging)
    /// </summary>
    public void TestCheckUI()
    {
        Debug.Log("CheckStatusUI.TestCheckUI: Testing UI display manually...");
        ShowCheckMessage(PieceColor.White);
        
        // Also test if we can find other components
        Debug.Log($"CheckStatusUI.TestCheckUI: CheckVisualFeedbackManager.Instance = {(CheckVisualFeedbackManager.Instance != null ? "FOUND" : "NULL")}");
        Debug.Log($"CheckStatusUI.TestCheckUI: CheckDetectionManager.Instance = {(CheckDetectionManager.Instance != null ? "FOUND" : "NULL")}");
        Debug.Log($"CheckStatusUI.TestCheckUI: showCheckStatus = {showCheckStatus}");
        Debug.Log($"CheckStatusUI.TestCheckUI: isShowingCheckMessage = {isShowingCheckMessage}");
    }
    
    // ===== FORFEIT AND DRAW EVENT HANDLERS =====
    
    /// <summary>
    /// Event handler for when a player forfeits
    /// </summary>
    private void OnPlayerForfeit(PieceColor forfeitingPlayer)
    {
        ShowForfeitMessage(forfeitingPlayer, GameEndDetectionManager.ForfeitWinner(forfeitingPlayer));
    }
    
    /// <summary>
    /// Event handler for when a draw is offered
    /// </summary>
    private void OnDrawOffered(PieceColor offeringPlayer)
    {
        ShowDrawOfferMessage(offeringPlayer);
    }
    
    /// <summary>
    /// Event handler for when a draw is accepted
    /// </summary>
    private void OnDrawAccepted(PieceColor acceptingPlayer)
    {
        ShowDrawAcceptedMessage();
    }
    
    /// <summary>
    /// Event handler for when a draw is declined
    /// </summary>
    private void OnDrawDeclined(PieceColor decliningPlayer)
    {
        ShowDrawDeclinedMessage(decliningPlayer);
    }
    
    /// <summary>
    /// Show a forfeit message
    /// </summary>
    /// <param name="forfeitingPlayer">Player who forfeited</param>
    /// <param name="winner">Winning player</param>
    public void ShowForfeitMessage(PieceColor forfeitingPlayer, PieceColor winner)
    {
        if (!showCheckStatus) return;
        
        currentCheckMessage = $"{winner.ToString().ToUpper()} WINS BY FORFEIT!";
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        animationTime = 0f;
        
        // Forfeit messages stay longer
        messageDuration = 10f;
        
        Debug.Log($"CheckStatusUI: Showing forfeit message - {currentCheckMessage}");
    }
    
    /// <summary>
    /// Show a draw offer notification
    /// </summary>
    /// <param name="offeringPlayer">Player who offered the draw</param>
    public void ShowDrawOfferMessage(PieceColor offeringPlayer)
    {
        if (!showCheckStatus) return;
        
        PieceColor respondingPlayer = (offeringPlayer == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        currentCheckMessage = $"DRAW OFFERED BY {offeringPlayer.ToString().ToUpper()}";
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        animationTime = 0f;
        
        // Draw offer messages stay until resolved
        messageDuration = 60f; // Long duration, will be cleared by draw resolution
        
        Debug.Log($"CheckStatusUI: Showing draw offer message - {currentCheckMessage}");
    }
    
    /// <summary>
    /// Show draw accepted message
    /// </summary>
    public void ShowDrawAcceptedMessage()
    {
        if (!showCheckStatus) return;
        
        currentCheckMessage = "DRAW ACCEPTED! GAME IS A DRAW!";
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        animationTime = 0f;
        
        // Draw result messages stay longer
        messageDuration = 10f;
        
        Debug.Log($"CheckStatusUI: Showing draw accepted message - {currentCheckMessage}");
    }
    
    /// <summary>
    /// Show draw declined message (briefly, then hide)
    /// </summary>
    /// <param name="decliningPlayer">Player who declined</param>
    public void ShowDrawDeclinedMessage(PieceColor decliningPlayer)
    {
        if (!showCheckStatus) return;
        
        currentCheckMessage = $"DRAW DECLINED BY {decliningPlayer.ToString().ToUpper()}";
        isShowingCheckMessage = true;
        messageStartTime = Time.time;
        animationTime = 0f;
        
        // Declined messages show briefly
        messageDuration = 2f;
        
        Debug.Log($"CheckStatusUI: Showing draw declined message - {currentCheckMessage}");
        
        // Auto-hide after brief display
        StartCoroutine(AutoHideAfterDelay(messageDuration));
    }
    
    /// <summary>
    /// Auto-hide message after specified delay
    /// </summary>
    private System.Collections.IEnumerator AutoHideAfterDelay(float delay)
    {
        yield return new UnityEngine.WaitForSeconds(delay);
        HideCheckMessage();
    }
}