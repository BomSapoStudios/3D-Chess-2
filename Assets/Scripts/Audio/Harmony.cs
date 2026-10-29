using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the game's notes in one chord at a time, so a game played out sounds like a song. Every
/// move (or setup placement, or Queen Blitz slide) is one bar: the chord steps along an eight-bar
/// loop in D minor, Dm Bb F C | Dm Bb C A, and the move, pick, place, capture and your-turn sounds
/// all take their notes from the chord of the bar they fall in. A soft bass note plays each
/// chord's root as the move lands.
/// </summary>
public static class Harmony
{
    // Each chord: its root and its notes, as semitones above D (pitch classes 0-11)
    private struct Chord { public int root; public int[] notes; }

    private static readonly Chord[] Loop =
    {
        new Chord { root = 0,  notes = new[] { 0, 3, 7 } },   // D minor
        new Chord { root = 8,  notes = new[] { 8, 0, 3 } },   // Bb
        new Chord { root = 3,  notes = new[] { 3, 7, 10 } },  // F
        new Chord { root = 10, notes = new[] { 10, 2, 5 } },  // C
        new Chord { root = 0,  notes = new[] { 0, 3, 7 } },   // D minor
        new Chord { root = 8,  notes = new[] { 8, 0, 3 } },   // Bb
        new Chord { root = 10, notes = new[] { 10, 2, 5 } },  // C
        new Chord { root = 7,  notes = new[] { 7, 11, 2 } },  // A (leads home to D minor)
    };

    private static int bar = -1;
    private static float lastBeat = -10f;

    private static Chord Current => Loop[Mathf.Max(bar, 0) % Loop.Length];

    /// <summary>
    /// Moves on to the next chord. Called by everything that starts a bar; calls less than a third
    /// of a second apart (a move landing and the capture it makes) share one bar.
    /// </summary>
    public static void Beat()
    {
        float now = Time.unscaledTime;
        if (now - lastBeat > 0.3f) bar++;
        lastBeat = now;
    }

    /// <summary>The n-th note of the current chord counting up from floor (semitones from D)</summary>
    public static int Tone(int n, int floor)
    {
        if (n < 0) n = 0;
        int[] notes = Current.notes;
        for (int s = floor; ; s++)
        {
            int pc = ((s % 12) + 12) % 12;
            if (System.Array.IndexOf(notes, pc) >= 0 && n-- == 0) return s;
        }
    }

    /// <summary>How far to move a sound written on D so it sits on the current chord's root (-6 to 5)</summary>
    public static int RootShift
    {
        get
        {
            int r = Current.root;
            return r > 5 ? r - 12 : r;
        }
    }

    // Every game starts at the top of the loop
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook() => SceneManager.sceneLoaded += (_, __) => bar = -1;
}
