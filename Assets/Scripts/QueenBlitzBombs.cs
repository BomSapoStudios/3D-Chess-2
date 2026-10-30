using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queen Blitz bombs (Brandon, Oct 30). This mirrors the bomb rules in Tools/QueenBlitz/solver.py:
/// - A bomb sits on an empty square. Some puzzles have bombs already down; others give the player
///   bombs to place before the first move (placing one isn't a move), like placing the queen.
/// - It goes off when the queen passes over it or stops on it, taking every pawn on the 26 squares
///   around it (the 3x3x3 cube), and is gone. A blast that reaches another bomb sets it off too.
/// - Bombs don't stop her, and blocks, portals and ramps survive a blast.
/// </summary>
public static partial class QueenBlitz
{
    // Every bomb still on the board; placedBombs are the ones the player put down this setup
    private static readonly HashSet<BoardPosition> liveBombs = new HashSet<BoardPosition>();
    private static readonly List<BoardPosition> placedBombs = new List<BoardPosition>();

    /// <summary>Goes up whenever bombs are placed, picked up, go off or come back (Undo)</summary>
    public static int BombVersion { get; private set; }

    public static IEnumerable<BoardPosition> LiveBombs => liveBombs;
    public static bool IsBomb(BoardPosition p) => Active && liveBombs.Contains(p);
    public static bool IsPlacedBomb(BoardPosition p) => Active && placedBombs.Contains(p);
    public static bool HasBombs => Active && (Current.bombs.Length > 0 || Current.placeBombs > 0);
    /// <summary>Bombs still on the bench (setup)</summary>
    public static int BombsToPlace => Active ? Mathf.Max(0, Current.placeBombs - placedBombs.Count) : 0;

    private static void SetUpBombs(Puzzle p)
    {
        liveBombs.Clear();
        placedBombs.Clear();
        foreach (BoardPosition b in p.bombs ?? new BoardPosition[0]) liveBombs.Add(b);
        BombVersion++;
    }

    /// <summary>A bomb can go here: an empty square of the board (no piece, block, portal, ramp or bomb)</summary>
    public static bool CanPlaceBomb(BoardPosition c) =>
        BombsToPlace > 0 && ChessBoard.Instance != null && ChessBoard.Instance.IsValidPosition(c) && !IsBlocked(c) && ChessBoard.Instance.GetPieceAt(c) == null;

    public static bool PlaceBomb(BoardPosition c)
    {
        if (!CanPlaceBomb(c)) return false;
        placedBombs.Add(c);
        liveBombs.Add(c);
        BombVersion++;
        return true;
    }

    /// <summary>Back to the bench: only bombs the player placed, during setup</summary>
    public static bool PickUpBomb(BoardPosition c)
    {
        if (!placedBombs.Remove(c)) return false;
        liveBombs.Remove(c);
        BombVersion++;
        return true;
    }

    /// <summary>Auto-place (and resuming a saved puzzle): the bombs go where the par solution has them</summary>
    private static void PlaceBestBombs()
    {
        foreach (BoardPosition b in placedBombs.ToArray()) PickUpBomb(b);
        foreach (BoardPosition b in Current.bestBombs ?? new BoardPosition[0])
        {
            // The queen may be standing there: she's about to go on her own par square anyway
            ChessPiece there = ChessBoard.Instance != null ? ChessBoard.Instance.GetPieceAt(b) : null;
            if (there != null && there.pieceType == ChessPieceType.Queen && there.pieceColor == PlayerColor && PlacementManager.Instance != null)
                PlacementManager.Instance.ReturnToTray(there, b);
            PlaceBomb(b);
        }
    }

    /// <summary>Resuming a saved puzzle: put the bombs down so setup can finish (the save then says which are left)</summary>
    public static void FillBombs()
    {
        if (BombsToPlace > 0) PlaceBestBombs();
    }

    /// <summary>The bombs still on the board, for Undo and saved games</summary>
    public static List<BoardPosition> BombsSnapshot() => new List<BoardPosition>(liveBombs);

    public static void RestoreBombs(IEnumerable<BoardPosition> bombs)
    {
        liveBombs.Clear();
        if (bombs != null) foreach (BoardPosition b in bombs) liveBombs.Add(b);
        BombVersion++;
    }

