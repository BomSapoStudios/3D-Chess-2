using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What's unlocked on this phone, and what things cost. Cyberpunk pieces, the Neon Glow backdrop
/// and the Fade promotion are free; everything else unlocks for pawns (PawnBank) or for money
/// (Purchases, the App Store). Ownership is saved on this phone.
/// </summary>
public static class Store
{
    // ── Prices (change these to retune) ──
    public static int PieceSetPawns => PawnBank.PieceSetPrice;   // 4,500
    public const string PieceSetDollars = "$2.99";
    public const int BackdropPawns = 3000;
    public const string BackdropDollars = "$1.99";

    public const int PromotionPawns = 1000;                       // placeholder until Brandon confirms
    public const string PromotionDollars = "$0.99";               // placeholder until Brandon confirms

    public static string Id(PieceShapes.Kind set) => "set." + set;
    public static string Id(PromotionStyle.Kind promotion) => "promotion." + promotion;
    public static bool Free(PromotionStyle.Kind promotion) => promotion == PromotionStyle.Kind.Fade;
    public static bool Owns(PromotionStyle.Kind promotion) => Free(promotion) || TestUnlockAll || OwnsId(Id(promotion));
    public static string Id(Backdrops.Kind backdrop) => "backdrop." + backdrop;

    public static bool Free(PieceShapes.Kind set) => set == PieceShapes.Kind.Cyberpunk;
    public static bool Free(Backdrops.Kind backdrop) => backdrop == Backdrops.Kind.NeonGlow;

    public static bool Owns(PieceShapes.Kind set) => Free(set) || TestUnlockAll || OwnsId(Id(set));
    public static bool Owns(Backdrops.Kind backdrop) => Free(backdrop) || TestUnlockAll || OwnsId(Id(backdrop));

    /// <summary>Owned on the server (Wallet), or unlocked on this phone before the server kept it</summary>
    public static bool OwnsId(string id) => Wallet.Owns(id, "Owned." + id);

    /// <summary>Unlock for pawns (the server checks the price and the balance); done(null) once it's yours, else a message</summary>
    public static void BuyWithPawns(string id, System.Action<string> done) => Wallet.Unlock(id, done);

    /// <summary>The App Store product ID for an item, e.g. BomSapo.Chess3D.set.abyssglow</summary>
    public static string ProductId(string id) => "BomSapo.Chess3D." + id.ToLowerInvariant();

    /// <summary>Every item that's sold (everything that isn't free)</summary>
    public static IEnumerable<string> PaidIds()
    {
        foreach (PieceShapes.Kind k in PieceShapes.All) if (!Free(k)) yield return Id(k);
        foreach (Backdrops.Kind k in Backdrops.All) if (!Free(k)) yield return Id(k);
        foreach (PromotionStyle.Kind k in PromotionStyle.All) if (!Free(k)) yield return Id(k);
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
