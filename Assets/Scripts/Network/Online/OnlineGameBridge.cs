using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Plays the other player's actions from the server into the local game during an
/// online match: their placements, ready, moves, promotions and resignation. Also
/// shows connection status and reconnects to the room if the connection drops.
/// Local actions are sent by hooks in PlacementManager, ChessBoard and friends.
/// </summary>
public class OnlineGameBridge : MonoBehaviour
{
    public static OnlineGameBridge Instance { get; private set; }

    private readonly Queue<ServerMessage> backlog = new Queue<ServerMessage>();
    private string banner;
    private float bannerUntil;
    private bool reconnecting;
    private bool gameOverShown;
    private bool roomClosed;
    private long moveDeadline;      // Unix ms the player on move must move by (Take your time); 0 if none
    private string currentTurn;

    public static void Begin()
    {
        if (Instance != null) Destroy(Instance.gameObject);
        var go = new GameObject("Online Game Bridge");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<OnlineGameBridge>();
    }

    private void OnEnable()
    {
        var client = OnlineClient.Instance;
        if (client == null) return;
        client.OnMessage += Receive;
        client.OnClosed += Lost;
    }

    private void OnDisable()
    {
        var client = OnlineClient.Instance;
        if (client == null) return;
        client.OnMessage -= Receive;
        client.OnClosed -= Lost;
    }

    private void Update()
    {
        if (!OnlineSession.IsActive)
        {
            Destroy(gameObject);
            return;
        }
        // Messages can arrive before the game scene has finished building; replay them once it has
        while (backlog.Count > 0 && GameReady())
        {
            Apply(backlog.Dequeue());
        }
    }

    private static bool GameReady() =>
        ChessBoard.Instance != null && PlacementManager.Instance != null && TurnManager.Instance != null &&
        GameStateManager.Instance != null && PieceTray.GetTrayForColor(PieceColor.Black) != null;

    private void Receive(ServerMessage msg)
    {
        if (msg.state != null)
        {
            moveDeadline = msg.state.moveDeadline;
            currentTurn = msg.state.currentTurn;
        }
        if (GameReady() && backlog.Count == 0) Apply(msg);
        else backlog.Enqueue(msg);
    }

    // ───────────────────────── Applying the other player's actions ─────────────────────────

    private void Apply(ServerMessage msg)
    {
        PieceColor? color = Enum.TryParse(msg.color, out PieceColor c) ? c : (PieceColor?)null;
        bool fromOpponent = color.HasValue && !OnlineSession.IsLocal(color.Value); // another device's side

        switch (msg.type)
        {
            case "game:piecePlaced":
                if (fromOpponent && Enum.TryParse(msg.pieceType, out ChessPieceType placedType))
                    Remote(() => PlaceFromTray(color.Value, placedType, msg.position.ToBoard()));
                break;

            case "game:playerReady":
                if (fromOpponent) Remote(() => PlacementManager.Instance.SetPlayerReady(color.Value, true));
                break;

            case "game:moveValidated":
                if (fromOpponent) Remote(() => ChessBoard.Instance.MovePiece(msg.from.ToBoard(), msg.to.ToBoard()));
                break;

            case "game:pawnPromoted":
                if (fromOpponent && Enum.TryParse(msg.pieceType, out ChessPieceType promoted) && PawnPromotionManager.Instance != null)
                    Remote(() => PawnPromotionManager.Instance.ApplyRemotePromotion(promoted));
                break;

            case "game:phaseChanged":
                // The server starts play once everyone is ready, even if a player left pieces on their bench
                if (msg.phase == "playing" && GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces())
                    Remote(() => PlacementManager.Instance.CompletePlacementPhase());
                break;

            case "game:chaosRotation":
                // The server decides every Chaos Mode spin so all devices turn the same slice
                if (msg.rotation?.positionMap != null && ChaosRotationManager.Instance != null)
                {
                    var map = new Dictionary<BoardPosition, BoardPosition>();
                    foreach (var step in msg.rotation.positionMap) map[step.from.ToBoard()] = step.to.ToBoard();
                    ChaosRotationManager.Instance.ApplyRemoteRotation(map);
                }
                break;

            case "game:playerEliminated":
                // Checkmates are found on every device; this covers resignations, timeouts and walk-outs
                if (color.HasValue && GameEndDetectionManager.Instance != null)
                {
                    PieceColor? by = Enum.TryParse(msg.conqueredBy, out PieceColor winner) ? winner : (PieceColor?)null;
                    Remote(() => GameEndDetectionManager.Instance.ApplyRemoteElimination(color.Value, by));
                }
                break;

            case "game:moveRejected":
                // The server's rules disagreed with this device's; say so plainly
                Show($"The server didn't accept that move ({msg.error}).", 6f);
                break;

            case "game:over":
                HandleGameOver(msg);
                break;

            case "room:playerLeft":
                if (!fromOpponent) break;
                if (msg.graceMs > 0)
                    Show($"{Who(msg)} disconnected. They have {Mathf.RoundToInt(msg.graceMs / 60000f)} minutes to come back.", 120f);
                else
                    Show($"{Who(msg)} stepped away. The game waits for them.", 5f);
                break;

            case "room:playerReconnected":
                if (fromOpponent) Show($"{Who(msg)} is back!", 3f);
                break;

            case "game:timeout":
                if (color.HasValue) Show(color.Value == OnlineSession.LocalColor ? "You ran out of time." : $"{color.Value} ran out of time.", 6f);
                break;

            case "game:abandoned":
                if (fromOpponent) Show($"{color.Value} didn't come back and is out of the game.", 6f);
                break;

            case "room:closed":
                roomClosed = true;
                break;

            case "room:joined":
                if (reconnecting)
                {
                    reconnecting = false;
                    Show("Reconnected", 2f);
                }
                break;
        }
    }

