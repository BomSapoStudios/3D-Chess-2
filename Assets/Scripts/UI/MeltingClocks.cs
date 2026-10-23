using System;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Melting Clocks backdrop: a surreal golden dusk over a still, empty plain.
//
//  - DreamSky: the 360° sky. Indigo overhead fading through teal to a glowing gold
//    horizon, a low pale sun, long thin pink clouds, a rocky headland over a glassy
//    sea, and a faint clock-faced moon dripping in the sky.
//  - DreamScenery: a smooth sand plain far below the board, a bare tree with a soft
//    clock draped over its branch, a stone plinth with a clock sliding off its edge, a
//    giant toppled pawn with a clock slumped over it, and clocks drifting around the
//    board, slowly melting and un-melting while their hands spin at odd speeds.
//
// Everything sits well away from the board so the board stays the focus.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural surreal dusk sky. Pure C# so it can run on a worker thread.</summary>
public static class DreamSky
{
    // Matches the fog so the far plain melts into the painted horizon
    public static readonly Color HazeColor = new Color(0.86f, 0.62f, 0.5f);

    public const float SunU = 0.25f;   // ahead of the default White view (+x)
    private const float SunV = 0.47f;  // just above the horizon

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        var rng = new System.Random(seed);
        const float horizon = 0.5f; // v of the horizon (v = 0 straight up)

