using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Hanging Gardens backdrop: the board floats over the heart of the Hanging Gardens of Babylon.
//
//  - HangingGardensSky: the 360° sky. Blue hour: deep teal overhead fading to a rose-gold sunset,
//    long pink clouds, and on the skyline the walls of Babylon with palms and cypresses, the great
//    stepped ziggurat with gardens on its steps, and the blue-glazed Ishtar Gate.
//  - HangingGardensScenery: a sunken garden of terraces stepping up all round the board, like an
//    upside-down ziggurat. Every wall is a sandstone arcade with a blue-and-gold glazed frieze, smothered
//    in hanging vines and flowers; every terrace is crowded with bushes, flower beds, date palms,
//    flowering trees and cypresses; waterfalls spill from terrace to terrace into a lotus pool far below,
//    and palm groves and a river ring the rim.
//  - Moving bits: giant neon blooms that sway and open and close, butterflies, drifting petals,
//    golden pollen sparkles and the waterfalls.
//
// The still garden is merged into one mesh per material (GardenBatch), so thousands of plants
// cost a handful of draw calls. Everything sits well away from the board.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural Babylon sky at blue hour. Pure C# so it can run on a worker thread.</summary>
public static class HangingGardensSky
{
    // Matches the fog so the far groves melt into the dusk
    public static readonly Color HazeColor = new Color(0.36f, 0.2f, 0.32f);

    public const float SunU = 0.25f;           // ahead of the default White view (+x), like Melting Clocks
    private const float SunV = 0.465f;         // just above the horizon, half behind the trees
    private const float SunR = 0.05f;
    private const float ZigguratU = 0.62f;     // the great ziggurat on the skyline
    private const float GateU = 0.86f;         // the Ishtar Gate

    // Skyline layers
    private const byte Sky = 0, Far = 1, Zig = 2, Near = 3, Gate = 4; // nearer layers draw over farther ones

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        var rng = new System.Random(seed);
        const float horizon = 0.5f;
        int hy = height / 2;
        var layer = new byte[width * height];

        // Far row: a soft rolling line of rounded crowns
        for (int i = 0; i < 80; i++)
        {
            int cx = rng.Next(width);
            float r = 12f + (float)rng.NextDouble() * 22f;
            float lift = 6f + (float)rng.NextDouble() * 16f;
            Disc(layer, width, height, Far, cx, hy - lift - r * 0.7f, r);
            Rect(layer, width, height, Far, cx - r * 0.8f, cx + r * 0.8f, hy - lift - r * 0.7f, hy);
        }

        // The great ziggurat behind the city wall: seven shrinking steps with a shrine on top
        float zx = ZigguratU * width, zBase = hy - 4f;
        for (int step = 0; step < ZigSteps; step++)
        {
            float half = ZigHalf - step * 14f;
            Rect(layer, width, height, Zig, zx - half, zx + half, zBase - (step + 1) * ZigStepH, zBase);
        }
        Rect(layer, width, height, Zig, zx - 14f, zx + 14f, zBase - ZigSteps * ZigStepH - 16f, zBase); // the shrine
        Rect(layer, width, height, Zig, zx - 5f, zx + 5f, zBase - ZigSteps * ZigStepH - 24f, zBase);

        // Near row: the walls of Babylon all the way round, with stepped battlements
        int sunX = (int)(SunU * width);
        for (int x = 0; x < width; x++)
        {
            float wall = 13f + ((x / 6) % 2 == 0 ? 5f : 0f);
            if (((x / 90) % 4) == 0 && (x % 90) < 16) wall += 12f; // a tower every so often
            Rect(layer, width, height, Near, x, x + 1, hy - wall, hy);
        }
        // Near trees, cypresses and palms, leaving the sun clear
        bool NearSun(float x) => Mathf.Abs(WrapPx(x - sunX, width)) < width * 0.045f;
        bool NearLandmark(float x) => Mathf.Abs(WrapPx(x - ZigguratU * width, width)) < ZigHalf + 20f || Mathf.Abs(WrapPx(x - GateU * width, width)) < 70f;
        for (int i = 0; i < 10; i++)
        {
            float cx = rng.Next(width);
            if (NearSun(cx) || NearLandmark(cx)) continue;
            float r = 14f + (float)rng.NextDouble() * 14f;
            float lift = 12f + (float)rng.NextDouble() * 12f;
            Disc(layer, width, height, Near, cx, hy - lift - r, r);
            Disc(layer, width, height, Near, cx - r * 0.7f, hy - lift - r * 0.6f, r * 0.7f);
            Disc(layer, width, height, Near, cx + r * 0.7f, hy - lift - r * 0.55f, r * 0.65f);
            Rect(layer, width, height, Near, cx - 3f, cx + 3f, hy - lift - r * 0.5f, hy);
        }
        for (int i = 0; i < 30; i++)
        {
            float cx = rng.Next(width);
            if (NearSun(cx) || NearLandmark(cx)) continue;
            float w = 6f + (float)rng.NextDouble() * 7f;
            float h = 40f + (float)rng.NextDouble() * 45f;
            Flame(layer, width, height, Near, cx, hy, w, h);
        }
        for (int i = 0; i < 14; i++)
        {
            float cx = rng.Next(width);
            if (NearSun(cx) || NearLandmark(cx)) continue;
            float h = 50f + (float)rng.NextDouble() * 30f;
            float lean = ((float)rng.NextDouble() - 0.5f) * 30f;
            Palm(layer, width, height, Near, cx, hy, h, lean, rng);
        }

        // The Ishtar Gate: two tall blue towers with battlements and an arched gateway between
        float gx = GateU * width;
        for (int side = -1; side <= 1; side += 2)
        {
            float tx = gx + side * 30f;
            Rect(layer, width, height, Gate, tx - 13f, tx + 13f, hy - GateH, hy);
            for (int m = -1; m <= 1; m++) Rect(layer, width, height, Gate, tx + m * 9f - 3f, tx + m * 9f + 3f, hy - GateH - 6f, hy - GateH);
        }
        Rect(layer, width, height, Gate, gx - 17f, gx + 17f, hy - GateH * 0.7f, hy);

        // Fireflies among the near trees
        var sparks = new HashSet<int>();
        for (int k = 0; k < 900; k++)
        {
            int x = rng.Next(width), y = hy - 2 - rng.Next(110);
            if (layer[y * width + x] == Near) sparks.Add(y * width + x);
        }

        var stars = new HashSet<int>();
        for (int k = 0; k < 1400; k++) stars.Add(rng.Next((int)(height * 0.3f)) * width + rng.Next(width));

        Color zenith = new Color(0.02f, 0.06f, 0.16f), dusk = new Color(0.1f, 0.3f, 0.45f); // blue hour: deep teal overhead
        Color rose = new Color(0.98f, 0.45f, 0.55f), gold = new Color(1f, 0.76f, 0.5f);
        Color nearDark = new Color(0.1f, 0.07f, 0.16f); // dusk silhouettes, not black

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float du = Wrap(u - SunU) * 2f;            // in v units, so the sun is round
                float sunSide = Mathf.Exp(-du * du / 0.12f); // 1 facing the sun, 0 behind you
                Color c;
                if (v < horizon)
                {
                    float t = v / horizon;
                    c = t < 0.55f ? Color.Lerp(zenith, dusk, t / 0.55f)
                      : t < 0.86f ? Color.Lerp(dusk, rose, (t - 0.55f) / 0.31f)
                      : Color.Lerp(rose, gold, (t - 0.86f) / 0.14f);
                    // The far side of the sky is cooler and darker than the sunset side
                    c = Color.Lerp(c * new Color(0.75f, 0.7f, 1f), c, 0.45f + 0.55f * sunSide);
                    if (stars.Contains(y * width + x)) c += Color.white * (0.5f * Mathf.Clamp01(1f - t * 2.4f));

                    // The setting sun: a soft halo and a glow that spreads along the horizon
                    float dv = v - SunV;
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    c += new Color(1f, 0.6f, 0.4f) * (0.55f * Mathf.Exp(-d * d / 0.004f));
                    c += new Color(1f, 0.5f, 0.35f) * (0.3f * Mathf.Exp(-du * du / 0.05f) * Mathf.Exp(-Mathf.Pow((horizon - v) / 0.06f, 2f)));
                    if (d < SunR)
                    {
                        Color disc = Color.Lerp(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.62f), Mathf.Clamp01(dv / SunR * 0.5f + 0.5f));
                        c = Color.Lerp(c, disc, Mathf.Clamp01((SunR - d) / 0.003f));
                    }

