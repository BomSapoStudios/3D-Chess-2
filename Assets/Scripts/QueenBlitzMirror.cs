using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Mirror Queen (Brandon, Oct 30). This mirrors Tools/QueenBlitz/solver.py (Board.mirror_follow
/// and Board.pair_moves) exactly:
/// - She starts on the mirror image of the queen's square (flipped on the puzzle's axes, all three
///   by default: through the cube's centre). A queen square whose mirror image is a block, a portal,
///   a ramp, a pawn or the square itself can't be picked.
/// - Each queen move she sets off the opposite way (each flipped axis of the direction reversed) and
///   works out her own move against the board: on a normal floor as many squares as the queen went
///   unless something stops her sooner, on ice as far as she can slide. She goes through portals and
///   up ramps the same way, and sweeps every pawn she passes or stops on.
/// - Each queen is a block to the other: the queen moves first and can't pass the mirror; the mirror
///   then can't pass where the queen stopped.
/// She isn't a piece: nothing can tap or pick her up, and she isn't on the board's squares. Her square
/// is kept here (and in Undo and saved games); null means she's still on her start, opposite the queen.
/// </summary>
public static partial class QueenBlitz
{
    /// <summary>Where the Mirror Queen stands (null: on her start, the queen's mirror image)</summary>
    public static BoardPosition? MirrorAt { get; set; }

    public static bool HasMirror => Active && Current.mirror;

    public static BoardPosition Reflect(BoardPosition c)
    {
        int n = Current.size - 1;
        Vector3Int f = Current.mirrorFlip;
        return new BoardPosition(f.x != 0 ? n - c.x : c.x, f.y != 0 ? n - c.y : c.y, f.z != 0 ? n - c.z : c.z);
    }

    public static Vector3Int Flip(Vector3Int d)
    {
        Vector3Int f = Current.mirrorFlip;
        return new Vector3Int(f.x != 0 ? -d.x : d.x, f.y != 0 ? -d.y : d.y, f.z != 0 ? -d.z : d.z);
    }

    /// <summary>The Mirror Queen's square with the queen on `queenAt`</summary>
    public static BoardPosition MirrorSquare(BoardPosition queenAt) => MirrorAt ?? Reflect(queenAt);

    /// <summary>With a Mirror Queen, the queen can't start here: the mirror's square would be taken (or the same square)</summary>
    private static bool MirrorForbids(BoardPosition p)
    {
        int n = Current.size;
        if (!Current.mirror || p.x < 0 || p.y < 0 || p.z < 0 || p.x >= n || p.y >= n || p.z >= n) return false;
        BoardPosition m = Reflect(p);
        return m == p || Solid(m) || System.Array.IndexOf(Current.pawns, m) >= 0;
    }

    /// <summary>The Mirror Queen's move answering the queen's from → to (route: the queen's, if known).
    /// Null when she can't move at all.</summary>
    public static Route MirrorRoute(BoardPosition from, BoardPosition to, Route queenRoute)
    {
        BoardPosition m = MirrorSquare(from);
        Vector3Int d0;
        int steps;
        if (queenRoute != null) { d0 = queenRoute.dir; steps = queenRoute.steps; }
        else
        {
            d0 = new Vector3Int(System.Math.Sign(to.x - from.x), System.Math.Sign(to.y - from.y), System.Math.Sign(to.z - from.z));
            steps = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y), Mathf.Abs(to.z - from.z));
        }
        Vector3Int d = Flip(d0);

        // Step() lights the portals the held queen can go into: keep that as it was
        var lit = new List<BoardPosition>(EnterablePortals);
        alsoBlocked = to; // she can't pass where the queen stopped
        var route = new Route { from = m, to = m, dir = d };
        route.Point(Floor(m));
        BoardPosition c = m;
        Route last = null;
        int n = Current.size;
        for (int k = 0; k < 4 * n * n && (Current.ice || k < steps); k++)
        {
            if (last != null) { route.swept.Add(c); route.sweptDir.Add(d); } // carrying on past a stop
            if (!Step(c, d, route, out BoardPosition landing, out Vector3Int heading)) break;
            c = landing;
            d = heading;
            if (c == m) break; // a portal loop back to the start
            last = route.Copy();
            last.to = c;
            last.steps = k + 1;
        }
        alsoBlocked = null;
        EnterablePortals.Clear();
        EnterablePortals.UnionWith(lit);
        return last;
    }

    /// <summary>The queen is moving from → to: the Mirror Queen answers, sweeping her pawns (called from SweepPath)</summary>
    private static void MoveMirror(BoardPosition from, BoardPosition to, Route queenRoute)
    {
        BoardPosition start = MirrorSquare(from);
        Route r = MirrorRoute(from, to, queenRoute);
        MirrorAt = r != null ? r.to : start;
        if (r == null) return; // boxed in: she stays put
        QueenBlitzMirror look = QueenBlitzMirror.Instance;
        Transform container = look != null && look.Model != null ? look.Model.transform.parent : null;
        for (int i = 0; i <= r.swept.Count; i++)
        {
            BoardPosition p = i < r.swept.Count ? r.swept[i] : r.to;
            Vector3Int d = i < r.swept.Count ? r.sweptDir[i] : (r.sweptDir.Count > 0 ? r.sweptDir[r.sweptDir.Count - 1] : r.dir);
            ChessPiece pawn = ChessBoard.Instance.GetPieceAt(p);
            if (pawn == null || pawn.pieceColor != PawnColor) continue;
            Swept++;
            Vector3 local = new Vector3(d.x, d.y, d.z);
            Vector3 world = container != null ? container.TransformDirection(local) : local;
            if (look != null && look.Model != null) PieceEffects.KnockAway(pawn, look.Model.transform, () => look != null && look.Sliding, world, Current.ice);
            ChessBoard.Instance.RemovePieceFromBoard(pawn, p);
        }
        if (look != null) look.Slide(r);
    }
}

