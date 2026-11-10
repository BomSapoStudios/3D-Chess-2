using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Gladiator Arena backdrop: the board floats high over the sand of a great oval colosseum at
// sunset. Built so that every way you turn shows something different:
//
//   ahead of White  (~42°)   the Emperor's Box: porphyry and gold, a canopy on marble columns
//                            with a golden eagle, the emperor on his throne (thumb up now and
//                            then), guards, braziers, rose petals, and a purple carpet of honour
//                            laid up the stands behind it. Musicians with great curled horns and
//                            a water organ off to its right.
//   White's left    (~92°)   the ruined side: the stands have fallen in, a few broken arches of
//                            the outer wall stand black against the setting sun, and through the
//                            gap lies the countryside: hills, cypresses, an aqueduct, a hill
//                            temple and a little town. Golden dust hangs in the air.
//   ahead of Black  (~218°)  the Great Gate: a monumental arch, portcullis up and torchlight in
//                            the tunnel, flanked by two colossal bronze gladiators. Dropped
//                            shields on the sand. A lion in its cage rides the lift up through a
//                            trapdoor nearby, paces, and sinks again.
//   White's right   (~280°)  the sunlit side under the striped awnings, packed with the rival
//                            Blues and Greens waving their flags; weapon racks on the sand. The
//                            moon rises over it and the first stars come out.
//
// The crowd does the wave round the stands, white birds wheel overhead and pennants flutter on
// the masts. Angles are measured from White's forward (+x) toward White's left (+z), as
// atan2(z, x). Everything is built from code into a few merged meshes coloured from one small
// palette texture, so the stadium and its ~8,000 spectators cost only a handful of draw calls.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural sunset sky over the arena. Pure C# so it can run on a worker thread.</summary>
public static class ArenaSky
{
    /// <summary>The far haze, matched by the fog so the countryside melts into the sunset</summary>
    public static readonly Color HazeColor = new Color(0.86f, 0.6f, 0.44f);

    public const float SunDeg = 92f, SunElev = 5f, MoonDeg = 280f, MoonElev = 24f;

    /// <summary>World direction for a compass angle (from +x toward +z) and an elevation, in degrees</summary>
    public static Vector3 Dir(float deg, float elev)
    {
        float a = deg * Mathf.Deg2Rad, e = elev * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a));
    }

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        float off = (seed % 997) * 0.173f;
        Vector3 sun = Dir(SunDeg, SunElev), moon = Dir(MoonDeg, MoonElev);
        Vector3 moonShade = Dir(MoonDeg, MoonElev - 0.8f);   // the dark disc that leaves a crescent lit on top, toward the sun over the zenith

        // Far hazy mountains along the horizon, as heights in degrees per column
        var ridge = new float[width];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float a = u * Mathf.PI * 2f;
            ridge[x] = Mathf.Max(0.2f, 0.5f + 0.45f * Mathf.Sin(a * 4f + 1.3f) + 0.3f * Mathf.Sin(a * 11f + 0.4f) + 0.9f * Seam(u, 0.3f, 9f, 1f, off));
        }

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float e = (0.5f - v) * 180f;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float deg = (0.5f - u) * 360f;   // the panoramic skybox puts +x in the middle and turns toward +z leftward
                Vector3 d = Dir(deg, e);
                float az = Mathf.Abs(Mathf.DeltaAngle(deg, SunDeg));
                float warm = Mathf.Exp(-(az * az) / (75f * 75f));   // 1 on the sun's side of the sky, 0 opposite
                float sunAng = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, sun), -1f, 1f)) * Mathf.Rad2Deg;
                Color c;
                if (e > -0.5f)
                {
                    // Indigo overhead, deepening away from the sun; gold at the sun's horizon, rose-lilac opposite
                    float h = Mathf.Clamp01(1f - Mathf.Max(e, 0f) / 90f);
                    Color zenith = Color.Lerp(new Color(0.1f, 0.08f, 0.26f), new Color(0.2f, 0.2f, 0.44f), warm);
                    Color low = Color.Lerp(new Color(0.62f, 0.4f, 0.62f), new Color(1f, 0.62f, 0.32f), warm);
                    c = Color.Lerp(zenith, low, Mathf.Pow(h, 5f));
                    c = Color.Lerp(c, Color.Lerp(new Color(0.85f, 0.5f, 0.6f), new Color(1f, 0.8f, 0.5f), warm), Mathf.Pow(h, 22f) * 0.7f);
                    c += new Color(1f, 0.6f, 0.3f) * (0.45f * Mathf.Exp(-sunAng * sunAng / 500f)) + new Color(1f, 0.85f, 0.6f) * (0.6f * Mathf.Exp(-sunAng * sunAng / 30f));

                    // The first stars, on the side away from the sun
                    float night = (1f - warm) * Mathf.Clamp01((e - 10f) / 25f);
                    if (night > 0.02f)
                    {
                        float s = (Hash(x, y, seed) & 0xffff) / 65535f;
                        if (s > 0.9965f) c += Color.white * (night * ((s - 0.9965f) / 0.0035f) * 0.9f);
                    }

                    // Long streaks of cloud: gold and pink near the sun, lavender away from it
                    float band = Mathf.Clamp01(e / 4f) * Mathf.Clamp01((38f - e) / 18f);
                    if (band > 0f)
                    {
                        float n0 = Seam(u, v, 5f, 46f, off);
                        float n = n0 * 0.6f + Seam(u, v, 13f, 110f, off + 21f) * 0.4f;
                        float cloud = Mathf.Clamp01((n - 0.5f) * 4.5f) * band;
                        if (cloud > 0f)
                        {
                            float under = Mathf.Clamp01((n0 - Seam(u, v - 0.004f, 5f, 46f, off)) * 12f + 0.5f);
                            Color lit = Color.Lerp(new Color(0.58f, 0.42f, 0.62f), new Color(1f, 0.55f, 0.42f), warm);
                            Color rim = Color.Lerp(new Color(0.9f, 0.6f, 0.7f), new Color(1f, 0.85f, 0.55f), warm);
                            Color cc = Color.Lerp(lit, rim, under * (0.4f + 0.6f * warm));
                            cc += new Color(1f, 0.7f, 0.4f) * (0.5f * Mathf.Exp(-sunAng * sunAng / 200f));
                            c = Color.Lerp(c, cc, cloud * 0.85f);
                        }
                    }

                    // The moon: a pale crescent with a faint halo
                    float moonAng = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, moon), -1f, 1f)) * Mathf.Rad2Deg;
                    c += new Color(0.5f, 0.5f, 0.6f) * (0.25f * Mathf.Exp(-moonAng * moonAng / 20f));
                    if (moonAng < 1.8f)
                    {
                        float shadeAng = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, moonShade), -1f, 1f)) * Mathf.Rad2Deg;
                        Color face = new Color(0.98f, 0.95f, 0.85f) * (0.9f + 0.1f * Mathf.PerlinNoise(u * 900f, v * 900f));
                        if (shadeAng < 1.65f) face = Color.Lerp(face, c, 0.85f);
                        c = Color.Lerp(c, face, Mathf.Clamp01((1.8f - moonAng) / 0.25f));
                    }

                    // The sun, low and big, just above the hills
                    if (sunAng < 2.6f) c = Color.Lerp(c, new Color(1f, 0.95f, 0.78f), Mathf.Clamp01((2.6f - sunAng) / 0.35f));

                    // Far hazy mountains with a sunlit rim on the sun's side
                    float rh = ridge[x];
                    if (e < rh)
                    {
                        Color hill = Color.Lerp(new Color(0.42f, 0.36f, 0.55f), new Color(0.7f, 0.45f, 0.42f), warm);
                        hill = Color.Lerp(hill, low, 0.35f);
                        c = Color.Lerp(hill, new Color(1f, 0.8f, 0.5f), warm * Mathf.Clamp01(1f - (rh - e) * 6f) * 0.6f);
                    }
                }
                else
                {
                    Color haze = Color.Lerp(new Color(0.5f, 0.38f, 0.5f), HazeColor, warm);
                    c = Color.Lerp(haze, new Color(0.22f, 0.2f, 0.16f), Mathf.Clamp01(-e / 15f));
                }
                result[(height - 1 - y) * width + x] = new Color32(B(c.r), B(c.g), B(c.b), 255); // Unity textures start at the bottom row
            }
        }
        return result;
    }

    // Perlin noise that wraps round the sky with no seam where u goes from 1 back to 0
    private static float Seam(float u, float v, float su, float sv, float off) =>
        Mathf.PerlinNoise(u * su + off, v * sv) * (1f - u) + Mathf.PerlinNoise((u - 1f) * su + off, v * sv) * u;

    private static uint Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}

/// <summary>A place to build at: x along, y up, z forward, all times a scale</summary>
public struct ArenaBasis
{
    public Vector3 o, r, u, f;
    public float s;
    public Quaternion rot;

    public ArenaBasis(Vector3 origin, Vector3 forward, float scale)
    {
        o = origin;
        u = Vector3.up;
        f = new Vector3(forward.x, 0f, forward.z).normalized;
        r = Vector3.Cross(u, f);
        s = scale;
        rot = Quaternion.LookRotation(f, u);
    }

    public Vector3 P(float x, float y, float z) => o + (r * x + u * y + f * z) * s;
    public Vector3 P(Vector3 v) => P(v.x, v.y, v.z);
    public Matrix4x4 M(Vector3 pos, Vector3 size, Vector3 euler) => Matrix4x4.TRS(P(pos), rot * Quaternion.Euler(euler), size * s);
    public Matrix4x4 M(float x, float y, float z, float sx, float sy, float sz) => M(new Vector3(x, y, z), new Vector3(sx, sy, sz), Vector3.zero);
    public Matrix4x4 M(float x, float y, float z, float sx, float sy, float sz, float ex, float ey, float ez) => M(new Vector3(x, y, z), new Vector3(sx, sy, sz), new Vector3(ex, ey, ez));

    /// <summary>A basis at pos in this one, turned yaw degrees and scaled</summary>
    public ArenaBasis Sub(float x, float y, float z, float yaw, float scale) =>
        new ArenaBasis(P(x, y, z), rot * Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, s * scale);

    /// <summary>The same axes and scale at the world origin, for parts that move on their own</summary>
    public ArenaBasis Local => new ArenaBasis(Vector3.zero, f, s);
}

/// <summary>Every colour the arena uses, one small cell each in a single texture</summary>
public class ArenaPalette
{
    private const int Size = 256, Cell = 4, PerRow = Size / Cell;
    private readonly Dictionary<int, int> index = new Dictionary<int, int>();
    private readonly List<Color32> colors = new List<Color32>();
    public readonly Texture2D Texture;

    public ArenaPalette()
    {
        Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "Arena Palette",
        };
    }

    public Vector2 UV(Color c)
    {
        Color32 q = c;
        int key = (q.r >> 2) << 12 | (q.g >> 2) << 6 | (q.b >> 2);
        if (!index.TryGetValue(key, out int i))
        {
            if (colors.Count >= PerRow * PerRow) i = 0;
            else { i = colors.Count; colors.Add(q); }
            index[key] = i;
        }
        return new Vector2(((i % PerRow) * Cell + Cell * 0.5f) / Size, ((i / PerRow) * Cell + Cell * 0.5f) / Size);
    }

    /// <summary>Paint the cells, once everything is built</summary>
    public void Apply()
    {
        var px = new Color32[Size * Size];
        for (int i = 0; i < colors.Count; i++)
        {
            int cx = (i % PerRow) * Cell, cy = (i / PerRow) * Cell;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                    px[(cy + y) * Size + cx + x] = colors[i];
        }
        Texture.SetPixels32(px);
        Texture.Apply(false);
    }
}

/// <summary>Collects flat-shaded faces into one mesh, each face a palette colour</summary>
public class ArenaKit
{
    public const int PX = 1, NX = 2, PY = 4, NY = 8, PZ = 16, NZ = 32, All = 63;
    /// <summary>The faces anyone in the arena can see of a figure facing the board: no back, no bottom</summary>
    public const int Seen = PX | NX | PY | PZ;

    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> tris = new List<int>();
    private readonly ArenaPalette palette;

    public ArenaKit(ArenaPalette palette) { this.palette = palette; }

    public bool Empty => verts.Count == 0;

    /// <summary>
    /// A four-cornered face, corners clockwise seen from the front. With a facing direction it
    /// turns itself round if needed so its front looks that way.
    /// </summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 facing = default)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(c - a, d - a);
        if (facing != Vector3.zero && Vector3.Dot(n, facing) < 0f) { Vector3 tmp = b; b = d; d = tmp; n = -n; }
        n.Normalize();
        Vector2 uv = palette.UV(col);
        int k = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        for (int i = 0; i < 4; i++) { normals.Add(n); uvs.Add(uv); }
        tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
        tris.Add(k); tris.Add(k + 2); tris.Add(k + 3);
    }

    /// <summary>A face that shows from both sides (cloth, flags)</summary>
    public void Quad2(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        Quad(a, b, c, d, col);
        Quad(a, d, c, b, col);
    }

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 facing = default)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (facing != Vector3.zero && Vector3.Dot(n, facing) < 0f) { Vector3 tmp = b; b = c; c = tmp; n = -n; }
        n.Normalize();
        Vector2 uv = palette.UV(col);
        int k = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        for (int i = 0; i < 3; i++) { normals.Add(n); uvs.Add(uv); }
        tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
    }

    private static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

    /// <summary>A unit cube (centred, -0.5..0.5) through m; top colours the upper face if given</summary>
    public void Box(Matrix4x4 m, Color col, int faces = All, Color? top = null)
    {
        for (int f = 0; f < 6; f++)
        {
            if ((faces & (1 << f)) == 0) continue;
            Vector3 n = FaceN[f];
            Vector3 up = f == 2 || f == 3 ? Vector3.forward : Vector3.up;
            Vector3 rt = Vector3.Cross(n, up);
            Vector3 c0 = n * 0.5f;
            Quad(m.MultiplyPoint3x4(c0 - rt * 0.5f - up * 0.5f), m.MultiplyPoint3x4(c0 - rt * 0.5f + up * 0.5f),
                 m.MultiplyPoint3x4(c0 + rt * 0.5f + up * 0.5f), m.MultiplyPoint3x4(c0 + rt * 0.5f - up * 0.5f),
                 f == 2 && top.HasValue ? top.Value : col, m.MultiplyVector(n));
        }
    }

    /// <summary>A unit cylinder (radius 0.5, y -0.5..0.5) through m, with its top cap</summary>
    public void Cylinder(Matrix4x4 m, int sides, Color col, bool cap = true, Color? capCol = null)
    {
        Vector3 topC = m.MultiplyPoint3x4(new Vector3(0f, 0.5f, 0f));
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f, am = (a0 + a1) * 0.5f;
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f), p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            Vector3 b0 = m.MultiplyPoint3x4(p0 - Vector3.up * 0.5f), t0 = m.MultiplyPoint3x4(p0 + Vector3.up * 0.5f);
            Vector3 b1 = m.MultiplyPoint3x4(p1 - Vector3.up * 0.5f), t1 = m.MultiplyPoint3x4(p1 + Vector3.up * 0.5f);
            Quad(b0, t0, t1, b1, col, m.MultiplyVector(new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am))));
            if (cap) Tri(topC, t0, t1, capCol ?? col, m.MultiplyVector(Vector3.up));
        }
    }

    /// <summary>A unit cone (base radius 0.5 at y -0.5, point at y 0.5) through m</summary>
    public void Cone(Matrix4x4 m, int sides, Color col)
    {
        Vector3 tip = m.MultiplyPoint3x4(new Vector3(0f, 0.5f, 0f));
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f, am = (a0 + a1) * 0.5f;
            Vector3 b0 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f));
            Vector3 b1 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f));
            Tri(tip, b0, b1, col, m.MultiplyVector(new Vector3(Mathf.Cos(am), 0.5f, Mathf.Sin(am))));
        }
    }

    public Mesh Build(string name)
    {
        var mesh = new Mesh { name = name };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}

