using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Zombie Survival: whether a run is on, its settings, and its score</summary>
public static class ZombieMode
{
    /// <summary>The current game is a zombie run</summary>
    public static bool Active { get; set; }
    /// <summary>Zombies play Black; the player is White</summary>
    public const PieceColor ZombieColor = PieceColor.Black;
    public const PieceColor PlayerColor = PieceColor.White;
    /// <summary>Set while the zombies move, so each move doesn't hand the turn back</summary>
    public static bool HoldTurn { get; set; }

    public static AIDifficulty Difficulty { get; set; } = AIDifficulty.Medium;
    public static int Rounds { get; set; }
    public static int PawnsDestroyed { get; set; }
    public static int PromotedDestroyed { get; set; }
    public static int Score => Rounds * 10 + PawnsDestroyed * 5 + PromotedDestroyed * 20;

    /// <summary>The round from which two zombies rise each round instead of one</summary>
    public static int StepUpRound => Difficulty == AIDifficulty.Easy ? 25 : Difficulty == AIDifficulty.Hard ? 10 : 15;
    /// <summary>What a zombie becomes on reaching the player's side</summary>
    public static ChessPieceType PromotesTo => Difficulty == AIDifficulty.Easy ? ChessPieceType.Knight : ChessPieceType.Queen;
    /// <summary>Galaxy: pieces bitten by a zombie become zombies</summary>
    public static bool BittenTurn => Difficulty == AIDifficulty.Hard;

    public static string DifficultyName(AIDifficulty d) => d == AIDifficulty.Easy ? "Pea" : d == AIDifficulty.Hard ? "Galaxy" : "Walnut";

    public static int Best(AIDifficulty d) => PlayerPrefs.GetInt($"ZombieBest.{d}", 0);

    /// <summary>Save the score if it beats the best; true when it does</summary>
    public static bool SaveBest()
    {
        if (Score <= Best(Difficulty)) return false;
        PlayerPrefs.SetInt($"ZombieBest.{Difficulty}", Score);
        PlayerPrefs.Save();
        return true;
    }

    public static void Reset()
    {
        Rounds = 0;
        PawnsDestroyed = 0;
        PromotedDestroyed = 0;
        HoldTurn = false;
    }
}

/// <summary>
/// Runs the zombie side of a Zombie Survival game. After each of the player's moves:
///  1. zombies that reached the player's side (now real pieces) hunt: they take the player's
///     most valuable piece in reach, or close in on the nearest one;
///  2. every zombie pawn steps one cell toward the player, or bites a piece on one of the
///     8 cells diagonally in front of it (front-most zombies first; blocked ones wait);
///     one reaching the player's side becomes a Knight (Pea) or a Queen;
///  3. new zombies rise on random empty cells of the far face: one a round, two once the
///     difficulty's step-up round arrives.
/// On Galaxy a bitten piece rises as a zombie where its attacker stood. There is no check;
/// the run ends when the player has no pieces left.
/// </summary>
public class ZombieDirector : MonoBehaviour
{
    public static ZombieDirector Instance { get; private set; }

    private const int StartingZombies = 1;
    private const float StepDelay = 0.08f; // between zombie moves, so the horde moves as a wave

