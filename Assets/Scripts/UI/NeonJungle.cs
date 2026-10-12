using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Neon Jungle backdrop: a night jungle around a glowing pond.
//
//  - JungleSky: the 360° sky. Night gradient with a pale moon and faint aurora, three
//    layers of tree-crown silhouettes with glowing rims, and mist on the horizon.
//  - JungleScenery: giant circuit-lined trees standing in a dark mirror pond, hanging
//    vines, glowing mushrooms and lily pads, mist and fireflies.
//  - Techpunk animals: frogs on the lily pads (they croak and hop between pads), a
//    toucan-bot on a branch, robot butterflies, and pairs of eyes blinking in the trees.
//
// Everything sits well away from the board so the board stays the focus.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural night-jungle sky. Pure C# so it can run on a worker thread.</summary>
public static class JungleSky
{
    // Matches the fog so the 3D trees melt into the painted ones
    public static readonly Color HazeColor = new Color(0.05f, 0.16f, 0.18f);

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        var rng = new System.Random(seed);
        const float horizon = 0.5f;          // v of the horizon (v = 0 is straight up)

        // Tree-crown silhouettes, far to near: per column, height above the horizon (in v)
        float[][] layers =
        {
            Crowns(width, seed + 1, 34, 0.035f, 0.075f, 0.012f),
            Crowns(width, seed + 2, 22, 0.05f, 0.12f, 0.02f),
            Crowns(width, seed + 3, 14, 0.07f, 0.17f, 0.03f),
        };
        Color[] bodies = { new Color(0.10f, 0.20f, 0.26f), new Color(0.04f, 0.10f, 0.12f), new Color(0.01f, 0.03f, 0.035f) };
        Color[] rims = { new Color(0.9f, 0.2f, 0.75f), new Color(0.1f, 0.9f, 0.85f), new Color(0.65f, 1f, 0.2f) };
        float[] rimStrength = { 0.35f, 0.5f, 0.6f };

        // Sparse stars
        var stars = new HashSet<int>();
        for (int k = 0; k < 1100; k++) stars.Add(rng.Next((int)(height * 0.33f)) * width + rng.Next(width));

        // Glowing specks in the trees (bioluminescence), and mushroom glows below the horizon
        var specks = new Dictionary<int, Color>();
        Color[] speckColors = { new Color(0.6f, 1f, 0.25f), new Color(0.2f, 1f, 0.9f), new Color(1f, 0.35f, 0.8f) };
        for (int k = 0; k < 2600; k++)
        {
            int sx = rng.Next(width);
            int sy = (int)(height * (horizon - 0.15f * (float)rng.NextDouble()));
            specks[sy * width + sx] = speckColors[rng.Next(speckColors.Length)];
        }

        const float moonU = 0.62f, moonV = 0.2f, moonR = 0.045f;

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color c;

                if (v < horizon)
                {
                    float above = horizon - v;          // 0 at the horizon, 0.5 straight up
                    float t = above / horizon;
                    // Deep violet overhead, teal toward the horizon
                    c = Color.Lerp(HazeColor * 1.6f, new Color(0.03f, 0.01f, 0.09f), Mathf.Pow(t, 0.7f));
                    if (stars.Contains(y * width + x)) c += Color.white * (0.45f * t);

                    // Faint aurora ribbons high up, slowly changing colour around the sky
                    float ribbon = Fbm(u * 6f, v * 3f, seed + 9) - 0.55f;
                    float band = Mathf.Exp(-Mathf.Pow((above - 0.27f - 0.05f * Mathf.Sin(u * Mathf.PI * 2 * 3)) / 0.05f, 2));
                    if (ribbon > 0f) c += Color.Lerp(new Color(0.1f, 0.8f, 0.5f), new Color(0.6f, 0.2f, 0.9f), 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 4)) * (ribbon * band * 0.9f);

                    // Moon: pale cyan disc with soft craters, inside a wide glow
                    float du = WrapDelta(u - moonU) * 2f, dv = v - moonV;
                    float dist = Mathf.Sqrt(du * du + dv * dv);
                    c += new Color(0.3f, 0.75f, 0.85f) * (0.35f * Mathf.Exp(-dist * dist / (moonR * moonR * 9f)));
                    if (dist < moonR)
                    {
                        float crater = Fbm(du * 40f, dv * 40f, seed + 4);
                        Color moon = Color.Lerp(new Color(0.75f, 0.98f, 1f), new Color(0.45f, 0.75f, 0.85f), crater * 0.8f);
                        float edge = Mathf.Clamp01((moonR - dist) / 0.004f);
                        c = Color.Lerp(c, moon, edge);
                    }

