using System;
using UnityEngine;

/// <summary>
/// Pawns: the currency players collect in Queen Blitz and Zombie Survival (to spend later in a
/// store for piece sets and puzzle packs). Kept on this phone for now.
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

    /// <summary>Store prices, for when the store is built (not used yet)</summary>
    public const int PieceSetPrice = 4500;
    public const int PuzzlePackPrice = 3000;

    // ── The bank ──

    private const string TotalKey = "Pawns.Total";
    private const string EarnedKey = "Pawns.Earned";   // lifetime pawns collected (spending doesn't lower it)
    private const string ZombieDayKey = "Pawns.ZombieDay";
    private const string ZombieTodayKey = "Pawns.ZombieToday";

    /// <summary>Pawns the player has to spend</summary>
    public static int Total => PlayerPrefs.GetInt(TotalKey, 0);
    /// <summary>Every pawn ever collected</summary>
    public static int LifetimeEarned => PlayerPrefs.GetInt(EarnedKey, 0);

    public static void Earn(int pawns)
    {
        if (pawns <= 0) return;
        PlayerPrefs.SetInt(TotalKey, Total + pawns);
        PlayerPrefs.SetInt(EarnedKey, LifetimeEarned + pawns);
        PlayerPrefs.Save();
    }

    /// <summary>Spend pawns if there are enough; false (nothing spent) if not</summary>
    public static bool Spend(int pawns)
    {
        if (pawns <= 0) return true;
        if (Total < pawns) return false;
        PlayerPrefs.SetInt(TotalKey, Total - pawns);
        PlayerPrefs.Save();
        return true;
    }

    // ── Zombie Survival's daily allowance ──

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>Pawns Zombie Survival has paid today</summary>
    public static int ZombieEarnedToday =>
        PlayerPrefs.GetString(ZombieDayKey, "") == Today ? PlayerPrefs.GetInt(ZombieTodayKey, 0) : 0;

    public static int ZombieLeftToday => Mathf.Max(0, ZombieDailyCap - ZombieEarnedToday);

    /// <summary>Pay pawns earned in Zombie Survival, up to what's left of today's allowance; returns the pawns paid</summary>
    public static int EarnFromZombies(int pawns)
    {
        int pay = Mathf.Min(pawns, ZombieLeftToday);
        if (pay <= 0) return 0;
        PlayerPrefs.SetString(ZombieDayKey, Today);
        PlayerPrefs.SetInt(ZombieTodayKey, ZombieEarnedToday + pay);
        Earn(pay);
        return pay;
    }
}