public static class ArenaScenery
{
    public const float GroundY = -30f;
    /// <summary>The arena's half-length (toward White and Black) and half-width</summary>
    public const float A0 = 64f, B0 = 50f;
    public const float PodiumTop = GroundY + 7f;
    public const int Rows = 30;
    private const float Tread = 1.5f, Rise = 0.8f, WallRise = 1.6f, Walk = 2.2f;
    private const int Seg = 240;   // slices round the stands

    public const float EmperorDeg = 42f, GateDeg = 218f, RuinDeg = 92f, RuinHalf = 24f;
    public const float AwningFrom = 238f, AwningTo = 324f, MusicDeg = 334f, LionDeg = 158f, ChariotDeg = 64f, RackDeg = 300f;

    /// <summary>Toward the light: the low sun, lifted a little so the pieces are lit</summary>
    public static Vector3 SunLight => ArenaSky.Dir(ArenaSky.SunDeg, 17f);

    private static readonly float[] rowOff = new float[Rows], rowY = new float[Rows];
    private static float topOff, topY;
    private static List<Object> made;

    // ── Colours ──
    private static readonly Color Marble = new Color(0.92f, 0.9f, 0.85f), MarbleDark = new Color(0.82f, 0.79f, 0.74f);
    private static readonly Color Travertine = new Color(0.86f, 0.79f, 0.65f), Weathered = new Color(0.74f, 0.67f, 0.55f);
    private static readonly Color Walkway = new Color(0.66f, 0.62f, 0.56f), Shadow = new Color(0.1f, 0.075f, 0.07f);
    private static readonly Color Red = new Color(0.64f, 0.15f, 0.12f), DeepRed = new Color(0.5f, 0.1f, 0.1f), Ochre = new Color(0.82f, 0.6f, 0.26f);
    private static readonly Color Gold = new Color(0.96f, 0.75f, 0.32f), Bronze = new Color(0.68f, 0.46f, 0.26f), Iron = new Color(0.22f, 0.21f, 0.22f);
    private static readonly Color Purple = new Color(0.42f, 0.12f, 0.46f), Porphyry = new Color(0.48f, 0.13f, 0.22f);
    private static readonly Color Wood = new Color(0.46f, 0.31f, 0.18f), DarkWood = new Color(0.3f, 0.2f, 0.12f);
    private static readonly Color Brick = new Color(0.68f, 0.38f, 0.26f);
    private static readonly Color Blues = new Color(0.16f, 0.36f, 0.82f), Greens = new Color(0.16f, 0.6f, 0.26f);

    private static readonly Color[] Tunics =
    {
        new Color(0.72f, 0.33f, 0.22f), new Color(0.82f, 0.62f, 0.28f), new Color(0.45f, 0.48f, 0.25f), new Color(0.88f, 0.82f, 0.68f),
        new Color(0.35f, 0.55f, 0.75f), new Color(0.45f, 0.3f, 0.2f), new Color(0.78f, 0.5f, 0.52f), new Color(0.2f, 0.5f, 0.5f),
        new Color(0.95f, 0.7f, 0.2f), new Color(0.5f, 0.35f, 0.6f), new Color(0.9f, 0.88f, 0.84f), new Color(0.62f, 0.2f, 0.18f),
    };
    private static readonly Color[] Loud = { new Color(1f, 0.25f, 0.65f), new Color(0.6f, 1f, 0.2f), new Color(0.2f, 0.95f, 1f) };
    private static readonly Color[] Skins =
    {
        new Color(0.95f, 0.78f, 0.64f), new Color(0.84f, 0.63f, 0.47f), new Color(0.66f, 0.46f, 0.32f), new Color(0.47f, 0.32f, 0.22f), new Color(0.98f, 0.86f, 0.74f),
    };
    private static readonly Color[] Hair =
    {
        new Color(0.12f, 0.08f, 0.06f), new Color(0.3f, 0.18f, 0.1f), new Color(0.55f, 0.28f, 0.12f), new Color(0.7f, 0.68f, 0.64f), new Color(0.85f, 0.7f, 0.4f),
    };

    // ── The ellipse ──

    /// <summary>The ellipse parameter whose point lies at compass angle deg from the middle</summary>
    public static float ParamFor(float deg)
    {
        float p = deg * Mathf.Deg2Rad;
        return Mathf.Atan2(A0 * Mathf.Sin(p), B0 * Mathf.Cos(p));
    }

    /// <summary>A point on the ring off units out from the arena wall, at height y</summary>
    public static Vector3 Ell(float t, float off, float y) => new Vector3(Mathf.Cos(t) * (A0 + off), y, Mathf.Sin(t) * (B0 + off));
    public static Vector3 Outward(float t) => new Vector3(Mathf.Cos(t) * B0, 0f, Mathf.Sin(t) * A0).normalized;
    public static float DegOf(Vector3 p) { float d = Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg; return d < 0f ? d + 360f : d; }
    private static bool Near(float deg, float centre, float half) => Mathf.Abs(Mathf.DeltaAngle(deg, centre)) < half;
    private static bool Within(float deg, float from, float to) => Mathf.Repeat(deg - from, 360f) <= Mathf.Repeat(to - from, 360f);

    /// <summary>A basis on the ring at compass angle deg: x along the ring, y up, z in toward the board</summary>
    public static ArenaBasis At(float deg, float off, float y, float scale = 1f)
    {
        float t = ParamFor(deg);
        return new ArenaBasis(Ell(t, off, y), -Outward(t), scale);
    }

    private static float Hash01(int a, int b)
    {
        uint h = (uint)(a * 374761393 + b * 668265263 + 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    private static float R(System.Random rng) => (float)rng.NextDouble();
    private static float R(System.Random rng, float a, float b) => a + (b - a) * (float)rng.NextDouble();
    private static Color Vary(Color c, System.Random rng, float amount) { float k = 1f + (R(rng) - 0.5f) * 2f * amount; return new Color(c.r * k, c.g * k, c.b * k); }

    // The seating's cross-section: each row's front edge out from the wall and its tread height
    private static void Profile()
    {
        float off = 3f, y = PodiumTop;
        for (int k = 0; k < Rows; k++)
        {
            if (IsWall(k)) { y += WallRise; off += Walk; }
            y += Rise;
            rowOff[k] = off;
            rowY[k] = y;
            off += Tread;
        }
        topOff = off;
        topY = y;
    }

    // A painted wall and a walkway in front of rows 10 and 20, splitting the stands in three
    private static bool IsWall(int k) => k == 10 || k == 20;

    /// <summary>How many rows still stand at this slice: all of them, except where the stands have fallen in</summary>
    private static int Cut(float deg, int i)
    {
        float d = Mathf.Abs(Mathf.DeltaAngle(deg, RuinDeg));
        if (d > RuinHalf + 4f) return Rows;
        int jag = (int)(Hash01(i / 2, 7) * 4f);
        if (d > RuinHalf) return Rows - 1 - jag % 3;   // the edge of the break: the top is knocked off
        float f = 1f - d / RuinHalf;
        f = f * f * (3f - 2f * f);
        return Mathf.Clamp(Rows - Mathf.RoundToInt((Rows - 6) * f) - jag + 1, 4, Rows - 1);
    }

    // A vertical strip of the ring between params t0 and t1, off out from the wall, facing in
    private static void Band(ArenaKit k, float t0, float t1, float off, float y0, float y1, Color c)
    {
        Vector3 inward = -Outward((t0 + t1) * 0.5f);
        k.Quad(Ell(t0, off, y0), Ell(t0, off, y1), Ell(t1, off, y1), Ell(t1, off, y0), c, inward);
    }

    // A level strip of the ring from off0 to off1 out from the wall, facing up (or down)
    private static void Flat(ArenaKit k, float t0, float t1, float off0, float off1, float y, Color c, bool down = false)
    {
        k.Quad(Ell(t0, off0, y), Ell(t1, off0, y), Ell(t1, off1, y), Ell(t0, off1, y), c, down ? Vector3.down : Vector3.up);
    }

    private static T Keep<T>(T thing) where T : Object { made.Add(thing); return thing; }

    private static Transform Piece(Transform parent, string name, Mesh mesh, Material mat, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        if (mesh == null) return go.transform;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    private static Transform Piece(Transform parent, string name, ArenaKit kit, Material mat, Vector3 at) =>
        Piece(parent, name, kit.Empty ? null : Keep(kit.Build(name)), mat, at);

    // ─────────────────────────────────────────────────────────────────────────

    private class Ctx
    {
        public ArenaPalette pal;
        public ArenaKit stone, metal, glow;
        public ArenaKit[] crowd;
        public Material lit, shiny, bright;
        public Transform root;
        public ArenaLife life;
        public Mesh flame;
        public System.Random rng;
    }

    public static void Build(Transform root)
    {
        made = root.gameObject.AddComponent<ArenaCleanup>().Made;
        pennants.Clear();   // the last build's pennant meshes go with its cleanup (and its palette)
        Profile();

        var pal = new ArenaPalette();
        Keep(pal.Texture);
        var c = new Ctx
        {
            pal = pal,
            stone = new ArenaKit(pal),
            metal = new ArenaKit(pal),
            glow = new ArenaKit(pal),
            crowd = new ArenaKit[36],
            lit = Keep(OrbitScenery.Lit(Color.white, Color.black, 0f, 0.12f, pal.Texture)),
            shiny = Keep(OrbitScenery.Lit(Color.white, Color.black, 0.6f, 0.55f, pal.Texture)),
            bright = Keep(new Material(Shader.Find("Unlit/Texture")) { mainTexture = pal.Texture }),
            root = root,
            life = root.gameObject.AddComponent<ArenaLife>(),
            rng = new System.Random(476),
        };
        for (int i = 0; i < c.crowd.Length; i++) c.crowd[i] = new ArenaKit(pal);
        c.flame = FlameMesh(c);

        Sand(c);
        Stands(c);
        RuinArcade(c);
        Countryside(c);
        EmperorBox(c);
        Musicians(c);
        Chariot(c);
        Gate(c);
        Colossus(c, At(GateDeg + 15f, -4f, GroundY), false);
        Colossus(c, At(GateDeg - 15f, -4f, GroundY), true);
        LionLift(c);
        Racks(c);
        Torches(c);
        Birds(c);
        Air(c);

        Piece(root, "Arena Stone", c.stone, c.lit, Vector3.zero);
        Piece(root, "Arena Bronze", c.metal, c.shiny, Vector3.zero);
        Piece(root, "Arena Glow", c.glow, c.bright, Vector3.zero);
        for (int i = 0; i < c.crowd.Length; i++)
        {
            if (c.crowd[i].Empty) continue;
            Transform chunk = Piece(root, "Crowd " + (i * 10), c.crowd[i], c.lit, Vector3.zero);
            c.life.Crowd.Add((chunk, i * 10f + 5f));
        }
        c.life.WaveFrom = RuinDeg + RuinHalf;
        c.life.WaveTo = RuinDeg - RuinHalf + 360f;

        pal.Apply();
    }

    // ── The sand ──

    private static void Sand(Ctx c)
    {
        const int size = 512;
        const float span = 2f * (A0 + 2f);
        var tex = Keep(new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Arena Sand" });
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float wx = ((x + 0.5f) / size - 0.5f) * span, wz = ((y + 0.5f) / size - 0.5f) * span;
                float n = Mathf.PerlinNoise(wx * 0.15f + 3.1f, wz * 0.15f + 7.7f) * 0.6f + Mathf.PerlinNoise(wx * 0.6f + 11f, wz * 0.6f) * 0.4f;
                Color col = Color.Lerp(new Color(0.78f, 0.62f, 0.42f), new Color(0.92f, 0.78f, 0.56f), n);
                float re = Mathf.Sqrt((wx / A0) * (wx / A0) + (wz / B0) * (wz / B0));   // 1 at the arena wall
                float r = Mathf.Sqrt(wx * wx + wz * wz), a = Mathf.Atan2(wz, wx);
                float deg = a * Mathf.Rad2Deg;

                // Raked rings round the middle
                if (re > 0.28f && re < 0.78f) col *= 1f + 0.06f * Mathf.Sin(re * A0 * Mathf.PI * 2f / 1.1f);
                // A double ring and an eight-point star raked under the board
                if (Mathf.Abs(r - 13f) < 0.35f || Mathf.Abs(r - 11.6f) < 0.22f) col *= 0.8f;
                float star = 5f + 6.6f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4f)), 6f);
                if (r < 11.6f && Mathf.Abs(r - star) < 0.28f) col *= 0.84f;
                // Chariot ruts looping close to the wall
                float wob = 0.006f * Mathf.Sin(a * 5f + 1f);
                if (Mathf.Abs(re - 0.86f - wob) < 0.006f || Mathf.Abs(re - 0.885f - wob) < 0.006f) col *= 0.8f;
                // Scuffs and footprints where the fighters come in and train
                if ((Near(deg, GateDeg, 20f) || Near(deg, RackDeg, 12f)) && re > 0.55f)
                {
                    float scuff = Mathf.PerlinNoise(wx * 0.9f, wz * 0.9f + 40f);
                    if (scuff > 0.62f) col *= 0.86f;
                }
                // Grit and grass blown in below the ruined side
                if (Near(deg, RuinDeg, RuinHalf) && re > 0.88f)
                {
                    float g = Mathf.PerlinNoise(wx * 1.3f, wz * 1.3f);
                    if (g > 0.66f) col = Color.Lerp(col, new Color(0.42f, 0.48f, 0.22f), 0.6f);
                    else if (g < 0.25f) col *= 0.75f;
                }
                px[y * size + x] = col;
            }
        tex.SetPixels32(px);
        tex.Apply(true);

        // An oval fan just under the arena wall
        const int sides = 128;
        var verts = new List<Vector3> { new Vector3(0f, GroundY, 0f) };
        var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        var tris = new List<int>();
        for (int i = 0; i <= sides; i++)
        {
            float t = i / (float)sides * Mathf.PI * 2f;
            Vector3 p = Ell(t, 0.3f, GroundY);
            verts.Add(p);
            uvs.Add(new Vector2(p.x / span + 0.5f, p.z / span + 0.5f));
            if (i > 0) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
        }
        var normals = new List<Vector3>();
        foreach (Vector3 _ in verts) normals.Add(Vector3.up);
        var mesh = Keep(new Mesh { name = "Arena Sand" });
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        Piece(c.root, "Arena Sand", mesh, Keep(OrbitScenery.Lit(Color.white, Color.black, 0f, 0.05f, tex)), Vector3.zero);
    }

