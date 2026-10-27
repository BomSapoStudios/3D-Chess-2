using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Queen Blitz: a puzzle mode. The board holds pawns (and, later, blocks); the player has a
/// single Queen and clears every pawn in as few moves as possible. A move slides the queen any
/// distance along one of its 26 lines, sweeping up every pawn it passes through or stops on;
/// blocks and the board's edge stop it. Early puzzles place the queen for you; later ones make
/// you choose where she starts (placing her is not a move). On an ice floor she can't stop
/// partway: every move slides to the board's edge or the square before a block.
///
/// Puzzles load from JSON pack files; pars come from the solver in Tools/QueenBlitz (packs.py checks them all).
/// </summary>
public static partial class QueenBlitz
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
        public BoardPosition[] line;     // one par solution: the queen's start, then each square she stops on
        public int score;                // difficulty 1-100 from the pipeline (0 for hand-made puzzles)
        public string tier;
        public PortalPair[] portals;     // see QueenBlitzRoutes
        public RampRise[] ramps;
    }

    // ───────────────────────── Packs ─────────────────────────
    // Puzzles live in JSON, one file per pack, in Assets/Resources/QueenBlitz (written and checked by
    // Tools/QueenBlitz/packs.py). Squares are flat x,y,z lists; an empty "queen" means the player places her.

    [System.Serializable]
    private class PuzzleData
    {
        public string name;
        public int size;
        public int[] queen;
        public int[] best;
        public int[] pawns;
        public int[] blocks;
        public int par;
        public string tip;
        public bool ice;
        public int[] line;
        public int score;
        public string tier;
        public int[] portals;  // 13 a pair: in x,y,z, its facing, out x,y,z, its facing, two-way 0/1
        public int[] ramps;    // 6 a rising direction: x,y,z, dx,0,dz (a square repeats for each way it rises)
    }

    [System.Serializable]
    private class PackData
    {
        public string id;
        public string name;
        public bool free;
        public PuzzleData[] puzzles;
    }

    /// <summary>A pack the menu can show. Its puzzles are in Resources/QueenBlitz/{id}.json.</summary>
    public class PackInfo
    {
        public string id;
        public string name;
        public bool free;
        public int pawnPrice;        // 0: can't be bought with pawns
        public bool review;          // puzzles waiting for Brandon's playtest (see Reviewer)
    }

    public const int ChapterSize = 10;
    /// <summary>Clears in a chapter that open the next one</summary>
    public const int ChapterUnlock = 7;
    public const int PackPawnPrice = 4500;

    // Every pack in menu order; ones without a file yet are skipped (see Available)
    private static readonly PackInfo[] Catalog =
    {
        new PackInfo { id = "starter", name = "Starter", free = true },
        new PackInfo { id = "cube-run", name = "Cube Run", pawnPrice = PackPawnPrice },
        new PackInfo { id = "deep-freeze", name = "Deep Freeze", pawnPrice = PackPawnPrice },
        new PackInfo { id = "blockade", name = "Blockade", pawnPrice = PackPawnPrice },
        new PackInfo { id = "feast", name = "Feast", pawnPrice = PackPawnPrice },
        new PackInfo { id = "review", name = "Review", free = true, review = true },
        new PackInfo { id = "review-5", name = "Review 5³", free = true, review = true },
        new PackInfo { id = "tricky", name = "Tricky (test)", free = true, review = true },
        new PackInfo { id = "tricky-2", name = "Tricky 2 (test)", free = true, review = true },
        new PackInfo { id = "tricky-3", name = "Tricky 3 (test)", free = true, review = true },
        new PackInfo { id = "tricky-4", name = "Tricky 4 (test)", free = true, review = true },
        new PackInfo { id = "portal-test", name = "Portals & Ramps (test)", free = true, review = true },
    };

    /// <summary>The packs that exist in this build, in menu order</summary>
    public static List<PackInfo> Available()
    {
        var list = new List<PackInfo>();
        foreach (PackInfo p in Catalog)
        {
            if (p.review && !Reviewer) continue;
            if (PuzzlesOf(p.id).Length > 0) list.Add(p);
        }
        return list;
    }

    /// <summary>Brandon's playtest switch: shows the Review pack. Always on in development builds;
    /// in other builds, tap "Your pawns" in the Queen Blitz picker five times.</summary>
    public static bool Reviewer
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        get => true;
#else
        get => PlayerPrefs.GetInt("QueenBlitzReviewer", 0) == 1;