                    // Tree crowns, far to near, each with a thin glowing rim along its top
                    for (int l = 0; l < layers.Length; l++)
                    {
                        float top = layers[l][x];
                        if (above > top) continue;
                        float depth = (top - above);
                        Color body = bodies[l];
                        // Lighter toward the top of distant layers (moonlit), mist toward the horizon
                        body = Color.Lerp(body, HazeColor * 1.3f, Mathf.Clamp01(1f - above / 0.05f) * (0.7f - 0.2f * l));
                        float rim = Mathf.Exp(-depth / 0.0025f) * rimStrength[l];
                        c = body + rims[l] * rim;
                        if (specks.TryGetValue(y * width + x, out Color s) && l > 0) c += s * 0.9f;
                    }
                }
                else
                {
                    // Below the horizon: the dark jungle floor and pond, misty near the horizon
                    float below = v - horizon;
                    c = Color.Lerp(HazeColor * 1.2f, new Color(0.0f, 0.02f, 0.025f), Mathf.Clamp01(below / 0.12f));
                    if (specks.TryGetValue(y * width + x, out Color s)) c += s * 0.7f;
                    // Glowing mushroom patches just below the horizon
                    float patch = Fbm(u * 18f, below * 30f, seed + 5);
                    if (below < 0.08f && patch > 0.68f)
                        c += Color.Lerp(new Color(1f, 0.3f, 0.75f), new Color(0.2f, 0.9f, 1f), Hash01(x / 40, seed)) * ((patch - 0.68f) * 2.5f * (1f - below / 0.08f));
                }

                result[y * width + x] = new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), 255);
            }
        }
        return result;
    }

    /// <summary>
    /// A row of rounded tree crowns around the sky: domes of varied width and height, with
    /// smaller bumps on top, so the skyline reads as foliage rather than mountains.
    /// </summary>
    private static float[] Crowns(int width, int seed, int count, float minH, float maxH, float bumps)
    {
        var rng = new System.Random(seed);
        var heights = new float[width];
        var centers = new List<(float c, float half, float h)>();
        float u = 0f;
        while (u < 1f)
        {
            float half = (0.6f + (float)rng.NextDouble()) / count;
            centers.Add((u + half, half * 1.25f, minH + (float)rng.NextDouble() * (maxH - minH)));
            u += half * 1.6f;
        }
        for (int x = 0; x < width; x++)
        {
            float ux = (x + 0.5f) / width;
            float best = 0f;
            foreach (var crown in centers)
            {
                float d = Mathf.Abs(WrapDelta(ux - crown.c)) / crown.half;
                if (d >= 1f) continue;
                float dome = crown.h * Mathf.Sqrt(1f - d * d);
                if (dome > best) best = dome;
            }
            // Leafy lumps along the top
            float lumps = bumps * (Noise1(ux * count * 5f, count * 5, seed + 11) - 0.5f);
            heights[x] = Mathf.Max(0.01f, best + lumps);
        }
        return heights;
    }

    private static float WrapDelta(float d)
    {
        d %= 1f;
        if (d > 0.5f) d -= 1f;
        if (d < -0.5f) d += 1f;
        return d;
    }

    private static float Noise1(float x, int period, int seed)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i;
        float a = Hash01(((i % period) + period) % period, seed), b = Hash01((((i + 1) % period) + period) % period, seed);
        f = f * f * (3f - 2f * f);
        return a + (b - a) * f;
    }

    private static float Fbm(float x, float y, int seed)
    {
        float sum = 0f, amp = 0.5f;
        for (int o = 0; o < 4; o++)
        {
            sum += amp * Value(x, y, seed + o * 31);
            x *= 2f; y *= 2f; amp *= 0.5f;
        }
        return sum;
    }

    private static float Value(float x, float y, int seed)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash2(ix, iy, seed), b = Hash2(ix + 1, iy, seed), c = Hash2(ix, iy + 1, seed), d = Hash2(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Hash01(int x, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    private static float Hash2(int x, int y, int seed) => Hash01(x * 73856093 ^ y * 19349663, seed);

    private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
}

/// <summary>The 3D jungle around the board</summary>
public static class JungleScenery
{
    public const float WaterY = -30f;

    private static Material dark, trunk, leaf, water, padMat;
    private static Material[] neon;

    public static void Build(Transform root)
    {
        var rng = new System.Random(4242);
        dark = Lit(new Color(0.02f, 0.05f, 0.05f), Color.black, 0.2f, 0.2f);
        trunk = Lit(new Color(0.05f, 0.08f, 0.09f), new Color(0.0f, 0.06f, 0.07f), 0.5f, 0.4f);
        leaf = Lit(new Color(0.03f, 0.16f, 0.12f), new Color(0.02f, 0.2f, 0.14f), 0.1f, 0.35f);
        padMat = Lit(new Color(0.04f, 0.25f, 0.12f), new Color(0.1f, 0.5f, 0.15f) * 0.35f, 0.1f, 0.5f);
        neon = new[]
        {
            Unlit(new Color(0.55f, 1f, 0.2f)),   // lime
            Unlit(new Color(0.15f, 1f, 0.9f)),   // cyan
            Unlit(new Color(1f, 0.3f, 0.78f)),   // pink
            Unlit(new Color(1f, 0.85f, 0.25f)),  // amber
        };

        // A still, dark pond far below the board that mirrors the sky
        water = Lit(new Color(0.0f, 0.03f, 0.04f), Color.black, 0.9f, 0.95f);
        var pond = Part(root, PrimitiveType.Cylinder, new Vector3(0, WaterY, 0), new Vector3(420f, 0.05f, 420f), water);
        pond.name = "Pond";

        BuildTrees(root, rng);
        var pads = BuildLilyPads(root, rng);
        BuildMushrooms(root, rng);
        BuildFrogs(root, pads, rng);
        BuildToucan(root);
        BuildButterflies(root, rng);
        BuildEyes(root, rng);

        // Everything that never moves is merged into a few big meshes, so the phone draws it cheaply
        var still = new List<GameObject>();
        foreach (string name in new[] { "Trees", "Lily Pads", "Mushrooms" })
        {
            Transform holder = root.Find(name);
            if (holder == null) continue;
            foreach (MeshRenderer r in holder.GetComponentsInChildren<MeshRenderer>())
                if (r.GetComponentInParent<JungleSway>() == null) still.Add(r.gameObject);
        }
        StaticBatchingUtility.Combine(still.ToArray(), root.gameObject);

        root.gameObject.AddComponent<JungleFireflies>();
        root.gameObject.AddComponent<JungleMist>();
    }

    // ───────── Trees ─────────

    private static readonly List<(Vector3 pos, float radius)> trunks = new List<(Vector3, float)>();

    private static void BuildTrees(Transform root, System.Random rng)
    {
        trunks.Clear();
        var holder = new GameObject("Trees").transform;
        holder.SetParent(root, false);
        int count = 14;
        for (int i = 0; i < count; i++)
        {
            float angle = (i + (float)rng.NextDouble() * 0.6f) / count * Mathf.PI * 2f;
            float dist = 72f + (float)rng.NextDouble() * 45f; // well back from the board
            Vector3 basePos = new Vector3(Mathf.Cos(angle) * dist, WaterY, Mathf.Sin(angle) * dist);
            float height = 45f + (float)rng.NextDouble() * 30f;       // tops between y = 15 and 45
            float r = 2.2f + (float)rng.NextDouble() * 1.6f;
            var tree = new GameObject("Tree").transform;
            tree.SetParent(holder, false);
            tree.localPosition = basePos;
            tree.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            trunks.Add((basePos, r));

            // Trunk, a little thinner toward the top, gently leaning
            float lean = ((float)rng.NextDouble() - 0.5f) * 6f;
            var t1 = Part(tree, PrimitiveType.Cylinder, new Vector3(0, height * 0.25f, 0), new Vector3(r * 2f, height * 0.25f, r * 2f), trunk);
            var t2 = Part(tree, PrimitiveType.Cylinder, new Vector3(0, height * 0.72f, 0), new Vector3(r * 1.5f, height * 0.25f, r * 1.5f), trunk);
            t2.localRotation = Quaternion.Euler(lean, 0f, lean * 0.5f);

            // Roots flaring into the water
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f + (float)rng.NextDouble() * 30f;
                var root1 = Part(tree, PrimitiveType.Capsule, Quaternion.Euler(0, a, 0) * new Vector3(r * 1.3f, 1.2f, 0), new Vector3(r * 0.7f, r * 1.4f, r * 0.7f), trunk);
                root1.localRotation = Quaternion.Euler(0, a, 0) * Quaternion.Euler(0, 0, 55f);
            }

            // Glowing circuit lines running up the bark, with a node every so often
            Material line = neon[rng.Next(neon.Length)];
            int lines = 1 + rng.Next(3);
            for (int k = 0; k < lines; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float len = height * (0.3f + (float)rng.NextDouble() * 0.4f);
                float y0 = 1f + (float)rng.NextDouble() * height * 0.2f;
                Vector3 side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r * 1.0f + 0.02f);
                Part(tree, PrimitiveType.Cube, side + Vector3.up * (y0 + len / 2f), new Vector3(0.18f, len, 0.18f), line);
                for (float ny = y0 + 3f; ny < y0 + len; ny += 4f + (float)rng.NextDouble() * 4f)
                    Part(tree, PrimitiveType.Sphere, side + Vector3.up * ny, Vector3.one * 0.55f, line);
            }
            // A glowing ring or two around the trunk
            if (rng.NextDouble() < 0.6)
            {
                var ring = Part(tree, PrimitiveType.Cylinder, new Vector3(0, height * (0.3f + 0.3f * (float)rng.NextDouble()), 0), new Vector3(r * 2.15f, 0.12f, r * 2.15f), neon[rng.Next(neon.Length)]);
                ring.name = "Ring";
            }

            // Canopy: a cluster of flattened leafy domes, dotted with glowing buds
            int puffs = 4 + rng.Next(3);
            for (int k = 0; k < puffs; k++)
            {
                Vector3 offset = new Vector3(((float)rng.NextDouble() - 0.5f) * 18f, height + ((float)rng.NextDouble() - 0.3f) * 6f, ((float)rng.NextDouble() - 0.5f) * 18f);
                float size = 12f + (float)rng.NextDouble() * 10f;
                var puff = Part(tree, PrimitiveType.Sphere, offset, new Vector3(size, size * 0.45f, size), leaf);
                for (int b = 0; b < 6; b++)
                {
                    Vector3 dir = new Vector3((float)rng.NextDouble() - 0.5f, -0.2f - (float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() - 0.5f).normalized;
                    Vector3 p = offset + Vector3.Scale(dir, new Vector3(size, size * 0.45f, size) * 0.5f);
                    Part(tree, PrimitiveType.Sphere, p, Vector3.one * (0.5f + (float)rng.NextDouble() * 0.5f), neon[rng.Next(neon.Length)]);
                }
            }

            // Vines hanging from the canopy, some with glowing bulbs
            int vines = 2 + rng.Next(3);
            for (int k = 0; k < vines; k++)
            {
                Vector3 top = new Vector3(((float)rng.NextDouble() - 0.5f) * 14f, height - 3f, ((float)rng.NextDouble() - 0.5f) * 14f);
                float len = 12f + (float)rng.NextDouble() * 22f;
                var vine = new GameObject("Vine").transform;
                vine.SetParent(tree, false);
                vine.localPosition = top;
                Part(vine, PrimitiveType.Cylinder, new Vector3(0, -len / 2f, 0), new Vector3(0.18f, len / 2f, 0.18f), leaf);
                if (rng.NextDouble() < 0.7)
                    Part(vine, PrimitiveType.Sphere, new Vector3(0, -len, 0), Vector3.one * 0.7f, neon[rng.Next(neon.Length)]);
                vine.gameObject.AddComponent<JungleSway>().Init((float)rng.NextDouble() * 6f, 2.5f);
            }
        }
    }

    // ───────── Pond life ─────────

    private static List<Transform> BuildLilyPads(Transform root, System.Random rng)
    {
        var pads = new List<Transform>();
        var holder = new GameObject("Lily Pads").transform;
        holder.SetParent(root, false);
        // Rings of pads at the distances the frogs live at, so they can hop between them
        for (int i = 0; i < 26; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float dist = 24f + (float)rng.NextDouble() * 26f;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * dist, WaterY + 0.05f, Mathf.Sin(angle) * dist);
            float size = 4.5f + (float)rng.NextDouble() * 3.5f;
            var pad = new GameObject("Lily Pad").transform;
            pad.SetParent(holder, false);
            pad.localPosition = pos;
            pad.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            Part(pad, PrimitiveType.Cylinder, Vector3.zero, new Vector3(size, 0.12f, size), padMat);
            // Thin neon rim showing just past the leaf
            Part(pad, PrimitiveType.Cylinder, new Vector3(0, -0.04f, 0), new Vector3(size * 1.05f, 0.06f, size * 1.05f), neon[0]);
            // Glowing veins
            for (int v = 0; v < 3; v++)
            {
                var vein = Part(pad, PrimitiveType.Cube, new Vector3(0, 0.08f, 0), new Vector3(size * 0.9f, 0.02f, 0.08f), neon[1]);
                vein.localRotation = Quaternion.Euler(0, v * 60f, 0);
            }
            // The odd lotus bud
            if (rng.NextDouble() < 0.3)
            {
                Material petal = neon[2];
                for (int p = 0; p < 5; p++)
                {
                    var pt = Part(pad, PrimitiveType.Capsule, Quaternion.Euler(0, p * 72f, 0) * new Vector3(0.35f, 0.7f, 0), new Vector3(0.45f, 0.7f, 0.25f), petal);
                    pt.localRotation = Quaternion.Euler(0, p * 72f, -25f);
                }
            }
            pads.Add(pad);
        }
        return pads;
    }

    private static void BuildMushrooms(Transform root, System.Random rng)
    {
        var holder = new GameObject("Mushrooms").transform;
        holder.SetParent(root, false);
        foreach (var t in trunks)
        {
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 pos = t.pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (t.radius + 1.5f + (float)rng.NextDouble() * 3f);
                float s = 0.8f + (float)rng.NextDouble() * 1.4f;
                Material cap = neon[rng.Next(neon.Length)];
                var m = new GameObject("Mushroom").transform;
                m.SetParent(holder, false);
                m.localPosition = pos;
                Part(m, PrimitiveType.Cylinder, new Vector3(0, s * 1.2f, 0), new Vector3(s * 0.35f, s * 1.2f, s * 0.35f), dark);
                Part(m, PrimitiveType.Sphere, new Vector3(0, s * 2.4f, 0), new Vector3(s * 2.2f, s * 0.9f, s * 2.2f), cap);
            }
        }
    }

    // ───────── Animals ─────────

    private static void BuildFrogs(Transform root, List<Transform> pads, System.Random rng)
    {
        // Three frogs spread around the pond, so one is in view from any side
        var holder = new GameObject("Frogs").transform;
        holder.SetParent(root, false);
        Color[] skins = { new Color(0.1f, 0.85f, 0.35f), new Color(0.15f, 0.75f, 0.95f), new Color(0.95f, 0.35f, 0.75f) };
        for (int i = 0; i < 3; i++)
        {
            // The pad closest to this frog's third of the pond
            float want = i / 3f * Mathf.PI * 2f + 0.4f;
            Transform home = pads[0];
            float best = float.MaxValue;
            foreach (Transform p in pads)
            {
                float a = Mathf.Atan2(p.localPosition.z, p.localPosition.x);
                float d = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, want * Mathf.Rad2Deg)) + p.localPosition.magnitude * 0.3f;
                if (d < best) { best = d; home = p; }
            }
            var frog = BuildFrog(holder, skins[i], i == 0 ? 2.1f : 1.6f);
            frog.gameObject.AddComponent<JungleFrog>().Init(pads, home, rng.Next());
        }
    }

    /// <summary>
    /// A chunky robot tree frog: armoured back plates with glowing seams, big lamp eyes,
    /// an antenna, toe pads that glow, and a throat sac that lights up when it croaks.
    /// </summary>
    private static Transform BuildFrog(Transform parent, Color skinColor, float scale)
    {
        var frog = new GameObject("Frog").transform;
        frog.SetParent(parent, false);
        frog.localScale = Vector3.one * scale;
        var body = new GameObject("Body").transform; // breathes and squashes; the frog root hops
        body.SetParent(frog, false);

        Material skin = Lit(skinColor * 0.55f, skinColor * 0.4f, 0.5f, 0.75f);
        Material plate = Lit(new Color(0.05f, 0.08f, 0.1f), Color.black, 0.85f, 0.8f);
        Material glow = Unlit(skinColor);
        Material eyeShell = Lit(new Color(0.75f, 0.8f, 0.85f), new Color(0.25f, 0.3f, 0.35f), 0.6f, 0.9f);
        Material pupil = Unlit(new Color(0.2f, 1f, 1f));
        Material sac = Lit(new Color(0.6f, 0.15f, 0.45f), new Color(1f, 0.3f, 0.75f) * 0.6f, 0.2f, 0.6f);

        // Body and head (frog faces +z)
        Part(body, PrimitiveType.Sphere, new Vector3(0, 1.3f, -0.3f), new Vector3(2.6f, 1.6f, 3.0f), skin);
        Part(body, PrimitiveType.Sphere, new Vector3(0, 1.75f, 1.0f), new Vector3(2.3f, 1.25f, 1.8f), skin);
        // Armour plates on the back with glowing seams between them
        for (int k = 0; k < 3; k++)
        {
            var p = Part(body, PrimitiveType.Cube, new Vector3(0, 2.05f - k * 0.08f, 0.2f - k * 0.75f), new Vector3(1.7f - k * 0.15f, 0.25f, 0.6f), plate);
            p.localRotation = Quaternion.Euler(-12f - k * 8f, 0, 0);
            Part(body, PrimitiveType.Cube, new Vector3(0, 2.05f - k * 0.08f, -0.18f - k * 0.75f), new Vector3(1.5f - k * 0.15f, 0.12f, 0.08f), glow);
        }
        // Side stripes
        Part(body, PrimitiveType.Cube, new Vector3(1.22f, 1.35f, -0.2f), new Vector3(0.06f, 0.12f, 2.2f), glow);
        Part(body, PrimitiveType.Cube, new Vector3(-1.22f, 1.35f, -0.2f), new Vector3(0.06f, 0.12f, 2.2f), glow);
        // Mouth seam
        Part(body, PrimitiveType.Cube, new Vector3(0, 1.45f, 1.85f), new Vector3(1.6f, 0.05f, 0.08f), Unlit(new Color(1f, 0.3f, 0.75f)));

        // Lamp eyes with cyan pupils (they blink)
        var eyes = new List<Transform>();
        foreach (float side in new[] { -1f, 1f })
        {
            Part(body, PrimitiveType.Sphere, new Vector3(side * 0.75f, 2.4f, 1.15f), Vector3.one * 0.95f, eyeShell);
            var pupilT = Part(body, PrimitiveType.Sphere, new Vector3(side * 0.85f, 2.48f, 1.55f), new Vector3(0.5f, 0.5f, 0.25f), pupil);
            eyes.Add(pupilT);
        }

        // Antenna with a blinking tip
        Part(body, PrimitiveType.Cylinder, new Vector3(0.35f, 2.6f, 0.2f), new Vector3(0.06f, 0.45f, 0.06f), plate);
        var tip = Part(body, PrimitiveType.Sphere, new Vector3(0.35f, 3.1f, 0.2f), Vector3.one * 0.22f, Unlit(new Color(1f, 0.85f, 0.25f)));

        // Throat sac (croaks)
        var throat = Part(body, PrimitiveType.Sphere, new Vector3(0, 1.0f, 1.55f), new Vector3(1.1f, 0.8f, 0.9f), sac);

        // Legs: big folded back legs, small front legs, glowing toe pads
        foreach (float side in new[] { -1f, 1f })
        {
            var thigh = Part(body, PrimitiveType.Capsule, new Vector3(side * 1.25f, 0.95f, -0.9f), new Vector3(0.8f, 1.0f, 0.8f), skin);
            thigh.localRotation = Quaternion.Euler(70f, side * 20f, 0);
            var shin = Part(body, PrimitiveType.Capsule, new Vector3(side * 1.5f, 0.45f, -0.4f), new Vector3(0.5f, 0.85f, 0.5f), skin);
            shin.localRotation = Quaternion.Euler(-60f, side * 10f, 0);
            var foot = Part(body, PrimitiveType.Sphere, new Vector3(side * 1.6f, 0.12f, 0.2f), new Vector3(0.9f, 0.2f, 1.1f), skin);
            for (int toe = -1; toe <= 1; toe++)
                Part(body, PrimitiveType.Sphere, new Vector3(side * 1.6f + toe * 0.3f, 0.12f, 0.75f), Vector3.one * 0.22f, glow);

            var arm = Part(body, PrimitiveType.Capsule, new Vector3(side * 0.85f, 0.55f, 1.35f), new Vector3(0.35f, 0.55f, 0.35f), skin);
            arm.localRotation = Quaternion.Euler(10f, 0, side * 12f);
            for (int toe = -1; toe <= 1; toe++)
                Part(body, PrimitiveType.Sphere, new Vector3(side * 0.95f + toe * 0.18f, 0.08f, 1.6f), Vector3.one * 0.16f, glow);
        }

        var parts = frog.gameObject.AddComponent<FrogParts>();
        parts.body = body;
        parts.throat = throat;
        parts.throatMaterial = sac;
        parts.eyes = eyes;
        parts.antenna = tip;
        return frog;
    }

    /// <summary>A toucan-bot perched on a branch, turning its head now and then</summary>
    private static void BuildToucan(Transform root)
    {
        if (trunks.Count == 0) return;
        // A branch reaching in from the nearest tree toward the board
        var (basePos, radius) = trunks[0];
        Vector3 inward = (-new Vector3(basePos.x, 0, basePos.z)).normalized;
        var perch = new GameObject("Toucan Perch").transform;
        perch.SetParent(root, false);
        perch.localPosition = basePos + inward * (radius + 7f) + Vector3.up * 34f;
        perch.localRotation = Quaternion.LookRotation(inward);
        var branch = Part(perch, PrimitiveType.Capsule, new Vector3(0, 0, -4f), new Vector3(0.9f, 5f, 0.9f), trunk);
        branch.localRotation = Quaternion.Euler(90f, 0, 0);
        Part(perch, PrimitiveType.Cube, new Vector3(0, 0.46f, -4f), new Vector3(0.12f, 0.06f, 8f), neon[1]);

        var bird = new GameObject("Toucan").transform;
        bird.SetParent(perch, false);
        bird.localPosition = new Vector3(0, 0.5f, -1.2f);
        bird.localScale = Vector3.one * 1.4f;
        Material feathers = Lit(new Color(0.03f, 0.03f, 0.06f), Color.black, 0.7f, 0.8f);
        Material chest = Lit(new Color(0.9f, 0.85f, 0.3f), new Color(1f, 0.85f, 0.25f) * 0.35f, 0.3f, 0.6f);
        Material beak = Lit(new Color(1f, 0.45f, 0.1f), new Color(1f, 0.35f, 0.1f) * 0.6f, 0.4f, 0.8f);
        Part(bird, PrimitiveType.Capsule, new Vector3(0, 1.4f, 0), new Vector3(1.1f, 1.2f, 1.0f), feathers);
        Part(bird, PrimitiveType.Sphere, new Vector3(0, 1.5f, 0.35f), new Vector3(0.8f, 1.1f, 0.4f), chest);
        var tail = Part(bird, PrimitiveType.Cube, new Vector3(0, 0.5f, -0.45f), new Vector3(0.5f, 1.4f, 0.12f), feathers);
        tail.localRotation = Quaternion.Euler(-20f, 0, 0);
        Part(bird, PrimitiveType.Cube, new Vector3(0, 0.3f, -0.62f), new Vector3(0.45f, 0.06f, 0.06f), neon[2]);
        foreach (float side in new[] { -1f, 1f })
            Part(bird, PrimitiveType.Cube, new Vector3(side * 0.55f, 1.4f, -0.05f), new Vector3(0.06f, 0.9f, 0.7f), neon[2]); // wing edge lights

        var head = new GameObject("Head").transform;
        head.SetParent(bird, false);
        head.localPosition = new Vector3(0, 2.45f, 0.05f);
        Part(head, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.85f, feathers);
        var bk = Part(head, PrimitiveType.Capsule, new Vector3(0, -0.05f, 0.95f), new Vector3(0.45f, 0.75f, 0.55f), beak);
        bk.localRotation = Quaternion.Euler(95f, 0, 0);
        Part(head, PrimitiveType.Cube, new Vector3(0, 0.12f, 0.95f), new Vector3(0.05f, 0.05f, 1.2f), neon[0]); // circuit line on the beak
        foreach (float side in new[] { -1f, 1f })
            Part(head, PrimitiveType.Sphere, new Vector3(side * 0.36f, 0.12f, 0.18f), Vector3.one * 0.2f, neon[1]);
        head.gameObject.AddComponent<JungleHeadTurn>();
    }

    private static void BuildButterflies(Transform root, System.Random rng)
    {
        var holder = new GameObject("Butterflies").transform;
        holder.SetParent(root, false);
        Shader sprite = Shader.Find("Sprites/Default");
        Color[] colors = { new Color(0.2f, 1f, 0.95f), new Color(1f, 0.3f, 0.8f), new Color(0.65f, 1f, 0.2f), new Color(1f, 0.8f, 0.2f) };
        Texture2D wingTex = WingTexture();
        for (int i = 0; i < 9; i++)
        {
            var b = new GameObject("Butterfly").transform;
            b.SetParent(holder, false);
            var wingMat = new Material(sprite) { mainTexture = wingTex, color = colors[i % colors.Length] };
            Part(b, PrimitiveType.Capsule, Vector3.zero, new Vector3(0.12f, 0.45f, 0.12f), dark).localRotation = Quaternion.Euler(90f, 0, 0);
            var left = Wing(b, wingMat, -1f);
            var right = Wing(b, wingMat, 1f);
            b.localScale = Vector3.one * (1.4f + (float)rng.NextDouble() * 0.8f);
            b.gameObject.AddComponent<JungleButterfly>().Init(left, right, rng.Next());
        }
    }

    private static Transform Wing(Transform body, Material mat, float side)
    {
        var hinge = new GameObject("Wing").transform;
        hinge.SetParent(body, false);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(hinge, false);
        quad.transform.localPosition = new Vector3(side * 0.55f, 0, 0);
        quad.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        quad.transform.localScale = new Vector3(side * 1.1f, 1.2f, 1f);
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return hinge;
    }

    // Wing shape: two rounded lobes with a glowing edge and circuit dots, transparent elsewhere
    private static Texture2D WingTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            // Upper lobe big, lower lobe smaller, both attached at the body (u = 0)
            float d1 = new Vector2((u - 0.45f) / 0.5f, (v - 0.65f) / 0.35f).magnitude;
            float d2 = new Vector2((u - 0.35f) / 0.35f, (v - 0.25f) / 0.25f).magnitude;
            float d = Mathf.Min(d1, d2);
            float a = 0f, bright = 0.55f;
            if (d < 1f)
            {
                a = 0.75f;
                if (d > 0.82f) { a = 1f; bright = 1f; }                        // glowing edge
                if (((x / 6) + (y / 6)) % 4 == 0 && d < 0.7f) bright = 0.95f;  // circuit dots
            }
            tex.SetPixel(x, y, new Color(bright, bright, bright, a));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>Pairs of glowing eyes in the dark between the trees, blinking now and then</summary>
    private static void BuildEyes(Transform root, System.Random rng)
    {
        var holder = new GameObject("Eyes").transform;
        holder.SetParent(root, false);
        Material[] eyeMats = { neon[3], neon[0], Unlit(new Color(1f, 0.2f, 0.2f)) };
        for (int i = 0; i < 10; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float dist = 70f + (float)rng.NextDouble() * 35f;
            var pair = new GameObject("Eyes").transform;
            pair.SetParent(holder, false);
            pair.localPosition = new Vector3(Mathf.Cos(angle) * dist, WaterY + 6f + (float)rng.NextDouble() * 30f, Mathf.Sin(angle) * dist);
            pair.localRotation = Quaternion.LookRotation(-new Vector3(pair.localPosition.x, 0, pair.localPosition.z));
            Material m = eyeMats[rng.Next(eyeMats.Length)];
            float gap = 0.6f + (float)rng.NextDouble() * 0.5f;
            Part(pair, PrimitiveType.Sphere, new Vector3(-gap, 0, 0), new Vector3(0.6f, 0.35f, 0.2f), m);
            Part(pair, PrimitiveType.Sphere, new Vector3(gap, 0, 0), new Vector3(0.6f, 0.35f, 0.2f), m);
            pair.gameObject.AddComponent<JungleEyes>().Init(rng.Next());
        }
    }

    // ───────── Helpers ─────────

    public static Transform Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        UnityEngine.Object.Destroy(go.GetComponent<Collider>()); // scenery never blocks the camera or taps
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    private static Material Lit(Color color, Color emission, float metallic, float smoothness)
    {
        Material template = Resources.Load<Material>("PieceSets/Glow");
        Material m = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        m.mainTexture = null;
        m.color = color;
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", null);
        m.SetColor("_EmissionColor", emission);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }

    private static Material Unlit(Color color)
    {
        var m = new Material(Shader.Find("Unlit/Color"));
        m.color = color;
        return m;
    }
}

