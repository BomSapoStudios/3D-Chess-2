using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Ashen Wastes backdrop: a volcanic wasteland under a smoky red sky, far below the board.
//
//  - AshenSky: the 360° sky. Smoke-dark overhead, burning red-orange at the horizon, a dull
//    red sun behind the ash clouds, jagged black ranges rimmed in fire and a far fire mountain
//    with a smoke plume ahead of Black.
//  - AshenScenery: black basalt ground split by glowing cracks, a river of lava that rings the
//    board, a huge smoking volcano ahead of White (off to the left so the board doesn't hide
//    it) with lava running down its sides into the ring, and a spiked dark tower to the right.
//    Jagged rock spires with neon crystal clusters stand all round. Ash drifts down, embers
//    rise, the volcano spits glowing rocks now and then and lightning cracks in the plume and
//    onto the tower's tallest spike.
//
// An original scene: no eyes, no rings, nothing borrowed. Everything sits well away from the
// board so the board stays the focus.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural sky over the wastes. Pure C# so it can run on a worker thread.</summary>
public static class AshenSky
{
    // Matches the fog so the far rocks melt into the smoky haze
    public static readonly Color HazeColor = new Color(0.2f, 0.07f, 0.06f);

    private const float SunU = 0.27f, SunV = 0.3f, SunR = 0.028f;   // a dull red sun ahead of White, behind the smoke
    private const float PeakU = 0.77f;                                // the far fire mountain, ahead of Black

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        const float horizon = 0.5f;
        float off = (seed % 997) * 0.173f;

