using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Command line (editor closed): VIS_TAG=after_ Unity -projectPath . -executeMethod VisibilityShots.Run
/// (VIS_TAG, optional, starts each file name; VIS_SETS=AbyssGlow,CloseEncounters renders those piece sets)
/// Plays from MainMenu with VisibilityDemo on: a Queen Blitz puzzle on every backdrop in every finish.
/// </summary>
public static class VisibilityShots
{
    public static void Run()
    {
        PlayerPrefs.SetInt("DebugVisibility", 1);
        PlayerPrefs.SetString("DebugVisibilityTag", System.Environment.GetEnvironmentVariable("VIS_TAG") ?? "");
        PlayerPrefs.SetString("DebugVisibilitySets", System.Environment.GetEnvironmentVariable("VIS_SETS") ?? "");
        PlayerPrefs.Save();
        StoreImageRenderer.SetGameViewSize(2532, 1170); // iPhone 13 Pro, landscape
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        EditorApplication.EnterPlaymode();
    }
}
