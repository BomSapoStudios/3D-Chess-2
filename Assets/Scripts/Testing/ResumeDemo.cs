#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Development-only: checks saved games. Put a file ResumeDemo.txt holding N in the app's Documents
/// on the simulator, then launch. Starts a game (1 Quick Play as Black, 2 Zombies!, 3 Queen Blitz,
/// 4 a 4-player Local Game with Chaos and clocks), sets it up, waits for the save, goes back to the
/// menu, shows the Resume card, resumes, and logs whether the board came back the same.
/// </summary>
public class ResumeDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string file = System.IO.Path.Combine(Application.persistentDataPath, "ResumeDemo.txt");
        int mode = System.IO.File.Exists(file) && int.TryParse(System.IO.File.ReadAllText(file).Trim(), out int m) ? m : 0;
        if (mode == 0 || FindFirstObjectByType<ResumeDemo>() != null) return;
        var go = new GameObject("Resume Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<ResumeDemo>().StartCoroutine(Run(mode));
    }

    private static GameConfiguration Config(int mode, out string slot)
    {
        var c = new GameConfiguration { playerCount = 2, boardSize = BoardSize.Small5x5x5, aiDifficulty = AIDifficulty.Easy };
        c.playerTypes = new List<PlayerType> { PlayerType.Human, PlayerType.Computer };
        slot = SavedGames.Local;
        switch (mode)
        {
            case 1:
                c.playerTypes = new List<PlayerType> { PlayerType.Computer, PlayerType.Human };
                slot = SavedGames.QuickPlay;
                PlayerViewUI.StartView = PieceColor.Black;
                break;
            case 2:
                c.zombieMode = true;
                c.boardSize = BoardSize.Large8x8x8;
                slot = SavedGames.Zombies;
                break;
            case 3:
                c.queenBlitz = true;
                QueenBlitz.PackId = "starter";
                c.blitzPuzzle = 2;
                c.boardSize = QueenBlitz.BoardFor(QueenBlitz.PuzzlesOf("starter")[2].size);
                slot = SavedGames.Blitz;
                break;
            case 4:
                c.playerCount = 4;
                c.boardSize = BoardSize.Medium6x6x6;
                c.playerTypes = new List<PlayerType> { PlayerType.Human, PlayerType.Computer, PlayerType.Human, PlayerType.Computer };
                c.enableChaosMode = true;
                c.chaosTurnInterval = 5;
                c.enableTimedPlay = true;
                c.timePerPlayerMinutes = 10;
                break;
        }
        int robots = 0;
        foreach (PlayerType t in c.playerTypes) if (t == PlayerType.Computer) robots++;
        c.aiPlayerCount = robots;
        c.saveSlot = slot;
        if (c.playerCount == 2) c.ApplyToLegacyFields();
        return c;
    }

    private static string Board()
    {
        var all = new List<string>();
        foreach (ChessPiece p in UndoManager.BoardPieces())
            all.Add($"{p.pieceColor} {p.pieceType} {p.CurrentPosition.x}{p.CurrentPosition.y}{p.CurrentPosition.z}");
        all.Sort();
        return string.Join(", ", all);
    }

    private static string Clocks()
    {
        if (TimerManager.Instance == null || !TimerManager.Instance.IsTimedPlayActive()) return "no clocks";
        var all = new List<string>();
        foreach (var kv in TimerManager.Instance.GetAllTimerStates()) all.Add($"{kv.Key} {kv.Value:F0}s");
        return string.Join(", ", all);
    }

    private static IEnumerator Run(int mode)
    {
        yield return new WaitForSeconds(2f);
        if (SceneController.Instance == null) yield break;
        SceneController.Instance.LoadGameScene(Config(mode, out string slot));
        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1.5f);

        // Set up every person's side
        if (QueenBlitz.Active)
        {
            if (!QueenBlitz.Current.queen.HasValue)
                foreach (ChessPiece p in PieceTray.GetTrayForColor(PieceColor.White).GetComponentsInChildren<ChessPiece>(true))
                    if (p.pieceType == ChessPieceType.Queen) { PlacementManager.Instance.ExecuteAIPlacement(p, QueenBlitz.Current.best); break; }
        }
        else
        {
            foreach (PieceColor c in PlayerManager.Instance.GetAllPlayers())
            {
                if (TurnManager.Instance.IsPlayerAI(c)) continue;
                PlacementManager.Instance.AutoPlaceRemaining(c);
                while (PlacementManager.Instance.IsAutoPlacing) yield return null;
                PlacementManager.Instance.SetPlayerReady(c, true);
            }
        }
        float until = Time.time + 60f;
        while (!SavedGames.Saving && Time.time < until) yield return null;
        yield return new WaitForSeconds(3f); // a 3-second-old clock shows it really counts down
        string before = Board();
        string turnBefore = TurnManager.Instance.GetCurrentPlayer().ToString();
        Debug.Log($"RESUME DEMO: saved={SavedGames.Has(slot)} turn {turnBefore}, clocks {Clocks()}, zombies {ZombieMode.Rounds}/{ZombieMode.Score}, blitz moves {QueenBlitz.Moves}");
        Debug.Log($"RESUME DEMO: before {before}");

        SceneController.Instance.LoadMainMenu();
        yield return new WaitForSeconds(4f);
        NeonMenu menu = FindFirstObjectByType<NeonMenu>();
        if (menu != null) menu.DebugOfferResume(slot);
        Debug.Log("RESUME DEMO: card up");
        yield return new WaitForSeconds(5f);
        Debug.Log($"RESUME DEMO: resume {SavedGames.Resume(slot)}");

        until = Time.time + 60f;
        while (!SavedGames.Saving && Time.time < until) yield return null;
        yield return new WaitForSeconds(1f);
        string after = Board();
        Debug.Log($"RESUME DEMO: after turn {TurnManager.Instance.GetCurrentPlayer()}, state {GameStateManager.Instance.currentState}, clocks {Clocks()}, zombies {ZombieMode.Rounds}/{ZombieMode.Score}, blitz moves {QueenBlitz.Moves}");
        Debug.Log($"RESUME DEMO: after {after}");
        Debug.Log($"RESUME DEMO: RESULT board {(before == after ? "SAME" : "DIFFERENT")}, turn {(turnBefore == TurnManager.Instance.GetCurrentPlayer().ToString() ? "SAME" : "DIFFERENT")}");
    }
}
#endif