        // Two jagged ranges as heights above the horizon per column; the fire mountain rises
        // out of the far one
        var far = new float[width];
        var near = new float[width];
        const float peakTop = 0.105f, craterHalf = 0.011f;
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float a = u * Mathf.PI * 2f;
            far[x] = 0.024f + 0.014f * Mathf.Sin(a * 3f + 1.1f) + 0.008f * Mathf.Sin(a * 8f + 0.3f) + 0.016f * (1f - Mathf.Abs(Mathf.Sin(a * 6f + 2f)));
            near[x] = 0.01f + 0.005f * Mathf.Sin(a * 5f + 2f) + 0.007f * (1f - Mathf.Abs(Mathf.Sin(a * 13f + 0.7f))) + 0.003f * (1f - Mathf.Abs(Mathf.Sin(a * 37f)));
            float dp = Mathf.Abs(Wrap(u - PeakU));
            // Concave slopes up to a notched crater
            float cone = dp < craterHalf ? peakTop - (craterHalf - dp) * 0.5f : peakTop - 0.9f * Mathf.Pow(dp - craterHalf, 0.85f);
            far[x] = Mathf.Max(far[x], cone);
        }

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
                    // Smoke-black violet overhead, burning red then orange toward the horizon
                    c = Color.Lerp(new Color(0.05f, 0.025f, 0.05f), new Color(0.32f, 0.07f, 0.06f), Mathf.Pow(t, 2.2f));
                    c = Color.Lerp(c, new Color(0.85f, 0.3f, 0.08f), Mathf.Pow(t, 14f));

                    // The sun: a dull red disc with a faint halo
                    float su = Wrap(u - SunU) * 2f, sv = v - SunV;
                    float sd = Mathf.Sqrt(su * su + sv * sv);
                    c += new Color(0.6f, 0.15f, 0.05f) * (0.35f * Mathf.Exp(-sd * sd / (SunR * SunR * 10f)));
                    if (sd < SunR) c = Color.Lerp(c, new Color(0.9f, 0.32f, 0.14f), 0.75f * Mathf.Clamp01((SunR - sd) / 0.003f));

                    // Banks of ash cloud: dark smears, lit orange from below near the horizon
                    float n = Seam(u, v, 7f, 12f, off) * 0.6f + Seam(u, v, 19f, 31f, off + 40f) * 0.4f;
                    float cloud = Mathf.Clamp01((n - 0.48f) * 4f);
                    if (cloud > 0f)
                    {
                        float lit = Mathf.Clamp01((Seam(u, v + 0.012f, 7f, 12f, off) - Seam(u, v, 7f, 12f, off)) * 10f + 0.2f) * t * t;
                        Color ash = Color.Lerp(new Color(0.06f, 0.04f, 0.05f), new Color(0.6f, 0.18f, 0.06f), lit);
                        c = Color.Lerp(c, ash, cloud * 0.85f);
                    }

                    // The fire mountain's plume: a smoke column leaning off to one side, glowing at its foot
                    float craterV = horizon - peakTop;
                    float rise = craterV - v;
                    if (rise > -0.005f && rise < 0.42f)
                    {
                        float centre = PeakU + rise * 0.45f + 0.015f * Mathf.Sin(rise * 30f);
                        float half = 0.008f + rise * 0.22f;
                        float du = Mathf.Abs(Wrap(u - centre)) / half;
                        if (du < 1.4f)
                        {
                            float puff = Mathf.PerlinNoise(u * 60f + off, v * 40f) * 0.5f + Mathf.PerlinNoise(u * 16f, v * 12f + off) * 0.5f;
                            float dens = Mathf.Clamp01((1.1f - du) * 2f) * Mathf.Clamp01(puff * 1.6f - 0.15f) * Mathf.Clamp01(1f - rise * 2.2f);
                            Color smoke = Color.Lerp(new Color(1f, 0.45f, 0.12f), new Color(0.09f, 0.06f, 0.07f), Mathf.Clamp01(rise * 14f));
                            c = Color.Lerp(c, smoke, dens);
                        }
                    }
                    // Fire glow round the crater
                    {
                        float gu = Wrap(u - PeakU) * 2f, gv = v - craterV;
                        c += new Color(1f, 0.35f, 0.08f) * (0.6f * Mathf.Exp(-(gu * gu + gv * gv) / 0.0004f));
                    }

                    // Far ranges: charcoal with a fiery rim; the fire mountain has lava running down
                    float farTop = horizon - far[x];
                    if (v > farTop)
                    {
                        float depth = (v - farTop) * height;
                        c = Color.Lerp(new Color(0.16f, 0.06f, 0.07f), new Color(0.24f, 0.08f, 0.06f), Mathf.Clamp01((v - farTop) / 0.06f));
                        c = Color.Lerp(c, new Color(1f, 0.35f, 0.12f), Mathf.Clamp01(1f - depth / 2f) * 0.7f);
                        float dp = Wrap(u - PeakU);
                        if (Mathf.Abs(dp) < 0.06f)
                        {
                            float streak = Mathf.Abs(Mathf.Sin(dp * 230f + Mathf.PerlinNoise(dp * 40f + off, v * 25f) * 5f));
                            float fall = (v - farTop) / 0.06f;   // fades as it runs down
                            if (streak > 0.94f && fall < 1f) c = Color.Lerp(c, new Color(1f, 0.55f, 0.15f), (streak - 0.94f) / 0.06f * (1f - fall));
                        }
                    }
                    // Near range: near-black with a hot pink neon rim
                    float nearTop = horizon - near[x];
                    if (v > nearTop)
                    {
                        float rim = Mathf.Clamp01(1f - (v - nearTop) * height / 2f);
                        c = Color.Lerp(new Color(0.05f, 0.025f, 0.035f), new Color(1f, 0.25f, 0.55f), rim * 0.65f);
                    }
                }
                else
                {
                    // Below the horizon: smoky haze over black rock, with thin glowing cracks
                    float below = v - horizon;
                    c = Color.Lerp(HazeColor, new Color(0.05f, 0.025f, 0.03f), Mathf.Clamp01(below * 5f));
                    float crack = Mathf.Abs(Seam(u, v, 24f, 30f, off + 90f) - 0.5f);
                    if (crack < 0.012f) c = Color.Lerp(c, new Color(1f, 0.4f, 0.1f), (1f - crack / 0.012f) * 0.8f);
                }
                result[(height - 1 - y) * width + x] = new Color32(B(c.r), B(c.g), B(c.b), 255); // Unity textures start at the bottom row
            }
        }
        return result;
    }

    // Perlin noise that wraps round the sky with no seam where u goes from 1 back to 0
    private static float Seam(float u, float v, float su, float sv, float off) =>
        Mathf.PerlinNoise(u * su + off, v * sv) * (1f - u) + Mathf.PerlinNoise((u - 1f) * su + off, v * sv) * u;

    private static float Wrap(float d) { if (d > 0.5f) d -= 1f; if (d < -0.5f) d += 1f; return d; }
    private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}

public static class AshenScenery
{
    public const float GroundY = -30f;
    public const float RingRadius = 50f, RingHalfWidth = 3.5f;

    /// <summary>The lava ring wanders in and out as it goes round the board</summary>
    public static float RingRadiusAt(float angle) => RingRadius + 4f * Mathf.Sin(angle * 3f + 0.4f) + 2f * Mathf.Sin(angle * 7f + 1.9f);