        // Skyline: low flat-topped mesas all round, and a tall rocky headland ending in a cliff
        var land = new float[width];
        var headland = new bool[width];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float mesa = 0.006f + 0.014f * Mathf.Clamp01((Ridge(u * 14f, 14, seed) - 0.35f) * 2.2f);
            // Headland: rises gently from the left, drops as a cliff on the right
            float d = WrapDelta(u - 0.37f);
            float rise = d < 0f ? Mathf.Clamp01(1f + d / 0.16f) : Mathf.Clamp01(1f - d / 0.012f);
            float rock = rise * rise * (0.05f + 0.045f * Ridge(u * 40f, 40, seed + 3));
            land[x] = Mathf.Max(mesa, rock);
            headland[x] = rock > mesa;
        }

        var stars = new System.Collections.Generic.HashSet<int>();
        for (int k = 0; k < 900; k++) stars.Add(rng.Next((int)(height * 0.25f)) * width + rng.Next(width));

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color c;

                if (v < horizon)
                {
                    float t = v / horizon; // 0 straight up, 1 at the horizon
                    // Indigo overhead, teal in the middle, luminous gold at the horizon
                    c = t < 0.62f
                        ? Color.Lerp(new Color(0.05f, 0.03f, 0.2f), new Color(0.1f, 0.42f, 0.56f), Mathf.Pow(t / 0.62f, 1.3f))
                        : Color.Lerp(new Color(0.1f, 0.42f, 0.56f), new Color(1f, 0.8f, 0.46f), Mathf.Pow((t - 0.62f) / 0.38f, 0.8f));
                    if (stars.Contains(y * width + x)) c += Color.white * (0.4f * (1f - t * 1.6f));

                    // Pale low sun with a wide warm glow
                    float du = WrapDelta(u - SunU) * 2f, dv = v - SunV;
                    float dist = Mathf.Sqrt(du * du + dv * dv);
                    c += new Color(1f, 0.7f, 0.5f) * (0.5f * Mathf.Exp(-dist * dist / 0.004f));
                    c = Color.Lerp(c, new Color(1f, 0.97f, 0.85f), Mathf.Clamp01((0.026f - dist) / 0.002f));

                    // Long, thin pink clouds stretched along the horizon
                    for (int b = 0; b < 4; b++)
                    {
                        float bandV = 0.33f + b * 0.032f + 0.006f * Mathf.Sin(u * Mathf.PI * 2f * (2 + b) + b);
                        float streak = Mathf.Exp(-Mathf.Pow((v - bandV) / (0.0035f + 0.001f * b), 2f));
                        float patch = Mathf.Clamp01((Noise1(u * 9f, 9, seed + 20 + b) - 0.45f) * 3f);
                        c = Color.Lerp(c, new Color(1f, 0.5f, 0.7f), streak * patch * 0.6f);
                    }

                    PaintClockMoon(ref c, u, v);

                    // Skyline: headland in deep plum with a gold sunlit rim, mesas in lavender haze
                    float top = horizon - land[x];
                    if (v > top)
                    {
                        float rim = Mathf.Clamp01(1f - (v - top) * height / 2.5f);
                        Color body = headland[x]
                            ? Color.Lerp(new Color(0.36f, 0.14f, 0.32f), new Color(0.22f, 0.07f, 0.24f), (v - top) / land[x])
                            : Color.Lerp(new Color(0.78f, 0.5f, 0.62f), HazeColor, 0.4f);
                        c = Color.Lerp(body, headland[x] ? new Color(1f, 0.8f, 0.35f) : new Color(1f, 0.55f, 0.75f), rim * 0.85f);
                    }
                }
                else
                {
                    float below = v - horizon;
                    if (below < 0.014f)
                    {
                        // Glassy sea along the horizon: the gold sky reflected, with fine ripple lines
                        float shimmer = 0.5f + 0.5f * Mathf.Sin((0.02f / Mathf.Max(below, 0.0004f)) * 6f + Mathf.Sin(u * 80f));
                        c = Color.Lerp(new Color(1f, 0.78f, 0.5f), new Color(0.35f, 0.55f, 0.65f), below / 0.014f);
                        c += new Color(0.25f, 0.2f, 0.12f) * shimmer * (1f - below / 0.014f) * 0.5f;
                    }
                    else
                    {
                        // The plain beyond the 3D ground: warm sand fading into haze at the horizon
                        c = Color.Lerp(HazeColor, new Color(0.62f, 0.36f, 0.3f), Mathf.Clamp01((below - 0.014f) * 6f));
                        c = Color.Lerp(c, new Color(0.26f, 0.12f, 0.16f), Mathf.Clamp01((below - 0.2f) * 2f));
                    }
                }

                result[(height - 1 - y) * width + x] = new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), 255); // Unity textures start at the bottom row
            }
        }
        return result;
    }

    // A faint clock face hanging in the sky like a moon, its lower edge running in drips
    private static void PaintClockMoon(ref Color c, float u, float v)
    {
        const float cu = 0.335f, cv = 0.235f, r = 0.042f;
        float du = WrapDelta(u - cu) * 2f, dv = v - cv;
        if (Mathf.Abs(du) > r * 1.2f || dv < -r * 1.2f || dv > r * 3f) return;
        float d = Mathf.Sqrt(du * du + dv * dv);

        // Drips: below the middle, the edge sags and runs down in a few long drops
        float drip = 0f;
        if (dv > 0f && Mathf.Abs(du) < r)
        {
            float across = du / r;
            drip = r * (0.35f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(across * 7.5f + 0.6f)), 6f) + 0.9f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(across * 3.1f - 0.4f)), 30f));
            drip *= Mathf.Sqrt(Mathf.Max(0f, 1f - across * across));
        }
        bool inside = d < r || (dv > 0f && Mathf.Abs(du) < r * 0.98f && dv < Mathf.Sqrt(Mathf.Max(0f, r * r - du * du)) + drip);
        if (!inside) return;

        Color face = new Color(0.98f, 0.92f, 1f);
        float a = 0.5f;
        if (d < r)
        {
            // Hour marks, a lavender bezel and two hands
            float ang = Mathf.Atan2(du, -dv);
            float tick = Mathf.Abs(Mathf.Repeat(ang / (Mathf.PI / 6f) + 0.5f, 1f) - 0.5f) * (Mathf.PI / 6f) * d;
            if (d > r * 0.72f && d < r * 0.86f && tick < r * 0.035f) face = new Color(0.25f, 0.15f, 0.45f);
            if (d > r * 0.9f) face = new Color(0.75f, 0.45f, 1f);
            if (Hand(du, dv, -0.6f, r * 0.5f, r * 0.04f) || Hand(du, dv, 2.3f, r * 0.75f, r * 0.03f)) face = new Color(0.2f, 0.12f, 0.4f);
        }
        c = Color.Lerp(c, face, a);
    }

    private static bool Hand(float du, float dv, float angle, float length, float width)
    {
        // Angle from 12 o'clock, clockwise; screen up is -v
        float hx = Mathf.Sin(angle), hy = -Mathf.Cos(angle);
        float along = du * hx + dv * hy;
        float across = Mathf.Abs(-du * hy + dv * hx);
        return along > 0f && along < length && across < width;
    }

    private static float WrapDelta(float d)
    {
        if (d > 0.5f) d -= 1f;
        if (d < -0.5f) d += 1f;
        return d;
    }

    // Ridged noise in 0..1; repeats every `period` so the sky has no seam
    private static float Ridge(float x, int period, int seed)
    {
        float sum = 0f, amp = 0.55f, norm = 0f;
        int freq = 1;
        for (int o = 0; o < 4; o++)
        {
            float n = Noise1(x * freq, period * freq, seed + o * 31);
            sum += amp * (1f - Math.Abs(n * 2f - 1f));
            norm += amp;
            amp *= 0.5f;
            freq *= 2;
        }
        return sum / norm;
    }

    private static float Noise1(float x, int period, int seed)
    {
        int xi = (int)Math.Floor(x);
        float f = x - xi;
        float t = f * f * (3 - 2 * f);
        int a = ((xi % period) + period) % period, b = (((xi + 1) % period) + period) % period;
        return Hash(a, seed) * (1 - t) + Hash(b, seed) * t;
    }

    private static float Hash(int x, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + seed * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
}