    // The Mirror Queen's move answering this queen move (MoveMirror sets it), for her bombs
    private static Route mirrorMove;

    /// <summary>
    /// The queen's move is under way along this route (her own sweep and the Mirror Queen's already
    /// done): every bomb either of them passes or stops on goes off, then any bomb caught in a blast,
    /// and so on. Pawns caught come off the board now; the effect waits for whichever queen set it off
    /// to reach the bomb that started it.
    /// </summary>
    private static void SetOffBombs(ChessPiece queen, Route route)
    {
        if (liveBombs.Count == 0 || ChessBoard.Instance == null) return;
        var queue = new List<(BoardPosition at, int link, BoardPosition trigger, bool byMirror)>();
        foreach (BoardPosition c in new List<BoardPosition>(route.swept) { route.to })
            if (liveBombs.Contains(c) && !queue.Exists(q => q.at == c)) queue.Add((c, 0, c, false));
        if (mirrorMove != null)
            foreach (BoardPosition c in new List<BoardPosition>(mirrorMove.swept) { mirrorMove.to })
                if (liveBombs.Contains(c) && !queue.Exists(q => q.at == c)) queue.Add((c, 0, c, true));
        if (queue.Count == 0) return;
        QueenBlitzMirror look = QueenBlitzMirror.Instance;
        Transform container = queen.transform.parent;
        Vector3 World(BoardPosition c, float lift) =>
            container != null ? container.TransformPoint(ChessBoard.Instance.BoardToLocalPosition(c) + Vector3.up * ChessBoard.Instance.cellSize * lift)
                              : ChessBoard.Instance.BoardToLocalPosition(c);
        for (int i = 0; i < queue.Count; i++)
        {
            var (at, link, trigger, byMirror) = queue[i];
            liveBombs.Remove(at);
            var victims = new List<ChessPiece>();
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                var c = new BoardPosition(at.x + dx, at.y + dy, at.z + dz);
                if (!ChessBoard.Instance.IsValidPosition(c)) continue;
                if (liveBombs.Contains(c))
                {
                    if (!queue.Exists(q => q.at == c)) queue.Add((c, link + 1, trigger, byMirror)); // a chain
                    continue;
                }
                if (c == route.to) continue; // the queen takes the pawn she lands on herself
                ChessPiece pawn = ChessBoard.Instance.GetPieceAt(c);
                if (pawn != null && pawn.pieceColor == PawnColor) victims.Add(pawn);
            }
            GameObject model = QueenBlitzBombs.Instance != null ? QueenBlitzBombs.Instance.Detach(at) : null;
            if (byMirror && look != null && look.Model != null)
                PieceEffects.BombBlast(World(at, 0.3f), World(trigger, 0f), look.Model.transform, () => look != null && look.Sliding, link, model, victims);
            else PieceEffects.BombBlast(World(at, 0.3f), World(trigger, 0f), queen.transform, () => queen != null && queen.IsMoving, link, model, victims);
            foreach (ChessPiece pawn in victims)
            {
                Swept++;
                ChessBoard.Instance.RemovePieceFromBoard(pawn, pawn.CurrentPosition);
            }
            Debug.Log($"Queen Blitz: bomb at {at} went off (link {link}), {victims.Count} pawns");
        }
        BombVersion++;
    }

    /// <summary>Pawns this route's bombs would blast on top of the ones she sweeps herself (route choice and hints)</summary>
    public static int BlastCount(Route r)
    {
        if (liveBombs.Count == 0 || ChessBoard.Instance == null) return 0;
        var gone = new HashSet<BoardPosition>(r.swept) { r.to }; // (not the Mirror Queen's answer: this only ranks the queen's routes)
        var queue = new List<BoardPosition>();
        foreach (BoardPosition c in gone) if (liveBombs.Contains(c) && !queue.Contains(c)) queue.Add(c);
        var blasted = new HashSet<BoardPosition>();
        for (int i = 0; i < queue.Count; i++)
        {
            BoardPosition at = queue[i];
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                var c = new BoardPosition(at.x + dx, at.y + dy, at.z + dz);
                if (liveBombs.Contains(c)) { if (!queue.Contains(c)) queue.Add(c); continue; }
                if (gone.Contains(c) || !ChessBoard.Instance.IsValidPosition(c)) continue;
                ChessPiece pawn = ChessBoard.Instance.GetPieceAt(c);
                if (pawn != null && pawn.pieceColor == PawnColor) blasted.Add(c);
            }
        }
        return blasted.Count;
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>BlitzDemo: put the puzzle's par bombs down, as Auto-place would (for free)</summary>
    public static void DebugPlaceBombs() => PlaceBestBombs();
