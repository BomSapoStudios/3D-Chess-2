using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages timed play functionality for 3D Chess games.
/// Tracks remaining time for each player and handles timer-based game events.
/// Integrates with GameConfiguration settings and TurnManager for turn-based timing.
/// </summary>
public class TimerManager : MonoBehaviour
{
    [Header("Timer Settings")]
    public bool enableTimedPlay = false;
    public int timePerPlayerMinutes = 10; // Minutes per player
    
    [Header("Debug")]
    public bool debugMode = false;

    // Timer state - MULTI-PLAYER SUPPORT: Track time for all active players (2-6)
    private Dictionary<PieceColor, float> playerTimeRemaining = new Dictionary<PieceColor, float>();
    private bool timerActive = false;
    private PieceColor activeTimerPlayer = PieceColor.White;
    
    // Game integration
    private bool isInitialized = false;
    private Coroutine currentTimerCoroutine;
    
    public static TimerManager Instance { get; private set; }
    
    // Events
    public System.Action<PieceColor, float> OnTimerUpdated; // Player, seconds remaining
    public System.Action<PieceColor> OnTimerExpired; // Player ran out of time
    public System.Action<PieceColor> OnLowTimeWarning; // Player has less than 1 minute
    public System.Action<bool> OnTimerActiveChanged; // Timer started/stopped
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("TimerManager: Instance created");
        }
        else
        {
            Debug.LogWarning("TimerManager: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        Debug.Log("🕒 TimerManager: Start() method called - beginning initialization...");
        
        // Initialize timer settings from game configuration
        InitializeFromConfiguration();
        
        // Subscribe to game state changes
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged += OnGameStateChanged;
        }
        
        // Subscribe to chaos rotation events to pause/resume timer during animations
        if (ChaosRotationManager.Instance != null)
        {
            ChaosRotationManager.Instance.OnChaosRotationStarted += OnChaosRotationStarted;
            ChaosRotationManager.Instance.OnChaosRotationCompleted += OnChaosRotationCompleted;
            Debug.Log("⏰ TimerManager: Subscribed to chaos rotation events for timer pause/resume");
        }
        
        Debug.Log($"🕒 TimerManager: Initialized with timed play {(enableTimedPlay ? "ENABLED" : "DISABLED")}");
        Debug.Log($"🕒 TimerManager: isInitialized: {isInitialized}, enableTimedPlay: {enableTimedPlay}");
        if (enableTimedPlay)
        {
            Debug.Log($"🕒 TimerManager: Each player has {timePerPlayerMinutes} minutes ({timePerPlayerMinutes * 60} seconds)");
        }
    }
    
    /// <summary>
    /// Initialize timer settings from the current game configuration
    /// </summary>
    private void InitializeFromConfiguration()
    {
        Debug.Log("🕒 TimerManager: InitializeFromConfiguration - Starting configuration loading...");
        
        // Get configuration from SceneController if available
        if (SceneController.Instance != null)
        {
            Debug.Log("🕒 TimerManager: SceneController.Instance found, getting configuration...");
            GameConfiguration config = SceneController.Instance.GetCurrentGameConfiguration();
            if (config != null)
            {
                Debug.Log($"🕒 TimerManager: GameConfiguration loaded - enableTimedPlay: {config.enableTimedPlay}, timePerPlayer: {config.timePerPlayerMinutes}");
                ApplyTimerConfiguration(config);
            }
            else
            {
                Debug.LogError("🕒 TimerManager: GameConfiguration is NULL from SceneController!");
                Debug.LogError("🕒 TimerManager: Will remain uninitialized - enableTimedPlay will be False");
                
                // TEMPORARY FALLBACK: Force enable timed play for testing
                Debug.LogWarning("🕒 TimerManager: APPLYING FALLBACK CONFIGURATION FOR TESTING (GameConfig NULL)");
                enableTimedPlay = true;
                timePerPlayerMinutes = 10;

                // Initialize timers for all active players
                List<PieceColor> activePlayers = PlayerManager.Instance != null
                    ? PlayerManager.Instance.GetActivePlayers()
                    : new List<PieceColor> { PieceColor.White, PieceColor.Black };

                playerTimeRemaining.Clear();
                foreach (PieceColor player in activePlayers)
                {
                    playerTimeRemaining[player] = 600f; // 10 minutes in seconds
                }

                isInitialized = true;
                Debug.LogWarning($"🕒 TimerManager: Fallback applied - enableTimedPlay: True, 10 minutes for {activePlayers.Count} players");
            }
        }
        else
        {
            Debug.LogError("🕒 TimerManager: SceneController.Instance is NULL - cannot load timer configuration!");
            Debug.LogError("🕒 TimerManager: Will remain uninitialized - enableTimedPlay will be False");
            
            // TEMPORARY FALLBACK: Force enable timed play for testing
            Debug.LogWarning("🕒 TimerManager: APPLYING FALLBACK CONFIGURATION FOR TESTING");
            enableTimedPlay = true;
            timePerPlayerMinutes = 10;

            // Initialize timers for all active players
            List<PieceColor> activePlayers = PlayerManager.Instance != null
                ? PlayerManager.Instance.GetActivePlayers()
                : new List<PieceColor> { PieceColor.White, PieceColor.Black };

            playerTimeRemaining.Clear();
            foreach (PieceColor player in activePlayers)
            {
                playerTimeRemaining[player] = 600f; // 10 minutes in seconds
            }

            isInitialized = true;
            Debug.LogWarning($"🕒 TimerManager: Fallback applied - enableTimedPlay: True, 10 minutes for {activePlayers.Count} players");
        }
    }
    
    /// <summary>
    /// Apply timer configuration settings
    /// MULTI-PLAYER SUPPORT: Initializes timers for all active players (2-6)
    /// </summary>
    public void ApplyTimerConfiguration(GameConfiguration config)
    {
        if (config == null)
        {
            Debug.LogWarning("TimerManager: Received null configuration");
            return;
        }

        enableTimedPlay = config.enableTimedPlay;
        timePerPlayerMinutes = config.timePerPlayerMinutes;

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

        // Convert minutes to seconds and set initial time for all active players
        float initialTimeSeconds = timePerPlayerMinutes * 60f;
        playerTimeRemaining.Clear();

        foreach (PieceColor player in activePlayers)
        {
            playerTimeRemaining[player] = initialTimeSeconds;
        }

        isInitialized = true;

        Debug.Log($"🕒 TimerManager: Applied configuration - Timed Play: {enableTimedPlay}");
        Debug.Log($"🕒   Time per player: {timePerPlayerMinutes} minutes ({initialTimeSeconds} seconds)");
        Debug.Log($"🕒   Initialized timers for {activePlayers.Count} players:");
        foreach (var kvp in playerTimeRemaining)
        {
            Debug.Log($"🕒     {kvp.Key}: {kvp.Value}s");
        }
        Debug.Log($"🕒   isInitialized set to: {isInitialized}");
    }
    
    /// <summary>
    /// Start the timer for the specified player
    /// </summary>
    public void StartTimer(PieceColor player)
    {
        if (!enableTimedPlay || !isInitialized)
        {
            if (debugMode)
                Debug.Log($"TimerManager: Not starting timer - Enabled: {enableTimedPlay}, Initialized: {isInitialized}");
            return;
        }
        
        activeTimerPlayer = player;
        timerActive = true;
        
        // Start the countdown coroutine
        if (currentTimerCoroutine != null)
        {
            StopCoroutine(currentTimerCoroutine);
        }
        currentTimerCoroutine = StartCoroutine(TimerCountdown());
        
        OnTimerActiveChanged?.Invoke(true);
        Debug.Log($"⏰ TimerManager: Started timer for {player} ({GetRemainingTime(player):F1}s remaining)");
    }
    
    /// <summary>
    /// Pause the timer
    /// </summary>
    public void PauseTimer()
    {
        if (!timerActive) return;
        
        timerActive = false;
        
        if (currentTimerCoroutine != null)
        {
            StopCoroutine(currentTimerCoroutine);
            currentTimerCoroutine = null;
        }
        
        OnTimerActiveChanged?.Invoke(false);
        Debug.Log($"⏸️ TimerManager: Timer paused for {activeTimerPlayer} ({GetRemainingTime(activeTimerPlayer):F1}s remaining)");
    }
    
    /// <summary>
    /// Resume the timer
    /// </summary>
    public void ResumeTimer()
    {
        if (!enableTimedPlay || !isInitialized) return;
        
        timerActive = true;
        
        // Restart the countdown coroutine
        if (currentTimerCoroutine != null)
        {
            StopCoroutine(currentTimerCoroutine);
        }
        currentTimerCoroutine = StartCoroutine(TimerCountdown());
        
        OnTimerActiveChanged?.Invoke(true);
        Debug.Log($"▶️ TimerManager: Timer resumed for {activeTimerPlayer} ({GetRemainingTime(activeTimerPlayer):F1}s remaining)");
    }
    
    /// <summary>
    /// Stop all timers (game ended)
    /// </summary>
    public void StopAllTimers()
    {
        timerActive = false;
        
        if (currentTimerCoroutine != null)
        {
            StopCoroutine(currentTimerCoroutine);
            currentTimerCoroutine = null;
        }
        
        OnTimerActiveChanged?.Invoke(false);
        Debug.Log("🛑 TimerManager: All timers stopped");
    }
    
    /// <summary>
    /// Main timer countdown coroutine
    /// MULTI-PLAYER SUPPORT: Works for any active player
    /// </summary>
    private IEnumerator TimerCountdown()
    {
        while (timerActive && enableTimedPlay)
        {
            // Get current remaining time
            float currentTime = GetRemainingTime(activeTimerPlayer);

            if (currentTime <= 0f)
            {
                // Time expired
                Debug.Log($"⏰ TimerManager: Time expired for {activeTimerPlayer}!");
                OnTimerExpired?.Invoke(activeTimerPlayer);
                timerActive = false;
                yield break;
            }

            // Check for low time warning (under 60 seconds)
            if (currentTime <= 60f && currentTime > 59f)
            {
                Debug.Log($"⚠️ TimerManager: Low time warning for {activeTimerPlayer} (under 1 minute)");
                OnLowTimeWarning?.Invoke(activeTimerPlayer);
            }

            // Update timer for active player using dictionary
            if (playerTimeRemaining.ContainsKey(activeTimerPlayer))
            {
                playerTimeRemaining[activeTimerPlayer] -= Time.deltaTime;
            }
            else
            {
                Debug.LogError($"⏰ TimerManager: Active player {activeTimerPlayer} not found in timer dictionary!");
                timerActive = false;
                yield break;
            }

            // Fire update event
            OnTimerUpdated?.Invoke(activeTimerPlayer, currentTime);

            yield return null; // Wait one frame
        }
    }
    
    /// <summary>
    /// Get remaining time for a player in seconds
    /// MULTI-PLAYER SUPPORT: Returns time for any player color
    /// </summary>
    public void SetRemainingTime(PieceColor player, float seconds)
    {
        playerTimeRemaining[player] = seconds;
    }

    public float GetRemainingTime(PieceColor player)
    {
        return playerTimeRemaining.ContainsKey(player) ? playerTimeRemaining[player] : 0f;
    }
    
    /// <summary>
    /// Get remaining time formatted as MM:SS string
    /// </summary>
    public string GetFormattedTime(PieceColor player)
    {
        float seconds = GetRemainingTime(player);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int remainingSeconds = Mathf.FloorToInt(seconds % 60f);
        return $"{minutes:D2}:{remainingSeconds:D2}";
    }
    
    /// <summary>
    /// Check if timed play is currently active
    /// </summary>
    public bool IsTimedPlayActive()
    {
        return enableTimedPlay && isInitialized;
    }
    
    /// <summary>
    /// Check if timer is currently running
    /// </summary>
    public bool IsTimerRunning()
    {
        return timerActive;
    }
    
    /// <summary>
    /// Handle game state changes
    /// </summary>
    private void OnGameStateChanged(GameState newState)
    {
        Debug.Log($"⏰ TimerManager: Game state changed to {newState}");
        
        switch (newState)
        {
            case GameState.Playing:
                // Game started - ready to begin timing when turns start
                Debug.Log("⏰ TimerManager: Game started - ready for timing");
                Debug.Log($"⏰ TimerManager: Configuration state - enableTimedPlay: {enableTimedPlay}, isInitialized: {isInitialized}");
                Debug.Log($"⏰ TimerManager: IsTimedPlayActive(): {IsTimedPlayActive()}");
                break;
                
            case GameState.GameOver:
                // Game ended - stop all timers
                StopAllTimers();
                Debug.Log("⏰ TimerManager: Game ended - all timers stopped");
                break;
                
            default:
                // Other states - pause timers
                if (timerActive)
                {
                    PauseTimer();
                    Debug.Log($"⏰ TimerManager: Game state {newState} - timer paused");
                }
                break;
        }
    }
    
    /// <summary>
    /// Get debug information about timer state
    /// MULTI-PLAYER SUPPORT: Shows all active player timers
    /// </summary>
    public string GetDebugInfo()
    {
        if (!enableTimedPlay) return "Timed Play: Disabled";

        string info = $"Timed Play: Enabled ({timePerPlayerMinutes} min per player)\\n";

        // Show all player times
        foreach (var kvp in playerTimeRemaining)
        {
            info += $"{kvp.Key} time: {GetFormattedTime(kvp.Key)} ({kvp.Value:F1}s)\\n";
        }

        info += $"Active timer: {activeTimerPlayer}\\n";
        info += $"Timer running: {timerActive}\\n";
        info += $"Initialized: {isInitialized}";

        return info;
    }

    /// <summary>
    /// Check if a specific player has an active timer
    /// MULTI-PLAYER SUPPORT: Used by UI to determine which timers to display
    /// </summary>
    public bool HasActiveTimer(PieceColor color)
    {
        return playerTimeRemaining.ContainsKey(color);
    }

    /// <summary>
    /// Get array of all player colors that have active timers
    /// MULTI-PLAYER SUPPORT: Used by UI to create timer displays dynamically
    /// </summary>
    public PieceColor[] GetActivePlayerColors()
    {
        return playerTimeRemaining.Keys.ToArray();
    }

    /// <summary>
    /// Get all timer states for debugging
    /// MULTI-PLAYER SUPPORT: Returns dictionary of all player times
    /// </summary>
    public Dictionary<PieceColor, float> GetAllTimerStates()
    {
        return new Dictionary<PieceColor, float>(playerTimeRemaining);
    }

    /// <summary>
    /// Handle chaos rotation started - pause timer to prevent unfair time consumption
    /// </summary>
    private void OnChaosRotationStarted(ChaosRotationType rotationType)
    {
        if (!enableTimedPlay || !isInitialized)
        {
            return;
        }
        
        if (timerActive)
        {
            PauseTimer();
            Debug.Log($"🌪️⏸️ TimerManager: Timer paused due to chaos rotation starting ({rotationType})");
        }
        else
        {
            Debug.Log($"🌪️ TimerManager: Chaos rotation started ({rotationType}) but timer was already inactive");
        }
    }
    
    /// <summary>
    /// Handle chaos rotation completed - resume timer for active player
    /// </summary>
    private void OnChaosRotationCompleted(ChaosRotationType rotationType)
    {
        if (!enableTimedPlay || !isInitialized)
        {
            return;
        }
        
        if (!timerActive)
        {
            ResumeTimer();
            Debug.Log($"🌪️▶️ TimerManager: Timer resumed after chaos rotation completion ({rotationType})");
        }
        else
        {
            Debug.Log($"🌪️ TimerManager: Chaos rotation completed ({rotationType}) but timer is already active");
        }
    }
    
    /// <summary>
    /// Clean up when timer manager is destroyed
    /// </summary>
    private void OnDestroy()
    {
        // Unsubscribe from game state events
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= OnGameStateChanged;
        }
        
        // Unsubscribe from chaos rotation events
        if (ChaosRotationManager.Instance != null)
        {
            ChaosRotationManager.Instance.OnChaosRotationStarted -= OnChaosRotationStarted;
            ChaosRotationManager.Instance.OnChaosRotationCompleted -= OnChaosRotationCompleted;
        }
        
        // Stop any running coroutines
        if (currentTimerCoroutine != null)
        {
            StopCoroutine(currentTimerCoroutine);
        }
    }
}