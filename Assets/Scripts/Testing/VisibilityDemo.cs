#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Editor-only: screenshots a Queen Blitz puzzle on every backdrop in every finish, to check the
/// pieces stand out. `PlayerPrefs DebugVisibility = 1` (set by VisibilityShots.Run) turns it on;
/// shots go to ~/Desktop/visibility/&lt;tag&gt;&lt;backdrop&gt;_&lt;finish&gt;.png (DebugVisibilityTag) and the editor quits after.
/// </summary>
public class VisibilityDemo : MonoBehaviour
{
    private static int blitzSeen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugVisibility", 0) == 0 || FindFirstObjectByType<VisibilityDemo>() != null) return;
        blitzSeen = PlayerPrefs.GetInt("HowToPlaySeen.Blitz", 0);
        PlayerPrefs.SetInt("HowToPlaySeen.Blitz", 1);
        var go = new GameObject("Visibility Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<VisibilityDemo>().StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        Application.runInBackground = true;
        yield return new WaitForSeconds(2f);
        QueenBlitz.PackId = PlayerPrefs.GetString("DebugBlitzPack", "blitz-5");
        int idx = 0, most = 0;
        for (int i = 0; i < QueenBlitz.Puzzles.Length; i++)
        {
            var p = QueenBlitz.Puzzles[i];
            if (!p.ice && p.queen.HasValue && p.pawns.Length > most) { idx = i; most = p.pawns.Length; }
        }
        QueenBlitz.Puzzle pz = QueenBlitz.Puzzles[idx];
        string folder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "Desktop", "visibility");
        Directory.CreateDirectory(folder);
        string only = PlayerPrefs.GetString("DebugVisibilityBackdrops", "");
        string tag = PlayerPrefs.GetString("DebugVisibilityTag", "");
        PieceSets.Kind saved = PieceSets.Current;

        // One game per piece set (DebugVisibilitySets: comma-separated PieceShapes.Kind names; none = the chosen set)
        string sets = PlayerPrefs.GetString("DebugVisibilitySets", "");
        foreach (string setName in sets.Length > 0 ? sets.Split(',') : new[] { "" })
        {
            PieceShapes.Showing = setName.Length > 0 ? (PieceShapes.Kind?)System.Enum.Parse(typeof(PieceShapes.Kind), setName.Trim()) : null;
            string prefix = tag + (setName.Length > 0 ? setName.Trim() + "_" : "");
            var config = new GameConfiguration { playerCount = 2, boardSize = QueenBlitz.BoardFor(pz.size), aiPlayerCount = 1, queenBlitz = true, blitzPuzzle = idx };
            config.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
            config.ApplyToLegacyFields();
            SceneController.Instance.LoadGameScene(config);
            yield return new WaitForSeconds(1f);
            float giveUp = Time.realtimeSinceStartup + 40f;
            while ((QueenBlitzDirector.Instance == null || GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing)
                   && Time.realtimeSinceStartup < giveUp) yield return null;
            Debug.Log($"VISIBILITY: {setName} puzzle {idx + 1} ({most} pawns), state {GameStateManager.Instance?.currentState}");
            yield return new WaitForSeconds(3f);

            foreach (Backdrops.Kind backdrop in Backdrops.All)
            {
                if (only.Length > 0 && !only.Contains(backdrop.ToString())) continue;
                float until = Time.realtimeSinceStartup + 20f;
                while (!Backdrops.Apply(Camera.main, backdrop) && Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForSeconds(2f);
                foreach (PieceSets.Kind finish in PieceSets.All)
                {
                    PieceSets.Current = finish;
                    foreach (ChessPiece piece in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None)) piece.ApplyMaterial();
                    yield return new WaitForSeconds(0.6f);
                    string file = Path.Combine(folder, $"{prefix}{backdrop}_{finish}.png");
                    ScreenCapture.CaptureScreenshot(file);
                    yield return new WaitForSeconds(0.6f);
                    Debug.Log("VISIBILITY: " + file);
                }
            }
        }
        PieceShapes.Showing = null;
        PieceSets.Current = saved;
        PlayerPrefs.DeleteKey("DebugVisibility");
        PlayerPrefs.DeleteKey("DebugVisibilitySets");
        PlayerPrefs.SetInt("HowToPlaySeen.Blitz", blitzSeen);
        PlayerPrefs.Save();
        Debug.Log("VISIBILITY: done");
        UnityEditor.EditorApplication.Exit(0);
    }
}
#endif
