using UnityEngine;

/// <summary>
/// Pawns: the currency players collect in Queen Blitz and Zombie Survival and spend on Customize
/// items and puzzle packs. The server keeps each account's pawns (Wallet; its rules in server-cf/src/economy.ts).
///
/// Every number that decides how pawns are earned or spent lives here.
/// </summary>
public static class PawnBank
{
    // ── The rules (change these to retune the economy) ──

    /// <summary>Zombie Survival: pawns for each zombie destroyed (pawn or promoted), by brain size.
    /// A run's total is rounded down, so 3 Walnut kills pay 4.</summary>
    public static float PawnsPerZombieKill(AIDifficulty brain) =>
        brain == AIDifficulty.Hard ? 2f : brain == AIDifficulty.Medium ? 1.5f : 1f;
    /// <summary>Zombie Survival: the most pawns it can pay in one day (local time)</summary>
    public const int ZombieDailyCap = 100;
    /// <summary>Queen Blitz: a puzzle pays its pawns only the first time it's cleared</summary>
    public const bool BlitzPaysFirstClearOnly = true;
    /// <summary>Queen Blitz: what Auto-place costs</summary>
    public const int AutoPlaceCost = 1;

    /// <summary>Piece set price in pawns (the server charges the same: economy.ts)</summary>
    public const int PieceSetPrice = 4500;

    // ── The bank (the server keeps it: Wallet) ──

    /// <summary>Pawns the player has to spend</summary>
    public static int Total => Wallet.Total;
    /// <summary>Every pawn ever collected</summary>
    public static int LifetimeEarned => Wallet.LifetimeEarned;

    /// <summary>A Queen Blitz puzzle's first clear</summary>
    public static void EarnFromBlitz(string puzzleId, int pawns)
    {
        if (pawns > 0) Wallet.EarnBlitz(puzzleId, pawns);
    }

    /// <summary>Queen Blitz Auto-place: false if there isn't a pawn for it</summary>
    public static bool SpendAutoPlace() => Wallet.SpendAutoPlace();

    // ── Zombie Survival's daily allowance (the server's day is UTC) ──

    public static int ZombieLeftToday => Wallet.ZombieLeftToday;

    /// <summary>
    /// A Zombies! run's kills so far are worth `pawns` more: pays up to what's left of today's allowance
    /// and returns the pawns paid
    /// </summary>
    public static int EarnFromZombies(string runId, AIDifficulty brain, int kills, int pawns)
    {
        int pay = Mathf.Min(pawns, ZombieLeftToday);
        Wallet.EarnZombies(runId, brain == AIDifficulty.Hard ? "hard" : brain == AIDifficulty.Medium ? "medium" : "easy", kills, Mathf.Max(0, pay));
        return Mathf.Max(0, pay);
    }

    // ── What the phone kept before the server did (moved over once by Wallet) ──

    public static int PhoneTotal => PlayerPrefs.GetInt("Pawns.Total", 0);
    public static int PhoneLifetime => PlayerPrefs.GetInt("Pawns.Earned", 0);
}
