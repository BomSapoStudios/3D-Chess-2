#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Development-only check of the robot's move choice on set positions.
/// Turn on with `defaults write BomSapo.Chess3D DebugAISelfTest -int 1` on the simulator and start
/// Quick Play. Once play starts the board is cleared and each position is set up and solved at
/// each difficulty. Results are logged as "AI SELF-TEST".
/// </summary>
public class AISelfTest : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugAISelfTest", 0) != 1) return;
        var go = new GameObject("AI Self Test");
        DontDestroyOnLoad(go);
        go.AddComponent<AISelfTest>();
    }

    private readonly List<string> failures = new List<string>();
    private int setupCount;
    private int passed;
    private bool started;

    private void Update()
    {
        if (started || ChessBoard.Instance == null || PlacementManager.Instance == null || GameStateManager.Instance == null) return;
        started = true;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(2f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSeconds(1f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        float wait = 0f;
        while (GameStateManager.Instance.currentState != GameState.Playing && wait < 120f) { wait += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(4f); // let the robot's first reply finish

        // 1. A free queen: everyone but Easy should take it
        Setup(() =>
        {
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<King>(PieceColor.Black, P(3, 3, 3));
            Put<Rook>(PieceColor.White, P(0, 2, 1));
            Put<Queen>(PieceColor.Black, P(3, 2, 1));
        });
        foreach (AIDifficulty d in new[] { AIDifficulty.Medium, AIDifficulty.Hard })
        {
            AIMove m = Solve(PieceColor.White, d, out _);
            Expect(m != null && m.toPosition == P(3, 2, 1), $"{d}: should take the free queen, chose {m}");
        }

        // 2. A poisoned pawn: taking it with the queen loses the queen to a rook
        Setup(() =>
        {
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<King>(PieceColor.Black, P(3, 1, 3));
            Put<Queen>(PieceColor.White, P(0, 3, 0));
            Put<Pawn>(PieceColor.Black, P(3, 3, 0));
            Put<Rook>(PieceColor.Black, P(3, 0, 0)); // guards the pawn along y
        });
        foreach (AIDifficulty d in new[] { AIDifficulty.Medium, AIDifficulty.Hard })
        {
            AIMove m = Solve(PieceColor.White, d, out _);
            Expect(m != null && !(m.piece is Queen && m.toPosition == P(3, 3, 0)), $"{d}: shouldn't trade the queen for a pawn, chose {m}");
        }

        // 3. Its own queen is attacked by a pawn: Medium and Hard move it (or protect it by taking the attacker)
        Setup(() =>
        {
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<King>(PieceColor.Black, P(3, 3, 3));
            Put<Queen>(PieceColor.White, P(2, 1, 2));
            Put<Pawn>(PieceColor.Black, P(3, 2, 2));
        });
        foreach (AIDifficulty d in new[] { AIDifficulty.Medium, AIDifficulty.Hard })
        {
            AIMove m = Solve(PieceColor.White, d, out _);
            bool queenSafe = m != null && QueenSafeAfter(m);
            Expect(queenSafe, $"{d}: should not leave its queen to be taken, chose {m}");
        }

        // 4. Mate in one, if this position has one: Hard finds it
        Setup(() =>
        {
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<King>(PieceColor.Black, P(3, 3, 3));
            Put<Rook>(PieceColor.White, P(0, 2, 3));
            Put<Rook>(PieceColor.White, P(2, 0, 3));
            Put<Queen>(PieceColor.White, P(1, 2, 1));
        });
        List<AIMove> mates = MatingMoves(PieceColor.White);
        if (mates.Count == 0) Debug.Log("AI SELF-TEST: position 4 has no mate in one; skipped");
        else
        {
            AIMove m = Solve(PieceColor.White, AIDifficulty.Hard, out _);
            Expect(m != null && mates.Exists(x => x.piece == m.piece && x.toPosition == m.toPosition), $"Hard: should play one of {mates.Count} mates in one, chose {m}");
        }

        // 5. Every choice is legal, and Hard stays inside its time
        Setup(() =>
        {
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<King>(PieceColor.Black, P(3, 3, 3));
            Put<Queen>(PieceColor.White, P(1, 0, 0));
            Put<Rook>(PieceColor.White, P(0, 3, 0));
            Put<Bishop>(PieceColor.White, P(1, 0, 1));
            Put<Knight>(PieceColor.White, P(0, 1, 2));
            Put<Queen>(PieceColor.Black, P(3, 2, 3));
            Put<Rook>(PieceColor.Black, P(2, 3, 3));
            Put<Knight>(PieceColor.Black, P(3, 3, 1));
        });
        foreach (AIDifficulty d in new[] { AIDifficulty.Easy, AIDifficulty.Medium, AIDifficulty.Hard })
        {
            AIMove m = Solve(PieceColor.White, d, out float seconds);
            Expect(m != null && m.piece.GetLegalMoves().Contains(m.toPosition), $"{d}: move must be legal, chose {m}");
            Expect(seconds < RobotSearch.Settings.For(d).seconds + 0.6f, $"{d}: took {seconds:F2}s");
        }

        Debug.Log($"AI SELF-TEST: {passed} passed, {failures.Count} failed");
        foreach (string f in failures) Debug.Log("AI SELF-TEST FAIL: " + f);
    }

    // ───────── helpers ─────────

    private AIMove Solve(PieceColor color, AIDifficulty d, out float seconds)
    {
        var search = new RobotSearch();
        var sw = Stopwatch.StartNew();
        AIMove m = search.FindMove(color, RobotSearch.Settings.For(d));
        seconds = (float)sw.Elapsed.TotalSeconds;
        Debug.Log($"AI SELF-TEST: {d} chose {m} ({search.Nodes} positions, depth {search.DepthReached}, {seconds:F2}s)");
        return m;
    }

    private bool QueenSafeAfter(AIMove m)
    {
        var board = ChessBoard.Instance;
        board.EnableSimulationMode();
        ChessPiece victim = board.GetPieceAt(m.toPosition);
        board.SetPieceAt(m.fromPosition, null);
        board.SetPieceAt(m.toPosition, m.piece);
        m.piece.SetCurrentPosition(m.toPosition);
        ChessPiece queen = FindPiece(PieceColor.White, ChessPieceType.Queen);
        bool safe = queen != null && !AttackedBy(queen.CurrentPosition, PieceColor.Black);
        board.SetPieceAt(m.toPosition, victim);
        board.SetPieceAt(m.fromPosition, m.piece);
        m.piece.SetCurrentPosition(m.fromPosition);
        board.DisableSimulationMode();
        return safe;
    }

    private List<AIMove> MatingMoves(PieceColor color)
    {
        var mates = new List<AIMove>();
        var board = ChessBoard.Instance;
        PieceColor enemy = color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        foreach (ChessPiece p in AllPieces())
        {
            if (p.pieceColor != color) continue;
            BoardPosition from = p.CurrentPosition;
            foreach (BoardPosition to in p.GetLegalMoves())
            {
                ChessPiece victim = board.GetPieceAt(to);
                board.EnableSimulationMode();
                board.SetPieceAt(from, null); board.SetPieceAt(to, p); p.SetCurrentPosition(to);
                CheckDetectionManager.Instance.InvalidateCache();
                bool mate = CheckDetectionManager.Instance.IsKingInCheck(enemy) && !GameEndDetectionManager.Instance.HasLegalMoves(enemy);
                board.SetPieceAt(to, victim); board.SetPieceAt(from, p); p.SetCurrentPosition(from);
                board.DisableSimulationMode();
                CheckDetectionManager.Instance.InvalidateCache();
                if (mate) mates.Add(new AIMove { piece = p, fromPosition = from, toPosition = to });
            }
        }
        return mates;
    }

    private bool AttackedBy(BoardPosition square, PieceColor color)
    {
        foreach (ChessPiece p in AllPieces())
            if (p.pieceColor == color && p.GetAttackSquares().Contains(square)) return true;
        return false;
    }

    private ChessPiece FindPiece(PieceColor color, ChessPieceType type)
    {
        foreach (ChessPiece p in AllPieces()) if (p.pieceColor == color && p.pieceType == type) return p;
        return null;
    }

    private static IEnumerable<ChessPiece> AllPieces()
    {
        Vector3Int d = BoardDimensionsManager.Instance.GetDimensions();
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null) yield return p;
        }
    }

    private void Setup(System.Action place)
    {
        var board = ChessBoard.Instance;
        board.EnableSimulationMode();
        foreach (ChessPiece p in new List<ChessPiece>(AllPieces()))
        {
            board.SetPieceAt(p.CurrentPosition, null);
            p.gameObject.SetActive(false);
        }
        place();
        board.DisableSimulationMode();
        CheckDetectionManager.Instance.InvalidateCache();
        // White is to move, so Black's king must not already be under attack
        ChessPiece blackKing = FindPiece(PieceColor.Black, ChessPieceType.King);
        if (blackKing != null && AttackedBy(blackKing.CurrentPosition, PieceColor.White))
            failures.Add($"Test position {++setupCount} starts with Black already in check (fix the position)");
        else ++setupCount;
    }

    private static T Put<T>(PieceColor color, BoardPosition at) where T : ChessPiece
    {
        var go = new GameObject($"Test {color} {typeof(T).Name}");
        go.SetActive(false);
        go.AddComponent<SphereCollider>();
        T piece = go.AddComponent<T>();
        piece.pieceColor = color;
        piece.pieceType = PieceModels.TypeOf(piece);
        go.SetActive(true);
        piece.ApplyMaterial();
        ChessBoard.Instance.SetPieceAt(at, piece);
        piece.SetCurrentPosition(at);
        return piece;
    }

    private void Expect(bool ok, string message)
    {
        if (ok) passed++;
        else failures.Add(message);
    }

    private static BoardPosition P(int x, int y, int z) => new BoardPosition(x, y, z);
}
#endif
