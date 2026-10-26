using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How Queen Blitz portals and ramps look on the board (the rules are in QueenBlitzRoutes).
/// Portals: an upright disc per end in its pair's colour; the way in is a dark swirl turning inward
/// like a drain, with a solid rim and chevrons pointing in; the way out is a bright swirl turning
/// outward, with a rim of rays, chevrons pointing out and an arrow on the floor the way she leaves.
/// Ramps: lime slopes rising the ways she can climb, with uphill arrows; every other side is a
/// pink wall face.
/// </summary>
public partial class QueenBlitzDirector
{
    // Pair colours: none is the board's cyan, the blocks' pink or the ramps' lime
    private static readonly Color[] PairColors =
    {
        NeonTheme.Hex("B36BFF"), new Color(1f, 0.55f, 0.1f), new Color(0.95f, 0.95f, 1f), NeonTheme.Hex("3DFFB0"),
    };

    private static Material Unlit(Color color, Texture2D texture = null) =>
        new Material(Shader.Find("Sprites/Default")) { color = color, mainTexture = texture };

    private void MakeFeatures(Transform container, QueenBlitz.Puzzle puzzle)
    {
        var pairs = puzzle.portals ?? new QueenBlitz.PortalPair[0];
        for (int i = 0; i < pairs.Length; i++)
        {
            Color color = PairColors[i % PairColors.Length];
            QueenBlitz.PortalPair p = pairs[i];
            blockObjects.Add(MakePortal(container, p.a, p.faceA, color, p.twoWay ? PortalEnd.Both : PortalEnd.In));
            blockObjects.Add(MakePortal(container, p.b, p.faceB, color, p.twoWay ? PortalEnd.Both : PortalEnd.Out));
        }
        var rises = new Dictionary<BoardPosition, List<Vector3Int>>();
        foreach (QueenBlitz.RampRise r in puzzle.ramps ?? new QueenBlitz.RampRise[0])
        {
            if (!rises.TryGetValue(r.at, out var dirs)) rises[r.at] = dirs = new List<Vector3Int>();
            dirs.Add(r.dir);
        }
        foreach (var kv in rises) blockObjects.Add(MakeRamp(container, kv.Key, kv.Value));
    }

    // ── Portals ──

    private enum PortalEnd { In, Out, Both }

    private static GameObject MakePortal(Transform container, BoardPosition at, Vector3Int face, Color color, PortalEnd end)
    {
        float cell = ChessBoard.Instance.cellSize;
        var root = new GameObject($"Blitz Portal {end} {at}").transform;
        root.SetParent(container, false);
        root.localPosition = ChessBoard.Instance.BoardToLocalPosition(at) + Vector3.up * cell * 0.5f;
        root.localRotation = Quaternion.LookRotation(new Vector3(face.x, 0f, face.z), Vector3.up); // +z out of the front
        bool way_out = end != PortalEnd.In;

        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(root, false);
        disc.transform.localScale = Vector3.one * cell * 0.86f;
        disc.GetComponent<MeshRenderer>().material = Unlit(Color.white, Swirl(color, end == PortalEnd.Out));
        disc.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        disc.AddComponent<PortalSpin>().speed = end == PortalEnd.Out ? 120f : -90f;

        float r = cell * 0.45f;
        if (end == PortalEnd.Out)
        {
            for (int i = 0; i < 12; i++)
            {
                float t = i / 12f * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0f);
                Line(root, new[] { d * r * 0.98f, d * r * 1.22f }, color, cell * 0.05f, false);
            }
        }
        else Line(root, Circle(r, 48), color, cell * 0.07f, true);

