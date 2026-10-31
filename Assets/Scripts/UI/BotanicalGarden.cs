using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Botanical Garden backdrop: a glowing formal garden at twilight far below the board.
//
//  - BotanicalSky: the 360° sky. Deep teal overhead fading through turquoise to a rose-gold sunset,
//    long pink clouds, and a skyline of rounded trees, cypress spikes, palms and a distant
//    glasshouse whose glass glows.
//  - BotanicalScenery: a lawn laid out in rings around the board: neon-edged gravel walks,
//    carpet beds of glowing flowers in patterns, clipped hedges with fairy lights, rose arches,
//    cypress avenues, cherry blossom trees, globe lamps, a tiered fountain and a great Victorian
//    glasshouse with palms inside.
//  - Moving bits: giant neon blooms that sway and open and close, butterflies, drifting petals,
//    golden pollen sparkles and the fountain's jets.
//
// The static garden is merged into one mesh per material (GardenBatch), so thousands of
// flowers cost a handful of draw calls. Everything sits well away from the board.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural twilight garden sky. Pure C# so it can run on a worker thread.</summary>
public static class BotanicalSky
{
    // Matches the fog so the far lawn melts into the dusk
    public static readonly Color HazeColor = new Color(0.36f, 0.2f, 0.32f);

    public const float SunU = 0.25f;           // ahead of the default White view (+x), like Melting Clocks
    private const float SunV = 0.465f;         // just above the horizon, half behind the trees
    private const float SunR = 0.05f;
    private const float GlassU = 0.62f;        // the far glasshouse on the skyline

    // Skyline layers
    private const byte Sky = 0, Far = 1, Near = 2, Wing = 3, Dome = 4; // nearer layers draw over farther ones

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

