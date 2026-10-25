using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Cyber City backdrop: the board hangs high above a neon megacity at night.
/// - CitySky paints a 360° sky with a hazy magenta horizon, a big hologram moon and three
///   layers of skyline with lit windows, neon sign strips and antenna lights, plus a sea of
///   city lights below the horizon.
/// - CityScenery builds real 3D towers in a ring around the board (lit windows, rooftop neon,
///   billboards), flying traffic with light trails, light rain and purple haze.
/// </summary>
public static class CitySky
{
    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        var rng = new System.Random(seed);
        const float horizon = 0.5f;

        // Three skyline layers, far to near: per column, the height of the building top above the horizon,
        // and which building it belongs to (for windows and signs)
        var layers = new[]
        {
            Skyline(width, seed + 1, 10, 26, 0.02f, 0.085f),
            Skyline(width, seed + 2, 14, 34, 0.03f, 0.15f),
            Skyline(width, seed + 3, 20, 46, 0.04f, 0.22f),
        };
        Color[] bodies = { new Color(0.30f, 0.08f, 0.42f), new Color(0.13f, 0.03f, 0.24f), new Color(0.04f, 0.01f, 0.09f) };
        float[] lit = { 0.10f, 0.22f, 0.32f };

        // Sparse stars high up
        var stars = new HashSet<int>();
        for (int k = 0; k < 700; k++) stars.Add(rng.Next((int)(height * 0.22f)) * width + rng.Next(width));

        const float moonU = 0.68f, moonV = 0.2f, moonR = 0.07f;

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color c;

