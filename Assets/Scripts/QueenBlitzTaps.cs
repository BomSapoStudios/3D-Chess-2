using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Making Queen Blitz moves easy to see and start when portals are about:
/// - While the queen is picked up, a faint dotted trail runs along each move to where she'll stop:
///   every move on ice, and on other floors the moves that go through a portal or up a ramp. Into a
///   portal's way in, out of its partner, on to the landing.
/// - A tap on a portal (or the pad in front of its way in) chooses the move through it: played at
///   once if only one move goes in, otherwise those moves' trails and landings light up to pick from.
/// - On ice, a tap on a square she only slides past lights up that move's trail and landing; a tap
///   on the landing then plays it.
/// </summary>
public class QueenBlitzTrails : MonoBehaviour
{
    public static QueenBlitzTrails Instance { get; private set; }

    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private readonly HashSet<BoardPosition> lit = new HashSet<BoardPosition>(); // landings of the moves picked out
    private ChessPiece shownFor;
    private BoardPosition shownAt;
    private int shownLit = -1;
    private Material dotted;
    private float worldCell = 1f;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private static ChessPiece HeldQueen()
    {
        ChessPiece held = InputManager.Instance != null ? InputManager.Instance.SelectedPiece : null;
        bool can = GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.Playing;
        return can && held != null && held.pieceType == ChessPieceType.Queen && !held.IsInTray() ? held : null;
    }

    private void Update()
    {
        ChessPiece queen = HeldQueen();
        if (queen == null)
        {
            if (shownFor != null || lines.Count > 0) Clear();
            lit.Clear();
            return;
        }
        if (queen != shownFor || queen.CurrentPosition != shownAt || lit.Count != shownLit) Build(queen);
    }

    private void Clear()
    {
        foreach (LineRenderer l in lines) if (l != null) Destroy(l.gameObject);
        lines.Clear();
        rest.Clear();
        shownFor = null;
        shownLit = -1;
    }

    private void Build(ChessPiece queen)
    {
        Clear();
        shownFor = queen;
        shownAt = queen.CurrentPosition;
        shownLit = lit.Count;
        GameObject container = GameObject.Find("Pieces Container");
        if (container == null || ChessBoard.Instance == null) return;
        worldCell = Vector3.Distance(container.transform.TransformPoint(ChessBoard.Instance.BoardToLocalPosition(new BoardPosition(0, 0, 0))),
                                     container.transform.TransformPoint(ChessBoard.Instance.BoardToLocalPosition(new BoardPosition(1, 0, 0))));
        bool ice = QueenBlitz.Current.ice;
        foreach (QueenBlitz.Route r in QueenBlitz.RoutesFrom(queen.CurrentPosition))
        {
            bool bright = lit.Contains(r.to);
            if (!ice && !r.special && !bright) continue;
            if (lit.Count > 0 && !bright && !ice) continue;
            Draw(container.transform, r, bright, lit.Count > 0 && !bright);
        }
        HighlightDots();
    }

    private void Draw(Transform container, QueenBlitz.Route r, bool bright, bool dim)
    {
        float cell = ChessBoard.Instance.cellSize;
        Vector3 lift = Vector3.up * cell * 0.18f;
        var run = new List<Vector3> { container.TransformPoint(r.points[0] + lift) };
        for (int i = 0; i + 1 < r.points.Count; i++)
        {
            if (r.teleport[i]) { Line(run, bright, dim); run = new List<Vector3>(); }
            run.Add(container.TransformPoint(r.points[i + 1] + lift));
        }
        Line(run, bright, dim);
    }

    private void Line(List<Vector3> pts, bool bright, bool dim)
    {
        if (pts.Count < 2) return;
        if (dotted == null) dotted = new Material(Shader.Find("Sprites/Default")) { mainTexture = DotTexture() };
        float cell = worldCell;
        var go = new GameObject("Blitz Trail", typeof(LineRenderer));
        go.transform.SetParent(transform, false);
        var lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = pts.Count;
        lr.SetPositions(pts.ToArray());
        lr.material = dotted;
        lr.textureMode = LineTextureMode.Tile;
        float width = cell * (bright ? 0.11f : 0.07f);
        lr.widthMultiplier = width;
        lr.textureScale = new Vector2(1f / (cell * 0.3f) * width, 1f); // a dot every third of a square
        Color c = bright ? NeonTheme.Yellow : NeonTheme.Lavender;
        c.a = bright ? 0.95f : dim ? 0.15f : 0.45f;
        lr.startColor = lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lines.Add(lr);
    }

