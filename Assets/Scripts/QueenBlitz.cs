using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Queen Blitz: a puzzle mode. The board holds pawns (and, later, blocks); the player has a
/// single Queen and clears every pawn in as few moves as possible. A move slides the queen any
/// distance along one of its 26 lines, sweeping up every pawn it passes through or stops on;
/// blocks and the board's edge stop it. Early puzzles place the queen for you; later ones make
/// you choose where she starts (placing her is not a move). On an ice floor she can't stop
/// partway: every move slides to the board's edge or the square before a block.
///
/// Pars come from the solver in Tools/QueenBlitz (puzzles.py checks them all).
/// </summary>
public static class QueenBlitz
{
    public class Puzzle
    {
        public string name;
        public int size;
        public BoardPosition? queen;     // null: the player places the queen
        public BoardPosition best;       // a starting square that reaches par (Auto-place uses it)
        public BoardPosition[] pawns;
        public BoardPosition[] blocks;
        public int par;
        public string tip;
        public bool ice;                 // ice floor: every move slides to the edge or a block
    }

    private static BoardPosition P(int x, int y, int z) => new BoardPosition(x, y, z);

    public static readonly Puzzle[] Puzzles =
    {
        new Puzzle { name = "Straight Shot", size = 4, queen = P(0, 1, 1), par = 1,
            pawns = new[] { P(1, 1, 1), P(2, 1, 1), P(3, 1, 1) }, blocks = new BoardPosition[0],
            tip = "The queen drives straight through every pawn on her line." },
        new Puzzle { name = "Corner Turn", size = 4, queen = P(0, 0, 0), par = 2,
            pawns = new[] { P(1, 0, 0), P(2, 0, 0), P(3, 0, 0), P(3, 1, 0), P(3, 2, 0), P(3, 3, 0) }, blocks = new BoardPosition[0],
            tip = "Finish one line where the next one begins." },
        new Puzzle { name = "Stop Short", size = 4, queen = P(0, 0, 0), par = 2,
            pawns = new[] { P(1, 1, 1), P(2, 2, 2), P(2, 2, 0), P(2, 2, 1) }, blocks = new BoardPosition[0],
            tip = "You don't have to ride a line to the edge." },
        new Puzzle { name = "Long Way Round", size = 4, queen = P(2, 0, 2), par = 3,
            pawns = new[] { P(0, 0, 0), P(1, 1, 1), P(2, 0, 1), P(2, 1, 1), P(2, 2, 2) }, blocks = new BoardPosition[0],
            tip = "The biggest sweep isn't always the best first move." },
        new Puzzle { name = "Your Move", size = 4, queen = null, best = P(3, 0, 0), par = 2,
            pawns = new[] { P(1, 2, 0), P(2, 2, 1), P(3, 1, 1), P(3, 2, 2) }, blocks = new BoardPosition[0],
            tip = "Place the queen yourself. Where you start matters." },
        new Puzzle { name = "Roadblocks", size = 6, queen = null, best = P(3, 0, 5), par = 3,
            pawns = new[] { P(3, 0, 4), P(3, 5, 2), P(4, 4, 1), P(5, 3, 0) },
            blocks = new[] { P(1, 1, 5), P(2, 4, 4), P(4, 1, 4), P(4, 3, 2) },
            tip = "Blocks stop the queen. Find a way around them." },
        new Puzzle { name = "Diagonal Detour", size = 6, queen = null, best = P(5, 0, 5), par = 3,
            pawns = new[] { P(0, 4, 5), P(1, 3, 4), P(2, 0, 1), P(2, 2, 3), P(3, 0, 1), P(4, 0, 4) },
            blocks = new[] { P(0, 1, 4), P(2, 2, 5), P(3, 3, 1), P(5, 5, 4) },
            tip = "Only one starting square works in three." },
        new Puzzle { name = "First Frost", size = 4, queen = P(0, 0, 0), par = 2, ice = true,
            pawns = new[] { P(1, 0, 0), P(2, 0, 0), P(2, 1, 0), P(2, 2, 0), P(2, 3, 0) },
            blocks = new[] { P(3, 0, 0) },
            tip = "Ice! The queen slides until the edge or a block stops her." },
        new Puzzle { name = "Ice Rink", size = 6, queen = null, best = P(0, 1, 4), par = 4, ice = true,
            pawns = new[] { P(1, 1, 4), P(2, 4, 4), P(2, 5, 4), P(4, 1, 4), P(4, 5, 4), P(5, 1, 4), P(5, 5, 1), P(5, 5, 2), P(5, 5, 3), P(5, 5, 4) },
            blocks = new[] { P(0, 1, 3), P(1, 0, 5), P(4, 3, 1), P(4, 3, 5), P(5, 1, 0), P(5, 2, 5), P(5, 3, 2), P(5, 5, 5) },
            tip = "Use the blocks as brakes." },
        new Puzzle { name = "Black Ice", size = 6, queen = null, best = P(4, 5, 1), par = 5, ice = true,
            pawns = new[] { P(0, 0, 5), P(2, 2, 5), P(3, 0, 2), P(3, 3, 5), P(3, 5, 5), P(4, 3, 1), P(4, 4, 1), P(4, 4, 5), P(4, 5, 4), P(4, 5, 5), P(5, 5, 3) },
            blocks = new[] { P(0, 5, 5), P(1, 4, 4), P(2, 3, 1), P(2, 5, 3), P(2, 5, 5), P(3, 2, 3), P(4, 3, 3), P(5, 3, 3) },
            tip = "Only one starting square clears it in five." },
    };