#endif
}

/// <summary>
/// How bombs look and how the player places them: a round neon bomb with a lit fuse on its square,
/// a faint outline of the cube its blast clears, and during setup a Bomb button that shows a dot on
/// every square one can go on. Tap a dot to put it there; tap a placed bomb to pick it up again.
/// </summary>
public class QueenBlitzBombs : MonoBehaviour
{
    public static QueenBlitzBombs Instance { get; private set; }

    private readonly Dictionary<BoardPosition, GameObject> shown = new Dictionary<BoardPosition, GameObject>();
    private readonly List<GameObject> spots = new List<GameObject>();
    private int shownVersion = -1, spotsVersion = -1;
    private bool armed;
    private static readonly Color BodyColor = new Color(0.16f, 0.07f, 0.26f);
    private static readonly Color FuseColor = new Color(1f, 0.6f, 0.15f);

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private static GameState State => GameStateManager.Instance != null ? GameStateManager.Instance.currentState : GameState.WaitingForConfiguration;

    /// <summary>The player can still place or move bombs: setup, before Ready</summary>
    private static bool Placing =>
        QueenBlitz.Active && QueenBlitz.Current.placeBombs > 0 && State == GameState.PiecePlacement
        && PlacementManager.Instance != null && !PlacementManager.Instance.IsPlayerReady(QueenBlitz.PlayerColor);

    private void Update()
    {
        if (!QueenBlitz.Active || ChessBoard.Instance == null) return;
        if (shownVersion != QueenBlitz.BombVersion) Sync();
        if (armed && (!Placing || QueenBlitz.BombsToPlace == 0
                      || (PlacementManager.Instance != null && PlacementManager.Instance.SelectedTrayColor.HasValue)))
            armed = false; // the queen picked up from the bench, or nothing left to place
        if (!armed && spots.Count > 0) ClearSpots();
        if (armed && spotsVersion != QueenBlitz.BombVersion) ShowSpots();
    }

    // ── Models ──

    private void Sync()
    {
        shownVersion = QueenBlitz.BombVersion;
        GameObject container = GameObject.Find("Pieces Container");
        if (container == null) { shownVersion = -1; return; }
        var live = new HashSet<BoardPosition>(QueenBlitz.LiveBombs);
        foreach (BoardPosition at in new List<BoardPosition>(shown.Keys))
        {
            if (live.Contains(at)) continue;
            if (shown[at] != null) Destroy(shown[at]);
            shown.Remove(at);
        }
        foreach (BoardPosition at in live)
            if (!shown.ContainsKey(at)) shown[at] = MakeBomb(container.transform, at);
    }

    /// <summary>A bomb going off: its model is handed to the blast effect, which removes it when it goes</summary>
    public GameObject Detach(BoardPosition at)
    {
        if (!shown.TryGetValue(at, out GameObject model)) return null;
        shown.Remove(at);
        return model;
    }

    private static Material Unlit(Color c) => new Material(Shader.Find("Sprites/Default")) { color = c };

