using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// While pieces are being placed, hide every floor square outside the placing player's
/// starting zone, and the other players' pieces, so the bench and the squares you can use
/// stand out from any angle. Everything comes back when placement ends.
/// Only re-enables what it hid itself, so the Floors buttons keep working alongside it.
/// </summary>
public class PlacementFocus : MonoBehaviour
{
    private readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
    private readonly List<(Renderer renderer, BoardPosition pos)> floors = new List<(Renderer, BoardPosition)>();
    private PieceColor? shownFor;
    private float nextRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("Placement Focus");
        go.AddComponent<PlacementFocus>();
        DontDestroyOnLoad(go);
    }

    private void LateUpdate()
    {
        bool placing = GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces() && ChessBoard.Instance != null;
        PieceColor? color = null;
        if (placing && PlacementManager.Instance != null)
            color = PlacementManager.Instance.SelectedTrayColor ?? PlacementUI.WorkingOn;
        if (!color.HasValue)
        {
            shownFor = null;
            RestoreAll();
            if (!placing) floors.Clear(); // a new game builds new floors
            return;
        }

        // Only redo the work when the player changes, or a few times a second for pieces just placed
        if (color == shownFor && Time.unscaledTime < nextRefresh) return;
        shownFor = color;
        nextRefresh = Time.unscaledTime + 0.3f;

        if (floors.Count == 0) FindFloors();
        PlacementZone zone = PlacementManager.GetPlacementZoneForColor(color.Value);
        foreach (var (r, p) in floors)
        {
            if (r == null) continue;
            bool inZone = p.x >= zone.xMin && p.x <= zone.xMax && p.y >= zone.yMin && p.y <= zone.yMax && p.z >= zone.zMin && p.z <= zone.zMax;
            SetVisible(r, inZone);
        }
        foreach (ChessPiece piece in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
        {
            BoardPosition pos = piece.CurrentPosition;
            bool onBoard = pos.IsValid() && ChessBoard.Instance.GetPieceAt(pos) == piece;
            bool visible = !onBoard || piece.pieceColor == color.Value;
            foreach (Renderer r in piece.GetComponentsInChildren<Renderer>()) SetVisible(r, visible);
        }
    }

    // Floors are named Floor_x_y_z by EmergencyChessBoard
    private void FindFloors()
    {
        foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string[] parts = r.name.Split('_');
            if (parts.Length == 4 && parts[0] == "Floor" && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y) && int.TryParse(parts[3], out int z))
                floors.Add((r, new BoardPosition(x, y, z)));
        }
    }

    private void SetVisible(Renderer r, bool visible)
    {
        if (!visible && r.enabled) { r.enabled = false; hidden.Add(r); }
        else if (visible && hidden.Remove(r)) r.enabled = true;
    }

    private void RestoreAll()
    {
        if (hidden.Count == 0) return;
        foreach (Renderer r in hidden) if (r != null) r.enabled = true;
        hidden.Clear();
    }
}
