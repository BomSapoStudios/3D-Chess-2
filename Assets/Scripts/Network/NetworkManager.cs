/**
 * NetworkManager for 3D Chess Online Multiplayer
 * Uses native SocketIOClient for cross-platform support
 * Supports: Windows, Mac, Linux, iOS, Android, WebGL
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ChessNetwork;

/// <summary>
/// Manages network communication for online multiplayer chess games.
/// Handles server connection, room management, and game state synchronization.
/// </summary>
public class NetworkManager : MonoBehaviour
{
    [Header("Server Configuration")]
    [SerializeField] private string serverUrl = "http://localhost:3000";
    [SerializeField] private bool useProductionUrl = false;  // Set to false for local development
    [SerializeField] private string productionUrl = ""; // Legacy Socket.IO server; online play now uses OnlineClient
    [SerializeField] private bool autoConnect = false;
    [SerializeField] private float connectionTimeout = 10f;

    #if UNITY_WEBGL && !UNITY_EDITOR
    [SerializeField] private float reconnectDelay = 2f;  // WebGL: Faster reconnection attempts
    #else
    [SerializeField] private float reconnectDelay = 5f;  // Standalone: Standard delay
    #endif
    
    [Header("Debug Settings")]
    [SerializeField] private bool enableDebugLogging = true;
    [SerializeField] private bool logAllMessages = false;
    
    // Singleton instance
    public static NetworkManager Instance { get; private set; }
    
    // Connection state
    public bool IsConnected { get; private set; } = false;
    public bool IsHost { get; private set; } = false;
    public string RoomCode { get; private set; } = "";
    public string PlayerName { get; private set; } = "";
    public PieceColor AssignedColor { get; private set; } = PieceColor.White;
    public ConnectionStatus CurrentConnectionStatus { get; private set; }
    
    // Native Socket.IO client (works on all platforms)
    private SocketIOClient socket;
    
    // Game state
    private NetworkGameState lastGameState;
    private Dictionary<string, NetworkPlayerInfo> connectedPlayers = new Dictionary<string, NetworkPlayerInfo>();
    private Queue<NetworkMoveData> pendingMoves = new Queue<NetworkMoveData>();
    
    // Timing and reconnection
    private float lastPingTime;
    private float lastHeartbeat;
    private bool isReconnecting = false;
    private int reconnectAttempts = 0;

    #if UNITY_WEBGL && !UNITY_EDITOR
    private const int maxReconnectAttempts = 10;  // WebGL: More attempts due to browser restrictions
    #else
    private const int maxReconnectAttempts = 5;   // Standalone: Standard attempts
    #endif
    
    #region Events
    
    // Connection events
    public System.Action<bool> OnConnectionStateChanged;
    public System.Action<ConnectionStatus> OnConnectionStatusUpdated;
    public System.Action<string> OnConnectionError;
    
    // Room events  
    public System.Action<string, PieceColor> OnRoomCreated;
    public System.Action<string, PieceColor> OnRoomJoined;
    public System.Action<string> OnRoomError;
    public System.Action<NetworkPlayerInfo> OnPlayerJoined;
    public System.Action<NetworkPlayerInfo> OnPlayerLeft;
    
    // Matchmaking events
    public System.Action<string, PieceColor> OnMatchFound;
    public System.Action<string> OnMatchmakingProgress;
    public System.Action OnMatchmakingTimeout;
    
    // Game events
    public System.Action<NetworkGameState> OnGameStateUpdated;
    public System.Action<NetworkMoveData> OnMoveReceived;
    public System.Action OnGameStarted;
    public System.Action<string> OnGameEnded;
    public System.Action<NetworkErrorMessage> OnGameError;
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Set the assigned color for this player
    /// </summary>
    public void SetAssignedColor(PieceColor color)
    {
        AssignedColor = color;
        if (enableDebugLogging)
            Debug.Log($"🎨 NetworkManager: AssignedColor set to {color}");
    }
    
    #endregion
    
    #region Unity Lifecycle
    
    private void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeNetworking();
            
            if (enableDebugLogging)
                Debug.Log("🌐 NetworkManager: Instance created and initialized");
        }
        else if (Instance != this)
        {
            Debug.LogWarning("🌐 NetworkManager: Duplicate instance detected, destroying...");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        if (autoConnect && !IsConnected)
        {
            ConnectToServer();
        }
    }
    
    private void Update()
    {
        // Handle periodic tasks
        HandleHeartbeat();
        HandleReconnection();
        ProcessPendingMoves();
    }
    
    private void OnDestroy()
    {
        DisconnectFromServer();
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeNetworking()
    {
        // Determine the actual server URL to use
        string actualServerUrl = DetermineServerUrl();
        
        CurrentConnectionStatus = new ConnectionStatus
        {
            isConnected = false,
            serverUrl = actualServerUrl,
            status = "disconnected"
        };
        
        // Initialize native Socket.IO client
        socket = new SocketIOClient(this);

        if (enableDebugLogging)
        {
            Debug.Log($"🌐 NetworkManager: Native Socket.IO client initialized for {actualServerUrl}");
            Debug.Log($"✅ Cross-platform support: Windows, Mac, Linux, iOS, Android, WebGL");
        }
    }
    
    /// <summary>
    /// Determine which server URL to use based on configuration
    /// </summary>
    private string DetermineServerUrl()
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Try to auto-detect server from current browser URL
        string currentUrl = Application.absoluteURL;
        if (!string.IsNullOrEmpty(currentUrl))
        {
            try
            {
                Uri uri = new Uri(currentUrl);
                string webglServerUrl = $"{uri.Scheme}://{uri.Host}:3000";

                if (enableDebugLogging)
                    Debug.Log($"🌐 WebGL: Auto-detected server URL from browser: {webglServerUrl}");

                return webglServerUrl;
            }
            catch (Exception e)
            {
                if (enableDebugLogging)
                    Debug.LogWarning($"⚠️ WebGL: Failed to parse URL: {e.Message}");
            }
        }

        // WebGL fallback: Use production URL if set, otherwise localhost
        if (useProductionUrl && !string.IsNullOrEmpty(productionUrl))
        {
            if (enableDebugLogging)
                Debug.Log($"🌐 WebGL: Using configured production URL: {productionUrl}");
            return productionUrl;
        }

        if (enableDebugLogging)
            Debug.Log($"🌐 WebGL: Using default server URL: {serverUrl}");
        return serverUrl;

        #else
        // Standalone platforms: Use original logic
        if (useProductionUrl && !string.IsNullOrEmpty(productionUrl))
        {
            if (enableDebugLogging)
                Debug.Log($"🌐 Using production URL: {productionUrl}");
            return productionUrl;
        }

        // Use a saved server URL if there is one, otherwise localhost
        string detectedUrl = TryLoadSavedServerUrl();
        if (!string.IsNullOrEmpty(detectedUrl))
        {
            if (enableDebugLogging)
                Debug.Log($"🌐 Using saved server URL: {detectedUrl}");
            return detectedUrl;
        }

        if (enableDebugLogging)
            Debug.Log($"🌐 Using default localhost URL: {serverUrl}");
        return serverUrl;
        #endif
    }
    
    /// <summary>
    /// Load a server URL saved by SaveServerUrlToConfig, if any
    /// </summary>
    private string TryLoadSavedServerUrl()
    {
        // In a real implementation, you might check environment variables or config files
        // For now, we'll provide a way to manually set the production URL

        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Use PlayerPrefs instead of File.IO (files not accessible in browser)
        try
        {
            string configUrl = PlayerPrefs.GetString("ServerConfigUrl", "");
            if (!string.IsNullOrEmpty(configUrl) && configUrl.StartsWith("http"))
            {
                if (enableDebugLogging)
                    Debug.Log($"🌐 WebGL: Loaded server URL from PlayerPrefs: {configUrl}");
                return configUrl;
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
                Debug.LogWarning($"⚠️ WebGL: Error reading server config from PlayerPrefs: {e.Message}");
        }
        #else
        // Standalone: Use file system
        try
        {
            string configPath = Path.Combine(Application.persistentDataPath, "server_config.txt");
            if (File.Exists(configPath))
            {
                string configUrl = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(configUrl) && configUrl.StartsWith("http"))
                {
                    return configUrl;
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
                Debug.LogWarning($"⚠️ Error reading server config: {e.Message}");
        }
        #endif

        return null;
    }
    
    
    #endregion
    
    #region Connection Management
    
    /// <summary>
    /// Connect to the game server
    /// </summary>
    public void ConnectToServer()
    {
        if (IsConnected)
        {
            if (enableDebugLogging)
                Debug.LogWarning("🌐 NetworkManager: Already connected to server");
            return;
        }
        
        // Get the current server URL (may have been updated)
        string currentServerUrl = DetermineServerUrl();
        CurrentConnectionStatus.serverUrl = currentServerUrl;
        
        if (enableDebugLogging)
            Debug.Log($"🌐 NetworkManager: Connecting to server at {currentServerUrl}");
        
        UpdateConnectionStatus("connecting");

        // Connect using native Socket.IO client
        socket.Connect(
            currentServerUrl,
            "UNITY",
            onSuccess: () => {
                IsConnected = true;
                UpdateConnectionStatus("connected");
                SetupSocketEventHandlers();
                OnConnectionStateChanged?.Invoke(true);

                if (enableDebugLogging)
                    Debug.Log($"✅ Connected to server successfully! Socket ID: {socket.SocketId}");
            },
            onFail: (error) => {
                IsConnected = false;
                UpdateConnectionStatus("error");
                OnConnectionError?.Invoke(error);

                Debug.LogError($"❌ Connection failed: {error}");
            }
        );
    }
    
    /// <summary>
    /// Disconnect from the game server
    /// </summary>
    public void DisconnectFromServer()
    {
        if (!IsConnected) return;

        if (enableDebugLogging)
            Debug.Log("🌐 NetworkManager: Disconnecting from server");

        // Disconnect socket
        socket?.Disconnect();

        IsConnected = false;
        IsHost = false;
        RoomCode = "";
        UpdateConnectionStatus("disconnected");

        OnConnectionStateChanged?.Invoke(false);
    }

    /// <summary>
    /// Setup all socket event handlers
    /// </summary>
    private void SetupSocketEventHandlers()
    {
        if (socket == null) return;

        // Connection events
        socket.On("disconnect", (data) => {
            IsConnected = false;
            UpdateConnectionStatus("disconnected");
            OnConnectionStateChanged?.Invoke(false);
            Debug.LogWarning("🔌 Disconnected from server");
        });

        socket.On("connection_error", (data) => {
            OnConnectionError?.Invoke(data);
            Debug.LogError($"❌ Connection error: {data}");
        });

        // Room events
        socket.On("room_created", HandleRoomCreated);
        socket.On("room_joined", HandleRoomJoined);
        socket.On("player_joined", HandlePlayerJoined);
        socket.On("player_left", HandlePlayerLeft);
        socket.On("room_error", HandleRoomError);

        // Matchmaking events
        socket.On("match_found", HandleMatchFound);
        socket.On("matchmaking_queue_joined", HandleQueueJoined);

        // Game events
        socket.On("game_started", HandleGameStarted);
        socket.On("move_received", HandleMoveReceived);
        socket.On("game_state_updated", HandleGameStateUpdated);
        socket.On("game_ended", HandleGameEnded);

        if (enableDebugLogging)
            Debug.Log("✅ Socket event handlers configured");
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handle room created event from server
    /// </summary>
    private void HandleRoomCreated(string data)
    {
        try
        {
            var response = JsonUtility.FromJson<RoomCreatedResponse>(data);
            RoomCode = response.roomCode;
            SetAssignedColor(ParseColor(response.assignedColor));
            IsHost = response.isHost;

            if (enableDebugLogging)
                Debug.Log($"🏠 Room created: {RoomCode}, Color: {AssignedColor}, Host: {IsHost}");

            OnRoomCreated?.Invoke(RoomCode, AssignedColor);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error handling room_created: {e.Message}");
        }
    }

    /// <summary>
    /// Handle room joined event from server
    /// </summary>
    private void HandleRoomJoined(string data)
    {
        try
        {
            var response = JsonUtility.FromJson<RoomJoinedResponse>(data);
            RoomCode = response.roomCode;
            SetAssignedColor(ParseColor(response.assignedColor));
            IsHost = false;

            if (enableDebugLogging)
                Debug.Log($"🚪 Joined room: {RoomCode}, Color: {AssignedColor}");

            OnRoomJoined?.Invoke(RoomCode, AssignedColor);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error handling room_joined: {e.Message}");
        }
    }

    /// <summary>
    /// Handle player joined event
    /// </summary>
    private void HandlePlayerJoined(string data)
    {
        try
        {
            var playerInfo = JsonUtility.FromJson<ChessNetwork.NetworkPlayerInfo>(data);

            if (enableDebugLogging)
                Debug.Log($"👤 Player joined: {playerInfo.playerName} ({playerInfo.assignedColor})");

            connectedPlayers[playerInfo.playerId] = playerInfo;
            OnPlayerJoined?.Invoke(playerInfo);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error handling player_joined: {e.Message}");
        }
    }

    /// <summary>
    /// Handle player left event
    /// </summary>
    private void HandlePlayerLeft(string data)
    {
        try
        {
            var playerInfo = JsonUtility.FromJson<ChessNetwork.NetworkPlayerInfo>(data);

            if (enableDebugLogging)
                Debug.Log($"👋 Player left: {playerInfo.playerName}");

            connectedPlayers.Remove(playerInfo.playerId);
            OnPlayerLeft?.Invoke(playerInfo);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error handling player_left: {e.Message}");
        }
    }

    /// <summary>
    /// Handle room error event
    /// </summary>
    private void HandleRoomError(string data)
    {
        try
        {
            var error = JsonUtility.FromJson<ErrorResponse>(data);
            string errorMsg = error.error ?? data;

            Debug.LogError($"❌ Room error: {errorMsg}");
            OnRoomError?.Invoke(errorMsg);
        }
        catch
        {
            OnRoomError?.Invoke(data);
        }
    }

    /// <summary>
    /// Handle match found event (from matchmaking)
    /// </summary>
    private void HandleMatchFound(string data)
    {
        try
        {
            var response = JsonUtility.FromJson<MatchFoundResponse>(data);
            RoomCode = response.roomCode;
            SetAssignedColor(ParseColor(response.assignedColor));
            IsHost = response.isHost;

            if (enableDebugLogging)
                Debug.Log($"🎯 Match found! Room: {RoomCode}, Color: {AssignedColor}");

            OnMatchFound?.Invoke(RoomCode, AssignedColor);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error handling match_found: {e.Message}");
        }
    }

    /// <summary>
    /// Handle queue joined event
    /// </summary>
    private void HandleQueueJoined(string data)
    {
        if (enableDebugLogging)
            Debug.Log($"🎮 Joined matchmaking queue: {data}");

        OnMatchmakingProgress?.Invoke("Searching for match...");
    }

    /// <summary>
    /// Handle game started event
    /// </summary>
    private void HandleGameStarted(string data)
    {
        if (enableDebugLogging)
            Debug.Log($"🎮 Game started: {data}");

        OnGameStarted?.Invoke();
    }

    /// <summary>
    /// Handle move received event
    /// </summary>
    private void HandleMoveReceived(string data)
    {
        try
        {
            var moveData = JsonUtility.FromJson<ChessNetwork.NetworkMoveData>(data);

            if (enableDebugLogging)
                Debug.Log($"📨 Move received: ({moveData.fromX},{moveData.fromY},{moveData.fromZ}) → ({moveData.toX},{moveData.toY},{moveData.toZ})");

            OnMoveReceived?.Invoke(moveData);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error handling move_received: {e.Message}");
        }
    }

    /// <summary>
    /// Handle game state updated event
    /// </summary>
    private void HandleGameStateUpdated(string data)
    {
        try
        {
            var gameState = JsonUtility.FromJson<ChessNetwork.NetworkGameState>(data);
            lastGameState = gameState;

            if (enableDebugLogging)
                Debug.Log($"🎮 Game state updated: Phase={gameState.gamePhase}, Turn={gameState.currentPlayer}");

            OnGameStateUpdated?.Invoke(gameState);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error handling game_state_updated: {e.Message}");
        }
    }

    /// <summary>
    /// Handle game ended event
    /// </summary>
    private void HandleGameEnded(string data)
    {
        try
        {
            var result = JsonUtility.FromJson<GameEndedResponse>(data);

            if (enableDebugLogging)
                Debug.Log($"🏁 Game ended: {result.result}");

            OnGameEnded?.Invoke(result.result);
        }
        catch
        {
            OnGameEnded?.Invoke(data);
        }
    }

    /// <summary>
    /// Parse color string to PieceColor enum
    /// </summary>
    private PieceColor ParseColor(string colorString)
    {
        if (System.Enum.TryParse<PieceColor>(colorString, true, out PieceColor color))
        {
            return color;
        }
        return PieceColor.White; // Default
    }

    // Helper classes for JSON parsing
    [System.Serializable]
    private class RoomCreatedResponse
    {
        public string roomCode;
        public string assignedColor;
        public bool isHost;
    }

    [System.Serializable]
    private class RoomJoinedResponse
    {
        public string roomCode;
        public string assignedColor;
    }

    [System.Serializable]
    private class MatchFoundResponse
    {
        public string roomCode;
        public string assignedColor;
        public bool isHost;
    }

    [System.Serializable]
    private class ErrorResponse
    {
        public string error;
    }

    [System.Serializable]
    private class GameEndedResponse
    {
        public string result;
    }

    #endregion

    private void UpdateConnectionStatus(string status)
    {
        CurrentConnectionStatus.status = status;
        CurrentConnectionStatus.isConnected = IsConnected;
        CurrentConnectionStatus.lastHeartbeat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        OnConnectionStatusUpdated?.Invoke(CurrentConnectionStatus);
    }
    
    #region Room Management
    
    /// <summary>
    /// Create a new game room
    /// </summary>
    public void CreateRoom(string playerName)
    {
        CreateRoom(playerName, null);
    }
    
    /// <summary>
    /// Create a new game room with specific configuration
    /// </summary>
    public void CreateRoom(string playerName, GameConfiguration gameConfig)
    {
        if (!IsConnected)
        {
            OnRoomError?.Invoke("Not connected to server");
            return;
        }
        
        PlayerName = playerName;
        IsHost = true;
        
        if (enableDebugLogging)
        {
            string configDesc = gameConfig?.GetFullDescription() ?? "default settings";
            Debug.Log($"🏠 NetworkManager: Creating room for player {playerName} with {configDesc}");
        }
        
        // Send create room message to server via Socket.IO
        // Convert BoardSize enum to integer (Small4x4x4=4, Small5x5x5=5, Medium6x6x6=6, Large8x8x8=8)
        int boardSizeInt = 8; // default
        if (gameConfig != null)
        {
            boardSizeInt = gameConfig.boardSize switch
            {
                BoardSize.Small4x4x4 => 4,
                BoardSize.Small5x5x5 => 5,
                BoardSize.Medium6x6x6 => 6,
                BoardSize.Large8x8x8 => 8,
                _ => 8
            };
        }

        var roomData = new
        {
            playerName = playerName,
            maxPlayers = gameConfig?.playerCount ?? 2,
            boardSize = boardSizeInt,
            chaosMode = gameConfig?.enableChaosMode ?? false,
            timedPlay = gameConfig?.enableTimedPlay ?? false,
            timeLimit = (gameConfig?.timePerPlayerMinutes ?? 10) * 60, // Convert minutes to seconds
            isPublic = false  // Can be made configurable later
        };

        socket.EmitJson("create_room", roomData, (response) => {
            if (enableDebugLogging)
                Debug.Log($"📨 Room creation response: {response}");
        });
    }
    
    /// <summary>
    /// Join an existing game room
    /// </summary>
    public void JoinRoom(string roomCode, string playerName)
    {
        if (!IsConnected)
        {
            OnRoomError?.Invoke("Not connected to server");
            return;
        }
        
        PlayerName = playerName;
        IsHost = false;
        
        if (enableDebugLogging)
            Debug.Log($"🚪 NetworkManager: Joining room {roomCode} as player {playerName}");
        
        // Send join room message to server via Socket.IO
        var joinData = new
        {
            roomCode = roomCode.ToUpper(),
            playerName = playerName
        };

        socket.EmitJson("join_room", joinData, (response) => {
            if (enableDebugLogging)
                Debug.Log($"📨 Room join response: {response}");
        });
    }

    /// <summary>
    /// Send a move to the server
    /// </summary>
    public void SendMove(BoardPosition from, BoardPosition to, PieceColor playerColor)
    {
        if (!IsConnected || string.IsNullOrEmpty(RoomCode))
        {
            if (enableDebugLogging)
                Debug.LogWarning("⚠️ Cannot send move - not connected or not in a room");
            return;
        }

        // Create move data
        var moveData = new NetworkMoveData
        {
            fromX = from.x,
            fromY = from.y,
            fromZ = from.z,
            toX = to.x,
            toY = to.y,
            toZ = to.z,
            playerColor = playerColor.ToString(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Send move to server
        socket.EmitJson("send_move", moveData, (response) => {
            if (enableDebugLogging)
                Debug.Log($"📨 Move sent: {from} → {to}");
        });

        // Add to pending moves queue
        pendingMoves.Enqueue(moveData);
    }
    
    /// <summary>
    /// Leave the current room
    /// </summary>
    public void LeaveRoom()
    {
        if (string.IsNullOrEmpty(RoomCode)) return;
        
        if (enableDebugLogging)
            Debug.Log($"👋 NetworkManager: Leaving room {RoomCode}");
        
        // TODO: Send leave room message to server
        
        RoomCode = "";
        IsHost = false;
        connectedPlayers.Clear();
    }
    
    /// <summary>
    /// Temporary method to simulate room creation for UI testing
    /// </summary>
    private System.Collections.IEnumerator SimulateRoomCreation()
    {
        yield return new UnityEngine.WaitForSeconds(0.5f);
        
        RoomCode = GenerateRoomCode();
        AssignedColor = PieceColor.White; // Host is typically white
        
        OnRoomCreated?.Invoke(RoomCode, AssignedColor);
        
        if (enableDebugLogging)
            Debug.Log($"🏠 NetworkManager: Room created successfully - Code: {RoomCode}");
    }
    
    /// <summary>
    /// Temporary method to simulate room joining for UI testing
    /// </summary>
    private System.Collections.IEnumerator SimulateRoomJoining(string roomCode)
    {
        yield return new UnityEngine.WaitForSeconds(0.5f);
        
        // Simulate specific error scenarios for testing
        if (roomCode.ToUpper() == "ERROR" || roomCode.ToUpper() == "NOROOM")
        {
            OnRoomError?.Invoke("Room not found - room does not exist");
            yield break;
        }
        
        if (roomCode.ToUpper() == "FULL" || roomCode.ToUpper() == "FULLL")
        {
            OnRoomError?.Invoke("Room full - all player positions filled");
            yield break;
        }
        
        RoomCode = roomCode;
        AssignedColor = PieceColor.Black; // Joiner is typically black
        
        OnRoomJoined?.Invoke(RoomCode, AssignedColor);
        
        if (enableDebugLogging)
            Debug.Log($"🚪 NetworkManager: Joined room successfully - Code: {RoomCode}");
    }
    
    private string GenerateRoomCode()
    {
        // Generate a 6-character room code
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Avoid confusing characters
        var random = new System.Random();
        var code = "";
        
        for (int i = 0; i < 6; i++)
        {
            code += chars[random.Next(chars.Length)];
        }
        
        return code;
    }
    
    /// <summary>
    /// Send create room message via Socket.IO
    /// </summary>
    private void SendCreateRoomMessage(string playerName, GameConfiguration gameConfig)
    {
        try
        {
            if (socket == null)
            {
                Debug.LogError("❌ Socket is null, cannot send create room message");
                StartCoroutine(SimulateRoomCreation());
                return;
            }
            
            // Use reflection to call emit method
            var socketType = socket.GetType();
            var emitMethod = socketType.GetMethod("Emit", new System.Type[] { typeof(string), typeof(object) });
            
            if (emitMethod != null)
            {
                // Create comprehensive room data including game configuration
                var gameConfigData = new
                {
                    playerCount = gameConfig?.playerCount ?? 2,
                    boardSize = gameConfig?.boardSize.ToString() ?? "Small5x5x5",
                    chaosMode = new
                    {
                        enabled = gameConfig?.enableChaosMode ?? false,
                        turnInterval = gameConfig?.chaosTurnInterval ?? 9
                    },
                    timedMode = new
                    {
                        enabled = gameConfig?.enableTimedPlay ?? false,
                        timePerPlayer = gameConfig?.timePerPlayerMinutes ?? 10
                    },
                    ai = new
                    {
                        enabled = (gameConfig?.aiPlayerCount ?? 0) > 0,
                        difficulty = gameConfig?.aiDifficulty.ToString() ?? "Medium"
                    }
                };
                
                var roomData = new
                {
                    playerName = playerName,
                    gameConfig = gameConfigData
                };
                
                emitMethod.Invoke(socket, new object[] { "create_room", roomData });
                
                if (enableDebugLogging)
                {
                    string configDesc = gameConfig?.GetFullDescription() ?? "default settings";
                    Debug.Log($"🏠 Sent create_room message for player: {playerName} with config: {configDesc}");
                }
                
                // Set up response handler
                SetupSocketIOEventHandlers();
            }
            else
            {
                Debug.LogWarning("⚠️ Socket.IO emit method not found, falling back to simulation");
                StartCoroutine(SimulateRoomCreation());
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error sending create room message: {e.Message}");
            StartCoroutine(SimulateRoomCreation());
        }
    }
    
    /// <summary>
    /// Send join room message via Socket.IO
    /// </summary>
    private void SendJoinRoomMessage(string roomCode, string playerName)
    {
        try
        {
            if (socket == null)
            {
                Debug.LogError("❌ Socket is null, cannot send join room message");
                StartCoroutine(SimulateRoomJoining(roomCode));
                return;
            }
            
            // Use reflection to call emit method
            var socketType = socket.GetType();
            var emitMethod = socketType.GetMethod("Emit", new System.Type[] { typeof(string), typeof(object) });
            
            if (emitMethod != null)
            {
                var joinData = new
                {
                    roomCode = roomCode,
                    playerName = playerName
                };
                
                emitMethod.Invoke(socket, new object[] { "join_room", joinData });
                
                if (enableDebugLogging)
                    Debug.Log($"🚪 Sent join_room message for room: {roomCode}, player: {playerName}");
                
                // Set up response handler
                SetupSocketIOEventHandlers();
            }
            else
            {
                Debug.LogWarning("⚠️ Socket.IO emit method not found, falling back to simulation");
                StartCoroutine(SimulateRoomJoining(roomCode));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error sending join room message: {e.Message}");
            StartCoroutine(SimulateRoomJoining(roomCode));
        }
    }
    
    /// <summary>
    /// Set up Socket.IO event handlers for room responses
    /// </summary>
    private void SetupSocketIOEventHandlers()
    {
        if (socket == null) return;
        
        try
        {
            var socketType = socket.GetType();
            var onMethod = socketType.GetMethod("On", new System.Type[] { typeof(string), typeof(System.Action<object>) });
            
            if (onMethod != null)
            {
                // Set up room creation response handler
                System.Action<object> roomCreatedHandler = (data) => {
                    HandleRoomCreatedResponse(data);
                };
                onMethod.Invoke(socket, new object[] { "room_created", roomCreatedHandler });
                
                // Set up room join response handler
                System.Action<object> roomJoinedHandler = (data) => {
                    HandleRoomJoinedResponse(data);
                };
                onMethod.Invoke(socket, new object[] { "room_joined", roomJoinedHandler });
                
                // Set up error handlers
                System.Action<object> errorHandler = (data) => {
                    HandleSocketIOError(data);
                };
                onMethod.Invoke(socket, new object[] { "room_join_failed", errorHandler });
                onMethod.Invoke(socket, new object[] { "room_create_failed", errorHandler });
                
                if (enableDebugLogging)
                    Debug.Log("🔌 Socket.IO event handlers set up successfully");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error setting up Socket.IO event handlers: {e.Message}");
        }
    }
    
    /// <summary>
    /// Handle room created response from server
    /// </summary>
    private void HandleRoomCreatedResponse(object data)
    {
        try
        {
            if (enableDebugLogging)
                Debug.Log($"🏠 Received room_created response: {data}");
            
            // In a real implementation, you would parse the JSON response
            // For now, simulate success and generate a room code
            RoomCode = GenerateRoomCode();
            AssignedColor = PieceColor.White; // Host is typically white
            
            OnRoomCreated?.Invoke(RoomCode, AssignedColor);
            
            if (enableDebugLogging)
                Debug.Log($"🏠 Room created successfully - Code: {RoomCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error handling room created response: {e.Message}");
            OnRoomError?.Invoke("Failed to process room creation response");
        }
    }
    
    /// <summary>
    /// Handle room joined response from server
    /// </summary>
    private void HandleRoomJoinedResponse(object data)
    {
        try
        {
            if (enableDebugLogging)
                Debug.Log($"🚪 Received room_joined response: {data}");
            
            // In a real implementation, you would parse the JSON response
            // For now, simulate success
            AssignedColor = PieceColor.Black; // Joiner is typically black
            
            OnRoomJoined?.Invoke(RoomCode, AssignedColor);
            
            if (enableDebugLogging)
                Debug.Log($"🚪 Joined room successfully - Code: {RoomCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error handling room joined response: {e.Message}");
            OnRoomError?.Invoke("Failed to process room join response");
        }
    }
    
    /// <summary>
    /// Handle Socket.IO errors
    /// </summary>
    private void HandleSocketIOError(object data)
    {
        try
        {
            string errorMessage = data?.ToString() ?? "Unknown Socket.IO error";
            
            if (enableDebugLogging)
                Debug.LogError($"❌ Socket.IO error: {errorMessage}");
            
            OnRoomError?.Invoke(errorMessage);
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error handling Socket.IO error: {e.Message}");
            OnRoomError?.Invoke("Failed to process server error");
        }
    }
    
    #endregion
    
    #region Matchmaking
    
    /// <summary>
    /// Start matchmaking with user preferences
    /// </summary>
    public void StartMatchmaking(GameConfiguration preferences)
    {
        if (!IsConnected)
        {
            OnRoomError?.Invoke("Not connected to server");
            return;
        }
        
        if (enableDebugLogging)
            Debug.Log($"🎯 NetworkManager: Starting matchmaking with preferences: {preferences.GetFullDescription()}");

        // Store player info (default for 2-player version)
        PlayerName = "Player";
        IsHost = false; // Matchmaking players are not hosts initially
        
        // Start the progressive matchmaking simulation
        StartCoroutine(SimulateProgressiveMatchmaking(preferences));
    }

    /// <summary>
    /// Join matchmaking queue for an existing room (auto-fill mode)
    /// </summary>
    public void JoinMatchmakingForRoom(string roomCode, GameConfiguration config, int timerSeconds)
    {
        if (!IsConnected)
        {
            OnRoomError?.Invoke("Not connected to server");
            return;
        }

        if (string.IsNullOrEmpty(roomCode))
        {
            OnRoomError?.Invoke("Room code is required for matchmaking");
            return;
        }

        if (enableDebugLogging)
            Debug.Log($"🎯 NetworkManager: Joining matchmaking for room {roomCode} with {timerSeconds}s timer");

        // Convert board size enum to int
        int boardSizeInt = config.boardSize switch
        {
            BoardSize.Small4x4x4 => 4,
            BoardSize.Small5x5x5 => 5,
            BoardSize.Medium6x6x6 => 6,
            BoardSize.Large8x8x8 => 8,
            _ => 8
        };

        // Prepare matchmaking data
        var matchmakingData = new
        {
            roomCode = roomCode,
            preferences = new
            {
                boardSize = boardSizeInt,
                playerCount = config.playerCount,
                chaosMode = config.enableChaosMode,
                timedPlay = config.enableTimedPlay,
                timeLimit = config.timePerPlayerMinutes * 60, // Convert minutes to seconds
                skillLevel = "any",
                includeAI = true // Always allow AI fill-in for auto-matchmaking
            },
            timerSeconds = timerSeconds
        };

        string jsonData = JsonUtility.ToJson(matchmakingData);
        socket.Emit("join_matchmaking_for_room", jsonData);

        if (enableDebugLogging)
            Debug.Log($"📤 Sent join_matchmaking_for_room event for room {roomCode}");

        // Start a coroutine to simulate the matchmaking timer
        StartCoroutine(SimulateMatchmakingTimer(timerSeconds));
    }

    /// <summary>
    /// Simulate matchmaking timer countdown
    /// </summary>
    private System.Collections.IEnumerator SimulateMatchmakingTimer(int seconds)
    {
        float elapsed = 0f;
        float duration = seconds;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            int remaining = Mathf.CeilToInt(duration - elapsed);

            // Update progress every second
            if (remaining != Mathf.CeilToInt(duration - (elapsed - Time.deltaTime)))
            {
                OnMatchmakingProgress?.Invoke($"Finding players... {remaining}s remaining");
            }

            yield return null;
        }

        // Timer expired
        if (enableDebugLogging)
            Debug.Log("⏱️ NetworkManager: Matchmaking timer expired - server will fill with AI");

        OnMatchmakingProgress?.Invoke("Timer expired - filling with AI players...");
    }

    /// <summary>
    /// Simulate progressive matchmaking with 30-second window
    /// </summary>
    private System.Collections.IEnumerator SimulateProgressiveMatchmaking(GameConfiguration preferences)
    {
        float matchmakingDuration = 30f;
        float elapsedTime = 0f;
        
        // Progressive matching phases
        bool phase1Checked = false; // 5-10 seconds: existing games
        bool phase2Checked = false; // 15-20 seconds: wait for new games
        bool phase3Checked = false; // 25-30 seconds: final attempt
        
        while (elapsedTime < matchmakingDuration)
        {
            // Phase 1: Check existing games (5-10 seconds)
            if (elapsedTime >= 5f && !phase1Checked)
            {
                phase1Checked = true;
                OnMatchmakingProgress?.Invoke("Checking existing games...");
                
                if (SimulateMatchAttempt(preferences, 0.4f)) // Higher chance for existing games
                {
                    yield return new WaitForSeconds(1f); // Brief delay for realism
                    CompleteMatchmaking("Found existing game!");
                    yield break;
                }
            }
            
            // Phase 2: Wait for new games (15-20 seconds)
            else if (elapsedTime >= 15f && !phase2Checked)
            {
                phase2Checked = true;
                OnMatchmakingProgress?.Invoke("Waiting for new games to be created...");
                
                if (SimulateMatchAttempt(preferences, 0.3f)) // Medium chance for new games
                {
                    yield return new WaitForSeconds(1f);
                    CompleteMatchmaking("Matched with new game!");
                    yield break;
                }
            }
            
            // Phase 3: Final attempt (25-30 seconds)
            else if (elapsedTime >= 25f && !phase3Checked)
            {
                phase3Checked = true;
                OnMatchmakingProgress?.Invoke("Final attempt...");
                
                if (SimulateMatchAttempt(preferences, 0.2f)) // Lower chance for final attempt
                {
                    yield return new WaitForSeconds(1f);
                    CompleteMatchmaking("Found game at last moment!");
                    yield break;
                }
            }
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        
        // Timeout - no match found
        OnMatchmakingTimeout?.Invoke();
        
        if (enableDebugLogging)
            Debug.Log("🚫 NetworkManager: Matchmaking timeout - no suitable games found");
    }
    
    /// <summary>
    /// Simulate a matchmaking attempt based on preferences
    /// </summary>
    private bool SimulateMatchAttempt(GameConfiguration preferences, float baseSuccessChance)
    {
        // SIMPLIFIED: For 2-player version, matchmaking is basic
        // More specific preferences would reduce chance of finding match in real implementation
        float adjustedChance = baseSuccessChance;

        // Reduce chance if specific chaos mode is requested
        if (preferences.enableChaosMode)
        {
            adjustedChance *= 0.7f; // Reduce by 30%
        }

        // Reduce chance if timed mode is requested
        if (preferences.enableTimedPlay)
        {
            adjustedChance *= 0.7f; // Reduce by 30%
        }
        
        bool success = UnityEngine.Random.Range(0f, 1f) < adjustedChance;
        
        if (enableDebugLogging)
            Debug.Log($"🎲 NetworkManager: Match attempt with {adjustedChance:F2} chance = {(success ? "SUCCESS" : "FAILED")}");
        
        return success;
    }
    
    /// <summary>
    /// Complete successful matchmaking
    /// </summary>
    private void CompleteMatchmaking(string message)
    {
        // Generate a realistic room code for the matched game
        RoomCode = GenerateRoomCode();
        AssignedColor = UnityEngine.Random.Range(0f, 1f) < 0.5f ? PieceColor.White : PieceColor.Black;
        
        OnMatchFound?.Invoke(RoomCode, AssignedColor);
        
        if (enableDebugLogging)
            Debug.Log($"🎯 NetworkManager: {message} - Room: {RoomCode}, Color: {AssignedColor}");
    }
    
    #endregion
    
    #region Move Management
    
    /// <summary>
    /// Start the game (host only)
    /// </summary>
    public void StartGame()
    {
        if (!IsHost)
        {
            Debug.LogError("🌐 NetworkManager: Only host can start the game");
            return;
        }
        
        if (enableDebugLogging)
            Debug.Log("🎮 NetworkManager: Starting game");
        
        // TODO: Send start game message to server
        OnGameStarted?.Invoke();
    }
    
    private int GetNextMoveNumber()
    {
        return lastGameState?.totalMoves + 1 ?? 1;
    }
    
    #endregion
    
    #region Message Processing
    
    private void HandleHeartbeat()
    {
        if (!IsConnected) return;
        
        // Send heartbeat every 30 seconds
        if (Time.time - lastHeartbeat > 30f)
        {
            // TODO: Send heartbeat to server
            lastHeartbeat = Time.time;
        }
    }
    
    private void HandleReconnection()
    {
        if (isReconnecting && Time.time - lastPingTime > reconnectDelay)
        {
            if (reconnectAttempts < maxReconnectAttempts)
            {
                reconnectAttempts++;
                ConnectToServer();
                lastPingTime = Time.time;
            }
            else
            {
                isReconnecting = false;
                OnConnectionError?.Invoke("Failed to reconnect after multiple attempts");
            }
        }
    }
    
    private void ProcessPendingMoves()
    {
        // Process queued moves (temporary for testing)
        while (pendingMoves.Count > 0)
        {
            var move = pendingMoves.Dequeue();
            
            // Simulate server processing and broadcast back
            StartCoroutine(SimulateMoveProcessing(move));
        }
    }
    
    private System.Collections.IEnumerator SimulateMoveProcessing(NetworkMoveData move)
    {
        yield return new UnityEngine.WaitForSeconds(0.1f);
        
        // Simulate receiving the move back from server
        OnMoveReceived?.Invoke(move);
        
        if (logAllMessages)
            Debug.Log($"🎯 NetworkManager: Move processed - {move.GetFromPosition()} → {move.GetToPosition()}");
    }
    
    #endregion
    
    #region Public API
    
    /// <summary>
    /// Update server URL (must be disconnected)
    /// </summary>
    public void SetServerUrl(string url)
    {
        if (IsConnected)
        {
            Debug.LogWarning("🌐 NetworkManager: Cannot change server URL while connected");
            return;
        }
        
        serverUrl = url;
        CurrentConnectionStatus.serverUrl = url;
        
        if (enableDebugLogging)
            Debug.Log($"🌐 NetworkManager: Server URL updated to {url}");
    }
    
    /// <summary>
    /// Set production URL and enable production mode
    /// </summary>
    public void SetProductionUrl(string url)
    {
        if (IsConnected)
        {
            Debug.LogWarning("🌐 NetworkManager: Cannot change production URL while connected");
            return;
        }
        
        productionUrl = url;
        useProductionUrl = true;
        
        // Update the connection status URL immediately
        CurrentConnectionStatus.serverUrl = url;
        
        if (enableDebugLogging)
            Debug.Log($"🌐 NetworkManager: Production URL set to {url}");
    }
    
    /// <summary>
    /// Save server URL to persistent config file for future auto-detection
    /// </summary>
    public void SaveServerUrlToConfig(string url)
    {
        try
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: Save to PlayerPrefs (persistent in browser)
            PlayerPrefs.SetString("ServerConfigUrl", url);
            PlayerPrefs.Save();

            if (enableDebugLogging)
                Debug.Log($"🌐 WebGL: Server URL saved to PlayerPrefs: {url}");
            #else
            // Standalone: Save to file
            string configPath = Path.Combine(Application.persistentDataPath, "server_config.txt");
            File.WriteAllText(configPath, url);

            if (enableDebugLogging)
                Debug.Log($"🌐 NetworkManager: Server URL saved to config file: {url}");
            #endif
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Error saving server config: {e.Message}");
        }
    }
    
    /// <summary>
    /// Get list of connected players
    /// </summary>
    public NetworkPlayerInfo[] GetConnectedPlayers()
    {
        var players = new NetworkPlayerInfo[connectedPlayers.Count];
        connectedPlayers.Values.CopyTo(players, 0);
        return players;
    }
    
    /// <summary>
    /// Check if it's the local player's turn
    /// </summary>
    public bool IsMyTurn()
    {
        return lastGameState?.GetCurrentPlayer() == AssignedColor;
    }
    
    /// <summary>
    /// Get the current game state
    /// </summary>
    public NetworkGameState GetCurrentGameState()
    {
        return lastGameState;
    }
    
    #endregion
    
    #region Debug and Testing
    
    /// <summary>
    /// Simulate network error for testing
    /// </summary>
    [ContextMenu("Simulate Connection Error")]
    public void SimulateConnectionError()
    {
        if (enableDebugLogging)
            Debug.LogWarning("🌐 NetworkManager: Simulating connection error");
        
        OnConnectionError?.Invoke("Simulated connection error");
    }
    
    /// <summary>
    /// Log current network state
    /// </summary>
    [ContextMenu("Log Network State")]
    public void LogNetworkState()
    {
        Debug.Log("=== NETWORK STATE ===");
        Debug.Log($"Connected: {IsConnected}");
        Debug.Log($"Server URL: {serverUrl}");
        Debug.Log($"Room Code: {RoomCode}");
        Debug.Log($"Is Host: {IsHost}");
        Debug.Log($"Player Name: {PlayerName}");
        Debug.Log($"Assigned Color: {AssignedColor}");
        Debug.Log($"Connected Players: {connectedPlayers.Count}");
        Debug.Log("===================");
    }
    
    #endregion
}