#endif
        set { PlayerPrefs.SetInt("QueenBlitzReviewer", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static PackInfo Info(string id)
    {
        if (IsDaily(id)) return new PackInfo { id = id, name = $"Daily #{DailyNumber(DailyDate(id))}", free = true };
        foreach (PackInfo p in Catalog) if (p.id == id) return p;
        return Catalog[0];
    }

    private static string packId = "starter";

    /// <summary>The pack being played (the menu sets it before a puzzle starts)</summary>
    public static string PackId
    {
        get => packId;
        set => packId = string.IsNullOrEmpty(value) ? "starter" : value;
    }

    public static PackInfo CurrentPack => Info(PackId);

    private static readonly Dictionary<string, Puzzle[]> loaded = new Dictionary<string, Puzzle[]>();

    public static Puzzle[] Puzzles => PuzzlesOf(PackId);

    public static Puzzle[] PuzzlesOf(string id)
    {
        if (!loaded.TryGetValue(id, out Puzzle[] list))
        {
            list = LoadPack(id);
            loaded[id] = list;
        }
        return list;
    }

    /// <summary>"Puzzle 3: Corner Turn", or just "Daily #12" for a daily</summary>
    public static string PuzzleTitle => IsDaily(PackId) ? Current.name : $"Puzzle {Index + 1}: {Current.name}";

    // ───────────────────────── Daily puzzle ─────────────────────────
    // One puzzle per date, made in advance (Tools/QueenBlitz/daily.py) and served as static files
    // from the Worker at /daily/YYYY-MM-DD.json. The app keeps the past week and the next six days,
    // so it plays offline for a week. A daily is a one-puzzle pack with the id "daily:YYYY-MM-DD".

    /// <summary>Daily #1 (daily.py's EPOCH must match)</summary>
    public static readonly System.DateTime DailyEpoch = new System.DateTime(2026, 11, 1);
    public const string DailyPrefix = "daily:";
    public const int DailyAhead = 6;
    public const int DailyBehind = 7;

    /// <summary>The player's own calendar date. Development builds can pretend another day with
    /// PlayerPrefs "DebugDailyToday" = "yyyy-MM-dd" (for testing dailies before they start).</summary>
    public static System.DateTime Today
    {
        get
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            string debug = PlayerPrefs.GetString("DebugDailyToday", "");
            if (System.DateTime.TryParseExact(debug, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out System.DateTime pretend)) return pretend;
#endif
            return System.DateTime.Now.Date;
        }
    }
    public static bool IsDaily(string id) => id != null && id.StartsWith(DailyPrefix);
    public static string DailyId(System.DateTime day) => DailyPrefix + day.ToString("yyyy-MM-dd");
    public static int DailyNumber(System.DateTime day) => (int)(day.Date - DailyEpoch).TotalDays + 1;

    public static System.DateTime DailyDate(string id) =>
        System.DateTime.ParseExact(id.Substring(DailyPrefix.Length), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string DailyFile(string date) =>
        System.IO.Path.Combine(Application.persistentDataPath, "QueenBlitzDaily", date + ".json");

    public static bool HasDaily(System.DateTime day) => System.IO.File.Exists(DailyFile(day.ToString("yyyy-MM-dd")));

    public static bool DailyCleared(System.DateTime day) => EverCleared(0, DailyId(day));

    /// <summary>Days in a row with the daily cleared, up to today (today not done yet doesn't break it)</summary>
    public static int DailyStreak()
    {
        System.DateTime day = DailyCleared(Today) ? Today : Today.AddDays(-1);
        int streak = 0;
        while (day >= DailyEpoch && DailyCleared(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    /// <summary>Download any of these days' dailies the phone doesn't have yet. Days before Daily #1
    /// are skipped; a missing file (not made yet, or offline) is simply left for next time.</summary>
    public static IEnumerator FetchDailies(System.DateTime first, System.DateTime last, System.Action done = null)
    {
        for (System.DateTime day = first.Date; day <= last.Date; day = day.AddDays(1))
        {
            if (day < DailyEpoch || HasDaily(day)) continue;
            string date = day.ToString("yyyy-MM-dd");
            // A day that wasn't there is asked for again tomorrow at the earliest: missing files reach
            // the Worker itself, and those requests count against its daily limit
            string triedKey = "QueenBlitzDailyTried." + date, todayText = Today.ToString("yyyy-MM-dd");
            if (PlayerPrefs.GetString(triedKey, "") == todayText) continue;
            PlayerPrefs.SetString(triedKey, todayText);
            using (UnityWebRequest request = UnityWebRequest.Get($"{OnlineClient.DefaultServerUrl}/daily/{date}.json"))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    PlayerPrefs.DeleteKey(triedKey); // offline: try again next time
                    break;
                }
                if (request.result != UnityWebRequest.Result.Success) continue;
                string text = request.downloadHandler.text;
                if (string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith("{")) continue;
                string path = DailyFile(date);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, text);
                loaded.Remove(DailyId(day));
            }
        }
        done?.Invoke();
    }

    /// <summary>The usual window: the past week (catch-up) and the next six days (offline)</summary>
    public static IEnumerator FetchDailyWindow(System.Action done = null) =>
        FetchDailies(Today.AddDays(-DailyBehind), Today.AddDays(DailyAhead), done);

    // ───────────────────────── Owning packs and opening chapters ─────────────────────────

    public static bool Owns(PackInfo pack) => pack.free || PlayerPrefs.GetInt("QueenBlitzOwned." + pack.id, 0) == 1;

    /// <summary>Unlock a pack: after an App Store purchase or restore, or with pawns</summary>
    public static void Grant(string id)
    {
        PlayerPrefs.SetInt("QueenBlitzOwned." + id, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Buy a pack with pawns. False if there aren't enough.</summary>
    public static bool BuyWithPawns(PackInfo pack)
    {
        if (Owns(pack) || pack.pawnPrice <= 0 || !PawnBank.Spend(pack.pawnPrice)) return false;
        Grant(pack.id);
        return true;
    }

    public static int Chapters(string id) => Mathf.Max(1, (PuzzlesOf(id).Length + ChapterSize - 1) / ChapterSize);

    public static int ClearedCount(string id, int first, int count)
    {
        int n = 0;
        for (int i = first; i < first + count && i < PuzzlesOf(id).Length; i++) if (EverCleared(i, id)) n++;
        return n;
    }

    public static int ClearedCount(string id) => ClearedCount(id, 0, PuzzlesOf(id).Length);

    /// <summary>Chapter 1 is always open; each later one opens after 7 clears in the one before</summary>
    public static bool ChapterOpen(string id, int chapter)
    {
        if (chapter <= 0 || Info(id).review) return true;
        return ClearedCount(id, (chapter - 1) * ChapterSize, ChapterSize) >= ChapterUnlock;
    }

    public static bool PuzzleOpen(string id, int index) => Owns(Info(id)) && ChapterOpen(id, index / ChapterSize);

    private static Puzzle[] LoadPack(string id)
    {
        PackData pack;
        if (IsDaily(id))
        {
            // A daily is one puzzle, downloaded into the phone's storage (see FetchDailies)
            string path = DailyFile(id.Substring(DailyPrefix.Length));
            if (!System.IO.File.Exists(path)) return new Puzzle[0];
            pack = new PackData { id = id, free = true, puzzles = new[] { JsonUtility.FromJson<PuzzleData>(System.IO.File.ReadAllText(path)) } };
        }
        else
        {
            TextAsset file = Resources.Load<TextAsset>("QueenBlitz/" + id);
            if (file == null) return new Puzzle[0]; // a pack not made yet
            pack = JsonUtility.FromJson<PackData>(file.text);
        }
        var list = new List<Puzzle>();
        foreach (PuzzleData d in pack.puzzles)
        {
            BoardPosition[] queen = Cells(d.queen);
            BoardPosition[] best = Cells(d.best);
            list.Add(new Puzzle
            {
                name = d.name, size = d.size, par = d.par, tip = d.tip, ice = d.ice,
                queen = queen.Length > 0 ? queen[0] : (BoardPosition?)null,
                best = best.Length > 0 ? best[0] : default,
                pawns = Cells(d.pawns), blocks = Cells(d.blocks), line = Cells(d.line), score = d.score, tier = d.tier,
                portals = Portals(d.portals), ramps = Ramps(d.ramps),
            });
        }
        return list.ToArray();
    }

    private static PortalPair[] Portals(int[] f)
    {
        var list = new List<PortalPair>();
        for (int i = 0; f != null && i + 12 < f.Length; i += 13)
            list.Add(new PortalPair
            {
                a = new BoardPosition(f[i], f[i + 1], f[i + 2]), faceA = new Vector3Int(f[i + 3], f[i + 4], f[i + 5]),
                b = new BoardPosition(f[i + 6], f[i + 7], f[i + 8]), faceB = new Vector3Int(f[i + 9], f[i + 10], f[i + 11]),
                twoWay = f[i + 12] != 0,
            });
        return list.ToArray();
    }

    private static RampRise[] Ramps(int[] f)
    {
        var list = new List<RampRise>();
        for (int i = 0; f != null && i + 5 < f.Length; i += 6)
            list.Add(new RampRise { at = new BoardPosition(f[i], f[i + 1], f[i + 2]), dir = new Vector3Int(f[i + 3], f[i + 4], f[i + 5]) });
        return list.ToArray();
    }

    private static BoardPosition[] Cells(int[] flat)
    {
        if (flat == null) return new BoardPosition[0];
        var cells = new BoardPosition[flat.Length / 3];
        for (int i = 0; i < cells.Length; i++) cells[i] = new BoardPosition(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
        return cells;
    }

    /// <summary>Saved progress for a puzzle. Starter keeps the keys it had before packs existed.</summary>
    private static string Key(string kind, int index, string pack = null)
    {
        pack = pack ?? PackId;
        return pack == "starter" ? $"QueenBlitz{kind}.{index}" : $"QueenBlitz{kind}.{pack}.{index}";
    }

    /// <summary>The current game is a Queen Blitz puzzle</summary>
    public static bool Active { get; set; }
    public static int Index { get; set; }
    public static Puzzle Current => Puzzles[Mathf.Clamp(Index, 0, Puzzles.Length - 1)];
    public static int Moves { get; set; }
    /// <summary>Pawns the queen has swept this puzzle: the tracker never shows more as taken</summary>
    public static int Swept { get; set; }
    public static bool Cleared { get; set; }
    /// <summary>Par is the move limit: the moves ran out with pawns still on the board</summary>
    public static bool OutOfMoves { get; set; }
    /// <summary>Set when leaving a puzzle for the puzzle picker: the menu opens on this pack</summary>
    public static bool ReturnToPicker { get; set; }
    public static int MovesLeft => Mathf.Max(0, Current.par - Moves);
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
        Swept = 0;
        Cleared = false;
        OutOfMoves = false;
        AutoPlaced = false;
        blocked.Clear();
        foreach (BoardPosition b in Current.blocks) blocked.Add(b);
        SetUpFeatures(Current);
    }

    /// <summary>Nothing can be placed here: a block, a portal or a ramp</summary>
    public static bool IsBlocked(BoardPosition p) => Active && (blocked.Contains(p) || portalAt.ContainsKey(p) || rampAt.ContainsKey(p));

    /// <summary>The pawns this puzzle pays when cleared: all of them, the first time (PawnBank sets the rule)</summary>
    public static int Reward(int index) => Puzzles[index].pawns.Length;

    public static bool EverCleared(int index, string pack = null) => PlayerPrefs.GetInt(Key("Cleared", index, pack), 0) == 1;
    public static int BestMoves(int index, string pack = null) => PlayerPrefs.GetInt(Key("Best", index, pack), 0);

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
        PlayerPrefs.SetInt(Key("Cleared", Index), 1);
        int before = BestMoves(Index);
        bool best = before == 0 || Moves < before;
        if (best) PlayerPrefs.SetInt(Key("Best", Index), Moves);
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
        FindRoutes(queen.CurrentPosition);
        foreach (var key in routes.Keys) moves.Add(key.Item2);
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
        Route route = RouteFor(from, to);
        PendingRoute = route != null && route.special ? route : null; // the slide follows it through portals and up ramps
        if (route == null)
        {
            foreach (BoardPosition p in Between(from, to)) Sweep(queen, p, from, to);
            return;
        }
        for (int i = 0; i < route.swept.Count; i++)
        {
            Vector3Int d = route.sweptDir[i];
            BoardPosition p = route.swept[i];
            Sweep(queen, p, new BoardPosition(p.x - d.x, p.y - d.y, p.z - d.z), p);
        }
    }

    private static void Sweep(ChessPiece queen, BoardPosition p, BoardPosition from, BoardPosition to)
    {
        ChessPiece pawn = ChessBoard.Instance.GetPieceAt(p);
        if (pawn == null || pawn.pieceColor == queen.pieceColor) return;
        KnockAway(pawn, queen, from, to);
        ChessBoard.Instance.RemovePieceFromBoard(pawn, p);
    }

    /// <summary>Knock a pawn flying the way the queen is sliding</summary>
    public static void KnockAway(ChessPiece pawn, ChessPiece queen, BoardPosition from, BoardPosition to)
    {
        if (pawn.pieceColor == PawnColor) Swept++;
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

    /// <summary>The cube a puzzle of this size plays on</summary>
    public static BoardSize BoardFor(int size)
    {
        switch (size)
        {
            case 4: return BoardSize.Small4x4x4;
            case 5: return BoardSize.Small5x5x5;
            case 6: return BoardSize.Medium6x6x6;
            default: return BoardSize.Large8x8x8;
        }
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
public partial class QueenBlitzDirector : MonoBehaviour
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
        MakeFeatures(container, puzzle); // portals and ramps
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
        if (ended) return;
        if (PawnsLeft() == 0) StartCoroutine(Finish());
        else if (QueenBlitz.MovesLeft == 0) StartCoroutine(Fail()); // par is the limit
    }

    /// <summary>Out of moves with pawns left: nothing is paid or recorded, Retry tries again</summary>
    private IEnumerator Fail()
    {
        ended = true;
        QueenBlitz.OutOfMoves = true;
        float until = Time.time + 3f;
        while (Time.time < until && GameStateManager.Instance != null && !GameStateManager.Instance.CanMovePieces()) yield return null;
        yield return new WaitForSeconds(0.6f);
        int left = PawnsLeft();
        string reason = $"{left} pawn{(left == 1 ? "" : "s")} left. Par is the limit.";
        if (GameEndDetectionManager.Instance != null) GameEndDetectionManager.Instance.EndGame(QueenBlitz.PawnColor, reason);
        else if (GameStateManager.Instance != null) GameStateManager.Instance.ChangeState(GameState.GameOver);
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
        GameCenter.CheckPuzzles();
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
        config.boardSize = QueenBlitz.BoardFor(size);
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
        int left = PawnsLeft();
        // Guard: pieces gone that no move took (never expected) start the puzzle again cleanly
        // rather than showing pawns as collected or a board with no queen
        var state = GameStateManager.Instance != null ? GameStateManager.Instance.currentState : GameState.WaitingForConfiguration;
        bool missing = !ended && state == GameState.Playing && GameStateManager.Instance.CanMovePieces() && !restarting
            && (left < total - QueenBlitz.Swept || !QueenOnBoard());
        if (!missing) missingSince = -1f;
        else if (missingSince < 0f) missingSince = Time.time;
        if (missing && Time.time - missingSince > 1f) // not just a frame between a restore's steps
        {
            Debug.LogError($"Queen Blitz: pieces went missing ({left} pawns, {QueenBlitz.Swept} swept of {total}, queen {QueenOnBoard()}): starting the puzzle again");
            restarting = true;
            Restart(QueenBlitz.Index);
            return;
        }
        int taken = Mathf.Clamp(Mathf.Min(total - left, QueenBlitz.Swept), 0, total);
        if (taken < shownTaken) shownTaken = taken;
        else if (taken > shownTaken && Time.time >= nextFill)
        {
            filledAt[shownTaken] = Time.time;
            shownTaken++;
            nextFill = Time.time + 0.12f;
        }
    }

    // ── The left column: your pawn total, Restart, Undo (UndoManager), then the puzzle's pawns ──
    private bool restarting;
    private float missingSince = -1f;

    private static bool QueenOnBoard()
    {
        foreach (ChessPiece p in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
            if (p.pieceType == ChessPieceType.Queen && p.pieceColor == QueenBlitz.PlayerColor && !p.IsInTray() && p.CurrentPosition.IsValid()
                && ChessBoard.Instance.GetPieceAt(p.CurrentPosition) == p) return true;
        return false;
    }

    private bool alreadyCollected;    // cleared before: its pawns show filled but greyed out
    private int shownTotal;           // the total as shown (it counts up as earned pawns arrive)
    private float funnelStarted = -1f, tallyBump = -10f;
    private int funnelArrived;
    private const float SlotSize = 26f, SlotGap = 6f, FunnelStep = 0.09f, FunnelFlight = 0.5f;
    private static readonly Rect TallyTop = new Rect(16f, 16f, 150f, 44f);
    // While placing the queen, White's Ready button has the top-left corner: the total sits under
    // it, where Restart goes once play starts
    private static readonly Rect TallyUnderReady = new Rect(16f, 80f, 150f, 44f);
    private static Rect TallyRect => GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.PiecePlacement
        ? TallyUnderReady : TallyTop;

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
        float k = AccessibilityPrefs.ReduceMotion ? 1f : Mathf.Clamp01((Time.time - tallyBump) / 0.3f); // Reduce Motion: no bounce
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
                if (f > 0f && AccessibilityPrefs.ReduceMotion) continue; // Reduce Motion: it leaves its slot without flying
                if (f > 0f)
                {
                    float e = f * f;                      // speeds up into the tally
                    Vector2 from = r.center, to = TallyRect.position + new Vector2(26f, TallyRect.height / 2f);
                    Vector2 mid = new Vector2(Mathf.Lerp(from.x, to.x, 0.5f) + 40f, Mathf.Lerp(from.y, to.y, 0.5f));
                    Vector2 at = Vector2.Lerp(Vector2.Lerp(from, mid, e), Vector2.Lerp(mid, to, e), e);
                    float s = SlotSize * Mathf.Lerp(0.55f, 0.4f, e); // a stream of small pawns
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
            float k = AccessibilityPrefs.ReduceMotion ? 1f : Mathf.Clamp01((Time.time - filledAt[i]) / 0.4f);
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
            if (PlayerSymbols.On)
            {
                // Color-Blind Symbols: taken pawns get a tick, so taken and not-yet-taken differ in shape too
                GUI.color = NeonTheme.Ground;
                GUI.DrawTexture(new Rect(r.xMax - SlotSize * 0.5f + 1f, r.yMax - SlotSize * 0.5f + 1f, SlotSize * 0.5f, SlotSize * 0.5f), NeonIcons.Tick);
                GUI.color = NeonTheme.Lime;
                GUI.DrawTexture(new Rect(r.xMax - SlotSize * 0.5f, r.yMax - SlotSize * 0.5f, SlotSize * 0.5f, SlotSize * 0.5f), NeonIcons.Tick);
            }
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
        if (!playing) selectorOpen = false;
        GUI.depth = selectorOpen ? -30 : 0; // the puzzle selector sits over every other control
        if (!playing && !finishing && !over) return;
        Vector2 ui = TouchGUI.Begin();
        GUI.enabled = !selectorOpen; // under the selector nothing here takes a tap
        if (playing)
        {
            QueenBlitz.Puzzle puzzle = QueenBlitz.Current;
            int left = QueenBlitz.MovesLeft;
            string movesLeft = $"Moves left {left}";
            if (left <= 1) movesLeft = $"<color=#{ColorUtility.ToHtmlStringRGB(NeonTheme.Pink)}>{movesLeft}</color>";
            string text = state == GameState.PiecePlacement
                ? $"{QueenBlitz.PuzzleTitle}  ·  Place your queen  ·  Par {puzzle.par}"
                : $"{QueenBlitz.PuzzleTitle}  ·  {movesLeft}  ·  Par {puzzle.par}";
            // At the very top (the turn line is hidden in puzzles), between the pawn total on the
            // left and the "?" on the right with a clear gap to each; smaller text, then two lines,
            // on narrow screens
            // on narrow screens. A grid icon at its end: tap the bar for the puzzle selector
            float from = TallyTop.xMax + 12f, to = ui.x - 82f - 12f;
            float w = Mathf.Min(460f, to - from);
            const float icon = 26f;
            GUIStyle style = NeonTheme.GUILabelStyle(NeonTheme.Yellow, 16, true, TextAnchor.MiddleCenter);
            style.richText = true;
            while (style.fontSize > 12 && style.CalcSize(new GUIContent(text)).x > w - 16f - icon) style.fontSize--;
            float h = Mathf.Max(32f, style.CalcHeight(new GUIContent(text), w - 16f - icon) + 8f);
            Rect rect = new Rect(from + (to - from - w) / 2f, 6f, w, h);
            NeonTheme.GUIPanel(rect, NeonTheme.Yellow, 0.85f);
            GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 16f - icon, rect.height), text, style);
            DrawGridIcon(new Rect(rect.xMax - 8f - 16f, rect.center.y - 8f, 16f, 16f));
            TouchGUI.Block(rect);
            if (!selectorOpen && GUI.Button(rect, GUIContent.none, GUIStyle.none)) OpenSelector();
            if (QueenBlitz.Moves == 0)
            {
                GUIStyle tip = NeonTheme.GUILabelStyle(NeonTheme.Lavender, 14, true, TextAnchor.UpperCenter);
                float tw = to - from;
                NeonTheme.SceneLabel(new Rect(from, rect.yMax + 2f, tw, tip.CalcHeight(new GUIContent(puzzle.tip), tw) + 2f), puzzle.tip, tip);
            }
            if (state == GameState.Playing && TouchGUI.NeonButton(new Rect(16, 76, 150, 44), "Restart", NeonTheme.Yellow, NeonTheme.Pink, fontSize: 16))
                Restart(QueenBlitz.Index);
        }
        DrawTally();
        if (!over) DrawTracker();
        GUI.enabled = true;
        TouchGUI.End();
        if (selectorOpen) DrawSelector();
    }
}