/// <summary>Holds the moving parts of a frog for JungleFrog</summary>
public class FrogParts : MonoBehaviour
{
    public Transform body, throat, antenna;
    public Material throatMaterial;
    public List<Transform> eyes;
}

/// <summary>
/// A frog living on the lily pads: breathes, blinks, croaks (the throat sac swells and
/// glows), and every so often hops to a nearby pad, landing with a ripple of light.
/// It always settles facing the board.
/// </summary>
public class JungleFrog : MonoBehaviour
{
    private List<Transform> pads;
    private Transform pad;
    private System.Random rng;
    private FrogParts parts;
    private float nextCroak, nextBlink, nextHop;
    private bool hopping;
    private Vector3 throatScale;
    private Color throatGlow;

    public void Init(List<Transform> allPads, Transform home, int seed)
    {
        pads = allPads;
        pad = home;
        rng = new System.Random(seed);
    }

    private void Start()
    {
        parts = GetComponent<FrogParts>();
        throatScale = parts.throat.localScale;
        throatGlow = parts.throatMaterial.GetColor("_EmissionColor");
        Settle();
        float now = Time.time;
        nextCroak = now + 1f + (float)rng.NextDouble() * 4f;
        nextBlink = now + 2f + (float)rng.NextDouble() * 3f;
        nextHop = now + 6f + (float)rng.NextDouble() * 8f;
    }

