using UnityEngine;

/// <summary>Board options picked in Customize and kept on this phone (applied by NeonGameLook)</summary>
public static class BoardLook
{
    /// <summary>Floor squares drawn solid instead of see-through</summary>
    public static bool SolidFloors
    {
        get => PlayerPrefs.GetInt("BoardSolidFloors", 0) == 1;
        set { PlayerPrefs.SetInt("BoardSolidFloors", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>The wireframe's up-and-down lines hidden, leaving each level's outline</summary>
    public static bool HideVerticalLines
    {
        get => PlayerPrefs.GetInt("BoardHideVerticalLines", 0) == 1;
        set { PlayerPrefs.SetInt("BoardHideVerticalLines", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static string Description()
    {
        string floors = SolidFloors ? "Solid floors" : "See-through floors";
        string lines = HideVerticalLines ? "level outlines only" : "full wireframe";
        return $"{floors}, {lines}";
    }
}
