using UnityEditor;

/// <summary>Tools > Tipped Cube Test: draw the cube tipped (as Yellow/Orange see it online) in local games, in the Editor</summary>
public static class TippedCubeTest
{
    private const string MenuPath = "Tools/Tipped Cube Test";
    private const string Key = "TippedCubeTest";

    [InitializeOnLoadMethod]
    private static void Load() => BoardView.ForceForTesting = EditorPrefs.GetBool(Key, false);

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        BoardView.ForceForTesting = !BoardView.ForceForTesting;
        EditorPrefs.SetBool(Key, BoardView.ForceForTesting);
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleCheck()
    {
        UnityEditor.Menu.SetChecked(MenuPath, BoardView.ForceForTesting);
        return true;
    }
}
