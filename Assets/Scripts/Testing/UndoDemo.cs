#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Development-only check of Undo. `defaults write BomSapo.Chess3D DebugUndoDemo -int 1` (Quick Play)
/// or 2 (a zombie run) on the simulator, then launch: plays two of White's turns (the robot or the
/// zombies answer each), taps Undo, and checks the board is back as it was before the second turn.
/// Logs "UNDO DEMO".
/// </summary>
public class UndoDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        int mode = PlayerPrefs.GetInt("DebugUndoDemo", 0);
        if (mode == 0 || FindFirstObjectByType<UndoDemo>() != null) return;
        var go = new GameObject("Undo Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<UndoDemo>().StartCoroutine(Run(mode == 2));
    }

    private static IEnumerator Run(bool zombies)
    {
        yield return new WaitForSeconds(2f);
        var config = new GameConfiguration();
        config.playerCount = 2;
        config.boardSize = zombies ? BoardSize.Large8x8x8 : BoardSize.Small4x4x4;
        config.aiDifficulty = AIDifficulty.Easy;
        config.playerTypes = new List<PlayerType> { PlayerType.Human, PlayerType.Computer };
        config.aiPlayerCount = 1;
        config.zombieMode = zombies;
        config.ApplyToLegacyFields();
        SceneController.Instance.LoadGameScene(config);

        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSeconds(4f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        while (GameStateManager.Instance.currentState != GameState.Playing) yield return null;

        string before = "";
        for (int turn = 0; turn < 2; turn++)
        {
            yield return WaitForWhite();
            yield return new WaitForSeconds(0.5f); // let Undo save this turn
            if (turn == 1) before = Board();
            AIMove move = new RobotBrain().FindMove(PieceColor.White, RobotBrain.Settings.For(AIDifficulty.Easy));
            ChessBoard.Instance.MovePiece(move.fromPosition, move.toPosition);
            Debug.Log($"UNDO DEMO: White plays {move}");
        }
        yield return WaitForWhite();
        float wait = Time.time + 5f;
        while (!UndoManager.Instance.CanUndo && Time.time < wait) yield return null;
        string after = Board();
        Debug.Log($"UNDO DEMO: can undo {UndoManager.Instance.CanUndo}, board changed {after != before}, zombie rounds {ZombieMode.Rounds}");
        UndoManager.Instance.Undo();
        yield return new WaitForSeconds(1f);
        string undone = Board();
        Debug.Log($"UNDO DEMO: {(undone == before ? "PASS" : "FAIL")} board restored, turn {TurnManager.Instance.GetCurrentPlayer()}, zombie rounds {ZombieMode.Rounds}");
        if (undone != before) Debug.Log($"UNDO DEMO: expected {before}\nUNDO DEMO: got {undone}");
        // And play on from there
        AIMove again = new RobotBrain().FindMove(PieceColor.White, RobotBrain.Settings.For(AIDifficulty.Easy));
        bool moved = again != null && ChessBoard.Instance.MovePiece(again.fromPosition, again.toPosition);
        yield return WaitForWhite();
        Debug.Log($"UNDO DEMO: {(moved && TurnManager.Instance.GetCurrentPlayer() == PieceColor.White ? "PASS" : "FAIL")} play continues after undo");
    }

    private static IEnumerator WaitForWhite()
    {
        float until = Time.time + 30f;
        while (Time.time < until && !(TurnManager.Instance.GetCurrentPlayer() == PieceColor.White && GameStateManager.Instance.CanMovePieces())) yield return null;
    }

    private static string Board()
    {
        Vector3Int d = BoardDimensionsManager.Instance.GetDimensions();
        var parts = new List<string>();
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) parts.Add($"{p.pieceColor.ToString()[0]}{p.pieceType}@{x}{y}{z}");
        }
        return string.Join(" ", parts);
    }
}
#endif
