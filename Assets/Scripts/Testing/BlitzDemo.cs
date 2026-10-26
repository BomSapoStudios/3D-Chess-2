#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

/// <summary>
/// Development-only: plays Queen Blitz puzzles by themselves with the solver's answers (each
/// puzzle's "line" in its pack file) and checks
/// that each one clears in exactly its par. `defaults write BomSapo.Chess3D DebugBlitzDemo -int N`
/// plays puzzle N (1-10) and stays on it; 99 plays them all in turn; 98 plays them all using
/// Auto-place for the queen (each costs a pawn); 97 plays puzzle 2's first move then a wrong one,
/// so it runs out of moves; 96 opens the in-game menu after puzzle 2's first move; 95 opens the
/// puzzle picker on the pack. `DebugBlitzPack` picks the pack (default starter) and
/// `DebugBlitzMissPuzzle` the puzzle 97 and 96 use. Logs "BLITZ DEMO".
/// </summary>
public class BlitzDemo : MonoBehaviour
{
    private static bool autoPlace, miss, menu;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        int which = PlayerPrefs.GetInt("DebugBlitzDemo", 0);
        if (which == 0 || FindFirstObjectByType<BlitzDemo>() != null) return;
        QueenBlitz.PackId = PlayerPrefs.GetString("DebugBlitzPack", "starter");
        if (which == 95) { QueenBlitz.ReturnToPicker = true; return; } // the menu opens on the pack
        var go = new GameObject("Blitz Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<BlitzDemo>().StartCoroutine(Run(which));
    }

    private static IEnumerator Run(int which)
    {
        yield return new WaitForSeconds(2f);
        miss = which == 97;
        menu = which == 96;
        if (miss || menu) which = PlayerPrefs.GetInt("DebugBlitzMissPuzzle", 2);
        bool all = which >= 98;
        autoPlace = which == 98;
        int first = all ? 0 : which - 1, last = all ? QueenBlitz.Puzzles.Length - 1 : which - 1;
        int passed = 0;
        for (int i = first; i <= last; i++)
        {
            bool ok = false;
            yield return Play(i, r => ok = r, stay: !all);
            if (ok) passed++;
        }
        Debug.Log($"BLITZ DEMO: done, {passed}/{last - first + 1} puzzles cleared at par");
    }

