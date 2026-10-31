using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Medieval Kingdom backdrop: a neon storybook kingdom at dusk, far below the board.
// Made to go with the Castle Calamity piece set.
//
//  - KingdomSky: the 360° sky. Indigo overhead fading through magenta to a hot amber
//    sunset, a big low sun, streaky pink clouds, purple mountains and a hilltop castle
//    with lit windows on the far side.
//  - KingdomScenery: grassy ground with a torch-lit road round the board, a castle with
//    towers, cone roofs and waving pennants ahead of White, a village of glowing cottages,
//    lollipop trees, a windmill ahead of Black and striped tournament tents to the sides.
//    Fireflies drift about and now and then a dragon glides across the sky.
//
// Everything sits well away from the board so the board stays the focus.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural sunset sky over the kingdom. Pure C# so it can run on a worker thread.</summary>
public static class KingdomSky
{
    // Matches the fog so the far hills melt into the dusk
    public static readonly Color HazeColor = new Color(0.2f, 0.09f, 0.2f);

    public const float SunU = 0.27f;            // ahead of the default White view (+x), behind the castle
    private const float SunV = 0.455f;
    private const float SunR = 0.055f;
    private const float CastleU = 0.77f;        // the hilltop castle, ahead of Black

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        var rng = new System.Random(seed);
        const float horizon = 0.5f;

        // Two mountain ranges and the hilltop castle, as heights above the horizon per column
        var far = new float[width];
        var near = new float[width];
        var castle = new float[width];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float a = u * Mathf.PI * 2f;
            far[x] = 0.03f + 0.022f * Mathf.Sin(a * 3f + 0.6f) + 0.012f * Mathf.Sin(a * 7f + 2f) + 0.006f * Mathf.Sin(a * 17f);
            // Sharper peaks: fold the wave so the tops come to points
            far[x] += 0.018f * (1f - Mathf.Abs(Mathf.Sin(a * 5f + 1.3f)));
            near[x] = 0.012f + 0.007f * Mathf.Sin(a * 4f + 3f) + 0.004f * Mathf.Sin(a * 11f + 1f);
            // A rise under the castle
            float dc = Wrap(u - CastleU);
            near[x] += 0.016f * Mathf.Exp(-dc * dc / 0.0012f);
        }

        // The hilltop castle: walls with battlements, towers with pointed roofs
        (float u, float w, float top, float roof)[] towers =
        {
            (CastleU - 0.028f, 0.0035f, 0.03f, 0.016f), (CastleU - 0.012f, 0.0045f, 0.045f, 0.022f),
            (CastleU + 0.004f, 0.006f, 0.06f, 0.028f), (CastleU + 0.018f, 0.004f, 0.04f, 0.018f),
            (CastleU + 0.03f, 0.0035f, 0.028f, 0.014f),
        };
        var windows = new HashSet<int>();
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float du = Wrap(u - CastleU);
            float baseH = near[x];
            float h = 0f;
            if (Mathf.Abs(du) < 0.03f)
            {
                h = 0.022f;
                if (((int)((du + 1f) * width / 3f)) % 2 == 0) h += 0.003f;   // battlements
            }
            foreach (var t in towers)
            {
                float d = Mathf.Abs(Wrap(u - t.u));
                if (d < t.w) h = Mathf.Max(h, t.top);
                if (d < t.w * 1.4f) h = Mathf.Max(h, t.top + t.roof * (1f - d / (t.w * 1.4f)));
            }
            if (h > 0f) castle[x] = baseH + h;
        }
        // Lit windows: small warm dots inside the towers and walls
        foreach (var t in towers)
            for (int k = 0; k < 3; k++)
            {
                int wx = (int)(t.u * width);
                float wv = horizon - near[((wx % width) + width) % width] - t.top * (0.3f + 0.25f * k);
                int wy = (int)(wv * height);
                for (int dy = 0; dy < 3; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        windows.Add((wy + dy) * width + ((wx + dx) % width + width) % width);
            }

        var stars = new HashSet<int>();
        for (int k = 0; k < 1100; k++) stars.Add(rng.Next((int)(height * 0.35f)) * width + rng.Next(width));

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color c;
                if (v < horizon)
                {
                    float t = v / horizon;
                    // Indigo overhead, magenta in the middle, hot amber at the horizon
                    Color top = new Color(0.04f, 0.02f, 0.13f), mid = new Color(0.42f, 0.08f, 0.42f), low = new Color(1f, 0.48f, 0.26f);
                    c = t < 0.7f ? Color.Lerp(top, mid, Mathf.Pow(t / 0.7f, 1.4f)) : Color.Lerp(mid, low, Mathf.Pow((t - 0.7f) / 0.3f, 1.3f));
                    // The sunset is strongest on the sun's side of the sky
                    float sunSide = 0.5f + 0.5f * Mathf.Cos(Wrap(u - SunU) * Mathf.PI * 2f);
                    c = Color.Lerp(c * new Color(0.75f, 0.7f, 0.95f), c, 0.45f + 0.55f * sunSide);
                    if (stars.Contains(y * width + x)) c += Color.white * (0.5f * Mathf.Clamp01(1f - t * 1.6f));

                    // The sun: big, low and glowing, with a wide warm halo
                    float du = Wrap(u - SunU) * 2f, dv = v - SunV;
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    c += new Color(1f, 0.45f, 0.35f) * (0.55f * Mathf.Exp(-d * d / (SunR * SunR * 6f)));
                    if (d < SunR)
                    {
                        Color sun = Color.Lerp(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.45f, 0.5f), Mathf.Clamp01((v - (SunV - SunR)) / (SunR * 2f)));
                        c = Color.Lerp(c, sun, Mathf.Clamp01((SunR - d) / 0.002f));
                    }

                    // Long streaky clouds low in the sky, lit pink underneath
                    float cloud = Mathf.PerlinNoise(u * 18f, v * 70f + 4f) * Mathf.PerlinNoise(u * 4f + 11f, v * 12f);
                    float band = Mathf.Exp(-Mathf.Pow((v - 0.4f) / 0.045f, 2f));
                    c = Color.Lerp(c, Color.Lerp(new Color(0.55f, 0.15f, 0.45f), new Color(1f, 0.55f, 0.6f), sunSide), Mathf.Clamp01((cloud - 0.2f) * 4f) * band * 0.8f);

                    // Far mountains: hazy purple, a soft pink rim on top
                    float farTop = horizon - far[x];
                    if (v > farTop)
                    {
                        float rim = Mathf.Clamp01(1f - (v - farTop) * height / 2f);
                        c = Color.Lerp(Color.Lerp(new Color(0.3f, 0.1f, 0.35f), new Color(0.45f, 0.18f, 0.4f), (v - farTop) / Mathf.Max(0.001f, far[x])), new Color(1f, 0.45f, 0.75f), rim * 0.4f);
                    }
                    // Near hills and the castle: dark violet with a neon rim (cyan hills, gold castle)
                    float nearTop = horizon - Mathf.Max(near[x], castle[x]);
                    if (v > nearTop)
                    {
                        float rim = Mathf.Clamp01(1f - (v - nearTop) * height / 2f);
                        Color rimColor = castle[x] > near[x] ? new Color(1f, 0.8f, 0.3f) : new Color(0.3f, 1f, 0.95f);
                        c = Color.Lerp(new Color(0.06f, 0.02f, 0.09f), rimColor, rim * 0.55f);
                        if (windows.Contains(y * width + x)) c = new Color(1f, 0.82f, 0.35f);
                    }
                }
                else
                {
                    // Below the horizon: dusky haze over dark fields
                    float below = v - horizon;
                    c = Color.Lerp(HazeColor * 1.4f, new Color(0.03f, 0.03f, 0.04f), Mathf.Clamp01(below * 5f));
                }
                result[(height - 1 - y) * width + x] = new Color32(B(c.r), B(c.g), B(c.b), 255); // Unity textures start at the bottom row
            }
        }
        return result;
    }

    private static float Wrap(float d) { if (d > 0.5f) d -= 1f; if (d < -0.5f) d += 1f; return d; }
    private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}