                if (v < horizon)
                {
                    // Night sky: ink at the top, violet, then a hot magenta glow of city light at the horizon
                    float t = Mathf.Pow(v / horizon, 1.8f);
                    c = Lerp3(new Color(0.015f, 0.0f, 0.06f), new Color(0.11f, 0.02f, 0.24f), new Color(0.6f, 0.1f, 0.42f), t);
                    // A thin cyan band of smog right at the horizon
                    c += new Color(0.05f, 0.35f, 0.45f) * Mathf.Exp(-Mathf.Pow((horizon - v) * 60f, 2f)) * 0.6f;
                    if (stars.Contains(y * width + x)) c += Color.white * 0.4f * (1f - t);

                    // Hologram moon: pale pink disc with scanlines and a soft halo
                    float du = Wrap(u - moonU) * 2f, dv = v - moonV;
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    c += new Color(1f, 0.3f, 0.7f) * 0.35f * Mathf.Exp(-d * d / (moonR * moonR * 5f));
                    if (d < moonR)
                    {
                        float scan = (y % 6) < 2 ? 0.75f : 1f;
                        float edge = Mathf.Clamp01((moonR - d) / 0.003f);
                        Color moon = Color.Lerp(new Color(1f, 0.85f, 0.95f), new Color(1f, 0.45f, 0.8f), (dv + moonR) / (2 * moonR)) * scan;
                        c = Color.Lerp(c, moon, edge * 0.9f);
                    }

                    // Skyline layers, far to near
                    for (int L = 0; L < layers.Length; L++)
                    {
                        var layer = layers[L];
                        float top = horizon - layer.height[x];
                        if (v < top) continue;
                        int b = layer.building[x];
                        Color body = bodies[L];
                        // Lit windows on a grid
                        int lx = x - layer.start[b];
                        int ly = y - (int)(top * height);
                        int cw = 4 + L, ch = 5 + L;
                        bool inWindow = (lx % cw) > 0 && (lx % cw) < cw - 1 && (ly % ch) > 1 && (ly % ch) < ch - 1 && ly > 3;
                        if (inWindow && Hash(b * 131 + lx / cw, ly / ch, seed + L) < lit[L])
                        {
                            float pick = Hash(b, ly / ch, seed + 77);
                            Color win = pick < 0.55f ? new Color(1f, 0.82f, 0.45f) : pick < 0.8f ? NeonTheme.Cyan : NeonTheme.Pink;
                            body = Color.Lerp(body, win, 0.55f + 0.2f * L);
                        }
                        // Vertical neon sign strip on some near/mid buildings
                        if (L > 0 && layer.sign[b] != 0)
                        {
                            int sx = layer.start[b] + 2 + (int)(Hash(b, 3, seed) * Mathf.Max(1, layer.width[b] - 6));
                            if (x >= sx && x < sx + 2 + L && ly > 6 && ly < 6 + (int)(layer.height[x] * height * 0.6f))
                                body = layer.sign[b] == 1 ? NeonTheme.Pink : layer.sign[b] == 2 ? NeonTheme.Cyan : NeonTheme.Yellow;
                        }
                        // Rooftop rim light
                        if (ly <= 1) body = Color.Lerp(body, L == 2 ? NeonTheme.Cyan : NeonTheme.Pink, 0.6f);
                        // Farther layers sink into the haze
                        c = Color.Lerp(body, new Color(0.45f, 0.1f, 0.4f), L == 0 ? 0.2f : L == 1 ? 0.06f : 0f);
                    }

                    // Antenna masts with red tip lights on the near layer
                    var near = layers[2];
                    int nb = near.building[x];
                    if (near.antenna[nb] > 0)
                    {
                        int ax = near.start[nb] + near.width[nb] / 2;
                        float roof = horizon - near.height[ax];
                        float tip = roof - near.antenna[nb];
                        if (x == ax && v >= tip && v < roof) c = new Color(0.1f, 0.05f, 0.15f);
                        float tdx = (x - ax) / (float)width * 2f, tdv = v - tip;
                        c += new Color(1f, 0.15f, 0.2f) * Mathf.Exp(-(tdx * tdx + tdv * tdv) / 0.000008f);
                    }

                    // Ground haze rising off the city: thickest at the horizon, thinning upward,
                    // with a ragged, billowing top edge (no flat line) and wispy streaks inside
                    float rise = horizon - v;
                    float hazeTop = HazeHeight(u);
                    if (rise < hazeTop)
                    {
                        float k = 1f - rise / hazeTop;
                        float streak = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * 23f + rise * 180f + Mathf.Sin(u * Mathf.PI * 2f * 4f) * 3f);
                        float smooth = k * k * (3f - 2f * k);   // no kink at the horizon or the top
                        float alpha = smooth * Mathf.Lerp(1f, 0.55f + 0.45f * streak, 1f - k);
                        c = Color.Lerp(c, Haze, alpha);
                    }
                }
                else
                {
                    // Below the horizon: the city at night seen from far above, a sea of lights in haze
                    float below = v - horizon;
                    // The haze thins out downward too, over an uneven distance, so the horizon has no seam
                    float thin = Mathf.Clamp01(below / (HazeHeight(u + 0.37f) * 0.7f));
                    thin = thin * thin * (3f - 2f * thin);
                    c = Color.Lerp(Haze, new Color(0.03f, 0.0f, 0.07f), thin);
                    float density = Mathf.Lerp(0.12f, 0.02f, Mathf.Clamp01(below * 3f));
                    float h = Hash(x / 2, y / 2, seed + 900);
                    // Lights emerge out of the haze: invisible at the horizon, full strength a little below,
                    // with a ragged boundary so there is no straight edge
                    float emerge = Mathf.Clamp01((below - HazeHeight(u + 0.37f) * 0.3f) * 14f);
                    if (h < density && emerge > 0f)
                    {
                        float pick = Hash(x / 2, y / 2, seed + 901);
                        Color light = pick < 0.6f ? new Color(1f, 0.75f, 0.4f) : pick < 0.82f ? NeonTheme.Cyan : NeonTheme.Pink;
                        c = Color.Lerp(c, light, (0.6f * (1f - Mathf.Clamp01(below * 1.5f)) + 0.15f) * emerge);
                    }
                    // Glowing avenues radiating out
                    float avenue = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2f * 9f + below * 14f));
                    if (avenue < 0.025f) c += new Color(1f, 0.5f, 0.3f) * 0.35f * (1f - Mathf.Clamp01(below * 2f)) * Mathf.Clamp01(below * 12f);
                }

                c.a = 1f;
                result[(height - 1 - y) * width + x] = c;
            }
        }
        return result;
    }

    private static readonly Color Haze = new Color(0.3f, 0.07f, 0.27f);
    public static Color HazeColor => Haze;

    // Height of the haze above the horizon at each point around the sky: rolling, uneven billows
    // (whole sine periods so it wraps around 360° without a seam)
    private static float HazeHeight(float u)
    {
        float t = u * Mathf.PI * 2f;
        float billow = 0.5f + 0.22f * Mathf.Sin(t * 3f + 1.1f) + 0.14f * Mathf.Sin(t * 7f + 0.4f)
                     + 0.08f * Mathf.Sin(t * 17f + 2.3f) + 0.05f * Mathf.Abs(Mathf.Sin(t * 41f + 0.9f));
        return 0.015f + 0.1f * Mathf.Clamp01(billow);
    }

    private class Layer
    {
        public float[] height;
        public int[] building;
        public int[] start, width, sign;
        public float[] antenna;
    }

    // A row of buildings around the full 360°: random widths and heights, wrapping seamlessly
    private static Layer Skyline(int width, int seed, int minW, int maxW, float minH, float maxH)
    {
        var rng = new System.Random(seed);
        var layer = new Layer { height = new float[width], building = new int[width] };
        var starts = new List<int>(); var widths = new List<int>(); var signs = new List<int>(); var antennas = new List<float>();
        int x = 0, id = 0;
        while (x < width)
        {
            int w = Math.Min(rng.Next(minW, maxW), width - x);
            float h = minH + (float)Math.Pow(rng.NextDouble(), 1.6) * (maxH - minH);
            // Occasional skyscraper much taller than its neighbours
            if (rng.NextDouble() < 0.08) h = maxH * 1.25f;
            // Stepped top for some buildings
            bool stepped = rng.NextDouble() < 0.3;
            for (int i = 0; i < w; i++)
            {
                float hi = h;
                if (stepped && (i < w / 4 || i > w * 3 / 4)) hi *= 0.85f;
                layer.height[x + i] = hi;
                layer.building[x + i] = id;
            }
            starts.Add(x); widths.Add(w);
            signs.Add(rng.NextDouble() < 0.25 ? rng.Next(1, 4) : 0);
            antennas.Add(rng.NextDouble() < 0.15 ? 0.015f + (float)rng.NextDouble() * 0.03f : 0f);
            x += w; id++;
        }
        layer.start = starts.ToArray(); layer.width = widths.ToArray(); layer.sign = signs.ToArray(); layer.antenna = antennas.ToArray();
        return layer;
    }

    private static Color Lerp3(Color a, Color b, Color c, float t) => t < 0.5f ? Color.Lerp(a, b, t * 2f) : Color.Lerp(b, c, (t - 0.5f) * 2f);

    private static float Wrap(float d) => d > 0.5f ? d - 1f : d < -0.5f ? d + 1f : d;

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}