    // ── The stands and their crowd ──

    private static void Stands(Ctx c)
    {
        ArenaKit k = c.stone;
        var cut = new int[Seg];
        var segDeg = new float[Seg];
        for (int i = 0; i < Seg; i++) segDeg[i] = DegOf(Ell((i + 0.5f) / Seg * Mathf.PI * 2f, 20f, 0f));
        for (int i = 0; i < Seg; i++) cut[i] = Cut(segDeg[i], i);

        for (int i = 0; i < Seg; i++)
        {
            float t0 = i / (float)Seg * Mathf.PI * 2f, t1 = (i + 1) / (float)Seg * Mathf.PI * 2f, tm = (t0 + t1) * 0.5f;
            float deg = segDeg[i];
            bool gate = Near(deg, GateDeg, 8.5f);
            bool emperor = Near(deg, EmperorDeg, 7.5f);
            bool aisle = i % 12 == 6;
            int rows = cut[i];
            bool ruin = rows < Rows;
            float grain = 0.97f + 0.06f * Hash01(i, 3);

            // The arena wall: a dark plinth, marble panels, a red band and a gold line under the parapet
            if (!gate)
            {
                Band(k, t0, t1, 0f, GroundY, GroundY + 1f, new Color(0.42f, 0.36f, 0.31f));
                Band(k, t0, t1, 0f, GroundY + 1f, PodiumTop - 1.3f, (i % 4 == 0 ? MarbleDark : Marble) * grain);
                Band(k, t0, t1, 0f, PodiumTop - 1.3f, PodiumTop - 0.45f, Red);
                Band(c.metal, t0, t1, -0.05f, PodiumTop - 0.45f, PodiumTop, Gold);
                if (!emperor)
                {
                    Band(k, t0, t1, 0f, PodiumTop, PodiumTop + 1.2f, Marble * grain);
                    Flat(k, t0, t1, 0f, 0.6f, PodiumTop + 1.2f, MarbleDark);
                }
                Flat(k, t0, t1, 0.6f, 3f, PodiumTop, Walkway);
            }

            // Banner colours for this part of the ring
            Color banner = Near(deg, EmperorDeg, 40f) ? Purple : Within(deg, AwningFrom, 281f) ? Blues : Within(deg, 281f, AwningTo) ? Greens
                : Near(deg, GateDeg, 30f) ? Red : (i / 4) % 2 == 0 ? new Color(0.95f, 0.66f, 0.15f) : new Color(0.1f, 0.55f, 0.55f);

            for (int r = gate ? 9 : 0; r < rows; r++)
            {
                float o = rowOff[r], y = rowY[r];
                if (IsWall(r))
                {
                    float wy = y - Rise - WallRise, wo = o - Walk;
                    Band(k, t0, t1, wo, wy, wy + WallRise, aisle ? Shadow : r == 10 ? DeepRed : Ochre);
                    if (!aisle) Band(c.metal, t0, t1, wo - 0.03f, wy + WallRise - 0.22f, wy + WallRise, r == 10 ? Gold : DeepRed);
                    Flat(k, t0, t1, wo, o, wy + WallRise, Walkway);
                    // Banners hang from the wall over the row below
                    if (i % 4 == 1 && !ruin && !gate && !Near(deg, EmperorDeg, 4f))
                    {
                        float ta = Mathf.Lerp(t0, t1, -0.3f), tb = Mathf.Lerp(t0, t1, 1.3f);
                        float top = wy + WallRise + 0.1f, bottom = wy - 1.4f;
                        k.Quad(Ell(ta, wo - 0.12f, bottom + 0.35f), Ell(ta, wo - 0.12f, top), Ell(tb, wo - 0.12f, top), Ell(tb, wo - 0.12f, bottom + 0.35f), banner, -Outward(tm));
                        k.Quad(Ell(ta, wo - 0.14f, bottom), Ell(ta, wo - 0.14f, bottom + 0.35f), Ell(tb, wo - 0.14f, bottom + 0.35f), Ell(tb, wo - 0.14f, bottom), Gold, -Outward(tm));
                    }
                }
                bool broken = ruin && r >= rows - 2;
                Color tc = aisle ? new Color(0.8f, 0.76f, 0.68f) : broken ? new Color(0.58f, 0.56f, 0.46f) : r < 10 ? Marble : r < 20 ? Travertine : Weathered;
                tc *= grain;
                Band(k, t0, t1, o, y - Rise, y, tc * 0.84f);
                Flat(k, t0, t1, o, o + Tread, y, tc);

                if (ruin && r >= rows - 6 && Hash01(i * 31 + r, 11) < 0.22f)   // grass in the cracks
                {
                    Vector3 p = Ell(Mathf.Lerp(t0, t1, Hash01(i, r)), o + 0.4f + Hash01(r, i) * 0.8f, y);
                    k.Cone(Matrix4x4.TRS(p + Vector3.up * 0.3f, Quaternion.Euler(0f, i * 37f, 0f), new Vector3(0.9f, 0.6f, 0.9f)), 5, new Color(0.36f, 0.5f, 0.2f) * (0.85f + 0.3f * Hash01(r, i + 5)));
                }
                if (ruin && r == rows - 1 && Hash01(i, 19) < 0.6f)   // rubble on the broken top row
                {
                    Vector3 p = Ell(Mathf.Lerp(t0, t1, Hash01(i, 23)), o + 0.75f, y);
                    float s = 0.6f + Hash01(i, 29) * 0.9f;
                    k.Box(Matrix4x4.TRS(p + Vector3.up * s * 0.35f, Quaternion.Euler(Hash01(i, 31) * 30f, Hash01(i, 37) * 90f, Hash01(i, 41) * 30f), new Vector3(s * 1.3f, s * 0.8f, s)),
                        Hash01(i, 43) < 0.5f ? Weathered : Brick, ArenaKit.All & ~ArenaKit.NY);
                }

                // The crowd on this row of this slice
                Crowd(c, i, r, t0, t1, deg, aisle, gate, rows);
            }

            // Where the stands have fallen, the brick core shows at the broken ends
            if (ruin)
                for (int side = -1; side <= 1; side += 2)
                {
                    int j = (i + side + Seg) % Seg;
                    if (cut[j] <= rows) continue;
                    float tb = side < 0 ? t0 : t1;
                    Vector3 toward = Ell(tm, 20f, 0f) - Ell(tb, 20f, 0f);
                    float baseY = rowY[rows - 1] - 6f;
                    for (int r = rows; r < cut[j]; r++)
                    {
                        float front = rowOff[r] - (IsWall(r) ? Walk : 0f);
                        Color b = Brick * (0.86f + 0.24f * Hash01(i * 7 + r, side + 3));
                        k.Quad(Ell(tb, front, baseY), Ell(tb, front, rowY[r]), Ell(tb, rowOff[r] + Tread, rowY[r]), Ell(tb, rowOff[r] + Tread, baseY), b, toward);
                    }
                    if (cut[j] == Rows)
                        k.Quad(Ell(tb, topOff, baseY), Ell(tb, topOff, topY + 10f), Ell(tb, topOff + 5.8f, topY + 10f), Ell(tb, topOff + 5.8f, baseY), Brick * 0.9f, toward);
                }

            if (rows == Rows) Portico(c, i, t0, t1, tm, deg);
        }

        CarpetOfHonour(c);
    }

    // Seat this row of this slice with people, as dense as that part of the stands is
    private static void Crowd(Ctx c, int i, int r, float t0, float t1, float deg, bool aisle, bool gate, int rows)
    {
        if (aisle) return;
        if (Near(deg, EmperorDeg, 7.5f) && r < 3) return;   // the emperor's box stands there
        if (Near(deg, EmperorDeg, 3.7f)) return;              // the carpet of honour
        var rng = c.rng;
        bool awning = Within(deg, AwningFrom, AwningTo);
        float density = awning ? 0.86f : r >= 20 ? 0.52f : 0.64f;
        if (Near(deg, GateDeg, 25f)) density = 0.72f;
        float fromRuin = Mathf.Abs(Mathf.DeltaAngle(deg, RuinDeg));
        if (fromRuin < RuinHalf + 14f) density *= Mathf.Clamp01((fromRuin - RuinHalf * 0.55f) / (RuinHalf * 0.9f));
        if (rows < Rows && r >= rows - 1) density *= 0.3f;
        if (density <= 0f) return;

        float o = rowOff[r] + 0.95f, y = rowY[r];
        float len = (Ell(t1, o, y) - Ell(t0, o, y)).magnitude;
        int n = Mathf.Max(1, Mathf.FloorToInt(len / 0.9f + Hash01(i, r)));
        for (int j = 0; j < n; j++)
        {
            if (R(rng) > density) continue;
            float t = Mathf.Lerp(t0, t1, (j + 0.5f + (R(rng) - 0.5f) * 0.3f) / n);
            var b = new ArenaBasis(Ell(t, o + (R(rng) - 0.5f) * 0.15f, y), -Outward(t), 1f);
            float pd = DegOf(b.o);

            // What they wear depends on where they sit
            Color tunic = Tunics[rng.Next(Tunics.Length)];
            if (r < 10 && Near(pd, EmperorDeg, 45f)) tunic = R(rng) < 0.75f ? new Color(0.94f, 0.92f, 0.86f) : R(rng) < 0.5f ? Purple : new Color(0.75f, 0.2f, 0.5f);   // senators in white togas
            else if (Within(pd, AwningFrom - 10f, 281f) && R(rng) < 0.62f) tunic = Vary(Blues, rng, 0.25f);
            else if (Within(pd, 281f, AwningTo + 10f) && R(rng) < 0.62f) tunic = Vary(Greens, rng, 0.25f);
            if (R(rng) < 0.03f) tunic = Loud[rng.Next(Loud.Length)];   // a few superfans in loud colours
            tunic = Vary(tunic, rng, 0.1f);
            if (awning && r >= 12) tunic *= 0.82f;   // in the awnings' shade

            Color skin = Skins[rng.Next(Skins.Length)], hair = Hair[rng.Next(Hair.Length)];
            ArenaKit k = c.crowd[Mathf.Clamp((int)(pd / 10f), 0, 35)];
            float h = 0.9f + R(rng) * 0.2f;
            k.Box(b.M(0f, 0.31f * h, 0f, 0.56f, 0.62f * h, 0.34f), tunic, ArenaKit.Seen);
            k.Box(b.M(0f, 0.62f * h + 0.17f, 0.02f, 0.3f, 0.32f, 0.3f), skin, ArenaKit.Seen, hair);

            float roll = R(rng);
            if (roll < 0.14f)   // cheering
            {
                k.Box(b.M(0.34f, 0.85f * h, 0.03f, 0.13f, 0.62f, 0.13f), skin, ArenaKit.Seen);
                if (roll < 0.07f) k.Box(b.M(-0.34f, 0.85f * h, 0.03f, 0.13f, 0.62f, 0.13f), skin, ArenaKit.Seen);
            }
            else if (roll < (awning ? 0.24f : 0.16f))   // waving a flag
            {
                Color flag = Within(pd, AwningFrom - 10f, 281f) ? Blues : Within(pd, 281f, AwningTo + 10f) ? Greens : R(rng) < 0.5f ? Gold : Loud[rng.Next(Loud.Length)];
                k.Box(b.M(0.34f, 1.5f, 0.05f, 0.06f, 2.2f, 0.06f), DarkWood, ArenaKit.Seen);
                k.Box(b.M(0.34f + 0.48f, 2.3f, 0.05f, 0.9f, 0.55f, 0.04f), flag, ArenaKit.All);
            }
        }
    }