public static class KingdomScenery
{
    public const float GroundY = -30f;
    public const float RoadRadius = 46f;

    private static Mesh cone, pennant;

    public static void Build(Transform root)
    {
        var rng = new System.Random(1215);
        Material grass = new Material(Shader.Find("Standard")) { color = new Color(0.04f, 0.07f, 0.05f) };
        grass.SetFloat("_Glossiness", 0.05f);
        OrbitScenery.Part(root, PrimitiveType.Cylinder, new Vector3(0f, GroundY, 0f), new Vector3(800f, 0.05f, 800f), grass).name = "Fields";

        Material stone = OrbitScenery.Lit(new Color(0.42f, 0.38f, 0.46f), new Color(0.03f, 0.02f, 0.05f), 0.05f, 0.2f);
        Material darkStone = OrbitScenery.Lit(new Color(0.22f, 0.2f, 0.26f), Color.black, 0.05f, 0.15f);
        Material road = OrbitScenery.Lit(new Color(0.16f, 0.12f, 0.12f), Color.black, 0f, 0.1f);
        Material plaster = OrbitScenery.Lit(new Color(0.6f, 0.5f, 0.45f), new Color(0.04f, 0.02f, 0.03f), 0f, 0.15f);
        Material timber = OrbitScenery.Lit(new Color(0.12f, 0.07f, 0.06f), Color.black, 0f, 0.2f);
        Material bark = OrbitScenery.Lit(new Color(0.14f, 0.08f, 0.07f), Color.black, 0f, 0.15f);
        Material leaves = OrbitScenery.Lit(new Color(0.1f, 0.3f, 0.14f), new Color(0.02f, 0.12f, 0.05f), 0f, 0.3f);
        Material[] roofs =
        {
            OrbitScenery.Lit(new Color(0.45f, 0.1f, 0.3f), new Color(0.12f, 0.02f, 0.08f), 0.1f, 0.4f),   // berry
            OrbitScenery.Lit(new Color(0.2f, 0.12f, 0.45f), new Color(0.05f, 0.03f, 0.14f), 0.1f, 0.4f),  // violet
            OrbitScenery.Lit(new Color(0.08f, 0.3f, 0.4f), new Color(0.02f, 0.08f, 0.12f), 0.1f, 0.4f),   // teal
        };
        Material gold = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.8f, 0.3f) };
        Material window = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.75f, 0.35f) };
        Material pink = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.35f, 0.75f) };
        Material cyan = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.35f, 1f, 0.95f) };
        Material shadow = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.02f, 0.01f, 0.03f) };

        var still = new GameObject("Kingdom").transform;
        still.SetParent(root, false);

        // A dirt road round the board
        int segments = 72;
        for (int i = 0; i < segments; i++)
        {
            float a = (i + 0.5f) / segments * Mathf.PI * 2f;
            Part(still, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * RoadRadius, GroundY + 0.05f, Mathf.Sin(a) * RoadRadius),
                new Vector3(5f, 0.1f, RoadRadius * Mathf.PI * 2f / segments + 0.3f), road, Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))));
        }

        // The castle ahead of White, facing the board
        Castle(still, new Vector3(108f, GroundY, -14f), stone, darkStone, roofs, gold, window, pink, cyan, shadow);

        // The windmill ahead of Black (not batched: its sails turn)
        var mill = Windmill(root, new Vector3(-82f, GroundY, 18f), plaster, timber, roofs[0], cyan, window);

        // Tournament tents to either side, in two little camps
        Material[] stripes =
        {
            OrbitScenery.Lit(new Color(0.75f, 0.2f, 0.5f), new Color(0.25f, 0.04f, 0.15f), 0f, 0.3f),
            OrbitScenery.Lit(new Color(0.2f, 0.6f, 0.65f), new Color(0.04f, 0.18f, 0.2f), 0f, 0.3f),
            OrbitScenery.Lit(new Color(0.8f, 0.65f, 0.25f), new Color(0.25f, 0.18f, 0.04f), 0f, 0.3f),
            OrbitScenery.Lit(new Color(0.85f, 0.82f, 0.78f), new Color(0.12f, 0.1f, 0.12f), 0f, 0.3f),
        };
        var flags = new List<Transform>();
        foreach (float camp in new[] { 90f, 270f })
            for (int i = 0; i < 4; i++)
            {
                float a = (camp + (i - 1.5f) * 11f) * Mathf.Deg2Rad;
                float d = 66f + (i % 2) * 10f;
                Vector3 at = new Vector3(Mathf.Cos(a) * d, GroundY, Mathf.Sin(a) * d);
                float r = 4f + (float)rng.NextDouble() * 1.5f;
                Material body = stripes[(i + (camp > 180f ? 2 : 0)) % stripes.Length];
                Material top = stripes[(i + 1 + (camp > 180f ? 2 : 0)) % stripes.Length];
                Part(still, PrimitiveType.Cylinder, at + Vector3.up * 2.2f, new Vector3(r * 2f, 2.2f, r * 2f), body);
                Cone(still, at + Vector3.up * 4.4f, r * 2.3f, r * 1.4f, top);
                Part(still, PrimitiveType.Cylinder, at + Vector3.up * 4.45f, new Vector3(r * 2.35f, 0.08f, r * 2.35f), gold); // glowing hem
                Part(still, PrimitiveType.Cube, at + Vector3.up * 1.6f - new Vector3(at.x, 0f, at.z).normalized * r, new Vector3(1.6f, 3.2f, 0.1f), shadow, Quaternion.LookRotation(new Vector3(at.x, 0f, at.z))); // doorway
                flags.Add(Pennant(root, at + Vector3.up * (4.4f + r * 1.4f), 2.2f, i % 2 == 0 ? pink : cyan));
            }

        // A village of cottages round the road, leaving the castle, mill and camps clear
        int placed = 0;
        for (int tries = 0; tries < 200 && placed < 34; tries++)
        {
            float deg = (float)rng.NextDouble() * 360f;
            if (Near(deg, -7f, 30f) || Near(deg, 167f, 18f) || Near(deg, 90f, 22f) || Near(deg, 270f, 22f)) continue;
            float a = deg * Mathf.Deg2Rad;
            float d = (rng.NextDouble() < 0.5 ? 54f : 74f) + (float)rng.NextDouble() * 8f;
            Cottage(still, new Vector3(Mathf.Cos(a) * d, GroundY, Mathf.Sin(a) * d), plaster, timber, roofs[rng.Next(roofs.Length)], rng.NextDouble() < 0.5 ? pink : cyan, window, shadow, rng);
            placed++;
        }

        // Lollipop trees dotted about
        for (int i = 0; i < 26; i++)
        {
            float deg = (float)rng.NextDouble() * 360f;
            if (Near(deg, -7f, 22f)) continue;
            float a = deg * Mathf.Deg2Rad;
            float d = 38f + (float)rng.NextDouble() * 55f;
            if (Mathf.Abs(d - RoadRadius) < 4f) d += 8f;
            Vector3 at = new Vector3(Mathf.Cos(a) * d, GroundY, Mathf.Sin(a) * d);
            float h = 4f + (float)rng.NextDouble() * 3f;
            Part(still, PrimitiveType.Cylinder, at + Vector3.up * h * 0.5f, new Vector3(0.7f, h * 0.5f, 0.7f), bark);
            float s = 4.5f + (float)rng.NextDouble() * 2.5f;
            Part(still, PrimitiveType.Sphere, at + Vector3.up * (h + s * 0.35f), new Vector3(s, s * 0.9f, s), leaves);
        }
        // Pennants on the castle towers wave too, so they come out of the batch
        foreach (Transform t in still.GetComponentsInChildren<Transform>())
            if (t.name == "Pennant") flags.Add(t);
        foreach (Transform t in flags) t.SetParent(root, true);
        StaticBatchingUtility.Combine(still.gameObject);

        root.gameObject.AddComponent<KingdomPennants>().Init(flags);
        root.gameObject.AddComponent<KingdomWindmill>().Init(mill);
        root.gameObject.AddComponent<KingdomTorches>();
        root.gameObject.AddComponent<KingdomFireflies>();
        root.gameObject.AddComponent<KingdomDragon>();
    }

    private static bool Near(float deg, float centre, float half) => Mathf.Abs(Mathf.DeltaAngle(deg, centre)) < half;

    // Curtain walls with battlements, a round tower at each corner, a tall keep with a turret,
    // a glowing portcullis facing the board and lit windows
    private static void Castle(Transform parent, Vector3 at, Material stone, Material darkStone, Material[] roofs, Material gold, Material window, Material pink, Material cyan, Material shadow)
    {
        var castle = new GameObject("Castle").transform;
        castle.SetParent(parent, false);
        castle.position = at;
        castle.rotation = Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z));   // +z faces the board
        const float half = 20f, wallH = 9f;

        // Walls: front (with the gate), back and sides
        for (int side = 0; side < 4; side++)
        {
            Quaternion q = Quaternion.Euler(0f, side * 90f, 0f);
            Vector3 mid = q * new Vector3(0f, wallH * 0.5f, half);
            Part(castle, PrimitiveType.Cube, mid, new Vector3(half * 2f, wallH, 2f), stone, q);
            for (int m = 0; m < 14; m++)
            {
                float x = -half + 1.5f + m * (half * 2f - 3f) / 13f;
                Part(castle, PrimitiveType.Cube, q * new Vector3(x, wallH + 0.7f, half), new Vector3(1.4f, 1.4f, 2f), stone, q);
            }
            Part(castle, PrimitiveType.Cube, q * new Vector3(0f, wallH + 0.05f, half + 1.05f), new Vector3(half * 2f, 0.15f, 0.05f), side == 0 ? gold : cyan, q); // neon line along the walkway
        }
        // The gate: a dark arch with glowing bars
        Part(castle, PrimitiveType.Cube, new Vector3(0f, 3f, half + 1.02f), new Vector3(5f, 6f, 0.05f), shadow);
        Part(castle, PrimitiveType.Cylinder, new Vector3(0f, 6f, half + 1.01f), new Vector3(5f, 0.03f, 5f), shadow, Quaternion.Euler(90f, 0f, 0f));
        for (int b = 0; b < 5; b++)
            Part(castle, PrimitiveType.Cube, new Vector3(-2f + b, 3.6f, half + 1.06f), new Vector3(0.18f, 7f, 0.05f), gold);
        Part(castle, PrimitiveType.Cube, new Vector3(0f, 2.5f, half + 1.07f), new Vector3(4.6f, 0.18f, 0.05f), gold);
        Part(castle, PrimitiveType.Cube, new Vector3(0f, 5f, half + 1.07f), new Vector3(4.6f, 0.18f, 0.05f), gold);

        // Corner towers
        for (int c = 0; c < 4; c++)
        {
            Vector3 p = new Vector3((c % 2 == 0 ? -1 : 1) * half, 0f, (c < 2 ? -1 : 1) * half);
            Tower(castle, p, 4.2f, 16f, stone, roofs[c % 2 == 0 ? 0 : 1], gold, window, c < 2 ? 0 : 2);
        }

        // The keep: a big block, a turret on each corner and one tall tower
        Part(castle, PrimitiveType.Cube, new Vector3(0f, 13f, -4f), new Vector3(18f, 26f, 16f), darkStone);
        for (int m = 0; m < 9; m++)
        {
            float x = -8f + m * 2f;
            Part(castle, PrimitiveType.Cube, new Vector3(x, 26.7f, 4f), new Vector3(1.3f, 1.4f, 1.3f), darkStone);
        }
        for (int r = 0; r < 3; r++)
            for (int w = 0; w < 3; w++)
                Part(castle, PrimitiveType.Cube, new Vector3(-5f + w * 5f, 9f + r * 6f, 4.03f), new Vector3(1.4f, 2.4f, 0.05f), window);
        Part(castle, PrimitiveType.Cube, new Vector3(0f, 26.05f, 4.05f), new Vector3(18f, 0.2f, 0.05f), pink);
        Tower(castle, new Vector3(-9f, 0f, 4f), 2.4f, 31f, darkStone, roofs[1], gold, window, 3);
        Tower(castle, new Vector3(9f, 0f, 4f), 2.4f, 31f, darkStone, roofs[1], gold, window, 3);
        Tower(castle, new Vector3(3f, 0f, -6f), 3.6f, 42f, stone, roofs[0], gold, window, 4, pennantSize: 5f);
    }

    private static void Tower(Transform parent, Vector3 foot, float radius, float height, Material stone, Material roof, Material rim, Material window, int windows, float pennantSize = 3f)
    {
        Part(parent, PrimitiveType.Cylinder, foot + Vector3.up * height * 0.5f, new Vector3(radius * 2f, height * 0.5f, radius * 2f), stone);
        Part(parent, PrimitiveType.Cylinder, foot + Vector3.up * height, new Vector3(radius * 2.5f, 0.12f, radius * 2.5f), rim);   // glowing ring under the roof
        float roofH = radius * 2.4f;
        Cone(parent, foot + Vector3.up * height, radius * 2.5f, roofH, roof);
        for (int w = 0; w < windows; w++)
        {
            Vector3 p = foot + new Vector3(0f, height * (0.3f + 0.6f * w / Mathf.Max(1, windows)), radius + 0.02f);
            Part(parent, PrimitiveType.Cube, p, new Vector3(0.8f, 1.8f, 0.05f), window);
        }
        Pennant(parent, foot + Vector3.up * (height + roofH), pennantSize, rim);
    }

    // Plaster walls, a pitched roof with a neon ridge, a glowing window or two and a chimney
    private static void Cottage(Transform parent, Vector3 at, Material plaster, Material timber, Material roof, Material ridge, Material window, Material shadow, System.Random rng)
    {
        var house = new GameObject("Cottage").transform;
        house.SetParent(parent, false);
        house.position = at;
        house.rotation = Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z)) * Quaternion.Euler(0f, ((float)rng.NextDouble() - 0.5f) * 30f, 0f);
        float w = 4.5f + (float)rng.NextDouble() * 2f, h = 3.5f + (float)rng.NextDouble() * 1.5f, d = 4f + (float)rng.NextDouble() * 2f;
        Part(house, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), plaster);
        // Timber framing: a beam across and two posts on the front
        Part(house, PrimitiveType.Cube, new Vector3(0f, h * 0.55f, d * 0.5f + 0.03f), new Vector3(w, 0.25f, 0.06f), timber);
        Part(house, PrimitiveType.Cube, new Vector3(-w * 0.45f, h * 0.5f, d * 0.5f + 0.03f), new Vector3(0.25f, h, 0.06f), timber);
        Part(house, PrimitiveType.Cube, new Vector3(w * 0.45f, h * 0.5f, d * 0.5f + 0.03f), new Vector3(0.25f, h, 0.06f), timber);
        // The roof: a cube turned 45° so only its top half pokes out as a ridge
        float s = w * 0.74f;
        Part(house, PrimitiveType.Cube, new Vector3(0f, h, 0f), new Vector3(s, s, d + 0.8f), roof, Quaternion.Euler(0f, 0f, 45f));
        Part(house, PrimitiveType.Cube, new Vector3(0f, h + s * 0.707f, 0f), new Vector3(0.18f, 0.18f, d + 0.9f), ridge);
        Part(house, PrimitiveType.Cube, new Vector3(-w * 0.2f, h * 0.27f, d * 0.5f + 0.04f), new Vector3(0.9f, h * 0.5f, 0.05f), shadow);   // door
        Part(house, PrimitiveType.Cube, new Vector3(w * 0.22f, h * 0.3f, d * 0.5f + 0.04f), new Vector3(0.9f, 0.8f, 0.05f), window);
        if (rng.NextDouble() < 0.6) Part(house, PrimitiveType.Cube, new Vector3(0f, h * 0.8f, d * 0.5f + 0.04f), new Vector3(0.7f, 0.6f, 0.05f), window);
        if (rng.NextDouble() < 0.5) Part(house, PrimitiveType.Cube, new Vector3(w * 0.25f, h + s * 0.45f, -d * 0.2f), new Vector3(0.7f, 1.8f, 0.7f), timber);   // chimney
    }

    // A round stone mill with a cone cap; returns the hub the sails turn on
    private static Transform Windmill(Transform parent, Vector3 at, Material plaster, Material timber, Material roof, Material neon, Material window)
    {
        var mill = new GameObject("Windmill").transform;
        mill.SetParent(parent, false);
        mill.position = at;
        mill.rotation = Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z));
        Part(mill, PrimitiveType.Cylinder, new Vector3(0f, 8f, 0f), new Vector3(9f, 8f, 9f), plaster);
        Part(mill, PrimitiveType.Cylinder, new Vector3(0f, 16f, 0f), new Vector3(11f, 0.12f, 11f), neon);
        Cone(mill, new Vector3(0f, 16f, 0f), 11f, 8f, roof);
        Part(mill, PrimitiveType.Cube, new Vector3(0f, 2f, 4.5f), new Vector3(2f, 4f, 0.1f), timber);
        Part(mill, PrimitiveType.Cube, new Vector3(0f, 9f, 4.52f), new Vector3(1.1f, 1.6f, 0.05f), window);
        var hub = new GameObject("Sails").transform;
        hub.SetParent(mill, false);
        hub.localPosition = new Vector3(0f, 15f, 5.6f);
        Part(hub, PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.4f, 0.6f, 1.4f), timber, Quaternion.Euler(90f, 0f, 0f));
        for (int i = 0; i < 4; i++)
        {
            var arm = new GameObject("Sail").transform;
            arm.SetParent(hub, false);
            arm.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
            Part(arm, PrimitiveType.Cube, new Vector3(0f, 6f, 0.3f), new Vector3(0.4f, 12f, 0.3f), timber);
            Part(arm, PrimitiveType.Cube, new Vector3(1.3f, 7f, 0.35f), new Vector3(2.2f, 9f, 0.08f), plaster);
            Part(arm, PrimitiveType.Cube, new Vector3(2.45f, 7f, 0.42f), new Vector3(0.12f, 9f, 0.04f), neon);   // neon edge along each sail
        }
        return hub;
    }

    /// <summary>A pole with a long triangular flag; the flag's pivot is named "Pennant" and returned</summary>
    private static Transform Pennant(Transform parent, Vector3 foot, float size, Material cloth)
    {
        Material pole = OrbitScenery.Lit(new Color(0.15f, 0.12f, 0.1f), Color.black, 0.4f, 0.4f);
        Part(parent, PrimitiveType.Cylinder, foot + Vector3.up * size * 0.5f, new Vector3(0.15f, size * 0.5f, 0.15f), pole);
        var pivot = new GameObject("Pennant").transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = foot + Vector3.up * size * 0.92f;
        var flag = new GameObject("Flag");
        flag.transform.SetParent(pivot, false);
        flag.transform.localScale = new Vector3(size * 0.9f, size * 0.35f, 1f);
        flag.AddComponent<MeshFilter>().sharedMesh = PennantMesh();
        var r = flag.AddComponent<MeshRenderer>();
        r.sharedMaterial = cloth;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return pivot;
    }

    /// <summary>A cone with its base centred on foot (diameter like Unity's primitives, so 1 = radius 0.5)</summary>
    private static Transform Cone(Transform parent, Vector3 foot, float diameter, float height, Material mat)
    {
        var go = new GameObject("Cone");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = foot;
        go.transform.localScale = new Vector3(diameter, height, diameter);
        go.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    private static Mesh ConeMesh()
    {
        if (cone != null) return cone;
        const int sides = 16;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            // Each face gets its own tip and base corners so the shading stays crisp
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f), b1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            Vector3 n = Vector3.Cross(b1 - b0, Vector3.up - b0).normalized;
            if (n.y < 0f) n = -n;
            int k = verts.Count;
            verts.Add(Vector3.up); verts.Add(b0); verts.Add(b1);
            normals.Add(n); normals.Add(n); normals.Add(n);
            // Wound so the outside faces out
            Vector3 face = Vector3.Cross(b0 - Vector3.up, b1 - Vector3.up);
            if (Vector3.Dot(face, n) > 0f) { tris.Add(k); tris.Add(k + 1); tris.Add(k + 2); }
            else { tris.Add(k); tris.Add(k + 2); tris.Add(k + 1); }
        }
        cone = new Mesh { name = "Kingdom Cone" };
        cone.SetVertices(verts);
        cone.SetNormals(normals);
        cone.SetTriangles(tris, 0);
        cone.RecalculateBounds();
        return cone;
    }

    // A flat triangle pointing along +x from the pole, both sides visible
    private static Mesh PennantMesh()
    {
        if (pennant != null) return pennant;
        pennant = new Mesh { name = "Kingdom Pennant" };
        pennant.vertices = new[] { new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(1f, 0f, 0f) };
        pennant.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.forward, Vector3.forward, Vector3.forward };
        pennant.triangles = new[] { 0, 2, 1, 3, 4, 5 };
        pennant.RecalculateBounds();
        return pennant;
    }

    private static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        => OrbitScenery.Part(parent, type, pos, scale, mat, rot);
}

