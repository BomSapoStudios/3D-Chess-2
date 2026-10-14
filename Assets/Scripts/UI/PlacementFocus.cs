using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// While pieces are being placed, hide every floor square outside the placing player's
/// starting zone, and that player's own 3D bench (they use the on-screen one), so the squares you can use
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
        // A player who has tapped Ready is done: their bench (and any unused pieces) goes away
        if (placing && PlacementManager.Instance != null)
        {
            foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
            {
                PieceTray tray = PieceTray.GetTrayForColor(c);
                if (tray == null) continue;
                bool done = PlacementManager.Instance.IsPlayerReady(c);
                if (tray.gameObject.activeSelf == done) tray.gameObject.SetActive(!done);
            }
        }

        PieceColor? color = null;
        if (placing && PlacementManager.Instance != null)
            color = PlacementManager.Instance.SelectedTrayColor ?? PlacementUI.WorkingOn ?? PlacementUI.InView ?? OnlyOwnPlayer();
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
        nextRefresh = Time.unscaledTime + 0.1f;

        if (floors.Count == 0) FindFloors();
        PlacementZone zone = PlacementManager.GetPlacementZoneForColor(color.Value);
        bool solid = BoardLook.SolidFloors;
        foreach (var (r, p) in floors)
        {
            if (r == null) continue;
            bool inZone = p.x >= zone.xMin && p.x <= zone.xMax && p.y >= zone.yMin && p.y <= zone.yMax && p.z >= zone.zMin && p.z <= zone.zMax;
            if (solid)
            {
                // Solid floors stay whole (a board with holes in it reads as broken); squares
                // outside the starting zone are just dimmed so the usable ones stand out
                SetVisible(r, true);
                Dim(r, !inZone);
            }
            else SetVisible(r, inZone);
        }
        foreach (ChessPiece piece in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
        {
            if (piece.GetComponentInParent<PieceTray>() != null) continue; // benches are handled below
            // Pieces on the board always show (including ones just moved off a hidden bench)
            foreach (Renderer r in piece.GetComponentsInChildren<Renderer>()) SetVisible(r, true);
        }

        // The in-view player's 3D bench goes away (they pick from the on-screen bench), so it never
        // sits between the camera and their squares. Taps pass through it too.
        foreach (PieceColor c in System.Enum.GetValues(typeof(PieceColor)))
        {
            PieceTray tray = PieceTray.GetTrayForColor(c);
            if (tray == null) continue;
            bool show = c != color.Value;
            foreach (Renderer r in tray.GetComponentsInChildren<Renderer>(true)) SetVisible(r, show);
            foreach (Collider col in tray.GetComponentsInChildren<Collider>(true)) SetEnabled(col, show);
        }
    }

    /// <summary>When this device has exactly one human player still setting up (Quick Play, online), that player</summary>
    private static PieceColor? OnlyOwnPlayer()
    {
        if (PlayerManager.Instance == null || PlacementManager.Instance == null) return null;
        PieceColor? only = null;
        foreach (PieceColor c in PlayerManager.Instance.GetActivePlayers())
        {
            bool human = TurnManager.Instance == null || !TurnManager.Instance.IsPlayerAI(c);
            if (!human || !OnlineSession.CanControl(c) || PlacementManager.Instance.IsPlayerReady(c)) continue;
            if (only.HasValue) return null;
            only = c;
        }
        return only;
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

    private readonly HashSet<Collider> disabled = new HashSet<Collider>();

    private void SetEnabled(Collider c, bool on)
    {
        if (!on && c.enabled) { c.enabled = false; disabled.Add(c); }
        else if (on && disabled.Remove(c)) c.enabled = true;
    }

    private readonly HashSet<Renderer> dimmed = new HashSet<Renderer>();
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Dim(Renderer r, bool dim)
    {
        if (dim && !dimmed.Contains(r))
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(ColorId, Color.Lerp(r.sharedMaterial.color, NeonTheme.Ground, 0.4f));
            r.SetPropertyBlock(block);
            dimmed.Add(r);
        }
        else if (!dim && dimmed.Remove(r)) r.SetPropertyBlock(null);
    }

    private void RestoreAll()
    {
        foreach (Renderer r in dimmed) if (r != null) r.SetPropertyBlock(null);
        dimmed.Clear();
        foreach (Renderer r in hidden) if (r != null) r.enabled = true;
        hidden.Clear();
        foreach (Collider c in disabled) if (c != null) c.enabled = true;
        disabled.Clear();
    }
}