    // The colonnade along the top of the stands, the attic wall over it and its masts
    private static void Portico(Ctx c, int i, float t0, float t1, float tm, float deg)
    {
        ArenaKit k = c.stone;
        float po = topOff, py = topY;
        Flat(k, t0, t1, po, po + 4.5f, py, Walkway);
        bool door = i % 2 == 0;
        if (door)
        {
            Band(k, t0, t1, po + 4.5f, py, py + 3.8f, Shadow);
            Band(k, t0, t1, po + 4.5f, py + 3.8f, py + 5.6f, Travertine);
        }
        else Band(k, t0, t1, po + 4.5f, py, py + 5.6f, Travertine * 0.94f);
        Flat(k, t0, t1, po + 0.3f, po + 4.5f, py + 5.6f, new Color(0.5f, 0.44f, 0.38f), true);   // the ceiling, seen from below
        Band(k, t0, t1, po + 0.3f, py + 5.6f, py + 6.6f, Travertine);                            // entablature
        Flat(k, t0, t1, po + 0.3f, po + 5f, py + 6.6f, Weathered);
        Band(k, t0, t1, po + 5f, py + 6.6f, py + 10f, (i % 2 == 0 ? Travertine : Weathered));   // the attic wall
        Flat(k, t0, t1, po + 5f, po + 5.8f, py + 10f, Weathered);

        if (!door)
        {
            // A column at the slice's edge, and a bronze statue in the opening on the gate's side
            Vector3 col = Ell(t1, po + 0.8f, py);
            k.Cylinder(Matrix4x4.TRS(col + Vector3.up * 2.8f, Quaternion.identity, new Vector3(0.76f, 5.6f, 0.76f)), 8, Marble);
            k.Box(Matrix4x4.TRS(col + Vector3.up * 5.4f, Quaternion.LookRotation(-Outward(t1)), new Vector3(1.1f, 0.4f, 1.1f)), Travertine);
            k.Box(Matrix4x4.TRS(col + Vector3.up * 0.2f, Quaternion.LookRotation(-Outward(t1)), new Vector3(1.0f, 0.4f, 1.0f)), Travertine);
            if (Near(deg, GateDeg + 11f, 6f) || Near(deg, GateDeg - 41f, 9f))
            {
                var b = new ArenaBasis(Ell(tm, po + 3.2f, py), -Outward(tm), 1.3f);
                c.metal.Box(b.M(0f, 0.3f, 0f, 0.9f, 0.6f, 0.9f), Bronze);
                Figure(c.metal, b.Sub(0f, 0.6f, 0f, 0f, 1f), Bronze, Bronze, Bronze);
            }
        }
        else if (i % 6 == 0)   // bronze shields on the attic
            c.metal.Cylinder(Matrix4x4.TRS(Ell(tm, po + 4.95f, py + 8.3f), Quaternion.FromToRotation(Vector3.up, -Outward(tm)), new Vector3(1.4f, 0.1f, 1.4f)), 10, Gold);
        else Band(k, t0 + (t1 - t0) * 0.3f, t1 - (t1 - t0) * 0.3f, po + 4.97f, py + 7.8f, py + 8.9f, Shadow);   // little attic windows

        // Masts for the awnings, every third slice, each with a pennant
        if (i % 3 == 0)
        {
            Vector3 foot = Ell(tm, po + 5.4f, py + 10f);
            k.Box(Matrix4x4.TRS(foot + Vector3.up * 4.5f, Quaternion.LookRotation(-Outward(tm)), new Vector3(0.35f, 9f, 0.35f)), new Color(0.48f, 0.36f, 0.24f));
            Color pc = Near(deg, EmperorDeg, 40f) ? Purple : Within(deg, AwningFrom, 281f) ? Blues : Within(deg, 281f, AwningTo) ? Greens : Near(deg, GateDeg, 35f) ? Red : (i / 3) % 2 == 0 ? Gold : new Color(0.1f, 0.6f, 0.6f);
            Pennant(c, foot + Vector3.up * 9f, pc, i);

            // The awnings: striped canvas from mast top in over the upper seats, sagging between
            if (Within(deg, AwningFrom, AwningTo))
            {
                float ta = tm, tb = tm + 3f / Seg * Mathf.PI * 2f;
                const int steps = 6;
                for (int s = 0; s < steps; s++)
                {
                    float s0 = s / (float)steps, s1 = (s + 1) / (float)steps;
                    for (int stripe = 0; stripe < 3; stripe++)
                    {
                        float u0 = Mathf.Lerp(ta, tb, stripe / 3f), u1 = Mathf.Lerp(ta, tb, (stripe + 1) / 3f);
                        Color cloth = stripe % 2 == 0 ? new Color(0.78f, 0.18f, 0.16f) : new Color(0.96f, 0.9f, 0.76f);
                        c.stone.Quad2(Awning(u0, s0), Awning(u0, s1), Awning(u1, s1), Awning(u1, s0), cloth);
                    }
                }
            }
        }
    }

    // A point on the awnings: s 0 at the mast tops, 1 at the inner edge over the middle seats
    private static Vector3 Awning(float t, float s)
    {
        float off = Mathf.Lerp(topOff + 5.4f, 24f, s);
        float y = Mathf.Lerp(topY + 18.5f, topY + 11f, s) - 1.4f * Mathf.Sin(s * Mathf.PI);
        return Ell(t, off, y);
    }

    // A purple carpet with a golden wreath laid up the stands behind the emperor's box
    private static void CarpetOfHonour(Ctx c)
    {
        float ta = ParamFor(EmperorDeg - 3.3f), tb = ParamFor(EmperorDeg + 3.3f);
        const float lift = 1.15f;
        float o0 = rowOff[3], y0 = rowY[3] - Rise + lift, o1 = topOff, y1 = topY + lift;
        Vector3 Pt(float side, float up) => Ell(Mathf.Lerp(ta, tb, side), Mathf.Lerp(o0, o1, up), Mathf.Lerp(y0, y1, up));
        Vector3 inward = -Outward((ta + tb) * 0.5f) + Vector3.up;
        c.stone.Quad(Pt(0f, 0f), Pt(0f, 1f), Pt(1f, 1f), Pt(1f, 0f), Purple, inward);
        // Gold borders, a little proud of the cloth
        Vector3 lift2 = Vector3.up * 0.04f;
        c.metal.Quad(Pt(0f, 0f) + lift2, Pt(0f, 1f) + lift2, Pt(0.08f, 1f) + lift2, Pt(0.08f, 0f) + lift2, Gold, inward);
        c.metal.Quad(Pt(0.92f, 0f) + lift2, Pt(0.92f, 1f) + lift2, Pt(1f, 1f) + lift2, Pt(1f, 0f) + lift2, Gold, inward);

        // A laurel wreath round an eagle-gold star, two thirds of the way up
        Vector3 centre = Pt(0.5f, 0.55f) + Vector3.up * 0.08f;
        Vector3 across = (Pt(1f, 0.55f) - Pt(0f, 0.55f)).normalized;
        Vector3 upSlope = (Pt(0.5f, 1f) - Pt(0.5f, 0f)).normalized;
        Vector3 normal = Vector3.Cross(upSlope, across).normalized;
        if (Vector3.Dot(normal, inward) < 0f) normal = -normal;
        Quaternion onCloth = Quaternion.LookRotation(normal, upSlope);
        for (int side = -1; side <= 1; side += 2)
            for (int j = 0; j < 9; j++)
            {
                float a = (-80f + j * 19f) * Mathf.Deg2Rad;   // up the side, open at the top
                Vector3 p = centre + (across * (side * Mathf.Cos(a)) + upSlope * Mathf.Sin(a)) * 3.2f;
                c.metal.Box(Matrix4x4.TRS(p, onCloth * Quaternion.Euler(0f, 0f, side * (a * Mathf.Rad2Deg + 90f) + side * 30f), new Vector3(1.1f, 0.45f, 0.12f)), Gold, ArenaKit.All & ~ArenaKit.NZ);
            }
        for (int j = 0; j < 4; j++)
            c.metal.Box(Matrix4x4.TRS(centre, onCloth * Quaternion.Euler(0f, 0f, j * 45f), new Vector3(3.2f, 0.4f, 0.12f)), Gold, ArenaKit.All & ~ArenaKit.NZ);
    }

    // ── The ruined side ──

    // Broken piers and arches of the outer wall standing in the gap, black against the sunset
    private static void RuinArcade(Ctx c)
    {
        int[] levels = { 3, 3, 2, 3, 2, 1, 1, 0, 1, 2, 3, 2, 1, 0, 0, 1, 0, 1, 2, 1, 2, 3, 3 };
        float from = RuinDeg - RuinHalf - 3f, step = (2f * RuinHalf + 6f) / (levels.Length - 1);
        float off = topOff + 5.4f;
        float levelH = (topY + 10f - GroundY) / 3f;
        Color stone = Travertine * 0.92f;
        for (int p = 0; p < levels.Length; p++)
        {
            ArenaBasis b = At(from + p * step, off, GroundY);
            int lv = levels[p];
            if (lv == 0)
            {
                c.stone.Box(b.M(0f, 1.2f, 0f, 2.4f, 2.4f, 2.6f, 0f, 20f, 8f), stone);   // only a stump left
                continue;
            }
            float h = lv * levelH - (lv < 3 ? Hash01(p, 5) * levelH * 0.4f : 0f);
            c.stone.Box(b.M(0f, h * 0.5f, 0f, 2.2f, h, 2.6f), Vary(stone, c.rng, 0.06f));
            // Arches to the next pier where both stand tall enough
            if (p + 1 >= levels.Length) continue;
            int shared = Mathf.Min(lv, levels[p + 1]);
            ArenaBasis mid = At(from + (p + 0.5f) * step, off, GroundY);
            float gap = (b.o - At(from + (p + 1) * step, off, GroundY).o).magnitude - 2.2f;
            float ar = gap * 0.5f;
            for (int l = 0; l < shared; l++)
            {
                float spring = l * levelH + levelH - 2.4f - ar;
                for (int v = 0; v < 7; v++)
                {
                    float a = (v + 0.5f) / 7f * Mathf.PI;
                    c.stone.Box(mid.M(Mathf.Cos(a) * (ar + 0.35f), spring + Mathf.Sin(a) * (ar + 0.35f), 0f, 0.95f, 0.75f, 2.5f, 0f, 0f, a * Mathf.Rad2Deg + 90f), stone);
                }
                c.stone.Box(mid.M(0f, spring + ar + 1.55f, 0f, gap + 0.4f, 1.6f, 2.5f), stone);       // wall over the arch
                c.stone.Box(mid.M(0f, l * levelH + levelH - 0.5f, 0f, gap + 2.6f, 1f, 2.9f), Weathered); // cornice
            }
        }

        // Fallen blocks and column drums spilled onto the sand below the break
        var rng = c.rng;
        for (int j = 0; j < 26; j++)
        {
            ArenaBasis b = At(RuinDeg + R(rng, -RuinHalf * 0.8f, RuinHalf * 0.8f), -R(rng, 0.5f, 7f), GroundY);
            float s = R(rng, 0.7f, 1.8f);
            if (R(rng) < 0.4f)
                c.stone.Cylinder(b.M(new Vector3(0f, s * 0.45f, 0f), new Vector3(s, s * 1.3f, s), new Vector3(90f, R(rng, 0f, 180f), 0f)), 8, Marble * 0.9f);
            else
                c.stone.Box(b.M(new Vector3(0f, s * 0.4f, 0f), new Vector3(s * 1.4f, s * 0.8f, s), new Vector3(R(rng, -15f, 15f), R(rng, 0f, 90f), R(rng, -15f, 15f))), R(rng) < 0.5f ? Weathered : Brick, ArenaKit.All & ~ArenaKit.NY);
        }
    }