/// <summary>The 3D part of the Melting Clocks backdrop</summary>
public static class DreamScenery
{
    public const float GroundY = -26f;

    private static Material stone, bark, pawnMat, handMat, backMat;
    private static Material[] faces, facesNoHands;

    public static void Build(Transform root)
    {
        var rng = new System.Random(1931);
        stone = Lit(new Color(0.72f, 0.5f, 0.46f), new Color(0.12f, 0.04f, 0.1f), 0f, 0.25f, null);
        bark = Lit(new Color(0.2f, 0.12f, 0.14f), new Color(0.05f, 0.01f, 0.06f), 0.1f, 0.3f, null);
        pawnMat = Lit(new Color(0.95f, 0.9f, 1f), new Color(0.5f, 0.2f, 0.6f) * 0.35f, 0.3f, 0.8f, null);
        handMat = Lit(new Color(0.08f, 0.05f, 0.2f), new Color(0.05f, 0.02f, 0.15f), 0.4f, 0.6f, null);
        backMat = Lit(new Color(1f, 0.75f, 0.3f), new Color(0.35f, 0.2f, 0.05f), 0.85f, 0.75f, null);
        Color[] bezels = { new Color(1f, 0.3f, 0.78f), new Color(0.15f, 1f, 0.9f), new Color(0.6f, 1f, 0.2f), new Color(0.7f, 0.45f, 1f) };
        faces = new Material[bezels.Length];
        facesNoHands = new Material[bezels.Length];
        for (int i = 0; i < bezels.Length; i++)
        {
            faces[i] = FaceMaterial(bezels[i], true, i);
            facesNoHands[i] = FaceMaterial(bezels[i], false, i);
        }

        // A smooth sand plain far below the board
        var groundMat = Lit(Color.white, Color.black, 0f, 0.15f, GroundTexture());
        groundMat.mainTextureScale = new Vector2(36f, 36f);
        var ground = JungleScenery.Part(root, PrimitiveType.Cylinder, new Vector3(0f, GroundY, 0f), new Vector3(900f, 0.05f, 900f), groundMat);
        ground.name = "Plain";

        BuildTree(root, Dir(28f) * 78f, 34f, 1.15f, 6.5f, 0);
        BuildTree(root, Dir(152f) * 96f, 26f, 0.9f, 5f, 1);
        BuildPlinth(root, Dir(-34f) * 70f, 2);
        BuildFallenPawn(root, Dir(212f) * 72f, 3);
        BuildPlinth(root, Dir(105f) * 110f, 1);

        // Clocks drifting round the board, well outside the camera's reach
        var floaters = new GameObject("Floating Clocks").transform;
        floaters.SetParent(root, false);
        int count = 7;
        for (int i = 0; i < count; i++)
        {
            float angle = (i + (float)rng.NextDouble() * 0.5f) / count * 360f;
            float radius = 62f + (float)rng.NextDouble() * 30f;
            float h = -6f + (float)rng.NextDouble() * 26f;
            float size = 3.2f + (float)rng.NextDouble() * 2.6f;
            var clock = MakeClock(floaters, "Floating Clock", size, facesNoHands[i % facesNoHands.Length]);
            clock.gameObject.AddComponent<DreamFloatingClock>().Init(angle, radius, h, size, rng.Next(), handMat);
        }

        root.gameObject.AddComponent<DesertDust>();
    }