                    // Long thin clouds, lit gold-pink on the sunset side and violet behind
                    float n = SeamlessPerlin(u, 9f, v * 55f + 3f) * SeamlessPerlin(u, 3f, v * 12f + 7f);
                    float band = Mathf.Exp(-Mathf.Pow((v - 0.37f) / 0.07f, 2f));
                    Color cloud = Color.Lerp(new Color(0.5f, 0.22f, 0.52f), new Color(1f, 0.62f, 0.58f), sunSide);
                    c = Color.Lerp(c, cloud, Mathf.Clamp01((n - 0.2f) * 4f) * band * 0.8f);

                    byte l = layer[y * width + x];
                    bool edge = y > 0 && layer[(y - 1) * width + x] != l;
                    bool edge2 = y > 1 && layer[(y - 2) * width + x] != l;
                    if (l == Far)
                    {
                        // Hazy far trees, taking on the sky colour behind them
                        c = Color.Lerp(c, new Color(0.16f, 0.07f, 0.2f), 0.72f);
                        if (edge) c = Color.Lerp(c, rose, 0.25f * (0.3f + 0.7f * sunSide));
                    }
                    else if (l == Near)
                    {
                        c = nearDark;
                        if (edge || edge2) c = Color.Lerp(nearDark, new Color(1f, 0.45f, 0.7f), (edge ? 0.55f : 0.25f) * (0.35f + 0.65f * sunSide));
                        if (sparks.Contains(y * width + x)) c = new Color(1f, 0.9f, 0.45f);
                    }
                    else if (l == Zig)
                    {
                        c = ZigguratColor(x, y, zx, zBase, width, sunSide);
                        if (edge) c = Color.Lerp(c, new Color(1f, 0.6f, 0.5f), 0.5f);
                    }
                    else if (l == Gate)
                    {
                        c = GateColor(x, y, gx, hy, width);
                        if (edge) c = new Color(1f, 0.82f, 0.4f);
                    }
                }
                else
                {
                    // Below the horizon: dusk haze over dark lawn
                    float below = v - horizon;
                    c = Color.Lerp(HazeColor * 1.1f, new Color(0.03f, 0.06f, 0.05f), Mathf.Clamp01(below * 6f));
                }
                result[(height - 1 - y) * width + x] = new Color32(B(c.r), B(c.g), B(c.b), 255); // Unity textures start at the bottom row
            }
        }
        Soften(result, layer, width, hy);
        return result;
    }

    // Blends each skyline edge pixel with its neighbours, so silhouettes and glass ribs don't stair-step when magnified
    private static void Soften(Color32[] result, byte[] layer, int width, int hy)
    {
        int height = result.Length / width;
        var src = (Color32[])result.Clone();
        for (int y = 1; y < hy; y++)
            for (int x = 0; x < width; x++)
            {
                int xl = (x + width - 1) % width, xr = (x + 1) % width;
                byte l = layer[y * width + x];
                if (layer[y * width + xl] == l && layer[y * width + xr] == l && layer[(y - 1) * width + x] == l && layer[(y + 1) * width + x] == l
                    && !(l == Zig || l == Gate)) continue;
                int r = 0, g = 0, b = 0;
                for (int oy = -1; oy <= 1; oy++)
                    for (int k = 0; k < 3; k++)
                    {
                        int ox = k == 0 ? xl : k == 1 ? x : xr;
                        Color32 p = src[(height - 1 - (y + oy)) * width + ox];
                        int w = oy == 0 && ox == x ? 4 : (oy == 0 || ox == x ? 2 : 1);
                        r += p.r * w; g += p.g * w; b += p.b * w;
                    }
                result[(height - 1 - y) * width + x] = new Color32((byte)(r / 16), (byte)(g / 16), (byte)(b / 16), 255);
            }
    }

    private const int ZigSteps = 7;
    private const float ZigHalf = 110f, ZigStepH = 11f, GateH = 52f;

    // Mud-brick steps, each edged with a glowing blue band and trailing green gardens, with lit doorways
    private static Color ZigguratColor(int x, int y, float zx, float zBase, int width, float sunSide)
    {
        float dx = WrapPx(x - zx, width), up = zBase - y;
        float inStep = Mathf.Repeat(up, ZigStepH);
        Color c = Color.Lerp(new Color(0.16f, 0.09f, 0.1f), new Color(0.3f, 0.16f, 0.15f), sunSide);
        if (up > ZigSteps * ZigStepH) return Mathf.Abs(dx) < 3f ? new Color(1f, 0.82f, 0.4f) : new Color(0.75f, 0.55f, 0.25f); // the golden shrine
        if (inStep > ZigStepH - 2f) return new Color(0.2f, 0.45f, 1f);                                                         // glazed band
        float vine = Mathf.PerlinNoise(x * 0.35f, up * 0.2f);
        if (inStep > ZigStepH - 7f && vine > 0.45f) return Color.Lerp(new Color(0.06f, 0.3f, 0.12f), new Color(1f, 0.4f, 0.75f), vine > 0.68f ? 1f : 0f); // gardens spilling over
        if (Mathf.Repeat(dx, 16f) < 3f && inStep > 3f && inStep < 8f) return new Color(1f, 0.72f, 0.38f) * 0.8f;               // lit doorways
        if (Mathf.Abs(dx) < 6f) return c * 1.4f;                                                                             // the great stair
        return c;
    }

    // Blue glazed brick with rows of golden beasts and a dark arched gateway
    private static Color GateColor(int x, int y, float gx, int hy, int width)
    {
        float dx = WrapPx(x - gx, width), up = hy - y;
        if (Mathf.Abs(dx) < 9f && up < GateH * 0.45f) return Mathf.Abs(dx) < 7.5f ? new Color(0.03f, 0.03f, 0.08f) : new Color(1f, 0.82f, 0.4f);
        if (Mathf.Abs(dx) < 9f && up < GateH * 0.45f + 9f && dx * dx + Mathf.Pow(up - GateH * 0.45f, 2f) < 81f) return new Color(0.03f, 0.03f, 0.08f);
        bool beast = Mathf.Repeat(up, 14f) > 9f && Mathf.Repeat(up, 14f) < 12f && Mathf.Repeat(dx + (int)(up / 14f) * 4f, 8f) < 4f;
        return beast ? new Color(1f, 0.82f, 0.4f) : new Color(0.08f, 0.28f, 0.85f);
    }

    // ───────── Silhouette drawing (pixel coordinates, y = 0 at the zenith, x wraps) ─────────

    private static void Disc(byte[] layer, int width, int height, byte value, float cx, float cy, float r)
    {
        for (int y = Mathf.Max(0, (int)(cy - r)); y <= Mathf.Min(height / 2 - 1, (int)(cy + r)); y++)
        {
            float dy = y + 0.5f - cy;
            float half = Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
            for (int x = (int)(cx - half); x <= (int)(cx + half); x++) Set(layer, width, x, y, value);
        }
    }

    private static void Rect(byte[] layer, int width, int height, byte value, float x0, float x1, float y0, float y1)
    {
        for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(height / 2, (int)Mathf.Ceil(y1)); y++)
            for (int x = (int)x0; x < (int)Mathf.Ceil(x1); x++) Set(layer, width, x, y, value);
    }

    // A cypress: a tall rounded flame shape
    private static void Flame(byte[] layer, int width, int height, byte value, float cx, float baseY, float halfWidth, float h)
    {
        for (int y = (int)(baseY - h); y < (int)baseY; y++)
        {
            float k = (baseY - y) / h; // 0 at the foot, 1 at the tip
            float half = halfWidth * Mathf.Sqrt(Mathf.Clamp01(1f - k)) * Mathf.Min(1f, (1f - Mathf.Abs(k - 0.35f)) * 1.3f);
            for (int x = (int)(cx - half); x <= (int)(cx + half); x++) Set(layer, width, x, y, value);
        }
    }

    // A palm: a curving trunk and drooping fronds
    private static void Palm(byte[] layer, int width, int height, byte value, float cx, float baseY, float h, float lean, System.Random rng)
    {
        for (int y = (int)(baseY - h); y < (int)baseY; y++)
        {
            float k = (baseY - y) / h;
            float x = cx + lean * k * k;
            float half = 2.6f - k * 1.2f;
            for (int xx = (int)(x - half); xx <= (int)(x + half); xx++) Set(layer, width, xx, y, value);
        }
        float topX = cx + lean, topY = baseY - h;
        for (int f = 0; f < 8; f++)
        {
            float a = f / 8f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
            float len = 22f + (float)rng.NextDouble() * 10f;
            float dir = Mathf.Cos(a);           // left/right reach
            float rise = Mathf.Sin(a) * 0.5f + 0.25f;
            for (int s = 0; s < 40; s++)
            {
                float k = s / 40f;
                float px = topX + dir * len * k;
                float py = topY - rise * len * k + k * k * len * 0.7f; // droops toward the tip
                float thick = 2.4f * (1f - k) + 0.6f;
                for (int yy = (int)(py - thick); yy <= (int)(py + thick); yy++)
                    for (int xx = (int)(px - 0.8f); xx <= (int)(px + 0.8f); xx++) Set(layer, width, xx, yy, value);
            }
        }
    }

    private static void Set(byte[] layer, int width, int x, int y, byte value)
    {
        if (y < 0 || y >= layer.Length / width) return;
        x = ((x % width) + width) % width;
        int i = y * width + x;
        if (value >= layer[i]) layer[i] = value; // nearer layers draw over farther ones
    }

    // Perlin noise that joins up where the sky wraps round
    private static float SeamlessPerlin(float u, float freq, float y) =>
        Mathf.PerlinNoise(u * freq, y) * (1f - u) + Mathf.PerlinNoise((u - 1f) * freq, y) * u;

    private static float Wrap(float d) { if (d > 0.5f) d -= 1f; if (d < -0.5f) d += 1f; return d; }
    private static float WrapPx(float d, int width) { if (d > width * 0.5f) d -= width; if (d < -width * 0.5f) d += width; return d; }
    private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}