/// <summary>Every pennant flutters in the evening breeze</summary>
public class KingdomPennants : MonoBehaviour
{
    private readonly List<(Transform t, float phase)> flags = new List<(Transform, float)>();

    public void Init(List<Transform> pennants)
    {
        for (int i = 0; i < pennants.Count; i++) flags.Add((pennants[i], i * 1.7f));
    }

    private void LateUpdate()
    {
        float t = AccessibilityPrefs.AmbientTime;
        foreach (var f in flags)
        {
            if (f.t == null) continue;
            // All blow roughly the same way, each with its own flutter
            float yaw = 200f + 18f * Mathf.Sin(t * 1.3f + f.phase) + 6f * Mathf.Sin(t * 4.1f + f.phase * 2f);
            f.t.rotation = Quaternion.Euler(0f, yaw, 4f * Mathf.Sin(t * 3f + f.phase));
        }
    }
}

/// <summary>The windmill's sails turn slowly</summary>
public class KingdomWindmill : MonoBehaviour
{
    private Transform sails;

    public void Init(Transform hub) => sails = hub;

    private void LateUpdate()
    {
        if (sails != null) sails.localRotation = Quaternion.Euler(0f, 0f, AccessibilityPrefs.AmbientTime * -14f);
    }
}

/// <summary>Torches on posts along the road, each with a flickering flame and warm glow</summary>
public class KingdomTorches : MonoBehaviour
{
    private readonly List<(Material flame, Transform glow, float phase)> torches = new List<(Material, Transform, float)>();