    private void Settle()
    {
        transform.localPosition = pad.localPosition + Vector3.up * 0.12f;
        FaceBoard();
    }

    private void FaceBoard()
    {
        Vector3 toBoard = -new Vector3(transform.localPosition.x, 0, transform.localPosition.z);
        if (toBoard.sqrMagnitude > 0.01f) transform.localRotation = Quaternion.LookRotation(toBoard);
    }

    private void Update()
    {
        float now = Time.time;

        // Breathing
        if (!hopping)
        {
            float b = 1f + Mathf.Sin(now * 2.2f) * 0.025f;
            parts.body.localScale = new Vector3(1f, b, 1f);
        }

        // Croak: the throat swells twice and glows
        float sinceCroak = now - nextCroak;
        if (sinceCroak >= 0f && sinceCroak < 1.1f)
        {
            float pulse = Mathf.Max(0f, Mathf.Sin(sinceCroak / 1.1f * Mathf.PI * 2f));
            parts.throat.localScale = throatScale * (1f + pulse * 0.9f);
            parts.throatMaterial.SetColor("_EmissionColor", throatGlow * (1f + pulse * 3f));
        }
        else if (sinceCroak >= 1.1f)
        {
            parts.throat.localScale = throatScale;
            parts.throatMaterial.SetColor("_EmissionColor", throatGlow);
            nextCroak = now + 4f + (float)rng.NextDouble() * 7f;
        }

        // Blink: pupils squash shut for a moment
        float sinceBlink = now - nextBlink;
        float lid = sinceBlink >= 0f && sinceBlink < 0.14f ? 0.1f : 1f;
        foreach (Transform e in parts.eyes) e.localScale = new Vector3(0.5f, 0.5f * lid, 0.25f);
        if (sinceBlink >= 0.14f) nextBlink = now + 2.5f + (float)rng.NextDouble() * 4f;

        // Antenna tip blinks
        parts.antenna.localScale = Vector3.one * (Mathf.Repeat(now, 1.6f) < 0.2f ? 0.32f : 0.2f);

        if (!hopping && now >= nextHop) StartCoroutine(Hop());
    }