public static class HangingGardensScenery
{
    // A sunken garden: a lotus pool far below the board, then terraces stepping up all round it
    public const float FloorY = -40f;
    public const float TierH = 9f;          // each wall's height
    public const int Tiers = 4;
    public const float InnerHalf = 30f;     // half-width of the pool, the lowest square
    public const float Step = 13f;          // each terrace's depth
    public static float RimY => FloorY + TierH * Tiers;

    private static float Half(int tier) => InnerHalf + Step * tier;       // where tier's wall stands
    private static float TopY(int tier) => FloorY + TierH * (tier + 1);  // the terrace on top of that wall

    // Inward normals of the four walls of a square
    private static readonly Vector3[] Inward = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };

    // A wall's frame: origin at the foot of the wall's middle, +z out of the wall toward the board, +x along it.
    // The terrace on top is at y = TierH, behind the wall (z < 0).
    private static Matrix4x4 WallFrame(int tier, int side) =>
        Matrix4x4.TRS(-Inward[side] * Half(tier) + Vector3.up * (TopY(tier) - TierH), Quaternion.LookRotation(Inward[side]), Vector3.one);

    private static float F(System.Random rng) => (float)rng.NextDouble();

    public static void Build(Transform root)
    {
        var rng = new System.Random(1759);
        var batch = new GardenBatch();
        var m = new GardenMaterials();

        BuildGround(batch, m);
        for (int tier = 0; tier < Tiers; tier++)
            for (int side = 0; side < 4; side++)
            {
                Matrix4x4 frame = WallFrame(tier, side);
                float half = Half(tier);
                BuildWall(batch, m, rng, frame, half);
                BuildVines(batch, m, rng, frame, half);
                BuildTerrace(batch, m, rng, frame, half, tier);
            }
        BuildPool(batch, m, rng);
        BuildRim(batch, m, rng);
        batch.Build(root, "Hanging Gardens");

        BuildGiantBlooms(root, m, rng);
        root.gameObject.AddComponent<GardenWaterfalls>();
        root.gameObject.AddComponent<GardenButterflies>();
        root.gameObject.AddComponent<GardenPetals>();
        root.gameObject.AddComponent<GardenPollen>();
    }

    // ───────── The stepped bowl itself ─────────

    private static void BuildGround(GardenBatch batch, GardenMaterials m)
    {
        const float far = 420f;
        // Each tier is a solid square ring from its wall out to the far distance; the higher ones sit on the lower
        for (int tier = 0; tier < Tiers; tier++)
        {
            float h = Half(tier), top = TopY(tier);
            Ring(batch, m.Sandstone, h, far, FloorY - 2f, top);
            // Grass on the terrace (the top tier's grass is the plain round the rim)
            float outer = tier == Tiers - 1 ? far : h + Step;
            Ring(batch, m.Grass, h + 0.6f, outer, top, top + 0.06f);
        }
        // The floor under the pool
        batch.Add(GardenShapes.Cube, m.Sandstone, new Vector3(0f, FloorY - 1f, 0f), Quaternion.identity, new Vector3(InnerHalf * 2f, 2f, InnerHalf * 2f));
    }

    // A square ring (as four boxes) between two half-widths, from y0 to y1
    private static void Ring(GardenBatch batch, Material mat, float inner, float outer, float y0, float y1)
    {
        float mid = (inner + outer) * 0.5f, depth = outer - inner, ym = (y0 + y1) * 0.5f, h = y1 - y0;
        batch.Add(GardenShapes.Cube, mat, new Vector3(0f, ym, mid), Quaternion.identity, new Vector3(outer * 2f, h, depth));
        batch.Add(GardenShapes.Cube, mat, new Vector3(0f, ym, -mid), Quaternion.identity, new Vector3(outer * 2f, h, depth));
        batch.Add(GardenShapes.Cube, mat, new Vector3(mid, ym, 0f), Quaternion.identity, new Vector3(depth, h, inner * 2f));
        batch.Add(GardenShapes.Cube, mat, new Vector3(-mid, ym, 0f), Quaternion.identity, new Vector3(depth, h, inner * 2f));
    }

    // ───────── Walls: an arcade of lit arches under a blue-and-gold glazed frieze ─────────

    private static void BuildWall(GardenBatch batch, GardenMaterials m, System.Random rng, Matrix4x4 frame, float half)
    {
        const float pitch = 5.5f;
        int arches = Mathf.FloorToInt((half * 2f - 4f) / pitch);
        float start = -(arches - 1) * pitch * 0.5f;
        for (int a = 0; a < arches; a++)
        {
            float x = start + a * pitch;
            // Pilaster to the left of each arch (and one closing the row)
            batch.Add(frame, GardenShapes.Cube, m.SandstoneLight, new Vector3(x - pitch * 0.5f, 3.2f, 0.25f), Quaternion.identity, new Vector3(0.8f, 6.4f, 0.5f));
            if (a == arches - 1) batch.Add(frame, GardenShapes.Cube, m.SandstoneLight, new Vector3(x + pitch * 0.5f, 3.2f, 0.25f), Quaternion.identity, new Vector3(0.8f, 6.4f, 0.5f));
            if (Mathf.Abs(x) < 3f) continue; // the waterfall comes down here
            // The arch: a dark recess with a round top, a warm lamp hanging inside, a gold keystone
            batch.Add(frame, GardenShapes.Cube, m.Recess, new Vector3(x, 2.3f, 0.04f), Quaternion.identity, new Vector3(3.2f, 4.6f, 0.06f));
            batch.Add(frame, GardenShapes.Cylinder, m.Recess, new Vector3(x, 4.6f, 0.04f), Quaternion.Euler(90f, 0f, 0f), new Vector3(3.2f, 0.03f, 3.2f));
            batch.Add(frame, GardenShapes.Bud, m.Lamp, new Vector3(x, 3.9f, 0.4f), Quaternion.identity, Vector3.one * 0.55f);
            batch.Add(frame, GardenShapes.Cube, m.Gold, new Vector3(x, 6.25f, 0.12f), Quaternion.Euler(0f, 0f, 45f), new Vector3(0.45f, 0.45f, 0.12f));
        }
        // The glazed frieze with golden rosettes, and a gold line under it
        batch.Add(frame, GardenShapes.Cube, m.Ishtar, new Vector3(0f, 7.6f, 0.1f), Quaternion.identity, new Vector3(half * 2f, 1.3f, 0.2f));
        batch.Add(frame, GardenShapes.Cube, m.Gold, new Vector3(0f, 6.8f, 0.12f), Quaternion.identity, new Vector3(half * 2f, 0.16f, 0.24f));
        for (float x = -half + 1.2f; x < half - 1f; x += 2.4f)
            batch.Add(frame, GardenShapes.Cube, m.Gold, new Vector3(x, 7.6f, 0.22f), Quaternion.Euler(0f, 0f, 45f), new Vector3(0.5f, 0.5f, 0.08f));
    }

    // ───────── Vines and flowers smothering every wall ─────────

    private static void BuildVines(GardenBatch batch, GardenMaterials m, System.Random rng, Matrix4x4 frame, float half)
    {
        // Two overlapping curtains of painted vines: a full one close to the wall, a ragged one in front
        for (int layer = 0; layer < 2; layer++)
        {
            float x = -half + 0.5f;
            while (x < half - 0.5f)
            {
                float w = layer == 0 ? 5f + F(rng) * 3f : 3f + F(rng) * 3f;
                float cx = x + w * 0.5f;
                x += layer == 0 ? w * 0.75f : w * (1.1f + F(rng) * 0.8f);
                if (Mathf.Abs(cx) < 2.6f + w * 0.5f) continue; // keep the waterfall clear
                float h = layer == 0 ? TierH * (0.75f + F(rng) * 0.25f) : TierH * (0.45f + F(rng) * 0.5f);
                Mesh quad = GardenShapes.Vine(rng.Next(4));
                float flip = rng.NextDouble() < 0.5 ? -1f : 1f;
                batch.Add(frame, quad, m.Vines, new Vector3(cx, TierH + 0.4f - h * 0.5f, 0.55f + layer * 0.35f + F(rng) * 0.1f), Quaternion.Euler(90f, 0f, 0f), new Vector3(w * flip, 1f, h));
            }
        }
        // Real leafy clumps along the top edge, spilling over, so the curtain has depth
        for (float x = -half + 1f; x < half - 1f; x += 2.2f)
        {
            if (Mathf.Abs(x) < 2.4f) continue;
            float s = 2.2f + F(rng) * 1.2f;
            batch.Add(frame, GardenShapes.Bud, m.Leaf, new Vector3(x + (F(rng) - 0.5f), TierH + 0.2f, 0.2f + F(rng) * 0.6f), Quaternion.Euler(0f, F(rng) * 360f, 0f), new Vector3(s, s * 0.75f, s * 0.8f));
            if (rng.NextDouble() < 0.6)
                batch.Add(frame, GardenShapes.Bud, m.Blooms[rng.Next(m.Blooms.Length)], new Vector3(x + (F(rng) - 0.5f) * 1.5f, TierH + 0.4f + F(rng) * 0.6f, 0.9f + F(rng) * 0.5f), Quaternion.identity, Vector3.one * (0.6f + F(rng) * 0.3f));
            // A few long 3D strands trailing down
            if (rng.NextDouble() < 0.35)
            {
                float len = 3f + F(rng) * 5f;
                batch.Add(frame, GardenShapes.Bud, m.Leaf, new Vector3(x, TierH - len * 0.5f, 1.25f), Quaternion.identity, new Vector3(0.8f, len, 0.6f));
                for (int b = 0; b < 3; b++)
                    batch.Add(frame, GardenShapes.Bud, m.Blooms[rng.Next(4)], new Vector3(x + (F(rng) - 0.5f) * 0.6f, TierH - F(rng) * len, 1.55f), Quaternion.identity, Vector3.one * 0.55f);
            }
        }
        // Bushes and flowers at the foot of the wall
        for (float x = -half + 1.5f; x < half - 1.5f; x += 3f)
        {
            if (Mathf.Abs(x) < 3.5f) continue;
            float s = 1.8f + F(rng) * 1.2f;
            batch.Add(frame, GardenShapes.Bud, m.Hedge, new Vector3(x + F(rng) - 0.5f, s * 0.35f, 1.4f + F(rng) * 0.6f), Quaternion.identity, new Vector3(s * 1.2f, s, s));
            batch.Add(frame, GardenShapes.Bud, m.Blooms[rng.Next(m.Blooms.Length)], new Vector3(x + (F(rng) - 0.5f) * 1.5f, s * 0.75f, 2.2f + F(rng) * 0.5f), Quaternion.identity, Vector3.one * 0.6f);
        }
    }

    // ───────── Terraces: flower beds, palms, flowering trees and cypresses ─────────

    private static void BuildTerrace(GardenBatch batch, GardenMaterials m, System.Random rng, Matrix4x4 frame, float half, int tier)
    {
        float y = TierH + 0.06f;
        // A strip of flowers just behind the vines on the edge
        for (float d = 2.4f; d <= 4f; d += 1.6f)
            for (float x = -(half + d) + 1f; x < half + d - 1f; x += 2.1f)
            {
                Vector3 at = new Vector3(x + (F(rng) - 0.5f) * 0.6f, y, -d);
                batch.Add(frame, GardenShapes.Bud, m.Leaf, at + Vector3.up * 0.35f, Quaternion.identity, new Vector3(1.8f, 0.7f, 1.6f));
                batch.Add(frame, GardenShapes.Bud, m.Blooms[(int)Mathf.Repeat(x * 0.25f + tier * 2f, m.Blooms.Length)], at + Vector3.up * (0.85f + F(rng) * 0.3f), Quaternion.identity, Vector3.one * (0.8f + F(rng) * 0.3f));
            }
        // Trees down the middle of the terrace, alternating palms and flowering trees
        float pitch = 8.5f, dm = Step * 0.5f;
        int trees = Mathf.FloorToInt((half + dm) * 2f / pitch);
        float x0 = -(trees - 1) * pitch * 0.5f;
        for (int t = 0; t < trees; t++)
        {
            Vector3 at = new Vector3(x0 + t * pitch + (F(rng) - 0.5f) * 1.5f, y, -dm + (F(rng) - 0.5f) * 2f);
            if ((t + tier) % 2 == 0) Palm(batch, m, rng, frame, at);
            else FloweringTree(batch, m, rng, frame, at, tier);
        }
        // Against the back (the next wall up, or open sky on the rim): cypresses with round bushes between
        if (tier == Tiers - 1) return;
        float db = Step - 1.6f;
        for (float x = -(half + db) + 2f; x < half + db - 2f; x += 5f)
        {
            Vector3 at = new Vector3(x, y, -db);
            if (Mathf.Abs(x) > 4f)
            {
                float h = 6.5f + F(rng) * 2.5f;
                batch.Add(frame, GardenShapes.Cone, m.Cypress, at, Quaternion.identity, new Vector3(2.2f, h, 2.2f));
                batch.Add(frame, GardenShapes.Bud, m.Cypress, at + Vector3.up * 1.2f, Quaternion.identity, new Vector3(2.3f, 2.6f, 2.3f));
            }
            Vector3 b = at + new Vector3(2.5f, 0f, 0.3f);
            float s = 2f + F(rng) * 1f;
            batch.Add(frame, GardenShapes.Bud, m.Hedge, b + Vector3.up * s * 0.4f, Quaternion.identity, new Vector3(s * 1.2f, s, s));
            batch.Add(frame, GardenShapes.Bud, m.Blooms[rng.Next(m.Blooms.Length)], b + Vector3.up * s * 0.85f, Quaternion.identity, Vector3.one * 0.7f);
        }
    }

    // A date palm: a gently curving trunk, a crown of drooping fronds and golden dates
    private static void Palm(GardenBatch batch, GardenMaterials m, System.Random rng, Matrix4x4 frame, Vector3 at)
    {
        float h = 7f + F(rng) * 3.5f;
        Quaternion lean = Quaternion.Euler((F(rng) - 0.5f) * 14f, 0f, (F(rng) - 0.5f) * 14f);
        Vector3 mid = at + lean * new Vector3(0f, h * 0.5f, 0f);
        batch.Add(frame, GardenShapes.Cylinder, m.Bark, at + lean * new Vector3(0f, h * 0.25f, 0f), lean, new Vector3(0.65f, h * 0.25f, 0.65f));
        Quaternion lean2 = lean * lean;
        batch.Add(frame, GardenShapes.Cylinder, m.Bark, mid + lean2 * new Vector3(0f, h * 0.25f, 0f), lean2, new Vector3(0.5f, h * 0.25f, 0.5f));
        Vector3 top = mid + lean2 * new Vector3(0f, h * 0.5f, 0f);
        float yaw0 = F(rng) * 360f;
        for (int f = 0; f < 8; f++)
        {
            Quaternion q = Quaternion.Euler(0f, yaw0 + f * 45f, 0f) * Quaternion.Euler(20f + (f % 2) * 14f, 0f, 0f);
            batch.Add(frame, GardenShapes.Bud, m.Palm, top + q * new Vector3(0f, 0f, 2.4f), q, new Vector3(1.2f, 0.2f, 5f));
        }
        batch.Add(frame, GardenShapes.Bud, m.Gold, top + Vector3.down * 0.5f, Quaternion.identity, new Vector3(1.1f, 0.8f, 1.1f));
    }

    // A flowering tree: a short trunk under a round cloud of blossom (violet jacaranda or pink pomegranate)
    private static void FloweringTree(GardenBatch batch, GardenMaterials m, System.Random rng, Matrix4x4 frame, Vector3 at, int tier)
    {
        float h = 3.5f + F(rng) * 1.5f;
        batch.Add(frame, GardenShapes.Cylinder, m.Bark, at + Vector3.up * h * 0.5f, Quaternion.identity, new Vector3(0.6f, h * 0.5f, 0.6f));
        Material bloom = (tier + (int)(at.x * 3f)) % 3 == 0 ? m.Pomegranate : m.Jacaranda;
        Vector3 top = at + Vector3.up * h;
        for (int c = 0; c < 8; c++)
        {
            float a = F(rng) * Mathf.PI * 2f, r = F(rng) * 2.6f;
            float s = 2.2f + F(rng) * 1.6f;
            batch.Add(frame, GardenShapes.Bud, c % 4 == 3 ? m.Leaf : bloom, top + new Vector3(Mathf.Cos(a) * r, 1.2f + F(rng) * 1.8f - r * 0.2f, Mathf.Sin(a) * r), Quaternion.identity, new Vector3(s, s * 0.8f, s));
        }
    }

    // ───────── The lotus pool at the bottom ─────────

    private static void BuildPool(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        batch.Add(GardenShapes.Cube, m.Water, new Vector3(0f, FloorY + 0.25f, 0f), Quaternion.identity, new Vector3(InnerHalf * 2f - 0.2f, 0.1f, InnerHalf * 2f - 0.2f));
        for (int i = 0; i < 70; i++)
        {
            Vector3 p = new Vector3((F(rng) - 0.5f) * (InnerHalf * 2f - 4f), FloorY + 0.32f, (F(rng) - 0.5f) * (InnerHalf * 2f - 4f));
            float s = 1.4f + F(rng) * 1.4f;
            batch.Add(GardenShapes.Cylinder, m.Leaf, p, Quaternion.identity, new Vector3(s, 0.03f, s));
            if (i % 3 == 0)
            {
                Material lotus = i % 2 == 0 ? m.Blooms[0] : m.Blooms[5];
                for (int k = 0; k < 5; k++)
                {
                    Quaternion q = Quaternion.Euler(0f, k * 72f + i * 11f, 0f) * Quaternion.Euler(-40f, 0f, 0f);
                    batch.Add(GardenShapes.Bud, lotus, p + Vector3.up * 0.2f + q * new Vector3(0f, 0f, 0.35f), q, new Vector3(0.4f, 0.12f, 0.8f));
                }
                batch.Add(GardenShapes.Bud, m.Gold, p + Vector3.up * 0.3f, Quaternion.identity, Vector3.one * 0.3f);
            }
        }
    }

    // ───────── Round the rim: palm groves and orchards on the plain, and a river beyond ─────────

    private static void BuildRim(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        float rimHalf = Half(Tiers - 1);
        (float d, float pitch)[] rows = { (18f, 9f), (32f, 12f), (50f, 16f) };
        for (int side = 0; side < 4; side++)
        {
            Matrix4x4 frame = WallFrame(Tiers - 1, side);
            foreach (var row in rows)
            {
                float reach = rimHalf + row.d;
                for (float x = -reach + row.pitch * 0.5f; x < reach - row.pitch * 0.3f; x += row.pitch)
                {
                    Vector3 at = new Vector3(x + (F(rng) - 0.5f) * row.pitch * 0.6f, TierH + 0.06f, -row.d + (F(rng) - 0.5f) * 6f);
                    float pick = F(rng);
                    if (pick < 0.55f) Palm(batch, m, rng, frame, at);
                    else if (pick < 0.85f) FloweringTree(batch, m, rng, frame, at, side);
                    else
                    {
                        float h = 7f + F(rng) * 3f;
                        batch.Add(frame, GardenShapes.Cone, m.Cypress, at, Quaternion.identity, new Vector3(2.4f, h, 2.4f));
                    }
                }
            }
        }
        // The river, winding round in a wide loop
        int seg = 72;
        for (int i = 0; i < seg; i++)
        {
            float a = (i + 0.5f) / seg * Mathf.PI * 2f;
            float r = 165f + Mathf.Sin(a * 3f) * 8f;
            Quaternion along = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            batch.Add(GardenShapes.Cube, m.Water, new Vector3(Mathf.Cos(a) * r, RimY + 0.08f, Mathf.Sin(a) * r), along, new Vector3(14f, 0.06f, r * Mathf.PI * 2f / seg * 1.1f));
        }
    }

    // ───────── Giant neon blooms in the corners of two terraces ─────────

    private static void BuildGiantBlooms(Transform root, GardenMaterials m, System.Random rng)
    {
        var holder = new GameObject("Giant Blooms").transform;
        holder.SetParent(root, false);
        Color[] colors = { new Color(1f, 0.3f, 0.75f), new Color(0.3f, 1f, 0.95f), new Color(0.72f, 0.45f, 1f), new Color(1f, 0.82f, 0.3f) };
        int i = 0;
        foreach (int tier in new[] { 0, 2 })
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    float c = Half(tier) + Step * 0.5f;
                    var flower = new GameObject("Giant Bloom").transform;
                    flower.SetParent(holder, false);
                    flower.position = new Vector3(cx * c, TopY(tier) + 0.06f, cz * c);
                    flower.rotation = Quaternion.Euler(0f, F(rng) * 360f, 0f);
                    float h = 6f + F(rng) * 2.5f;
                    GardenShapes.Piece(flower, GardenShapes.Cylinder, m.Stem, new Vector3(0f, h * 0.5f, 0f), Quaternion.identity, new Vector3(0.45f, h * 0.5f, 0.45f));
                    for (int l = 0; l < 2; l++)
                    {
                        Quaternion q = Quaternion.Euler(0f, l * 180f + 30f, 0f) * Quaternion.Euler(-35f, 0f, 0f);
                        GardenShapes.Piece(flower, GardenShapes.Sphere, m.Leaf, new Vector3(0f, h * 0.25f + l * 0.8f, 0f) + q * new Vector3(0f, 0f, 1.6f), q, new Vector3(1.4f, 0.25f, 3.4f));
                    }
                    var head = new GameObject("Head").transform;
                    head.SetParent(flower, false);
                    head.localPosition = new Vector3(0f, h, 0f);
                    Color color = colors[i++ % colors.Length];
                    var petalMat = new Material(Shader.Find("Unlit/Color")) { color = color };
                    var innerMat = new Material(Shader.Find("Unlit/Color")) { color = Color.Lerp(color, Color.white, 0.55f) };
                    var petals = new List<(Transform pivot, float yaw, float open)>();
                    for (int p = 0; p < 7; p++) petals.Add(Petal(head, petalMat, p * 360f / 7f, 2.8f, 1.6f, 45f));
                    for (int p = 0; p < 5; p++) petals.Add(Petal(head, innerMat, p * 72f + 36f, 1.8f, 1.1f, 68f));
                    GardenShapes.Piece(head, GardenShapes.Sphere, m.Gold, new Vector3(0f, 0.35f, 0f), Quaternion.identity, Vector3.one * 1.1f);
                    flower.gameObject.AddComponent<GardenBloom>().Init(head, petals, petalMat, color, F(rng) * 10f);
                }
    }

    private static (Transform, float, float) Petal(Transform head, Material mat, float yaw, float length, float widthScale, float open)
    {
        var pivot = new GameObject("Petal").transform;
        pivot.SetParent(head, false);
        GardenShapes.Piece(pivot, GardenShapes.Sphere, mat, new Vector3(0f, 0f, length * 0.5f), Quaternion.identity, new Vector3(widthScale, 0.22f, length));
        pivot.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-open, 0f, 0f);
        return (pivot, yaw, open);
    }

    /// <summary>Where each waterfall pours over: the middle of every wall, as (top centre, inward normal)</summary>
    public static IEnumerable<(Vector3 top, Vector3 inward)> Waterfalls()
    {
        for (int tier = 0; tier < Tiers; tier++)
            for (int side = 0; side < 4; side++)
                yield return (-Inward[side] * Half(tier) + Vector3.up * TopY(tier), Inward[side]);
    }
}

