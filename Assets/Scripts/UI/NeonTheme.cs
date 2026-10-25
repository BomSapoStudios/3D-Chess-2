using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared look for the "wacky quirky neon" style: palette, procedural sprites and
/// helpers for chunky tilted buttons with hard offset shadows.
/// </summary>
public static class NeonTheme
{
    public static readonly Color Ground = Hex("14002E");
    public static readonly Color Pink = Hex("FF2E9A");
    public static readonly Color PinkSoft = Hex("FF8FC7");
    public static readonly Color Lime = Hex("C6FF3D");
    public static readonly Color Cyan = Hex("29F0FF");
    public static readonly Color Yellow = Hex("FFE14D");
    public static readonly Color Lavender = Hex("C9B8F0");
    private static readonly Color MutedBase = Hex("5A3A8C");
    private static readonly Color MutedBright = Hex("A08ED0");

    /// <summary>Dim purple for things that are off or unavailable (lighter with High Contrast, to stay readable)</summary>
    public static Color Muted => AccessibilityPrefs.HighContrast ? MutedBright : MutedBase;
    public static readonly Color White = Color.white;

    // Fonts (Google Fonts, SIL Open Font License; files and licenses in Resources/Fonts)
    private static Font displayFont, bodyFont;
    private static TMP_FontAsset displayTMP, bodyTMP;

    /// <summary>Bungee: chunky display face for titles</summary>
    public static Font DisplayFont => displayFont != null ? displayFont : (displayFont = Resources.Load<Font>("Fonts/Bungee-Regular"));

    /// <summary>Fredoka SemiBold: rounded face for buttons and body text</summary>
    public static Font BodyFont => bodyFont != null ? bodyFont : (bodyFont = Resources.Load<Font>("Fonts/Fredoka-SemiBold"));

    public static TMP_FontAsset DisplayTMP => displayTMP != null ? displayTMP : (displayTMP = MakeTMP(DisplayFont));
    public static TMP_FontAsset BodyTMP => bodyTMP != null ? bodyTMP : (bodyTMP = MakeTMP(BodyFont));

    private static TMP_FontAsset MakeTMP(Font font)
    {
        if (font == null) return null;
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
        if (asset != null && TMP_Settings.defaultFontAsset != null)
        {
            // Characters these fonts lack (like the superscript in "4³") fall back to the default font
            asset.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
        }
        return asset;
    }

    /// <summary>Use the neon body (or display) font on a TextMeshPro label</summary>
    public static void ApplyFont(TMP_Text text, bool display = false)
    {
        TMP_FontAsset asset = display ? DisplayTMP : BodyTMP;
        if (asset == null) return;
        text.font = asset;
        text.fontStyle = FontStyles.Normal; // the fonts are already heavy; no synthetic bold
    }

    private static Sprite rounded;
    private static Sprite roundedOutline;
    private static Sprite circle;

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    /// <summary>Filled rounded rectangle, 9-sliced so it stretches to any size</summary>
    public static Sprite Rounded
    {
        get
        {
            if (rounded == null) rounded = MakeRounded(18f, false);
            return rounded;
        }
    }

    /// <summary>Rounded rectangle outline (3px stroke), 9-sliced</summary>
    public static Sprite RoundedOutline
    {
        get
        {
            if (roundedOutline == null) roundedOutline = MakeRounded(18f, true);
            return roundedOutline;
        }
    }

    public static Sprite Circle
    {
        get
        {
            if (circle == null)
            {
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                float r = size / 2f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
                }
                tex.Apply();
                circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
            return circle;
        }
    }

    private static Sprite squareOutline;

    /// <summary>Sharp-cornered square outline, for drawing board levels</summary>
    public static Sprite SquareOutline
    {
        get
        {
            if (squareOutline == null) squareOutline = MakeRounded(1f, true);
            return squareOutline;
        }
    }

