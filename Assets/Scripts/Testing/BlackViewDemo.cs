#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Development-only: Quick Play as Black (DebugBlackView 1), to check the board as Black sees it. Logs "BLACK VIEW".</summary>
public class BlackViewDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugBlackView", 0) != 1 || FindFirstObjectByType<BlackViewDemo>() != null) return;
        var go = new GameObject("Black View Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<BlackViewDemo>().StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        yield return new WaitForSeconds(2f);
        PlayerViewUI.StartView = PieceColor.Black;
        SceneController.Instance.QuickStartHumanVsAI(PieceColor.Black, AIDifficulty.Easy);
        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.Black);
        yield return new WaitForSeconds(4f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.Black, true);
        while (GameStateManager.Instance.currentState != GameState.Playing) yield return null;
        yield return new WaitForSeconds(4f);
        int on = 0, off = 0;
        var offList = new List<string>();
        foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!r.name.StartsWith("Floor_")) continue;
            if (r.enabled && r.gameObject.activeInHierarchy) on++; else { off++; if (offList.Count < 12) offList.Add(r.name); }
        }
        Debug.Log($"BLACK VIEW: floors on {on}, off {off}: {string.Join(" ", offList)}");
    }
}
#endif