    /// <summary>The current game is a Queen Blitz puzzle</summary>
    public static bool Active { get; set; }
    public static int Index { get; set; }
    public static Puzzle Current => Puzzles[Mathf.Clamp(Index, 0, Puzzles.Length - 1)];
    public static int Moves { get; set; }
    public static bool Cleared { get; set; }
    /// <summary>Pawns the last clear paid (0 if the puzzle had already paid out)</summary>
    public static int LastPaid { get; set; }
    /// <summary>The player paid for Auto-place to put the queen on a best square</summary>
    public static bool AutoPlaced { get; set; }

    public const PieceColor PlayerColor = PieceColor.White;
    public const PieceColor PawnColor = PieceColor.Black;

    private static readonly HashSet<BoardPosition> blocked = new HashSet<BoardPosition>();

    public static void Begin(int index)
    {
        Index = index;
        Moves = 0;
        Cleared = false;
        AutoPlaced = false;
        blocked.Clear();
        foreach (BoardPosition b in Current.blocks) blocked.Add(b);
    }

    public static bool IsBlocked(BoardPosition p) => Active && blocked.Contains(p);

    /// <summary>The pawns this puzzle pays when cleared: all of them, the first time (PawnBank sets the rule)</summary>
    public static int Reward(int index) => Puzzles[index].pawns.Length;

    public static bool EverCleared(int index) => PlayerPrefs.GetInt($"QueenBlitzCleared.{index}", 0) == 1;
    public static int BestMoves(int index) => PlayerPrefs.GetInt($"QueenBlitzBest.{index}", 0);

    /// <summary>Record a clear: pay the pawns if this puzzle still pays, keep the fewest moves.
    /// Returns the pawns paid, and whether the moves are a new best.</summary>
    public static (int paid, bool best) RecordClear()
    {
        int paid = 0;
        if (!EverCleared(Index) || !PawnBank.BlitzPaysFirstClearOnly)
        {
            paid = Reward(Index);
            PawnBank.Earn(paid);
        }
        PlayerPrefs.SetInt($"QueenBlitzCleared.{Index}", 1);
        int before = BestMoves(Index);
        bool best = before == 0 || Moves < before;
        if (best) PlayerPrefs.SetInt($"QueenBlitzBest.{Index}", Moves);
        PlayerPrefs.Save();
        return (paid, best && before > 0);
    }

    /// <summary>Auto-place: the queen goes on a square that can reach par, for a pawn</summary>
    public static bool CanAutoPlace => PawnBank.Total >= PawnBank.AutoPlaceCost;