    public static Vector3 RingPoint(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * RingRadiusAt(angle) + Vector3.up * GroundY;

    // The volcano looms ahead of White, a little to the left so the board doesn't hide it; the
    // dark tower stands off to the right.
    public static readonly Vector3 VolcanoDir = new Vector3(0.871f, 0f, 0.491f);
    public const float VolcanoDistance = 150f, VolcanoHeight = 62f, VolcanoBase = 58f, CraterRadius = 9f;
    public static readonly Vector3 TowerDir = new Vector3(0.8f, 0f, -0.6f);
    public const float TowerDistance = 120f;

    public static Vector3 VolcanoFoot => VolcanoDir * VolcanoDistance + Vector3.up * GroundY;
    public static Vector3 CraterTop => VolcanoFoot + Vector3.up * VolcanoHeight;

    /// <summary>Tip of the tower's tallest spike, where the lightning strikes</summary>
    public static Vector3 TowerTip { get; private set; }

    /// <summary>The volcano's radius at angle a round it, t from its foot (0) to the crater rim (1)</summary>
    public static float VolcanoRadius(float a, float t) =>
        (CraterRadius + (VolcanoBase - CraterRadius) * Mathf.Pow(1f - t, 1.7f))
        * (1f + 0.08f * Mathf.Sin(a * 5f + 1f) + 0.05f * Mathf.Sin(a * 11f + 3f) + 0.04f * Mathf.Sin(a * 23f + t * 6f));

    private static List<Object> made;   // meshes and textures built here, freed with the scenery

    public static void Build(Transform root)
    {
        made = root.gameObject.AddComponent<AshenCleanup>().Made;
        var rng = new System.Random(6660);

        Material basalt = new Material(Shader.Find("Standard")) { color = new Color(0.09f, 0.065f, 0.07f) };
        basalt.SetFloat("_Glossiness", 0.15f);
        OrbitScenery.Part(root, PrimitiveType.Cylinder, new Vector3(0f, GroundY, 0f), new Vector3(800f, 0.05f, 800f), basalt).name = "Ash Plain";

        Material rock = OrbitScenery.Lit(new Color(0.13f, 0.09f, 0.1f), new Color(0.03f, 0.008f, 0.01f), 0f, 0.3f);
        Material darkRock = OrbitScenery.Lit(new Color(0.07f, 0.05f, 0.06f), new Color(0.015f, 0.004f, 0.006f), 0.2f, 0.45f);
        Material tower = OrbitScenery.Lit(new Color(0.08f, 0.07f, 0.09f), Color.black, 0.5f, 0.55f);   // dark iron-black stone
        Material crust = OrbitScenery.Lit(new Color(0.12f, 0.05f, 0.04f), new Color(0.45f, 0.1f, 0.03f), 0f, 0.2f);   // cooling banks, still glowing
        Material ember = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.5f, 0.15f) };
        Material pink = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.3f, 0.7f) };
        Material cyan = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.35f, 1f, 0.95f) };
        Texture2D lavaTex = LavaTexture();
        made.Add(lavaTex);
        Material lava = new Material(Shader.Find("Unlit/Texture")) { mainTexture = lavaTex };
        Material lavaFall = new Material(Shader.Find("Unlit/Texture")) { mainTexture = lavaTex };

        var still = new GameObject("Wastes").transform;
        still.SetParent(root, false);

        // The lava ring round the board, on a wider bank of glowing crust
        var ring = new List<Vector3>();
        for (int i = 0; i <= 160; i++) ring.Add(RingPoint(i / 160f * Mathf.PI * 2f));
        Flat(still, ring, RingHalfWidth + 2.2f, 0.1f, crust, 12f);
        Flat(still, ring, RingHalfWidth, 0.25f, lava, 12f);

        // The volcano, with lava running down its sides
        float volcanoAngle = Mathf.Atan2(VolcanoDir.z, VolcanoDir.x);
        Volcano(still, rock, lava, lavaFall, rng);
        // The biggest flow faces the board and snakes on across the plain into the ring
        float faceBoard = Mathf.Atan2(-VolcanoDir.z, -VolcanoDir.x);
        Vector3 foot = VolcanoFoot + new Vector3(Mathf.Cos(faceBoard), 0f, Mathf.Sin(faceBoard)) * VolcanoRadius(faceBoard, 0f);
        Vector3 into = RingPoint(volcanoAngle);
        var channel = new List<Vector3>();
        Vector3 side = new Vector3(-VolcanoDir.z, 0f, VolcanoDir.x);
        for (int i = 0; i <= 24; i++)
        {
            float k = i / 24f;
            channel.Add(Vector3.Lerp(foot, into, k) + side * (6f * Mathf.Sin(k * Mathf.PI * 2f)));
        }
        Flat(still, channel, 4.5f, 0.11f, crust, 12f);
        Flat(still, channel, 2.6f, 0.26f, lava, 12f);

        // The dark tower off to the right
        Tower(still, TowerDir * TowerDistance + Vector3.up * GroundY, rock, tower, ember, pink, rng);

        // Glowing cracks in the ground
        for (int i = 0; i < 26; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float d = 40f + (float)rng.NextDouble() * 90f;
            Vector3 p = new Vector3(Mathf.Cos(a) * d, GroundY, Mathf.Sin(a) * d);
            float heading = (float)rng.NextDouble() * Mathf.PI * 2f;
            var crack = new List<Vector3>();
            for (int k = 0; k < 9; k++)
            {
                crack.Add(p);
                heading += ((float)rng.NextDouble() - 0.5f) * 1.2f;
                p += new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * (2f + (float)rng.NextDouble() * 2.5f);
            }
            if (Mathf.Abs((crack[0] - new Vector3(0f, GroundY, 0f)).magnitude - RingRadius) < 10f) continue;   // the ring is enough there
            Flat(still, crack, 0.25f + (float)rng.NextDouble() * 0.3f, 0.18f, lava, 6f);
        }

        // Jagged rock spires all round, some with clusters of neon crystals at their feet
        float towerAngle = Mathf.Atan2(TowerDir.z, TowerDir.x) * Mathf.Rad2Deg;
        for (int i = 0, placed = 0; i < 120 && placed < 36; i++)
        {
            float deg = (float)rng.NextDouble() * 360f;
            if (Near(deg, volcanoAngle * Mathf.Rad2Deg, 24f) || Near(deg, towerAngle, 12f)) continue;
            float a = deg * Mathf.Deg2Rad;
            float d = 40f + (float)rng.NextDouble() * 85f;
            if (Mathf.Abs(d - RingRadiusAt(a)) < 9f) d += 16f;
            Vector3 at = new Vector3(Mathf.Cos(a) * d, GroundY, Mathf.Sin(a) * d);
            float h = 6f + (float)rng.NextDouble() * 18f;
            float r = 1.8f + (float)rng.NextDouble() * 2.8f + h * 0.08f;
            var spire = Rock(still, at, r, 0.15f + (float)rng.NextDouble() * 0.5f, h, 6, 4, 0.2f, rng.Next(), rng.NextDouble() < 0.5 ? rock : darkRock);
            spire.localRotation = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 16f, (float)rng.NextDouble() * 360f, ((float)rng.NextDouble() - 0.5f) * 16f);
            if (rng.NextDouble() < 0.4)
                for (int s = 0, n = 3 + rng.Next(3); s < n; s++)
                {
                    float sa = (float)rng.NextDouble() * Mathf.PI * 2f;
                    Vector3 sp = at + new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * (r * 0.9f + (float)rng.NextDouble());
                    float sh = 1.5f + (float)rng.NextDouble() * 2.5f;
                    OrbitScenery.Part(still, PrimitiveType.Cube, sp + Vector3.up * sh * 0.4f, new Vector3(0.45f, sh, 0.45f), s % 2 == 0 ? pink : cyan,
                        Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 50f, (float)rng.NextDouble() * 90f, ((float)rng.NextDouble() - 0.5f) * 50f));
                }
            placed++;
        }
        // Loose boulders
        for (int i = 0; i < 30; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float d = 38f + (float)rng.NextDouble() * 80f;
            if (Mathf.Abs(d - RingRadiusAt(a)) < 7f) continue;
            float s = 1.5f + (float)rng.NextDouble() * 3f;
            OrbitScenery.Part(still, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * d, GroundY + s * 0.2f, Mathf.Sin(a) * d), new Vector3(s * 1.3f, s * 0.7f, s), darkRock,
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
        }

        StaticBatchingUtility.Combine(still.gameObject);

        root.gameObject.AddComponent<AshenLava>().Init(lava, lavaFall);
        root.gameObject.AddComponent<AshenVolcano>();
        root.gameObject.AddComponent<AshenAir>();
        root.gameObject.AddComponent<AshenLightning>();
    }

    private static bool Near(float deg, float centre, float half) => Mathf.Abs(Mathf.DeltaAngle(deg, centre)) < half;

    // A broad cone of rock rising to a crater, lava running down from the rim and a pool of
    // lava in the crater
    private static void Volcano(Transform parent, Material rock, Material lava, Material lavaFall, System.Random rng)
    {
        const int sides = 48, rings = 14;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int j = 0; j <= rings; j++)
        {
            float t = j / (float)rings;
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                float h = t * VolcanoHeight;
                if (j == rings) h -= 2.5f * (0.5f + 0.5f * Mathf.Sin(a * 7f));   // a broken crater rim
                verts.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * VolcanoRadius(a, t) + Vector3.up * h);
            }
        }
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < sides; i++)
            {
                int k = j * (sides + 1) + i, up = k + sides + 1;
                tris.Add(k); tris.Add(up); tris.Add(k + 1);
                tris.Add(k + 1); tris.Add(up); tris.Add(up + 1);
            }
        Mesh mesh = Keep(new Mesh { name = "Volcano" });
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        MeshPart(parent, mesh, VolcanoFoot, Quaternion.identity, rock);

        // The crater's lava pool, just under the rim
        OrbitScenery.Part(parent, PrimitiveType.Cylinder, CraterTop - Vector3.up * 2.5f, new Vector3(CraterRadius * 1.6f, 0.2f, CraterRadius * 1.6f), lava);

        // Lava flows down the slopes, the widest facing the board
        float faceBoard = Mathf.Atan2(-VolcanoDir.z, -VolcanoDir.x);
        var flows = new List<float> { faceBoard };
        for (int i = 0; i < 6; i++) flows.Add(faceBoard + (i + 1) * Mathf.PI * 2f / 7f + ((float)rng.NextDouble() - 0.5f) * 0.4f);
        for (int f = 0; f < flows.Count; f++)
        {
            float a0 = flows[f];
            float start = f == 0 ? 0.98f : 0.8f + (float)rng.NextDouble() * 0.18f;
            float stop = f == 0 ? 0f : (float)rng.NextDouble() * 0.35f;   // some cool and stop partway
            var pts = new List<Vector3>();
            var normals = new List<Vector3>();
            var widths = new List<float>();
            for (int k = 0; k <= 30; k++)
            {
                float t = Mathf.Lerp(start, stop, k / 30f);
                float a = a0 + 0.07f * Mathf.Sin(t * 9f + a0 * 3f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                pts.Add(VolcanoFoot + dir * (VolcanoRadius(a, t) + 0.6f) + Vector3.up * (t * VolcanoHeight));
                normals.Add((dir + Vector3.up * 0.8f).normalized);
                widths.Add(Mathf.Lerp(f == 0 ? 3f : 2f, 0.6f, t));
            }
            MeshPart(parent, Ribbon(pts, normals, widths, 10f), Vector3.zero, Quaternion.identity, lavaFall);
        }
    }

    // A tall spiked tower of black stone on a crag: tiers stepping in, neon bands at each ledge,
    // slit windows glowing ember-orange, buttress fins and a crown of spikes
    private static void Tower(Transform parent, Vector3 at, Material rock, Material stone, Material ember, Material pink, System.Random rng)
    {
        Rock(parent, at, 18f, 8f, 16f, 9, 4, 0.15f, 44, rock);
        const int tiers = 5;
        const float tierHeight = 11f, baseY = 14f;
        Quaternion turn = Quaternion.Euler(0f, 45f, 0f);
        for (int i = 0; i < tiers; i++)
        {
            float w = Mathf.Lerp(13f, 6.5f, i / (float)(tiers - 1));
            Vector3 mid = at + Vector3.up * (baseY + i * tierHeight + tierHeight * 0.5f);
            OrbitScenery.Part(parent, PrimitiveType.Cube, mid, new Vector3(w, tierHeight + 0.2f, w), stone, turn);
            OrbitScenery.Part(parent, PrimitiveType.Cube, mid + Vector3.up * tierHeight * 0.5f, new Vector3(w + 0.7f, 0.35f, w + 0.7f), pink, turn);   // neon ledge
            // Slit windows on each face
            for (int face = 0; face < 4; face++)
            {
                Vector3 normal = Quaternion.Euler(0f, 45f + face * 90f, 0f) * Vector3.forward;
                Vector3 across = Vector3.Cross(Vector3.up, normal);
                int slits = 1 + rng.Next(2);
                for (int s = 0; s < slits; s++)
                {
                    float x = slits == 1 ? 0f : (s - 0.5f) * w * 0.4f;
                    float y = ((float)rng.NextDouble() - 0.5f) * tierHeight * 0.4f;
                    OrbitScenery.Part(parent, PrimitiveType.Cube, mid + normal * (w * 0.5f + 0.05f) + across * x + Vector3.up * y, new Vector3(0.5f, 2.4f, 0.2f), ember, Quaternion.LookRotation(normal));
                }
            }
        }
        // Buttress fins up the corners, leaning in
        for (int c = 0; c < 4; c++)
        {
            Vector3 axis = Quaternion.Euler(0f, c * 90f, 0f) * Vector3.forward;
            OrbitScenery.Part(parent, PrimitiveType.Cube, at + axis * (13f * 0.707f + 0.8f) + Vector3.up * (baseY + 15f), new Vector3(1.2f, 32f, 4f), stone,
                Quaternion.LookRotation(axis) * Quaternion.Euler(-7f, 0f, 0f));
        }
        // A crown of spikes leaning outward round one tall middle spike
        Vector3 top = at + Vector3.up * (baseY + tiers * tierHeight);
        for (int s = 0; s < 6; s++)
        {
            Vector3 dir = Quaternion.Euler(0f, s * 60f + 15f, 0f) * Vector3.forward;
            Transform spike = Rock(parent, top + dir * 2.4f, 1.3f, 0.05f, 9f + (s % 2) * 4f, 5, 3, 0.1f, 70 + s, stone);
            spike.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(20f, 0f, 0f);
        }
        Rock(parent, top, 1.8f, 0.05f, 20f, 6, 4, 0.08f, 77, stone);
        TowerTip = top + Vector3.up * 20f;
    }

    // ───────── Mesh helpers ─────────

    /// <summary>
    /// A rough cone of rock from a base radius up to a top radius, the radius wobbling round each
    /// ring so it reads as jagged
    /// </summary>
    private static Transform Rock(Transform parent, Vector3 foot, float baseR, float topR, float height, int sides, int rings, float jag, int seed, Material mat)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float s = seed % 100;
        for (int j = 0; j <= rings; j++)
        {
            float t = j / (float)rings;
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                float r = Mathf.Lerp(topR, baseR, Mathf.Pow(1f - t, 1.3f)) * (1f + jag * (0.6f * Mathf.Sin(a * 3f + s + t * 4f) + 0.4f * Mathf.Sin(a * 7f + s * 2f)));
                verts.Add(new Vector3(Mathf.Cos(a) * r, t * height, Mathf.Sin(a) * r));
            }
        }
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < sides; i++)
            {
                int k = j * sides + i, k1 = j * sides + (i + 1) % sides;
                tris.Add(k); tris.Add(k + sides); tris.Add(k1);
                tris.Add(k1); tris.Add(k + sides); tris.Add(k1 + sides);
            }
        // Cap the top so a blunt spire isn't hollow
        int tip = verts.Count;
        verts.Add(new Vector3(0f, height + topR * 0.5f, 0f));
        for (int i = 0; i < sides; i++) { tris.Add(rings * sides + i); tris.Add(tip); tris.Add(rings * sides + (i + 1) % sides); }
        Mesh mesh = Keep(new Mesh { name = "Ashen Rock" });
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return MeshPart(parent, mesh, foot, Quaternion.identity, mat);
    }

    // A flat ribbon lying on the ground along a path, raised `lift` above it
    private static void Flat(Transform parent, List<Vector3> path, float halfWidth, float lift, Material mat, float tile)
    {
        var pts = new List<Vector3>();
        var normals = new List<Vector3>();
        var widths = new List<float>();
        foreach (Vector3 p in path) { pts.Add(p + Vector3.up * lift); normals.Add(Vector3.up); widths.Add(halfWidth); }
        MeshPart(parent, Ribbon(pts, normals, widths, tile), Vector3.zero, Quaternion.identity, mat);
    }

    /// <summary>
    /// A strip along a path of points: u across, v along (one texture tile every `tile` units,
    /// stretched a touch so a closed loop meets itself without a seam). Both sides face out.
    /// </summary>
    private static Mesh Ribbon(List<Vector3> pts, List<Vector3> normals, List<float> halfWidths, float tile)
    {
        int n = pts.Count;
        bool closed = (pts[0] - pts[n - 1]).sqrMagnitude < 0.01f;
        float length = 0f;
        for (int i = 1; i < n; i++) length += (pts[i] - pts[i - 1]).magnitude;
        float per = closed ? length / Mathf.Max(1, Mathf.Round(length / tile)) : tile;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var norms = new List<Vector3>();
        var tris = new List<int>();
        float along = 0f;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) along += (pts[i] - pts[i - 1]).magnitude;
            Vector3 prev = i > 0 ? pts[i - 1] : closed ? pts[n - 2] : pts[i];
            Vector3 next = i < n - 1 ? pts[i + 1] : closed ? pts[1] : pts[i];
            Vector3 side = Vector3.Cross(normals[i], next - prev).normalized;
            verts.Add(pts[i] - side * halfWidths[i]);
            verts.Add(pts[i] + side * halfWidths[i]);
            uvs.Add(new Vector2(0f, along / per));
            uvs.Add(new Vector2(1f, along / per));
            norms.Add(normals[i]);
            norms.Add(normals[i]);
        }
        for (int i = 0; i < n - 1; i++)
        {
            int k = i * 2;
            tris.Add(k); tris.Add(k + 2); tris.Add(k + 1);
            tris.Add(k + 1); tris.Add(k + 2); tris.Add(k + 3);
            // and the other way round, so it shows from either side
            tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
            tris.Add(k + 1); tris.Add(k + 3); tris.Add(k + 2);
        }
        Mesh mesh = Keep(new Mesh { name = "Ashen Ribbon" });
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Transform MeshPart(Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, Material mat)
    {
        var go = new GameObject(mesh.name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    private static T Keep<T>(T thing) where T : Object { made.Add(thing); return thing; }

    // Molten rock: orange with hot yellow veins between drifting plates of dark crust. Built
    // from whole-number waves so it tiles seamlessly as it scrolls.
    private static Texture2D LavaTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Ashen Lava" };
        var px = new Color32[size * size];
        const float tau = Mathf.PI * 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            // Warp, then layer waves into a cell-ish pattern of plates
            float wu = u + 0.06f * Mathf.Sin(tau * (2f * v + 0.3f)), wv = v + 0.06f * Mathf.Sin(tau * (3f * u + 0.7f));
            float n = Mathf.Sin(tau * (3f * wu + 1f * wv)) * 0.4f + Mathf.Sin(tau * (-2f * wu + 4f * wv + 0.2f)) * 0.35f
                    + Mathf.Sin(tau * (5f * wu - 3f * wv + 0.6f)) * 0.25f;
            Color c;
            if (n > 0.25f) c = Color.Lerp(new Color(0.55f, 0.12f, 0.04f), new Color(0.16f, 0.04f, 0.03f), Mathf.Clamp01((n - 0.25f) * 4f));   // crust
            else if (n > 0.1f) c = Color.Lerp(new Color(1f, 0.45f, 0.1f), new Color(0.55f, 0.12f, 0.04f), (n - 0.1f) / 0.15f);
            else c = Color.Lerp(new Color(1f, 0.92f, 0.45f), new Color(1f, 0.45f, 0.1f), Mathf.Clamp01((n + 0.3f) / 0.4f));       // white-hot veins
            px[y * size + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>A soft round glow, for sprites and particles</summary>
    public static Texture2D Soft()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Ashen Glow" };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)), 2f));
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        made?.Add(tex);
        return tex;
    }
}

/// <summary>Frees the meshes and textures the wastes built when the scenery goes away</summary>
public class AshenCleanup : MonoBehaviour
{
    public readonly List<Object> Made = new List<Object>();

    private void OnDestroy()
    {
        foreach (Object o in Made) if (o != null) Destroy(o);
    }
}

/// <summary>The lava creeps along: the ring and channels slowly, the falls down the slopes faster</summary>
public class AshenLava : MonoBehaviour
{
    private Material ground, falls;

    public void Init(Material ring, Material slopes) { ground = ring; falls = slopes; }

    private void LateUpdate()
    {
        float t = AccessibilityPrefs.AmbientTime;
        if (ground != null) ground.mainTextureOffset = new Vector2(0f, -t * 0.05f);
        if (falls != null) falls.mainTextureOffset = new Vector2(0f, t * 0.12f);   // ribbons run from the rim down
    }
}

/// <summary>
/// The volcano: a smoke plume rolling up from the crater, lit orange from underneath, a
/// glow over the crater that throbs, and every so often a spray of glowing rocks
/// </summary>
public class AshenVolcano : MonoBehaviour
{
    private Transform glow;
    private Material glowMat;

    private void Start()
    {
        Texture2D soft = AshenScenery.Soft();
        Vector3 crater = AshenScenery.CraterTop;

        // Smoke: big soft puffs that rise, swell and drift off with the wind
        var smoke = new GameObject("Plume").AddComponent<ParticleSystem>();
        smoke.transform.SetParent(transform, false);
        smoke.transform.position = crater;
        smoke.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // emit upward
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = smoke.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = smoke.emission;
        emission.rateOverTime = 5f;
        var shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = AshenScenery.CraterRadius * 0.6f;
        var vel = smoke.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.z = new ParticleSystem.MinMaxCurve(-3f, -1.5f);   // the wind carries it off over the tower
        var grow = smoke.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3f));
        var fade = smoke.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.45f, 0.15f), 0f), new GradientColorKey(new Color(0.3f, 0.12f, 0.1f), 0.15f), new GradientColorKey(new Color(0.12f, 0.09f, 0.1f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.08f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var sr = smoke.GetComponent<ParticleSystemRenderer>();
        sr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = soft };
        sr.sortingFudge = 10f;
        smoke.Play();

        // Glowing rocks flung up in a burst every so often, trailing sparks as they fall back
        var bombs = new GameObject("Eruption").AddComponent<ParticleSystem>();
        bombs.transform.SetParent(transform, false);
        bombs.transform.position = crater;
        bombs.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        bombs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var bm = bombs.main;
        bm.loop = true;
        bm.duration = 11f;
        bm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
        bm.startSpeed = new ParticleSystem.MinMaxCurve(18f, 28f);
        bm.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        bm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.4f, 0.1f));
        bm.gravityModifier = 1f;
        bm.maxParticles = 80;
        bm.simulationSpace = ParticleSystemSimulationSpace.World;
        var be = bombs.emission;
        be.rateOverTime = 0f;
        be.SetBursts(new[] { new ParticleSystem.Burst(0.5f, 25, 40) });
        var bs = bombs.shape;
        bs.shapeType = ParticleSystemShapeType.Cone;
        bs.angle = 28f;
        bs.radius = 2f;
        var trails = bombs.trails;
        trails.enabled = true;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.35f);
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 0f));
        trails.inheritParticleColor = true;
        var br = bombs.GetComponent<ParticleSystemRenderer>();
        var spark = new Material(Shader.Find("Sprites/Default")) { mainTexture = soft };
        br.sharedMaterial = spark;
        br.trailMaterial = new Material(Shader.Find("Sprites/Default"));
        bombs.Play();

        // The glow over the crater
        glow = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        Destroy(glow.GetComponent<Collider>());
        glow.name = "Crater Glow";
        glow.SetParent(transform, false);
        glow.position = crater + Vector3.up * 3f;
        glowMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = soft, color = new Color(1f, 0.45f, 0.12f, 0.7f) };
        var gr = glow.GetComponent<MeshRenderer>();
        gr.sharedMaterial = glowMat;
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void LateUpdate()
    {
        if (glow == null) return;
        float t = AccessibilityPrefs.AmbientTime;
        float throb = 0.8f + 0.12f * Mathf.Sin(t * 1.3f) + 0.08f * Mathf.Sin(t * 3.7f);
        glow.localScale = Vector3.one * 40f * throb;
        glowMat.color = new Color(1f, 0.45f, 0.12f, 0.55f * throb);
        Camera cam = Camera.main;
        if (cam != null) glow.rotation = Quaternion.LookRotation(glow.position - cam.transform.position);
    }
}

