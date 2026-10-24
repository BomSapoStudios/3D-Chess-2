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

    /// <summary>The latest rematch news from the server (null until the game ends)</summary>
    public NetRematch Rematch { get; private set; }
    /// <summary>This player's rating change once the game is recorded (null until then, or unrated)</summary>
    public NetRatingChange MyRating { get; private set; }
    /// <summary>The server says this game is over (it can end there before this device notices)</summary>
    public bool ServerGameOver { get; private set; }

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
        if (OnlineSession.Replaying)
        {
            CatchUp();
            return;
        }
        // Messages can arrive before the game scene has finished building; replay them once it has
        while (backlog.Count > 0 && GameReady())
        {
            Apply(backlog.Dequeue());
        }
    }

    // ───────────────────────── Resuming a game after the app restarted ─────────────────────────

    private float nextReplayAt, replayStartedAt = -1f;
    private int appliedSeq;

    /// <summary>
    /// Rebuild a resumed game from the server's history, one event at a time, waiting for
    /// each change to settle (setup ending, a Chaos spin) before the next. Then hand back control.
    /// </summary>
    private void CatchUp()
    {
        if (replayStartedAt < 0f)
        {
            replayStartedAt = Time.unscaledTime;
            Show("Picking up where you left off…", 60f);
        }
        var client = OnlineClient.Instance;
        bool timedOut = Time.unscaledTime - replayStartedAt > 90f;
        if (backlog.Count == 0)
        {
            if (client == null || appliedSeq >= client.CatchUpTo || timedOut) FinishCatchUp();
            return;
        }
        if (!GameReady() || Time.unscaledTime < nextReplayAt || Busy()) return;
        ServerMessage msg = backlog.Dequeue();
        Apply(msg);
        if (msg.seq > appliedSeq) appliedSeq = msg.seq;
        nextReplayAt = Time.unscaledTime + 0.03f;
    }

    private static bool Busy()
    {
        if (ChaosRotationManager.Instance != null && ChaosRotationManager.Instance.IsChaosAnimationInProgress()) return true;
        foreach (ChessPiece piece in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
            if (piece.IsMoving) return true;
        return false;
    }

    private void FinishCatchUp()
    {
        OnlineSession.Replaying = false;
        Show("You're back in the game", 3f);
        // Robots played on this device carry on (setup or their turn)
        if (PlacementManager.Instance != null) PlacementManager.Instance.ResumeAIPlacement();
        if (TurnManager.Instance != null) TurnManager.Instance.ResumeAIIfNeeded();
        // Anything that arrived live while catching up
        while (backlog.Count > 0 && GameReady()) Apply(backlog.Dequeue());
    }

    private static bool GameReady() =>
        ChessBoard.Instance != null && PlacementManager.Instance != null && TurnManager.Instance != null &&
        GameStateManager.Instance != null && PieceTray.GetTrayForColor(PieceColor.Black) != null &&
        PlacementManager.Instance.PiecesReady; // trays filled, so placements can be taken from them

    private void Receive(ServerMessage msg)
    {
        if (msg.state != null)
        {
            moveDeadline = msg.state.moveDeadline;
            currentTurn = msg.state.currentTurn;
            if (msg.state.phase == "gameover") ServerGameOver = true;
            if (msg.state.rematch != null && msg.state.rematch.deadline > 0) Rematch = msg.state.rematch;
        }
        if (msg.type == "room:rematch" && msg.rematch != null) Rematch = msg.rematch;
        NetRatings ratings = msg.type == "game:rated" ? msg.ratings : msg.state?.ratings;
        if (ratings != null && ratings.For(OnlineSession.LocalColor) != null) MyRating = ratings.For(OnlineSession.LocalColor);
        if (msg.type == "game:over") ServerGameOver = true;
        if (GameReady() && backlog.Count == 0 && !OnlineSession.Replaying) Apply(msg);
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

            case "game:pieceUnplaced":
                // Another device took a piece back to its bench
                if (fromOpponent && ChessBoard.Instance != null)
                {
                    BoardPosition at = msg.position.ToBoard();
                    ChessPiece taken = ChessBoard.Instance.GetPieceAt(at);
                    if (taken != null && taken.pieceColor == color.Value) Remote(() => PlacementManager.Instance.ReturnToTray(taken, at));
                }
                break;

            case "game:placementsRevealed":
                // Setup was private: everyone's pieces go on the board just before play starts
                if (msg.pieces != null)
                    foreach (NetPlacement p in msg.pieces)
                    {
                        if (!Enum.TryParse(p.color, out PieceColor owner) || OnlineSession.IsLocal(owner)) continue;
                        if (!Enum.TryParse(p.pieceType, out ChessPieceType type) || p.position == null) continue;
                        BoardPosition at = p.position.ToBoard();
                        if (ChessBoard.Instance != null && ChessBoard.Instance.GetPieceAt(at) != null) continue; // already shown
                        Remote(() => PlaceFromTray(owner, type, at));
                    }
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

            case "game:drawOffered":
                // The other player offers a draw: the Accept / Decline buttons appear here
                if (Enum.TryParse(msg.fromColor, out PieceColor offerer) && !OnlineSession.IsLocal(offerer) && GameEndDetectionManager.Instance != null)
                {
                    Remote(() => GameEndDetectionManager.Instance.HandleDrawOffer(offerer));
                    Show($"{offerer} offers a draw. Open the menu to accept or decline.", 8f);
                }
                break;

            case "game:drawAccepted":
            case "game:drawDeclined":
                if (fromOpponent && GameEndDetectionManager.Instance != null)
                {
                    bool accepted = msg.type == "game:drawAccepted";
                    Remote(() => GameEndDetectionManager.Instance.HandleDrawResponse(color.Value, accepted));
                    if (!accepted) Show($"{color.Value} declined the draw.", 4f);
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