    private static GameObject Part(PrimitiveType type, Transform parent, Vector3 at, Vector3 scale, Material mat, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private static GameObject MakeBomb(Transform container, BoardPosition at)
    {
        float cell = ChessBoard.Instance.cellSize;
        var root = new GameObject($"Blitz Bomb {at}").transform;
        root.SetParent(container, false);
        root.localPosition = ChessBoard.Instance.BoardToLocalPosition(at);
        root.localRotation = Quaternion.identity;

        // A round dark body with a pink sheen, a cap and a curled fuse with a spark on the end
        Material body = new Material(Shader.Find("Standard")) { color = BodyColor };
        body.EnableKeyword("_EMISSION");
        body.SetColor("_EmissionColor", NeonTheme.Pink * 0.3f);
        body.SetFloat("_Glossiness", 0.85f);
        float r = cell * 0.24f;
        GameObject ball = Part(PrimitiveType.Sphere, root, Vector3.up * (r + cell * 0.02f), Vector3.one * r * 2f, body, keepCollider: true);
        ball.AddComponent<QueenBlitzBombBody>().at = at;
        Part(PrimitiveType.Sphere, root, Vector3.up * (r + cell * 0.02f), Vector3.one * r * 2.5f, Unlit(new Color(NeonTheme.Pink.r, NeonTheme.Pink.g, NeonTheme.Pink.b, 0.12f)))
            .AddComponent<QueenBlitzBombPulse>();
        Vector3 top = Vector3.up * (2f * r + cell * 0.02f);
        Part(PrimitiveType.Cylinder, root, top, new Vector3(r * 0.55f, cell * 0.04f, r * 0.55f), Unlit(NeonTheme.Lavender));

        var fuse = new GameObject("Fuse", typeof(LineRenderer)).GetComponent<LineRenderer>();
        fuse.transform.SetParent(root, false);
        fuse.useWorldSpace = false;
        var pts = new Vector3[8];
        for (int i = 0; i < pts.Length; i++)
        {
            float t = i / (pts.Length - 1f);
            pts[i] = top + new Vector3(Mathf.Sin(t * 2.4f) * r * 0.5f, t * cell * 0.22f, (1f - Mathf.Cos(t * 2.4f)) * r * 0.2f);
        }
        fuse.positionCount = pts.Length;
        fuse.SetPositions(pts);
        fuse.widthMultiplier = cell * 0.03f;
        fuse.material = Unlit(Color.white);
        fuse.startColor = NeonTheme.Lavender;
        fuse.endColor = FuseColor;
        fuse.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Part(PrimitiveType.Sphere, root, pts[pts.Length - 1], Vector3.one * cell * 0.08f, Unlit(NeonTheme.Yellow)).AddComponent<QueenBlitzBombSpark>();

        // The cube its blast clears: a faint outline three squares across
        Outline(root, cell);
        return root.gameObject;
    }

    private static void Outline(Transform root, float cell)
    {
        float h = cell * 1.5f;
        Vector3 mid = Vector3.up * cell * 0.5f;
        Material mat = Unlit(Color.white);
        Color c = new Color(FuseColor.r, FuseColor.g, FuseColor.b, 0.22f);
        for (int axis = 0; axis < 3; axis++)
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
        {
            Vector3 from = Vector3.zero, to = Vector3.zero;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            from[axis] = -h; to[axis] = h;
            from[u] = to[u] = a * h;
            from[v] = to[v] = b * h;
            var lr = new GameObject("Blast Edge", typeof(LineRenderer)).GetComponent<LineRenderer>();
            lr.transform.SetParent(root, false);
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPositions(new[] { mid + from, mid + to });
            lr.widthMultiplier = cell * 0.025f;
            lr.material = mat;
            lr.startColor = lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // ── Placing ──

    private void ShowSpots()
    {
        ClearSpots();
        spotsVersion = QueenBlitz.BombVersion;
        GameObject container = GameObject.Find("Pieces Container");
        if (container == null) return;
        int n = QueenBlitz.Current.size;
        Material mat = Unlit(new Color(FuseColor.r, FuseColor.g, FuseColor.b, 0.7f));
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        for (int z = 0; z < n; z++)
        {
            var c = new BoardPosition(x, y, z);
            if (!QueenBlitz.CanPlaceBomb(c)) continue;
            // The same size and tap target as the queen's placement dots (PlacementManager): a dot
            // 0.8 across with a tap sphere twice its size (a small dot was finicky to hit)
            GameObject dot = Part(PrimitiveType.Sphere, container.transform, ChessBoard.Instance.BoardToLocalPosition(c) + Vector3.up * 0.5f,
                Vector3.one * 0.8f, mat, keepCollider: true);
            ((SphereCollider)dot.GetComponent<Collider>()).radius = 1.0f;
            dot.name = $"Bomb Spot {c}";
            dot.AddComponent<QueenBlitzBombSpot>().at = c;
            spots.Add(dot);
        }
    }

    private void ClearSpots()
    {
        foreach (GameObject s in spots) if (s != null) Destroy(s);
        spots.Clear();
        spotsVersion = -1;
    }

    private void Arm()
    {
        if (PlacementManager.Instance != null) PlacementManager.Instance.DeselectTrayPiece();
        armed = true;
        Haptics.Selection();
        GameSounds.Pick();
    }

    /// <summary>
    /// A tap during setup: on a bomb spot (while placing one) puts a bomb there; on a bomb the player
    /// placed picks it up to go somewhere else. True when it was one of those.
    /// </summary>
    public bool OnTap(Vector2 screen, Camera cam)
    {
        if (!Placing || cam == null) return false;
        RaycastHit[] hits = Physics.RaycastAll(cam.ScreenPointToRay(screen), Mathf.Infinity);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            QueenBlitzBombSpot spot = armed ? h.collider.GetComponent<QueenBlitzBombSpot>() : null;
            if (spot != null)
            {
                if (!QueenBlitz.PlaceBomb(spot.at)) return true;
                Haptics.Light();
                GameSounds.Placed(spot.at.y);
                if (QueenBlitz.BombsToPlace == 0) armed = false;
                return true;
            }
            QueenBlitzBombBody body = h.collider.GetComponent<QueenBlitzBombBody>();
            if (body != null && QueenBlitz.IsPlacedBomb(body.at))
            {
                QueenBlitz.PickUpBomb(body.at); // back to the bench, and ready to go down again
                Arm();
                return true;
            }
            ChessPiece piece = h.collider.GetComponent<ChessPiece>();
            if (piece != null && piece.pieceColor == QueenBlitz.PlayerColor) break; // the queen in front: that tap is for her
        }
        if (armed) armed = false; // a tap anywhere else puts the bomb back down on the bench
        return false;
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    public bool Armed => armed;
    public int SpotCount => spots.Count;
    public void DebugArm() => Arm();
#endif

    // The bomb button: left side, just above the hint line, under the pawn column and clear of the
    // board in the middle (Brandon wants it off to the left on every bomb puzzle)
    private void OnGUI()
    {
        if (!Placing) return;
        Vector2 ui = TouchGUI.Begin();
        int left = QueenBlitz.BombsToPlace;
        const float width = 150f, height = 58f;
        Rect r = new Rect(16f, ui.y - 70f - height - 8f, width, height);
        string label = left == 0 ? "Bombs placed" : armed ? "Tap a dot" : left == 1 ? "Place bomb" : $"Place bomb ×{left}";
        GUI.enabled = left > 0;
        if (TouchGUI.NeonButton(r, label, armed ? NeonTheme.Lime : FuseColor, NeonTheme.Pink, outlined: left == 0, fontSize: 15))
        {
            if (armed) armed = false;
            else Arm();
        }
        GUI.enabled = true;
        TouchGUI.Block(r);
        TouchGUI.End();
    }
}

/// <summary>A bomb's body: tapped during setup to pick a placed bomb up again</summary>
public class QueenBlitzBombBody : MonoBehaviour
{
    public BoardPosition at;
}

/// <summary>A square a bomb can be put on (shown while placing one)</summary>
public class QueenBlitzBombSpot : MonoBehaviour
{
    public BoardPosition at;
}

/// <summary>The glow round a bomb, slowly breathing</summary>
public class QueenBlitzBombPulse : MonoBehaviour
{
    private Vector3 rest;
    private void Start() => rest = transform.localScale;
    private void Update()
    {
        if (AccessibilityPrefs.ReduceMotion) return;
        transform.localScale = rest * (1f + 0.08f * Mathf.Sin(Time.time * 3f));
    }
}

/// <summary>The fuse's spark, flickering</summary>
public class QueenBlitzBombSpark : MonoBehaviour
{
    private Vector3 rest;
    private void Start() => rest = transform.localScale;
    private void Update()
    {
        if (AccessibilityPrefs.ReduceMotion) return;
        transform.localScale = rest * (0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 9f, transform.position.x));
    }
}