    // What you see through the gap: fields, hills, cypresses, an aqueduct, a hill temple and a town
    private static void Countryside(Ctx c)
    {
        var rng = c.rng;
        OrbitScenery.Part(c.root, PrimitiveType.Cylinder, new Vector3(0f, GroundY - 0.6f, 0f), new Vector3(1800f, 0.05f, 1800f),
            Keep(OrbitScenery.Lit(new Color(0.42f, 0.42f, 0.22f), Color.black, 0f, 0.05f))).name = "Fields";

        Material[] hills =
        {
            Keep(OrbitScenery.Lit(new Color(0.32f, 0.38f, 0.18f), Color.black, 0f, 0.05f)),
            Keep(OrbitScenery.Lit(new Color(0.4f, 0.4f, 0.2f), Color.black, 0f, 0.05f)),
            Keep(OrbitScenery.Lit(new Color(0.28f, 0.33f, 0.2f), Color.black, 0f, 0.05f)),
        };
        for (int j = 0; j < 12; j++)
        {
            float deg = R(rng, 35f, 155f), d = R(rng, 330f, 560f), w = R(rng, 120f, 240f), h = R(rng, 50f, 110f);
            Vector3 p = ArenaSky.Dir(deg, 0f) * d + Vector3.up * GroundY;
            OrbitScenery.Part(c.root, PrimitiveType.Sphere, p, new Vector3(w, h, w * 0.8f), hills[j % hills.Length], Quaternion.Euler(0f, R(rng, 0f, 180f), 0f)).name = "Hill";
        }
        // The temple's hill
        Vector3 templeHill = ArenaSky.Dir(116f, 0f) * 300f + Vector3.up * GroundY;
        OrbitScenery.Part(c.root, PrimitiveType.Sphere, templeHill, new Vector3(110f, 56f, 90f), hills[0]).name = "Temple Hill";

        // A temple on the hilltop
        var tb = new ArenaBasis(templeHill + Vector3.up * 26.5f, -ArenaSky.Dir(116f, 0f), 1.4f);
        c.stone.Box(tb.M(0f, 1f, 0f, 16f, 2f, 10f), Marble);
        for (int x = 0; x < 6; x++)
            for (int z = 0; z < 2; z++)
                c.stone.Cylinder(tb.M(-6.5f + x * 2.6f, 5f, z == 0 ? 4f : -4f, 1f, 6f, 1f), 6, Marble);
        c.stone.Box(tb.M(0f, 8.5f, 0f, 16.5f, 1f, 10.5f), Marble);
        c.stone.Box(tb.M(0f, 5f, 0f, 11f, 6f, 6f), Travertine * 0.8f);
        for (int side = -1; side <= 1; side += 2)
            c.stone.Tri(tb.P(-8.25f, 9f, side * 5.25f), tb.P(0f, 11.5f, side * 5.25f), tb.P(8.25f, 9f, side * 5.25f), Marble, tb.f * side);
        c.stone.Quad(tb.P(-8.25f, 9f, 5.25f), tb.P(-8.25f, 9f, -5.25f), tb.P(0f, 11.5f, -5.25f), tb.P(0f, 11.5f, 5.25f), new Color(0.72f, 0.32f, 0.2f), Vector3.up - tb.r);
        c.stone.Quad(tb.P(8.25f, 9f, 5.25f), tb.P(0f, 11.5f, 5.25f), tb.P(0f, 11.5f, -5.25f), tb.P(8.25f, 9f, -5.25f), new Color(0.72f, 0.32f, 0.2f), Vector3.up + tb.r);

        // The aqueduct striding across the land on two tiers of arches
        Vector3 a0 = ArenaSky.Dir(58f, 0f) * 250f + Vector3.up * GroundY, a1 = ArenaSky.Dir(140f, 0f) * 265f + Vector3.up * GroundY;
        Vector3 along = (a1 - a0).normalized, toward = Vector3.Cross(Vector3.up, along);
        int piers = Mathf.RoundToInt((a1 - a0).magnitude / 9f);
        var ab = new ArenaBasis(a0, toward, 1f);
        float span = (a1 - a0).magnitude / piers;
        Color aq = new Color(0.82f, 0.72f, 0.58f);
        for (int p = 0; p <= piers; p++)
        {
            ArenaBasis pb = new ArenaBasis(a0 + along * (span * p), toward, 1f);
            float px = 0f;
            c.stone.Box(pb.M(px, 9f, 0f, 2.6f, 18f, 3f), aq);
            c.stone.Box(pb.M(px, 23f, 0f, 1.8f, 7f, 2.4f), aq);
            if (p == piers) break;
            for (int tier = 0; tier < 2; tier++)
            {
                float spring = tier == 0 ? 13f : 24f, ar = (span - (tier == 0 ? 2.6f : 1.8f)) * 0.5f;
                ArenaBasis mb = new ArenaBasis(a0 + along * (span * (p + 0.5f)), toward, 1f);
                for (int v = 0; v < 5; v++)
                {
                    float a = (v + 0.5f) / 5f * Mathf.PI;
                    // Arch stones along the line of the aqueduct: x of this basis runs back along `along`
                    Vector3 pos = mb.o + along * (-Mathf.Cos(a) * (ar + 0.4f)) + Vector3.up * (spring + Mathf.Sin(a) * (ar + 0.4f));
                    c.stone.Box(Matrix4x4.TRS(pos, Quaternion.LookRotation(toward) * Quaternion.Euler(0f, 0f, -(a * Mathf.Rad2Deg + 90f)), new Vector3(1.4f, 0.9f, tier == 0 ? 3f : 2.4f)), aq);
                }
                c.stone.Box(Matrix4x4.TRS(mb.o + Vector3.up * (spring + ar + 1.6f), Quaternion.LookRotation(toward), new Vector3(span, tier == 0 ? 2.2f : 1.4f, tier == 0 ? 3f : 2.4f)), aq * 0.95f);
            }
        }
        c.stone.Box(Matrix4x4.TRS((a0 + a1) * 0.5f + Vector3.up * 28.5f, Quaternion.LookRotation(toward), new Vector3((a1 - a0).magnitude + 3f, 1.6f, 2.6f)), aq * 0.9f);

        // Cypresses and umbrella pines between the arena and the hills
        for (int j = 0; j < 48; j++)
        {
            float deg = R(rng, 48f, 140f), d = R(rng, 140f, 240f);
            Vector3 p = ArenaSky.Dir(deg, 0f) * d + Vector3.up * GroundY;
            if (R(rng) < 0.8f)
            {
                float h = R(rng, 9f, 17f), w = h * R(rng, 0.17f, 0.24f);
                c.stone.Cone(Matrix4x4.TRS(p + Vector3.up * (h * 0.5f + 1f), Quaternion.Euler(0f, R(rng, 0f, 90f), 0f), new Vector3(w, h, w)), 7, Vary(new Color(0.12f, 0.24f, 0.13f), rng, 0.2f));
                c.stone.Box(Matrix4x4.TRS(p + Vector3.up * 0.6f, Quaternion.identity, new Vector3(0.5f, 1.2f, 0.5f)), DarkWood);
            }
            else
            {
                float h = R(rng, 8f, 12f);
                c.stone.Box(Matrix4x4.TRS(p + Vector3.up * h * 0.5f, Quaternion.Euler(0f, 0f, R(rng, -6f, 6f)), new Vector3(0.6f, h, 0.6f)), DarkWood);
                c.stone.Cylinder(Matrix4x4.TRS(p + Vector3.up * (h + 0.6f), Quaternion.Euler(0f, R(rng, 0f, 90f), 0f), new Vector3(R(rng, 7f, 10f), 2.2f, R(rng, 6f, 9f))), 9, Vary(new Color(0.2f, 0.32f, 0.15f), rng, 0.15f));
            }
        }

        // A little town of white walls and terracotta roofs
        for (int j = 0; j < 34; j++)
        {
            float deg = R(rng, 120f, 150f), d = R(rng, 175f, 235f);
            Vector3 p = ArenaSky.Dir(deg, 0f) * d + Vector3.up * GroundY;
            float w = R(rng, 4f, 8f), dpt = R(rng, 4f, 7f), h = R(rng, 3f, 6f);
            Quaternion q = Quaternion.Euler(0f, R(rng, 0f, 90f), 0f);
            c.stone.Box(Matrix4x4.TRS(p + Vector3.up * h * 0.5f, q, new Vector3(w, h, dpt)), Vary(new Color(0.9f, 0.84f, 0.72f), rng, 0.08f));
            c.stone.Cone(Matrix4x4.TRS(p + Vector3.up * (h + 0.9f), q * Quaternion.Euler(0f, 45f, 0f), new Vector3(Mathf.Max(w, dpt) * 1.35f, 1.8f, Mathf.Max(w, dpt) * 1.35f)), 4, Vary(new Color(0.72f, 0.32f, 0.2f), rng, 0.1f));
        }
    }

    // ── The emperor's box ──

    private static void EmperorBox(Ctx c)
    {
        ArenaBasis e = At(EmperorDeg, 0f, PodiumTop);
        ArenaKit k = c.stone, m = c.metal;

        // Porphyry and gold, jutting out over the sand
        k.Box(e.M(0f, -3.1f, 2.25f, 16f, 7.8f, 4.5f), Porphyry, ArenaKit.All, Marble);
        k.Box(e.M(0f, 0.4f, -1.5f, 16f, 0.8f, 3f), Marble);
        m.Box(e.M(0f, 0.62f, 2.25f, 16.3f, 0.36f, 4.75f), Gold);
        m.Box(e.M(0f, -6.7f, 2.25f, 16.3f, 0.6f, 4.75f), Gold);
        for (int j = -1; j <= 1; j++)
        {
            m.Cylinder(e.M(j * 5f, -3.2f, 4.55f, 2.2f, 0.18f, 2.2f, 90f, 0f, 0f), 12, Gold);
            k.Cylinder(e.M(j * 5f, -3.2f, 4.62f, 1.4f, 0.12f, 1.4f, 90f, 0f, 0f), 12, Purple);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box(e.M(side * 9.3f, -3.3f, 0.25f, 2.2f, 7.4f, 0.12f), Purple);   // drapes down the wall either side
            m.Box(e.M(side * 9.3f, -6.8f, 0.3f, 2.2f, 0.4f, 0.12f), Gold);
        }

        // Balustrade
        for (float x = -7.5f; x <= 7.51f; x += 0.75f) k.Box(e.M(x, 1.35f, 4.25f, 0.22f, 1.1f, 0.22f), Marble, ArenaKit.Seen);
        m.Box(e.M(0f, 1.95f, 4.25f, 16f, 0.18f, 0.4f), Gold);

        // Columns, canopy, pediment and the golden eagle
        foreach (float z in new[] { 3.8f, -2.4f })
            foreach (float x in new[] { -7.3f, -2.6f, 2.6f, 7.3f })
            {
                k.Cylinder(e.M(x, 4.4f, z, 0.8f, 7.2f, 0.8f), 10, Marble);
                k.Box(e.M(x, 1.0f, z, 1.1f, 0.4f, 1.1f), Marble);
                m.Box(e.M(x, 8.0f, z, 1.2f, 0.45f, 1.2f), Gold);
            }
        k.Box(e.M(0f, 8.55f, 0.7f, 17f, 0.7f, 7.6f), Marble);
        m.Box(e.M(0f, 8.55f, 4.52f, 17f, 0.3f, 0.06f), Gold);
        k.Box(e.M(0f, 4.6f, -3.2f, 16.5f, 8f, 0.6f), Purple);   // purple hangings behind the throne
        k.Box(e.M(0f, 7.7f, 4.45f, 16.5f, 1.0f, 0.08f), Purple); // valance
        m.Box(e.M(0f, 7.15f, 4.48f, 16.5f, 0.18f, 0.08f), Gold);
        for (int side = -1; side <= 1; side += 2) k.Box(e.M(side * 7.95f, 4.6f, 4.3f, 0.9f, 6.4f, 0.15f), Purple);

        k.Tri(e.P(-8.5f, 8.9f, 4.4f), e.P(0f, 11.4f, 4.4f), e.P(8.5f, 8.9f, 4.4f), Marble, e.f);
        k.Tri(e.P(-6.6f, 9.1f, 4.46f), e.P(0f, 11.0f, 4.46f), e.P(6.6f, 9.1f, 4.46f), Purple, e.f);
        k.Quad(e.P(-8.5f, 8.9f, 4.4f), e.P(-8.5f, 8.9f, -3.1f), e.P(0f, 11.4f, -3.1f), e.P(0f, 11.4f, 4.4f), MarbleDark, Vector3.up - e.r);
        k.Quad(e.P(8.5f, 8.9f, 4.4f), e.P(0f, 11.4f, 4.4f), e.P(0f, 11.4f, -3.1f), e.P(8.5f, 8.9f, -3.1f), MarbleDark, Vector3.up + e.r);
        Eagle(m, e.Sub(0f, 11.4f, 4.0f, 0f, 1.3f));
        Eagle(m, e.Sub(0f, 9.4f, 4.5f, 0f, 0.6f));

        // Throne and emperor
        m.Box(e.M(0f, 1.3f, 0.6f, 2.6f, 1.0f, 1.8f), Gold);
        k.Box(e.M(0f, 2.9f, -0.15f, 2.6f, 3.2f, 0.35f), Purple);
        m.Box(e.M(0f, 4.6f, -0.15f, 2.9f, 0.3f, 0.5f), Gold);
        for (int side = -1; side <= 1; side += 2) m.Box(e.M(side * 1.2f, 2.25f, 0.6f, 0.25f, 0.9f, 1.6f), Gold);
        ArenaBasis em = e.Sub(0f, 0f, 0f, 0f, 1.25f);
        Color skin = Skins[1];
        k.Box(em.M(0f, 1.72f, 0.88f, 1.0f, 0.5f, 1.0f), Purple);
        k.Box(em.M(0f, 2.45f, 0.45f, 0.95f, 1.25f, 0.62f), Purple, ArenaKit.Seen);
        k.Box(em.M(0.08f, 2.45f, 0.78f, 0.32f, 1.3f, 0.05f, 0f, 0f, 28f), Marble);
        k.Box(em.M(0f, 3.33f, 0.48f, 0.5f, 0.54f, 0.5f), skin, ArenaKit.Seen, Hair[3]);
        m.Box(em.M(0f, 3.56f, 0.48f, 0.6f, 0.13f, 0.6f), new Color(0.68f, 0.78f, 0.3f));   // laurel crown
        k.Box(em.M(-0.6f, 2.4f, 0.6f, 0.25f, 0.9f, 0.25f), Purple, ArenaKit.Seen);

        // His right arm moves on its own: thumb up now and then
        var arm = new ArenaKit(c.pal);
        ArenaBasis al = em.Local;
        arm.Box(al.M(0f, -0.45f, 0f, 0.25f, 0.9f, 0.25f), Purple, ArenaKit.All);
        arm.Box(al.M(0f, -1.0f, 0f, 0.27f, 0.27f, 0.27f), skin, ArenaKit.All);
        arm.Box(al.M(0f, -1.22f, 0f, 0.09f, 0.22f, 0.09f), skin, ArenaKit.All);
        c.life.Arm = Piece(c.root, "Emperor Arm", arm, c.lit, em.P(0.6f, 2.95f, 0.45f));
        c.life.ArmAxis = e.r;

        // Guards with tall red shields
        for (int side = -1; side <= 1; side += 2)
        {
            ArenaBasis g = e.Sub(side * 5.3f, 0.8f, 2.7f, 0f, 1.15f);
            Figure(k, g, Red, Skins[2], Hair[0], false);
            m.Box(g.M(0f, 1.15f, 0.05f, 0.6f, 0.55f, 0.36f), Gold);                         // breastplate
            m.Box(g.M(0f, 1.82f, 0f, 0.36f, 0.22f, 0.36f), Gold);                           // helmet
            k.Box(g.M(0f, 2.05f, 0f, 0.08f, 0.3f, 0.5f), Red);                              // crest
            k.Box(g.M(side * 0.45f, 1.0f, 0.3f, 0.12f, 1.4f, 0.9f), Red, ArenaKit.All);     // shield
            m.Box(g.M(side * 0.52f, 1.0f, 0.3f, 0.06f, 0.25f, 0.25f), Gold);
            k.Box(g.M(-side * 0.42f, 1.5f, 0.15f, 0.07f, 3.2f, 0.07f), Wood);               // spear
            m.Box(g.M(-side * 0.42f, 3.25f, 0.15f, 0.12f, 0.35f, 0.04f), new Color(0.8f, 0.8f, 0.78f));
        }

        // Braziers on the sand either side
        for (int side = -1; side <= 1; side += 2)
        {
            ArenaBasis br = At(EmperorDeg + side * 9f, -2.5f, GroundY);
            for (int leg = 0; leg < 3; leg++)
            {
                float a = leg * 120f;
                Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.55f;
                m.Box(Matrix4x4.TRS(br.o + off + Vector3.up * 1.7f, Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(-10f, 0f, 0f), new Vector3(0.18f, 3.4f, 0.18f)), Bronze);
            }
            m.Cylinder(Matrix4x4.TRS(br.o + Vector3.up * 3.5f, Quaternion.identity, new Vector3(2.2f, 0.7f, 2.2f)), 10, Bronze, true, new Color(0.25f, 0.1f, 0.05f));
            AddFlame(c, br.o + Vector3.up * 3.8f, 2.2f);
        }

        // Rose petals thrown on the sand in front of the box
        var rng = c.rng;
        Color[] petals = { new Color(0.95f, 0.35f, 0.5f), new Color(0.85f, 0.15f, 0.25f), new Color(1f, 0.7f, 0.78f), new Color(1f, 0.92f, 0.94f) };
        for (int j = 0; j < 220; j++)
        {
            Vector3 p = e.P(R(rng, -11f, 11f), GroundY - PodiumTop + 0.03f, R(rng, 4.8f, 6f + R(rng) * R(rng) * 16f));
            k.Box(Matrix4x4.TRS(p, Quaternion.Euler(0f, R(rng, 0f, 180f), 0f), new Vector3(0.5f, 0.02f, 0.32f)), petals[rng.Next(petals.Length)], ArenaKit.PY);
        }
    }

