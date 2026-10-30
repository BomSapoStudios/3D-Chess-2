using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What's unlocked on this phone, and what things cost. Cyberpunk pieces, the Neon Glow backdrop
/// and the Fade promotion are free; everything else unlocks for pawns (PawnBank) or for money
/// (Purchases, the App Store). Unlock Everything (money only) owns every paid item and Queen Blitz
/// pack, including ones added later. Ownership is saved on this phone.
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
    public const string EverythingDollars = "$19.99";

    /// <summary>
    /// Pawn top-ups (Consumable App Store products, id → pawns). They're only given away with App Store
    /// offer codes (Redeem code): nothing in the game shows them for sale.
    /// </summary>
    public static readonly Dictionary<string, int> PawnProducts = new Dictionary<string, int> { { "pawns.50", 50 }, { "pawns.100", 100 } };

    /// <summary>Unlock Everything: every piece set, backdrop, promotion and Queen Blitz pack, now and later</summary>
    public const string EverythingId = "bundle.everything";
    public static bool OwnsEverything => OwnsId(EverythingId);

    public static string Id(PieceShapes.Kind set) => "set." + set;
    public static string Id(PromotionStyle.Kind promotion) => "promotion." + promotion;
    public static bool Free(PromotionStyle.Kind promotion) => promotion == PromotionStyle.Kind.Fade;
    public static bool Owns(PromotionStyle.Kind promotion) => Free(promotion) || OwnsEverything || OwnsId(Id(promotion));
    public static string Id(Backdrops.Kind backdrop) => "backdrop." + backdrop;

    public static bool Free(PieceShapes.Kind set) => set == PieceShapes.Kind.Cyberpunk;
    public static bool Free(Backdrops.Kind backdrop) => backdrop == Backdrops.Kind.NeonGlow;

    public static bool Owns(PieceShapes.Kind set) => Free(set) || OwnsEverything || OwnsId(Id(set));
    public static bool Owns(Backdrops.Kind backdrop) => Free(backdrop) || OwnsEverything || OwnsId(Id(backdrop));

    /// <summary>Owned on the server (Wallet), or unlocked on this phone before the server kept it</summary>
    public static bool OwnsId(string id) => Wallet.Owns(id, "Owned." + id);

    /// <summary>Unlock for pawns (the server checks the price and the balance); done(null) once it's yours, else a message</summary>
    public static void BuyWithPawns(string id, System.Action<string> done) => Wallet.Unlock(id, done);

    /// <summary>An item's name for messages ("Abyss Glow", "Graveyard", "Everything")</summary>
    public static string DisplayName(string id)
    {
        if (id == EverythingId) return "Everything";
        int dot = id.IndexOf('.');
        string kind = dot > 0 ? id.Substring(0, dot) : "", name = dot > 0 ? id.Substring(dot + 1) : id;
        if (kind == "set" && System.Enum.TryParse(name, out PieceShapes.Kind set)) return PieceShapes.DisplayName(set);
        if (kind == "backdrop" && System.Enum.TryParse(name, out Backdrops.Kind backdrop)) return Backdrops.DisplayName(backdrop);
        if (kind == "promotion" && System.Enum.TryParse(name, out PromotionStyle.Kind promotion)) return PromotionStyle.DisplayName(promotion);
        if (kind == "blitz" && QueenBlitz.Info(name).id == name) return QueenBlitz.Info(name).name + " puzzle pack";
        return name;
    }

    /// <summary>The App Store product ID for an item, e.g. BomSapo.Chess3D.set.abyssglow</summary>
    public static string ProductId(string id) => "BomSapo.Chess3D." + id.ToLowerInvariant();

    /// <summary>Every item that's sold (everything that isn't free)</summary>
    public static IEnumerable<string> PaidIds()
    {
        foreach (PieceShapes.Kind k in PieceShapes.All) if (!Free(k)) yield return Id(k);
        foreach (Backdrops.Kind k in Backdrops.All) if (!Free(k)) yield return Id(k);
        foreach (PromotionStyle.Kind k in PromotionStyle.All) if (!Free(k)) yield return Id(k);
    }

    /// <summary>Everything sold on the App Store: each paid item, each sold Queen Blitz pack, and Unlock Everything</summary>
    public static IEnumerable<string> ProductIds()
    {
        foreach (string id in PaidIds()) yield return id;
        foreach (string pack in QueenBlitz.PawnPacks()) yield return "blitz." + pack;
        yield return EverythingId;
    }
}