        // Near row: a clipped hedge line all the way round
        int sunX = (int)(SunU * width);
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float hedge = 7f + 3f * Mathf.Sin(u * Mathf.PI * 2f * 9f) + 2f * Mathf.Sin(u * Mathf.PI * 2f * 23f + 1f);
            Rect(layer, width, height, Near, x, x + 1, hy - hedge, hy);
        }
        // Near trees, cypresses and palms, leaving the sun clear
        bool NearSun(float x) => Mathf.Abs(WrapPx(x - sunX, width)) < width * 0.045f;
        for (int i = 0; i < 16; i++)
        {
            float cx = rng.Next(width);
            if (NearSun(cx)) continue;
            float r = 26f + (float)rng.NextDouble() * 30f;
            float lift = 18f + (float)rng.NextDouble() * 22f;
            Disc(layer, width, height, Near, cx, hy - lift - r, r);
            Disc(layer, width, height, Near, cx - r * 0.7f, hy - lift - r * 0.6f, r * 0.7f);
            Disc(layer, width, height, Near, cx + r * 0.7f, hy - lift - r * 0.55f, r * 0.65f);
            Rect(layer, width, height, Near, cx - 3f, cx + 3f, hy - lift - r * 0.5f, hy);
        }
        for (int i = 0; i < 30; i++)
        {
            float cx = rng.Next(width);
            if (NearSun(cx)) continue;
            float w = 6f + (float)rng.NextDouble() * 7f;
            float h = 55f + (float)rng.NextDouble() * 80f;
            Flame(layer, width, height, Near, cx, hy, w, h);
        }
        for (int i = 0; i < 7; i++)
        {
            float cx = rng.Next(width);
            if (NearSun(cx) || Mathf.Abs(WrapPx(cx - GlassU * width, width)) < 180f) continue;
            float h = 90f + (float)rng.NextDouble() * 50f;
            float lean = ((float)rng.NextDouble() - 0.5f) * 30f;
            Palm(layer, width, height, Near, cx, hy, h, lean, rng);
        }

        // The glasshouse: a domed palm house with a long barrel-vaulted wing each side
        float gx = GlassU * width, gBase = hy - 9f;
        const float domeR = 70f, wingR = 34f, wingLen = 120f;
        Disc(layer, width, height, Dome, gx, gBase, domeR);
        Rect(layer, width, height, Dome, gx - 7f, gx + 7f, gBase - domeR - 26f, gBase - domeR + 2f); // cupola
        for (int side = -1; side <= 1; side += 2)
        {
            float x0 = gx + side * (domeR - 10f), x1 = gx + side * (domeR + wingLen);
            Rect(layer, width, height, Wing, Mathf.Min(x0, x1), Mathf.Max(x0, x1), gBase - wingR * 0.5f, hy);
            for (float x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x += 1f)
                Rect(layer, width, height, Wing, x, x + 1f, gBase - wingR * 0.5f - wingR * 0.5f * Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow((x - (x0 + x1) * 0.5f) / (Mathf.Abs(x1 - x0) * 0.5f), 2f))), gBase);
        }

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
        Color nearDark = new Color(0.02f, 0.05f, 0.06f);

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
                    else if (l == Dome || l == Wing)
                    {
                        c = GlassColor(l, x, y, gx, gBase, domeR, wingR, width, hy);
                        if (edge) c = new Color(0.6f, 1f, 0.95f);
                        if (y > gBase) c = Mathf.Abs(y - gBase - 1f) < 1f ? new Color(1f, 0.82f, 0.4f) : nearDark; // plinth with a gold line
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
                    && !(l == Dome || l == Wing)) continue;
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

    // Glowing glass with a neon iron frame: ribs and rings on the dome, arches along the wings
    private static Color GlassColor(byte l, int x, int y, float gx, float gBase, float domeR, float wingR, int width, int hy)
    {
        float dx = WrapPx(x - gx, width), up = gBase - y;
        Color inside = Color.Lerp(new Color(1f, 0.72f, 0.42f), new Color(0.25f, 0.75f, 0.72f), Mathf.Clamp01(up / (domeR * 1.2f)));
        inside *= 0.55f;
        bool frame;
        if (l == Dome)
        {
            float r = Mathf.Sqrt(dx * dx + up * up);
            float ang = Mathf.Atan2(Mathf.Max(up, 0f), dx) * Mathf.Rad2Deg;
            float rib = Mathf.Abs(Mathf.Repeat(ang + 7.5f, 15f) - 7.5f) * Mathf.Deg2Rad * r;
            frame = rib < 1.1f || Mathf.Repeat(r, 14f) < 1.4f || Mathf.Abs(dx) < 1.2f || up < 1.5f;
        }
        else
        {
            frame = Mathf.Repeat(dx, 12f) < 1.3f || Mathf.Repeat(up, 11f) < 1.3f;
        }
        return frame ? new Color(0.45f, 1f, 0.92f) : inside;
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
            float len = 34f + (float)rng.NextDouble() * 16f;
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

public static class BotanicalScenery
{
    public const float GroundY = -30f;

    // The walks: rings round the board, and straight walks out between the beds
    private static readonly float[] Rings = { 46f, 64f, 88f };
    private const int Spokes = 8;
    private const float PathWidth = 3f, SpokeWidth = 3f;
    private const float WalkEnd = 120f;
    private const int FountainSpoke = 1, GlasshouseSpoke = 5;

    private static float SpokeAngle(int k) => (k + 0.5f) / Spokes * Mathf.PI * 2f;
    private static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    private static Vector3 Ground(float a, float r, float lift = 0f) => Dir(a) * r + Vector3.up * (GroundY + lift);

    // Angle from a to the nearest walk out from the board
    private static float SpokeGap(float a)
    {
        float best = float.MaxValue;
        for (int k = 0; k < Spokes; k++) best = Mathf.Min(best, Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, SpokeAngle(k) * Mathf.Rad2Deg)) * Mathf.Deg2Rad);
        return best;
    }

    public static void Build(Transform root)
    {
        var rng = new System.Random(1759);
        var batch = new GardenBatch();

        Material lawn = new Material(Shader.Find("Standard")) { color = new Color(0.05f, 0.14f, 0.08f) };
        lawn.SetFloat("_Glossiness", 0.08f);
        OrbitScenery.Part(root, PrimitiveType.Cylinder, new Vector3(0f, GroundY, 0f), new Vector3(800f, 0.05f, 800f), lawn).name = "Lawn";

        var m = new GardenMaterials();
        BuildWalks(batch, m);
        BuildHedges(batch, m, rng);
        BuildBeds(batch, m, rng);
        BuildCypresses(batch, m, rng);
        BuildArches(batch, m, rng);
        BuildCherryTrees(batch, m, rng);
        BuildTopiary(batch, m, rng);
        BuildFountain(root, batch, m, rng);
        var glows = root.gameObject.AddComponent<GardenGlows>();
        BuildGlasshouse(batch, m, rng, glows);
        BuildLamps(batch, m, glows);
        batch.Build(root, "Garden");

        BuildGiantBlooms(root, m, rng);
        root.gameObject.AddComponent<GardenButterflies>();
        root.gameObject.AddComponent<GardenPetals>();
        root.gameObject.AddComponent<GardenPollen>();
    }

    // ───────── Walks ─────────

    private static void BuildWalks(GardenBatch batch, GardenMaterials m)
    {
        Material[] edges = { m.NeonPink, m.NeonGold, m.NeonCyan };
        for (int ri = 0; ri < Rings.Length; ri++)
        {
            float r = Rings[ri];
            int seg = Mathf.CeilToInt(r * Mathf.PI * 2f / 2.5f);
            float len = r * Mathf.PI * 2f / seg * 1.04f;
            for (int i = 0; i < seg; i++)
            {
                float a = (i + 0.5f) / seg * Mathf.PI * 2f;
                Quaternion along = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
                batch.Add(GardenShapes.Cube, m.Gravel, Ground(a, r, 0.06f), along, new Vector3(PathWidth, 0.12f, len));
                if (SpokeGap(a) * r < SpokeWidth * 0.5f + 0.5f) continue; // no edge line across a walk
                for (int side = -1; side <= 1; side += 2)
                    batch.Add(GardenShapes.Cube, edges[ri], Ground(a, r + side * (PathWidth * 0.5f + 0.15f), 0.07f), along, new Vector3(0.22f, 0.14f, len));
            }
        }

        for (int k = 0; k < Spokes; k++)
        {
            float a = SpokeAngle(k);
            Vector3 dir = Dir(a), across = new Vector3(-dir.z, 0f, dir.x);
            float r0 = Rings[0], r1 = k == GlasshouseSpoke ? 108f : k == FountainSpoke ? 96f : WalkEnd;
            Quaternion along = Quaternion.LookRotation(dir);
            batch.Add(GardenShapes.Cube, m.Gravel, Ground(a, (r0 + r1) * 0.5f, 0.08f), along, new Vector3(SpokeWidth, 0.16f, r1 - r0));
            // Edge lines in the stretches between the rings
            float from = r0 + PathWidth * 0.5f;
            for (int ri = 1; ri <= Rings.Length; ri++)
            {
                float to = ri < Rings.Length ? Rings[ri] - PathWidth * 0.5f : r1;
                for (int side = -1; side <= 1; side += 2)
                    batch.Add(GardenShapes.Cube, m.NeonGold, Ground(a, (from + to) * 0.5f, 0.09f) + across * side * (SpokeWidth * 0.5f + 0.15f), along, new Vector3(0.22f, 0.18f, to - from));
                if (ri < Rings.Length) from = Rings[ri] + PathWidth * 0.5f;
            }
        }
    }

    // ───────── Hedges: a tall clipped ring with fairy lights, and low box borders round the beds ─────────

    private static void BuildHedges(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        Hedge(batch, m, rng, 83.6f, 2.6f, 2.6f, true);
        foreach (float r in new[] { 48.6f, 62.4f, 66.6f, 80.4f })
            Hedge(batch, m, rng, r, 0.8f, 0.7f, false);
    }

    private static void Hedge(GardenBatch batch, GardenMaterials m, System.Random rng, float r, float thick, float tall, bool lights)
    {
        int seg = Mathf.CeilToInt(r * Mathf.PI * 2f / 2.2f);
        float len = r * Mathf.PI * 2f / seg * 1.03f;
        for (int i = 0; i < seg; i++)
        {
            float a = (i + 0.5f) / seg * Mathf.PI * 2f;
            if (SpokeGap(a) * r < SpokeWidth * 0.5f + (lights ? 1.2f : 0.5f)) continue;
            Quaternion along = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            batch.Add(GardenShapes.Cube, m.Hedge, Ground(a, r, tall * 0.5f), along, new Vector3(thick, tall, len));
            if (lights && rng.NextDouble() < 0.6)
            {
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float lift = 0.6f + (float)rng.NextDouble() * (tall - 0.4f);
                batch.Add(GardenShapes.Bud, rng.NextDouble() < 0.7 ? m.Fairy : m.NeonPink, Ground(a, r + side * thick * 0.52f, lift), Quaternion.identity, Vector3.one * 0.32f);
            }
        }
    }

    // ───────── Carpet beds: glowing flowers planted in patterns ─────────

    private static readonly List<(float a, float r)> giantSpots = new List<(float, float)>();

    private static void BuildBeds(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        giantSpots.Clear();
        for (int k = 0; k < Spokes; k++) giantSpots.Add(((k + 1) / (float)Spokes * Mathf.PI * 2f, 74f));

        Material[] palette = m.Blooms;
        (float inner, float outer)[] beds = { (49.6f, 61.4f), (67.6f, 79.4f) };
        for (int b = 0; b < beds.Length; b++)
        {
            int row = 0;
            for (float r = beds[b].inner + 0.9f; r < beds[b].outer - 0.6f; r += 2.4f, row++)
            {
                int count = Mathf.FloorToInt(r * Mathf.PI * 2f / 2.4f);
                for (int i = 0; i < count; i++)
                {
                    float a = (i + 0.5f + (row % 2) * 0.5f) / count * Mathf.PI * 2f;
                    if (SpokeGap(a) * r < SpokeWidth * 0.5f + 1.4f) continue;
                    int sector = Mathf.FloorToInt(Mathf.Repeat(a - SpokeAngle(0), Mathf.PI * 2f) / (Mathf.PI * 2f / Spokes));
                    // Inner beds: bands of colour by row. Outer beds: waves of colour running round.
                    int pick = b == 0
                        ? (row + sector * 2) % palette.Length
                        : Mathf.FloorToInt(Mathf.Repeat(a * 12f / (Mathf.PI * 2f) * 4f + Mathf.Sin(r * 0.5f) * 1.4f + sector, palette.Length));
                    Vector3 at = Ground(a, r) + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.8f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.8f);
                    if (b == 1 && NearGiant(at, 3.4f)) continue;
                    // A clump of leaves with the bloom sitting on top
                    batch.Add(GardenShapes.Bud, m.Leaf, at + Vector3.up * 0.35f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(1.9f, 0.7f, 1.9f));
                    float s = 0.8f + (float)rng.NextDouble() * 0.35f;
                    batch.Add(GardenShapes.Bud, palette[pick], at + Vector3.up * (0.85f + (float)rng.NextDouble() * 0.3f), Quaternion.identity, new Vector3(s, s * 0.8f, s));
                }
            }

            // Tall flower spikes (like foxgloves) dotted through each bed
            for (int k = 0; k < Spokes; k++)
                for (int j = 0; j < 4; j++)
                {
                    float a = SpokeAngle(k) + (0.15f + 0.7f * (float)rng.NextDouble()) * (Mathf.PI * 2f / Spokes);
                    float r = Mathf.Lerp(beds[b].inner + 1.5f, beds[b].outer - 1.5f, (float)rng.NextDouble());
                    Vector3 at = Ground(a, r);
                    if (NearGiant(at, 4f)) continue;
                    Material spike = rng.NextDouble() < 0.5 ? m.Blooms[3] : m.Blooms[0];
                    float h = 2.6f + (float)rng.NextDouble() * 1.6f;
                    batch.Add(GardenShapes.Cylinder, m.Stem, at + Vector3.up * h * 0.5f, Quaternion.identity, new Vector3(0.18f, h * 0.5f, 0.18f));
                    for (int f = 0; f < 6; f++)
                    {
                        float k01 = f / 6f;
                        float s = 0.75f * (1f - k01 * 0.6f);
                        batch.Add(GardenShapes.Bud, spike, at + Vector3.up * (h * 0.4f + k01 * h * 0.62f), Quaternion.identity, new Vector3(s, s * 1.2f, s));
                    }
                }
        }
    }

    private static bool NearGiant(Vector3 at, float r)
    {
        foreach (var g in giantSpots)
        {
            Vector3 c = Ground(g.a, g.r);
            if ((new Vector2(at.x - c.x, at.z - c.z)).sqrMagnitude < r * r) return true;
        }
        return false;
    }

    // ───────── Cypress avenues along the walks past the hedge ─────────

    private static void BuildCypresses(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        for (int k = 0; k < Spokes; k++)
        {
            float a = SpokeAngle(k);
            Vector3 across = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            float end = k == GlasshouseSpoke ? 104f : k == FountainSpoke ? 92f : WalkEnd;
            for (float r = 93f; r <= end; r += 9f)
                for (int side = -1; side <= 1; side += 2)
                {
                    float h = 7f + (float)rng.NextDouble() * 2.5f;
                    Vector3 at = Ground(a, r) + across * side * 4.2f;
                    batch.Add(GardenShapes.Cone, m.Cypress, at, Quaternion.identity, new Vector3(2.4f, h, 2.4f));
                    batch.Add(GardenShapes.Sphere, m.Cypress, at + Vector3.up * 1.2f, Quaternion.identity, new Vector3(2.5f, 2.6f, 2.5f));
                }
        }
    }

    // ───────── Rose arches where the walks pass through the tall hedge ─────────

    private static void BuildArches(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        for (int k = 0; k < Spokes; k++)
        {
            float a = SpokeAngle(k);
            var frame = Matrix4x4.TRS(Ground(a, 83.6f), Quaternion.LookRotation(Dir(a)), Vector3.one);
            const float half = 2.6f, legH = 3.6f;
            for (int side = -1; side <= 1; side += 2)
                batch.Add(frame, GardenShapes.Cube, m.Iron, new Vector3(side * half, legH * 0.5f, 0f), Quaternion.identity, new Vector3(0.3f, legH, 0.3f));
            for (int s = 0; s < 12; s++)
            {
                float t0 = s / 12f * Mathf.PI, t1 = (s + 1) / 12f * Mathf.PI, tm = (t0 + t1) * 0.5f;
                Vector3 p = new Vector3(Mathf.Cos(tm) * half, legH + Mathf.Sin(tm) * half, 0f);
                Quaternion q = Quaternion.LookRotation(Vector3.forward, new Vector3(Mathf.Cos(tm), Mathf.Sin(tm), 0f));
                batch.Add(frame, GardenShapes.Cube, m.Iron, p, q, new Vector3(half * (t1 - t0) * 1.05f, 0.3f, 0.3f));
            }
            // Climbing roses all over it
            for (int f = 0; f < 34; f++)
            {
                float t = (float)rng.NextDouble();
                Vector3 p;
                if (t < 0.5f) { float side = rng.NextDouble() < 0.5 ? -1f : 1f; p = new Vector3(side * half, (float)rng.NextDouble() * legH, 0f); }
                else { float th = (float)rng.NextDouble() * Mathf.PI; p = new Vector3(Mathf.Cos(th) * half, legH + Mathf.Sin(th) * half, 0f); }
                p += new Vector3(((float)rng.NextDouble() - 0.5f) * 0.5f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.7f);
                batch.Add(frame, GardenShapes.Sphere, m.Leaf, p, Quaternion.identity, Vector3.one * 0.8f);
                if (f % 2 == 0) batch.Add(frame, GardenShapes.Sphere, f % 4 == 0 ? m.Blooms[0] : m.Blooms[5], p + new Vector3(0f, 0.15f, 0f), Quaternion.identity, Vector3.one * 0.5f);
            }
        }
    }

    // ───────── Cherry blossom trees in the outer lawn ─────────

    private static void BuildCherryTrees(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        for (int k = 0; k < Spokes; k++)
        {
            float mid = SpokeAngle(k) + Mathf.PI / Spokes;
            for (int j = 0; j < 2; j++)
            {
                float a = mid + (j == 0 ? -1f : 1f) * 0.15f + ((float)rng.NextDouble() - 0.5f) * 0.05f;
                float r = (j == 0 ? 101f : 109f) + (float)rng.NextDouble() * 4f;
                CherryTree(batch, m, Ground(a, r), rng);
            }
        }
    }

    private static void CherryTree(GardenBatch batch, GardenMaterials m, Vector3 at, System.Random rng)
    {
        var frame = Matrix4x4.TRS(at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one);
        float h = 6f + (float)rng.NextDouble() * 2f;
        Quaternion lean = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 10f, 0f, ((float)rng.NextDouble() - 0.5f) * 10f);
        batch.Add(frame, GardenShapes.Cylinder, m.Bark, lean * new Vector3(0f, h * 0.5f, 0f), lean, new Vector3(1.1f, h * 0.5f, 1.1f));
        Vector3 top = lean * new Vector3(0f, h, 0f);
        for (int b = 0; b < 5; b++)
        {
            float yaw = b * 72f + (float)rng.NextDouble() * 30f;
            float len = 4f + (float)rng.NextDouble() * 2.5f;
            Quaternion q = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 45f + (float)rng.NextDouble() * 20f);
            batch.Add(frame, GardenShapes.Cylinder, m.Bark, top + q * Vector3.up * (len * 0.5f), q, new Vector3(0.45f, len * 0.5f, 0.45f));
        }
        // A wide, soft canopy of blossom clouds
        for (int c = 0; c < 16; c++)
        {
            float ca = (float)rng.NextDouble() * Mathf.PI * 2f, cr = (float)rng.NextDouble() * 5.5f;
            Vector3 p = top + new Vector3(Mathf.Cos(ca) * cr, 2.2f + (float)rng.NextDouble() * 3f - cr * 0.25f, Mathf.Sin(ca) * cr);
            float s = 2.8f + (float)rng.NextDouble() * 2.4f;
            batch.Add(frame, GardenShapes.Sphere, rng.NextDouble() < 0.6 ? m.Blossom : m.BlossomPale, p, Quaternion.identity, new Vector3(s, s * 0.8f, s));
        }
        // Fallen petals in a pink ring on the lawn
        batch.Add(frame, GardenShapes.Flat, m.FallenPetals, new Vector3(0f, 0.14f, 0f), Quaternion.identity, new Vector3(14f, 1f, 14f));
    }

    // ───────── Topiary on marble plinths just outside the outer walk ─────────

    private static void BuildTopiary(GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        for (int k = 0; k < Spokes; k++)
        {
            float a = SpokeAngle(k) + Mathf.PI / Spokes;
            Vector3 at = Ground(a, 93.5f);
            batch.Add(GardenShapes.Cube, m.Marble, at + Vector3.up * 0.6f, Quaternion.LookRotation(Dir(a)), new Vector3(2.6f, 1.2f, 2.6f));
            batch.Add(GardenShapes.Cube, m.NeonGold, at + Vector3.up * 1.22f, Quaternion.LookRotation(Dir(a)), new Vector3(2.7f, 0.08f, 2.7f));
            Vector3 b = at + Vector3.up * 1.2f;
            switch (k % 3)
            {
                case 0: // three stacked balls
                    for (int t = 0; t < 3; t++) batch.Add(GardenShapes.Sphere, m.Topiary, b + Vector3.up * (1.1f + t * 1.9f - t * t * 0.1f), Quaternion.identity, Vector3.one * (2.3f - t * 0.55f));
                    break;
                case 1: // a spiral wound round a cone
                    batch.Add(GardenShapes.Cone, m.Hedge, b, Quaternion.identity, new Vector3(1.2f, 6f, 1.2f));
                    for (int s = 0; s < 22; s++)
                    {
                        float k01 = s / 22f, ang = k01 * Mathf.PI * 2f * 3f;
                        float rad = 1.1f * (1f - k01);
                        batch.Add(GardenShapes.Sphere, m.Topiary, b + new Vector3(Mathf.Cos(ang) * rad, 0.3f + k01 * 5.4f, Mathf.Sin(ang) * rad), Quaternion.identity, Vector3.one * (1.2f - k01 * 0.6f));
                    }
                    break;
                default: // a ball on a clipped stem
                    batch.Add(GardenShapes.Cylinder, m.Bark, b + Vector3.up * 1.3f, Quaternion.identity, new Vector3(0.3f, 1.3f, 0.3f));
                    batch.Add(GardenShapes.Sphere, m.Topiary, b + Vector3.up * 3.6f, Quaternion.identity, Vector3.one * 3f);
                    batch.Add(GardenShapes.Sphere, m.Fairy, b + Vector3.up * 3.6f + Dir(a + Mathf.PI) * 1.45f, Quaternion.identity, Vector3.one * 0.3f);
                    break;
            }
        }
    }

    // ───────── The tiered fountain at the end of one walk ─────────

    private static void BuildFountain(Transform root, GardenBatch batch, GardenMaterials m, System.Random rng)
    {
        float a = SpokeAngle(FountainSpoke);
        Vector3 at = Ground(a, 104f);
        const float basin = 8.5f;
        batch.Add(GardenShapes.Cylinder, m.Marble, at + Vector3.up * 0.5f, Quaternion.identity, new Vector3(basin * 2f, 0.5f, basin * 2f));
        batch.Add(GardenShapes.Cylinder, m.Water, at + Vector3.up * 1.02f, Quaternion.identity, new Vector3(basin * 2f - 1.2f, 0.02f, basin * 2f - 1.2f));
        int seg = 48;
        for (int i = 0; i < seg; i++)
        {
            float t = (i + 0.5f) / seg * Mathf.PI * 2f;
            Quaternion along = Quaternion.LookRotation(new Vector3(-Mathf.Sin(t), 0f, Mathf.Cos(t)));
            batch.Add(GardenShapes.Cube, m.NeonCyan, at + new Vector3(Mathf.Cos(t) * basin, 1.04f, Mathf.Sin(t) * basin), along, new Vector3(0.25f, 0.1f, basin * Mathf.PI * 2f / seg * 1.05f));
        }
        // The column and two bowls
        batch.Add(GardenShapes.Cylinder, m.Marble, at + Vector3.up * 2.5f, Quaternion.identity, new Vector3(1.4f, 1.5f, 1.4f));
        batch.Add(GardenShapes.Sphere, m.Marble, at + Vector3.up * 4.1f, Quaternion.identity, new Vector3(7f, 1.2f, 7f));
        batch.Add(GardenShapes.Cylinder, m.Water, at + Vector3.up * 4.45f, Quaternion.identity, new Vector3(6f, 0.02f, 6f));
        batch.Add(GardenShapes.Cylinder, m.Marble, at + Vector3.up * 5.6f, Quaternion.identity, new Vector3(0.8f, 1.2f, 0.8f));
        batch.Add(GardenShapes.Sphere, m.Marble, at + Vector3.up * 6.9f, Quaternion.identity, new Vector3(3.4f, 0.7f, 3.4f));
        batch.Add(GardenShapes.Sphere, m.NeonCyan, at + Vector3.up * 7.4f, Quaternion.identity, Vector3.one * 0.6f);
        // Lily pads and lotus flowers in the basin
        for (int i = 0; i < 14; i++)
        {
            float t = (float)rng.NextDouble() * Mathf.PI * 2f, d = 3.5f + (float)rng.NextDouble() * (basin - 4.6f);
            Vector3 p = at + new Vector3(Mathf.Cos(t) * d, 1.06f, Mathf.Sin(t) * d);
            float s = 1.1f + (float)rng.NextDouble() * 0.7f;
            batch.Add(GardenShapes.Cylinder, m.Leaf, p, Quaternion.identity, new Vector3(s, 0.03f, s));
            if (i % 3 == 0) batch.Add(GardenShapes.Sphere, m.Blooms[0], p + Vector3.up * 0.25f, Quaternion.identity, new Vector3(0.7f, 0.5f, 0.7f));
        }

        var jets = new GameObject("Fountain").transform;
        jets.SetParent(root, false);
        jets.position = at;
        jets.gameObject.AddComponent<GardenFountain>();
    }

    // ───────── The great glasshouse at the end of the opposite walk ─────────

    private static void BuildGlasshouse(GardenBatch batch, GardenMaterials m, System.Random rng, GardenGlows glows)
    {
        float a = SpokeAngle(GlasshouseSpoke);
        Vector3 centre = Ground(a, 124f);
        var frame = Matrix4x4.TRS(centre, Quaternion.LookRotation(-Dir(a)), Vector3.one); // local +z faces the board
        const float R = 15f, wingR = 8f, wingLen = 22f;

        // Low marble plinth all round
        batch.Add(frame, GardenShapes.Cube, m.Marble, new Vector3(0f, 0.4f, 0f), Quaternion.identity, new Vector3((R + wingLen) * 2f, 0.8f, wingR * 2f + 1f));
        batch.Add(frame, GardenShapes.Cylinder, m.Marble, new Vector3(0f, 0.4f, 0f), Quaternion.identity, new Vector3(R * 2f + 1f, 0.4f, R * 2f + 1f));

        // The dome: glass, meridian ribs and rings
        batch.Add(frame, GardenShapes.Sphere, m.Glass, Vector3.zero, Quaternion.identity, Vector3.one * R * 2f);
        for (int mer = 0; mer < 16; mer++)
        {
            float phi = mer / 16f * Mathf.PI * 2f;
            for (int s = 0; s < 10; s++)
            {
                float b0 = s / 10f * Mathf.PI * 0.5f, b1 = (s + 1) / 10f * Mathf.PI * 0.5f;
                Vector3 p0 = OnDome(R, b0, phi), p1 = OnDome(R, b1, phi);
                batch.Add(frame, GardenShapes.Cube, m.NeonCyan, (p0 + p1) * 0.5f, Quaternion.LookRotation(p1 - p0), new Vector3(0.28f, 0.28f, (p1 - p0).magnitude * 1.05f));
            }
        }
        foreach (float beta in new[] { 0.3f, 0.65f, 1.0f, 1.3f })
        {
            int seg = 36;
            for (int s = 0; s < seg; s++)
            {
                Vector3 p0 = OnDome(R, beta, s / (float)seg * Mathf.PI * 2f), p1 = OnDome(R, beta, (s + 1) / (float)seg * Mathf.PI * 2f);
                batch.Add(frame, GardenShapes.Cube, m.NeonCyan, (p0 + p1) * 0.5f, Quaternion.LookRotation(p1 - p0), new Vector3(0.24f, 0.24f, (p1 - p0).magnitude * 1.05f));
            }
        }
        // Lantern cupola on top with a golden finial
        batch.Add(frame, GardenShapes.Cylinder, m.Glass, new Vector3(0f, R + 1.2f, 0f), Quaternion.identity, new Vector3(4.4f, 1.6f, 4.4f));
        batch.Add(frame, GardenShapes.Cone, m.NeonCyan, new Vector3(0f, R + 2.8f, 0f), Quaternion.identity, new Vector3(5f, 2.4f, 5f));
        batch.Add(frame, GardenShapes.Sphere, m.NeonGold, new Vector3(0f, R + 5.6f, 0f), Quaternion.identity, Vector3.one * 0.9f);

        // Two barrel-vaulted wings
        for (int side = -1; side <= 1; side += 2)
        {
            float x0 = side * (R - 3f), x1 = side * (R + wingLen), xm = (x0 + x1) * 0.5f, len = Mathf.Abs(x1 - x0);
            batch.Add(frame, GardenShapes.Cylinder, m.Glass, new Vector3(xm, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(wingR * 2f, len * 0.5f, wingR * 2f));
            for (float x = x0 + side * 2f; Mathf.Abs(x) <= Mathf.Abs(x1) + 0.01f; x += side * 2.5f)
                for (int s = 0; s < 12; s++)
                {
                    float t0 = s / 12f * Mathf.PI, t1 = (s + 1) / 12f * Mathf.PI;
                    Vector3 p0 = new Vector3(x, Mathf.Sin(t0) * wingR, Mathf.Cos(t0) * wingR), p1 = new Vector3(x, Mathf.Sin(t1) * wingR, Mathf.Cos(t1) * wingR);
                    batch.Add(frame, GardenShapes.Cube, m.NeonCyan, (p0 + p1) * 0.5f, Quaternion.LookRotation(p1 - p0), new Vector3(0.22f, 0.22f, (p1 - p0).magnitude * 1.05f));
                }
            foreach (float t in new[] { 0.5f, 1.5707963f, 2.64f })
                batch.Add(frame, GardenShapes.Cube, m.NeonCyan, new Vector3(xm, Mathf.Sin(t) * wingR, Mathf.Cos(t) * wingR), Quaternion.identity, new Vector3(len, 0.22f, 0.22f));
            // Plants crowding the wings
            for (int p = 0; p < 14; p++)
            {
                Vector3 at = new Vector3(Mathf.Lerp(x0, x1, (float)rng.NextDouble()), 0f, ((float)rng.NextDouble() - 0.5f) * wingR * 1.2f);
                float s = 2.2f + (float)rng.NextDouble() * 2f;
                batch.Add(frame, GardenShapes.Sphere, m.Leaf, at + Vector3.up * s * 0.4f, Quaternion.identity, new Vector3(s, s * 0.9f, s));
                if (p % 3 == 0) batch.Add(frame, GardenShapes.Sphere, m.Blooms[p % m.Blooms.Length], at + Vector3.up * s * 0.75f, Quaternion.identity, Vector3.one * 0.7f);
            }
            glows.Add(frame.MultiplyPoint3x4(new Vector3(xm, wingR * 0.45f, 0f)), 16f, new Color(1f, 0.8f, 0.5f, 0.3f), rng.Next());
        }

        // Palms under the dome
        for (int p = 0; p < 4; p++)
        {
            Vector3 at = new Vector3((p - 1.5f) * 5f, 0f, (p % 2 == 0 ? 2f : -3f));
            float h = 9f + p % 3 * 1.6f;
            Quaternion lean = Quaternion.Euler((p % 2 == 0 ? 6f : -5f), 0f, (p - 1.5f) * -4f);
            batch.Add(frame, GardenShapes.Cylinder, m.Bark, at + lean * new Vector3(0f, h * 0.5f, 0f), lean, new Vector3(0.7f, h * 0.5f, 0.7f));
            Vector3 top = at + lean * new Vector3(0f, h, 0f);
            for (int f = 0; f < 8; f++)
            {
                Quaternion q = Quaternion.Euler(0f, f * 45f + p * 13f, 0f) * Quaternion.Euler(-(-25f + (f % 2) * 12f), 0f, 0f);
                batch.Add(frame, GardenShapes.Sphere, m.Palm, top + q * new Vector3(0f, 0f, 2.6f), q, new Vector3(1.3f, 0.2f, 5.4f));
            }
        }
        glows.Add(frame.MultiplyPoint3x4(new Vector3(0f, R * 0.45f, 0f)), 34f, new Color(1f, 0.78f, 0.5f, 0.35f), rng.Next());
    }

    private static Vector3 OnDome(float r, float beta, float phi) =>
        new Vector3(Mathf.Cos(beta) * Mathf.Cos(phi) * r, Mathf.Sin(beta) * r, Mathf.Cos(beta) * Mathf.Sin(phi) * r);

    // ───────── Globe lamps along the walks ─────────

    private static void BuildLamps(GardenBatch batch, GardenMaterials m, GardenGlows glows)
    {
        for (int k = 0; k < Spokes; k++)
        {
            float a = SpokeAngle(k);
            Vector3 across = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            foreach (float r in new[] { 55.5f, 73.5f })
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 at = Ground(a, r) + across * side * (SpokeWidth * 0.5f + 0.9f);
                    const float h = 4.2f;
                    batch.Add(GardenShapes.Cylinder, m.Iron, at + Vector3.up * h * 0.5f, Quaternion.identity, new Vector3(0.22f, h * 0.5f, 0.22f));
                    batch.Add(GardenShapes.Cylinder, m.Iron, at + Vector3.up * 0.25f, Quaternion.identity, new Vector3(0.6f, 0.25f, 0.6f));
                    batch.Add(GardenShapes.Sphere, m.Lamp, at + Vector3.up * (h + 0.5f), Quaternion.identity, Vector3.one * 1.1f);
                    glows.Add(at + Vector3.up * (h + 0.5f), 5.5f, new Color(1f, 0.8f, 0.55f, 0.5f), k * 7 + side + (int)r);
                }
        }
    }

    // ───────── Giant neon blooms that sway and slowly open and close ─────────

    private static void BuildGiantBlooms(Transform root, GardenMaterials m, System.Random rng)
    {
        var holder = new GameObject("Giant Blooms").transform;
        holder.SetParent(root, false);
        Color[] colors = { new Color(1f, 0.3f, 0.75f), new Color(0.3f, 1f, 0.95f), new Color(0.72f, 0.45f, 1f), new Color(1f, 0.82f, 0.3f) };
        for (int i = 0; i < giantSpots.Count; i++)
        {
            var spot = giantSpots[i];
            var flower = new GameObject("Giant Bloom").transform;
            flower.SetParent(holder, false);
            flower.position = Ground(spot.a, spot.r);
            flower.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            float h = 6f + (float)rng.NextDouble() * 2.5f;
            GardenShapes.Piece(flower, GardenShapes.Cylinder, m.Stem, new Vector3(0f, h * 0.5f, 0f), Quaternion.identity, new Vector3(0.45f, h * 0.5f, 0.45f));
            for (int l = 0; l < 2; l++)
            {
                Quaternion q = Quaternion.Euler(0f, l * 180f + 30f, 0f) * Quaternion.Euler(-35f, 0f, 0f);
                GardenShapes.Piece(flower, GardenShapes.Sphere, m.Leaf, new Vector3(0f, h * 0.25f + l * 0.8f, 0f) + q * new Vector3(0f, 0f, 1.6f), q, new Vector3(1.4f, 0.25f, 3.4f));
            }

            var head = new GameObject("Head").transform;
            head.SetParent(flower, false);
            head.localPosition = new Vector3(0f, h, 0f);
            Color color = colors[i % colors.Length];
            var petalMat = new Material(Shader.Find("Unlit/Color")) { color = color };
            var innerMat = new Material(Shader.Find("Unlit/Color")) { color = Color.Lerp(color, Color.white, 0.55f) };
            var petals = new List<(Transform pivot, float yaw, float open)>();
            for (int p = 0; p < 7; p++) petals.Add(Petal(head, petalMat, p * 360f / 7f, 2.8f, 1.6f, 45f));
            for (int p = 0; p < 5; p++) petals.Add(Petal(head, innerMat, p * 72f + 36f, 1.8f, 1.1f, 68f));
            GardenShapes.Piece(head, GardenShapes.Sphere, m.NeonGold, new Vector3(0f, 0.35f, 0f), Quaternion.identity, Vector3.one * 1.1f);
            flower.gameObject.AddComponent<GardenBloom>().Init(head, petals, petalMat, color, (float)rng.NextDouble() * 10f);
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
}

/// <summary>The garden's materials, made once per build</summary>
public class GardenMaterials
{
    public readonly Material Gravel = OrbitScenery.Lit(new Color(0.42f, 0.34f, 0.42f), new Color(0.05f, 0.03f, 0.05f), 0f, 0.2f);
    public readonly Material Hedge = OrbitScenery.Lit(new Color(0.05f, 0.2f, 0.1f), new Color(0f, 0.035f, 0.02f), 0f, 0.15f);
    public readonly Material Topiary = OrbitScenery.Lit(new Color(0.08f, 0.28f, 0.13f), new Color(0.01f, 0.06f, 0.03f), 0f, 0.2f);
    public readonly Material Cypress = OrbitScenery.Lit(new Color(0.03f, 0.13f, 0.09f), new Color(0f, 0.03f, 0.025f), 0f, 0.15f);
    public readonly Material Leaf = OrbitScenery.Lit(new Color(0.1f, 0.42f, 0.18f), new Color(0.02f, 0.1f, 0.04f), 0f, 0.3f);
    public readonly Material Palm = OrbitScenery.Lit(new Color(0.12f, 0.5f, 0.25f), new Color(0.03f, 0.14f, 0.07f), 0f, 0.3f);
    public readonly Material Stem = OrbitScenery.Lit(new Color(0.1f, 0.35f, 0.15f), new Color(0.01f, 0.05f, 0.02f), 0f, 0.2f);
    public readonly Material Bark = OrbitScenery.Lit(new Color(0.12f, 0.07f, 0.08f), Color.black, 0f, 0.15f);
    public readonly Material Marble = OrbitScenery.Lit(new Color(0.78f, 0.74f, 0.82f), new Color(0.04f, 0.035f, 0.05f), 0f, 0.55f);
    public readonly Material Iron = OrbitScenery.Lit(new Color(0.1f, 0.09f, 0.12f), Color.black, 0.6f, 0.4f);
    public readonly Material Water = OrbitScenery.Lit(new Color(0.04f, 0.2f, 0.26f), new Color(0.04f, 0.2f, 0.24f), 0.2f, 0.95f);
    public readonly Material Blossom = OrbitScenery.Lit(new Color(1f, 0.55f, 0.78f), new Color(0.38f, 0.1f, 0.24f), 0f, 0.3f);
    public readonly Material BlossomPale = OrbitScenery.Lit(new Color(1f, 0.78f, 0.9f), new Color(0.32f, 0.16f, 0.24f), 0f, 0.3f);
    public readonly Material FallenPetals = new Material(Shader.Find("Sprites/Default")) { mainTexture = GardenTextures.Soft(0.8f), color = new Color(1f, 0.5f, 0.75f, 0.55f) };
    public readonly Material NeonPink = Unlit(new Color(1f, 0.35f, 0.75f));
    public readonly Material NeonGold = Unlit(new Color(1f, 0.82f, 0.4f));
    public readonly Material NeonCyan = Unlit(new Color(0.45f, 1f, 0.92f));
    public readonly Material Fairy = Unlit(new Color(1f, 0.92f, 0.65f));
    public readonly Material Lamp = Unlit(new Color(1f, 0.88f, 0.62f));
    public readonly Material Glass = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.45f, 1f, 0.9f, 0.13f) };
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
    private static Mesh cube, sphere, bud, cylinder, cone, flat;

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
    public static Mesh Bud => bud != null ? bud : (bud = MakeSphere(7, 4));
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