    private static Vector3 Dir(float degrees) => new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad));

    // Face the scenery piece's local +Z toward the board
    private static Quaternion FacingBoard(Vector3 at) => Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z).normalized);

    // ───────── Pieces of the scene ─────────

    // A bare, crooked tree with one long level branch; a soft clock hangs over the branch
    private static void BuildTree(Transform root, Vector3 at, float height, float scale, float clockSize, int face)
    {
        var tree = new GameObject("Bare Tree").transform;
        tree.SetParent(root, false);
        tree.localPosition = new Vector3(at.x, GroundY, at.z);
        // The branch runs across the view from the board, so the clock hangs toward us
        tree.localRotation = FacingBoard(at) * Quaternion.Euler(0f, 90f, 0f);
        tree.localScale = Vector3.one * scale;

        float h = height;
        Part(tree, PrimitiveType.Capsule, new Vector3(0f, h * 0.25f, 0f), new Vector3(2.4f, h * 0.27f, 2.4f), bark, Quaternion.Euler(0f, 0f, 4f));
        Part(tree, PrimitiveType.Capsule, new Vector3(-0.8f, h * 0.62f, 0f), new Vector3(1.6f, h * 0.2f, 1.6f), bark, Quaternion.Euler(0f, 0f, -9f));
        // Stubby broken limbs
        Part(tree, PrimitiveType.Capsule, new Vector3(1.6f, h * 0.55f, 0.6f), new Vector3(0.7f, 3.2f, 0.7f), bark, Quaternion.Euler(20f, 0f, -45f));
        Part(tree, PrimitiveType.Capsule, new Vector3(-1.6f, h * 0.86f, -0.4f), new Vector3(0.6f, 2.6f, 0.6f), bark, Quaternion.Euler(-15f, 0f, 35f));
        // The long level branch along +Z, thinning toward its tip
        float by = h * 0.74f, len = 15f, r = 0.55f;
        Part(tree, PrimitiveType.Capsule, new Vector3(-0.6f, by, len * 0.5f), new Vector3(r * 2.2f, len * 0.5f, r * 2.2f), bark, Quaternion.Euler(90f, 0f, 0f));
        Part(tree, PrimitiveType.Capsule, new Vector3(-0.6f, by + 0.8f, len * 0.95f), new Vector3(0.5f, 2.2f, 0.5f), bark, Quaternion.Euler(60f, 0f, 0f));

        var clock = MakeClock(tree, "Clock on Branch", clockSize, faces[face]);
        clock.localPosition = new Vector3(-0.6f, by + r * 1.1f, len * 0.62f);
        clock.localRotation = Quaternion.Euler(0f, 8f, 0f);
        Drape(clock, clockSize, -0.06f, 0.06f, r * 1.1f, 0.9f, 7);
    }

    // A stone block; one clock slides off its front edge
    private static void BuildPlinth(Transform root, Vector3 at, int face)
    {
        var plinth = new GameObject("Plinth").transform;
        plinth.SetParent(root, false);
        plinth.localPosition = new Vector3(at.x, GroundY, at.z);
        plinth.localRotation = FacingBoard(at) * Quaternion.Euler(0f, 12f, 0f);
        float w = 18f, hgt = 7.5f, depth = 11f;
        Part(plinth, PrimitiveType.Cube, new Vector3(0f, hgt * 0.5f, 0f), new Vector3(w, hgt, depth), stone, null);
        Part(plinth, PrimitiveType.Cube, new Vector3(-2f, hgt + 0.6f, -2.5f), new Vector3(6f, 1.2f, 4f), stone, Quaternion.Euler(0f, 20f, 0f));

        float size = 5.4f;
        var clock = MakeClock(plinth, "Clock on Plinth", size, faces[face]);
        // The fold lies on the block's front edge (local +Z)
        float edge = 0.28f;
        clock.localPosition = new Vector3(2.5f, hgt + 0.03f, depth * 0.5f - edge * size);
        clock.localRotation = Quaternion.Euler(0f, -90f, 0f); // clock +x (its hanging side) toward the board
        Drape(clock, size, float.NegativeInfinity, edge, 0.25f, 1.1f, 11);
    }

    // A giant pawn toppled on its side, a clock slumped over its body
    private static void BuildFallenPawn(Transform root, Vector3 at, int face)
    {
        var pawn = new GameObject("Fallen Pawn").transform;
        pawn.SetParent(root, false);
        pawn.localPosition = new Vector3(at.x, GroundY, at.z);
        pawn.localRotation = FacingBoard(at) * Quaternion.Euler(0f, 70f, 0f);
        // Lying along local +Z: base, body, collar, head
        float bodyR = 4.2f;
        Part(pawn, PrimitiveType.Cylinder, new Vector3(0f, 6.2f, -9f), new Vector3(12.4f, 1.2f, 12.4f), pawnMat, Quaternion.Euler(90f, 0f, 0f));
        Part(pawn, PrimitiveType.Capsule, new Vector3(0f, bodyR, -1.5f), new Vector3(bodyR * 2f, 7f, bodyR * 2f), pawnMat, Quaternion.Euler(90f, 0f, 0f));
        Part(pawn, PrimitiveType.Cylinder, new Vector3(0f, 5f, 5.2f), new Vector3(10f, 0.5f, 10f), pawnMat, Quaternion.Euler(90f, 0f, 0f));
        Part(pawn, PrimitiveType.Sphere, new Vector3(0f, 4.6f, 9.2f), Vector3.one * 9.2f, pawnMat, null);

        float size = 6.5f;
        var clock = MakeClock(pawn, "Clock on Pawn", size, faces[face]);
        clock.localPosition = new Vector3(0f, bodyR * 2f + 0.05f, -2f);
        clock.localRotation = Quaternion.Euler(0f, 6f, 0f);
        Drape(clock, size, -0.12f, 0.12f, bodyR, 0.6f, 3);
    }

    // ───────── Clocks ─────────

    public const int Rings = 14, Segments = 48;

    /// <summary>A soft clock: a disc with a face on top (+Y, 12 o'clock toward -Z) and gold behind</summary>
    public static Transform MakeClock(Transform parent, string name, float radius, Material face)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = "Soft Clock" };
        int ringVerts = Rings * Segments + 1;
        var verts = new Vector3[ringVerts * 2];
        var uvs = new Vector2[ringVerts * 2];
        for (int side = 0; side < 2; side++)
        {
            int o = side * ringVerts;
            verts[o] = Vector3.zero;
            uvs[o] = new Vector2(0.5f, 0.5f);
            for (int r = 1; r <= Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                float a = s * Mathf.PI * 2f / Segments, rr = radius * r / Rings;
                Vector3 p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                verts[o + Index(r, s)] = p;
                // Seen from above with 12 o'clock toward -Z, the viewer's right is -X
                uvs[o + Index(r, s)] = new Vector2(0.5f - p.x / (2f * radius), 0.5f - p.z / (2f * radius));
            }
        }
        var front = new System.Collections.Generic.List<int>();
        var back = new System.Collections.Generic.List<int>();
        for (int r = 0; r < Rings; r++)
        for (int s = 0; s < Segments; s++)
        {
            int s1 = (s + 1) % Segments;
            if (r == 0)
            {
                Tri(front, back, ringVerts, 0, Index(1, s1), Index(1, s));
                continue;
            }
            Tri(front, back, ringVerts, Index(r, s), Index(r + 1, s1), Index(r + 1, s));
            Tri(front, back, ringVerts, Index(r, s), Index(r, s1), Index(r + 1, s1));
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(front, 0);
        mesh.SetTriangles(back, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { face, backMat };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go.transform;
    }

    public static int Index(int ring, int segment) => 1 + (ring - 1) * Segments + segment;

    private static void Tri(System.Collections.Generic.List<int> front, System.Collections.Generic.List<int> back, int offset, int a, int b, int c)
    {
        front.Add(a); front.Add(b); front.Add(c);
        back.Add(offset + a); back.Add(offset + c); back.Add(offset + b);
    }

    /// <summary>
    /// Lay a clock over an edge (or two): flat between the folds, bent round a radius at each
    /// fold, then hanging down, stretching and running in drips the further it hangs.
    /// </summary>
    private static void Drape(Transform clock, float radius, float leftFold, float rightFold, float bend, float melt, int seed)
    {
        Mesh mesh = clock.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] verts = mesh.vertices;
        float l = leftFold * radius, r = rightFold * radius;
        for (int i = 0; i < verts.Length; i++)
        {
            float x = verts[i].x, z = verts[i].z;
            Vector3 p;
            if (x > r) p = Fold(x - r, z, radius, bend, melt, seed) + new Vector3(r, 0f, 0f);
            else if (x < l) { p = Fold(l - x, z, radius, bend, melt, seed + 5); p.x = l - p.x; }
            else p = new Vector3(x, 0f, z);
            verts[i] = p;
        }
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    // Past a fold: s along the clock from the fold → offset (x away from the fold, y down)
    private static Vector3 Fold(float s, float z, float radius, float bend, float melt, int seed)
    {
        float arc = bend * Mathf.PI * 0.5f;
        Vector3 p;
        if (s < arc)
        {
            float a = s / bend;
            p = new Vector3(bend * Mathf.Sin(a), -bend * (1f - Mathf.Cos(a)), z);
        }
        else
        {
            float hang = s - arc, t = hang / radius;
            float across = z / radius;
            // Drips: a few long runs at set places across the clock
            float drip = 0.45f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(across * 5.3f + seed)), 8f) + 0.25f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(across * 9.1f + seed * 0.7f)), 6f);
            float down = hang * (1f + 0.55f * melt * t) + drip * melt * t * t * radius;
            p = new Vector3(bend - hang * 0.04f, -bend - down, z * (1f - 0.12f * melt * Mathf.Min(t, 1f)));
        }
        return p;
    }

    // ───────── Materials and textures ─────────

    private static Material FaceMaterial(Color bezel, bool hands, int seed)
    {
        FaceTextures(bezel, hands, seed, out Texture2D albedo, out Texture2D glow);
        var m = Lit(Color.white, Color.white * 0.9f, 0.1f, 0.55f, albedo);
        m.SetTexture("_EmissionMap", glow);
        return m;
    }

    // A clock face: ivory dial, hour marks, minute dots, a gold bezel with a neon inner ring
    private static void FaceTextures(Color neon, bool hands, int seed, out Texture2D albedo, out Texture2D glow)
    {
        const int size = 256;
        albedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        glow = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var a = new Color32[size * size];
        var g = new Color32[size * size];
        Color ink = new Color(0.1f, 0.06f, 0.22f), gold = new Color(1f, 0.78f, 0.36f);
        float hourAngle = (seed * 2.3f + 1.7f) % (Mathf.PI * 2f), minuteAngle = (seed * 4.1f + 0.4f) % (Mathf.PI * 2f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = (x + 0.5f) / size * 2f - 1f, ny = (y + 0.5f) / size * 2f - 1f; // ny up
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float ang = Mathf.Atan2(nx, ny); // 0 at 12, clockwise
            Color c, e;
            if (r > 0.9f)
            {
                c = Color.Lerp(gold, gold * 0.7f, Mathf.Abs(Mathf.Sin(ang * 3f)) * 0.4f + (r - 0.9f) * 3f);
                e = gold * 0.15f;
            }
            else if (r > 0.86f)
            {
                c = neon;
                e = neon;
            }
            else
            {
                c = Color.Lerp(new Color(1f, 0.97f, 0.88f), new Color(0.93f, 0.84f, 0.76f), r * r);
                e = c * 0.45f;
                float step = Mathf.PI / 6f;
                float off = Mathf.Abs(Mathf.Repeat(ang / step + 0.5f, 1f) - 0.5f) * step * r;
                bool major = Mathf.Abs(Mathf.Repeat(ang / (Mathf.PI / 2f) + 0.5f, 1f) - 0.5f) * (Mathf.PI / 2f) * r < 0.04f;
                if (r > (major ? 0.58f : 0.66f) && r < 0.8f && off < (major ? 0.04f : 0.022f)) { c = ink; e = Color.black; }
                float mstep = Mathf.PI / 30f;
                float moff = Mathf.Abs(Mathf.Repeat(ang / mstep + 0.5f, 1f) - 0.5f) * mstep * r;
                if (Mathf.Abs(r - 0.83f) < 0.012f && moff < 0.012f) { c = ink; e = Color.black; }
                if (hands && (OnHand(nx, ny, hourAngle, 0.45f, 0.05f) || OnHand(nx, ny, minuteAngle, 0.68f, 0.032f))) { c = ink; e = Color.black; }
                if (r < 0.06f) { c = gold; e = gold * 0.3f; }
            }
            c.a = 1f; e.a = 1f;
            a[y * size + x] = c;
            g[y * size + x] = e;
        }
        albedo.SetPixels32(a);
        albedo.Apply(true, true);
        glow.SetPixels32(g);
        glow.Apply(true, true);
    }

    private static bool OnHand(float nx, float ny, float angle, float length, float width)
    {
        float hx = Mathf.Sin(angle), hy = Mathf.Cos(angle);
        float along = nx * hx + ny * hy, across = Mathf.Abs(nx * hy - ny * hx);
        return along > -0.08f && along < length && across < width * (1f - 0.5f * along / length);
    }

    // Smooth warm sand with faint wind streaks; tiles seamlessly
    private static Texture2D GroundTexture()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        var rng = new System.Random(5);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / size, v = (float)y / size;
            float streak = 0.5f + 0.5f * Mathf.Sin((v * 3f + 0.08f * Mathf.Sin(u * Mathf.PI * 2f)) * Mathf.PI * 2f);
            float grain = (float)rng.NextDouble() * 0.05f;
            Color c = Color.Lerp(new Color(0.5f, 0.32f, 0.38f), new Color(0.68f, 0.46f, 0.44f), 0.55f + 0.2f * streak) + new Color(grain, grain, grain);
            c.a = 1f;
            px[y * size + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    private static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rotation)
    {
        Transform t = JungleScenery.Part(parent, type, pos, scale, mat);
        if (rotation.HasValue) t.localRotation = rotation.Value;
        return t;
    }

    private static Material Lit(Color color, Color emission, float metallic, float smoothness, Texture2D texture)
    {
        Material template = Resources.Load<Material>("PieceSets/Glow");
        Material m = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        m.mainTexture = texture;
        m.color = color;
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", null);
        m.SetColor("_EmissionColor", emission);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }
}

