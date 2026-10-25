using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Color-Blind Symbols (Accessibility): each player has a shape as well as a color, so sides can be
/// told apart without seeing color. White circle, Black triangle, Green square, Purple diamond,
/// Yellow star, Orange cross. Shapes are drawn white with a dark rim, to be tinted with the
/// player's color. With the option on, Green, Yellow and Orange are also pushed further apart.
/// </summary>
public static class PlayerSymbols
{
    public enum Shape { Circle, Triangle, Square, Diamond, Star, Cross }

    public static bool On => AccessibilityPrefs.ColorBlindSymbols;

    public static Shape For(PieceColor color) => (Shape)(int)color;

    public static string Name(PieceColor color) => For(color).ToString().ToLowerInvariant();

    // ── Colors ──

    /// <summary>
    /// A player's color with the option's nudges: Green turns teal, Yellow pales to lemon and
    /// Orange deepens toward red, so the three differ in lightness and not only hue
    /// </summary>
    public static Color Nudge(PieceColor player, Color original)
    {
        if (!On) return original;
        float a = original.a;
        Color.RGBToHSV(original, out float h, out float s, out float v);
        Color c;
        switch (player)
        {
            case PieceColor.Green: c = Color.HSVToRGB(0.45f, Mathf.Max(s, 0.6f), Mathf.Clamp(v, 0.45f, 0.85f)); break;  // teal, a little darker
            case PieceColor.Yellow: c = Color.HSVToRGB(0.16f, Mathf.Min(s, 0.55f), 1f); break;                           // pale lemon, the lightest
            case PieceColor.Orange: c = Color.HSVToRGB(0.045f, Mathf.Max(s, 0.9f), Mathf.Clamp(v, 0.55f, 0.85f)); break; // red-orange, deeper
            default: return original;
        }
        c.a = a;
        return c;
    }

    /// <summary>The color a symbol is drawn in: the neon tint, nudged</summary>
    public static Color Tint(PieceColor player)
    {
        Color c = player == PieceColor.White ? Color.white
            : player == PieceColor.Black ? NeonTheme.Pink
            : PieceSets.Tint(PieceSets.Kind.NeonGlow, player);
        return Nudge(player, c);
    }

    // ── Pictures ──

    private static readonly Dictionary<Shape, Texture2D> textures = new Dictionary<Shape, Texture2D>();
    private static readonly Dictionary<Shape, Sprite> sprites = new Dictionary<Shape, Sprite>();
    private static Texture2D ring;

    /// <summary>White shape with a dark rim (tint it with the player's color)</summary>
    public static Texture2D Texture(PieceColor color) => Texture(For(color));

    public static Texture2D Texture(Shape shape)
    {
        if (textures.TryGetValue(shape, out Texture2D tex) && tex != null) return tex;
        tex = Draw(p => Distance(shape, p), 0.13f);
        tex.name = "Symbol " + shape;
        textures[shape] = tex;
        return tex;
    }

    public static Sprite Sprite(PieceColor color) => Sprite(For(color));

    public static Sprite Sprite(Shape shape)
    {
        if (sprites.TryGetValue(shape, out Sprite s) && s != null) return s;
        Texture2D tex = Texture(shape);
        s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        sprites[shape] = s;
        return s;
    }

    /// <summary>A thick ring with a dark rim, for marking a square or piece without relying on color</summary>
    public static Texture2D Ring
    {
        get
        {
            if (ring == null) ring = Draw(p => Mathf.Abs(p.magnitude - 0.72f) - 0.13f, 0.08f);
            return ring;
        }
    }

    private const int Size = 96;

    /// <summary>Rasterize a signed distance (shape units: -1..1 across the picture) with a dark rim</summary>
    private static Texture2D Draw(System.Func<Vector2, float> sdf, float rim)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float px = 2f / Size; // one pixel in shape units
        Color rimColor = new Color(0.04f, 0f, 0.1f, 1f);
        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            Vector2 p = new Vector2((x + 0.5f) * px - 1f, (y + 0.5f) * px - 1f);
            float d = sdf(p);
            float fill = Mathf.Clamp01(0.5f - d / px);              // inside the shape
            float outer = Mathf.Clamp01(0.5f - (d - rim) / px);     // inside shape + rim
            Color c = Color.Lerp(rimColor, Color.white, fill);
            c.a = outer;
            pixels[y * Size + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>Signed distance to each shape, sized to leave room for the rim</summary>
    private static float Distance(Shape shape, Vector2 p)
    {
        switch (shape)
        {
            case Shape.Circle: return p.magnitude - 0.62f;
            case Shape.Square: return Box(p, new Vector2(0.56f, 0.56f), 0.06f);
            case Shape.Diamond: return (Mathf.Abs(p.x) + Mathf.Abs(p.y) - 0.8f) * 0.7071f;
            case Shape.Triangle: return Triangle(p + new Vector2(0f, 0.14f), 0.74f);
            case Shape.Star: return Star(p + new Vector2(0f, 0.06f), 0.84f, 0.45f);
            default: return Mathf.Min(Box(p, new Vector2(0.7f, 0.22f), 0.03f), Box(p, new Vector2(0.22f, 0.7f), 0.03f));
        }
    }

    private static float Box(Vector2 p, Vector2 half, float round)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(round, round);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - round;
    }

    // Equilateral triangle pointing up (after Inigo Quilez's distance functions)
    private static float Triangle(Vector2 p, float r)
    {
        const float k = 1.7320508f;
        p.x = Mathf.Abs(p.x) - r;
        p.y = p.y + r / k;
        if (p.x + k * p.y > 0f) p = new Vector2(p.x - k * p.y, -k * p.x - p.y) / 2f;
        p.x -= Mathf.Clamp(p.x, -2f * r, 0f);
        return -p.magnitude * Mathf.Sign(p.y);
    }

    // Five-pointed star pointing up
    private static float Star(Vector2 p, float r, float inner)
    {
        Vector2 k1 = new Vector2(0.809016994f, -0.587785252f);
        Vector2 k2 = new Vector2(-k1.x, k1.y);
        p.x = Mathf.Abs(p.x);
        p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
        p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
        p.x = Mathf.Abs(p.x);
        p.y -= r;
        Vector2 ba = inner * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
        float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
        return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
    }

    // ── IMGUI ──

    /// <summary>Draw a player's symbol in an IMGUI rect (only when the option is on)</summary>
    public static void DrawGUI(Rect rect, PieceColor player)
    {
        if (!On || Event.current.type != EventType.Repaint) return;
        Color old = GUI.color;
        GUI.color = Tint(player);
        GUI.DrawTexture(rect, Texture(player), ScaleMode.ScaleToFit, true);
        GUI.color = old;
    }
}
