using UnityEngine;

/// <summary>
/// The Sound screen's options. Effects and menu music are on unless switched off; music while you
/// play is off unless switched on (chess is thinking time).
/// </summary>
public static class SoundPrefs
{
    /// <summary>Fired after any option changes, so playing music can follow</summary>
    public static event System.Action Changed;

    /// <summary>
    /// Music switch for the whole app. False until the real songs are in: the Music card is hidden
    /// from Settings > Sound and no music plays. Set to true to bring both back.
    /// </summary>
    public static readonly bool MusicReady = false; // readonly, not const, so the hidden code compiles without unreachable-code warnings

    public static bool Effects { get => PlayerPrefs.GetInt("Sound.Effects", 1) == 1; set => SetInt("Sound.Effects", value ? 1 : 0); }
    public static bool Music { get => MusicReady && PlayerPrefs.GetInt("Sound.Music", 1) == 1; set => SetInt("Sound.Music", value ? 1 : 0); }
    public static bool MusicInGames { get => PlayerPrefs.GetInt("Sound.MusicInGames", 0) == 1; set => SetInt("Sound.MusicInGames", value ? 1 : 0); }

    /// <summary>0 quiet, 1 medium, 2 loud</summary>
    public static int EffectsLevel { get => PlayerPrefs.GetInt("Sound.EffectsLevel", 2); set => SetInt("Sound.EffectsLevel", Mathf.Clamp(value, 0, 2)); }
    public static int MusicLevel { get => PlayerPrefs.GetInt("Sound.MusicLevel", 1); set => SetInt("Sound.MusicLevel", Mathf.Clamp(value, 0, 2)); }

    public static float EffectsVolume => Effects ? Level(EffectsLevel) : 0f;
    public static float MusicVolume => Music ? Level(MusicLevel) * 0.6f : 0f; // music sits under the effects

    // Far apart so each step is clearly heard: about -16 dB, -7 dB and full
    private static float Level(int level) => level == 0 ? 0.16f : level == 1 ? 0.45f : 1f;

    private static void SetInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