/// <summary>The garden's materials, made once per build</summary>
public class GardenMaterials
{
    public readonly Material Sandstone = OrbitScenery.Lit(new Color(0.62f, 0.44f, 0.32f), new Color(0.07f, 0.04f, 0.03f), 0f, 0.2f);
    public readonly Material SandstoneLight = OrbitScenery.Lit(new Color(0.8f, 0.62f, 0.44f), new Color(0.09f, 0.06f, 0.04f), 0f, 0.25f);
    public readonly Material Grass = OrbitScenery.Lit(new Color(0.06f, 0.2f, 0.09f), new Color(0.01f, 0.04f, 0.02f), 0f, 0.1f);
    public readonly Material Hedge = OrbitScenery.Lit(new Color(0.05f, 0.24f, 0.11f), new Color(0f, 0.05f, 0.025f), 0f, 0.15f);
    public readonly Material Cypress = OrbitScenery.Lit(new Color(0.03f, 0.15f, 0.1f), new Color(0f, 0.035f, 0.03f), 0f, 0.15f);
    public readonly Material Leaf = OrbitScenery.Lit(new Color(0.1f, 0.45f, 0.18f), new Color(0.02f, 0.11f, 0.04f), 0f, 0.3f);
    public readonly Material Palm = OrbitScenery.Lit(new Color(0.12f, 0.5f, 0.25f), new Color(0.03f, 0.14f, 0.07f), 0f, 0.3f);
    public readonly Material Stem = OrbitScenery.Lit(new Color(0.1f, 0.35f, 0.15f), new Color(0.01f, 0.05f, 0.02f), 0f, 0.2f);
    public readonly Material Bark = OrbitScenery.Lit(new Color(0.2f, 0.12f, 0.09f), Color.black, 0f, 0.15f);
    public readonly Material Water = OrbitScenery.Lit(new Color(0.04f, 0.2f, 0.26f), new Color(0.04f, 0.2f, 0.24f), 0.2f, 0.95f);
    public readonly Material Jacaranda = OrbitScenery.Lit(new Color(0.62f, 0.42f, 1f), new Color(0.2f, 0.1f, 0.38f), 0f, 0.3f);
    public readonly Material Pomegranate = OrbitScenery.Lit(new Color(1f, 0.4f, 0.55f), new Color(0.32f, 0.07f, 0.14f), 0f, 0.3f);
    public readonly Material Recess = Unlit(new Color(0.05f, 0.05f, 0.13f));
    public readonly Material Ishtar = Unlit(new Color(0.12f, 0.36f, 1f));      // the Ishtar Gate's glazed blue
    public readonly Material Gold = Unlit(new Color(1f, 0.82f, 0.4f));
    public readonly Material Lamp = Unlit(new Color(1f, 0.85f, 0.55f));
    public readonly Material Vines = new Material(Shader.Find("Sprites/Default")) { mainTexture = GardenTextures.Vines() };
    // Flower colours: pink, cyan, gold, violet, coral, white
    public readonly Material[] Blooms =
    {
        Unlit(new Color(1f, 0.32f, 0.72f)), Unlit(new Color(0.3f, 1f, 0.92f)), Unlit(new Color(1f, 0.85f, 0.3f)),
        Unlit(new Color(0.68f, 0.45f, 1f)), Unlit(new Color(1f, 0.5f, 0.42f)), Unlit(new Color(1f, 0.95f, 0.92f)),
    };