    private System.Collections.IEnumerator Hop()
    {
        hopping = true;
        // A pad close enough to jump to
        Transform target = null;
        for (int tries = 0; tries < 12 && target == null; tries++)
        {
            Transform p = pads[rng.Next(pads.Count)];
            float d = Vector3.Distance(p.localPosition, pad.localPosition);
            if (p != pad && d > 4f && d < 16f) target = p;
        }
        if (target == null) { hopping = false; nextHop = Time.time + 5f; yield break; }

        Vector3 from = transform.localPosition, to = target.localPosition + Vector3.up * 0.12f;
        Vector3 dir = to - from; dir.y = 0f;

        // Turn toward the target
        Quaternion start = transform.localRotation, facing = Quaternion.LookRotation(dir);
        for (float t = 0; t < 0.35f; t += Time.deltaTime) { transform.localRotation = Quaternion.Slerp(start, facing, t / 0.35f); yield return null; }
        // Crouch
        for (float t = 0; t < 0.15f; t += Time.deltaTime) { parts.body.localScale = new Vector3(1.08f, 1f - 0.2f * t / 0.15f, 1.08f); yield return null; }
        // Leap in an arc, stretched
        float duration = 0.55f + dir.magnitude * 0.03f;
        float arc = 3f + dir.magnitude * 0.35f;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            transform.localPosition = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * arc;
            parts.body.localScale = new Vector3(0.9f, 1.15f, 1.2f);
            yield return null;
        }
        transform.localPosition = to;
        pad = target;
        // Land: squash and a ring of light on the water
        PieceEffects.LandingRing(transform.position, new Color(0.55f, 1f, 0.3f));
        for (float t = 0; t < 0.2f; t += Time.deltaTime) { parts.body.localScale = new Vector3(1.12f, 0.85f + 0.15f * t / 0.2f, 1.12f); yield return null; }
        parts.body.localScale = Vector3.one;
        // Turn back toward the board
        start = transform.localRotation;
        Vector3 toBoard = -new Vector3(to.x, 0, to.z);
        Quaternion settle = Quaternion.LookRotation(toBoard);
        for (float t = 0; t < 0.6f; t += Time.deltaTime) { transform.localRotation = Quaternion.Slerp(start, settle, t / 0.6f); yield return null; }
        hopping = false;
        nextHop = Time.time + 8f + (float)rng.NextDouble() * 10f;
    }
}

