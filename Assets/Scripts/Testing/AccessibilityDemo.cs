#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Development-only scenes for checking the Accessibility options on the simulator.
/// `defaults write BomSapo.Chess3D DebugA11yDemo -int N`, set the A11y.* switches the same way,
/// then launch. Logs "A11Y DEMO: ready" when the scene is set for a screenshot.
///   1  Quick Play, White in check from a rook (symbols, check and threat markers, turn banner)
///   2  Quick Play setup with a White piece picked up (placement dots and dimmed zones)
///   3  Six players on the big cube, White placed (all six symbols, clocks on)
///   4  Quick Play, White resigns (the result panel)
///   5  Quick Play with the How to Play guide open
/// </summary>
public class AccessibilityDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        int mode = PlayerPrefs.GetInt("DebugA11yDemo", 0);
        if (mode == 0 || FindFirstObjectByType<AccessibilityDemo>() != null) return;
        PlayerPrefs.SetInt("HowToPlaySeen", mode == 5 ? 0 : 1);
        var go = new GameObject("Accessibility Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<AccessibilityDemo>().StartCoroutine(Run(mode));
    }

    private static IEnumerator Run(int mode)
    {
        yield return new WaitForSeconds(2f);
        if (SceneController.Instance == null) yield break;
        if (mode == 3)
        {
            var config = new GameConfiguration
            {
                playerCount = 6,
                boardSize = BoardSize.Large8x8x8,
                aiDifficulty = AIDifficulty.Easy,
                playerTypes = new List<PlayerType> { PlayerType.Human, PlayerType.Computer, PlayerType.Computer, PlayerType.Computer, PlayerType.Computer, PlayerType.Computer },
                aiPlayerCount = 5,
                enableTimedPlay = true,
                timePerPlayerMinutes = 10,
            };
            SceneController.Instance.LoadGameScene(config);
        }
        else SceneController.Instance.QuickStartHumanVsAI(PieceColor.White, AIDifficulty.Easy);

        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1.5f);
        if (mode == 2)
        {
            PieceTray tray = PieceTray.GetTrayForColor(PieceColor.White);
            ChessPiece pick = tray != null ? tray.GetComponentInChildren<ChessPiece>() : null;
            if (pick != null) PlacementManager.Instance.OnTrayPieceClicked(pick);
            yield return new WaitForSeconds(1f);
            Debug.Log("A11Y DEMO: ready");
            yield break;
        }
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSeconds(4f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        while (GameStateManager.Instance.CanPlacePieces()) yield return null;
        yield return new WaitForSeconds(2f);

        if (mode == 4)
        {
            GameEndDetectionManager.Instance.HandleForfeit(PieceColor.White);
            yield return new WaitForSeconds(2.5f);
        }
        else if (mode == 1)
        {
            // White's king on the bottom floor, a black rook straight above it
            Clear();
            Put<King>(PieceColor.White, P(1, 0, 0));
            Put<Queen>(PieceColor.White, P(2, 0, 1));
            Put<Knight>(PieceColor.White, P(0, 0, 2));
            Put<King>(PieceColor.Black, P(3, 3, 3));
            Put<Rook>(PieceColor.Black, P(1, 3, 0));
            Put<Bishop>(PieceColor.Black, P(3, 2, 2));
            ChessBoard.Instance.DisableSimulationMode();
            var camera = FindFirstObjectByType<CameraController>();
            if (camera != null) camera.distance = 15f;
            yield return new WaitForSeconds(0.5f);
            CheckDetectionManager.Instance.InvalidateCache();
            CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
            yield return new WaitForSeconds(2f);
        }
        Debug.Log("A11Y DEMO: ready");
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