    private static Material Unlit(Color c) => new Material(Shader.Find("Unlit/Color")) { color = c };
}

/// <summary>
/// Low-poly shapes for the garden (the built-in sphere is ~500 vertices; thousands of flowers need
/// far fewer). Sizes match Unity's primitives: sphere and cylinder 1 wide, cylinder 2 tall and centred,
/// cube 1. The cone stands on its base: 1 wide, 1 tall.
/// </summary>
public static class GardenShapes
{
    private static Mesh cube, sphere, bud, cylinder, cone;
    private static readonly Mesh[] vines = new Mesh[4];
    private static Mesh flat;

    public static Mesh Cube
    {
        get
        {
            if (cube == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = go.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(go);
            }
            return cube;
        }
    }

    public static Mesh Sphere => sphere != null ? sphere : (sphere = MakeSphere(10, 6));
    /// <summary>An even rougher sphere for the thousands of flowers and leaf clumps in the beds</summary>
    public static Mesh Bud => bud != null ? bud : (bud = MakeSphere(5, 3));
    public static Mesh Cylinder => cylinder != null ? cylinder : (cylinder = MakeCylinder(10));
    public static Mesh Cone => cone != null ? cone : (cone = MakeCone(10));
    /// <summary>A flat 1x1 square facing up, UV-mapped, for soft textured patches on the lawn</summary>
    public static Mesh Flat => flat != null ? flat : (flat = MakeFlat());