/// <summary>A robot butterfly fluttering along a slow wandering loop between the trees</summary>
public class JungleButterfly : MonoBehaviour
{
    private Transform left, right;
    private float radius, height, speed, phase, flap, wobble;

    public void Init(Transform l, Transform r, int seed)
    {
        left = l; right = r;
        var rng = new System.Random(seed);
        // Out past the board and below it, so they never flutter in front of the pieces
        radius = 32f + (float)rng.NextDouble() * 24f;
        height = JungleScenery.WaterY + 4f + (float)rng.NextDouble() * 18f;
        speed = (0.03f + (float)rng.NextDouble() * 0.04f) * (rng.NextDouble() < 0.5 ? 1f : -1f);
        phase = (float)rng.NextDouble() * Mathf.PI * 2f;
        flap = 9f + (float)rng.NextDouble() * 5f;
        wobble = (float)rng.NextDouble() * 10f;
    }

    private void Update()
    {
        float t = Time.time;
        float a = phase + t * speed * Mathf.PI * 2f;
        float r = radius + Mathf.Sin(t * 0.37f + wobble) * 6f;
        Vector3 pos = new Vector3(Mathf.Cos(a) * r, height + Mathf.Sin(t * 0.9f + wobble) * 2.5f, Mathf.Sin(a) * r);
        Vector3 vel = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * Mathf.Sign(speed);
        transform.localPosition = pos;
        transform.localRotation = Quaternion.LookRotation(vel) * Quaternion.Euler(-10f, 0, 0);
        float angle = Mathf.Sin(t * flap) * 55f;
        left.localRotation = Quaternion.Euler(0, 0, angle);
        right.localRotation = Quaternion.Euler(0, 0, -angle);
    }
}

