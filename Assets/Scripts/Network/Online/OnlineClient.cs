using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Connection to the Cloudflare game server (server-cf): guest sign-in, creating and
/// joining rooms, Find Match, and the room's WebSocket. Lives across scene loads.
/// Works on iPhone and WebGL (see GameSocket for how each platform connects).
/// </summary>
public class OnlineClient : MonoBehaviour
{
    // Production server. Development builds can override with PlayerPrefs "ServerUrl".
    public const string DefaultServerUrl = "https://3d-chess.bomsapostudios.workers.dev";

    public static OnlineClient Instance { get; private set; }

    public string ServerUrl
    {
        get
        {
            string custom = PlayerPrefs.GetString("ServerUrl", "");
            if (!string.IsNullOrEmpty(custom)) return custom.TrimEnd('/');
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Editor and development (Simulator) builds use the local server: `npm run dev` in server-cf
            return "http://localhost:8787";
#else
            return DefaultServerUrl;
#endif
        }
    }

    // Session state
    public bool IsConnected => socket != null && socket.IsOpen;
    public string RoomCode { get; private set; }
    public PieceColor LocalColor { get; private set; }
    public bool IsHost { get; private set; }
    public string PlayerName => PlayerPrefs.GetString("OnlineName", "");

    // Events (raised on the main thread)
    public event Action<ServerMessage> OnMessage;
    public event Action<string> OnClosed;

    private GameSocket socket;
    private GameSocket matchSocket;
    private int nextRequestId = 1;
    private readonly Dictionary<int, Action<ServerMessage>> pending = new Dictionary<int, Action<ServerMessage>>();
    private bool intentionalClose;