    private static Mesh MakeSphere(int around, int rings)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float lat = Mathf.PI * r / rings;
            for (int a = 0; a <= around; a++)
            {
                float lon = Mathf.PI * 2f * a / around;
                verts.Add(new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon)) * 0.5f);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int a = 0; a < around; a++)
            {
                int i0 = r * (around + 1) + a, i1 = i0 + around + 1;
                tris.AddRange(new[] { i0, i0 + 1, i1, i1, i0 + 1, i1 + 1 });
            }
        var mesh = new Mesh { name = "Garden Sphere" };
        mesh.SetVertices(verts);
        var normals = new List<Vector3>();
        foreach (Vector3 v in verts) normals.Add(v.normalized);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh MakeCylinder(int around)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        for (int a = 0; a <= around; a++)
        {
            float t = Mathf.PI * 2f * a / around;
            Vector3 n = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
            verts.Add(n * 0.5f + Vector3.down); normals.Add(n);
            verts.Add(n * 0.5f + Vector3.up); normals.Add(n);
        }
        for (int a = 0; a < around; a++)
        {
            int i = a * 2;
            tris.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
        }
        for (int cap = 0; cap < 2; cap++)
        {
            float y = cap == 0 ? -1f : 1f;
            int centre = verts.Count;
            verts.Add(new Vector3(0f, y, 0f)); normals.Add(new Vector3(0f, y, 0f));
            for (int a = 0; a <= around; a++)
            {
                float t = Mathf.PI * 2f * a / around;
                verts.Add(new Vector3(Mathf.Cos(t) * 0.5f, y, Mathf.Sin(t) * 0.5f)); normals.Add(new Vector3(0f, y, 0f));
            }
            for (int a = 0; a < around; a++)
            {
                if (cap == 1) tris.AddRange(new[] { centre, centre + a + 2, centre + a + 1 });
                else tris.AddRange(new[] { centre, centre + a + 1, centre + a + 2 });
            }
        }
        return Finish("Garden Cylinder", verts, normals, tris);
    }

    private static Mesh MakeCone(int around)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        for (int a = 0; a < around; a++)
        {
            float t0 = Mathf.PI * 2f * a / around, t1 = Mathf.PI * 2f * (a + 1) / around, tm = (t0 + t1) * 0.5f;
            Vector3 n = new Vector3(Mathf.Cos(tm), 0.5f, Mathf.Sin(tm)).normalized;
            int i = verts.Count;
            verts.Add(new Vector3(Mathf.Cos(t0) * 0.5f, 0f, Mathf.Sin(t0) * 0.5f));
            verts.Add(new Vector3(0f, 1f, 0f));
            verts.Add(new Vector3(Mathf.Cos(t1) * 0.5f, 0f, Mathf.Sin(t1) * 0.5f));
            normals.Add(n); normals.Add(n); normals.Add(n);
            tris.AddRange(new[] { i, i + 1, i + 2 });
        }
        return Finish("Garden Cone", verts, normals, tris);
    }

    /// <summary>
    /// A flat quad like Flat showing one of four overlapping slices of the vine texture, so neighbouring
    /// curtains don't repeat. Stood up with Euler(90, 0, 0) the texture's v = 0 edge is at the top.
    /// </summary>
    public static Mesh Vine(int variant)
    {
        if (vines[variant] == null)
        {
            Mesh mesh = Object.Instantiate(Flat);
            float u0 = variant * 0.25f;
            mesh.SetUVs(0, new List<Vector2> { new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u0 + 0.5f, 1f), new Vector2(u0 + 0.5f, 0f) });
            mesh.name = "Garden Vine";
            vines[variant] = mesh;
        }
        return vines[variant];
    }

    private static Mesh MakeFlat()
    {
        var mesh = new Mesh { name = "Garden Flat" };
        mesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) });
        mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
        mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh Finish(string name, List<Vector3> verts, List<Vector3> normals, List<int> tris)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>One moving part: a GameObject with a mesh, no collider, no shadows</summary>
    public static Transform Piece(Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = new GameObject(mesh.name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }
}

