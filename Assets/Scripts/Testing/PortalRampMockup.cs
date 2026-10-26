#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Development-only look test for Queen Blitz portals and ramps (no rules yet): on a puzzle's
/// board it puts two portal pairs (swirling, coloured, upright discs with an arrow out of the
/// front), a one-way ramp (slope up from one side, a wall on the other) and a four-way ramp.
/// `defaults write BomSapo.Chess3D DebugPortalMockup -int 1`, with BlitzDemo opening a puzzle;
/// 2 adds a close-up camera on a portal, 3 on the two ramps.
/// </summary>
public class PortalRampMockup : MonoBehaviour
{
    private readonly List<Transform> swirls = new List<Transform>();
    private readonly List<Transform> made = new List<Transform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugPortalMockup", 0) == 0) return;
        var go = new GameObject("Portal Ramp Mockup");
        DontDestroyOnLoad(go);
        go.AddComponent<PortalRampMockup>();
    }

    private bool built;

    private void Update()
    {
        foreach (Transform s in swirls) if (s != null) s.Rotate(0f, 0f, -90f * Time.deltaTime, Space.Self);
        if (built || QueenBlitzDirector.Instance == null || ChessBoard.Instance == null) return;
        var state = GameStateManager.Instance != null ? GameStateManager.Instance.currentState : GameState.WaitingForConfiguration;
        if (state != GameState.Playing && state != GameState.PiecePlacement) return;
        GameObject container = GameObject.Find("Pieces Container");
        if (container == null) return;
        built = true;
        int n = BoardDimensionsManager.Instance.GetDimensions().x;
        Transform parent = container.transform;
        // Two pairs: cyan (in on floor 2 facing +z, out on floor 4 facing -x) and orange
        Portal(parent, Free(new BoardPosition(1, 1, 0), n), Vector3.forward, NeonTheme.Cyan, "A");
        Portal(parent, Free(new BoardPosition(n - 1, 3, 2), n), Vector3.left, NeonTheme.Cyan, "A");
        Portal(parent, Free(new BoardPosition(0, 0, n - 2), n), Vector3.right, new Color(1f, 0.55f, 0.1f), "B");
        Portal(parent, Free(new BoardPosition(n - 2, 2, n - 1), n), Vector3.back, new Color(1f, 0.55f, 0.1f), "B");
        Ramp(parent, Free(new BoardPosition(2, 0, 1), n), Vector3.right, false);
        Ramp(parent, Free(new BoardPosition(2, 1, n - 2), n), Vector3.forward, true);
        Debug.Log("PORTAL MOCKUP: built");
        int view = PlayerPrefs.GetInt("DebugPortalMockup", 1);
        if (view >= 2) CloseUp(view == 2 ? made.GetRange(2, 1) : made.GetRange(4, 2));
    }

    /// <summary>The square asked for, or the nearest one on its floor without a piece</summary>
    private static BoardPosition Free(BoardPosition want, int n)
    {
        if (ChessBoard.Instance.GetPieceAt(want) == null) return want;
        for (int r = 1; r < n; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    var p = new BoardPosition(want.x + dx, want.y, want.z + dz);
                    if (p.x >= 0 && p.z >= 0 && p.x < n && p.z < n && ChessBoard.Instance.GetPieceAt(p) == null) return p;
                }
        return want;
    }

    private static Vector3 Centre(BoardPosition at)
    {
        float cell = ChessBoard.Instance.cellSize;
        return ChessBoard.Instance.BoardToLocalPosition(at) + Vector3.up * cell * 0.5f;
    }

    /// <summary>A second camera over the main one, close in on the pieces to look at</summary>
    private static void CloseUp(List<Transform> targets)
    {
        Camera main = Camera.main;
        if (main == null) return;
        Vector3 centre = Vector3.zero;
        foreach (Transform t in targets) centre += t.position;
        centre /= targets.Count;
        float spread = 0f;
        foreach (Transform t in targets) spread = Mathf.Max(spread, Vector3.Distance(t.position, centre));
        var cam = new GameObject("Mockup Close-up").AddComponent<Camera>();
        cam.CopyFrom(main);
        cam.depth = main.depth + 1;
        Vector3 from = (main.transform.position - centre).normalized;
        cam.transform.position = centre + from * (spread * 2.2f + ChessBoard.Instance.cellSize * 2.5f) + Vector3.up * ChessBoard.Instance.cellSize;
        cam.transform.LookAt(centre);
    }

    // ── Portals ──

    private void Portal(Transform parent, BoardPosition at, Vector3 facing, Color color, string pair)
    {
        float cell = ChessBoard.Instance.cellSize;
        var root = new GameObject($"Portal {pair} {at}").transform;
        made.Add(root);
        root.SetParent(parent, false);
        root.localPosition = Centre(at);
        root.localRotation = Quaternion.LookRotation(facing, Vector3.up); // +z out of the front

        // The swirl: a disc that turns, seen from both sides
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(root, false);
        disc.transform.localScale = Vector3.one * cell * 0.82f;
        disc.GetComponent<MeshRenderer>().material = new Material(Shader.Find("Sprites/Default")) { mainTexture = Swirl(color), color = Color.white };
        swirls.Add(disc.transform);

        // A bright rim
        Line(root, Circle(cell * 0.43f, 48), color, cell * 0.05f, true);
        // The way out: a chevron in front of the face
        float a = cell * 0.18f, z = cell * 0.32f;
        Line(root, new[] { new Vector3(-a, a * 0.9f, z * 0.6f), new Vector3(0, 0, z * 1.4f), new Vector3(a, a * 0.9f, z * 0.6f) }, color, cell * 0.06f, false);
        Line(root, new[] { new Vector3(-a, -a * 0.1f, z * 0.6f), new Vector3(0, -a, z * 1.4f), new Vector3(a, -a * 0.1f, z * 0.6f) }, color, cell * 0.06f, false);
    }

    private static Texture2D Swirl(Color color)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float angle = Mathf.Atan2(v, u) / (2f * Mathf.PI) + 0.5f;
                float band = Mathf.Repeat(angle * 3f + r * 2.2f, 1f);
                float arm = Mathf.SmoothStep(0.55f, 1f, Mathf.Abs(band - 0.5f) * 2f);
                float alpha = Mathf.Clamp01((1f - r) * 6f) * Mathf.Lerp(0.35f, 1f, arm);
                Color c = Color.Lerp(color * 0.5f, Color.Lerp(color, Color.white, 0.5f), arm * (1f - r * 0.6f));
                c.a = r < 1f ? alpha : 0f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    // ── Ramps ──

    /// <summary>A one-way ramp (slope rising toward `up`, a wall on the high side) or a four-way one</summary>
    private void Ramp(Transform parent, BoardPosition at, Vector3 up, bool fourWay)
    {
        float cell = ChessBoard.Instance.cellSize;
        var root = new GameObject($"Ramp {(fourWay ? "4-way" : "1-way")} {at}").transform;
        made.Add(root);
        root.SetParent(parent, false);
        root.localPosition = ChessBoard.Instance.BoardToLocalPosition(at);
        root.localRotation = Quaternion.LookRotation(up, Vector3.up);
        Color lime = NeonTheme.Lime;

        var go = new GameObject("Shape", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * cell * 0.94f;
        go.GetComponent<MeshFilter>().mesh = fourWay ? Pyramid() : Wedge();
        Material mat = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.32f, 0.1f) };
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", lime * 0.35f);
        go.GetComponent<MeshRenderer>().material = mat;

        float h = cell * 0.94f, s = h / 2f;
        if (fourWay)
        {
            // Edges from each corner to the top, and an arrow up each face
            Vector3 top = new Vector3(0, h, 0);
            foreach (Vector3 c in new[] { new Vector3(-s, 0, -s), new Vector3(s, 0, -s), new Vector3(s, 0, s), new Vector3(-s, 0, s) })
                Line(root, new[] { c, top }, lime, cell * 0.04f, false);
            Line(root, new[] { new Vector3(-s, 0, -s), new Vector3(s, 0, -s), new Vector3(s, 0, s), new Vector3(-s, 0, s), new Vector3(-s, 0, -s) }, lime, cell * 0.04f, false);
            for (int k = 0; k < 4; k++)
            {
                Quaternion q = Quaternion.Euler(0, 90f * k, 0);
                Chevron(root, q * new Vector3(0, h * 0.28f, -s * 0.55f), q * new Vector3(0, h * 0.6f, -s * 0.2f), lime, cell);
            }
        }
        else
        {
            // Low edge at the back (-z), high edge and wall at the front (+z)
            Line(root, new[] { new Vector3(-s, 0, -s), new Vector3(-s, h, s), new Vector3(s, h, s), new Vector3(s, 0, -s), new Vector3(-s, 0, -s) }, lime, cell * 0.04f, false);
            Line(root, new[] { new Vector3(-s, h, s), new Vector3(-s, 0, s), new Vector3(s, 0, s), new Vector3(s, h, s) }, NeonTheme.Pink, cell * 0.05f, false); // the wall side
            Chevron(root, new Vector3(0, h * 0.2f, -s * 0.6f), new Vector3(0, h * 0.75f, s * 0.5f), lime, cell);
        }
    }

    private static void Chevron(Transform root, Vector3 from, Vector3 to, Color color, float cell)
    {
        Vector3 dir = (to - from).normalized, side = Vector3.Cross(dir, Vector3.up).normalized;
        if (side == Vector3.zero) side = Vector3.right;
        Vector3 lift = Vector3.Cross(side, dir).normalized * cell * 0.03f;
        float a = cell * 0.13f;
        Line(root, new[] { to - dir * a + side * a + lift, to + lift, to - dir * a - side * a + lift }, color, cell * 0.045f, false);
    }

    private static Mesh Wedge()
    {
        // Unit cell, base at y 0: slope from the low back edge (z -0.5) to the top front edge (z 0.5)
        Vector3 a = new Vector3(-0.5f, 0, -0.5f), b = new Vector3(0.5f, 0, -0.5f), c = new Vector3(0.5f, 0, 0.5f), d = new Vector3(-0.5f, 0, 0.5f);
        Vector3 e = new Vector3(-0.5f, 1, 0.5f), f = new Vector3(0.5f, 1, 0.5f);
        return Faces(new[] { new[] { a, e, f, b }, new[] { d, c, f, e }, new[] { a, b, c, d }, new[] { a, d, e }, new[] { b, f, c } });
    }

    private static Mesh Pyramid()
    {
        Vector3 a = new Vector3(-0.5f, 0, -0.5f), b = new Vector3(0.5f, 0, -0.5f), c = new Vector3(0.5f, 0, 0.5f), d = new Vector3(-0.5f, 0, 0.5f), t = new Vector3(0, 1, 0);
        return Faces(new[] { new[] { a, t, b }, new[] { b, t, c }, new[] { c, t, d }, new[] { d, t, a }, new[] { a, b, c, d } });
    }

    /// <summary>Flat-shaded faces, each drawn from both sides</summary>
    private static Mesh Faces(Vector3[][] faces)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        foreach (Vector3[] face in faces)
            for (int side = 0; side < 2; side++)
            {
                int start = verts.Count;
                for (int i = 0; i < face.Length; i++) verts.Add(face[side == 0 ? i : face.Length - 1 - i]);
                for (int i = 1; i < face.Length - 1; i++) { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
            }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    // ── Lines ──

    private static Vector3[] Circle(float r, int segments)
    {
        var pts = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments * Mathf.PI * 2f;
            pts[i] = new Vector3(Mathf.Cos(t) * r, Mathf.Sin(t) * r, 0f);
        }
        return pts;
    }

    private static void Line(Transform parent, Vector3[] points, Color color, float width, bool loop)
    {
        var go = new GameObject("Line", typeof(LineRenderer));
        go.transform.SetParent(parent, false);
        var lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = loop;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
        lr.widthMultiplier = width * parent.lossyScale.x;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = color;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
#endif