/// <summary>3D city around the board: towers, rooftop neon, billboards, traffic and rain</summary>
public static class CityScenery
{
    private static Texture2D windows;
    private static Texture2D[] ads;

    public static void Build(Transform root)
    {
        var rng = new System.Random(2077);

        Material facade = Emissive(WindowTexture(), new Color(0.05f, 0.04f, 0.09f), Color.white * 0.55f);
        Material[] neon = { Unlit(NeonTheme.Pink), Unlit(NeonTheme.Cyan), Unlit(NeonTheme.Yellow), Unlit(NeonTheme.Lime) };
        Material red = Unlit(new Color(1f, 0.15f, 0.2f));
        Material[] adMats = new Material[4];
        for (int i = 0; i < adMats.Length; i++) adMats[i] = Billboard(AdTexture(i));

        var towers = new GameObject("Towers").transform;
        towers.SetParent(root, false);

        for (int i = 0; i < 56; i++)
        {
            float angle = (float)(i / 56.0 * Math.PI * 2 + rng.NextDouble() * 0.09);
            float radius = 78f + (float)rng.NextDouble() * 55f;
            float w = 6f + (float)rng.NextDouble() * 8f;
            float d = 6f + (float)rng.NextDouble() * 8f;
            // The board floats high above the city: rooftops sit well below it, rising a
            // little with distance, with the odd skyscraper reaching up toward board level
            // Very uneven rooftops so the far skyline never reads as one flat-topped mass
            float top = -62f + (float)Math.Pow(rng.NextDouble(), 1.4) * 58f;
            if (rng.NextDouble() < 0.12) top += 18f + (float)rng.NextDouble() * 14f;
            float bottom = -140f;
            var tower = Box("Tower", towers, new Vector3(w, top - bottom, d), facade, true);
            Vector3 centre = new Vector3(Mathf.Cos(angle) * radius, (top + bottom) / 2f, Mathf.Sin(angle) * radius);
            tower.position = centre;
            tower.rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + (float)rng.NextDouble() * 20f, 0f);

            // Neon trim around the roof
            if (rng.NextDouble() < 0.55)
            {
                Material trim = neon[rng.Next(neon.Length)];
                float y = (top - bottom) / 2f;
                Part(tower, new Vector3(0, y, d / 2f), new Vector3(w + 0.3f, 0.35f, 0.3f), trim);
                Part(tower, new Vector3(0, y, -d / 2f), new Vector3(w + 0.3f, 0.35f, 0.3f), trim);
                Part(tower, new Vector3(w / 2f, y, 0), new Vector3(0.3f, 0.35f, d + 0.3f), trim);
                Part(tower, new Vector3(-w / 2f, y, 0), new Vector3(0.3f, 0.35f, d + 0.3f), trim);
            }
            // Neon bands partway down some towers
            if (rng.NextDouble() < 0.3)
            {
                Material band = neon[rng.Next(neon.Length)];
                float y = (top - bottom) / 2f - 4f - (float)rng.NextDouble() * 12f;
                Part(tower, new Vector3(0, y, 0), new Vector3(w + 0.25f, 0.25f, d + 0.25f), band);
            }
            // Antenna with a red light
            if (rng.NextDouble() < 0.3)
            {
                float h = 4f + (float)rng.NextDouble() * 8f;
                float y = (top - bottom) / 2f;
                Part(tower, new Vector3(0, y + h / 2f, 0), new Vector3(0.25f, h, 0.25f), facade);
                var tip = Part(tower, new Vector3(0, y + h, 0), new Vector3(0.7f, 0.7f, 0.7f), red);
                tip.gameObject.AddComponent<BlinkLight>().phase = (float)rng.NextDouble() * 3f;
            }
            // Billboard facing the board
            if (rng.NextDouble() < 0.22)
            {
                float bh = 5f + (float)rng.NextDouble() * 4f;
                float y = (top - bottom) / 2f - bh / 2f - 2f - (float)rng.NextDouble() * 8f;
                var ad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.Destroy(ad.GetComponent<Collider>());
                ad.name = "Billboard";
                ad.GetComponent<MeshRenderer>().sharedMaterial = adMats[rng.Next(adMats.Length)];
                ad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ad.transform.SetParent(towers, false);
                Vector3 toBoard = -new Vector3(centre.x, 0f, centre.z).normalized;
                float inset = Mathf.Max(w, d) * 0.5f + 0.4f;
                ad.transform.position = new Vector3(centre.x, bottom + y + (top - bottom) / 2f, centre.z) + toBoard * inset;
                ad.transform.rotation = Quaternion.LookRotation(-toBoard);
                ad.transform.localScale = new Vector3(bh * 1.6f, bh, 1f);
            }
        }