/// <summary>
/// Collects the garden's many small still shapes and merges them into one mesh per material,
/// so thousands of flowers cost a few draw calls and no GameObjects each
/// </summary>
public class GardenBatch
{
    private readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>();

    public void Add(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale) => Add(Matrix4x4.identity, mesh, mat, pos, rot, scale);

    /// <summary>Add a shape placed in a local frame (a building or tree), given as a matrix</summary>
    public void Add(Matrix4x4 frame, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        if (!parts.TryGetValue(mat, out List<CombineInstance> list)) parts[mat] = list = new List<CombineInstance>();
        list.Add(new CombineInstance { mesh = mesh, transform = frame * Matrix4x4.TRS(pos, rot, scale) });
    }

    public void Build(Transform parent, string name)
    {
        var meshes = parent.gameObject.AddComponent<GardenMeshes>();
        foreach (var kv in parts)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(kv.Value.ToArray(), true, true);
            mesh.RecalculateBounds();
            meshes.Owned.Add(mesh);
            var go = new GameObject(name + " " + kv.Key.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = kv.Key;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        parts.Clear();
    }
}

/// <summary>Frees the merged garden meshes when the scenery goes</summary>
public class GardenMeshes : MonoBehaviour
{
    public readonly List<Mesh> Owned = new List<Mesh>();
    private void OnDestroy() { foreach (Mesh m in Owned) if (m != null) Destroy(m); }
}

/// <summary>A giant bloom: sways on its stem, petals slowly open and close, colour breathes</summary>
public class GardenBloom : MonoBehaviour
{
    private Transform head;
    private List<(Transform pivot, float yaw, float open)> petals;
    private Material glow;
    private Color color;
    private float phase, yaw;

    public void Init(Transform h, List<(Transform, float, float)> p, Material mat, Color c, float ph)
    {
        head = h; petals = p; glow = mat; color = c; phase = ph;
        yaw = transform.localEulerAngles.y;
    }

    private void Update()
    {
        if (head == null) return;
        float t = AccessibilityPrefs.AmbientTime;
        transform.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.5f + phase) * 3f, yaw, Mathf.Sin(t * 0.37f + phase * 1.7f) * 3f);
        float breathe = Mathf.Sin(t * 0.35f + phase);
        foreach (var p in petals)
            p.pivot.localRotation = Quaternion.Euler(0f, p.yaw, 0f) * Quaternion.Euler(-(p.open + breathe * 14f), 0f, 0f);
        head.localRotation = Quaternion.Euler(0f, t * 4f + phase * 10f, 0f);
        glow.color = color * (0.85f + 0.15f * (0.5f + 0.5f * breathe));
    }
}

/// <summary>
/// Waterfalls pouring over the middle of every wall: streaky water whose texture runs downward,
/// with spray where each lands
/// </summary>
public class GardenWaterfalls : MonoBehaviour
{
    private Mesh sheet;
    private readonly List<Vector2> uvs = new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero };

    private void Start()
    {
        sheet = Instantiate(GardenShapes.Flat);
        sheet.name = "Waterfall";
        var water = new Material(Shader.Find("Sprites/Default")) { mainTexture = GardenTextures.Falls(), color = new Color(0.75f, 1f, 1f, 0.85f) };
        var spray = new Material(Shader.Find("Sprites/Default")) { mainTexture = GardenTextures.Soft(1.5f) };
        foreach (var (top, inward) in HangingGardensScenery.Waterfalls())
        {
            var fall = new GameObject("Waterfall").transform;
            fall.SetParent(transform, false);
            fall.position = top + inward * 0.9f + Vector3.down * HangingGardensScenery.TierH * 0.5f;
            fall.rotation = Quaternion.LookRotation(inward) * Quaternion.Euler(90f, 0f, 0f);
            fall.localScale = new Vector3(3.2f, 1f, HangingGardensScenery.TierH + 0.4f);
            fall.gameObject.AddComponent<MeshFilter>().sharedMesh = sheet;
            var r = fall.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = water;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Spray(top + inward * 1.6f + Vector3.down * (HangingGardensScenery.TierH - 0.3f), spray);
        }
    }

    private void Spray(Vector3 at, Material mat)
    {
        var go = new GameObject("Spray");
        go.transform.SetParent(transform, false);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0.2f));
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 18f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 1.2f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
    }

    private void Update()
    {
        // One mesh shared by every waterfall: slide its texture down (v grows downward on the stood-up quad)
        if (sheet == null) return;
        float v = -AccessibilityPrefs.AmbientTime * 0.9f;
        uvs[0] = new Vector2(0f, v); uvs[1] = new Vector2(0f, v + 1f); uvs[2] = new Vector2(1f, v + 1f); uvs[3] = new Vector2(1f, v);
        sheet.SetUVs(0, uvs);
    }

    private void OnDestroy() { if (sheet != null) Destroy(sheet); }
}

/// <summary>Blossom petals drifting down into the gardens on a light breeze</summary>
public class GardenPetals : MonoBehaviour
{
    private void Start()
    {
        var holder = new GameObject("Petals");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(0f, HangingGardensScenery.RimY - 2f, 0f);
        var ps = holder.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.8f, 1f), new Color(0.75f, 0.6f, 1f, 1f));
        main.maxParticles = 320;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 18f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut; // round the board, never in front of it
        shape.radius = 52f;
        shape.donutRadius = 22f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.9f, -0.6f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.4f;
        noise.frequency = 0.15f;
        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        holder.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = GardenTextures.Petal() };
        ps.Play();
    }
}

/// <summary>Golden pollen sparkles rising slowly over the flower beds</summary>
public class GardenPollen : MonoBehaviour
{
    private void Start()
    {
        var holder = new GameObject("Pollen");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(0f, HangingGardensScenery.FloorY + 12f, 0f);
        var ps = holder.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.88f, 0.4f, 1f), new Color(0.6f, 1f, 0.9f, 1f));
        main.maxParticles = 240;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 26f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 50f;
        shape.donutRadius = 20f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        vel.y = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1f;
        noise.frequency = 0.3f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        holder.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = NeonTheme.Circle.texture };
        ps.Play();
    }
}

