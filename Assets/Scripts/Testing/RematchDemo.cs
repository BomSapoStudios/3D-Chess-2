#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

/// <summary>
/// Development-only: shows the result panel without playing a whole game.
/// `defaults write BomSapo.Chess3D DebugRematchDemo -int 1` on the simulator, then launch:
/// starts Quick Play vs the computer, places both sides, and White resigns.
/// With `DebugAISelfTest` on instead, it only starts the Quick Play game the self-test needs.
/// </summary>
public class RematchDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // The AI self-test needs a Quick Play game too: this starts one for it
        bool demo = PlayerPrefs.GetInt("DebugRematchDemo", 0) == 1, aiTest = PlayerPrefs.GetInt("DebugAISelfTest", 0) == 1;
        if (!(demo || aiTest) || FindFirstObjectByType<RematchDemo>() != null) return;
        var go = new GameObject("Rematch Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<RematchDemo>().StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        yield return new WaitForSeconds(2f);
        if (SceneController.Instance == null) yield break;
        SceneController.Instance.QuickStartHumanVsAI(PieceColor.White, AIDifficulty.Easy);
        if (PlayerPrefs.GetInt("DebugRematchDemo", 0) != 1) yield break; // just starting the game for the AI self-test
        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSeconds(4f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        while (GameStateManager.Instance.CanPlacePieces()) yield return null;
        yield return new WaitForSeconds(2f);
        GameEndDetectionManager.Instance.HandleForfeit(PieceColor.White);
        Debug.Log("REMATCH DEMO: White resigned");
    }
}
#endif