    public static void AutoPlaceQueen()
    {
        if (PlacementManager.Instance == null || Current.queen.HasValue || !CanAutoPlace) return;
        PieceTray tray = PieceTray.GetTrayForColor(PlayerColor);
        if (tray == null) return;
        foreach (ChessPiece p in tray.GetComponentsInChildren<ChessPiece>(true))
        {
            if (p.pieceType != ChessPieceType.Queen) continue;
            if (!PawnBank.Spend(PawnBank.AutoPlaceCost)) return;
            AutoPlaced = true;
            PlacementManager.Instance.ExecuteAIPlacement(p, Current.best);
            return;
        }
    }


    // ───────────────────────── Queen lines ─────────────────────────

    /// <summary>Every square the queen can stop on: along each of her 26 lines until a block or the edge, through pawns</summary>
    public static List<BoardPosition> SweepMoves(ChessPiece queen)
    {
        var moves = new List<BoardPosition>();
        if (ChessBoard.Instance == null) return moves;
        BoardPosition from = queen.CurrentPosition;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var at = from;
            BoardPosition? last = null;
            while (true)
            {
                at = new BoardPosition(at.x + dx, at.y + dy, at.z + dz);
                if (!ChessBoard.Instance.IsValidPosition(at) || blocked.Contains(at)) break;
                ChessPiece there = ChessBoard.Instance.GetPieceAt(at);
                if (there != null && there.pieceColor == queen.pieceColor) break;
                if (!Current.ice) moves.Add(at);
                last = at;
            }
            // On ice she can't stop partway: only the end of the slide
            if (Current.ice && last.HasValue) moves.Add(last.Value);
        }
        return moves;
    }

    /// <summary>The squares strictly between two squares on one of the queen's lines</summary>
    public static IEnumerable<BoardPosition> Between(BoardPosition from, BoardPosition to)
    {
        int dx = System.Math.Sign(to.x - from.x), dy = System.Math.Sign(to.y - from.y), dz = System.Math.Sign(to.z - from.z);
        var at = new BoardPosition(from.x + dx, from.y + dy, from.z + dz);
        int guard = 16;
        while (at != to && guard-- > 0)
        {
            yield return at;
            at = new BoardPosition(at.x + dx, at.y + dy, at.z + dz);
        }
    }

    /// <summary>The queen is sliding from → to: sweep up the pawns she passes through (the one she lands on is taken as usual)</summary>
    public static void SweepPath(ChessPiece queen, BoardPosition from, BoardPosition to)
    {
        foreach (BoardPosition p in Between(from, to))
        {
            ChessPiece pawn = ChessBoard.Instance.GetPieceAt(p);
            if (pawn == null || pawn.pieceColor == queen.pieceColor) continue;
            KnockAway(pawn, queen, from, to);
            ChessBoard.Instance.RemovePieceFromBoard(pawn, p);
        }
    }

    /// <summary>Knock a pawn flying the way the queen is sliding</summary>
    public static void KnockAway(ChessPiece pawn, ChessPiece queen, BoardPosition from, BoardPosition to)
    {
        Vector3 local = ChessBoard.Instance.BoardToLocalPosition(to) - ChessBoard.Instance.BoardToLocalPosition(from);
        Transform parent = queen.transform.parent;
        PieceEffects.KnockAway(pawn, queen, parent != null ? parent.TransformDirection(local) : local, Current.ice);
    }

    /// <summary>A queen move finished: count it and see whether the board is clear</summary>
    public static void OnQueenMoved()
    {
        Moves++;
        if (QueenBlitzDirector.Instance != null) QueenBlitzDirector.Instance.AfterMove();
    }
}

/// <summary>A block in a Queen Blitz puzzle: the queen can't enter or pass its square</summary>
public class QueenBlitzBlock : MonoBehaviour
{
    public BoardPosition at;
}

/// <summary>
/// Sets up and referees a Queen Blitz puzzle in the game scene: lays out the pawns and blocks,
/// places the queen when the puzzle gives her a square, keeps the turn with the player, counts
/// moves, and ends the puzzle when the last pawn falls.
/// </summary>
public class QueenBlitzDirector : MonoBehaviour
{
    public static QueenBlitzDirector Instance { get; private set; }

