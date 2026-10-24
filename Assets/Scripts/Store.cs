using UnityEngine;

/// <summary>
/// What's unlocked on this phone, and what things cost. Cyberpunk pieces and the Neon Glow
/// backdrop are free; every other piece set and backdrop unlocks for pawns (PawnBank) or, once
/// in-app purchases are set up, for money. Ownership is saved on this phone.
/// </summary>
public static class Store
{
    // ── Prices (change these to retune) ──
    public static int PieceSetPawns => PawnBank.PieceSetPrice;   // 4,500
    public const string PieceSetDollars = "$2.99";
    public const int BackdropPawns = 1500;                        // placeholder until Brandon confirms
    public const string BackdropDollars = "$0.99";                // placeholder until Brandon confirms

    public static string Id(PieceShapes.Kind set) => "set." + set;
    public static string Id(Backdrops.Kind backdrop) => "backdrop." + backdrop;

    public static bool Free(PieceShapes.Kind set) => set == PieceShapes.Kind.Cyberpunk;
    public static bool Free(Backdrops.Kind backdrop) => backdrop == Backdrops.Kind.NeonGlow;

    public static bool Owns(PieceShapes.Kind set) => Free(set) || TestUnlockAll || PlayerPrefs.GetInt("Owned." + Id(set), 0) == 1;
    public static bool Owns(Backdrops.Kind backdrop) => Free(backdrop) || TestUnlockAll || PlayerPrefs.GetInt("Owned." + Id(backdrop), 0) == 1;

    /// <summary>Spend pawns to unlock; false (nothing spent) if there aren't enough</summary>
    public static bool BuyWithPawns(string id, int price)
    {
        if (!PawnBank.Spend(price)) return false;
        PlayerPrefs.SetInt("Owned." + id, 1);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>
    /// For testing: everything counts as unlocked (nothing is bought). Toggled by tapping the
    /// pawn balance in Customize seven times quickly.
    /// </summary>
    public static bool TestUnlockAll
    {
        get => PlayerPrefs.GetInt("StoreTestUnlockAll", 0) == 1;
        set { PlayerPrefs.SetInt("StoreTestUnlockAll", value ? 1 : 0); PlayerPrefs.Save(); }
    }
}
