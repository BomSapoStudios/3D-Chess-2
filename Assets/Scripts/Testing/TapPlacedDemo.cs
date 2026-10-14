#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using UnityEngine;

/// <summary>Development-only (DebugTapPlaced 1): place White's pieces, tap one on screen, check it can go back to the bench. Logs "TAP PLACED".</summary>
public class TapPlacedDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugTapPlaced", 0) != 1 || FindFirstObjectByType<TapPlacedDemo>() != null) return;
        var go = new GameObject("Tap Placed Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<TapPlacedDemo>().StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        yield return new WaitForSeconds(2f);
        SceneController.Instance.QuickStartHumanVsAI(PieceColor.White, AIDifficulty.Easy);
        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSeconds(5f);
        ChessPiece target = null;
        Vector3Int d = BoardDimensionsManager.Instance.GetDimensions();
        for (int x = 0; x < d.x && target == null; x++)
        for (int y = 0; y < d.y && target == null; y++)
        for (int z = 0; z < d.z && target == null; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null && p.pieceColor == PieceColor.White && p.pieceType == ChessPieceType.Rook) target = p;
        }
        var cols = target.GetComponentsInChildren<Collider>(true);
        string info = "";
        foreach (var c in cols) info += $"{c.GetType().Name} on {c.name} enabled {c.enabled} active {c.gameObject.activeInHierarchy} bounds {c.bounds.center}/{c.bounds.size}; ";
        Debug.Log($"TAP PLACED: colliders: {info} piece pos {target.transform.position}");
        Vector3 aim = cols.Length > 0 ? cols[0].bounds.center : target.transform.position;
        Vector3 screen = Camera.main.WorldToScreenPoint(aim);
        var hits = Physics.RaycastAll(Camera.main.ScreenPointToRay(screen));
        string hitInfo = "";
        foreach (var h in hits) hitInfo += $"{h.collider.name}@{h.distance:F1} ";
        Debug.Log($"TAP PLACED: screen {screen}, cam {Camera.main.name}, hits {hits.Length}: {hitInfo}");
        var method = typeof(InputManager).GetMethod("ProcessTouch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Invoke(InputManager.Instance, new object[] { (Vector2)screen });
        yield return null;
        Debug.Log($"TAP PLACED: tapped {target.pieceType} at {target.CurrentPosition}, can send back {PlacementManager.Instance.CanReturnSelectedToTray}");
        if (PlacementManager.Instance.CanReturnSelectedToTray)
        {
            int before = PieceTray.GetTrayForColor(PieceColor.White).GetPieceCount();
            PlacementManager.Instance.ReturnSelectedToTray();
            yield return null;
            Debug.Log($"TAP PLACED: bench {before} -> {PieceTray.GetTrayForColor(PieceColor.White).GetPieceCount()}, square now {(ChessBoard.Instance.GetPieceAt(new BoardPosition(0,0,0)) != null)}");
        }
    }
}
#endif