    // A golden eagle, wings spread, facing out of b
    private static void Eagle(ArenaKit m, ArenaBasis b)
    {
        m.Box(b.M(0f, 0.8f, 0f, 0.9f, 1.4f, 0.8f), Gold);
        m.Box(b.M(0f, 1.7f, 0.25f, 0.5f, 0.5f, 0.6f), Gold);
        m.Box(b.M(0f, 1.6f, 0.65f, 0.18f, 0.18f, 0.35f), new Color(1f, 0.85f, 0.4f));
        for (int side = -1; side <= 1; side += 2)
        {
            m.Box(b.M(side * 1.45f, 1.25f, 0f, 2.3f, 0.3f, 0.9f, 0f, 0f, side * 28f), Gold);
            m.Box(b.M(side * 2.7f, 2.05f, 0f, 1.2f, 0.25f, 0.7f, 0f, 0f, side * 50f), Gold);
        }
        m.Box(b.M(0f, 0.05f, -0.3f, 0.7f, 0.25f, 0.8f), Gold);
    }

    // A standing figure about 1.75 tall at b's origin, facing b's forward
    private static void Figure(ArenaKit k, ArenaBasis b, Color tunic, Color skin, Color hair, bool arms = true)
    {
        k.Box(b.M(-0.13f, 0.42f, 0f, 0.2f, 0.84f, 0.24f), skin * 0.85f, ArenaKit.Seen);
        k.Box(b.M(0.13f, 0.42f, 0f, 0.2f, 0.84f, 0.24f), skin * 0.85f, ArenaKit.Seen);
        k.Box(b.M(0f, 1.12f, 0f, 0.56f, 0.66f, 0.32f), tunic, ArenaKit.Seen);
        k.Box(b.M(0f, 1.62f, 0.02f, 0.3f, 0.32f, 0.3f), skin, ArenaKit.Seen, hair);
        if (!arms) return;
        k.Box(b.M(-0.36f, 1.1f, 0f, 0.13f, 0.6f, 0.15f), skin, ArenaKit.Seen);
        k.Box(b.M(0.36f, 1.1f, 0f, 0.13f, 0.6f, 0.15f), skin, ArenaKit.Seen);
    }

    // ── Musicians with curled horns and a water organ ──

    private static void Musicians(Ctx c)
    {
        ArenaBasis mu = At(MusicDeg, -5.5f, GroundY);
        c.stone.Box(mu.M(0f, 0.4f, 0f, 10f, 0.8f, 4.2f), Wood, ArenaKit.All, new Color(0.6f, 0.44f, 0.28f));
        Color[] coats = { new Color(0.2f, 0.45f, 0.35f), new Color(0.75f, 0.55f, 0.2f), new Color(0.6f, 0.2f, 0.2f) };
        for (int j = 0; j < 3; j++)
        {
            ArenaBasis local = mu.Local;
            var player = new ArenaKit(c.pal);
            ArenaBasis pb = local.Sub(0f, 0f, 0f, 0f, 1.25f);
            Figure(player, pb, coats[j], Skins[j + 1], Hair[j], false);
            player.Box(pb.M(-0.3f, 1.25f, 0.25f, 0.13f, 0.13f, 0.5f), Skins[j + 1]);   // arms up to the horn
            player.Box(pb.M(0.3f, 1.25f, 0.25f, 0.13f, 0.13f, 0.5f), Skins[j + 1]);
            // The cornu: a great ring of brass round the player, its bell over the shoulder
            for (int s = 0; s < 16; s++)
            {
                float a = s / 16f * Mathf.PI * 2f;
                player.Box(pb.M(new Vector3(Mathf.Cos(a) * 1.05f, 1.15f + Mathf.Sin(a) * 1.05f, 0.45f), new Vector3(0.45f, 0.13f, 0.13f), new Vector3(0f, 0f, a * Mathf.Rad2Deg + 90f)), Gold);
            }
            player.Cone(pb.M(new Vector3(0.75f, 2.35f, 0.45f), new Vector3(0.7f, 0.6f, 0.7f), new Vector3(0f, 0f, -35f)), 8, Gold);
            player.Box(pb.M(new Vector3(0f, 1.15f, 0.42f), new Vector3(2.1f, 0.08f, 0.08f), new Vector3(0f, 0f, 35f)), DarkWood);
            Vector3 at = mu.P(-3.2f + j * 3.2f, 0.8f, 0.3f);
            Transform t = Piece(c.root, "Horn Player", player, c.shiny, at);
            c.life.Bobbers.Add((t, at, j * 1.9f));
        }
        // The water organ and its organist
        ArenaKit m = c.metal;
        m.Box(mu.M(6.4f, 1.6f, -0.3f, 2f, 1.6f, 1.4f), Bronze);
        for (int p = 0; p < 9; p++)
        {
            float h = 1.4f + 1.7f * Mathf.Sin(p / 8f * Mathf.PI);
            m.Cylinder(mu.M(5.6f + p * 0.2f, 2.4f + h * 0.5f, -0.3f, 0.17f, h, 0.17f), 6, Gold);
        }
        Figure(c.stone, mu.Sub(6.4f, 0.8f, -1.5f, 0f, 1.2f), new Color(0.35f, 0.3f, 0.6f), Skins[0], Hair[2]);
    }

    // ── A parked chariot ──

    private static void Chariot(Ctx c)
    {
        ArenaBasis ch = At(ChariotDeg, -9f, GroundY).Sub(0f, 0f, 0f, 65f, 1.3f);
        ArenaKit k = c.stone, m = c.metal;
        k.Box(ch.M(0f, 1.0f, 0f, 1.8f, 0.15f, 1.4f), Wood);
        k.Box(ch.M(0f, 1.65f, 0.7f, 1.8f, 1.2f, 0.12f), Red);
        for (int side = -1; side <= 1; side += 2) k.Box(ch.M(side * 0.9f, 1.45f, 0.2f, 0.12f, 0.9f, 1.0f), Red);
        m.Box(ch.M(0f, 2.28f, 0.7f, 1.9f, 0.1f, 0.16f), Gold);
        k.Box(ch.M(0f, 1.05f, -0.1f, 2.6f, 0.12f, 0.12f), DarkWood);
        for (int side = -1; side <= 1; side += 2)
        {
            m.Cylinder(ch.M(side * 1.2f, 1.05f, -0.1f, 2.1f, 0.12f, 2.1f, 0f, 0f, 90f), 14, Gold, true, DarkWood);
            for (int s = 0; s < 4; s++) k.Box(ch.M(side * 1.27f, 1.05f, -0.1f, 0.06f, 1.95f, 0.08f, s * 45f, 0f, 0f), DarkWood);
        }
        k.Box(ch.M(0f, 0.85f, 2.4f, 0.15f, 0.15f, 3.4f, -8f, 0f, 0f), Wood);
        k.Box(ch.M(0f, 0.6f, 4.0f, 2.0f, 0.15f, 0.15f), Wood);
    }

    // ── The great gate and its giants ──

    private static void Gate(Ctx c)
    {
        ArenaBasis g = At(GateDeg, 0f, GroundY);
        ArenaKit k = c.stone, m = c.metal;
        Color face = new Color(0.88f, 0.81f, 0.67f), inside = new Color(0.2f, 0.15f, 0.12f);

        for (int side = -1; side <= 1; side += 2)
        {
            k.Box(g.M(side * 8.25f, 13f, -6.5f, 6.5f, 26f, 18f), face);
            k.Quad(g.P(side * 4.98f, 0f, 2.5f), g.P(side * 4.98f, 11f, 2.5f), g.P(side * 4.98f, 11f, -15.5f), g.P(side * 4.98f, 0f, -15.5f), inside, -g.r * side);
            k.Quad(g.P(side * 4.98f, 0f, 2.3f), g.P(side * 4.98f, 11f, 2.3f), g.P(side * 4.98f, 11f, 1.2f), g.P(side * 4.98f, 0f, 1.2f), face * 0.7f, -g.r * side);
            k.Box(g.M(side * 9.6f, 11f, 2.7f, 1.4f, 20f, 0.4f), Marble);   // pilasters
            m.Box(g.M(side * 9.6f, 21.2f, 2.75f, 1.8f, 0.6f, 0.5f), Gold);
        }
        k.Box(g.M(0f, 21.5f, -6.5f, 10f, 9f, 18f), face);
        // The arch: stepped fill under the top block, light at the mouth and dark inside
        for (int j = 0; j < 10; j++)
        {
            float x = -4.5f + j;
            float edge = Mathf.Abs(x) + 0.5f;
            float ye = 11f + Mathf.Sqrt(Mathf.Max(0f, 25f - edge * edge));
            k.Box(g.M(x, (ye + 17f) * 0.5f, 1.25f, 1f, 17f - ye, 2.5f), face);
            k.Box(g.M(x, (ye + 17f) * 0.5f, -7f, 1f, 17f - ye, 14.5f), inside);
        }
        for (int v = 0; v < 11; v++)
        {
            float a = (v + 0.5f) / 11f * Mathf.PI;
            k.Box(g.M(new Vector3(Mathf.Cos(a) * 5.5f, 11f + Mathf.Sin(a) * 5.5f, 2.6f), new Vector3(1.5f, 0.9f, 0.4f), new Vector3(0f, 0f, a * Mathf.Rad2Deg + 90f)), v == 5 ? Gold : Marble);
        }
        // Tunnel floor, and torchlight glowing at the far end
        k.Quad(g.P(-5f, 0.03f, -15.5f), g.P(-5f, 0.03f, 2.5f), g.P(5f, 0.03f, 2.5f), g.P(5f, 0.03f, -15.5f), new Color(0.42f, 0.34f, 0.25f), Vector3.up);
        k.Quad(g.P(-5f, 0f, -15.4f), g.P(-5f, 17f, -15.4f), g.P(5f, 17f, -15.4f), g.P(5f, 0f, -15.4f), inside * 0.6f, g.f);
        c.glow.Quad(g.P(-2.6f, 0f, -15.3f), g.P(-2.6f, 6.5f, -15.3f), g.P(2.6f, 6.5f, -15.3f), g.P(2.6f, 0f, -15.3f), new Color(1f, 0.6f, 0.25f), g.f);
        c.glow.Tri(g.P(-1.8f, 6.5f, -15.3f), g.P(0f, 8.2f, -15.3f), g.P(1.8f, 6.5f, -15.3f), new Color(1f, 0.6f, 0.25f), g.f);
        for (int side = -1; side <= 1; side += 2)
            for (int d = 0; d < 2; d++)
            {
                Vector3 at = g.P(side * 4.6f, 5.2f, -5f - d * 6f);
                m.Box(Matrix4x4.TRS(at, g.rot, new Vector3(0.25f, 0.9f, 0.25f)), Bronze);
                AddFlame(c, at + Vector3.up * 0.45f, 0.9f);
            }

        // The portcullis, raised into the arch
        for (int j = 0; j < 13; j++)
        {
            float x = -4.5f + j * 0.75f;
            float top = 11f + Mathf.Sqrt(Mathf.Max(0f, 25f - x * x));
            if (top < 12.6f) continue;
            m.Box(g.M(x, (12.4f + top) * 0.5f, 0.6f, 0.14f, top - 12.4f, 0.14f), Iron);
            m.Cone(g.M(new Vector3(x, 12.15f, 0.6f), new Vector3(0.22f, 0.5f, 0.22f), new Vector3(180f, 0f, 0f)), 4, Iron);
        }
        for (int j = 0; j < 4; j++) m.Box(g.M(0f, 12.6f + j * 0.95f, 0.6f, 9.6f, 0.14f, 0.14f), Iron);

        // An inscription band of gold strokes, and a laurel wreath above it
        k.Box(g.M(0f, 19.9f, 2.6f, 12.5f, 1.7f, 0.1f), DeepRed);
        var rng = c.rng;
        for (float x = -5.6f; x < 5.6f;)
        {
            float w = R(rng, 0.3f, 0.55f);
            m.Box(g.M(x + w * 0.5f, 19.9f, 2.68f, w, 1.0f, 0.06f), Gold);
            x += w + (R(rng) < 0.18f ? 0.75f : 0.22f);
        }
        for (int side = -1; side <= 1; side += 2)
            for (int j = 0; j < 9; j++)
            {
                float a = (-80f + j * 19f) * Mathf.Deg2Rad;
                m.Box(g.M(new Vector3(side * Mathf.Cos(a) * 1.7f, 23.6f + Mathf.Sin(a) * 1.7f, 2.65f), new Vector3(0.75f, 0.32f, 0.12f), new Vector3(0f, 0f, side * (a * Mathf.Rad2Deg + 90f) + side * 30f)), Gold);
            }

        // Trophies of arms on the top corners
        for (int side = -1; side <= 1; side += 2)
        {
            ArenaBasis t = g.Sub(side * 8.25f, 26f, -1f, 0f, 1.6f);
            m.Box(t.M(0f, 1.2f, 0f, 0.2f, 2.4f, 0.2f), Bronze);
            m.Box(t.M(0f, 2.1f, 0f, 1.2f, 1.1f, 0.6f), Gold);
            m.Box(t.M(0f, 2.95f, 0f, 0.55f, 0.55f, 0.55f), Gold);
            k.Box(t.M(0f, 3.35f, 0f, 0.12f, 0.4f, 0.7f), Red);
            m.Cylinder(t.M(0f, 1.9f, 0.4f, 1.5f, 0.12f, 1.5f, 90f, 0f, 0f), 10, Bronze);
            for (int s = -1; s <= 1; s += 2) m.Box(t.M(0f, 2.2f, -0.1f, 0.08f, 3.6f, 0.08f, 0f, 0f, s * 30f), Bronze);
        }

        // Torches either side of the gate
        for (int side = -1; side <= 1; side += 2)
        {
            m.Box(g.M(side * 12.6f, 3.5f, 3.5f, 0.3f, 7f, 0.3f), Bronze);
            m.Cylinder(g.M(side * 12.6f, 7.1f, 3.5f, 1.1f, 0.4f, 1.1f), 8, Bronze, true, new Color(0.25f, 0.1f, 0.05f));
            AddFlame(c, g.P(side * 12.6f, 7.3f, 3.5f), 1.4f);
        }

        // Dropped shields, helmets and spears on the sand in front
        Color[] shields = { Red, Blues, Greens, new Color(0.85f, 0.65f, 0.2f) };
        for (int j = 0; j < 9; j++)
        {
            Vector3 p = g.P(R(rng, -16f, 16f), 0.12f, R(rng, 5f, 22f));
            Quaternion q = Quaternion.Euler(R(rng, -12f, 12f), R(rng, 0f, 180f), R(rng, -12f, 12f));
            if (j % 3 == 2)
            {
                k.Box(Matrix4x4.TRS(p + Vector3.up * 0.3f, q, new Vector3(0.7f, 0.6f, 0.7f)), Bronze, ArenaKit.All & ~ArenaKit.NY);
                k.Box(Matrix4x4.TRS(p + Vector3.up * 0.75f, q, new Vector3(0.12f, 0.35f, 0.75f)), Red, ArenaKit.All & ~ArenaKit.NY);
            }
            else
            {
                k.Cylinder(Matrix4x4.TRS(p, q, new Vector3(1.9f, 0.14f, 1.9f)), 10, shields[rng.Next(shields.Length)]);
                m.Cylinder(Matrix4x4.TRS(p + q * Vector3.up * 0.1f, q, new Vector3(0.5f, 0.12f, 0.5f)), 8, Gold);
            }
            Vector3 sp = g.P(R(rng, -16f, 16f), 0.1f, R(rng, 5f, 22f));
            k.Box(Matrix4x4.TRS(sp, Quaternion.Euler(0f, R(rng, 0f, 180f), 0f), new Vector3(0.09f, 0.09f, R(rng, 2f, 3.6f))), Wood, ArenaKit.All & ~ArenaKit.NY);
        }
    }

