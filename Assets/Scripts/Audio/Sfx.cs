using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sound effects. Clips live in Resources/Audio/Sfx (made by Tools/Sound/make_sounds.py); a name
/// with takes (move_1, move_2, move_3) picks one at random. Sounds outside the game's chords (see
/// Harmony) get a tiny random pitch shift so repeats don't sound copied. Respects the Sound switches; the iPhone's silent switch
/// mutes everything (the app uses the Ambient audio session).
/// </summary>
public static class Sfx
{
    /// <summary>
    /// Plays a sound. semitones moves it up or down in pitch; vary adds the small random shift;
    /// pan places it between the speakers (-1 left, 1 right)
    /// </summary>
    public static void Play(string name, float volume = 1f, float semitones = 0f, bool vary = true, float pan = 0f)
    {
        if (!SoundPrefs.Effects) return;
        AudioClip clip = Pick(name);
        if (clip == null) return;
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(name, out float last) && now - last < 0.03f) return; // same sound twice in one beat
        lastPlayed[name] = now;
        float pitch = Mathf.Pow(2f, semitones / 12f) * (vary ? Random.Range(0.985f, 1.015f) : 1f);
        Host.Play(clip, volume * SoundPrefs.EffectsVolume, pitch, pan);
    }

    /// <summary>
    /// The sound for a menu button: the blob bloop. Buttons with a PressSound already played it the
    /// moment they were touched, so the tap when the finger lifts is skipped for them.
    /// </summary>
    public static void Tap()
    {
        if (Time.unscaledTime - pressedAt < 0.8f) { pressedAt = -10f; return; }
        Play("ui_tap", 0.7f);
    }

    /// <summary>A button's sound, played as the finger touches it (see PressSound)</summary>
    public static void Press(bool back)
    {
        Play(back ? "ui_back" : "ui_tap", back ? 1f : 0.7f);
        pressedAt = Time.unscaledTime;
    }

    private static float pressedAt = -10f;

    // Every clip is loaded and the voices made at launch, so the first tap of each sound isn't late
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Preload()
    {
        var all = new Dictionary<string, List<AudioClip>>();
        foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio/Sfx"))
        {
            string name = clip.name;
            int us = name.LastIndexOf('_');
            if (us > 0 && int.TryParse(name.Substring(us + 1), out _)) name = name.Substring(0, us); // move_2 -> move
            if (!all.TryGetValue(name, out var list)) all[name] = list = new List<AudioClip>();
            list.Add(clip);
        }
        foreach (var pair in all) clips[pair.Key] = pair.Value.ToArray();
        _ = Host;
    }

    private static readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
    private static readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

    private static AudioClip Pick(string name)
    {
        if (!clips.TryGetValue(name, out AudioClip[] takes))
        {
            var found = new List<AudioClip>();
            AudioClip single = Resources.Load<AudioClip>("Audio/Sfx/" + name);
            if (single != null) found.Add(single);
            for (int i = 1; i <= 6; i++)
            {
                AudioClip take = Resources.Load<AudioClip>($"Audio/Sfx/{name}_{i}");
                if (take == null) break;
                found.Add(take);
            }
            if (found.Count == 0) Debug.LogWarning($"[Sfx] No sound called {name}");
            takes = found.ToArray();
            clips[name] = takes;
        }
        return takes.Length == 0 ? null : takes[Random.Range(0, takes.Length)];
    }

    private static SfxHost host;
    private static SfxHost Host
    {
        get
        {
            if (host == null)
            {
                var go = new GameObject("Sound Effects");
                Object.DontDestroyOnLoad(go);
                host = go.AddComponent<SfxHost>();
            }
            return host;
        }
    }
}

/// <summary>A small pool of audio sources so overlapping sounds don't cut each other off</summary>
public class SfxHost : MonoBehaviour
{
    private const int Voices = 12;
    private readonly AudioSource[] sources = new AudioSource[Voices];
    private int next;

    private void Awake()
    {
        for (int i = 0; i < Voices; i++)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.priority = 64;
            sources[i] = s;
        }
    }

    public void Play(AudioClip clip, float volume, float pitch, float pan)
    {
        if (volume <= 0f) return;
        // a free voice if there is one, otherwise the oldest
        AudioSource s = null;
        for (int i = 0; i < Voices; i++)
        {
            AudioSource candidate = sources[(next + i) % Voices];
            if (!candidate.isPlaying) { s = candidate; next = (next + i + 1) % Voices; break; }
        }
        if (s == null) { s = sources[next]; next = (next + 1) % Voices; }
        s.clip = clip;
        s.volume = volume;
        s.pitch = pitch;
        s.panStereo = pan;
        s.Play();
    }
}