    private static Texture2D DotTexture()
    {
        const int w = 32, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - w / 2f) / (h / 2f), dy = (y + 0.5f - h / 2f) / (h / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((0.8f - r) * 6f)));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>The landing dots of the moves picked out grow; the rest shrink back</summary>
    private void HighlightDots()
    {
        foreach (MoveTargetData dot in FindObjectsByType<MoveTargetData>(FindObjectsSortMode.None))
        {
            Transform t = dot.transform;
            if (!rest.ContainsKey(t)) rest[t] = t.localScale;
            t.localScale = rest[t] * (lit.Count > 0 && lit.Contains(dot.targetPosition) ? 1.6f : 1f);
        }
    }

    private readonly Dictionary<Transform, Vector3> rest = new Dictionary<Transform, Vector3>();

    // ── Taps ──

    /// <summary>
    /// A tap while the queen is picked up that didn't land on a dot: a portal, its pad, or (on ice) a
    /// square she slides past. True when it was one of those (and has been dealt with).
    /// </summary>
    public bool OnTap(Vector2 screen, Camera cam)
    {
        ChessPiece queen = HeldQueen();
        if (queen == null || cam == null) return false;
        GameObject container = GameObject.Find("Pieces Container");
        if (container == null) return false;
        float cell = ChessBoard.Instance.cellSize;
        var routes = new List<QueenBlitz.Route>(QueenBlitz.RoutesFrom(queen.CurrentPosition));

        // How close on screen a square's centre has to be: about half a square at that depth
        Vector3 Screen(BoardPosition c) => cam.WorldToScreenPoint(container.transform.TransformPoint(ChessBoard.Instance.BoardToLocalPosition(c) + Vector3.up * cell * 0.5f));
        float Reach(BoardPosition c)
        {
            Vector3 a = Screen(c);
            Vector3 b = cam.WorldToScreenPoint(container.transform.TransformPoint(ChessBoard.Instance.BoardToLocalPosition(c) + new Vector3(cell * 0.5f, cell * 0.5f, 0f)));
            return Mathf.Max(24f, Vector2.Distance(a, b));
        }

        // A portal or the pad before its way in: the moves that go into it
        BoardPosition? portal = null;
        float best = float.MaxValue;
        foreach (BoardPosition p in QueenBlitz.PortalSquares)
        {
            var spots = new List<BoardPosition> { p };
            BoardPosition? pad = QueenBlitz.PortalApproach(p);
            if (pad.HasValue) spots.Add(pad.Value);
            foreach (BoardPosition s in spots)
            {
                Vector3 sp = Screen(s);
                if (sp.z <= 0f) continue;
                float d = Vector2.Distance(sp, screen);
                if (d < Reach(s) && d < best) { best = d; portal = p; }
            }
        }
        if (portal.HasValue)
        {
            var through = routes.FindAll(r => r.entered.Contains(portal.Value));
            if (through.Count == 0) return false; // she can't get into it from here
            if (through.Count == 1) { Haptics.Selection(); lit.Clear(); InputManager.Instance.PlayBlitzMove(through[0].to); return true; }
            Pick(through);
            return true;
        }

        // Ice: a square she only slides past picks out that move
        if (!QueenBlitz.Current.ice) return false;
        QueenBlitz.Route passing = null;
        best = float.MaxValue;
        foreach (QueenBlitz.Route r in routes)
            foreach (BoardPosition s in r.swept)
            {
                Vector3 sp = Screen(s);
                if (sp.z <= 0f) continue;
                float d = Vector2.Distance(sp, screen);
                if (d < Reach(s) && d < best) { best = d; passing = r; }
            }
        if (passing == null) return false;
        Pick(new List<QueenBlitz.Route> { passing });
        return true;
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    public int LitCount => lit.Count;

    /// <summary>Tap the middle of a square on screen (BlitzDemo)</summary>
    public bool DebugTap(BoardPosition c)
    {
        Camera cam = Camera.main;
        GameObject container = GameObject.Find("Pieces Container");
        Vector3 w = container.transform.TransformPoint(ChessBoard.Instance.BoardToLocalPosition(c) + Vector3.up * ChessBoard.Instance.cellSize * 0.5f);
        return OnTap(cam.WorldToScreenPoint(w), cam);
    }
#endif

    private void Pick(List<QueenBlitz.Route> routes)
    {
        Haptics.Selection();
        lit.Clear();
        foreach (QueenBlitz.Route r in routes) lit.Add(r.to);
    }
}
