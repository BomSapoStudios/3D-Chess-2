using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helpers for the in-game OnGUI controls on touch screens:
/// - scales layouts authored at 160 dpi up for high-density phone screens
/// - keeps them inside the safe area (notch, home indicator)
/// - remembers where buttons were drawn so a tap on a button is not also
///   treated as a tap on the 3D board behind it
/// </summary>
public static class TouchGUI
{
    private struct BlockedRect
    {
        public Rect screenRect;
        public int frame;
    }

    private static readonly List<BlockedRect> blockedRects = new List<BlockedRect>();
    private static float currentScale = 1f;
    private static Vector2 currentOffset;

    // Layout size in GUI units of a landscape iPhone, which the in-game screens were designed on
    private const float PhoneWidth = 880f;
    private const float PhoneHeight = 400f;

    public static float Scale
    {
        get
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            float byDpi = Mathf.Clamp(dpi / 160f, 1f, 3f);
            // Bigger screens (iPad, a folding phone opened up) would otherwise show phone-sized
            // controls in a sea of space: grow until the layout is about an iPhone's width
            Rect safe = Screen.safeArea;
            float bySize = Mathf.Min(safe.width / PhoneWidth, safe.height / PhoneHeight);
            return Mathf.Max(byDpi, bySize);
        }
    }

    /// <summary>
    /// Start drawing in scaled, safe-area coordinates. Returns the usable width and height.
    /// zoom draws everything bigger still (Larger Text, for guides and result panels).
    /// </summary>
    public static Vector2 Begin(float zoom = 1f)
    {
        currentScale = Scale * zoom;
        if (NeonTheme.BodyFont != null) GUI.skin.font = NeonTheme.BodyFont;
        Rect safe = Screen.safeArea;
        currentOffset = new Vector2(safe.x, Screen.height - safe.yMax);
        GUI.matrix = Matrix4x4.TRS(new Vector3(currentOffset.x, currentOffset.y, 0f), Quaternion.identity, new Vector3(currentScale, currentScale, 1f));
        return new Vector2(safe.width / currentScale, safe.height / currentScale);
    }

    public static void End()
    {
        GUI.matrix = Matrix4x4.identity;
    }

    /// <summary>
    /// GUI.Button that also blocks board taps underneath it
    /// </summary>
    public static bool Button(Rect rect, string text)
    {
        Block(rect);
        return GUI.Button(rect, text);
    }

    public static bool Button(Rect rect, string text, GUIStyle style)
    {
        Block(rect);
        return GUI.Button(rect, text, style);
    }

    /// <summary>
    /// Neon-styled button (see NeonTheme) that also blocks board taps underneath it
    /// </summary>
    public static bool NeonButton(Rect rect, string text, Color fill, Color shadow, bool outlined = false, int fontSize = 15)
    {
        Block(rect);
        return NeonTheme.GUIButton(rect, text, fill, shadow, outlined, fontSize);
    }

    /// <summary>
    /// Mark a rect (in the coordinates passed to GUI calls since Begin) as covered by UI
    /// </summary>
    public static void Block(Rect rect)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        float x = currentOffset.x + rect.x * currentScale;
        float yTop = currentOffset.y + rect.y * currentScale;
        float w = rect.width * currentScale;
        float h = rect.height * currentScale;
        // GUI space is y-down from the top; touch positions are y-up from the bottom
        Rect screenRect = new Rect(x, Screen.height - yTop - h, w, h);
        blockedRects.Add(new BlockedRect { screenRect = screenRect, frame = Time.frameCount });
    }

    /// <summary>
    /// True when a screen position (touch or mouse, y-up) is over a button drawn recently
    /// </summary>
    public static bool IsOverUI(Vector2 screenPosition)
    {
        blockedRects.RemoveAll(b => Time.frameCount - b.frame > 2);
        foreach (BlockedRect b in blockedRects)
        {
            if (b.screenRect.Contains(screenPosition)) return true;
        }
        return false;
    }
}
