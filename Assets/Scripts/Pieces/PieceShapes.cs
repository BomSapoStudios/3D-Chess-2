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
        CloseEncounters = 4,
        RoboArena = 5,
    }

    private const string PrefKey = "PieceShapes";

    /// <summary>The set in use: the one picked, if it's unlocked (Store), else Cyberpunk</summary>
    public static Kind Current
    {
        get
        {
            var picked = (Kind)PlayerPrefs.GetInt(PrefKey, (int)Kind.Cyberpunk);
            return Store.Owns(picked) ? picked : Kind.Cyberpunk;
        }
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static readonly Kind[] All = { Kind.Cyberpunk, Kind.AbyssGlow, Kind.MushroomGrove, Kind.WildKingdom, Kind.CloseEncounters, Kind.RoboArena };

    /// <summary>Customize is showing a set that isn't unlocked yet (its preview), or null</summary>
    public static Kind? Showing;

    /// <summary>The set pieces are built from right now: the one being previewed, else the one in use</summary>
    public static Kind Active => Showing ?? Current;

    public static string DisplayName(Kind kind)
    {
        switch (kind)
        {
            case Kind.AbyssGlow: return "Abyss Glow";
            case Kind.MushroomGrove: return "Mushroom Grove";
            case Kind.WildKingdom: return "Wild Kingdom";
            case Kind.CloseEncounters: return "Close Encounters";
            case Kind.RoboArena: return "Robo Arena";
            default: return "Cyberpunk";
        }
    }

    public static string Description(Kind kind)
    {
        switch (kind)
        {
            case Kind.AbyssGlow: return "Sea creatures carved in coral, with a seahorse knight and anglerfish";
            case Kind.MushroomGrove: return "Toadstools, morels and a tree-frog knight";
            case Kind.WildKingdom: return "Lion king, peacock queen, owl, zebra, elephant castle and meerkat pawns";
            case Kind.CloseEncounters: return "Little green men, a flying-saucer rook and an alien steed";
            case Kind.RoboArena: return "Battle robots: a tank-tread rook, a mech horse and a shield-bearing queen";
            default: return "Faceted pieces with glowing light strips";
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
            case Kind.CloseEncounters: return "Aliens";
            case Kind.RoboArena: return "Robots";
            default: return "Cyberpunk";
        }
    }

    /// <summary>Sets whose models carry a detail texture (shaded into the finish)</summary>
    public static bool Textured(Kind kind) => kind != Kind.Cyberpunk;

    /// <summary>Where this set's bench/promotion icons live under Resources/PieceIcons ("" for cyberpunk)</summary>
    public static string IconFolder(Kind kind) => kind == Kind.Cyberpunk ? "" : Folder(kind) + "/";
}
