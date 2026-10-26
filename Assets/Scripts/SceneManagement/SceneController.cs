using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Types of scenes in the game
/// </summary>
public enum SceneType
{
    Unknown,
    MainMenu,
    Game
}

/// <summary>
/// Manages scene transitions and game configuration persistence between scenes.
/// This singleton handles loading scenes and passing data between them.
/// </summary>
public class SceneController : MonoBehaviour
{
    [Header("Scene Names")]
    public string mainMenuSceneName = "MainMenu";
    public string gameSceneName = "SampleScene";
    
    [Header("Scene Type Detection")]
    private SceneType currentSceneType = SceneType.Unknown;
    
    [Header("Loading")]
    public bool useAsyncLoading = true;
    public float minimumLoadTime = 1.0f; // Minimum time to show loading screen
    
    private GameConfiguration pendingGameConfig;
    private GameConfiguration currentGameConfig; // Persistent copy for GetCurrentGameConfiguration
    private bool isLoading = false;
    private string queuedScene; // asked for while another load was finishing: loaded right after it
    
    public static SceneController Instance { get; private set; }
    
    // Events
    public System.Action<string> OnSceneLoadStarted;
    public System.Action<string> OnSceneLoadCompleted;
    public System.Action<GameConfiguration> OnGameConfigurationSet;
    
    private void Awake()
    {
        Debug.Log("🚀 SceneController.Awake: CALLED - SceneController starting up!");
        Debug.Log($"SceneController.Awake: GameObject name={gameObject.name}");
        Debug.Log($"SceneController.Awake: Time.time={Time.time}");
        
        // Singleton pattern with DontDestroyOnLoad
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("✅ SceneController.Awake: Set as singleton Instance");
            Debug.Log("✅ SceneController.Awake: Marked as DontDestroyOnLoad");
            
            // Force redirect to MainMenu if we're in the wrong scene at startup
            try 
            {
                CheckAndRedirectToMainMenu();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ SceneController.Awake: Error in CheckAndRedirectToMainMenu: {e.Message}");
                Debug.LogError($"❌ SceneController.Awake: Stack trace: {e.StackTrace}");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ SceneController.Awake: Duplicate instance detected, destroying");
            Destroy(gameObject);
            return;
        }
        
        // Subscribe to scene loaded events
        SceneManager.sceneLoaded += OnSceneLoaded;
        Debug.Log("✅ SceneController.Awake: Initialization complete, redirect should begin soon");
    }
    
    /// <summary>
    /// Check if we're in the correct starting scene and redirect to MainMenu if not
    /// </summary>
    private void CheckAndRedirectToMainMenu()
    {
        Debug.Log("🔍 SceneController.CheckAndRedirectToMainMenu: ENTRY");
        
        string currentSceneName = SceneManager.GetActiveScene().name;
        Debug.Log($"🔍 SceneController: Current scene at startup: '{currentSceneName}'");
        Debug.Log($"🔍 SceneController: Target menu scene name: '{mainMenuSceneName}'");
        Debug.Log($"🔍 SceneController: Are they equal? {currentSceneName == mainMenuSceneName}");
        
        // If we're not in the MainMenu scene, redirect there
        if (currentSceneName != mainMenuSceneName)
        {
            Debug.Log($"🚨 SceneController: REDIRECT NEEDED! Not in MainMenu scene ({mainMenuSceneName}), redirecting from {currentSceneName}");
            
            #if UNITY_EDITOR
            Debug.Log("🎮 SceneController: Running in Unity Editor - using delayed redirect");
            // In editor, give a brief delay to ensure everything is initialized
            StartCoroutine(DelayedRedirectToMainMenu());
            #else
            Debug.Log("🎯 SceneController: Running in build - using immediate redirect");
            // In builds, redirect immediately
            LoadMainMenu();
            #endif
        }
        else
        {
            Debug.Log("✅ SceneController: Already in MainMenu scene, no redirect needed");
        }
        
        Debug.Log("🔍 SceneController.CheckAndRedirectToMainMenu: EXIT");
    }
    
    /// <summary>
    /// Delayed redirect for Unity Editor to ensure proper initialization
    /// </summary>
    private System.Collections.IEnumerator DelayedRedirectToMainMenu()
    {
        Debug.Log("⏳ SceneController.DelayedRedirectToMainMenu: Starting delay...");
        yield return new WaitForSeconds(0.1f); // Brief delay
        Debug.Log("⏳ SceneController.DelayedRedirectToMainMenu: Delay complete, performing redirect");
        LoadMainMenu();
    }
    
    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
    