/// <summary>
/// A clock drifting slowly round the board, facing it. Its lower half sags and runs as it
/// melts, then slowly firms up again, while its hands spin at their own odd speeds.
/// </summary>
public class DreamFloatingClock : MonoBehaviour
{
    private float angle, radius, height, size, phase, orbitSpeed, minuteSpeed, hourSpeed;
    private Transform minute, hour;
    private Mesh mesh;
    private Vector3[] flat, verts;

    public void Init(float startAngle, float orbitRadius, float h, float clockSize, int seed, Material handMat)
    {
        var rng = new System.Random(seed);
        angle = startAngle;
        radius = orbitRadius;
        height = h;
        size = clockSize;
        phase = (float)rng.NextDouble() * 10f;
        orbitSpeed = (rng.NextDouble() < 0.5 ? -1f : 1f) * (1.2f + (float)rng.NextDouble() * 1.5f);   // degrees a second
        minuteSpeed = (rng.NextDouble() < 0.3 ? -1f : 1f) * (40f + (float)rng.NextDouble() * 120f); // some run backwards
        hourSpeed = minuteSpeed / (6f + (float)rng.NextDouble() * 6f);

        // The clock mesh lies in its own XZ plane; stand it up facing the board, 12 o'clock up
        var holder = new GameObject("Floating Clock");
        holder.transform.SetParent(transform.parent, false);
        transform.SetParent(holder.transform, false);
        transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        mesh = GetComponent<MeshFilter>().sharedMesh;
        mesh.MarkDynamic();
        flat = mesh.vertices;
        verts = new Vector3[flat.Length];

        minute = Hand(handMat, size * 0.66f, size * 0.07f);
        hour = Hand(handMat, size * 0.42f, size * 0.1f);
        JungleScenery.Part(transform, PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(size * 0.12f, 0.06f, size * 0.12f), handMat);
    }