/// <summary>A pair of eyes in the dark that blink, and sometimes close for a while</summary>
public class JungleEyes : MonoBehaviour
{
    private System.Random rng;
    private float next, openFor;
    private bool open = true;

    public void Init(int seed) => rng = new System.Random(seed);

    private void Start() => next = Time.time + (float)rng.NextDouble() * 6f;

    private void Update()
    {
        if (Time.time < next) return;
        if (open)
        {
            // Blink, or close and vanish for a while
            bool vanish = rng.NextDouble() < 0.3;
            SetOpen(false);
            next = Time.time + (vanish ? 4f + (float)rng.NextDouble() * 8f : 0.15f);
        }
        else
        {
            SetOpen(true);
            next = Time.time + 2f + (float)rng.NextDouble() * 6f;
        }
    }

    private void SetOpen(bool value)
    {
        open = value;
        foreach (Transform child in transform) child.gameObject.SetActive(value);
    }
}

/// <summary>Turns a head to look around now and then</summary>
public class JungleHeadTurn : MonoBehaviour
{
    private Quaternion from, to;
    private float start, next;

    private void Start() { from = to = transform.localRotation; next = Time.time + 2f; }

    private void Update()
    {
        if (Time.time >= next)
        {
            from = transform.localRotation;
            to = Quaternion.Euler(UnityEngine.Random.Range(-15f, 20f), UnityEngine.Random.Range(-60f, 60f), UnityEngine.Random.Range(-15f, 15f));
            start = Time.time;
            next = Time.time + 1.5f + UnityEngine.Random.value * 3f;
        }
        transform.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, (Time.time - start) / 0.25f));
    }
}