    /// <summary>
    /// Load the main menu scene
    /// </summary>
    public void LoadMainMenu()
    {
        Debug.Log("🎯 SceneController.LoadMainMenu: ENTRY");
        Debug.Log($"🎯 SceneController.LoadMainMenu: About to load scene '{mainMenuSceneName}'");
        LoadScene(mainMenuSceneName);
        Debug.Log("🎯 SceneController.LoadMainMenu: LoadScene call completed");
    }
    
    /// <summary>
    /// Load the game scene with specified configuration
    /// </summary>
    public void LoadGameScene(GameConfiguration config)
    {
        if (config == null)
        {
            Debug.LogError("SceneController: Cannot load game scene with null configuration");
            return;
        }

        Debug.Log($"SceneController: Loading Game Scene with config: {config}");
        Debug.Log($"🔍 TRACE LoadGameScene: Received config.boardSize = {config.boardSize}");

        // Store the configuration for when the scene loads
        pendingGameConfig = config.Clone();
        Debug.Log($"🔍 TRACE LoadGameScene: pendingGameConfig.boardSize = {pendingGameConfig.boardSize}");
        OnGameConfigurationSet?.Invoke(pendingGameConfig);

        // CRITICAL: Apply board size BEFORE loading scene to ensure EmergencyChessBoard.Start() sees correct dimensions
        // BoardDimensionsManager persists via DontDestroyOnLoad, so set it now before scene transition
        Debug.Log($"🔍 TRACE LoadGameScene: BoardDimensionsManager.Instance = {(BoardDimensionsManager.Instance != null ? "EXISTS" : "NULL")}");
        if (BoardDimensionsManager.Instance != null)
        {
            Debug.Log($"🔍 TRACE LoadGameScene: About to call SetBoardSize({config.boardSize})");
            BoardDimensionsManager.Instance.SetBoardSize(config.boardSize);
            Debug.Log($"SceneController.LoadGameScene: Applied board size {config.boardSize} to BoardDimensionsManager BEFORE scene load");
            Debug.Log($"🔍 TRACE LoadGameScene: BoardDimensionsManager now reports dimensions = {BoardDimensionsManager.Instance.GetDimensions()}");
        }
        else
        {
            Debug.LogWarning("SceneController.LoadGameScene: BoardDimensionsManager.Instance not found, board size will be applied after scene load");
        }

        LoadScene(gameSceneName);
    }
    
    /// <summary>
    /// Load a scene by name
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (isLoading)
        {
            // Still finishing the last load (the menu that just opened holds it for minimumLoadTime):
            // go as soon as it's done instead of dropping the request, which left a player who picked
            // a puzzle straight away sitting on the menu
            Debug.Log($"SceneController: Still loading, {sceneName} goes next");
            queuedScene = sceneName;
            return;
        }
        
