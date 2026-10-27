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
/// puzzle picker on the pack; 94 goes puzzle > Menu > Queen Blitz > another puzzle, 93 runs out
/// of moves then Puzzles > another puzzle, 92 Main Menu > Queen Blitz! > Start new > another puzzle;
/// 86 opens puzzle DebugBlitzMissPuzzle with the queen picked up; 87 jumps Walls 2 > 3, moves and Undoes (the board must come back); 88 logs every stop from every square of each puzzle (BLITZ MOVES) to check against the solver;
/// 91 opens the in-puzzle selector, 90 the same on the next pack's chip, 89 jumps to Tricky 2's 6³ puzzle 5. `DebugBlitzPack` picks the pack (default starter) and
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
        if (which >= 92 && which <= 94) { yield return Hop(which); yield break; }
        if (which == 86)
        {
            // Open puzzle DebugBlitzMissPuzzle and pick the queen up (portal entry glow and dots), for screenshots
            yield return new WaitForSeconds(2f);
            int idx = PlayerPrefs.GetInt("DebugBlitzMissPuzzle", 1) - 1;
            QueenBlitz.Puzzle pz = QueenBlitz.Puzzles[idx];
            var c86 = new GameConfiguration { playerCount = 2, boardSize = QueenBlitz.BoardFor(pz.size), aiPlayerCount = 1, queenBlitz = true, blitzPuzzle = idx };
            c86.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
            c86.ApplyToLegacyFields();
            SceneController.Instance.LoadGameScene(c86);
            while (QueenBlitzDirector.Instance == null || GameStateManager.Instance.currentState != GameState.Playing) yield return null;
            yield return new WaitForSeconds(2f);
            ChessPiece q = ChessBoard.Instance.GetPieceAt(pz.line[0]);
            if (q != null) InputManager.Instance.DebugSelect(q);
            Debug.Log($"BLITZ DEMO: queen picked up on {pz.name}");
            // DebugCloseup 1: a camera close on the first portal's way in, from the front; 2: from behind and to the side
            int view = PlayerPrefs.GetInt("DebugCloseup", 0);
            if (view > 0 && pz.portals != null && pz.portals.Length > 0 && Camera.main != null)
            {
                GameObject container = GameObject.Find("Pieces Container");
                float cell = ChessBoard.Instance.cellSize;
                QueenBlitz.PortalPair pair = pz.portals[0];
                Vector3 local = ChessBoard.Instance.BoardToLocalPosition(pair.a) + Vector3.up * cell * 0.5f;
                Vector3 face = new Vector3(pair.faceA.x, 0f, pair.faceA.z);
                Vector3 side = Vector3.Cross(Vector3.up, face);
                Vector3 eye = local + (view == 1 ? face * 2.6f + side * 1.2f : -face * 2.2f + side * 1.8f) * cell + Vector3.up * cell * 1.6f;
                var cam = new GameObject("Demo Close-up").AddComponent<Camera>();
                cam.CopyFrom(Camera.main);
                cam.depth = Camera.main.depth + 1;
                cam.transform.position = container.transform.TransformPoint(eye);
                cam.transform.LookAt(container.transform.TransformPoint(local + face * cell * 0.4f));
            }
            yield break;
        }
        if (which == 87)
        {
            // Walls 2, jump to Walls 3 from the selector, make its first move, then Undo: the board must come back as it started
            yield return new WaitForSeconds(2f);
            QueenBlitz.Puzzle p2 = QueenBlitz.Puzzles[1];
            var cfg = new GameConfiguration { playerCount = 2, boardSize = QueenBlitz.BoardFor(p2.size), aiPlayerCount = 1, queenBlitz = true, blitzPuzzle = 1, saveSlot = SavedGames.Blitz };
            cfg.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
            cfg.ApplyToLegacyFields();
            SceneController.Instance.LoadGameScene(cfg);
            while (QueenBlitzDirector.Instance == null || GameStateManager.Instance.currentState != GameState.Playing) yield return null;
            yield return new WaitForSeconds(2f);
            QueenBlitzDirector.Instance.DebugJump(QueenBlitz.PackId, 2);
            yield return new WaitForSeconds(0.5f);
            while (QueenBlitzDirector.Instance == null || QueenBlitz.Index != 2 || GameStateManager.Instance.currentState != GameState.Playing) yield return null;
            yield return new WaitForSeconds(2f);
            BoardPosition[] line = QueenBlitz.Puzzles[2].line;
            Debug.Log($"BLITZ DEMO: undo check before, pawns {QueenBlitzDirector.Instance.PawnsLeft()}, queen {(ChessBoard.Instance.GetPieceAt(line[0]) != null)}");
            ChessBoard.Instance.MovePiece(line[0], line[1]);
            yield return new WaitForSeconds(2f);
            Debug.Log($"BLITZ DEMO: undo check after move, pawns {QueenBlitzDirector.Instance.PawnsLeft()}, can undo {UndoManager.Instance.CanUndo}");
            UndoManager.Instance.Undo();
            yield return new WaitForSeconds(1.5f);
            int pawns = QueenBlitzDirector.Instance.PawnsLeft();
            bool queen = ChessBoard.Instance.GetPieceAt(line[0]) != null;
            Debug.Log($"BLITZ DEMO: undo check {(pawns == QueenBlitz.Puzzles[2].pawns.Length && queen ? "PASS" : "FAIL")}, pawns {pawns}, queen back {queen}, moves {QueenBlitz.Moves}");
            yield break;
        }
        if (which == 88)
        {
            // Every stop from every free square of every puzzle in the pack, for comparing with the solver
            yield return new WaitForSeconds(2f);
            QueenBlitz.Puzzle opening = QueenBlitz.Puzzles[0];
            var setup = new GameConfiguration { playerCount = 2, boardSize = QueenBlitz.BoardFor(opening.size), aiPlayerCount = 1, queenBlitz = true, blitzPuzzle = 0 };
            setup.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
            setup.ApplyToLegacyFields();
            SceneController.Instance.LoadGameScene(setup);
            while (ChessBoard.Instance == null || GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing) yield return null;
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < QueenBlitz.Puzzles.Length; i++)
            {
                QueenBlitz.Index = i;
                QueenBlitz.Begin(i);
                QueenBlitz.Active = true;
                int n = QueenBlitz.Current.size;
                BoardDimensionsManager.Instance.SetBoardSize(QueenBlitz.BoardFor(n));
                yield return null;
                for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) for (int z = 0; z < n; z++)
                {
                    var c = new BoardPosition(x, y, z);
                    if (QueenBlitz.IsBlocked(c) || System.Array.IndexOf(QueenBlitz.Current.pawns, c) >= 0) continue;
                    Debug.Log($"BLITZ MOVES {i} {x}{y}{z} {QueenBlitz.DebugMoves(c)}");
                }
            }
            Debug.Log("BLITZ MOVES done");
            yield break;
        }
        if (which == 91 || which == 90 || which == 89)
        {
            // 91: open the puzzle selector on puzzle 2; 90: the same, showing the next pack's chip
            yield return new WaitForSeconds(2f);
            QueenBlitz.Puzzle puzzle = QueenBlitz.Puzzles[1];
            var config = new GameConfiguration { playerCount = 2, boardSize = QueenBlitz.BoardFor(puzzle.size), aiPlayerCount = 1, queenBlitz = true, blitzPuzzle = 1, saveSlot = SavedGames.Blitz };
            config.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
            config.ApplyToLegacyFields();
            SceneController.Instance.LoadGameScene(config);
            while (QueenBlitzDirector.Instance == null || GameStateManager.Instance.currentState != GameState.PiecePlacement && GameStateManager.Instance.currentState != GameState.Playing) yield return null;
            yield return new WaitForSeconds(3f);
            var packs = QueenBlitz.Available();
            string other = which == 90 && packs.Count > 1 ? packs[(packs.FindIndex(p => p.id == QueenBlitz.PackId) + 1) % packs.Count].id : null;
            QueenBlitzDirector.Instance.DebugOpenSelector(other);
            Debug.Log($"BLITZ DEMO: selector open on {other ?? QueenBlitz.PackId}");
            if (which == 89)
            {
                // Jump to puzzle 5 of Tricky 2 (a 6³ board) from the selector
                yield return new WaitForSeconds(2f);
                QueenBlitzDirector.Instance.DebugJump("tricky-2", 4);
                yield return new WaitForSeconds(6f);
                Debug.Log($"BLITZ DEMO: jumped to {QueenBlitz.PackId} {QueenBlitz.Index + 1}, board {BoardDimensionsManager.Instance.GetDimensions()}");
            }
            yield break;
        }
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

    /// <summary>94: open puzzle 1, Menu > Queen Blitz, then pick puzzle 3 in the picker,
    /// the way a player does; logs where it ends up</summary>
    private static IEnumerator Hop(int route)
    {
        yield return new WaitForSeconds(2f);
        QueenBlitz.Puzzle puzzle = QueenBlitz.Puzzles[0];
        var config = new GameConfiguration { playerCount = 2, boardSize = QueenBlitz.BoardFor(puzzle.size), aiPlayerCount = 1, queenBlitz = true, blitzPuzzle = 0, saveSlot = SavedGames.Blitz };
        config.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
        config.ApplyToLegacyFields();
        SceneController.Instance.LoadGameScene(config);
        float wait = 0f;
        while ((GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing) && wait < 20f) { wait += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(2f);
        Debug.Log($"BLITZ DEMO: hop 1, in {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, puzzle {QueenBlitz.Index + 1}, state {GameStateManager.Instance.currentState}");
        if (route == 93)
        {
            // Run out of moves: a sweep that takes nothing, until par is used up
            while (!QueenBlitz.OutOfMoves && QueenBlitz.Moves < 10)
            {
                BoardPosition at = puzzle.line[0];
                foreach (ChessPiece p in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None)) if (p.pieceType == ChessPieceType.Queen && !p.IsInTray()) at = p.CurrentPosition;
                ChessPiece queen = ChessBoard.Instance.GetPieceAt(at);
                BoardPosition to = queen.GetValidMoves().Find(c => ChessBoard.Instance.GetPieceAt(c) == null && System.Linq.Enumerable.All(QueenBlitz.Between(at, c), b => ChessBoard.Instance.GetPieceAt(b) == null));
                while (!GameStateManager.Instance.CanMovePieces()) yield return null;
                ChessBoard.Instance.MovePiece(at, to);
                yield return new WaitForSeconds(1.5f);
            }
            yield return new WaitForSeconds(4f);
            Debug.Log($"BLITZ DEMO: hop out of moves {QueenBlitz.OutOfMoves}, state {GameStateManager.Instance.currentState}");
            FindFirstObjectByType<RematchUI>().DebugPuzzles();
        }
        else if (route == 92) SceneController.Instance.LoadMainMenu(); // Menu > Main Menu
        else GameMenuUI.Instance.DebugBlitzPicker();
        yield return new WaitForSeconds(4f);
        NeonMenu menu = FindFirstObjectByType<NeonMenu>();
        Debug.Log($"BLITZ DEMO: hop 2, in {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, menu {(menu != null)}");
        if (menu != null && route == 92) { menu.DebugBlitzFromMenu(); yield return new WaitForSeconds(1f); }
        if (menu != null) menu.DebugStartBlitz(2);
        for (int i = 0; i < 8; i++)
        {
            yield return new WaitForSeconds(1f);
            Debug.Log($"BLITZ DEMO: hop 3 +{i + 1}s, in {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, puzzle {QueenBlitz.Index + 1}, active {QueenBlitz.Active}, state {(GameStateManager.Instance != null ? GameStateManager.Instance.currentState.ToString() : "none")}");
        }
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
