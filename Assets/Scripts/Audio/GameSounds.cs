using UnityEngine;

/// <summary>
/// Which sound a game event makes, so the game code only says what happened. Zombies! swaps in
/// its own sounds for zombie steps, bites and kills.
/// </summary>
public static class GameSounds
{
    private static bool IsZombie(ChessPiece p) => ZombieMode.Active && p != null && p.pieceColor == ZombieMode.ZombieColor;

    /// <summary>A move lands: the zap, higher on higher planes (a zombie shuffles instead)</summary>
    public static void MoveLanded(ChessPiece piece, BoardPosition to)
    {
        if (IsZombie(piece)) Sfx.Play("zombie_step", 0.8f);
        else Sfx.OnPlane("move", to.y);
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