        // Chevrons in front: into the way in, out of the way out (both, two-way)
        float a = cell * 0.18f, near = cell * 0.2f, far = cell * 0.45f;
        if (end != PortalEnd.Out) Chevrons(root, color, a, near, far, cell);
        if (way_out) Chevrons(root, color, a, far, near, cell);
        if (way_out)
        {
            // An arrow on the floor, the way she leaves
            float y = -cell * 0.48f;
            Line(root, new[] { new Vector3(0, y, cell * 0.05f), new Vector3(0, y, cell * 0.42f) }, color, cell * 0.06f, false);
            Line(root, new[] { new Vector3(-cell * 0.15f, y, cell * 0.28f), new Vector3(0, y, cell * 0.44f), new Vector3(cell * 0.15f, y, cell * 0.28f) }, color, cell * 0.06f, false);
        }
        root.gameObject.AddComponent<QueenBlitzBlock>().at = at; // shown with its floor
        return root.gameObject;
    }

    /// <summary>Two stacked chevrons in front of the disc; the point at `tip`, the wings at `wing` (local z)</summary>
    private static void Chevrons(Transform root, Color color, float a, float wing, float tip, float cell)
    {
        Line(root, new[] { new Vector3(-a, a * 0.9f, wing), new Vector3(0, 0, tip), new Vector3(a, a * 0.9f, wing) }, color, cell * 0.06f, false);
        Line(root, new[] { new Vector3(-a, -a * 0.1f, wing), new Vector3(0, -a, tip), new Vector3(a, -a * 0.1f, wing) }, color, cell * 0.06f, false);
    }

    private static Texture2D Swirl(Color color, bool exit)
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
                float alpha = Mathf.Clamp01((1f - r) * 6f) * Mathf.Lerp(0.45f, 1f, arm);
                Color c = Color.Lerp(color * 0.5f, Color.Lerp(color, Color.white, 0.5f), arm * (1f - r * 0.6f));
                // The way in falls away into a dark centre; the way out glows white-hot at its centre
                float core = Mathf.Clamp01(1f - r / 0.45f);
                c = exit ? Color.Lerp(c, Color.white, core * core) : Color.Lerp(c, new Color(0.03f, 0f, 0.08f), core);
                c.a = r < 1f ? Mathf.Max(alpha, core) : 0f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    // ── Ramps ──

    private static GameObject MakeRamp(Transform container, BoardPosition at, List<Vector3Int> rises)
    {
        float cell = ChessBoard.Instance.cellSize;
        var root = new GameObject($"Blitz Ramp {at}").transform;
        root.SetParent(container, false);
        root.localPosition = ChessBoard.Instance.BoardToLocalPosition(at);
        float h = cell * 0.94f, s = h / 2f;
        Color lime = NeonTheme.Lime, wall = NeonTheme.Pink;

        if (rises.Count == 1)
        {
            // A wedge rising toward its one direction; the high side is a wall
            root.localRotation = Quaternion.LookRotation(new Vector3(rises[0].x, 0f, rises[0].z), Vector3.up);
            Vector3 a = new Vector3(-s, 0, -s), b = new Vector3(s, 0, -s), c = new Vector3(s, 0, s), d = new Vector3(-s, 0, s);
            Vector3 e = new Vector3(-s, h, s), f = new Vector3(s, h, s);
            Shape(root, new[] { new[] { a, e, f, b } }, lime);                                  // the slope
            Shape(root, new[] { new[] { d, c, f, e }, new[] { a, d, e }, new[] { b, f, c } }, wall); // the walls
            Line(root, new[] { a, e, f, b, a }, lime, cell * 0.04f, false);
            Line(root, new[] { e, d, c, f }, wall, cell * 0.05f, false);
            Chevron(root, new Vector3(0, h * 0.2f, -s * 0.6f), new Vector3(0, h * 0.75f, s * 0.5f), lime, cell);
        }
        else
        {
            // A peak: a slope up each side she can climb from, a wall face on the others
            Vector3 top = new Vector3(0, h, 0);
            foreach (Vector3Int side in new[] { new Vector3Int(0, 0, -1), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0) })
            {
                Quaternion q = Quaternion.LookRotation(new Vector3(-side.x, 0f, -side.z), Vector3.up); // facing in from this side
                Vector3 l = q * new Vector3(-s, 0, -s), r = q * new Vector3(s, 0, -s);
                bool climb = rises.Contains(new Vector3Int(-side.x, 0, -side.z)); // coming in from this side, heading across
                Shape(root, new[] { new[] { l, top, r } }, climb ? lime : wall);
                Line(root, new[] { l, top }, lime, cell * 0.035f, false);
                if (climb) Chevron(root, q * new Vector3(0, h * 0.25f, -s * 0.6f), q * new Vector3(0, h * 0.6f, -s * 0.2f), lime, cell);
            }
            Line(root, new[] { new Vector3(-s, 0, -s), new Vector3(s, 0, -s), new Vector3(s, 0, s), new Vector3(-s, 0, s), new Vector3(-s, 0, -s) }, lime, cell * 0.035f, false);
        }
        root.gameObject.AddComponent<QueenBlitzBlock>().at = at;
        return root.gameObject;
    }

    private static void Chevron(Transform root, Vector3 from, Vector3 to, Color color, float cell)
    {
        Vector3 dir = (to - from).normalized, side = Vector3.Cross(dir, Vector3.up).normalized;
        if (side == Vector3.zero) side = Vector3.right;
        Vector3 lift = Vector3.Cross(side, dir).normalized * cell * 0.03f;
        float a = cell * 0.13f;
        Line(root, new[] { to - dir * a + side * a + lift, to + lift, to - dir * a - side * a + lift }, color, cell * 0.045f, false);
    }

    /// <summary>Flat-shaded faces in one colour, each drawn from both sides</summary>
    private static void Shape(Transform root, Vector3[][] faces, Color color)
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
        var go = new GameObject("Face", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root, false);
        go.GetComponent<MeshFilter>().mesh = mesh;
        Material mat = new Material(Shader.Find("Standard")) { color = color * 0.35f + new Color(0, 0, 0, 1f) };
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 0.4f);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.material = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
        lr.material = Unlit(Color.white);
        lr.startColor = lr.endColor = color;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}

/// <summary>Turns a portal's swirl (stands still with Reduce Motion)</summary>
public class PortalSpin : MonoBehaviour
{
    public float speed;

    private void Update()
    {
        if (!AccessibilityPrefs.ReduceMotion) transform.Rotate(0f, 0f, speed * Time.deltaTime, Space.Self);
    }
}
