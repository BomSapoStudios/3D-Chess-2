using UnityEngine;

/// <summary>Which animation plays when a pawn is promoted (picked in Customize, kept on this phone)</summary>
public static class PromotionStyle
{
    public enum Kind { GlitchRebuild, SpinMorph, Coronation, BlackHole, Splat }

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

    public static string DisplayName(Kind kind) => kind == Kind.Splat ? "Splat" : kind == Kind.SpinMorph ? "Spin morph" : kind == Kind.Coronation ? "Coronation" : kind == Kind.BlackHole ? "Black hole" : "Glitch rebuild";

    public static string Description(Kind kind) => kind == Kind.Splat
        ? "The new piece comes flying in and squashes the pawn with a big splat of paint."
        : kind == Kind.SpinMorph
        ? "The pawn spins into a column of light and the new piece spins out of it."
        : kind == Kind.Coronation
        ? "A glowing ring crowns the pawn, squeezes it to a spark, and the new piece rises in a shockwave."
        : kind == Kind.BlackHole
        ? "The pawn collapses into a black ball, which morphs into the new piece."
        : "The pawn glitches like a broken hologram, then rebuilds from glowing cubes.";
}