/// <summary>Flakes of grey ash drifting down, and embers rising off the lava</summary>
public class AshenAir : MonoBehaviour
{
    private void Start()
    {
        Texture2D soft = AshenScenery.Soft();
        Drift("Ash", soft, 22f, 180, new Color(0.55f, 0.5f, 0.5f, 0.7f), new Color(0.35f, 0.3f, 0.32f, 0.7f), 0.3f, 0.7f,
            AshenScenery.GroundY + 45f, new ParticleSystem.MinMaxCurve(-1.6f, -0.8f));
        Drift("Embers", soft, 16f, 140, new Color(1f, 0.6f, 0.2f, 1f), new Color(1f, 0.3f, 0.35f, 1f), 0.25f, 0.55f,
            AshenScenery.GroundY + 1f, new ParticleSystem.MinMaxCurve(1.2f, 2.6f));
    }

    private void Drift(string name, Texture2D tex, float rate, int max, Color a, Color b, float minSize, float maxSize, float y, ParticleSystem.MinMaxCurve rise)
    {
        var ps = new GameObject(name).AddComponent<ParticleSystem>();
        ps.transform.SetParent(transform, false);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 62f;
        shape.donutRadius = 26f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.position = new Vector3(0f, y, 0f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.3f, 1f);
        vel.y = rise;
        vel.z = new ParticleSystem.MinMaxCurve(-1.2f, -0.4f);   // the same wind that bends the plume
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.2f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        ps.Play();
    }
}

