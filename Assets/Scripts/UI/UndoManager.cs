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
    }

    private readonly List<Snapshot> history = new List<Snapshot>();
    private int turnsSeen, savedAtTurn = -1, activeCount = -1;
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
    private bool CurrentSaved => savedAtTurn == turnsSeen;

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
        if (!Allowed()) return;

        // Someone knocked out: no going back past that either
        int active = PlayerManager.Instance != null ? PlayerManager.Instance.GetActivePlayerCount() : 2;
        if (activeCount >= 0 && active != activeCount) history.Clear();
        activeCount = active;

        // Save once at the start of each turn a person plays, after the last move has settled
        if (savedAtTurn != turnsSeen && PersonToMove() && GameStateManager.Instance.CanMovePieces())
        {
            savedAtTurn = turnsSeen;
            history.Add(Capture());
            if (history.Count > MaxSaved) history.RemoveAt(0);
            Debug.Log($"Undo: saved the start of turn {turnsSeen} ({history.Count} saved)");
        }
    }

    private static Snapshot Capture()
    {
        var snap = new Snapshot
        {
            turn = TurnManager.Instance.GetCurrentPlayer(),
            zombieRounds = ZombieMode.Rounds, zombiePawns = ZombieMode.PawnsDestroyed, zombiePromoted = ZombieMode.PromotedDestroyed,
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
        savedAtTurn = turnsSeen; // the restored position is already saved
        Haptics.Selection();
    }

    private static void Restore(Snapshot snap)
    {
        var board = ChessBoard.Instance;
        if (InputManager.Instance != null) InputManager.Instance.ClearSelection();

        // Clear the board
        foreach (ChessPiece p in new List<ChessPiece>(BoardPieces()))
        {
            board.RemovePieceFromBoard(p, p.CurrentPosition);
            Destroy(p.gameObject);
        }
        // Put every saved piece back
        foreach (SavedPiece s in snap.pieces) Create(s.type, s.color, s.at);

        ZombieMode.Rounds = snap.zombieRounds;
        ZombieMode.PawnsDestroyed = snap.zombiePawns;
        ZombieMode.PromotedDestroyed = snap.zombiePromoted;
        if (ZombieDirector.Instance != null) ZombieDirector.Instance.Recount();

        if (GameEndDetectionManager.Instance != null) GameEndDetectionManager.Instance.ClearDrawOffer();
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
        TurnManager.Instance.SetCurrentPlayer(snap.turn);
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
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

    private static IEnumerable<ChessPiece> BoardPieces()
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
