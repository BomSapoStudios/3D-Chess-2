using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small line icons for the menus, drawn into white textures (tint them with RawImage.color).
/// Shapes are given on a 24-unit grid, like SVG icons.
/// </summary>
public static class NeonIcons
{
    private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    public static Texture2D Rook => Get("rook", p => p.Stroke(true, (8, 3), (10, 3), (10, 5), (14, 5), (14, 3), (16, 3), (16, 7), (14.5f, 9), (14.5f, 15), (17, 18), (17, 21), (7, 21), (7, 18), (9.5f, 15), (9.5f, 9), (8, 7)));

    public static Texture2D Picture => Get("picture", p =>
    {
        p.Stroke(true, (3, 4), (21, 4), (21, 20), (3, 20));
        p.Circle(16, 9, 2);
        p.Stroke(false, (3, 17), (8, 12), (12, 16), (15, 13), (21, 19));
    });

    public static Texture2D Cube => Get("cube", p =>
    {
        p.Stroke(true, (12, 2), (21, 7), (21, 17), (12, 22), (3, 17), (3, 7));
        p.Stroke(false, (21, 7), (12, 12), (3, 7));
        p.Stroke(false, (12, 12), (12, 22));
    });

    public static Texture2D Star => Get("star", p => p.Stroke(true, (12, 3), (14.2f, 8.2f), (20, 9), (15.7f, 12.8f), (17, 18.5f), (12, 15.6f), (7, 18.5f), (8.3f, 12.8f), (4, 9), (9.8f, 8.2f)));

    public static Texture2D Tick => Get("tick", p => { p.width = 4f; p.Stroke(false, (5, 12), (10, 17), (19, 7)); });

    public static Texture2D Pawn => Get("pawn", p =>
    {
        p.FillCircle(12, 6, 3.4f);
        p.Fill((8.6f, 11), (15.4f, 11), (17, 18), (7, 18));
        p.Fill((5.5f, 18), (18.5f, 18), (18.5f, 21.4f), (5.5f, 21.4f));
    });

    /// <summary>An empty pawn: just its outline</summary>
    public static Texture2D PawnOutline => Get("pawn outline", p =>
    {
        p.width = 1.6f;
        p.Circle(12, 6, 3.4f);
        p.Stroke(true, (8.6f, 11), (15.4f, 11), (17, 18), (7, 18));
        p.Stroke(true, (5.5f, 18), (18.5f, 18), (18.5f, 21.4f), (5.5f, 21.4f));
    });

    /// <summary>The board preview: three floors seen from the side, solid or open, with or without posts</summary>
    public static Texture2D CubeDiagram(bool solid, bool posts) => Get($"cube{solid}{posts}", p =>
    {
        p.width = 1.1f;
        for (int f = 0; f < 3; f++)
        {
            float y = 4f + f * 4.2f;
            (float, float)[] floor = { (12, y), (21, y + 3.6f), (12, y + 7.2f), (3, y + 3.6f) };
            if (solid) p.Fill(0.27f, floor);
            p.Stroke(true, floor);
        }
        if (posts)
        {
            p.alpha = 0.8f;
            p.Stroke(false, (3, 7.6f), (3, 16));
            p.Stroke(false, (21, 7.6f), (21, 16));
            p.Stroke(false, (12, 11.2f), (12, 19.6f));
            p.Stroke(false, (12, 4), (12, 7.2f));
        }
    }, 256, 226);

    private static Texture2D Get(string key, System.Action<Painter> draw, int w = 64, int h = 64)
    {
        if (cache.TryGetValue(key, out Texture2D t) && t != null) return t;
        var p = new Painter(w, h);
        draw(p);
        t = p.ToTexture();
        cache[key] = t;
        return t;
    }

    private class Painter
    {
        private readonly int w, h;
        private readonly float[] a;
        private readonly float scale;
        public float width = 2.2f;   // stroke width in grid units
        public float alpha = 1f;

        public Painter(int w, int h)
        {
            this.w = w; this.h = h;
            a = new float[w * h];
            scale = Mathf.Min(w, h) / 24f;
        }

        private Vector2 P((float x, float y) q) => new Vector2((q.x - 12f) * scale + w / 2f, h / 2f - (q.y - 12f) * scale);

        public void Stroke(bool closed, params (float, float)[] pts)
        {
            float half = width * scale / 2f;
            int n = pts.Length;
            for (int i = 0; i < (closed ? n : n - 1); i++)
            {
                Vector2 p0 = P(pts[i]), p1 = P(pts[(i + 1) % n]);
                int x0 = Mathf.Max(0, (int)(Mathf.Min(p0.x, p1.x) - half - 2)), x1 = Mathf.Min(w - 1, (int)(Mathf.Max(p0.x, p1.x) + half + 2));
                int y0 = Mathf.Max(0, (int)(Mathf.Min(p0.y, p1.y) - half - 2)), y1 = Mathf.Min(h - 1, (int)(Mathf.Max(p0.y, p1.y) + half + 2));
                Vector2 d = p1 - p0;
                float len2 = Mathf.Max(d.sqrMagnitude, 1e-4f);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 q = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(q - p0, d) / len2);
                    float dist = Vector2.Distance(q, p0 + d * t);
                    Put(x, y, Mathf.Clamp01(half - dist + 0.5f) * alpha);
                }
            }
        }

        public void Circle(float cx, float cy, float r)
        {
            var pts = new (float, float)[20];
            for (int i = 0; i < pts.Length; i++)
            {
                float ang = i * Mathf.PI * 2f / pts.Length;
                pts[i] = (cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
            }
            Stroke(true, pts);
        }

        public void FillCircle(float cx, float cy, float r)
        {
            var pts = new (float, float)[24];
            for (int i = 0; i < pts.Length; i++)
            {
                float ang = i * Mathf.PI * 2f / pts.Length;
                pts[i] = (cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
            }
            Fill(pts);
        }

        public void Fill(params (float, float)[] pts) => Fill(1f, pts);

        public void Fill(float opacity, params (float, float)[] pts)
        {
            var poly = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) poly[i] = P(pts[i]);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // 2x2 samples for soft edges
                float cover = 0f;
                for (int s = 0; s < 4; s++)
                    if (Inside(poly, new Vector2(x + 0.25f + 0.5f * (s % 2), y + 0.25f + 0.5f * (s / 2)))) cover += 0.25f;
                Put(x, y, cover * opacity);
            }
        }

        private static bool Inside(Vector2[] poly, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > q.y) != (poly[j].y > q.y) && q.x < (poly[j].x - poly[i].x) * (q.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        private void Put(int x, int y, float v)
        {
            int i = y * w + x;
            if (v > a[i]) a[i] = v;
        }

        public Texture2D ToTexture()
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a[y * w + x] * 255f)); // P() already puts grid-top at the texture's top row
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