/// <summary>Gentle sway, for hanging vines</summary>
public class JungleSway : MonoBehaviour
{
    private float phase, degrees;
    public void Init(float p, float deg) { phase = p; degrees = deg; }
    private void Update()
    {
        float t = Time.time * 0.6f + phase;
        transform.localRotation = Quaternion.Euler(Mathf.Sin(t) * degrees, 0, Mathf.Sin(t * 0.7f + 1f) * degrees);
    }
}

/// <summary>Fireflies drifting and pulsing in the space between the board and the trees</summary>
public class JungleFireflies : MonoBehaviour
{
    private void Start()
    {
        var holder = new GameObject("Fireflies");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(0, -8f, 0);
        var ps = holder.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 1f, 0.3f, 1f), new Color(0.3f, 1f, 0.85f, 1f));
        main.maxParticles = 220;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 22f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut; // around the board, never in front of it
        shape.radius = 38f;
        shape.donutRadius = 16f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.2f;
        noise.frequency = 0.25f;
        // Pulse on and off over their lives
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.15f, 0.35f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0.2f, 0.75f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = NeonTheme.Circle.texture };
        renderer.sharedMaterial = material;
        ps.Play();
    }
}

/// <summary>Low mist drifting over the pond</summary>
public class JungleMist : MonoBehaviour
{
    private void Start()
    {
        var holder = new GameObject("Mist");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(0f, JungleScenery.WaterY + 3f, 0f);
        var ps = holder.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(25f, 40f);
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(25f, 50f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(5f, 10f);
        main.startSizeZ = 1f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.9f, 0.8f, 0.06f), new Color(0.5f, 0.4f, 1f, 0.05f));
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 1.8f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 60f;
        shape.donutRadius = 30f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = NeonTheme.Circle.texture };
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        ps.Play();
    }
}