    private Transform Hand(Material mat, float length, float width)
    {
        var pivot = new GameObject("Hand").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0f, 0.06f, 0f);
        JungleScenery.Part(pivot, PrimitiveType.Cube, new Vector3(0f, 0f, -length * 0.45f), new Vector3(width, 0.05f, length), mat);
        return pivot;
    }

    private void Update()
    {
        if (mesh == null) return;
        float t = Time.time;
        angle += orbitSpeed * Time.deltaTime;
        float a = angle * Mathf.Deg2Rad;
        Transform holder = transform.parent;
        holder.localPosition = new Vector3(Mathf.Cos(a) * radius, height + Mathf.Sin(t * 0.3f + phase) * 2f, Mathf.Sin(a) * radius);
        Vector3 toBoard = -new Vector3(holder.localPosition.x, 0f, holder.localPosition.z);
        holder.localRotation = Quaternion.LookRotation(toBoard) * Quaternion.Euler(Mathf.Sin(t * 0.21f + phase) * 8f, 0f, Mathf.Sin(t * 0.17f + phase) * 10f);

        minute.localRotation = Quaternion.Euler(0f, t * minuteSpeed + phase * 40f, 0f);
        hour.localRotation = Quaternion.Euler(0f, t * hourSpeed + phase * 90f, 0f);

        // Melt: below the middle (+z is down on the face), stretch, narrow, curl forward and drip
        float melt = 0.55f + 0.4f * Mathf.Sin(t * 0.35f + phase);
        for (int i = 0; i < flat.Length; i++)
        {
            Vector3 p = flat[i];
            float below = Mathf.Max(0f, p.z / size);
            float across = p.x / size;
            float drip = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(across * 6.2f + phase)), 6f) * 0.6f;
            float wobble = Mathf.Sin(p.z / size * 3f + t * 0.8f + phase) * 0.03f * size;
            verts[i] = new Vector3(
                p.x * (1f - 0.18f * below * melt) + wobble,
                p.y + below * below * size * 0.35f * melt,
                p.z + below * below * size * 0.6f * melt + drip * below * below * below * size * melt);
        }
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}
