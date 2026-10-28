using UnityEngine.SceneManagement;

/// <summary>Whether the main menu scene (not a game) is the one showing</summary>
public static class MenuScene
{
    public const string Name = "MainMenu";

    public static bool IsActive => SceneManager.GetActiveScene().name == Name;
}