    private static IEnumerator Play(int index, System.Action<bool> result, bool stay)
    {
        QueenBlitz.Puzzle puzzle = QueenBlitz.Puzzles[index];
        var config = new GameConfiguration();
        config.playerCount = 2;
        config.boardSize = QueenBlitz.BoardFor(puzzle.size);
        config.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
        config.aiPlayerCount = 1;
        config.queenBlitz = true;
        config.blitzPuzzle = index;
        config.ApplyToLegacyFields();
        // After the first puzzle, go on the way a player does: the result panel's Next puzzle
        if (QueenBlitz.Active && GameStateManager.Instance.currentState == GameState.GameOver) QueenBlitzDirector.Restart(index);
        else SceneController.Instance.LoadGameScene(config);
        yield return new WaitForSeconds(1f);

        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1f);
        BoardPosition[] solution = puzzle.line; // the queen's start, then each square she stops on
        if (!puzzle.queen.HasValue)
        {
            // Place the queen where the solver starts, as a player would
            ChessPiece queen = null;
            foreach (ChessPiece p in PieceTray.GetTrayForColor(PieceColor.White).GetComponentsInChildren<ChessPiece>(true))
                if (p.pieceType == ChessPieceType.Queen) queen = p;
            Debug.Log($"BLITZ DEMO: {index + 1} placing the queen at {solution[0]} (zone allows it: {PlacementManager.IsPositionInColorZone(solution[0], PieceColor.White)})");
            if (autoPlace) QueenBlitz.AutoPlaceQueen();
            else PlacementManager.Instance.ExecuteAIPlacement(queen, solution[0]);
            yield return new WaitForSeconds(stay ? 4f : 1f); // leave time for a screenshot of placement
            PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        }
        if (autoPlace && !puzzle.queen.HasValue && ChessBoard.Instance.GetPieceAt(solution[0]) == null)
        {
            // Auto-place may pick another best square: play the solver's line from there instead
            Debug.Log($"BLITZ DEMO: {index + 1} auto-placed at {puzzle.best}, solution starts at {solution[0]}");
        }
        float wait = 0f;
        while (GameStateManager.Instance.currentState != GameState.Playing && wait < 20f) { wait += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(1f);
        int pawns = QueenBlitzDirector.Instance != null ? QueenBlitzDirector.Instance.PawnsLeft() : -1;
        Debug.Log($"BLITZ DEMO: {index + 1} {puzzle.name} started, state {GameStateManager.Instance.currentState}, pawns {pawns}, current {TurnManager.Instance.GetCurrentPlayer()}");

        bool good = true;
        for (int m = 1; m < solution.Length; m++)
        {
            ChessPiece queen = ChessBoard.Instance.GetPieceAt(solution[m - 1]);
            if (queen == null || queen.pieceType != ChessPieceType.Queen) { Debug.Log($"BLITZ DEMO: FAIL no queen at {solution[m - 1]}"); good = false; break; }
            if (!queen.GetValidMoves().Contains(solution[m])) { Debug.Log($"BLITZ DEMO: FAIL {solution[m]} isn't a legal sweep from {solution[m - 1]}"); good = false; break; }
            while (!GameStateManager.Instance.CanMovePieces()) yield return null;
            if ((miss || menu) && m == 2)
            {
                yield return new WaitForSeconds(4f); // the moves-left counter, pink at 1
                if (menu) { GameMenuUI.Instance.ShowMenu(); Debug.Log("BLITZ DEMO: menu open"); yield break; }
                // A legal sweep off the solver's line that leaves pawns: the last move runs out
                BoardPosition wrong = queen.GetValidMoves().Find(to => !to.Equals(solution[m]));
                Debug.Log($"BLITZ DEMO: {index + 1} missing on purpose: {solution[m - 1]} > {wrong}");
                ChessBoard.Instance.MovePiece(solution[m - 1], wrong);
                yield return new WaitForSeconds(5f);
                Debug.Log($"BLITZ DEMO: {index + 1} out of moves {QueenBlitz.OutOfMoves}, cleared {QueenBlitz.Cleared}, state {GameStateManager.Instance.currentState}, pawns {PawnBank.Total}");
                yield break;
            }
            ChessBoard.Instance.MovePiece(solution[m - 1], solution[m]);
            yield return new WaitForSeconds(1.2f);
            Debug.Log($"BLITZ DEMO: {index + 1} move {QueenBlitz.Moves}: {solution[m - 1]} > {solution[m]}, pawns left {(QueenBlitzDirector.Instance != null ? QueenBlitzDirector.Instance.PawnsLeft() : -1)}, turn {TurnManager.Instance.GetCurrentPlayer()}");
        }
        wait = 0f;
        while (GameStateManager.Instance.currentState != GameState.GameOver && wait < 6f) { wait += Time.deltaTime; yield return null; }
        // The cube has to be the puzzle's size, or its pawns and blocks sit off the board
        Vector3Int dims = BoardDimensionsManager.Instance.GetDimensions();
        if (dims.x != puzzle.size) { Debug.Log($"BLITZ DEMO: FAIL board is {dims.x}³ for a {puzzle.size}³ puzzle"); good = false; }
        bool cleared = QueenBlitz.Cleared && QueenBlitz.Moves == puzzle.par && GameStateManager.Instance.currentState == GameState.GameOver;
        Debug.Log($"BLITZ DEMO: {index + 1} {puzzle.name}: {(cleared && good ? "PASS" : "FAIL")} cleared {QueenBlitz.Cleared}, moves {QueenBlitz.Moves}, par {puzzle.par}, state {GameStateManager.Instance.currentState}, auto-placed {QueenBlitz.AutoPlaced}, paid {QueenBlitz.LastPaid}, pawns {PawnBank.Total}");
        result(cleared && good);
        yield return new WaitForSeconds(stay ? 1f : 2.5f);
    }
}
#endif