/// <summary>
/// Lightning now and then: in the volcano's plume, or down onto the tower's tallest spike.
/// Off while Reduce Motion is on, since it flashes.
/// </summary>
public class AshenLightning : MonoBehaviour
{
    private LineRenderer bolt;
    private Transform flash;
    private Material flashMat;
    private readonly System.Random rng = new System.Random(1313);
    private float next = 4f, until = -1f;
    private Vector3 from, to;

    private void Start()
    {
        bolt = new GameObject("Lightning").AddComponent<LineRenderer>();
        bolt.transform.SetParent(transform, false);
        bolt.useWorldSpace = true;
        bolt.positionCount = 14;
        bolt.widthCurve = AnimationCurve.Linear(0f, 0.9f, 1f, 0.35f);
        bolt.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        bolt.startColor = bolt.endColor = new Color(0.9f, 0.8f, 1f);
        bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bolt.enabled = false;

        flash = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        Destroy(flash.GetComponent<Collider>());
        flash.name = "Lightning Glow";
        flash.SetParent(transform, false);
        flashMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = AshenScenery.Soft(), color = new Color(0.8f, 0.6f, 1f, 0.6f) };
        var r = flash.GetComponent<MeshRenderer>();
        r.sharedMaterial = flashMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flash.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (bolt == null) return;
        float t = Time.time;
        if (AccessibilityPrefs.ReduceMotion) { bolt.enabled = false; flash.gameObject.SetActive(false); next = t + 2f; return; }

