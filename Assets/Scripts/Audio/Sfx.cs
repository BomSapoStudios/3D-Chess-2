using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sound effects. Clips live in Resources/Audio/Sfx (made by Tools/Sound/make_sounds.py); a name
/// with takes (move_1, move_2, move_3) picks one at random. Every sound gets a small random pitch
/// shift so repeats don't sound copied. Respects the Sound switches; the iPhone's silent switch
/// mutes everything (the app uses the Ambient audio session).
/// </summary>
public static class Sfx
{
    // Semitone steps of D minor pentatonic, for sounds that climb (planes, sweeps, tallies)
    private static readonly int[] Scale = { 0, 3, 5, 7, 10 };

    /// <summary>Semitones for the n-th step up the scale (0 = the root)</summary>
    public static int Step(int n)
    {
        if (n < 0) n = 0;
        return 12 * (n / Scale.Length) + Scale[n % Scale.Length];
    }

    /// <summary>Plays a sound. semitones moves it up or down in pitch; vary adds the small random shift</summary>
    public static void Play(string name, float volume = 1f, float semitones = 0f, bool vary = true)
    {
        if (!SoundPrefs.Effects) return;
        AudioClip clip = Pick(name);
        if (clip == null) return;
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(name, out float last) && now - last < 0.03f) return; // same sound twice in one beat
        lastPlayed[name] = now;
        float pitch = Mathf.Pow(2f, semitones / 12f) * (vary ? Random.Range(0.97f, 1.03f) : 1f);
        Host.Play(clip, volume * SoundPrefs.EffectsVolume, pitch);
    }

    /// <summary>Plays a sound pitched by the board plane it happens on: higher plane, higher note</summary>
    public static void OnPlane(string name, int plane, float volume = 1f)
    {
        Play(name, volume, Step(plane) - 5, vary: false);
    }

    /// <summary>The sound for a menu button: the blob bloop</summary>
    public static void Tap() => Play("ui_tap", 0.8f);

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

    public void Play(AudioClip clip, float volume, float pitch)
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
        s.Play();
    }
}
