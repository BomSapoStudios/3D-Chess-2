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

    public static string DisplayName(Kind kind) => kind == Kind.AbyssGlow ? "Abyss Glow" : "Cyberpunk";

    public static string Description(Kind kind) => kind == Kind.AbyssGlow
        ? "Sea-carved pieces: pale pearl against deep violet, with glowing rims"
        : "Faceted neon pieces with light strips; the Look below recolors them";

    /// <summary>The folder under Resources/Pieces that holds this set's models</summary>
    public static string Folder(Kind kind) => kind == Kind.AbyssGlow ? "Abyss" : "Cyberpunk";
}