    private bool started, busy, ended;
    private int pawnsAfterTurn, promotedAfterTurn; // zombie counts when they last finished moving

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded += (scene, mode) => TryCreate();
        TryCreate();
    }

    private static void TryCreate()
    {
        GameConfiguration config = SceneController.Instance != null ? SceneController.Instance.GetCurrentGameConfiguration() : null;
        bool inGame = Object.FindFirstObjectByType<MainMenuController>() == null;
        ZombieMode.Active = inGame && config != null && config.zombieMode && !config.isOnlineGame;
        ZombieMode.HoldTurn = false;
        if (!ZombieMode.Active || Instance != null) return;
        ZombieMode.Difficulty = config.aiDifficulty;
        ZombieMode.Reset();
        new GameObject("Zombie Director").AddComponent<ZombieDirector>();
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ZombieMode.HoldTurn = false;
    }

    private void Update()
    {
        var state = GameStateManager.Instance != null ? GameStateManager.Instance.currentState : GameState.WaitingForConfiguration;
        // The zombies have nothing to place: they're ready straight away
        if (state == GameState.PiecePlacement && PlacementManager.Instance != null && !PlacementManager.Instance.IsPlayerReady(ZombieMode.ZombieColor))
            PlacementManager.Instance.SetPlayerReady(ZombieMode.ZombieColor, true);

        if (state == GameState.Playing && !started)
        {
            started = true;
            for (int i = 0; i < StartingZombies; i++) SpawnZombie();
            CountZombies(out pawnsAfterTurn, out promotedAfterTurn);
        }
    }

    /// <summary>Recount the horde after the board was put back (Undo)</summary>
    public void Recount() => CountZombies(out pawnsAfterTurn, out promotedAfterTurn);

    /// <summary>The zombies' turn (called in place of the robot's move)</summary>
    public void TakeTurn()
    {
        if (busy || ended) return;
        StartCoroutine(TurnRoutine());
    }

    private IEnumerator TurnRoutine()
    {
        busy = true;
        yield return WaitForAnimations();
        if (!Playing()) { busy = false; yield break; }

        // What the player's move destroyed
        CountZombies(out int pawnsNow, out int promotedNow);
        ZombieMode.PawnsDestroyed += Mathf.Max(0, pawnsAfterTurn - pawnsNow);
        ZombieMode.PromotedDestroyed += Mathf.Max(0, promotedAfterTurn - promotedNow);
        ZombieMode.Rounds++;

        ZombieMode.HoldTurn = true;
        try
        {
            // 1. Promoted zombies hunt
            foreach (ChessPiece hunter in Zombies().Where(p => p.pieceType != ChessPieceType.Pawn).ToList())
            {
                if (hunter == null || !OnBoard(hunter)) continue;
                if (TryHunt(hunter)) yield return new WaitForSeconds(StepDelay);
                if (!Playing()) yield break;
            }

            // 2. Pawns march, front-most first so the ones behind can follow
            foreach (ChessPiece zombie in Zombies().Where(p => p.pieceType == ChessPieceType.Pawn).OrderBy(p => p.CurrentPosition.x).ToList())
            {
                if (zombie == null || !OnBoard(zombie)) continue;
                if (TryMarch(zombie)) yield return new WaitForSeconds(StepDelay);
                if (!Playing()) yield break;
            }

            // 3. New zombies rise
            int rising = ZombieMode.Rounds >= ZombieMode.StepUpRound ? 2 : 1;
            for (int i = 0; i < rising; i++) SpawnZombie();
        }
        finally
        {
            ZombieMode.HoldTurn = false;
        }

        CountZombies(out pawnsAfterTurn, out promotedAfterTurn);
        yield return WaitForAnimations();
        busy = false;

        if (!PlayerPieces().Any())
        {
            EndRun();
            yield break;
        }
        if (Playing() && TurnManager.Instance != null && TurnManager.Instance.GetCurrentPlayer() == ZombieMode.ZombieColor)
            TurnManager.Instance.NextTurn();
    }

    // ───────────────────────── Zombie moves ─────────────────────────

    /// <summary>A promoted zombie takes the most valuable piece it can reach, or moves toward the nearest one</summary>
    private bool TryHunt(ChessPiece hunter)
    {
        List<BoardPosition> moves = hunter.GetValidMoves();
        if (moves.Count == 0) return false;
        var board = ChessBoard.Instance;
        BoardPosition? best = null;
        float bestValue = 0f;
        foreach (BoardPosition to in moves)
        {
            ChessPiece target = board.GetPieceAt(to);
            if (target != null && target.pieceColor == ZombieMode.PlayerColor && RobotSearch.Value(target.pieceType) > bestValue)
            {
                best = to;
                bestValue = RobotSearch.Value(target.pieceType);
            }
        }
        if (best == null)
        {
            List<ChessPiece> prey = PlayerPieces().ToList();
            if (prey.Count == 0) return false;
            float bestDistance = float.MaxValue;
            foreach (BoardPosition to in moves)
            {
                if (board.GetPieceAt(to) != null) continue;
                float d = prey.Min(p => Distance(to, p.CurrentPosition));
                if (d < bestDistance) { bestDistance = d; best = to; }
            }
        }
        return best != null && Move(hunter, best.Value);
    }

    /// <summary>A zombie pawn bites the most valuable piece on its diagonals, or steps forward</summary>
    private bool TryMarch(ChessPiece zombie)
    {
        var board = ChessBoard.Instance;
        BoardPosition? best = null;
        float bestValue = -1f;
        foreach (BoardPosition to in zombie.GetValidMoves())
        {
            ChessPiece target = board.GetPieceAt(to);
            float value = target == null ? 0f : target.pieceColor == ZombieMode.PlayerColor ? RobotSearch.Value(target.pieceType) : -1f;
            if (value > bestValue) { bestValue = value; best = to; }
        }
        if (best == null || bestValue < 0f) return false; // blocked: wait

        Pawn pawn = zombie as Pawn;
        if (pawn != null && pawn.IsPromotionMove(best.Value)) return Promote(pawn, best.Value);
        return Move(zombie, best.Value);
    }

    private bool Move(ChessPiece piece, BoardPosition to)
    {
        var board = ChessBoard.Instance;
        BoardPosition from = piece.CurrentPosition;
        ChessPiece victim = board.GetPieceAt(to);
        bool bite = victim != null && victim.pieceColor == ZombieMode.PlayerColor;
        ChessPieceType? bitten = bite ? victim.pieceType : (ChessPieceType?)null;
        if (!board.MovePiece(from, to)) return false;
        // Galaxy: the bitten piece rises again as a zombie, where its attacker stood
        if (bitten.HasValue && ZombieMode.BittenTurn && board.GetPieceAt(from) == null) Create(ChessPieceType.Pawn, from);
        return true;
    }

    /// <summary>A zombie reached the player's side: it becomes a real piece (Knight on Pea, Queen otherwise)</summary>
    private bool Promote(Pawn pawn, BoardPosition to)
    {
        var board = ChessBoard.Instance;
        BoardPosition from = pawn.CurrentPosition;
        ChessPiece victim = board.GetPieceAt(to);
        if (victim != null)
        {
            if (victim.pieceColor != ZombieMode.PlayerColor) return false;
            PieceEffects.Shatter(victim, pawn);
            board.RemovePieceFromBoard(victim, to);
        }
        board.SetPieceAt(from, null);
        Destroy(pawn.gameObject);
        Create(ZombieMode.PromotesTo, to);
        if (victim != null && ZombieMode.BittenTurn) Create(ChessPieceType.Pawn, from);
        return true;
    }

    // ───────────────────────── Spawning ─────────────────────────

    /// <summary>A new zombie on a random empty cell of the far face</summary>
    private void SpawnZombie()
    {
        Vector3Int d = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(8, 8, 8);
        var empty = new List<BoardPosition>();
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            var p = new BoardPosition(d.x - 1, y, z);
            if (ChessBoard.Instance.GetPieceAt(p) == null) empty.Add(p);
        }
        if (empty.Count == 0) return;
        Create(ChessPieceType.Pawn, empty[Random.Range(0, empty.Count)]);
    }

    private static void Create(ChessPieceType type, BoardPosition at)
    {
        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm == null) return;
        GameObject prefab = type == ChessPieceType.Pawn ? gm.pawnPiecePrefab : type == ChessPieceType.Knight ? gm.knightPiecePrefab : gm.queenPiecePrefab;
        if (prefab == null) return;
        GameObject go = Instantiate(prefab);
        go.name = $"Zombie {type}";
        GameObject container = GameObject.Find("Pieces Container");
        if (container != null) go.transform.SetParent(container.transform);
        ChessPiece piece = go.GetComponent<ChessPiece>();
        if (piece == null) { Destroy(go); return; }
        piece.pieceType = type;
        piece.Initialize(at, ZombieMode.ZombieColor);
        ChessBoard.Instance.SetPieceAt(at, piece);
        go.SetActive(true);
    }

    // ───────────────────────── The end ─────────────────────────

    private void EndRun()
    {
        if (ended) return;
        ended = true;
        bool best = ZombieMode.SaveBest();
        string reason = $"Overrun after {ZombieMode.Rounds} round{(ZombieMode.Rounds == 1 ? "" : "s")}. Score {ZombieMode.Score}" + (best ? " (new best!)" : $" (best {ZombieMode.Best(ZombieMode.Difficulty)})");
        if (GameEndDetectionManager.Instance != null) GameEndDetectionManager.Instance.EndGame(ZombieMode.ZombieColor, reason);
        else if (GameStateManager.Instance != null) GameStateManager.Instance.ChangeState(GameState.GameOver);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static bool Playing() => GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.Playing;

    private static IEnumerator WaitForAnimations()
    {
        float until = Time.time + 5f;
        while (Time.time < until && GameStateManager.Instance != null && !GameStateManager.Instance.CanMovePieces() && Playing()) yield return null;
    }

    private static IEnumerable<ChessPiece> AllPieces()
    {
        Vector3Int d = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(8, 8, 8);
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) yield return p;
        }
    }

    private static IEnumerable<ChessPiece> Zombies() => AllPieces().Where(p => p.pieceColor == ZombieMode.ZombieColor);
    private static IEnumerable<ChessPiece> PlayerPieces() => AllPieces().Where(p => p.pieceColor == ZombieMode.PlayerColor);

    private static bool OnBoard(ChessPiece p) => ChessBoard.Instance.GetPieceAt(p.CurrentPosition) == p;

    private static void CountZombies(out int pawns, out int promoted)
    {
        pawns = 0;
        promoted = 0;
        foreach (ChessPiece p in Zombies())
        {
            if (p.pieceType == ChessPieceType.Pawn) pawns++;
            else promoted++;
        }
    }

    private static float Distance(BoardPosition a, BoardPosition b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Max(Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)));

    // Round, zombies and score along the top while the run is on
    private void OnGUI()
    {
        if (!started || ended || TurnPlannerUI.IsOpen || GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing) return;
        Vector2 ui = TouchGUI.Begin();
        CountZombies(out int pawns, out int promoted);
        // Under the turn / last move line, on its own panel so it reads clearly over the board
        string text = $"Round {ZombieMode.Rounds + 1}    ·    Zombies {pawns + promoted}    ·    Score {ZombieMode.Score}";
        Rect rect = new Rect(ui.x / 2f - 200f, 56f, 400f, 34f);
        NeonTheme.GUIPanel(rect, NeonTheme.Lime, 0.85f);
        GUI.Label(rect, text, NeonTheme.GUILabelStyle(NeonTheme.Lime, 17, true, TextAnchor.MiddleCenter));
        TouchGUI.End();
    }
}
