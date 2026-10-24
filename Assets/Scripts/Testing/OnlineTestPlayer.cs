#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

/// <summary>
/// Development-only: in an online game, places this side automatically and readies up, and logs
/// how many of the other side's pieces are on the board before and after play starts (private
/// setup check). `defaults write BomSapo.Chess3D DebugOpenStats -int 13` makes the room.
/// </summary>
public class OnlineTestPlayer : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugOpenStats", 0) != 13 || FindFirstObjectByType<OnlineTestPlayer>() != null) return;
        var go = new GameObject("Online Test Player");
        DontDestroyOnLoad(go);
        go.AddComponent<OnlineTestPlayer>().StartCoroutine(Run());
    }

    private static int Opponents()
    {
        int n = 0;
        if (ChessBoard.Instance == null) return 0;
        foreach (ChessPiece p in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
            if (p.CurrentPosition.x >= 0 && ChessBoard.Instance.GetPieceAt(p.CurrentPosition) == p && !OnlineSession.IsLocal(p.pieceColor)) n++;
        return n;
    }

    private static IEnumerator Run()
    {
        while (!OnlineSession.IsActive || PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(6f); // the other side places meanwhile
        Debug.Log($"ONLINE TEST: during setup, opponent pieces on my board: {Opponents()}");
        PlacementManager.Instance.AutoPlaceRemaining(OnlineSession.LocalColor);
        yield return new WaitForSeconds(2f);
        PlacementManager.Instance.SetPlayerReady(OnlineSession.LocalColor, true);
        float wait = 0f;
        while (GameStateManager.Instance.currentState != GameState.Playing && wait < 20f) { wait += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(1.5f);
        Debug.Log($"ONLINE TEST: play started ({GameStateManager.Instance.currentState}), opponent pieces on my board: {Opponents()}");
    }
}
#endif