    public static OnlineClient GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("Online Client");
        DontDestroyOnLoad(go);
        return go.AddComponent<OnlineClient>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        socket?.Dispatch();
        matchSocket?.Dispatch();
    }

    private async void OnApplicationQuit()
    {
        intentionalClose = true;
        if (socket != null) await socket.Close();
        if (matchSocket != null) await matchSocket.Close();
    }

    // ───────────────────────── Account ─────────────────────────

    private bool refreshedThisSession;

    /// <summary>True when this device's account is linked to Sign in with Apple</summary>
    public bool SignedInWithApple => PlayerPrefs.GetInt("OnlineAppleLinked", 0) == 1;

    /// <summary>
    /// Make sure this device has an account and a fresh token. Reuses the saved account
    /// (refreshing its token and name once per session), or creates a guest account.
    /// </summary>
    public IEnumerator EnsureSignedIn(Action<bool, string> done)
    {
        string token = Token;
        bool sameServer = PlayerPrefs.GetString("OnlineTokenServer", "") == ServerUrl; // local test server vs live
        if (!string.IsNullOrEmpty(token) && sameServer)
        {
            if (refreshedThisSession) { done(true, null); yield break; }
            bool expired = false;
            string error = null;
            yield return Call("POST", "/auth/refresh", "{}", token, (status, text) =>
            {
                if (status == 200) SaveAuth(text);
                else if (status == 401) expired = true;
                else error = ErrorText(status, text);
            });
            if (error != null) { done(false, error); yield break; }
            if (!expired) { refreshedThisSession = true; done(true, null); yield break; }
        }

        string guestError = null;
        yield return Call("POST", "/auth/guest", "{}", null, (status, text) =>
        {
            if (status == 200) { SaveAuth(text); PlayerPrefs.SetInt("OnlineAppleLinked", 0); }
            else guestError = ErrorText(status, text);
        });
        refreshedThisSession = guestError == null;
        done(guestError == null, guestError);
    }

    /// <summary>Swap this player's name for a new unique one</summary>
    public IEnumerator RerollName(Action<string, string> done)
    {
        string name = null, error = null;
        yield return Call("POST", "/names/reroll", "{}", Token, (status, text) =>
        {
            if (status == 200) name = SaveAuth(text).name;
            else error = ErrorText(status, text);
        });
        done(name, error);
    }

    /// <summary>
    /// Link this account to Sign in with Apple, or, if that Apple ID already has an account
    /// (e.g. after a reinstall), switch to it. Calls back with (restored, error).
    /// </summary>
    public IEnumerator LinkApple(string identityToken, Action<bool, string> done)
    {
        bool restored = false;
        string error = null;
        string body = JsonUtility.ToJson(new IdTokenBody { idToken = identityToken });
        yield return Call("POST", "/auth/apple", body, Token, (status, text) =>
        {
            if (status != 200) { error = ErrorText(status, text); return; }
            restored = SaveAuth(text).restored;
            PlayerPrefs.SetInt("OnlineAppleLinked", 1);
            PlayerPrefs.Save();
        });
        done(restored, error);
    }

    /// <summary>A code to type on another device to use this account there</summary>
    public IEnumerator CreateLinkCode(Action<string, string> done)
    {
        string code = null, error = null;
        yield return Call("POST", "/link/code", "{}", Token, (status, text) =>
        {
            if (status == 200) code = JsonUtility.FromJson<LinkCodeResponse>(text).code;
            else error = ErrorText(status, text);
        });
        done(code, error);
    }

    /// <summary>Become the account that showed this code on another device</summary>
    public IEnumerator RedeemLinkCode(string code, Action<string> done)
    {
        string error = null;
        string body = "{\"code\":\"" + code.Trim().ToUpperInvariant() + "\"}";
        yield return Call("POST", "/link/redeem", body, null, (status, text) =>
        {
            if (status == 200) { SaveAuth(text); PlayerPrefs.SetInt("OnlineAppleLinked", 0); refreshedThisSession = false; }
            else error = ErrorText(status, text);
        });
        done(error);
    }

    // ───────────────────────── Friends ─────────────────────────

    public IEnumerator GetFriends(Action<FriendsResponse, string> done)
    {
        FriendsResponse result = null;
        string error = null;
        yield return Call("GET", "/friends", null, Token, (status, text) =>
        {
            if (status == 200) result = JsonUtility.FromJson<FriendsResponse>(text);
            else error = ErrorText(status, text);
        });
        done(result, error);
    }

    /// <summary>Players whose name contains the query (3+ letters)</summary>
    public IEnumerator SearchPlayers(string query, Action<FriendInfo[], string> done)
    {
        FriendInfo[] results = null;
        string error = null;
        yield return Call("GET", "/friends/search?q=" + UnityWebRequest.EscapeURL(query.Trim()), null, Token, (status, text) =>
        {
            if (status == 200) results = JsonUtility.FromJson<SearchResponse>(text).results ?? new FriendInfo[0];
            else error = ErrorText(status, text);
        });
        done(results, error);
    }

    public IEnumerator SendFriendRequest(string name, Action<string, string> done)
    {
        string result = null, error = null;
        string body = JsonUtility.ToJson(new NameBody { name = name.Trim() });
        yield return Call("POST", "/friends/request", body, Token, (status, text) =>
        {
            if (status == 200) result = JsonUtility.FromJson<FriendStatusResponse>(text).status;
            else error = ErrorText(status, text);
        });
        done(result, error);
    }

    public IEnumerator RespondToFriend(string friendId, bool accept, Action<string> done) =>
        Simple("/friends/respond", "{\"friendId\":\"" + friendId + "\",\"accept\":" + (accept ? "true" : "false") + "}", done);

    public IEnumerator RemoveFriend(string friendId, Action<string> done) =>
        Simple("/friends/remove", "{\"friendId\":\"" + friendId + "\"}", done);

    public IEnumerator BlockPlayer(string friendId, Action<string> done) =>
        Simple("/friends/block", "{\"friendId\":\"" + friendId + "\"}", done);

    public IEnumerator DismissChallenge(string roomCode, Action<string> done) =>
        Simple("/friends/dismiss", "{\"roomCode\":\"" + roomCode + "\"}", done);

    /// <summary>Create a private game and invite a friend to it. Calls back with (roomCode, error).</summary>
    public IEnumerator ChallengeFriend(string friendId, string configJson, Action<string, string> done)
    {
        string code = null, error = null;
        string body = "{\"friendId\":\"" + friendId + "\",\"config\":" + configJson + "}";
        yield return Call("POST", "/friends/challenge", body, Token, (status, text) =>
        {
            if (status == 200) code = JsonUtility.FromJson<CreateRoomResponse>(text).code;
            else error = ErrorText(status, text);
        });
        done(code, error);
    }

    private IEnumerator Simple(string path, string body, Action<string> done)
    {
        string error = null;
        yield return Call("POST", path, body, Token, (status, text) =>
        {
            if (status != 200) error = ErrorText(status, text);
        });
        done(error);
    }

    // ───────────────────────── HTTP ─────────────────────────

    private string Token => PlayerPrefs.GetString("OnlineToken", "");

    /// <summary>Send a request; calls back with (HTTP status, body). Status 0 = couldn't reach the server.</summary>
    private IEnumerator Call(string method, string path, string json, string token, Action<long, string> done)
    {
        using (var request = new UnityWebRequest(ServerUrl + path, method))
        {
            if (json != null) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
            request.timeout = 15;
            yield return request.SendWebRequest();
            long status = request.result == UnityWebRequest.Result.ConnectionError ? 0 : request.responseCode;
            done(status, request.downloadHandler.text);
        }
    }

    private AuthResponse SaveAuth(string text)
    {
        var auth = JsonUtility.FromJson<AuthResponse>(text);
        PlayerPrefs.SetString("OnlineToken", auth.token);
        PlayerPrefs.SetString("OnlineName", auth.name);
        PlayerPrefs.SetString("OnlineUserId", auth.userId);
        PlayerPrefs.SetString("OnlineTokenServer", ServerUrl);
        PlayerPrefs.Save();
        return auth;
    }

    private static string ErrorText(long status, string text)
    {
        if (status == 0) return "Couldn't reach the game server";
        try
        {
            var e = JsonUtility.FromJson<ErrorResponse>(text);
            if (!string.IsNullOrEmpty(e.error)) return e.error;
        }
        catch { /* not JSON */ }
        return $"Server error ({status})";
    }

    // ───────────────────────── Rooms ─────────────────────────

    /// <summary>Create a private room. configJson is the server's game config (see NeonMenu.OnlineConfigJson).</summary>
    public IEnumerator CreateRoom(string configJson, Action<string, string> done)
    {
        string code = null, error = null;
        string body = "{\"config\":" + configJson + "}";
        yield return Call("POST", "/rooms", body, Token, (status, text) =>
        {
            if (status == 200) code = JsonUtility.FromJson<CreateRoomResponse>(text).code;
            else error = ErrorText(status, text);
        });
        done(code, error);
    }

    /// <summary>Open the room's WebSocket. room:joined arrives through OnMessage.</summary>
    public async void JoinRoom(string code)
    {
        await CloseRoom();
        RoomCode = code.Trim().ToUpperInvariant();
        intentionalClose = false;
        string url = ServerUrl.Replace("https://", "wss://").Replace("http://", "ws://") +
                     $"/rooms/{RoomCode}/connect?token={UnityWebRequest.EscapeURL(Token)}";

        var opened = socket = new GameSocket(url);
        opened.OnMessage += Handle;
        opened.OnError += error => Debug.LogWarning($"OnlineClient: socket error {error}");
        opened.OnClose += reason =>
        {
            if (!intentionalClose && socket == opened) OnClosed?.Invoke("Connection lost");
        };
        await socket.Connect();
    }

    public async System.Threading.Tasks.Task CloseRoom()
    {
        if (socket == null) return;
        intentionalClose = true;
        var old = socket;
        socket = null;
        pending.Clear();
        try { await old.Close(); } catch { /* already closed */ }
    }

    public async void LeaveOnline()
    {
        await CloseRoom();
        await CloseMatchmaking();
        RoomCode = null;
        OnlineSession.End();
    }

    // ───────────────────────── Find Match ─────────────────────────

    public async void FindMatch(string boardSize, Action<string> onFound, Action<string> onError)
    {
        await CloseMatchmaking();
        string url = ServerUrl.Replace("https://", "wss://").Replace("http://", "ws://") +
                     $"/matchmaking/connect?token={UnityWebRequest.EscapeURL(Token)}";
        var queue = matchSocket = new GameSocket(url);
        queue.OnOpen += () =>
            queue.SendText("{\"type\":\"matchmaking:join\",\"config\":{\"boardSize\":\"" + boardSize + "\",\"playerCount\":2}}");
        queue.OnMessage += text =>
        {
            var msg = JsonUtility.FromJson<ServerMessage>(text);
            if (msg.type == "matchmaking:found") onFound(msg.roomCode);
        };
        queue.OnError += error =>
        {
            Debug.LogError($"OnlineClient: matchmaking socket error: {error}");
            onError(Debug.isDebugBuild ? $"Couldn't reach the game server ({error})" : "Couldn't reach the game server");
        };
        await matchSocket.Connect();
    }

    public async System.Threading.Tasks.Task CloseMatchmaking()
    {
        if (matchSocket == null) return;
        var old = matchSocket;
        matchSocket = null;
        try { await old.Close(); } catch { /* already closed */ }
    }

    // ───────────────────────── Game messages ─────────────────────────

    // Each takes the side acting: the host also sends for its robot seats ("as")

    public void SendPlacePiece(PieceColor color, ChessPieceType type, BoardPosition position) =>
        Send("{\"type\":\"game:placePiece\"," + As(color) + "\"pieceType\":\"" + type + "\",\"position\":" + Pos(position) + "}");

    public void SendReady(PieceColor color) => Send("{\"type\":\"game:ready\"" + (color == LocalColor ? "" : "," + As(color).TrimEnd(',')) + "}");

    public void SendMove(PieceColor color, BoardPosition from, BoardPosition to) =>
        Send("{\"type\":\"game:move\"," + As(color) + "\"from\":" + Pos(from) + ",\"to\":" + Pos(to) + "}");

    public void SendPromotion(PieceColor color, BoardPosition position, ChessPieceType type) =>
        Send("{\"type\":\"game:promotePawn\"," + As(color) + "\"position\":" + Pos(position) + ",\"pieceType\":\"" + type + "\"}");

    public void SendResign(PieceColor color) => Send("{\"type\":\"game:resign\"" + (color == LocalColor ? "" : "," + As(color).TrimEnd(',')) + "}");

    private string As(PieceColor color) => color == LocalColor ? "" : "\"as\":\"" + color + "\",";

    /// <summary>Send a request; the server's ack (ok/error) goes to the callback if given</summary>
    private void Send(string json, Action<ServerMessage> onAck = null)
    {
        if (!IsConnected)
        {
            Debug.LogWarning($"OnlineClient: not connected, dropped {json}");
            return;
        }
        int id = nextRequestId++;
        if (onAck != null) pending[id] = onAck;
        socket.SendText(json.Insert(json.Length - 1, ",\"requestId\":" + id));
    }

    private void Handle(string text)
    {
        ServerMessage msg;
        try { msg = JsonUtility.FromJson<ServerMessage>(text); }
        catch (Exception e) { Debug.LogWarning($"OnlineClient: bad message {e.Message}"); return; }

        if (msg.type == "ack")
        {
            if (pending.TryGetValue(msg.requestId, out var callback))
            {
                pending.Remove(msg.requestId);
                callback(msg);
            }
            if (!msg.ok) Debug.LogWarning($"OnlineClient: request {msg.requestId} refused: {msg.error}");
            return;
        }

        if (msg.type == "room:joined")
        {
            RoomCode = msg.roomCode;
            IsHost = msg.isHost;
            if (Enum.TryParse(msg.yourColor, out PieceColor color)) LocalColor = color;
        }
        OnMessage?.Invoke(msg);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static string Pos(BoardPosition p) => "{\"x\":" + p.x + ",\"y\":" + p.y + ",\"z\":" + p.z + "}";

    [Serializable] private class NameBody { public string name; }
    [Serializable] private class IdTokenBody { public string idToken; }
    [Serializable] private class AuthResponse { public string token; public string userId; public string name; public bool restored; }
    [Serializable] private class CreateRoomResponse { public string code; }
    [Serializable] private class LinkCodeResponse { public string code; }
    [Serializable] private class FriendStatusResponse { public string status; }
    [Serializable] private class ErrorResponse { public string error; }
    [Serializable] private class SearchResponse { public FriendInfo[] results; }
}

