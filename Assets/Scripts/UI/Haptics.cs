using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Taps and buzzes on iPhone (does nothing elsewhere). Every call respects the Haptics switch
/// on the Accessibility screen, which is on unless the player turns it off.
/// </summary>
public static class Haptics
{
    /// <summary>A light tick when a game option changes or a button is tapped</summary>
    public static void Selection() { if (On) Native(0); }

    /// <summary>A soft bump: picking up a piece, placing one, a pawn swept away</summary>
    public static void Light() { if (On) Native(1); }

    /// <summary>A firm bump: a move lands, your turn arrives</summary>
    public static void Medium() { if (On) Native(2); }

    /// <summary>A heavy thud: a capture, a zombie bite</summary>
    public static void Heavy() { if (On) Native(3); }

    /// <summary>The "done!" pattern: checkmate won, puzzle cleared, a promotion</summary>
    public static void Success() { if (On) Native(4); }

    /// <summary>The "careful" pattern: check</summary>
    public static void Warning() { if (On) Native(5); }

    /// <summary>The "oh no" pattern: checkmated, overrun</summary>
    public static void Error() { if (On) Native(6); }

    // Light ticks for a burst of events (Queen Blitz sweeps): at most one every 70 ms, and only a few
    // per burst, so a long sweep doesn't buzz nonstop
    private static float tickWindowStart = -10f, lastTick = -10f;
    private static int ticksInWindow;

    /// <summary>A light tick that's dropped when too many come at once</summary>
    public static void Tick()
    {
        if (!On) return;
        float now = Time.unscaledTime;
        if (now - tickWindowStart > 1.2f) { tickWindowStart = now; ticksInWindow = 0; }
        if (now - lastTick < 0.07f || ticksInWindow >= 8) return;
        lastTick = now;
        ticksInWindow++;
        Native(1);
    }

    private static bool On => AccessibilityPrefs.Haptics;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void _HapticSelection();
    [DllImport("__Internal")] private static extern void _HapticImpact(int style);
    [DllImport("__Internal")] private static extern void _HapticNotify(int type);

    private static void Native(int kind)
    {
        switch (kind)
        {
            case 0: _HapticSelection(); break;
            case 1: _HapticImpact(0); break;
            case 2: _HapticImpact(1); break;
            case 3: _HapticImpact(2); break;
            case 4: _HapticNotify(0); break;
            case 5: _HapticNotify(1); break;
            default: _HapticNotify(2); break;
        }
    }
#else
    private static void Native(int kind) { }
#endif
}