        root.gameObject.AddComponent<CityTraffic>();
        root.gameObject.AddComponent<CityRain>();
        root.gameObject.AddComponent<CityWisps>();
    }

    // ───────── materials and meshes ─────────

    private static Material Emissive(Texture2D tex, Color baseColor, Color emission)
    {
        Material template = Resources.Load<Material>("PieceSets/Glow");
        Material m = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        m.mainTexture = tex;
        m.color = baseColor;
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", tex);
        m.SetColor("_EmissionColor", emission);
        m.SetFloat("_Metallic", 0.4f);
        m.SetFloat("_Glossiness", 0.55f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }

    private static Material Unlit(Color color)
    {
        var m = new Material(Shader.Find("Unlit/Color"));
        m.color = color;
        return m;
    }

    private static Material Billboard(Texture2D tex)
    {
        Shader s = Shader.Find("Unlit/Texture");
        var m = new Material(s != null ? s : Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        return m;
    }

    private static Transform Part(Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        var t = Box("Part", parent, size, mat, false);
        t.localPosition = localPos;
        t.localRotation = Quaternion.identity;
        return t;
    }

    // A box whose UVs are in world units / 4, so the window texture tiles at a constant size
    private static Transform Box(string name, Transform parent, Vector3 size, Material mat, bool worldUV)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = name };
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        Vector3 h = size / 2f;
        void Face(Vector3 n, Vector3 a, Vector3 b, float uw, float vh)
        {
            int start = verts.Count;
            Vector3 c = Vector3.Scale(n, h);
            Vector3 ea = Vector3.Scale(a, h), eb = Vector3.Scale(b, h);
            verts.Add(c - ea - eb); verts.Add(c + ea - eb); verts.Add(c + ea + eb); verts.Add(c - ea + eb);
            float su = worldUV ? uw / 4f : 1f, sv = worldUV ? vh / 4f : 1f;
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(su, 0)); uvs.Add(new Vector2(su, sv)); uvs.Add(new Vector2(0, sv));
            tris.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
        Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    // 4×4 world units of facade: dark panels, a grid of windows, some lit warm, cyan or pink
    private static Texture2D WindowTexture()
    {
        if (windows != null) return windows;
        const int size = 128;
        windows = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
        var rng = new System.Random(11);
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.03f, 0.02f, 0.06f);
        const int cols = 8, rows = 10;
        for (int cx = 0; cx < cols; cx++)
        for (int cy = 0; cy < rows; cy++)
        {
            double roll = rng.NextDouble();
            Color win = roll < 0.62 ? new Color(0.06f, 0.05f, 0.1f)
                      : roll < 0.86 ? new Color(1f, 0.78f, 0.42f)
                      : roll < 0.95 ? NeonTheme.Cyan : NeonTheme.Pink;
            win *= 0.75f + (float)rng.NextDouble() * 0.25f;
            int x0 = cx * size / cols + 3, x1 = (cx + 1) * size / cols - 3;
            int y0 = cy * size / rows + 4, y1 = (cy + 1) * size / rows - 2;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) px[y * size + x] = win;
        }
        windows.SetPixels(px);
        windows.Apply(true);
        return windows;
    }

    // Abstract neon ads: gradient panels with glyph blocks and stripes
    private static Texture2D AdTexture(int style)
    {
        const int w = 160, h = 100;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var rng = new System.Random(300 + style);
        Color[] a = { NeonTheme.Pink, NeonTheme.Cyan, NeonTheme.Yellow, NeonTheme.Lime };
        Color[] b = { new Color(0.35f, 0.05f, 0.6f), new Color(0.05f, 0.15f, 0.45f), new Color(0.8f, 0.2f, 0.4f), new Color(0.05f, 0.35f, 0.3f) };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color c = Color.Lerp(b[style], a[style] * 0.6f, (float)y / h);
            if (y % 4 == 0) c *= 0.8f; // scanlines
            px[y * w + x] = c;
        }
        // Glyph blocks: two rows of chunky "characters"
        for (int row = 0; row < 2; row++)
        {
            int gx = 12;
            while (gx < w - 20)
            {
                int gw = rng.Next(8, 16), gy = 58 - row * 34;
                for (int k = 0; k < 4; k++)
                {
                    int bx = gx + rng.Next(0, gw - 3), by = gy + rng.Next(0, 18), bw = rng.Next(3, gw / 2 + 2), bh = rng.Next(3, 10);
                    for (int y = by; y < Mathf.Min(by + bh, h); y++)
                    for (int x = bx; x < Mathf.Min(bx + bw, w); x++) px[y * w + x] = Color.white;
                }
                gx += gw + 5;
            }
        }
        // Border
        for (int x = 0; x < w; x++) { px[x] = a[style]; px[w + x] = a[style]; px[(h - 1) * w + x] = a[style]; px[(h - 2) * w + x] = a[style]; }
        for (int y = 0; y < h; y++) { px[y * w] = a[style]; px[y * w + 1] = a[style]; px[y * w + w - 1] = a[style]; px[y * w + w - 2] = a[style]; }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }
}