        if (useAsyncLoading)
        {
            StartCoroutine(LoadSceneAsync(sceneName));
        }
        else
        {
            LoadSceneImmediate(sceneName);
        }
    }
    
    /// <summary>
    /// Load scene immediately (synchronous)
    /// </summary>
    private void LoadSceneImmediate(string sceneName)
    {
        try
        {
            OnSceneLoadStarted?.Invoke(sceneName);
            SceneManager.LoadScene(sceneName);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SceneController: Failed to load scene '{sceneName}': {e.Message}");
            isLoading = false;
        }
    }
    
    /// <summary>
    /// Load scene asynchronously with loading feedback
    /// </summary>
    private IEnumerator LoadSceneAsync(string sceneName)
    {
        isLoading = true;
        float startTime = Time.time;
        
        OnSceneLoadStarted?.Invoke(sceneName);
        Debug.Log($"SceneController: Starting async load of scene '{sceneName}'");
        
        // Start loading the scene
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        
        if (asyncLoad == null)
        {
            Debug.LogError($"SceneController: Failed to start async load of scene '{sceneName}'");
            isLoading = false;
            yield break;
        }
        
        // Wait for the scene to load
        while (!asyncLoad.isDone)
        {
            // Optional: Update loading progress here
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f); // LoadSceneAsync never reaches 1.0
            yield return null;
        }
        
        // Ensure minimum loading time for smooth UX
        float elapsedTime = Time.time - startTime;
        if (elapsedTime < minimumLoadTime)
        {
            yield return new WaitForSecondsRealtime(minimumLoadTime - elapsedTime);
        }
        
        isLoading = false;
        Debug.Log($"SceneController: Completed async load of scene '{sceneName}' in {Time.time - startTime:F2} seconds");
        if (queuedScene != null)
        {
            string next = queuedScene;
            queuedScene = null;
            LoadScene(next);
        }
    }
    
    /// <summary>
    /// Called when a scene finishes loading
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"SceneController: Scene '{scene.name}' loaded successfully");
        
        // Detect and set current scene type
        currentSceneType = DetectSceneType(scene.name);
        Debug.Log($"SceneController: Scene type detected as {currentSceneType}");
        
        // Perform scene-specific cleanup
        PerformSceneCleanup(currentSceneType);
        
        OnSceneLoadCompleted?.Invoke(scene.name);
        
        // If we have a pending game configuration and this is the game scene, apply it
        if (pendingGameConfig != null && currentSceneType == SceneType.Game)
        {
            // Store persistent copy before applying
            currentGameConfig = pendingGameConfig.Clone();
            ApplyGameConfiguration(pendingGameConfig);
            pendingGameConfig = null; // Clear after applying
        }
        else if (currentSceneType == SceneType.Game && pendingGameConfig == null)
        {
            Debug.Log("SceneController: Game scene loaded without configuration - will let GameManager apply fallback");
            // GameManager will handle fallback configuration if needed
        }
    }
    
    /// <summary>
    /// Apply the game configuration to the loaded game scene
    /// </summary>
    private void ApplyGameConfiguration(GameConfiguration config)
    {
        Debug.Log($"SceneController: Applying game configuration: {config}");
        
        // Wait one frame to ensure all scene objects are initialized
        StartCoroutine(ApplyConfigurationDelayed(config));
    }
    
    /// <summary>
    /// Apply configuration with a slight delay to ensure scene is fully loaded
    /// </summary>
    private IEnumerator ApplyConfigurationDelayed(GameConfiguration config)
    {
        // CRITICAL: Apply board size BEFORE yield to ensure it's set before Start() methods execute
        // EmergencyChessBoard.Start() needs this value immediately on scene load
        Debug.Log($"🔍 TRACE ApplyConfigDelayed: config.boardSize = {config.boardSize}");
        Debug.Log($"🔍 TRACE ApplyConfigDelayed: BoardDimensionsManager.Instance = {(BoardDimensionsManager.Instance != null ? "EXISTS" : "NULL")}");
        if (BoardDimensionsManager.Instance != null)
        {
            Debug.Log($"🔍 TRACE ApplyConfigDelayed: BoardDimensionsManager dimensions BEFORE = {BoardDimensionsManager.Instance.GetDimensions()}");
            Debug.Log($"🔍 TRACE ApplyConfigDelayed: About to call SetBoardSize({config.boardSize})");
            BoardDimensionsManager.Instance.SetBoardSize(config.boardSize);
            Debug.Log($"SceneController: Applied board size {config.boardSize} to BoardDimensionsManager (BEFORE yield)");
            Debug.Log($"🔍 TRACE ApplyConfigDelayed: BoardDimensionsManager dimensions AFTER = {BoardDimensionsManager.Instance.GetDimensions()}");
        }
        else
        {
            Debug.LogWarning("SceneController: BoardDimensionsManager.Instance not found when applying board size");
        }

        yield return null; // Wait one frame for other managers to initialize

        // Apply to PlayerManager (CRITICAL for scalable turn cycling)
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.InitializePlayers(config);
            Debug.Log($"SceneController: Initialized PlayerManager with {config.playerCount} players");
        }
        else
        {
            Debug.LogWarning("SceneController: PlayerManager.Instance not found when applying configuration");
        }

        // Apply to TurnManager
        if (TurnManager.Instance != null)
        {
            // CRITICAL: Only use SetGameMode for 2-player games (uses legacy whitePlayerType/blackPlayerType fields)
            // For multi-player games (3-6 players), PlayerManager.InitializePlayers() already set all player types
            // Calling SetGameMode() would overwrite the correct multi-player configuration
            if (config.playerCount == 2)
            {
                TurnManager.Instance.SetGameMode(config.whitePlayerType, config.blackPlayerType);
                Debug.Log("SceneController: Applied player types to TurnManager (2-player mode)");
            }
            else
            {
                Debug.Log($"SceneController: Skipping TurnManager.SetGameMode for {config.playerCount}-player game (PlayerManager already configured)");
            }

            TurnManager.Instance.SetTurnValidation(config.enableTurnValidation);
        }
        else
        {
            Debug.LogWarning("SceneController: TurnManager.Instance not found when applying configuration");
        }

        // Apply to AIPlayer
        if (AIPlayer.Instance != null)
        {
            AIPlayer.Instance.SetDifficulty(config.aiDifficulty);
            Debug.Log($"SceneController: Applied AI difficulty {config.aiDifficulty} to AIPlayer");
        }
        else if (config.IsHumanVsAI() || config.IsAIVsAI())
        {
            Debug.LogWarning("SceneController: AIPlayer.Instance not found but AI is required for this game mode");
        }
        
        // Apply to ChaosRotationManager
        if (ChaosRotationManager.Instance != null)
        {
            ChaosRotationManager.Instance.ApplyChaosConfiguration(config);
            Debug.Log($"SceneController: Applied chaos mode configuration (enabled: {config.enableChaosMode}) to ChaosRotationManager");
        }
        else if (config.enableChaosMode)
        {
            Debug.LogWarning("SceneController: ChaosRotationManager.Instance not found but chaos mode is enabled in configuration");
        }
        
        // Apply other settings as needed
        // TODO: Apply showMoveHints, enableSoundEffects when these systems are implemented
        
        // Initialize the game state to start the actual game
        if (GameStateManager.Instance != null)
        {
            // CRITICAL FIX: Reset any non-WaitingForConfiguration state before initialization
            if (GameStateManager.Instance.currentState != GameState.WaitingForConfiguration)
            {
                Debug.Log($"SceneController: Resetting {GameStateManager.Instance.currentState} state to WaitingForConfiguration for new game");
                GameStateManager.Instance.ChangeState(GameState.WaitingForConfiguration);
            }
            
            GameStateManager.Instance.InitializeGameWithConfiguration();
            Debug.Log("SceneController: Game initialized with configuration");
        }
        else
        {
            Debug.LogWarning("SceneController: GameStateManager.Instance not found when initializing game");
        }
        
        Debug.Log("SceneController: Game configuration applied successfully");
    }
    
    /// <summary>
    /// DEBUG METHOD: Apply AI vs AI configuration for testing
    /// Can be called from inspector or console
    /// </summary>
    [ContextMenu("Force AI vs AI Configuration")]
    public void ForceAIVsAIConfiguration()
    {
        Debug.Log("🤖 SceneController: Forcing AI vs AI configuration");
        
        // Create AI vs AI configuration
        var aiConfig = new GameConfiguration
        {
            playerCount = 2,
            aiPlayerCount = 2,
            whitePlayerType = PlayerType.Computer,
            blackPlayerType = PlayerType.Computer,
            aiDifficulty = AIDifficulty.Medium,
            boardSize = BoardSize.Small5x5x5,
            enableTurnValidation = true,
            showMoveHints = true,
            enableSoundEffects = true
        };
        
        if (currentSceneType == SceneType.Game)
        {
            Debug.Log("🤖 SceneController: Applying AI vs AI configuration to current game scene");
            ApplyGameConfiguration(aiConfig);
        }
        else
        {
            Debug.Log("🤖 SceneController: Storing AI vs AI configuration as pending");
            pendingGameConfig = aiConfig;
        }
    }
    
    /// <summary>
    /// Get the current game configuration (if any)
    /// </summary>
    public GameConfiguration GetCurrentGameConfiguration()
    {
        Debug.Log($"🔍 SceneController.GetCurrentGameConfiguration: currentGameConfig={(currentGameConfig != null ? "EXISTS" : "NULL")}, pendingGameConfig={(pendingGameConfig != null ? "EXISTS" : "NULL")}");
        
        // Return persistent config if available, otherwise pending config
        GameConfiguration result = currentGameConfig?.Clone() ?? pendingGameConfig?.Clone();
        
        if (result != null)
        {
            Debug.Log($"🔍 SceneController.GetCurrentGameConfiguration: Returning config with enableTimedPlay={result.enableTimedPlay}, timePerPlayer={result.timePerPlayerMinutes}");
        }
        else
        {
            Debug.LogWarning("🔍 SceneController.GetCurrentGameConfiguration: No configuration available, returning NULL");
        }
        
        return result;
    }
    
    /// <summary>
    /// Check if currently loading a scene
    /// </summary>
    public bool IsLoading()
    {
        return isLoading;
    }
    
    /// <summary>
    /// Quit the application (for standalone builds)
    /// </summary>
    public void QuitApplication()
    {
        Debug.Log("SceneController: Quitting application");
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
    
    /// <summary>
    /// Quick load game with Human vs AI configuration
    /// </summary>
    public void QuickStartHumanVsAI(PieceColor humanColor = PieceColor.White, AIDifficulty difficulty = AIDifficulty.Medium)
    {
        GameConfiguration config;
        
        if (humanColor == PieceColor.White)
        {
            config = GameConfiguration.HumanWhiteVsAI(difficulty);
        }
        else
        {
            config = GameConfiguration.HumanBlackVsAI(difficulty);
        }
        
        LoadGameScene(config);
    }
    
    /// <summary>
    /// Quick load game with AI vs AI configuration
    /// </summary>
    public void QuickStartAIVsAI(AIDifficulty difficulty = AIDifficulty.Easy)
    {
        GameConfiguration config = GameConfiguration.AIVsAI(difficulty);
        LoadGameScene(config);
    }
    
    /// <summary>
    /// Detect the type of scene based on scene name
    /// </summary>
    private SceneType DetectSceneType(string sceneName)
    {
        if (sceneName == mainMenuSceneName)
        {
            return SceneType.MainMenu;
        }
        else if (sceneName == gameSceneName)
        {
            return SceneType.Game;
        }
        else
        {
            Debug.LogWarning($"SceneController: Unknown scene type for scene '{sceneName}'");
            return SceneType.Unknown;
        }
    }
    
    /// <summary>
    /// Perform scene-specific cleanup when entering a scene
    /// </summary>
    private void PerformSceneCleanup(SceneType sceneType)
    {
        switch (sceneType)
        {
            case SceneType.MainMenu:
                Debug.Log("SceneController: Entering main menu - performing menu cleanup");
                CleanupForMenuScene();
                break;
                
            case SceneType.Game:
                Debug.Log("SceneController: Entering game scene - performing game cleanup");
                CleanupForGameScene();
                break;
                
            case SceneType.Unknown:
                Debug.LogWarning("SceneController: Unknown scene type - skipping cleanup");
                break;
        }
    }
    
    /// <summary>
    /// Cleanup when entering the main menu scene
    /// </summary>
    private void CleanupForMenuScene()
    {
        // Clear any pending game configuration
        pendingGameConfig = null;
        currentGameConfig = null; // Also clear persistent config when returning to menu
        
        // Reset any game-specific state that might persist
        Debug.Log("SceneController: Menu scene cleanup completed");
    }
    
    /// <summary>
    /// Cleanup when entering the game scene
    /// </summary>
    private void CleanupForGameScene()
    {
        // Any game scene specific cleanup can go here
        // For now, just ensure we're ready to receive configuration
        Debug.Log("SceneController: Game scene cleanup completed - ready for configuration");
    }
    
    /// <summary>
    /// Get the current scene type
    /// </summary>
    public SceneType GetCurrentSceneType()
    {
        return currentSceneType;
    }
    
    /// <summary>
    /// Check if currently in main menu scene
    /// </summary>
    public bool IsInMainMenu()
    {
        return currentSceneType == SceneType.MainMenu;
    }
    
    /// <summary>
    /// Check if currently in game scene
    /// </summary>
    public bool IsInGameScene()
    {
        return currentSceneType == SceneType.Game;
    }
    
    /// <summary>
    /// Test method to verify scene flow works correctly
    /// </summary>
    [ContextMenu("Test Scene Flow")]
    public void TestSceneFlow()
    {
        Debug.Log("=== TESTING SCENE FLOW ===");
        Debug.Log($"Current Scene Type: {currentSceneType}");
        Debug.Log($"Pending Game Config: {(pendingGameConfig != null ? pendingGameConfig.ToString() : "None")}");
        
        if (IsInMainMenu())
        {
            Debug.Log("Testing: Load Human vs AI game from menu...");
            QuickStartHumanVsAI(PieceColor.White, AIDifficulty.Easy);
        }
        else if (IsInGameScene())
        {
            Debug.Log("Testing: Return to main menu from game...");
            LoadMainMenu();
        }
        else
        {
            Debug.LogWarning("Testing: Unknown scene type, loading main menu...");
            LoadMainMenu();
        }
    }
}