/// <summary>Soft glows round the lamps and inside the glasshouse, always facing the camera, gently pulsing</summary>
public class GardenGlows : MonoBehaviour
{
    private readonly List<(Transform t, float size, float phase)> glows = new List<(Transform, float, float)>();
    private static Texture2D soft;
    private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>(); // shared, so lamps batch and rebuilds don't leak

    public void Add(Vector3 position, float size, Color color, int seed)
    {
        if (soft == null) soft = GardenTextures.Soft(2f);
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        Destroy(q.GetComponent<Collider>());
        q.name = "Glow";
        q.SetParent(transform, false);
        q.position = position;
        var r = q.GetComponent<MeshRenderer>();
        if (!materials.TryGetValue(color, out Material mat) || mat == null)
            materials[color] = mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = soft, color = color };
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glows.Add((q, size, (new System.Random(seed)).Next(1000) / 100f));
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        float t = AccessibilityPrefs.AmbientTime;
        foreach (var g in glows)
        {
            g.t.localScale = Vector3.one * g.size * (0.92f + 0.08f * Mathf.Sin(t * 1.3f + g.phase));
            if (cam != null) g.t.rotation = Quaternion.LookRotation(g.t.position - cam.transform.position);
        }
    }
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

/// <summary>The fountain's centre jet and the curtain spilling off the top bowl</summary>
public class GardenFountain : MonoBehaviour
{
    private void Start()
    {
        var mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = NeonTheme.Circle.texture };