    // A colossal bronze gladiator on a plinth: sword raised and big shield, or trident and net
    private static void Colossus(Ctx c, ArenaBasis at, bool trident)
    {
        ArenaKit k = c.stone, m = c.metal;
        k.Box(at.M(0f, 2.5f, 0f, 7f, 5f, 7f), Marble);
        m.Box(at.M(0f, 4.8f, 0f, 7.3f, 0.4f, 7.3f), Gold);
        m.Box(at.M(0f, 0.3f, 0f, 7.3f, 0.6f, 7.3f), Gold);
        ArenaBasis s = at.Sub(0f, 5f, 0f, trident ? 10f : -10f, 1f);
        Color b = Bronze, dark = Bronze * 0.75f;

        for (int side = -1; side <= 1; side += 2)
        {
            m.Box(s.M(side * 1.2f, 0.35f, 0.3f, 1.5f, 0.7f, 2.4f), dark);
            m.Box(s.M(side * 1.2f, 4.7f, 0f, 1.6f, 8f, 1.8f), b);
            m.Box(s.M(side * 1.2f, 3.0f, 0.12f, 1.75f, 4.2f, 1.85f), Gold);   // greaves
        }
        m.Box(s.M(0f, 9.6f, 0f, 4f, 2.2f, 2.2f), dark);
        m.Box(s.M(0f, 10.7f, 0f, 4.3f, 0.9f, 2.5f), Gold);
        m.Box(s.M(0f, 12.4f, 0f, 3.4f, 2.6f, 2.1f), b);
        m.Box(s.M(0f, 15f, 0f, 4.6f, 2.8f, 2.5f), b);
        m.Box(s.M(0f, 16.8f, 0f, 1.1f, 0.9f, 1.1f), b);
        m.Box(s.M(0f, 18.3f, 0f, 2.3f, 2.4f, 2.3f), b);
        m.Box(s.M(0f, 17.3f, 0f, 3.3f, 0.3f, 3.3f), Gold);
        m.Box(s.M(0f, 18.2f, 1.17f, 1.6f, 1.4f, 0.06f), new Color(0.2f, 0.15f, 0.1f));   // visor grille

        if (!trident)
        {
            k.Box(s.M(0f, 20.3f, -0.1f, 0.5f, 1.8f, 3.4f), new Color(0.78f, 0.12f, 0.15f));   // crimson crest
            m.Box(s.M(0f, 19.6f, -0.1f, 0.7f, 0.3f, 3.2f), Gold);
            // Sword arm thrust up to the sky
            m.Box(s.M(2.6f, 15.6f, 0f, 1.4f, 1.4f, 1.6f), Gold);
            m.Box(s.M(2.95f, 17.5f, 0.2f, 1.1f, 3.4f, 1.1f, 0f, 0f, -8f), b);
            m.Box(s.M(3.15f, 20.8f, 0.3f, 1.05f, 3.2f, 1.05f), Gold);   // arm guard
            m.Box(s.M(3.2f, 22.6f, 0.3f, 1.1f, 1.1f, 1.1f), b);
            m.Box(s.M(3.2f, 23.25f, 0.3f, 1.4f, 0.25f, 0.5f), Gold);
            m.Box(s.M(3.2f, 25.6f, 0.3f, 0.45f, 4.5f, 0.18f), new Color(0.86f, 0.86f, 0.82f));
            m.Cone(s.M(3.2f, 28.1f, 0.3f, 0.45f, 0.6f, 0.18f), 4, new Color(0.86f, 0.86f, 0.82f));
            // Shield arm forward with the great curved shield
            m.Box(s.M(-2.9f, 14.3f, 0.6f, 1f, 3f, 1f, 30f, 0f, 0f), b);
            m.Box(s.M(-2.6f, 12.8f, 1.9f, 0.95f, 0.95f, 2.6f), b);
            m.Box(s.M(-2.5f, 11.8f, 3.4f, 4.4f, 7.6f, 0.45f, 0f, -12f, 0f), b);
            m.Box(s.M(-2.5f, 11.8f, 3.65f, 4.6f, 0.35f, 0.3f, 0f, -12f, 0f), Gold);
            m.Cylinder(s.M(-2.5f, 11.8f, 3.75f, 1.4f, 0.5f, 1.4f, 90f, -12f, 0f), 10, Gold);
        }
        else
        {
            m.Box(s.M(0f, 19.65f, 0f, 0.6f, 0.4f, 0.6f), Gold);
            // Trident planted at his side
            m.Box(s.M(3.2f, 14.2f, 0.2f, 1f, 3.2f, 1f, 0f, 0f, 15f), b);
            m.Box(s.M(3.8f, 12.6f, 1f, 0.95f, 2.6f, 0.95f, -50f, 0f, 0f), b);
            m.Box(s.M(3.9f, 12.3f, 2.0f, 1.05f, 1.05f, 1.05f), b);
            m.Box(s.M(3.9f, 13.5f, 2.1f, 0.35f, 27f, 0.35f), Gold);
            m.Box(s.M(3.9f, 27.2f, 2.1f, 2.0f, 0.3f, 0.3f), Gold);
            m.Box(s.M(3.9f, 28.4f, 2.1f, 0.3f, 2.4f, 0.3f), Gold);
            for (int side = -1; side <= 1; side += 2) m.Box(s.M(3.9f + side * 0.85f, 28.1f, 2.1f, 0.26f, 1.8f, 0.26f), Gold);
            // Tall shoulder guard, and the net bundled in his other hand
            m.Box(s.M(-2.9f, 17.4f, 0f, 0.4f, 2.6f, 1.8f, 0f, 0f, -15f), Gold);
            m.Box(s.M(-2.6f, 15.5f, 0f, 1.5f, 1.4f, 1.7f), b);
            m.Box(s.M(-3.0f, 13.4f, 0.3f, 1f, 4.6f, 1f, 0f, 0f, 8f), b);
            m.Box(s.M(-3.3f, 10.4f, 0.5f, 1.8f, 1.8f, 1.8f, 20f, 30f, 10f), dark);
        }
    }

    // ── The lion's lift ──

    private static void LionLift(Ctx c)
    {
        ArenaBasis li = At(LionDeg, -13f, GroundY);
        ArenaKit k = c.stone;
        k.Quad(li.P(-2.6f, 0.04f, -2.6f), li.P(-2.6f, 0.04f, 2.6f), li.P(2.6f, 0.04f, 2.6f), li.P(2.6f, 0.04f, -2.6f), new Color(0.05f, 0.04f, 0.035f), Vector3.up);
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box(li.M(side * 2.8f, 0.12f, 0f, 0.4f, 0.24f, 6f), DarkWood);
            k.Box(li.M(0f, 0.12f, side * 2.8f, 6f, 0.24f, 0.4f), DarkWood);
            k.Box(li.M(side * 4.4f, 0.12f, 0f, 2.6f, 0.2f, 5f), Wood);   // the hatch doors, flung open
            for (int p = 0; p < 3; p++) k.Box(li.M(side * 4.4f, 0.24f, -1.6f + p * 1.6f, 2.6f, 0.06f, 0.18f), Iron);
        }
        // Closed hatches round about
        Vector3[] hatches = { new Vector3(-10f, 0f, 3f), new Vector3(9f, 0f, -2f), new Vector3(-2f, 0f, 10f), new Vector3(12f, 0f, 8f) };
        foreach (Vector3 h in hatches)
        {
            k.Box(li.M(h.x, 0.1f, h.z, 4f, 0.2f, 4f), Wood, ArenaKit.All & ~ArenaKit.NY);
            for (int p = 0; p < 2; p++) k.Box(li.M(h.x, 0.22f, h.z - 1f + p * 2f, 4.05f, 0.06f, 0.2f), Iron);
        }

        // The cage rises and sinks on its own; the lion paces inside it
        ArenaBasis l = li.Local;
        var cage = new ArenaKit(c.pal);
        cage.Box(l.M(0f, 0.2f, 0f, 4.6f, 0.4f, 4.6f), Wood);
        cage.Box(l.M(0f, 4.6f, 0f, 4.7f, 0.25f, 4.7f), Wood);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                cage.Box(l.M(sx * 2.2f, 2.4f, sz * 2.2f, 0.3f, 4.2f, 0.3f), Wood);
        for (int b = 0; b < 9; b++)
        {
            float x = -1.8f + b * 0.45f;
            for (int side = -1; side <= 1; side += 2)
            {
                cage.Box(l.M(x, 2.4f, side * 2.2f, 0.09f, 4f, 0.09f), Iron);
                cage.Box(l.M(side * 2.2f, 2.4f, x, 0.09f, 4f, 0.09f), Iron);
            }
        }
        Transform cageT = Piece(c.root, "Lion Cage", cage, c.lit, li.o);
        c.life.Cage = cageT;
        c.life.CageRest = li.o;

