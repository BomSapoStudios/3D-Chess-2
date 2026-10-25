using UnityEngine;

/// <summary>
/// The player's accessibility choices (Accessibility screen in the menu). Every visual option is
/// off until the player turns it on, and none follows the iPhone's own settings, so the game looks
/// exactly as it always has unless asked. Haptics is the exception: on unless switched off.
/// </summary>
public static class AccessibilityPrefs
{
    /// <summary>Fired after any option changes, so open screens can restyle themselves</summary>
    public static event System.Action Changed;

    /// <summary>Spins, sweeps and flying pieces become short fades or instant moves; backdrops nearly stop</summary>
    public static bool ReduceMotion { get => Get("A11y.ReduceMotion", false); set => Set("A11y.ReduceMotion", value); }

    /// <summary>Each player gets a symbol (circle, triangle, square, diamond, star, cross) next to their color</summary>
    public static bool ColorBlindSymbols { get => Get("A11y.ColorBlindSymbols", false); set => Set("A11y.ColorBlindSymbols", value); }

    /// <summary>Darker plates behind small neon text and more solid highlights</summary>
    public static bool HighContrast { get => Get("A11y.HighContrast", false); set => Set("A11y.HighContrast", value); }

    /// <summary>Bigger text in the menus, How to Play, Customize, the Store and end panels</summary>
    public static bool LargerText { get => Get("A11y.LargerText", false); set => Set("A11y.LargerText", value); }

    /// <summary>Huge (2x) rather than large (1.5x), when Larger Text is on</summary>
    public static bool HugeText { get => Get("A11y.HugeText", false); set => Set("A11y.HugeText", value); }

    /// <summary>Taps and buzzes on iPhone (on unless switched off)</summary>
    public static bool Haptics { get => Get("A11y.Haptics", true); set => Set("A11y.Haptics", value); }

    /// <summary>How much bigger menu text is drawn: 1 normally, 1.5 or 2 with Larger Text</summary>
    public static float TextScale => LargerText ? (HugeText ? 2f : 1.5f) : 1f;

    /// <summary>Speed for decorative motion (backdrops): 1 normally, almost still with Reduce Motion</summary>
    public static float AmbientSpeed => ReduceMotion ? 0.04f : 1f;

    // Read every frame by backdrops and effects, so kept in memory after the first look
    private static readonly System.Collections.Generic.Dictionary<string, bool> cache = new System.Collections.Generic.Dictionary<string, bool>();

    private static bool Get(string key, bool fallback)
    {
        if (!cache.TryGetValue(key, out bool value))
        {
            value = PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
            cache[key] = value;
        }
        return value;
    }

    private static void Set(string key, bool value)
    {
        if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetInt(key) == (value ? 1 : 0)) return;
        cache[key] = value;
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    // ── Decorative-time clock ──
    // Backdrops read AmbientTime instead of Time.time. It falls behind Time.time only while Reduce
    // Motion is on (so it runs at AmbientSpeed), and keeps its place when the option changes, so
    // nothing jumps. If the option has never been on it is exactly Time.time.
    private static float lag, unscaledLag, lastTime = -1f, lastUnscaled = -1f;
    private static int lagFrame = -1, unscaledFrame = -1;

    /// <summary>Time.time, slowed to a crawl while Reduce Motion is on</summary>
    public static float AmbientTime
    {
        get
        {
            if (lagFrame != Time.frameCount)
            {
                lagFrame = Time.frameCount;
                if (lastTime >= 0f) lag += (Time.time - lastTime) * (1f - AmbientSpeed);
                lastTime = Time.time;
            }
            return Time.time - lag;
        }
    }

    /// <summary>Time.unscaledTime, slowed to a crawl while Reduce Motion is on</summary>
    public static float AmbientUnscaledTime
    {
        get
        {
            if (unscaledFrame != Time.frameCount)
            {
                unscaledFrame = Time.frameCount;
                if (lastUnscaled >= 0f) unscaledLag += (Time.unscaledTime - lastUnscaled) * (1f - AmbientSpeed);
                lastUnscaled = Time.unscaledTime;
            }
            return Time.unscaledTime - unscaledLag;
        }
    }

    /// <summary>Time.deltaTime, slowed to a crawl while Reduce Motion is on</summary>
    public static float AmbientDelta => Time.deltaTime * AmbientSpeed;
}