    private void Start()
    {
        var rng = new System.Random(1066);
        Material wood = OrbitScenery.Lit(new Color(0.16f, 0.1f, 0.08f), Color.black, 0f, 0.2f);
        Material iron = OrbitScenery.Lit(new Color(0.1f, 0.09f, 0.1f), Color.black, 0.6f, 0.4f);
        var glowMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = Soft(), color = new Color(1f, 0.6f, 0.25f, 0.55f) };
        const int count = 18;
        for (int i = 0; i < count; i++)
        {
            float a = (i + 0.5f) / count * Mathf.PI * 2f;
            float d = KingdomScenery.RoadRadius + (i % 2 == 0 ? 3.5f : -3.5f);   // alternating sides of the road
            var post = new GameObject("Torch").transform;
            post.SetParent(transform, false);
            post.position = new Vector3(Mathf.Cos(a) * d, KingdomScenery.GroundY, Mathf.Sin(a) * d);
            OrbitScenery.Part(post, PrimitiveType.Cylinder, new Vector3(0f, 2.5f, 0f), new Vector3(0.35f, 2.5f, 0.35f), wood);
            OrbitScenery.Part(post, PrimitiveType.Cylinder, new Vector3(0f, 5.1f, 0f), new Vector3(0.8f, 0.25f, 0.8f), iron);   // the bowl
            var flame = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.7f, 0.3f) };
            Transform fire = OrbitScenery.Part(post, PrimitiveType.Sphere, new Vector3(0f, 5.8f, 0f), new Vector3(0.7f, 1.2f, 0.7f), flame);
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(glow.GetComponent<Collider>());
            glow.name = "Glow";
            glow.SetParent(post, false);
            glow.localPosition = fire.localPosition;
            var gr = glow.GetComponent<MeshRenderer>();
            gr.sharedMaterial = glowMat;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            torches.Add((flame, glow, (float)rng.NextDouble() * 10f));
        }
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        float t = AccessibilityPrefs.AmbientTime;
        foreach (var l in torches)
        {
            float f = 0.85f + 0.1f * Mathf.Sin(t * 10f + l.phase) + 0.07f * Mathf.Sin(t * 25f + l.phase * 2f);
            l.flame.color = new Color(1f, 0.55f + 0.25f * f, 0.2f + 0.15f * f);
            l.glow.localScale = Vector3.one * 10f * f;
            if (cam != null) l.glow.rotation = Quaternion.LookRotation(l.glow.position - cam.transform.position);
        }
    }

    public static Texture2D Soft()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)), 2f));
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}

