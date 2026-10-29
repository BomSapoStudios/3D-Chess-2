using UnityEngine;

/// <summary>
/// Which sound a game event makes, so the game code only says what happened. Zombies! swaps in
/// its own sounds for zombie steps, bites and kills.
/// </summary>
public static class GameSounds
{
    private static bool IsZombie(ChessPiece p) => ZombieMode.Active && p != null && p.pieceColor == ZombieMode.ZombieColor;

    /// <summary>
    /// A move lands with a tone that belongs to its square (a zombie shuffles instead), in the
    /// chord of this move's bar (see Harmony), so a game sounds like a song. On cubes up to 8x8x8:
    /// the plane (height) picks the zap's note, climbing the chord; the row (depth) picks the soft
    /// note on top, one to three chord notes above it; a soft bass plays the chord's root; and the
    /// sound is panned to where the square is on screen, whichever way the cube is turned.
    /// </summary>
    public static void MoveLanded(ChessPiece piece, BoardPosition to)
    {
        if (IsZombie(piece)) { Sfx.Play("zombie_step", 0.8f); return; }
        Harmony.Beat();
        float pan = ScreenPan(piece.transform.position);
        int zap = Harmony.Tone(to.y, -5);
        if (zap > 12) zap -= 12; // tall cubes: back down an octave
        Sfx.Play("move", 1f, zap, vary: false, pan: pan);
        Sfx.Play("move_chime", 0.3f, Harmony.Tone(1 + to.z % 3, zap + 1), vary: false, pan: pan);
        Sfx.Play("move_bass", 0.5f, Harmony.RootShift, vary: false);
    }

    /// <summary>A piece picked up: a soft note from the chord that's sounding</summary>
    public static void Pick(float volume = 1f) => Sfx.Play("piece_pick", volume, Harmony.Tone(1, 0), vary: false);

    /// <summary>A piece set down in setup: each placement is a bar, pitched by its plane</summary>
    public static void Placed(int plane)
    {
        Harmony.Beat();
        Sfx.Play("piece_place", 1f, Harmony.Tone(plane, -5), vary: false);
        Sfx.Play("move_bass", 0.35f, Harmony.RootShift, vary: false);
    }

    /// <summary>Auto-place: each piece one note higher up the chord</summary>
    public static void Ripple(int i) => Sfx.Play("piece_place", 0.7f, Harmony.Tone(i % 7, -5), vary: false);

    /// <summary>A capture, landing on the root of this move's chord</summary>
    public static void Capture(string sound)
    {
        if (sound != "capture") { Sfx.Play(sound); return; }
        Harmony.Beat();
        Sfx.Play(sound, 1f, Harmony.RootShift, vary: false);
    }

    /// <summary>It's your move again: two pings on the chord's root and fifth</summary>
    public static void YourTurn() => Sfx.Play("your_turn", 1f, Harmony.RootShift, vary: false);

    /// <summary>-0.7 (left of the screen) to 0.7 (right)</summary>
    private static float ScreenPan(Vector3 world)
    {
        Camera cam = Camera.main;
        if (cam == null) return 0f;
        float x = cam.WorldToViewportPoint(world).x;
        return Mathf.Clamp((x - 0.5f) * 1.4f, -0.7f, 0.7f);
    }

    /// <summary>The sound of a piece being taken</summary>
    public static string CaptureSound(ChessPiece victim, ChessPiece attacker) =>
        IsZombie(attacker) ? "zombie_bite" : IsZombie(victim) ? "zombie_kill" : "capture";

    /// <summary>The game is over: a win, a loss or a draw (Zombies! and Queen Blitz have their own)</summary>
    public static void GameOver(Color outcome)
    {
        if (QueenBlitz.Active) Sfx.Play(QueenBlitz.Cleared ? "blitz_cleared" : "blitz_fail");
        else if (ZombieMode.Active) Sfx.Play("overrun");
        else if (outcome == NeonTheme.Lime) Sfx.Play("win");
        else if (outcome == NeonTheme.Pink) Sfx.Play("lose");
        else Sfx.Play("draw");
    }

    // Queen Blitz: each pawn in one sweep pops one note higher than the last
    private static float lastPop = -10f;
    private static int popStep;

    /// <summary>A pawn knocked flying by the queen (climbing the chord of this slide's bar)</summary>
    public static void PawnSwept()
    {
        float now = Time.unscaledTime;
        popStep = now - lastPop > 0.7f ? 0 : popStep + 1;
        lastPop = now;
        Sfx.Play("blitz_pop", 0.9f, Harmony.Tone(popStep, 0), vary: false);
    }

    /// <summary>The queen sets off: a new bar, then her slide</summary>
    public static void QueenSlide(bool ice)
    {
        Harmony.Beat();
        Sfx.Play("move_bass", 0.4f, Harmony.RootShift, vary: false);
        Sfx.Play(ice ? "ice_slide" : "blitz_slide");
    }

    /// <summary>One earned pawn arrives in the tally (they climb as they come in)</summary>
    public static void PawnBanked(int n) => Sfx.Play("pawn_ding", 0.7f, Harmony.Tone(n % 7, 0), vary: false);
}
