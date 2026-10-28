using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Undo for games on this phone (vs the computer, pass-and-play, zombie runs). At the start of
/// every turn a person plays here, the whole position is saved: each piece's type, colour and
/// square, whose turn it is, and the zombie score. Undo puts the board back to the start of the
/// previous such turn, so against the computer it takes back your move and its reply.
///
/// Not offered online (the other player's moves are theirs), with the chess clock running, or
/// across a Chaos Mode spin or a player being knocked out (those reshape the game itself).
/// </summary>
public class UndoManager : MonoBehaviour
{
    public static UndoManager Instance { get; private set; }
    private const int MaxSaved = 30;

    private struct SavedPiece { public ChessPieceType type; public PieceColor color; public BoardPosition at; }

    private class Snapshot
    {
        public List<SavedPiece> pieces = new List<SavedPiece>();
        public PieceColor turn;
        public int zombieRounds, zombiePawns, zombiePromoted;
        public int blitzMoves;
    }

    private readonly List<Snapshot> history = new List<Snapshot>();
    private int turnsSeen, savedAtTurn = -1, activeCount = -1;
    // This scene's game has reached its setup. The game state outlives scenes, so a puzzle opened
    // from another one mid-play starts out "Playing" before its pieces exist; saving then kept an
    // empty board, and Undo after the first move cleared the queen and every pawn
    private bool setUp;
    private ChaosRotationManager subscribedChaos;
    private TurnManager subscribedTurns;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded += (scene, mode) => TryCreate();
        TryCreate();
    }

    private static void TryCreate()
    {
        if (Instance != null || FindFirstObjectByType<MainMenuController>() != null) return;
        Instance = new GameObject("Undo").AddComponent<UndoManager>();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Undo makes sense in this game at all</summary>
    private static bool Allowed()
    {
        if (OnlineSession.IsActive) return false;
        if (TimerManager.Instance != null && TimerManager.Instance.IsTimedPlayActive()) return false;
        return GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.Playing;
    }

    private static bool PersonToMove()
    {
        if (TurnManager.Instance == null) return false;
        return !TurnManager.Instance.IsPlayerAI(TurnManager.Instance.GetCurrentPlayer());
    }

    /// <summary>This turn's start is already saved (the save waits for moves to settle)</summary>
    private bool CurrentSaved => savedAtTurn == TurnKey;

    /// <summary>Which turn this is. Queen Blitz keeps the turn with the player, so each queen move counts as one.</summary>
    private int TurnKey => QueenBlitz.Active ? QueenBlitz.Moves : turnsSeen;

    public bool CanUndo => Allowed() && PersonToMove() && history.Count >= (CurrentSaved ? 2 : 1) && GameStateManager.Instance.CanMovePieces()
                           && (InputManager.Instance == null || !InputManager.Instance.IsBusy);

    private void Update()
    {
        if (TurnManager.Instance != null && subscribedTurns != TurnManager.Instance)
        {
            subscribedTurns = TurnManager.Instance;
            subscribedTurns.OnTurnChanged += (a, b) => { if (a != b) turnsSeen++; };
        }
        if (ChaosRotationManager.Instance != null && subscribedChaos != ChaosRotationManager.Instance)
        {
            subscribedChaos = ChaosRotationManager.Instance;
            subscribedChaos.OnChaosRotationCompleted += t => history.Clear(); // the cube was reshaped: no going back past it
        }
        if (GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.PiecePlacement)
        {
            // Setup (this game's, or a rematch's in the same scene): a fresh start
            setUp = true;
            if (history.Count > 0 || savedAtTurn >= 0) { history.Clear(); savedAtTurn = -1; }
        }
        if (!Allowed() || !setUp) return;

        // Someone knocked out: no going back past that either
        int active = PlayerManager.Instance != null ? PlayerManager.Instance.GetActivePlayerCount() : 2;
        if (activeCount >= 0 && active != activeCount) history.Clear();
        activeCount = active;

        // Save once at the start of each turn a person plays, after the last move has settled
        if (savedAtTurn != TurnKey && PersonToMove() && GameStateManager.Instance.CanMovePieces() && HasOwnPieces() && ZombiesRisen())
        {
            savedAtTurn = TurnKey;
            history.Add(Capture());
            if (history.Count > MaxSaved) history.RemoveAt(0);
            Debug.Log($"Undo: saved the start of turn {TurnKey} ({history.Count} saved)");
        }
    }

    /// <summary>The player to move has pieces on the board (an empty position is never a real one to go back to)</summary>
    private static bool HasOwnPieces()
    {
        PieceColor me = TurnManager.Instance.GetCurrentPlayer();
        foreach (ChessPiece p in BoardPieces()) if (p.pieceColor == me) return true;
        return false;
    }

    /// <summary>
    /// A zombie run's first zombie is on the board. It rises in the director's Update on the frame
    /// play starts; saving before it kept a board without it, and Undo after the first move removed it
    /// </summary>
    private static bool ZombiesRisen() => !ZombieMode.Active || (ZombieDirector.Instance != null && ZombieDirector.Instance.Started);

    private static Snapshot Capture()
    {
        var snap = new Snapshot
        {
            turn = TurnManager.Instance.GetCurrentPlayer(),
            zombieRounds = ZombieMode.Rounds, zombiePawns = ZombieMode.PawnsDestroyed, zombiePromoted = ZombieMode.PromotedDestroyed,
            blitzMoves = QueenBlitz.Moves,
        };
        foreach (ChessPiece p in BoardPieces())
            snap.pieces.Add(new SavedPiece { type = p.pieceType, color = p.pieceColor, at = p.CurrentPosition });
        return snap;
    }

    /// <summary>Back to the start of the previous turn you played</summary>
    public void Undo()
    {
        if (!CanUndo) return;
        Debug.Log($"Undo: undoing at turn {turnsSeen}, saved {savedAtTurn}, {history.Count} saved");
        if (CurrentSaved) history.RemoveAt(history.Count - 1); // this turn, as it stands now
        Snapshot target = history[history.Count - 1];          // the one before
        Debug.Log($"Undo: restoring {target.pieces.Count} pieces, {target.turn} to move");
        Restore(target);
        savedAtTurn = TurnKey; // the restored position is already saved
        Haptics.Selection();
        Sfx.Play("undo");
    }

    private static void Restore(Snapshot snap)
    {
        if (InputManager.Instance != null) InputManager.Instance.ClearSelection();

        var pieces = new List<(ChessPieceType, PieceColor, BoardPosition)>();
        foreach (SavedPiece s in snap.pieces) pieces.Add((s.type, s.color, s.at));
        SetBoard(pieces);

        ZombieMode.Rounds = snap.zombieRounds;
        ZombieMode.PawnsDestroyed = snap.zombiePawns;
        ZombieMode.PromotedDestroyed = snap.zombiePromoted;
        if (ZombieDirector.Instance != null) ZombieDirector.Instance.Recount();
        if (QueenBlitz.Active)
        {
            QueenBlitz.Moves = snap.blitzMoves;
            int pawns = 0;
            foreach (SavedPiece s in snap.pieces) if (s.color == QueenBlitz.PawnColor) pawns++;
            QueenBlitz.Swept = QueenBlitz.Current.pawns.Length - pawns; // the pawns swept up to that point
        }

        if (GameEndDetectionManager.Instance != null) GameEndDetectionManager.Instance.ClearDrawOffer();
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
        TurnManager.Instance.SetCurrentPlayer(snap.turn);
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
    }

    /// <summary>Clear the board and put exactly these pieces on it</summary>
    public static void SetBoard(IEnumerable<(ChessPieceType type, PieceColor color, BoardPosition at)> pieces)
    {
        foreach (ChessPiece p in new List<ChessPiece>(BoardPieces()))
        {
            ChessBoard.Instance.RemovePieceFromBoard(p, p.CurrentPosition);
            Destroy(p.gameObject);
        }
        foreach (var s in pieces) Create(s.type, s.color, s.at);
    }

    public static ChessPiece Create(ChessPieceType type, PieceColor color, BoardPosition at)
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null) return null;
        GameObject prefab = type switch
        {
            ChessPieceType.King => gm.kingPiecePrefab,
            ChessPieceType.Queen => gm.queenPiecePrefab,
            ChessPieceType.Rook => gm.rookPiecePrefab,
            ChessPieceType.Bishop => gm.bishopPiecePrefab,
            ChessPieceType.Knight => gm.knightPiecePrefab,
            _ => gm.pawnPiecePrefab,
        };
        if (prefab == null) return null;
        GameObject go = Instantiate(prefab);
        go.name = $"{color} {type}";
        GameObject container = GameObject.Find("Pieces Container");
        if (container != null) go.transform.SetParent(container.transform);
        ChessPiece piece = go.GetComponent<ChessPiece>();
        if (piece == null) { Destroy(go); return null; }
        piece.pieceType = type;
        piece.pieceColor = color;
        piece.Initialize(at, color);
        piece.ApplyMaterial();
        ChessBoard.Instance.SetPieceAt(at, piece);
        go.SetActive(true);
        return piece;
    }

    public static IEnumerable<ChessPiece> BoardPieces()
    {
        Vector3Int d = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) yield return p;
        }
    }

    // The Undo button, under Turn Planner
    private void OnGUI()
    {
        if (TurnPlannerUI.IsOpen || RematchUI.Showing || !Allowed() || history.Count < (CurrentSaved ? 2 : 1)) return;
        TouchGUI.Begin();
        GUI.enabled = CanUndo;
        if (TouchGUI.NeonButton(new Rect(16, 128, 150, 44), "Undo", NeonTheme.Cyan, NeonTheme.Pink, fontSize: 16)) Undo();
        GUI.enabled = true;
        TouchGUI.End();
    }
}
