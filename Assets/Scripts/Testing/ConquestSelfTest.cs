#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Development-only test of multiplayer checkmate ("conquest").
/// Turn on with `defaults write BomSapo.Chess3D DebugConquestSelfTest -int 1` on the simulator and
/// start a 6-player game. The test places White's pieces, waits for play to start, then builds two
/// real checkmates and runs them through the game's own end-of-move logic:
///   1. White mates Purple while it is NOT Purple's turn next.
///   2. White mates Green when Green IS next to move (its turn must then be skipped).
/// It checks that the checkmating player conquers: the loser's king is removed, every other piece
/// becomes the winner's color (and plays like it), the loser is out, and the game carries on.
/// Results are logged as "CONQUEST SELF-TEST".
/// </summary>
public class ConquestSelfTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugConquestSelfTest", 0) != 1) return;
        var go = new GameObject("Conquest Self Test");
        DontDestroyOnLoad(go);
        go.AddComponent<ConquestSelfTest>();
    }

    private readonly List<string> failures = new List<string>();
    private int passed;
    private bool started;

    private void Update()
    {
        if (started || ChessBoard.Instance == null || PlacementManager.Instance == null || GameStateManager.Instance == null) return;
        if (PlayerManager.Instance == null || PlayerManager.Instance.GetActivePlayerCount() < 6) return;
        started = true;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        // Get through placement: White is the human seat
        yield return new WaitForSeconds(2f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSeconds(1f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        float wait = 0f;
        while (GameStateManager.Instance.currentState != GameState.Playing && wait < 120f) { wait += Time.deltaTime; yield return null; }
        if (GameStateManager.Instance.currentState != GameState.Playing) { Debug.Log("CONQUEST SELF-TEST: game never started"); yield break; }
        yield return new WaitForSeconds(1f);

        var board = ChessBoard.Instance;
        var turnChanged = typeof(GameEndDetectionManager).GetMethod("OnTurnChanged", BindingFlags.NonPublic | BindingFlags.Instance);

        // ───── 1. White mates Purple; Black is next to move ─────
        ClearBoard();
        var kings = new Dictionary<PieceColor, BoardPosition>
        {
            { PieceColor.White, P(0, 0, 3) }, { PieceColor.Black, P(0, 3, 3) }, { PieceColor.Green, P(3, 0, 3) },
            { PieceColor.Yellow, P(3, 3, 3) }, { PieceColor.Orange, P(3, 5, 3) }, { PieceColor.Purple, P(7, 7, 7) },
        };
        foreach (var k in kings) Put<King>(k.Key, k.Value);
        // Four White rooks along Z cover the corner king and all 7 escape squares
        Put<Rook>(PieceColor.White, P(7, 7, 0));
        Put<Rook>(PieceColor.White, P(6, 6, 0));
        Put<Rook>(PieceColor.White, P(7, 6, 0));
        Put<Rook>(PieceColor.White, P(6, 7, 0));
        ChessPiece purpleKnight = Put<Knight>(PieceColor.Purple, P(0, 0, 7));
        ChessPiece purplePawn = Put<Pawn>(PieceColor.Purple, P(1, 1, 6));
        ChessPiece purpleQueen = Put<Queen>(PieceColor.Purple, P(2, 0, 7));
        board.DisableSimulationMode();
        CheckDetectionManager.Instance.InvalidateCache();

        Expect(CheckDetectionManager.Instance.IsKingInCheck(PieceColor.Purple), "Purple should be in check");
        Expect(!GameEndDetectionManager.Instance.HasLegalMoves(PieceColor.Purple), "Purple should have no legal moves (checkmate)");

        SetTurn(PieceColor.Black);
        turnChanged.Invoke(GameEndDetectionManager.Instance, new object[] { PieceColor.White, PieceColor.Black });

        Expect(PlayerManager.Instance.IsPlayerEliminated(PieceColor.Purple), "Purple should be eliminated");
        Expect(board.GetPieceAt(P(7, 7, 7)) == null, "Purple's king should be removed from the board");
        foreach (ChessPiece p in new[] { purpleKnight, purplePawn, purpleQueen })
        {
            Expect(p != null && p.pieceColor == PieceColor.White, $"Purple {p?.GetType().Name} should now be White");
            Expect(board.GetPieceAt(p.CurrentPosition) == p, $"Converted {p.GetType().Name} should stay on its square");
        }
        var pawnMoves = purplePawn.GetValidMoves();
        Expect(pawnMoves.Contains(P(2, 1, 6)), $"Converted pawn should now advance like White (+X); moves: {string.Join(", ", pawnMoves)}");
        Expect(PlayerManager.Instance.GetActivePlayerCount() == 5, $"5 players should remain, got {PlayerManager.Instance.GetActivePlayerCount()}");
        Expect(GameStateManager.Instance.currentState == GameState.Playing, "The game should continue");
        Expect(TurnManager.Instance.GetCurrentPlayer() == PieceColor.Black, "It should still be Black's turn");

        // Each piece keeps its old look until the conquest orb reaches it
        yield return new WaitForSeconds(3f);
        foreach (ChessPiece p in new[] { purpleKnight, purplePawn, purpleQueen })
            Expect(StripColorIs(p, NeonTheme.Cyan), $"Converted {p.GetType().Name} should glow White's color once the orb arrives");

        // ───── 2. White mates Green; Green is next to move ─────
        board.EnableSimulationMode();
        ClearBoard();
        var kings2 = new Dictionary<PieceColor, BoardPosition>
        {
            { PieceColor.White, P(3, 0, 3) }, { PieceColor.Black, P(3, 3, 3) },
            { PieceColor.Yellow, P(5, 3, 3) }, { PieceColor.Orange, P(5, 0, 3) }, { PieceColor.Green, P(0, 7, 7) },
        };
        foreach (var k in kings2) Put<King>(k.Key, k.Value);
        Put<Rook>(PieceColor.White, P(0, 7, 0));
        Put<Rook>(PieceColor.White, P(1, 6, 0));
        Put<Rook>(PieceColor.White, P(0, 6, 0));
        Put<Rook>(PieceColor.White, P(1, 7, 0));
        ChessPiece greenBishop = Put<Bishop>(PieceColor.Green, P(5, 0, 0));
        board.DisableSimulationMode();
        CheckDetectionManager.Instance.InvalidateCache();

        Expect(!GameEndDetectionManager.Instance.HasLegalMoves(PieceColor.Green) && CheckDetectionManager.Instance.IsKingInCheck(PieceColor.Green), "Green should be checkmated");
        SetTurn(PieceColor.Green);
        turnChanged.Invoke(GameEndDetectionManager.Instance, new object[] { PieceColor.White, PieceColor.Green });

        Expect(PlayerManager.Instance.IsPlayerEliminated(PieceColor.Green), "Green should be eliminated");
        Expect(greenBishop.pieceColor == PieceColor.White, "Green's bishop should go to White (who delivered mate), not anyone else");
        Expect(board.GetPieceAt(P(0, 7, 7)) == null, "Green's king should be removed");

        yield return null;
        yield return null;
        PieceColor next = TurnManager.Instance.GetCurrentPlayer();
        Expect(next != PieceColor.Green && next != PieceColor.Purple, $"Green's turn should be skipped, but it is {next}'s turn");
        Expect(PlayerManager.Instance.GetActivePlayerCount() == 4, $"4 players should remain, got {PlayerManager.Instance.GetActivePlayerCount()}");
        Expect(GameStateManager.Instance.currentState == GameState.Playing, "The game should still continue");

        Debug.Log($"CONQUEST SELF-TEST: {passed} passed, {failures.Count} failed");
        foreach (string f in failures) Debug.Log("CONQUEST SELF-TEST FAIL: " + f);
    }

    // ───────── helpers ─────────

    private readonly List<GameObject> made = new List<GameObject>();

    private void ClearBoard()
    {
        var board = ChessBoard.Instance;
        board.EnableSimulationMode();
        Vector3Int d = BoardDimensionsManager.Instance.GetDimensions();
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece piece = board.GetPieceAt(P(x, y, z));
            if (piece == null) continue;
            board.SetPieceAt(P(x, y, z), null);
            piece.gameObject.SetActive(false);   // off the board for the test
        }
    }

    private T Put<T>(PieceColor color, BoardPosition at) where T : ChessPiece
    {
        var go = new GameObject($"Test {color} {typeof(T).Name}");
        go.SetActive(false);
        go.AddComponent<SphereCollider>();
        T piece = go.AddComponent<T>();
        piece.pieceColor = color;
        piece.pieceType = PieceModels.TypeOf(piece);   // the game sets this when it creates pieces
        go.SetActive(true);
        piece.ApplyMaterial();
        ChessBoard.Instance.SetPieceAt(at, piece);
        piece.SetCurrentPosition(at);
        made.Add(go);
        return piece;
    }

    private void Diagnose()
    {
        var board = ChessBoard.Instance;
        Vector3Int d = BoardDimensionsManager.Instance.GetDimensions();
        var lines = new List<string>();
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece piece = board.GetPieceAt(P(x, y, z));
            if (piece != null) lines.Add($"{piece.pieceColor} {piece.GetType().Name}/{piece.pieceType} at {P(x, y, z)} thinks {piece.CurrentPosition}");
        }
        Debug.Log("CONQUEST SELF-TEST DIAG board: " + string.Join(" | ", lines));
        ChessPiece rook = board.GetPieceAt(P(7, 7, 0));
        if (rook != null) Debug.Log("CONQUEST SELF-TEST DIAG rook moves: " + string.Join(", ", rook.GetValidMoves()));
        Debug.Log($"CONQUEST SELF-TEST DIAG opponents of Purple: {string.Join(", ", PlayerManager.Instance.GetOpponents(PieceColor.Purple))}; attacked: {CheckDetectionManager.Instance.IsPositionUnderAttack(P(7, 7, 7), PieceColor.White)}");
    }

    private static void SetTurn(PieceColor color) => TurnManager.Instance.currentPlayer = color;

    private static bool StripColorIs(ChessPiece piece, Color expected)
    {
        foreach (MeshRenderer r in piece.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.name != PieceModels.StripName || r.sharedMaterial == null) continue;
            Color c = r.sharedMaterial.color;
            return Mathf.Abs(c.r - expected.r) < 0.02f && Mathf.Abs(c.g - expected.g) < 0.02f && Mathf.Abs(c.b - expected.b) < 0.02f;
        }
        return false;
    }

    private void Expect(bool ok, string message)
    {
        if (ok) passed++;
        else failures.Add(message);
    }

    private static BoardPosition P(int x, int y, int z) => new BoardPosition(x, y, z);
}
#endif