/// <summary>Slow blink for antenna lights</summary>
public class BlinkLight : MonoBehaviour
{
    public float phase;
    private Renderer r;
    private void Start() => r = GetComponent<Renderer>();
    private void Update()
    {
        if (r != null) r.enabled = Mathf.Repeat(AccessibilityPrefs.AmbientTime + phase, 2.4f) < 1.2f;
    }
}

/// <summary>Flying cars circling the towers, each with a light trail</summary>
public class CityTraffic : MonoBehaviour
{
    private struct Car { public Transform t; public float radius, height, speed, angle, bob; }
    private readonly List<Car> cars = new List<Car>();

    private void Start()
    {
        var rng = new System.Random(5);
        Material body = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.9f, 0.95f, 1f) };
        Color[] trails = { NeonTheme.Cyan, NeonTheme.Pink, new Color(1f, 0.3f, 0.25f), NeonTheme.Yellow };
        Material trailMat = new Material(Shader.Find("Sprites/Default"));
        for (int i = 0; i < 26; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = "Flying Car";
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.6f, 0.3f, 1.4f);
            go.GetComponent<MeshRenderer>().sharedMaterial = body;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMat;
            trail.time = 1.6f;
            trail.widthMultiplier = 0.35f;
            Color tc = trails[rng.Next(trails.Length)];
            trail.startColor = tc;
            trail.endColor = new Color(tc.r, tc.g, tc.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float dir = rng.NextDouble() < 0.5 ? 1f : -1f;
            cars.Add(new Car
            {
                t = go.transform,
                radius = 70f + (float)rng.NextDouble() * 45f,
                height = -42f + (float)rng.NextDouble() * 30f,
                speed = dir * (0.05f + (float)rng.NextDouble() * 0.07f),
                angle = (float)(rng.NextDouble() * Math.PI * 2),
                bob = (float)rng.NextDouble() * 6f,
            });
        }
    }

    private void Update()
    {
        for (int i = 0; i < cars.Count; i++)
        {
            Car c = cars[i];
            c.angle += c.speed * AccessibilityPrefs.AmbientDelta;
            cars[i] = c;
            Vector3 p = new Vector3(Mathf.Cos(c.angle) * c.radius, c.height + Mathf.Sin(AccessibilityPrefs.AmbientTime * 0.3f + c.bob) * 1.5f, Mathf.Sin(c.angle) * c.radius);
            Vector3 tangent = new Vector3(-Mathf.Sin(c.angle), 0f, Mathf.Cos(c.angle)) * Mathf.Sign(c.speed);
            c.t.position = p;
            c.t.rotation = Quaternion.LookRotation(tangent);
        }
    }
}

