using UnityEngine;

/// <summary>
/// Which modeled piece set the board uses (Customize > Piece set), separate from the Look
/// (PieceSets), which recolors the cyberpunk pieces. Abyss Glow brings its own colors.
/// Saved on this phone and used in every mode; online, each player sees their own choice.
/// </summary>
public static class PieceShapes
{
    public enum Kind
    {
        Cyberpunk = 0,
        AbyssGlow = 1,
        MushroomGrove = 2,
        WildKingdom = 3,
    }

    private const string PrefKey = "PieceShapes";

    public static Kind Current
    {
        get => (Kind)PlayerPrefs.GetInt(PrefKey, (int)Kind.Cyberpunk);
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static readonly Kind[] All = { Kind.Cyberpunk, Kind.AbyssGlow, Kind.MushroomGrove, Kind.WildKingdom };

    public static string DisplayName(Kind kind)
    {
        switch (kind)
        {
            case Kind.AbyssGlow: return "Abyss Glow";
            case Kind.MushroomGrove: return "Mushroom Grove";
            case Kind.WildKingdom: return "Wild Kingdom";
            default: return "Cyberpunk";
        }
    }

    public static string Description(Kind kind)
    {
        switch (kind)
        {
            case Kind.AbyssGlow: return "Sea-carved pieces: pale pearl against deep violet, with glowing rims";
            case Kind.MushroomGrove: return "Toadstools and a tree-frog knight, in moonlight white and deep plum";
            case Kind.WildKingdom: return "Lion king, peacock queen, owl, zebra, elephant castle and meerkat pawns";
            default: return "Faceted neon pieces with light strips; the Look below recolors them";
        }
    }

    /// <summary>The folder under Resources/Pieces (and Resources/PieceIcons) for this set</summary>
    public static string Folder(Kind kind)
    {
        switch (kind)
        {
            case Kind.AbyssGlow: return "Abyss";
            case Kind.MushroomGrove: return "Mushroom";
            case Kind.WildKingdom: return "Wild";
            default: return "Cyberpunk";
        }
    }

    /// <summary>Sets with their own colors, which the Look doesn't change</summary>
    public static bool OwnColors(Kind kind) => kind != Kind.Cyberpunk;

    /// <summary>Where this set's bench/promotion icons live under Resources/PieceIcons ("" for cyberpunk)</summary>
    public static string IconFolder(Kind kind) => kind == Kind.Cyberpunk ? "" : Folder(kind) + "/";
}