    private static Sprite MakeRounded(float radius, bool outlineOnly)
    {
        const int size = 64;
        const float stroke = 4f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Signed distance to a rounded rectangle filling the texture
            float px = Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius);
            float py = Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius);
            float outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude + Mathf.Min(Mathf.Max(px, py), 0) - radius;
            float alpha = Mathf.Clamp01(0.5f - outside);
            if (outlineOnly) alpha *= Mathf.Clamp01(outside + stroke + 0.5f);
            tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        tex.Apply();
        float b = Mathf.Max(radius, stroke) + 2f;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    /// <summary>
    /// Style an Image as a chunky neon button face: rounded fill, dark border, hard offset shadow
    /// </summary>
    public static void StyleFilled(Image image, Color fill, Color shadow, float tiltDegrees = 0f)
    {
        image.sprite = Rounded;
        image.type = Image.Type.Sliced;
        image.color = fill;
        AddEffects(image.gameObject, Ground, shadow);
        image.rectTransform.localEulerAngles = new Vector3(0, 0, tiltDegrees);
    }

    /// <summary>Unselected chip: transparent with a colored border, no shadow</summary>
    public static void StyleOutline(Image image, Color border, float tiltDegrees = 0f)
    {
        image.sprite = RoundedOutline;
        image.type = Image.Type.Sliced;
        image.color = border;
        RemoveEffects(image.gameObject);
        image.rectTransform.localEulerAngles = new Vector3(0, 0, tiltDegrees);
    }

    private static void AddEffects(GameObject go, Color border, Color shadow)
    {
        RemoveEffects(go);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = border;
        outline.effectDistance = new Vector2(3f, -3f);
        var drop = go.AddComponent<Shadow>();
        drop.effectColor = shadow;
        drop.effectDistance = new Vector2(5f, -5f);
    }

    private static void RemoveEffects(GameObject go)
    {
        foreach (var effect in go.GetComponents<Shadow>()) Object.Destroy(effect); // Outline derives from Shadow
    }

    /// <summary>
    /// Button colors that only darken slightly on press, so the Image's own color shows as-is
    /// </summary>
    public static void NeutralTint(Button button)
    {
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
        button.colors = colors;
    }

    // IMGUI styles for the in-game OnGUI controls

    private static Texture2D solid;
    private static Texture2D Solid
    {
        get
        {
            if (solid == null)
            {
                solid = new Texture2D(1, 1);
                solid.SetPixel(0, 0, Color.white);
                solid.Apply();
            }
            return solid;
        }
    }

    /// <summary>
    /// Draw a neon button with IMGUI: hard offset shadow, dark border, colored face.
    /// Returns true when clicked.
    /// </summary>
    public static bool GUIButton(Rect rect, string text, Color fill, Color shadow, bool outlined = false, int fontSize = 15)
    {
        Color prev = GUI.color;
        if (!outlined)
        {
            GUI.color = shadow;
            GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), Solid, ScaleMode.StretchToFill, true, 0, shadow, 0, 10);
            GUI.color = prev;
            GUI.DrawTexture(rect, Solid, ScaleMode.StretchToFill, true, 0, fill, 0, 10);
            GUI.DrawTexture(rect, Solid, ScaleMode.StretchToFill, true, 0, Ground, 2.5f, 10);
        }
        else
        {
            GUI.DrawTexture(rect, Solid, ScaleMode.StretchToFill, true, 0, new Color(Ground.r, Ground.g, Ground.b, 0.6f), 0, 10);
            GUI.DrawTexture(rect, Solid, ScaleMode.StretchToFill, true, 0, fill, 2.5f, 10);
        }
        GUI.color = prev;

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            fontStyle = BodyFont != null ? FontStyle.Normal : FontStyle.Bold,
            wordWrap = true,
            font = BodyFont
        };
        style.normal.textColor = outlined ? fill : Ground;
        GUI.Label(rect, text, style);

        bool tapped = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        if (tapped) Haptics.Selection();
        return tapped;
    }

    /// <summary>
    /// A label drawn straight over the 3D scene. With High Contrast a dark plate goes behind the
    /// words so small neon text stays readable over any backdrop.
    /// </summary>
    public static void SceneLabel(Rect rect, string text, GUIStyle style)
    {
        if (AccessibilityPrefs.HighContrast && !string.IsNullOrEmpty(text) && Event.current.type == EventType.Repaint)
        {
            var content = new GUIContent(text);
            Vector2 one = style.CalcSize(content);
            float w = Mathf.Min(one.x, rect.width);
            float h = Mathf.Min(style.wordWrap ? style.CalcHeight(content, rect.width) : one.y, rect.height);
            TextAnchor a = style.alignment;
            int col = (int)a % 3, row = (int)a / 3; // 0 left/upper, 1 center/middle, 2 right/lower
            float x = col == 0 ? rect.x : col == 1 ? rect.center.x - w / 2f : rect.xMax - w;
            float y = row == 0 ? rect.y : row == 1 ? rect.center.y - h / 2f : rect.yMax - h;
            Rect plate = new Rect(x - 6f, y - 2f, w + 12f, h + 4f);
            GUI.DrawTexture(plate, Solid, ScaleMode.StretchToFill, true, 0, new Color(Ground.r, Ground.g, Ground.b, 0.88f), 0, 8);
        }
        GUI.Label(rect, text, style);
    }

    /// <summary>Rounded panel with a neon border (no shadow)</summary>
    public static void GUIPanel(Rect rect, Color border, float alpha = 0.92f)
    {
        GUI.DrawTexture(rect, Solid, ScaleMode.StretchToFill, true, 0, new Color(Ground.r, Ground.g, Ground.b, alpha), 0, 14);
        GUI.DrawTexture(rect, Solid, ScaleMode.StretchToFill, true, 0, border, 2.5f, 14);
    }

    public static GUIStyle GUILabelStyle(Color color, int fontSize, bool bold = true, TextAnchor anchor = TextAnchor.MiddleLeft, bool display = false)
    {
        Font font = display ? DisplayFont : BodyFont;
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = bold && font == null ? FontStyle.Bold : FontStyle.Normal,
            alignment = anchor,
            wordWrap = true,
            font = font
        };
        style.normal.textColor = color;
        return style;
    }
}
