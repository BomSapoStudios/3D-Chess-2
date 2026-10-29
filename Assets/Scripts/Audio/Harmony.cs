using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the game's notes in one chord at a time, so a game played out sounds like a song. Every
/// move (or setup placement, or Queen Blitz slide) is one bar: the chord steps along an eight-bar
/// loop in D minor, Dm Bb F C | Dm Bb C A, and the move, pick, place, capture and your-turn sounds
/// all take their notes from the chord of the bar they fall in. A soft bass note plays each
/// chord's root as the move lands. Planes climb a note set chosen for the cube's height, with the
/// chord's root on the bottom and top planes (see PlaneNote).
/// </summary>
public static class Harmony
{
    // Each chord: its root, its notes, and the seven-note scale that goes with it, as semitones
    // above D (pitch classes 0-11). The scales are D minor seen from each root, except A's,
    // which takes C# and F# so it sounds major and leads home.
    private struct Chord { public int root; public bool minor; public int[] notes; public int[] scale; }

    private static readonly Chord DMinor = new Chord { root = 0, minor = true, notes = new[] { 0, 3, 7 }, scale = new[] { 0, 2, 3, 5, 7, 8, 10 } };
    private static readonly Chord BFlat = new Chord { root = 8, notes = new[] { 8, 0, 3 }, scale = new[] { 8, 10, 0, 2, 3, 5, 7 } };
    private static readonly Chord F = new Chord { root = 3, notes = new[] { 3, 7, 10 }, scale = new[] { 3, 5, 7, 8, 10, 0, 2 } };
    private static readonly Chord C = new Chord { root = 10, notes = new[] { 10, 2, 5 }, scale = new[] { 10, 0, 2, 3, 5, 7, 8 } };
    private static readonly Chord A = new Chord { root = 7, notes = new[] { 7, 11, 2 }, scale = new[] { 7, 9, 11, 0, 2, 4, 5 } };

    private static readonly Chord[] Loop = { DMinor, BFlat, F, C, DMinor, BFlat, C, A };

    // Which scale steps the planes climb (0 = root, 7 = the octave), by the cube's height. Every set
    // starts and ends on the root, so both players' home planes sound like home:
    // 8 planes, the whole scale; 7, the scale without its 4th (the note most likely to clash);
    // 6, a pentatonic plus the octave; 5, the chord with its 7th, then the octave; 4 or less, the chord.
    private static readonly int[] Steps8 = { 0, 1, 2, 3, 4, 5, 6, 7 };
    private static readonly int[] Steps7 = { 0, 1, 2, 4, 5, 6, 7 };
    private static readonly int[] Steps6Minor = { 0, 2, 3, 4, 6, 7 };
    private static readonly int[] Steps6Major = { 0, 1, 2, 4, 5, 7 };
    private static readonly int[] Steps5 = { 0, 2, 4, 6, 7 };
    private static readonly int[] Steps4 = { 0, 2, 4, 7 };

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

    /// <summary>
    /// The note for a plane of a cube that's planes tall (semitones from D): the bottom plane is the
    /// chord's root, the top plane its octave, and the planes between climb the set for that height
    /// </summary>
    public static int PlaneNote(int plane, int planes)
    {
        Chord c = Current;
        int[] steps = planes >= 8 ? Steps8 : planes == 7 ? Steps7 : planes == 6 ? (c.minor ? Steps6Minor : Steps6Major)
            : planes == 5 ? Steps5 : Steps4;
        int step = steps[Mathf.Clamp(plane, 0, steps.Length - 1)];
        int baseNote = c.root > 2 ? c.root - 12 : c.root; // roots from F below to E above, so every chord climbs about the same range
        if (step == 7) return baseNote + 12;
        return baseNote + (c.scale[step] - c.root + 12) % 12;
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
