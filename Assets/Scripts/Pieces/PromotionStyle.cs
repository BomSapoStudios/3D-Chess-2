using UnityEngine;

/// <summary>Which animation plays when a pawn is promoted (picked in Customize, kept on this phone)</summary>
public static class PromotionStyle
{
    public enum Kind { GlitchRebuild, SpinMorph, Coronation }

    private const string PrefKey = "PromotionStyle";

    public static Kind Current
    {
        get => (Kind)PlayerPrefs.GetInt(PrefKey, (int)Kind.GlitchRebuild);
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static string DisplayName(Kind kind) => kind == Kind.SpinMorph ? "Spin morph" : kind == Kind.Coronation ? "Coronation" : "Glitch rebuild";

    public static string Description(Kind kind) => kind == Kind.SpinMorph
        ? "The pawn spins into a column of light and the new piece spins out of it."
        : kind == Kind.Coronation
        ? "A glowing ring crowns the pawn, squeezes it to a spark, and the new piece rises in a shockwave."
        : "The pawn glitches like a broken hologram, then rebuilds from glowing cubes.";
}
