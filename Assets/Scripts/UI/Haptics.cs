using System.Runtime.InteropServices;

/// <summary>A light tap felt when a game option changes (iOS; does nothing elsewhere)</summary>
public static class Haptics
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void _HapticSelection();
    public static void Selection() => _HapticSelection();
#else
    public static void Selection() { }
#endif
}