/// <summary>
/// Thin, slow-drifting wisps of pink and violet haze among the rooftops: large, very
/// faint soft puffs, so the city reads through them.
/// </summary>
public class CityWisps : MonoBehaviour
{
    private void Start()
    {
        var holder = new GameObject("Haze Wisps");
        holder.transform.SetParent(transform, false);
        holder.transform.position = new Vector3(0f, -36f, 0f);
        var ps = holder.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(28f, 45f);
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(30f, 60f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(6f, 12f);   // long, thin streaks
        main.startSizeZ = 1f;
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.35f, 0.75f, 0.07f), new Color(0.6f, 0.35f, 1f, 0.05f));
        main.maxParticles = 70;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 2f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut;   // a ring around the board, out among the towers
        shape.radius = 95f;
        shape.donutRadius = 30f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.mainTexture = SoftPuff();
        renderer.sharedMaterial = mat;
        ps.Play();
    }

    // A soft, uneven blob that fades to nothing at the edges
    private static Texture2D SoftPuff()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var rng = new System.Random(9);
        float ox = (float)rng.NextDouble() * 10f, oy = (float)rng.NextDouble() * 10f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float n = Mathf.PerlinNoise(ox + x * 0.09f, oy + y * 0.09f);
            float a = Mathf.Clamp01(1f - r) * (0.5f + 0.5f * n);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        tex.Apply(true);
        return tex;
    }
}

/// <summary>Light neon-tinted rain falling around the board</summary>
public class CityRain : MonoBehaviour
{
    private void Start()
    {
        var holder = new GameObject("Rain");
        holder.transform.SetParent(transform, false);
        holder.transform.position = new Vector3(0f, 30f, 0f);
        var ps = holder.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 2.2f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.9f, 1f, 0.35f), new Color(1f, 0.6f, 0.9f, 0.3f));
        main.maxParticles = 900;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 380f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(90f, 1f, 90f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-2.5f, -2f);
        velocity.y = new ParticleSystem.MinMaxCurve(-32f, -26f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 1f;
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.mainTexture = NeonTheme.Circle.texture;
        renderer.sharedMaterial = mat;
        ps.Play();
    }
}

