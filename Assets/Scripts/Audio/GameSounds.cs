using UnityEngine;

/// <summary>
/// Which sound a game event makes, so the game code only says what happened. Zombies! swaps in
/// its own sounds for zombie steps, bites and kills.
/// </summary>
public static class GameSounds
{
    private static bool IsZombie(ChessPiece p) => ZombieMode.Active && p != null && p.pieceColor == ZombieMode.ZombieColor;

    /// <summary>
    /// A move lands with a tone that belongs to its square (a zombie shuffles instead). On cubes up
    /// to 8x8x8: the plane (height) sets the zap's note, climbing D minor pentatonic; the row
    /// (depth) sets the soft note on top, so each square of a plane has its own two-note chord;
    /// and the sound is panned to where the square is on screen, whichever way the cube is turned.
    /// </summary>
    public static void MoveLanded(ChessPiece piece, BoardPosition to)
    {
        if (IsZombie(piece)) { Sfx.Play("zombie_step", 0.8f); return; }
        float pan = ScreenPan(piece.transform.position);
        Sfx.Play("move", 1f, Sfx.Step(to.y) - 5, vary: false, pan: pan);
        int top = to.y + 2 + to.z;
        if (top > 11) top -= 5; // an octave down, so the top note stays close to the zap
        Sfx.Play("move_chime", 0.3f, Sfx.Step(top) - 5, vary: false, pan: pan);
    }

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

    /// <summary>A pawn knocked flying by the queen</summary>
    public static void PawnSwept()
    {
        float now = Time.unscaledTime;
        popStep = now - lastPop > 0.7f ? 0 : popStep + 1;
        lastPop = now;
        Sfx.Play("blitz_pop", 0.9f, Sfx.Step(popStep), vary: false);
    }

    /// <summary>One earned pawn arrives in the tally (they climb as they come in)</summary>
    public static void PawnBanked(int n) => Sfx.Play("pawn_ding", 0.7f, Sfx.Step(n % 10), vary: false);
}