/// <summary>Fireflies blinking and drifting low over the fields</summary>
public class KingdomFireflies : MonoBehaviour
{
    private void Start()
    {
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 1f, 0.4f, 1f), new Color(1f, 0.85f, 0.35f, 1f));
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 22f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 62f;
        shape.donutRadius = 26f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.position = new Vector3(0f, KingdomScenery.GroundY + 3f, 0f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.5f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.3f;
        // Each one blinks on and off a couple of times in its life
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.1f, 0.35f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0.2f, 0.75f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = KingdomTorches.Soft() };
        ps.Play();
    }
}

/// <summary>
/// Every so often a friendly neon dragon glides across the sky beyond the castle, flapping
/// lazily, and puffs a little flame halfway over
/// </summary>
public class KingdomDragon : MonoBehaviour
{
    private Transform body, wingL, wingR, neck, fire;
    private readonly List<Transform> tail = new List<Transform>();
    private Vector3 from, to;
    private float started, next;
    private bool flying;
    private const float Duration = 16f;

    private void Start()
    {
        Material scales = OrbitScenery.Lit(new Color(0.1f, 0.35f, 0.3f), new Color(0.05f, 0.3f, 0.15f), 0.3f, 0.6f);
        Material belly = OrbitScenery.Lit(new Color(0.8f, 0.7f, 0.35f), new Color(0.2f, 0.15f, 0.04f), 0f, 0.4f);
        Material membrane = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.85f, 0.25f, 0.7f) };
        Material eye = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.95f, 0.4f) };
        Material flame = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.55f, 0.15f) };

        body = new GameObject("Dragon").transform;
        body.SetParent(transform, false);
        body.localScale = Vector3.one * 3f;
        OrbitScenery.Part(body, PrimitiveType.Sphere, Vector3.zero, new Vector3(1.3f, 1.1f, 3f), scales);
        OrbitScenery.Part(body, PrimitiveType.Sphere, new Vector3(0f, -0.25f, 0.1f), new Vector3(1f, 0.7f, 2.5f), belly);
        neck = new GameObject("Neck").transform;
        neck.SetParent(body, false);
        neck.localPosition = new Vector3(0f, 0.3f, 1.3f);
        OrbitScenery.Part(neck, PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0.6f), new Vector3(0.55f, 0.8f, 0.55f), scales, Quaternion.Euler(50f, 0f, 0f));
        var head = new GameObject("Head").transform;
        head.SetParent(neck, false);
        head.localPosition = new Vector3(0f, 1.1f, 1.3f);
        OrbitScenery.Part(head, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.8f, 0.7f, 1.1f), scales);
        OrbitScenery.Part(head, PrimitiveType.Cube, new Vector3(0f, -0.1f, 0.65f), new Vector3(0.55f, 0.4f, 0.7f), scales);
        OrbitScenery.Part(head, PrimitiveType.Sphere, new Vector3(0.3f, 0.2f, 0.25f), Vector3.one * 0.2f, eye);
        OrbitScenery.Part(head, PrimitiveType.Sphere, new Vector3(-0.3f, 0.2f, 0.25f), Vector3.one * 0.2f, eye);
        OrbitScenery.Part(head, PrimitiveType.Cube, new Vector3(0.22f, 0.45f, -0.3f), new Vector3(0.12f, 0.5f, 0.12f), belly, Quaternion.Euler(-35f, 0f, 0f));   // horns
        OrbitScenery.Part(head, PrimitiveType.Cube, new Vector3(-0.22f, 0.45f, -0.3f), new Vector3(0.12f, 0.5f, 0.12f), belly, Quaternion.Euler(-35f, 0f, 0f));
        fire = OrbitScenery.Part(head, PrimitiveType.Sphere, new Vector3(0f, -0.1f, 1.8f), new Vector3(0.6f, 0.6f, 2f), flame);
        fire.gameObject.SetActive(false);

        // Tail: a chain of shrinking balls that sways
        Transform link = body;
        for (int i = 0; i < 6; i++)
        {
            var seg = new GameObject("Tail").transform;
            seg.SetParent(link, false);
            seg.localPosition = new Vector3(0f, 0f, link == body ? -1.4f : -0.7f);
            float s = 0.75f - i * 0.1f;
            OrbitScenery.Part(seg, PrimitiveType.Sphere, Vector3.zero, new Vector3(s, s, s * 1.4f), scales);
            tail.Add(seg);
            link = seg;
        }
        OrbitScenery.Part(link, PrimitiveType.Cube, new Vector3(0f, 0f, -0.4f), new Vector3(0.5f, 0.05f, 0.5f), membrane, Quaternion.Euler(0f, 45f, 0f));   // spade tip

        Transform Wing(float side)
        {
            var pivot = new GameObject("Wing").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(side * 0.5f, 0.4f, 0.3f);
            OrbitScenery.Part(pivot, PrimitiveType.Cube, new Vector3(side * 1.8f, 0f, 0.4f), new Vector3(3.6f, 0.12f, 0.15f), scales);   // the arm
            OrbitScenery.Part(pivot, PrimitiveType.Cube, new Vector3(side * 1.7f, 0f, -0.5f), new Vector3(3.2f, 0.04f, 1.8f), membrane, Quaternion.Euler(0f, side * 12f, 0f));
            return pivot;
        }
        wingL = Wing(-1f);
        wingR = Wing(1f);
        body.gameObject.SetActive(false);
        next = AccessibilityPrefs.AmbientTime + 10f;
    }

    private void Update()
    {
        float t = AccessibilityPrefs.AmbientTime;
        if (!flying)
        {
            if (t < next) return;
            // Across the far side of the kingdom, high above the rooftops
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 across = new Vector3(-side.z, 0f, side.x);
            float h = Random.Range(8f, 22f);
            from = side * 120f - across * 160f + Vector3.up * h;
            to = side * 120f + across * 160f + Vector3.up * (h + Random.Range(-6f, 6f));
            started = t;
            flying = true;
            body.gameObject.SetActive(true);
        }
        float k = (t - started) / Duration;
        if (k >= 1f)
        {
            flying = false;
            body.gameObject.SetActive(false);
            next = t + Random.Range(25f, 45f);
            return;
        }
        Vector3 dir = (to - from).normalized;
        body.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(t * 1.4f) * 2f;
        body.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(Mathf.Sin(t * 1.4f + 1f) * 6f, 0f, Mathf.Sin(t * 0.7f) * 10f);
        float flap = Mathf.Sin(t * 3.2f) * 35f;
        wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
        wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
        neck.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.4f) * 8f, Mathf.Sin(t * 0.9f) * 10f, 0f);
        for (int i = 0; i < tail.Count; i++)
            tail[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 2f - i * 0.6f) * 12f, 0f);
        // A little puff of flame halfway across
        bool puff = k > 0.45f && k < 0.58f;
        fire.gameObject.SetActive(puff);
        if (puff) fire.localScale = new Vector3(0.6f, 0.6f, 2f) * (0.8f + 0.3f * Mathf.Sin(t * 30f));
    }
}