    private static string Who(ServerMessage msg) => !string.IsNullOrEmpty(msg.name) ? msg.name : msg.color ?? "Your opponent";

    // iOS suspends the app in the background and its connection with it; catch up on return
    private void OnApplicationPause(bool paused)
    {
        if (paused || !OnlineSession.IsActive || gameOverShown || roomClosed) return;
        var client = OnlineClient.Instance;
        if (client != null && !client.IsConnected) Resync();
    }

    private static void Remote(Action action)
    {
        OnlineSession.ApplyingRemote = true;
        try { action(); }
        catch (Exception e) { Debug.LogError($"OnlineGameBridge: applying remote action failed: {e}"); }
        finally { OnlineSession.ApplyingRemote = false; }
    }

    private static void PlaceFromTray(PieceColor color, ChessPieceType type, BoardPosition position)
    {
        PieceTray tray = PieceTray.GetTrayForColor(color);
        ChessPiece piece = tray != null
            ? tray.GetComponentsInChildren<ChessPiece>(true).FirstOrDefault(p => p.pieceType == type)
            : null;
        if (piece == null)
        {
            Debug.LogError($"OnlineGameBridge: no {color} {type} left in the tray to place at {position}");
            return;
        }
        PlacementManager.Instance.ExecuteAIPlacement(piece, position);
    }

    private void HandleGameOver(ServerMessage msg)
    {
        if (gameOverShown) return;
        gameOverShown = true;
        // Checkmate and stalemate are detected by the local game too. Resignation,
        // abandonment and timeouts only happen on the other device, so apply them here.
        bool weWon = msg.winner == OnlineSession.LocalColor.ToString();
        if (weWon && (msg.reason == "resignation" || msg.reason == "timeout"))
        {
            PieceColor loser = OnlineSession.LocalColor == PieceColor.White ? PieceColor.Black : PieceColor.White;
            if (GameEndDetectionManager.Instance != null && GameStateManager.Instance.currentState != GameState.GameOver)
                Remote(() => GameEndDetectionManager.Instance.HandleForfeit(loser));
        }
    }

    // ───────────────────────── Connection ─────────────────────────

    private void Lost(string reason)
    {
        if (!OnlineSession.IsActive) return;
        // A finished or closed room is meant to close: don't keep knocking on it
        if (gameOverShown || roomClosed || (GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.GameOver)) return;
        Show("Connection lost. Reconnecting…", 60f);
        Resync();
    }

    private void Resync()
    {
        if (reconnecting) return;
        reconnecting = true;
        StartCoroutine(ReconnectLoop());
    }

    private IEnumerator ReconnectLoop()
    {
        var client = OnlineClient.Instance;
        for (int attempt = 0; attempt < 20 && reconnecting && OnlineSession.IsActive; attempt++)
        {
            client.JoinRoom(client.RoomCode);
            yield return new WaitForSeconds(3f);
            if (client.IsConnected) yield break;
        }
        if (reconnecting) Show("Couldn't reconnect. Check your internet and return to the menu.", 30f);
    }

    private void Show(string text, float seconds)
    {
        banner = text;
        bannerUntil = Time.time + seconds;
    }

    private void OnGUI()
    {
        Vector2 ui = TouchGUI.Begin();
        GUI.depth = -5;

        // Who you are, so it's clear which side to play
        string you = $"Online · You are {OnlineSession.LocalColor}";
        string clock = null;
        if (moveDeadline > 0 && currentTurn == OnlineSession.LocalColor.ToString())
        {
            // Take your time games: how long is left to make this move
            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(moveDeadline) - DateTimeOffset.UtcNow;
            if (left.TotalSeconds > 0) clock = left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes}m left to move" : $"{Math.Max(1, left.Minutes)}m left to move";
        }
        if (!TurnPlannerUI.IsOpen) {
            GUI.Label(new Rect(20, ui.y - 34, 300, 24), you, NeonTheme.GUILabelStyle(NeonTheme.Cyan, 14));
            if (clock != null) GUI.Label(new Rect(20, ui.y - 56, 300, 24), clock, NeonTheme.GUILabelStyle(NeonTheme.Yellow, 14));
        }

        if (!string.IsNullOrEmpty(banner) && Time.time < bannerUntil)
        {
            Rect rect = new Rect(ui.x / 2f - 220f, 60f, 440f, 44f);
            NeonTheme.GUIPanel(rect, NeonTheme.Yellow, 0.95f);
            GUI.Label(rect, banner, NeonTheme.GUILabelStyle(NeonTheme.Yellow, 14, true, TextAnchor.MiddleCenter));
        }
        TouchGUI.End();
    }
}