        if (t >= next)
        {
            // Strike: two in three in the plume, the rest onto the tower
            if (rng.NextDouble() < 0.66)
            {
                Vector3 c = AshenScenery.CraterTop + new Vector3(4f, 0f, -6f);
                from = c + new Vector3(Rand(10f), 34f + Rand(8f), Rand(10f));
                to = c + new Vector3(Rand(8f), 8f + Rand(4f), Rand(8f));
            }
            else
            {
                to = AshenScenery.TowerTip;
                from = to + new Vector3(Rand(14f), 55f, Rand(14f));
            }
            until = t + 0.35f;
            next = t + 5f + (float)rng.NextDouble() * 7f;
            Jag();
        }

        bool on = t < until && (until - t > 0.24f || until - t < 0.12f);   // a quick double flicker
        if (on && rng.NextDouble() < 0.3) Jag();
        bolt.enabled = on;
        flash.gameObject.SetActive(on);
        if (on)
        {
            flash.position = to;
            flash.localScale = Vector3.one * 16f;
            Camera cam = Camera.main;
            if (cam != null) flash.rotation = Quaternion.LookRotation(flash.position - cam.transform.position);
        }
    }

    // A fresh crooked path from the cloud down to the strike point
    private void Jag()
    {
        int n = bolt.positionCount;
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)(n - 1);
            float wiggle = (i == 0 || i == n - 1) ? 0f : 2.2f;
            bolt.SetPosition(i, Vector3.Lerp(from, to, k) + new Vector3(Rand(wiggle), Rand(wiggle * 0.5f), Rand(wiggle)));
        }
    }

    private float Rand(float half) => ((float)rng.NextDouble() * 2f - 1f) * half;
}