[Serializable]
public class FriendsResponse
{
    public FriendInfo[] friends;
    public ChallengeInfo[] challenges;
}

[Serializable]
public class FriendInfo
{
    public string userId;
    public string name;
    public string status;   // friends | incoming | outgoing (| none, in search results)
    public bool online;
}

[Serializable]
public class ChallengeInfo
{
    public string fromUserId;
    public string fromName;
    public string roomCode;
}

/// <summary>Everything the server sends, flattened. Fields a message doesn't use stay empty.</summary>
[Serializable]
public class ServerMessage
{
    public string type;
    public int requestId;
    public bool ok;
    public string error;

    public string roomCode;
    public string yourColor;
    public bool isHost;
    public string color;
    public string name;
    public string phase;
    public string reason;
    public string winner;
    public string pieceType;
    public NetPos from;
    public NetPos to;
    public NetPos position;
    public NetState state;
    public NetRotation rotation;
    public string conqueredBy;
}

[Serializable]
public class NetRotation
{
    public string type;
    public NetStep[] positionMap;
}

[Serializable]
public class NetStep
{
    public NetPos from;
    public NetPos to;
}

[Serializable]
public class NetPos
{
    public int x, y, z;
    public BoardPosition ToBoard() => new BoardPosition(x, y, z);
}

[Serializable]
public class NetState
{
    public string phase;
    public string currentTurn;
    public int turnNumber;
    public NetConfig config;
    public NetPlayer[] players;
}

[Serializable]
public class NetConfig
{
    public string boardSize;
    public int playerCount;
    public bool chaosMode;
    public int chaosInterval;
    public bool timedMode;
    public int timePerPlayerSeconds;
    public string aiDifficulty;
    public string[] aiColors;
}

[Serializable]
public class NetPlayer
{
    public string username;
    public string color;
    public bool isConnected;
}