/// <summary>Neon butterflies fluttering in loops over the terraces</summary>
public class GardenButterflies : MonoBehaviour
{
    private class Fly { public Transform body, left, right; public float radius, height, speed, phase, flap, wobble; }
    private readonly List<Fly> flies = new List<Fly>();

    private void Start()
    {
        var rng = new System.Random(5150);
        Texture2D wing = GardenTextures.Wing();
        Color[] colors = { new Color(0.3f, 1f, 0.95f), new Color(1f, 0.35f, 0.8f), new Color(1f, 0.85f, 0.3f), new Color(0.7f, 0.5f, 1f) };
        var bodyMat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.08f, 0.05f, 0.1f) };
        for (int i = 0; i < 12; i++)
        {
            var f = new Fly();
            f.body = new GameObject("Butterfly").transform;
            f.body.SetParent(transform, false);
            GardenShapes.Piece(f.body, GardenShapes.Sphere, bodyMat, Vector3.zero, Quaternion.identity, new Vector3(0.14f, 0.14f, 0.9f));
            var wingMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = wing, color = colors[i % colors.Length] };
            f.left = Wing(f.body, wingMat, -1f);
            f.right = Wing(f.body, wingMat, 1f);
            f.body.localScale = Vector3.one * (1.3f + (float)rng.NextDouble() * 0.7f);
            f.radius = 34f + (float)rng.NextDouble() * 34f;
            f.height = HangingGardensScenery.FloorY + 8f + (float)rng.NextDouble() * 26f;
            f.speed = (0.012f + (float)rng.NextDouble() * 0.02f) * (rng.NextDouble() < 0.5 ? 1f : -1f);
            f.phase = (float)rng.NextDouble() * Mathf.PI * 2f;
            f.flap = 10f + (float)rng.NextDouble() * 5f;
            f.wobble = (float)rng.NextDouble() * 10f;
            flies.Add(f);
        }
    }

    private static Transform Wing(Transform body, Material mat, float side)
    {
        var hinge = new GameObject("Wing").transform;
        hinge.SetParent(body, false);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(hinge, false);
        quad.transform.localPosition = new Vector3(side * 0.55f, 0f, 0f);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(side * 1.1f, 1.2f, 1f);
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return hinge;
    }

    private void Update()
    {
        float t = AccessibilityPrefs.AmbientTime;
        foreach (Fly f in flies)
        {
            // Loops with little dips and darts, like flower-hopping
            float a = f.phase + t * f.speed * Mathf.PI * 2f + Mathf.Sin(t * 0.8f + f.wobble) * 0.04f;
            float r = f.radius + Mathf.Sin(t * 0.33f + f.wobble) * 7f;
            Vector3 pos = new Vector3(Mathf.Cos(a) * r, f.height + Mathf.Abs(Mathf.Sin(t * 1.1f + f.wobble)) * 2.5f, Mathf.Sin(a) * r);
            Vector3 vel = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(f.speed);
            f.body.localPosition = pos;
            f.body.localRotation = Quaternion.LookRotation(vel) * Quaternion.Euler(-10f, 0f, 0f);
            float angle = Mathf.Sin(t * f.flap + f.wobble) * 55f;
            f.left.localRotation = Quaternion.Euler(0f, 0f, angle);
            f.right.localRotation = Quaternion.Euler(0f, 0f, -angle);
        }
    }
}

/// <summary>Small generated textures for the garden's sprites</summary>
public static class GardenTextures
{
    /// <summary>A soft round glow; higher falloff = tighter centre</summary>
    public static Texture2D Soft(float falloff)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)), falloff));
            }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>A cherry petal: an oval with a notch at the tip and a darker base</summary>
    public static Texture2D Petal()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u / 0.45f + v * v);
                float notch = Mathf.Abs(u) < 0.12f && v > 0.75f ? 1f : 0f;
                float a = Mathf.Clamp01((1f - d) * 6f) * (1f - notch);
                float shade = 0.75f + 0.25f * (v * 0.5f + 0.5f);
                px[y * size + x] = new Color(shade, shade, shade, a);
            }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>Butterfly wing: two rounded lobes with a glowing edge and spots, transparent elsewhere</summary>
    public static Texture2D Wing()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float d1 = new Vector2((u - 0.45f) / 0.5f, (v - 0.65f) / 0.35f).magnitude;
                float d2 = new Vector2((u - 0.35f) / 0.35f, (v - 0.25f) / 0.25f).magnitude;
                float d = Mathf.Min(d1, d2);
                float a = 0f, bright = 0.55f;
                if (d < 1f)
                {
                    a = 0.8f;
                    if (d > 0.8f) { a = 1f; bright = 1f; }                                    // glowing edge
                    if (new Vector2(u - 0.55f, v - 0.7f).magnitude < 0.09f) bright = 1f;        // eye spot
                }
                px[y * size + x] = new Color(bright, bright, bright, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// Hanging vines: leafy strands of different lengths dropping from a thick mat of leaves along the
    /// top (v = 0), hung with clusters of neon flowers; transparent between the strands
    /// </summary>
    public static Texture2D Vines()
    {
        const int w = 256, h = 256;
        var rng = new System.Random(77);
        var px = new Color32[w * h];
        Color[] flowers = { new Color(1f, 0.35f, 0.75f), new Color(0.72f, 0.48f, 1f), new Color(1f, 0.95f, 0.92f), new Color(1f, 0.85f, 0.35f), new Color(0.35f, 1f, 0.92f) };
        void Dot(float cx, float cy, float r, Color c)
        {
            for (int y = Mathf.Max(0, (int)(cy - r)); y <= Mathf.Min(h - 1, (int)(cy + r)); y++)
                for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r * r) continue;
                    float shade = 1f - 0.25f * (dx + dy) / (r * 2f); // lit from one side
                    px[y * w + ((x % w) + w) % w] = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                }
        }
        Color Green() => Color.Lerp(new Color(0.08f, 0.32f, 0.14f), new Color(0.2f, 0.6f, 0.25f), (float)rng.NextDouble());
        // The mat along the top
        for (int k = 0; k < 260; k++) Dot(rng.Next(w), (float)rng.NextDouble() * 34f, 4f + (float)rng.NextDouble() * 6f, Green());
        // Strands
        for (int s = 0; s < 46; s++)
        {
            float x0 = rng.Next(w), len = 60f + (float)rng.NextDouble() * (h - 66f), wave = 3f + (float)rng.NextDouble() * 5f, ph = (float)rng.NextDouble() * 6f;
            Color flower = flowers[rng.Next(flowers.Length)];
            for (float y = 20f; y < len; y += 3.2f)
            {
                float x = x0 + Mathf.Sin(y * 0.06f + ph) * wave;
                float r = 2.6f + 1.4f * (1f - y / len);
                Dot(x + ((int)(y / 3.2f) % 2 == 0 ? -1.8f : 1.8f), y, r, Green());
                if (rng.NextDouble() < 0.22) Dot(x + ((float)rng.NextDouble() - 0.5f) * 6f, y, 1.8f + (float)rng.NextDouble() * 1.4f, flower);
            }
            // Wisteria-like clusters at the tips
            for (int b = 0; b < 7; b++) Dot(x0 + Mathf.Sin(len * 0.06f + ph) * wave + ((float)rng.NextDouble() - 0.5f) * 5f, len - b * 2.6f, 2.2f - b * 0.15f, flower);
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
        tex.wrapModeV = TextureWrapMode.Clamp;
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>Falling water: bright and dark streaks running down, soft at the sides; tiles vertically</summary>
    public static Texture2D Falls()
    {
        const int w = 64, h = 128;
        var rng = new System.Random(31);
        var cols = new float[w];
        for (int x = 0; x < w; x++) cols[x] = 0.55f + 0.45f * (float)rng.NextDouble();
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float side = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 6f);
                float streak = cols[x] * (0.75f + 0.25f * Mathf.Sin((y / (float)h) * Mathf.PI * 2f * (2 + x % 3) + x));
                float b = 0.7f + 0.3f * streak;
                px[y * w + x] = new Color(b, b, b, side * (0.45f + 0.5f * streak));
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}
