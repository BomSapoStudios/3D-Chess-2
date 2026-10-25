#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Development-only: App Store screenshots on the simulator. A file ShotDemo.txt in the app's
/// Documents turns it on: the development console stays hidden. With 1 in the file a
/// robot-vs-robot Quick Play starts by itself and freezes after 10 moves, so there's a game in
/// progress to capture; with 2, a Quick Play against the robot stops at the setup with a White piece
/// picked up from the tray. Logs "SHOT DEMO: ready" when the scene is set.
/// </summary>
public class ShotDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string file = System.IO.Path.Combine(Application.persistentDataPath, "ShotDemo.txt");
        if (!System.IO.File.Exists(file) || FindFirstObjectByType<ShotDemo>() != null) return;
        int.TryParse(System.IO.File.ReadAllText(file).Trim(), out int mode);
        PlayerPrefs.SetInt("HowToPlaySeen", 1);
        var go = new GameObject("Shot Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<ShotDemo>().StartCoroutine(Run(mode));
    }

    private void Update() => Debug.developerConsoleVisible = false;

    private static IEnumerator Run(int mode)
    {
        if (mode != 1 && mode != 2) yield break;
        yield return new WaitForSeconds(2f);
        if (SceneController.Instance == null) yield break;
        if (mode == 2)
        {
            SceneController.Instance.QuickStartHumanVsAI(PieceColor.White, AIDifficulty.Medium);
            while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
            yield return new WaitForSeconds(1.5f);
            PieceTray tray = PieceTray.GetTrayForColor(PieceColor.White);
            ChessPiece pick = tray != null ? tray.GetComponentInChildren<ChessPiece>() : null;
            if (pick != null) PlacementManager.Instance.OnTrayPieceClicked(pick);
            yield return new WaitForSeconds(1f);
            Debug.Log("SHOT DEMO: ready");
            yield break;
        }
        var config = new GameConfiguration
        {
            playerCount = 2, aiPlayerCount = 2, boardSize = BoardSize.Small5x5x5, aiDifficulty = AIDifficulty.Medium,
            playerTypes = new List<PlayerType> { PlayerType.Computer, PlayerType.Computer },
        };
        config.ApplyToLegacyFields();
        SceneController.Instance.LoadGameScene(config);
        while (TurnManager.Instance == null || TurnManager.Instance.GetTotalMovesMade() < 10) yield return null;
        yield return new WaitForSeconds(1.2f);  // let the last move land
        Time.timeScale = 0f;
        Debug.Log("SHOT DEMO: ready");
    }
}
#endif