    private bool laidOut, queenPlaced, ended;

    // The pawn tracker: one silhouette per pawn, filled in (with a little bump) as the queen takes them
    private int shownTaken;
    private float nextFill;
    private float[] filledAt;
    private readonly List<GameObject> blockObjects = new List<GameObject>();

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
        QueenBlitz.Active = inGame && config != null && config.queenBlitz && !config.isOnlineGame;
        if (!QueenBlitz.Active || Instance != null) return;
        QueenBlitz.Begin(config.blitzPuzzle);
        new GameObject("Queen Blitz Director").AddComponent<QueenBlitzDirector>();
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        var state = GameStateManager.Instance != null ? GameStateManager.Instance.currentState : GameState.WaitingForConfiguration;
        if (laidOut && ChessBoard.Instance != null) TrackTaken();
        if (state != GameState.PiecePlacement && state != GameState.Playing) return;
        if (ChessBoard.Instance == null || GameObject.Find("Pieces Container") == null) return;

        // Wait for the trays: the piece prefabs are built along with them
        if (!laidOut && PlacementManager.Instance != null && PlacementManager.Instance.PiecesReady) laidOut = LayOut();
        if (state == GameState.PiecePlacement && PlacementManager.Instance != null)
        {
            // The pawns have nothing to place: they're ready straight away
            if (!PlacementManager.Instance.IsPlayerReady(QueenBlitz.PawnColor))
                PlacementManager.Instance.SetPlayerReady(QueenBlitz.PawnColor, true);
            if (laidOut && !queenPlaced && QueenBlitz.Current.queen.HasValue) PlaceGivenQueen();
        }
    }

    /// <summary>Put the puzzle's pawns and blocks on the board; false if the pawns couldn't be made yet</summary>
    private bool LayOut()
    {
        QueenBlitz.Puzzle puzzle = QueenBlitz.Current;
        var made = new List<ChessPiece>();
        foreach (BoardPosition p in puzzle.pawns)
        {
            ChessPiece pawn = UndoManager.Create(ChessPieceType.Pawn, QueenBlitz.PawnColor, p);
            if (pawn == null)
            {
                // Not ready: take back what was made and try again next frame
                foreach (ChessPiece m in made) { ChessBoard.Instance.RemovePieceFromBoard(m, m.CurrentPosition); Destroy(m.gameObject); }
                return false;
            }
            made.Add(pawn);
        }
        Transform container = GameObject.Find("Pieces Container").transform;
        foreach (BoardPosition b in puzzle.blocks) blockObjects.Add(MakeBlock(container, b));
        Debug.Log($"Queen Blitz: puzzle {QueenBlitz.Index + 1} laid out, {made.Count} pawns, {puzzle.blocks.Length} blocks");
        return true;
    }

    /// <summary>A glowing stone cube sitting on the block's square</summary>
    private static GameObject MakeBlock(Transform container, BoardPosition at)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"Blitz Block {at}";
        Destroy(cube.GetComponent<Collider>()); // taps go to the board, not the block
        cube.transform.SetParent(container, false);
        float cell = ChessBoard.Instance.cellSize;
        cube.transform.localPosition = ChessBoard.Instance.BoardToLocalPosition(at) + Vector3.up * cell * 0.3f;
        cube.transform.localScale = Vector3.one * cell * 0.55f;
        cube.transform.localRotation = Quaternion.identity;
        var renderer = cube.GetComponent<MeshRenderer>();
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.35f, 0.25f, 0.55f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", NeonTheme.Pink * 0.35f);
        renderer.material = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // A neon outline so it reads as solid from any angle
        GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(edge.GetComponent<Collider>());
        edge.transform.SetParent(cube.transform, false);
        edge.transform.localScale = Vector3.one * 1.08f;
        Material glow = new Material(Shader.Find("Sprites/Default")) { color = new Color(NeonTheme.Pink.r, NeonTheme.Pink.g, NeonTheme.Pink.b, 0.25f) };
        edge.GetComponent<MeshRenderer>().material = glow;
        edge.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        cube.AddComponent<QueenBlitzBlock>().at = at;
        return cube;
    }

    /// <summary>Early puzzles: the queen starts on her square, and the player starts straight away</summary>
    private void PlaceGivenQueen()
    {
        PieceTray tray = PieceTray.GetTrayForColor(QueenBlitz.PlayerColor);
        if (tray == null) return;
        ChessPiece queen = null;
        foreach (ChessPiece p in tray.GetComponentsInChildren<ChessPiece>(true))
            if (p.pieceType == ChessPieceType.Queen && p.pieceColor == QueenBlitz.PlayerColor) { queen = p; break; }
        if (queen == null) return;
        queenPlaced = true;
        PlacementManager.Instance.ExecuteAIPlacement(queen, QueenBlitz.Current.queen.Value);
        StartCoroutine(ReadyNextFrame());
    }

    private IEnumerator ReadyNextFrame()
    {
        yield return null;
        if (PlacementManager.Instance != null && !PlacementManager.Instance.IsPlayerReady(QueenBlitz.PlayerColor))
            PlacementManager.Instance.SetPlayerReady(QueenBlitz.PlayerColor, true);
    }

    public int PawnsLeft()
    {
        int left = 0;
        int n = QueenBlitz.Current.size;
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        for (int z = 0; z < n; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null && p.pieceColor == QueenBlitz.PawnColor) left++;
        }
        return left;
    }

    public void AfterMove()
    {
        if (ended || PawnsLeft() > 0) return;
        StartCoroutine(Finish());
    }

    private IEnumerator Finish()
    {
        ended = true;
        QueenBlitz.Cleared = true;
        // Let the sweep play out first
        float until = Time.time + 3f;
        while (Time.time < until && GameStateManager.Instance != null && !GameStateManager.Instance.CanMovePieces()) yield return null;
        yield return new WaitForSeconds(0.3f);
        while (filledAt != null && shownTaken < filledAt.Length) yield return null; // let the last silhouettes fill in
        yield return new WaitForSeconds(0.35f);
        int par = QueenBlitz.Current.par;
        var (paid, best) = QueenBlitz.RecordClear();
        QueenBlitz.LastPaid = paid;
        // New pawns fly into the tally before the result shows (the pawns speak for themselves there)
        if (paid > 0) yield return Funnel(paid);
        string reason = $"Cleared in {QueenBlitz.Moves} move{(QueenBlitz.Moves == 1 ? "" : "s")}, par {par}." + (best ? " New best!" : "");
        if (GameEndDetectionManager.Instance != null) GameEndDetectionManager.Instance.EndGame(QueenBlitz.PlayerColor, reason);
        else if (GameStateManager.Instance != null) GameStateManager.Instance.ChangeState(GameState.GameOver);
    }

    /// <summary>Start this puzzle again from the beginning</summary>
    public static void Restart(int index)
    {
        GameConfiguration config = SceneController.Instance != null ? SceneController.Instance.GetCurrentGameConfiguration() : null;
        if (config == null) return;
        config = config.Clone();
        config.blitzPuzzle = Mathf.Clamp(index, 0, QueenBlitz.Puzzles.Length - 1);
        // The next puzzle may be on another size of cube
        int size = QueenBlitz.Puzzles[config.blitzPuzzle].size;
        config.boardSize = size == 4 ? BoardSize.Small4x4x4 : size == 6 ? BoardSize.Medium6x6x6 : BoardSize.Large8x8x8;
        SceneController.Instance.LoadGameScene(config);
    }

    // Fill in silhouettes one at a time (a sweep through several pawns fills them in quick
    // succession); Undo empties them again straight away
    private void TrackTaken()
    {
        int total = QueenBlitz.Current.pawns.Length;
        if (filledAt == null || filledAt.Length != total)
        {
            filledAt = new float[total];
            shownTaken = 0;
            alreadyCollected = QueenBlitz.EverCleared(QueenBlitz.Index);
            shownTotal = PawnBank.Total;
        }
        if (funnelStarted >= 0f) return; // the pawns are flying to the tally
        int taken = Mathf.Clamp(total - PawnsLeft(), 0, total);
        if (taken < shownTaken) shownTaken = taken;
        else if (taken > shownTaken && Time.time >= nextFill)
        {
            filledAt[shownTaken] = Time.time;
            shownTaken++;
            nextFill = Time.time + 0.12f;
        }
    }

    // ── The left column: your pawn total, Restart, Undo (UndoManager), then the puzzle's pawns ──
    private bool alreadyCollected;    // cleared before: its pawns show filled but greyed out
    private int shownTotal;           // the total as shown (it counts up as earned pawns arrive)
    private float funnelStarted = -1f, tallyBump = -10f;
    private int funnelArrived;
    private const float SlotSize = 26f, SlotGap = 6f, FunnelStep = 0.14f, FunnelFlight = 0.55f;
    private static readonly Rect TallyRect = new Rect(16f, 16f, 150f, 44f);

    private Rect Slot(int i) => new Rect(20f + (i % 4) * (SlotSize + 12f), 186f + (i / 4) * (SlotSize + SlotGap), SlotSize, SlotSize);

    /// <summary>Fly each newly earned pawn from its slot into the tally, one after another</summary>
    private IEnumerator Funnel(int paid)
    {
        funnelStarted = Time.time;
        funnelArrived = 0;
        float done = Time.time + (paid - 1) * FunnelStep + FunnelFlight + 0.5f;
        while (Time.time < done)
        {
            int arrived = Mathf.Clamp(Mathf.FloorToInt((Time.time - funnelStarted - FunnelFlight) / FunnelStep) + 1, 0, paid);
            if (arrived > funnelArrived) { funnelArrived = arrived; shownTotal = PawnBank.Total - paid + arrived; tallyBump = Time.time; }
            yield return null;
        }
        shownTotal = PawnBank.Total;
    }

    private void DrawTally()
    {
        NeonTheme.GUIPanel(TallyRect, NeonTheme.Yellow, 0.85f);
        float k = Mathf.Clamp01((Time.time - tallyBump) / 0.3f);
        float bump = 1f + 0.25f * (1f - k) * Mathf.Sin(k * Mathf.PI);
        Color before = GUI.color;
        GUI.color = NeonTheme.Yellow;
        float s = 24f * bump;
        GUI.DrawTexture(new Rect(TallyRect.x + 26f - s / 2f, TallyRect.center.y - s / 2f, s, s), NeonIcons.Pawn);
        GUI.color = before;
        int size = Mathf.RoundToInt(18 * bump);
        GUI.Label(new Rect(TallyRect.x + 42f, TallyRect.y, TallyRect.width - 50f, TallyRect.height), $"{shownTotal:N0}",
            NeonTheme.GUILabelStyle(NeonTheme.Yellow, size, true, TextAnchor.MiddleLeft));
    }

    private void DrawTracker()
    {
        if (filledAt == null) return;
        Color before = GUI.color;
        Color grey = new Color(0.62f, 0.58f, 0.7f);
        for (int i = 0; i < filledAt.Length; i++)
        {
            Rect r = Slot(i);
            bool taken = i < shownTaken;
            // Earned pawns flying to the tally
            if (funnelStarted >= 0f && taken && !alreadyCollected)
            {
                float f = (Time.time - funnelStarted - i * FunnelStep) / FunnelFlight;
                if (f >= 1f) continue;                 // arrived
                if (f > 0f)
                {
                    float e = f * f;                      // speeds up into the tally
                    Vector2 from = r.center, to = TallyRect.position + new Vector2(26f, TallyRect.height / 2f);
                    Vector2 mid = new Vector2(Mathf.Lerp(from.x, to.x, 0.5f) + 40f, Mathf.Lerp(from.y, to.y, 0.5f));
                    Vector2 at = Vector2.Lerp(Vector2.Lerp(from, mid, e), Vector2.Lerp(mid, to, e), e);
                    float s = SlotSize * Mathf.Lerp(1f, 0.6f, e);
                    GUI.color = NeonTheme.Yellow;
                    GUI.DrawTexture(new Rect(at.x - s / 2f, at.y - s / 2f, s, s), NeonIcons.Pawn);
                    continue;
                }
            }
            if (!taken)
            {
                // Not taken yet: an empty outline, or a dim grey pawn if it was collected before
                GUI.color = alreadyCollected ? new Color(grey.r, grey.g, grey.b, 0.4f) : new Color(NeonTheme.Lavender.r, NeonTheme.Lavender.g, NeonTheme.Lavender.b, 0.55f);
                GUI.DrawTexture(r, alreadyCollected ? NeonIcons.Pawn : NeonIcons.PawnOutline);
                continue;
            }
            // Impact: lands big, squashes, and settles with a wobble; a flash and a glow fade out
            float k = Mathf.Clamp01((Time.time - filledAt[i]) / 0.4f);
            float bump = 1f + 0.55f * Mathf.Exp(-6f * k) * Mathf.Cos(k * Mathf.PI * 3f);
            float squash = 1f - 0.25f * Mathf.Exp(-8f * k) * Mathf.Sin(k * Mathf.PI * 3f);
            float w = SlotSize * bump / squash, h = SlotSize * bump * squash;
            Rect hit = new Rect(r.center.x - w / 2f, r.yMax - h, w, h); // grows from its feet
            Color fill = alreadyCollected ? grey : NeonTheme.Yellow;
            if (k < 1f)
            {
                float glow = SlotSize * (0.8f + 1.3f * k);
                GUI.color = new Color(fill.r, fill.g, fill.b, 0.45f * (1f - k));
                GUI.DrawTexture(new Rect(r.center.x - glow / 2f, r.center.y - glow / 2f, glow, glow), NeonTheme.Circle.texture);
            }
            GUI.color = Color.Lerp(Color.white, fill, Mathf.Clamp01(k * 2.5f));
            GUI.DrawTexture(hit, NeonIcons.Pawn);
        }
        GUI.color = before;
    }

    // Puzzle name, moves and par along the top with the tip under it; down the left your pawn
    // total, Restart and the puzzle's pawns. After a clear, only the pawns and total stay while
    // new pawns fly into it.
    private void OnGUI()
    {
        var state = GameStateManager.Instance != null ? GameStateManager.Instance.currentState : GameState.WaitingForConfiguration;
        bool playing = !ended && (state == GameState.Playing || state == GameState.PiecePlacement);
        bool finishing = ended && state != GameState.GameOver;
        bool over = state == GameState.GameOver;   // the result is up: just the total stays
        if (!playing && !finishing && !over) return;
        Vector2 ui = TouchGUI.Begin();
        if (playing)
        {
            QueenBlitz.Puzzle puzzle = QueenBlitz.Current;
            string text = state == GameState.PiecePlacement
                ? $"Puzzle {QueenBlitz.Index + 1}: {puzzle.name}    ·    Place your queen    ·    Par {puzzle.par}"
                : $"Puzzle {QueenBlitz.Index + 1}: {puzzle.name}    ·    Moves {QueenBlitz.Moves}    ·    Par {puzzle.par}";
            // At the very top (the turn line is hidden in puzzles), clear of the board
            Rect rect = new Rect(ui.x / 2f - 230f, 6f, 460f, 32f);
            NeonTheme.GUIPanel(rect, NeonTheme.Yellow, 0.85f);
            GUI.Label(rect, text, NeonTheme.GUILabelStyle(NeonTheme.Yellow, 16, true, TextAnchor.MiddleCenter));
            if (QueenBlitz.Moves == 0)
                GUI.Label(new Rect(ui.x / 2f - 250f, 38f, 500f, 22f), puzzle.tip, NeonTheme.GUILabelStyle(NeonTheme.Lavender, 14, true, TextAnchor.MiddleCenter));
            if (state == GameState.Playing && TouchGUI.NeonButton(new Rect(16, 76, 150, 44), "Restart", NeonTheme.Yellow, NeonTheme.Pink, fontSize: 16))
                Restart(QueenBlitz.Index);
        }
        DrawTally();
        if (!over) DrawTracker();
        TouchGUI.End();
    }
}