/// <summary>
/// How the Mirror Queen looks: a copy of the queen's model, glassy lime, standing on QueenBlitz.MirrorAt
/// (on the queen's mirror image while she's being placed) and sliding when the queen moves.
/// </summary>
public class QueenBlitzMirror : MonoBehaviour
{
    public static QueenBlitzMirror Instance { get; private set; }

    public GameObject Model { get; private set; }
    public bool Sliding { get; private set; }

    private Coroutine slide;
    private BoardPosition slidingTo;
    private ChessPiece queen;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Model != null) Destroy(Model);
    }

    /// <summary>The queen on the board (not on the bench)</summary>
    private ChessPiece Queen()
    {
        if (queen != null && !queen.IsInTray() && queen.CurrentPosition.IsValid() && ChessBoard.Instance.GetPieceAt(queen.CurrentPosition) == queen) return queen;
        queen = null;
        foreach (ChessPiece p in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
            if (p.pieceType == ChessPieceType.Queen && p.pieceColor == QueenBlitz.PlayerColor && !p.IsInTray() && p.CurrentPosition.IsValid()
                && ChessBoard.Instance.GetPieceAt(p.CurrentPosition) == p) { queen = p; break; }
        return queen;
    }

    private void Update()
    {
        if (!QueenBlitz.HasMirror || ChessBoard.Instance == null || GameStateManager.Instance == null) return;
        GameState state = GameStateManager.Instance.currentState;
        ChessPiece q = Queen();
        BoardPosition? at = null;
        if (state == GameState.PiecePlacement)
        {
            QueenBlitz.MirrorAt = null;
            if (q != null) at = QueenBlitz.Reflect(q.CurrentPosition);
        }
        else if (state == GameState.Playing || state == GameState.GameOver)
        {
            if (QueenBlitz.MirrorAt == null && q != null && !q.IsMoving && state == GameState.Playing) QueenBlitz.MirrorAt = QueenBlitz.Reflect(q.CurrentPosition);
            at = QueenBlitz.MirrorAt;
        }

        if (Model == null && q != null && !q.IsMoving) Build(q);
        if (Model == null) return;
        // Undo or a restored game put her somewhere else mid-slide: stop and stand there
        if (Sliding && at != slidingTo) StopSlide();
        if (Sliding) return;
        Model.SetActive(at.HasValue);
        if (at.HasValue) Model.transform.localPosition = ChessBoard.Instance.BoardToLocalPosition(at.Value);
    }

    private static readonly Color Glass = new Color(0.85f, 1f, 0.75f);

    /// <summary>A copy of the queen's look, tinted so she reads as the other queen</summary>
    private void Build(ChessPiece q)
    {
        GameObject container = GameObject.Find("Pieces Container");
        if (container == null) return;
        Model = PieceEffects.ModelCopy(q);
        Model.name = "Mirror Queen";
        Model.transform.SetParent(container.transform, true);
        foreach (MeshRenderer r in Model.GetComponentsInChildren<MeshRenderer>())
        {
            Material[] mats = r.materials; // her own copies
            foreach (Material m in mats)
            {
                if (m.HasProperty("_Color")) m.color = Color.Lerp(m.color, Glass, 0.6f);
                if (m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", NeonTheme.Lime * 0.6f);
                }
            }
            r.materials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    public void Slide(QueenBlitz.Route route)
    {
        if (Model == null) return;
        StopSlide();
        Model.SetActive(true);
        slidingTo = route.to;
        Sliding = true;
        slide = StartCoroutine(SlideRoutine(route));
    }

    private void StopSlide()
    {
        if (slide != null) StopCoroutine(slide);
        slide = null;
        Sliding = false;
        if (Model != null) PieceEffects.SetSquash(Model.transform, 1f, 1f);
    }

    /// <summary>Along her route like the queen: eased on a plain slide, an even pace through portals and up ramps</summary>
    private IEnumerator SlideRoutine(QueenBlitz.Route route)
    {
        Transform t = Model.transform;
        Transform parent = t.parent;
        float cell = ChessBoard.Instance.cellSize;
        bool ice = QueenBlitz.Current.ice;
        if (!route.special && route.points.Count == 2)
        {
            Vector3 a = route.points[0], b = route.points[1];
            float duration = Mathf.Clamp(0.25f + Vector3.Distance(a, b) / cell * 0.14f, 0.35f, 1.1f); // ChessPiece.SlideCoroutine's pace
            Vector3 worldDir = parent != null ? parent.TransformDirection(b - a) : b - a;
            float nextTrail = 0f;
            for (float time = 0f; time < duration; time += Time.deltaTime)
            {
                float u = Mathf.Clamp01(time / duration);
                float k = ice ? 1f - (1f - u) * (1f - u) : u * u * (3f - 2f * u);
                t.localPosition = Vector3.Lerp(a, b, k);
                float speed = ice ? 2f * (1f - u) : 6f * u * (1f - u);
                if (!AccessibilityPrefs.ReduceMotion && time >= nextTrail && speed > 0.25f)
                {
                    nextTrail = time + 0.035f;
                    PieceEffects.SlideTrail(t.position, worldDir, ice);
                }
                yield return null;
            }
        }
        else
        {
            const float perCell = 0.13f, through = 0.14f;
            for (int i = 0; i + 1 < route.points.Count; i++)
            {
                Vector3 a = route.points[i], b = route.points[i + 1];
                if (route.teleport[i])
                {
                    for (float time = 0f; time < through; time += Time.deltaTime) { float k = 1f - time / through; PieceEffects.SetSquash(t, k, k); yield return null; }
                    t.localPosition = b;
                    for (float time = 0f; time < through; time += Time.deltaTime) { float k = time / through; PieceEffects.SetSquash(t, k, k); yield return null; }
                    PieceEffects.SetSquash(t, 1f, 1f);
                    continue;
                }
                float duration = Mathf.Max(0.06f, Vector3.Distance(a, b) / cell * perCell);
                Vector3 worldDir = parent != null ? parent.TransformDirection(b - a) : b - a;
                float nextTrail = 0f;
                for (float time = 0f; time < duration; time += Time.deltaTime)
                {
                    t.localPosition = Vector3.Lerp(a, b, time / duration);
                    if (!AccessibilityPrefs.ReduceMotion && time >= nextTrail)
                    {
                        nextTrail = time + 0.035f;
                        PieceEffects.SlideTrail(t.position, worldDir, ice);
                    }
                    yield return null;
                }
            }
        }
        t.localPosition = ChessBoard.Instance.BoardToLocalPosition(route.to);
        Sliding = false;
        slide = null;
    }
}
