#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

/// <summary>
/// Development-only slow-motion replay of the move, capture and conquest effects, for checking
/// how they look. Turn on with `defaults write BomSapo.Chess3D DebugEffectsDemo -int 1` on the
/// simulator and start Quick Play. Logs "EFFECTS DEMO: <step>" as each effect starts.
/// </summary>
public class EffectsDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugEffectsDemo", 0) != 1) return;
        var go = new GameObject("Effects Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<EffectsDemo>();
    }

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
        while (GameStateManager.Instance.currentState != GameState.Playing) yield return null;
        yield return new WaitForSeconds(2f);

        var camera = FindFirstObjectByType<CameraController>();
        if (camera != null) camera.distance = 13f;
        var board = ChessBoard.Instance;

        for (int round = 0; round < 20; round++)
        {
            // Capture: a rook hops across and the pawn shatters
            Clear();
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<King>(PieceColor.Black, P(3, 3, 3));
            Put<Rook>(PieceColor.White, P(0, 2, 1));
            Put<Pawn>(PieceColor.Black, P(3, 2, 1));
            board.DisableSimulationMode();
            Time.timeScale = 0.3f;
            Debug.Log("EFFECTS DEMO: capture");
            board.MovePiece(P(0, 2, 1), P(3, 2, 1));
            yield return new WaitForSecondsRealtime(6f);

            // Conquest: Black's king breaks into orbs that turn its pieces White
            Clear();
            ChessPiece king = Put<King>(PieceColor.Black, P(3, 3, 3));
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<Knight>(PieceColor.Black, P(1, 3, 0));
            Put<Queen>(PieceColor.Black, P(3, 0, 2));
            Put<Bishop>(PieceColor.Black, P(2, 1, 3));
            Put<Pawn>(PieceColor.Black, P(0, 3, 3));
            board.DisableSimulationMode();
            Debug.Log("EFFECTS DEMO: conquest");
            PlayerManager.Instance.TransferPiecesToPlayer(PieceColor.Black, PieceColor.White);
            yield return new WaitForSecondsRealtime(8f);

            // Promotion: a pawn morphs into a queen
            Clear();
            Put<King>(PieceColor.White, P(0, 0, 0));
            Put<King>(PieceColor.Black, P(3, 3, 3));
            ChessPiece pawn = Put<Pawn>(PieceColor.White, P(3, 1, 1));
            board.SetPieceAt(P(3, 1, 1), null);
            ChessPiece queen = Put<Queen>(PieceColor.White, P(3, 1, 1));
            board.DisableSimulationMode();
            Debug.Log("EFFECTS DEMO: promotion");
            PieceEffects.Promote(pawn, queen);
            Destroy(pawn.gameObject);
            yield return new WaitForSecondsRealtime(6f);
            Time.timeScale = 1f;
        }
    }

    private static void Clear()
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
            Destroy(piece.gameObject);
        }
    }

    private static T Put<T>(PieceColor color, BoardPosition at) where T : ChessPiece
    {
        var go = new GameObject($"Demo {color} {typeof(T).Name}");
        go.SetActive(false);
        GameObject container = GameObject.Find("Pieces Container");
        if (container != null) go.transform.SetParent(container.transform, false);
        go.transform.localPosition = ChessBoard.Instance.BoardToLocalPosition(at);
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

    private static BoardPosition P(int x, int y, int z) => new BoardPosition(x, y, z);
}
#endif