        // Up from the finial, falling back into the bowls
        var jet = Emitter("Jet", mat, new Vector3(0f, 7.4f, 0f), 0.9f);
        var main = jet.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(6.5f, 8f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 1.9f);
        main.gravityModifier = 0.8f;
        var shape = jet.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = 0.15f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var emission = jet.emission;
        emission.rateOverTime = 90f;
        jet.Play();

        // A ring of drips falling from the big bowl's rim
        var spill = Emitter("Spill", mat, new Vector3(0f, 4.3f, 0f), 0.6f);
        main = spill.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 0.9f);
        main.gravityModifier = 0.9f;
        shape = spill.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 3.5f;
        shape.radiusThickness = 0f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        emission = spill.emission;
        emission.rateOverTime = 110f;
        spill.Play();
    }

    private ParticleSystem Emitter(string name, Material mat, Vector3 at, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 1f, 0.95f, 0.85f), new Color(0.85f, 0.95f, 1f, 0.6f));
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        return ps;
    }
}

/// <summary>Cherry petals drifting down across the garden on a light breeze</summary>
public class GardenPetals : MonoBehaviour
{
    private void Start()
    {
        var holder = new GameObject("Petals");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(0f, BotanicalScenery.GroundY + 16f, 0f);
        var ps = holder.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.8f, 1f), new Color(1f, 0.85f, 0.92f, 1f));
        main.maxParticles = 320;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 18f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut; // round the board, never in front of it
        shape.radius = 88f;
        shape.donutRadius = 32f;
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
        holder.transform.localPosition = new Vector3(0f, BotanicalScenery.GroundY + 2f, 0f);
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
        shape.radius = 64f;
        shape.donutRadius = 18f;
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

/// <summary>Neon butterflies fluttering in loops over the flower beds</summary>
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
            f.radius = 44f + (float)rng.NextDouble() * 38f;
            f.height = BotanicalScenery.GroundY + 2.5f + (float)rng.NextDouble() * 6f;
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
}