        var lion = new ArenaKit(c.pal);
        ArenaBasis lb = l.Sub(0f, 0.4f, 0f, 0f, 1f);
        Color tawny = new Color(0.82f, 0.6f, 0.3f), mane = new Color(0.5f, 0.27f, 0.12f);
        lion.Box(lb.M(0f, 1.35f, 0f, 0.9f, 0.85f, 2.0f), tawny);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                lion.Box(lb.M(sx * 0.3f, 0.5f, sz * 0.7f, 0.25f, 1f, 0.25f), tawny * 0.92f);
        lion.Box(lb.M(0f, 1.85f, 0.95f, 1.2f, 1.2f, 0.7f), mane);
        lion.Box(lb.M(0f, 1.8f, 1.25f, 0.62f, 0.62f, 0.62f), tawny);
        lion.Box(lb.M(0f, 1.62f, 1.6f, 0.36f, 0.3f, 0.2f), new Color(0.95f, 0.82f, 0.62f));
        lion.Box(lb.M(0f, 1.7f, 1.71f, 0.12f, 0.1f, 0.04f), new Color(0.15f, 0.08f, 0.06f));
        lion.Box(lb.M(0f, 1.55f, -1.3f, 0.1f, 0.1f, 0.9f, -30f, 0f, 0f), tawny);
        lion.Box(lb.M(0f, 1.85f, -1.72f, 0.22f, 0.22f, 0.25f), mane);
        Transform lionT = Piece(cageT, "Lion", lion, c.lit, Vector3.zero);
        c.life.Lion = lionT;
        c.life.LionAcross = li.r;
    }

    // ── Weapon racks and a training post on the Blues-and-Greens side ──

    private static void Racks(Ctx c)
    {
        ArenaBasis w = At(RackDeg, -4.5f, GroundY);
        ArenaKit k = c.stone, m = c.metal;
        var rng = c.rng;
        for (int rack = -1; rack <= 1; rack += 2)
        {
            ArenaBasis rb = w.Sub(rack * 4f, 0f, 0f, 0f, 1.2f);
            for (int side = -1; side <= 1; side += 2) k.Box(rb.M(side * 1.6f, 1.1f, 0f, 0.18f, 2.3f, 0.18f, 0f, 0f, side * 6f), Wood);
            k.Box(rb.M(0f, 2.1f, 0f, 3.6f, 0.15f, 0.18f), Wood);
            k.Box(rb.M(0f, 0.6f, 0.25f, 3.4f, 0.12f, 0.15f), Wood);
            for (int s = 0; s < 6; s++)
            {
                float x = -1.3f + s * 0.52f;
                k.Box(rb.M(x, 1.7f, 0.45f, 0.07f, 3.4f, 0.07f, -14f, 0f, 0f), Wood);
                m.Cone(rb.M(x, 3.42f, 0.03f, 0.14f, 0.35f, 0.06f, -14f, 0f, 0f), 4, new Color(0.82f, 0.82f, 0.8f));
            }
            for (int s = 0; s < 2; s++)
                k.Cylinder(rb.M(new Vector3(-0.8f + s * 1.6f, 0.95f, 1.05f), new Vector3(1.6f, 0.12f, 1.6f), new Vector3(-75f, R(rng, -10f, 10f), 0f)), 10, s == 0 ? Blues : Greens);
        }
        // The training post
        k.Box(w.M(8.5f, 1.6f, 0.5f, 0.5f, 3.2f, 0.5f), Wood);
        k.Box(w.M(8.5f, 2.4f, 0.5f, 1.3f, 0.25f, 0.25f), Wood);
        // Barrels
        for (int j = 0; j < 3; j++)
            k.Cylinder(w.M(-8.5f + j * 1.1f, 0.6f, 0.3f * j, 1f, 1.2f, 1f), 9, DarkWood, true, Wood);
    }

    // ── Fire ──

    // One shared flame shape: three nested tongues of orange, yellow and white
    private static Mesh FlameMesh(Ctx c)
    {
        var k = new ArenaKit(c.pal);
        k.Box(Matrix4x4.TRS(new Vector3(0f, 0.5f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.7f, 1.1f, 0.7f)), new Color(1f, 0.42f, 0.08f));
        k.Box(Matrix4x4.TRS(new Vector3(0f, 0.45f, 0f), Quaternion.Euler(0f, 20f, 0f), new Vector3(0.5f, 0.95f, 0.5f)), new Color(1f, 0.72f, 0.2f));
        k.Box(Matrix4x4.TRS(new Vector3(0f, 0.35f, 0f), Quaternion.identity, new Vector3(0.3f, 0.65f, 0.3f)), new Color(1f, 0.96f, 0.7f));
        return Keep(k.Build("Arena Flame"));
    }

    private static void AddFlame(Ctx c, Vector3 at, float size)
    {
        Transform t = Piece(c.root, "Flame", c.flame, c.bright, at);
        t.localScale = Vector3.one * size;
        c.life.Flames.Add((t, t.localScale, at.x * 0.37f + at.z * 0.61f));
    }

    // Torches round the top of the arena wall, except where the box, the gate and the ruin are
    private static void Torches(Ctx c)
    {
        for (float deg = 7.5f; deg < 360f; deg += 15f)
        {
            if (Near(deg, EmperorDeg, 11f) || Near(deg, GateDeg, 14f) || Near(deg, RuinDeg, RuinHalf * 0.7f)) continue;
            ArenaBasis b = At(deg, 0.3f, PodiumTop + 1.2f);
            c.metal.Box(b.M(0f, 0.9f, 0f, 0.2f, 1.8f, 0.2f), Bronze);
            c.metal.Cylinder(b.M(0f, 1.9f, 0f, 0.6f, 0.25f, 0.6f), 8, Bronze, true, new Color(0.25f, 0.1f, 0.05f));
            AddFlame(c, b.P(0f, 2f, 0f), 0.8f);
        }
    }

    // Pennants flutter on the masts, all blown the same way by the evening breeze
    private static readonly Dictionary<Color, Mesh> pennants = new Dictionary<Color, Mesh>();

    private static void Pennant(Ctx c, Vector3 at, Color col, int i)
    {
        if (!pennants.TryGetValue(col, out Mesh mesh) || mesh == null)
        {
            var k = new ArenaKit(c.pal);
            k.Tri(new Vector3(0f, 0f, 0f), new Vector3(0f, 1.1f, 0f), new Vector3(2.4f, 0.55f, 0f), col, Vector3.forward);
            k.Tri(new Vector3(0f, 0f, 0f), new Vector3(0f, 1.1f, 0f), new Vector3(2.4f, 0.55f, 0f), col, Vector3.back);
            mesh = Keep(k.Build("Pennant"));
            pennants[col] = mesh;
        }
        Transform t = Piece(c.root, "Pennant", mesh, c.lit, at);
        Vector3 wind = -SunLight;
        wind.y = 0f;
        Quaternion rest = Quaternion.FromToRotation(Vector3.right, wind.normalized);
        t.localRotation = rest;
        c.life.Pennants.Add((t, rest, i * 0.7f));
    }

    // ── White birds wheeling over the arena ──

    private static void Birds(Ctx c)
    {
        // A gull-like shape: slim body, fanned tail, long swept wings with dark tips
        var body = new ArenaKit(c.pal);
        Color white = new Color(0.96f, 0.94f, 0.9f), tip = new Color(0.25f, 0.24f, 0.28f);
        body.Box(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.2f, 0.2f, 0.62f)), white);
        body.Box(Matrix4x4.TRS(new Vector3(0f, 0.06f, 0.38f), Quaternion.identity, new Vector3(0.17f, 0.17f, 0.2f)), white);
        body.Box(Matrix4x4.TRS(new Vector3(0f, 0.04f, 0.52f), Quaternion.identity, new Vector3(0.05f, 0.05f, 0.12f)), Gold);
        body.Tri(new Vector3(0f, 0f, -0.25f), new Vector3(-0.2f, 0f, -0.62f), new Vector3(0.2f, 0f, -0.62f), white, Vector3.up);
        body.Tri(new Vector3(0f, 0f, -0.25f), new Vector3(-0.2f, 0f, -0.62f), new Vector3(0.2f, 0f, -0.62f), white, Vector3.down);
        Mesh bodyMesh = Keep(body.Build("Bird"));
        Mesh WingMesh(float side)
        {
            var w = new ArenaKit(c.pal);
            Vector3 a = new Vector3(0f, 0f, 0.16f), b = new Vector3(0f, 0f, -0.16f);
            Vector3 elbowF = new Vector3(side * 0.65f, 0.08f, 0.12f), elbowB = new Vector3(side * 0.65f, 0.08f, -0.24f);
            Vector3 wrist = new Vector3(side * 1.05f, 0.05f, -0.02f), wristB = new Vector3(side * 1.05f, 0.05f, -0.3f);
            Vector3 point = new Vector3(side * 1.55f, -0.02f, -0.42f);
            w.Quad2(a, elbowF, elbowB, b, white);
            w.Quad2(elbowF, wrist, wristB, elbowB, white);
            w.Tri(wrist, point, wristB, tip, Vector3.up);
            w.Tri(wrist, point, wristB, tip, Vector3.down);
            return Keep(w.Build("Wing"));
        }
        Mesh rMesh = WingMesh(1f), lMesh = WingMesh(-1f);

        var rng = c.rng;
        for (int j = 0; j < 12; j++)
        {
            Transform b = Piece(c.root, "Bird", bodyMesh, c.lit, Vector3.zero);
            b.localScale = Vector3.one * R(rng, 1.8f, 2.4f);
            Transform r = Piece(b, "Wing", rMesh, c.lit, new Vector3(0.1f, 0.05f, 0.05f));
            Transform l = Piece(b, "Wing", lMesh, c.lit, new Vector3(-0.1f, 0.05f, 0.05f));
            c.life.Birds.Add(new ArenaLife.Bird
            {
                body = b, wingL = l, wingR = r,
                radius = R(rng, 42f, 78f),
                height = R(rng, 6f, 26f),
                speed = (j % 4 == 0 ? -1f : 1f) * R(rng, 0.12f, 0.2f),
                phase = R(rng, 0f, Mathf.PI * 2f),
            });
        }
    }

    // ── Petals drifting down by the emperor's box, golden dust in the sunlit gap ──

    private static void Air(Ctx c)
    {
        Texture2D soft = Soft();
        ArenaBasis e = At(EmperorDeg, 8f, PodiumTop + 12f);
        Drift(c, "Petals", soft, e.o, e.rot, new Vector3(18f, 2f, 16f), 5f, 80, new Color(1f, 0.45f, 0.6f, 1f), new Color(0.9f, 0.15f, 0.3f, 1f), 0.3f, 0.55f,
            new ParticleSystem.MinMaxCurve(-1.6f, -1.1f), 16f);
        ArenaBasis gap = At(RuinDeg, 8f, GroundY + 16f);
        Drift(c, "Dust", soft, gap.o, gap.rot, new Vector3(60f, 26f, 30f), 14f, 200, new Color(1f, 0.88f, 0.55f, 0.7f), new Color(1f, 0.7f, 0.4f, 0.5f), 0.18f, 0.4f,
            new ParticleSystem.MinMaxCurve(-0.15f, 0.25f), 12f);
    }

    private static void Drift(Ctx c, string name, Texture2D tex, Vector3 pos, Quaternion rot, Vector3 box, float rate, int max, Color a, Color b, float minSize, float maxSize, ParticleSystem.MinMaxCurve fall, float life)
    {
        var ps = new GameObject(name).AddComponent<ParticleSystem>();
        ps.transform.SetParent(c.root, false);
        ps.transform.localPosition = pos;
        ps.transform.localRotation = rot;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.75f, life);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        vel.y = fall;
        vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.8f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.2f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Keep(new Material(Shader.Find("Sprites/Default")) { mainTexture = tex });
        ps.Play();
    }

    /// <summary>A soft round glow, for particles</summary>
    private static Texture2D Soft()
    {
        const int size = 64;
        var tex = Keep(new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Arena Glow" });
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)), 1.5f));
            }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}

/// <summary>Frees the meshes, textures and materials the arena built when the scenery goes away</summary>
public class ArenaCleanup : MonoBehaviour
{
    public readonly List<Object> Made = new List<Object>();

    private void OnDestroy()
    {
        foreach (Object o in Made) if (o != null) Destroy(o);
    }
}

/// <summary>
/// The arena's life: the crowd's wave round the stands, flickering fires, fluttering pennants,
/// the emperor's thumb, the lion's lift, the musicians and the birds. All on AmbientTime, so
/// Reduce Motion slows it to a crawl.
/// </summary>
public class ArenaLife : MonoBehaviour
{
    public class Bird { public Transform body, wingL, wingR; public float radius, height, speed, phase; }

    public readonly List<(Transform t, float deg)> Crowd = new List<(Transform, float)>();
    public readonly List<(Transform t, Vector3 scale, float phase)> Flames = new List<(Transform, Vector3, float)>();
    public readonly List<(Transform t, Quaternion rest, float phase)> Pennants = new List<(Transform, Quaternion, float)>();
    public readonly List<(Transform t, Vector3 rest, float phase)> Bobbers = new List<(Transform, Vector3, float)>();
    public readonly List<Bird> Birds = new List<Bird>();
    public Transform Arm, Cage, Lion;
    public Vector3 ArmAxis, CageRest, LionAcross;
    public float WaveFrom, WaveTo;
    private float lionYaw = 90f;

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    private void LateUpdate()
    {
        float t = AccessibilityPrefs.AmbientTime;

        // The wave sweeps round the crowded stands every 34 seconds
        float at = WaveFrom + (t % 34f) * 38f;
        foreach (var (tr, deg) in Crowd)
        {
            float d = deg;
            while (d < WaveFrom) d += 360f;
            float k = (d - at) / 9f;
            float lift = d > WaveTo ? 0f : 0.55f * Mathf.Exp(-k * k);
            tr.localPosition = new Vector3(0f, lift, 0f);
        }

        foreach (var (tr, s, ph) in Flames)
        {
            float f = 1f + 0.22f * Mathf.Sin(t * 9f + ph) + 0.12f * Mathf.Sin(t * 14.7f + ph * 1.7f);
            tr.localScale = new Vector3(s.x * (1f + 0.08f * Mathf.Sin(t * 11f + ph)), s.y * f, s.z * (1f + 0.08f * Mathf.Cos(t * 10f + ph)));
        }

        foreach (var (tr, rest, ph) in Pennants)
            tr.localRotation = Quaternion.AngleAxis(14f * Mathf.Sin(t * 2.6f + ph) + 6f * Mathf.Sin(t * 5.3f + ph * 2f), Vector3.up) * rest;

        foreach (var (tr, rest, ph) in Bobbers)
            tr.localPosition = rest + Vector3.up * (0.08f * Mathf.Sin(t * 4f + ph));

        if (Arm != null)
        {
            float c = t % 13f;
            float up = c < 0.7f ? Smooth(c / 0.7f) : c < 3.2f ? 1f : c < 3.9f ? 1f - Smooth((c - 3.2f) / 0.7f) : 0f;
            Arm.localRotation = Quaternion.AngleAxis(-150f * up, ArmAxis);
        }

        if (Cage != null)
        {
            float c = t % 36f;
            float h = c < 4f ? Smooth(c / 4f) : c < 22f ? 1f : c < 26f ? 1f - Smooth((c - 22f) / 4f) : 0f;
            Cage.localPosition = CageRest + Vector3.up * (-5.4f * (1f - h));
        }
        if (Lion != null)
        {
            float x = Mathf.Sin(t * 0.55f);
            float target = Mathf.Cos(t * 0.55f) >= 0f ? 90f : -90f;
            lionYaw = Mathf.MoveTowards(lionYaw, target, Time.deltaTime * 240f * AccessibilityPrefs.AmbientSpeed);
            Lion.localPosition = LionAcross * (1.3f * x);
            Lion.localRotation = Quaternion.AngleAxis(lionYaw, Vector3.up);
        }

        foreach (Bird b in Birds)
        {
            float a = b.phase + t * b.speed;
            float dir = Mathf.Sign(b.speed);
            b.body.localPosition = new Vector3(Mathf.Cos(a) * b.radius, b.height + 1.2f * Mathf.Sin(t * 0.4f + b.phase), Mathf.Sin(a) * b.radius);
            Vector3 fwd = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * dir;
            b.body.localRotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, 14f * dir);
            float glide = Mathf.Clamp01(Mathf.Sin(t * 0.7f + b.phase * 3f) * 2f);
            float flap = 10f + 40f * Mathf.Sin(t * 9f + b.phase) * (1f - 0.85f * glide);   // slow deep beats, wings held a little up when gliding
            b.wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
            b.wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
        }
    }
}
