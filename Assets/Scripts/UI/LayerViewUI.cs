using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Level buttons down the right edge of the game screen. Picking a level hides the
/// floors, pieces and move/placement dots on every other level, so the inside of the
/// cube can be seen and tapped. "All" shows everything again.
/// </summary>
public class LayerViewUI : MonoBehaviour
{
    private int selectedLevel = -1; // -1 = all levels
    private readonly HashSet<Renderer> hiddenRenderers = new HashSet<Renderer>();
    private readonly HashSet<Collider> disabledColliders = new HashSet<Collider>();
    private EmergencyChessBoard board;
    private readonly List<(Renderer renderer, int level)> floors = new List<(Renderer, int)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        GameObject go = new GameObject("Layer View UI");
        go.AddComponent<LayerViewUI>();
        DontDestroyOnLoad(go);
    }

    private int LevelCount
    {
        get
        {
            if (BoardDimensionsManager.Instance != null) return BoardDimensionsManager.Instance.GetDimensions().y;
            return 4;
        }
    }

    private bool InGame()
    {
        if (board == null) board = FindFirstObjectByType<EmergencyChessBoard>();
        if (board == null) return false;
        if (GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.GameOver) return false;
        return true;
    }

    private void LateUpdate()
    {
        if (!InGame())
        {
            selectedLevel = -1;
            hiddenRenderers.Clear();
            disabledColliders.Clear();
            floors.Clear();
            return;
        }
        if (selectedLevel < 0) return;

        // Floors are named Floor_x_y_z by EmergencyChessBoard (they live under "Board Rotator")
        if (floors.Count == 0)
        {
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string[] parts = r.name.Split('_');
                if (parts.Length == 4 && parts[0] == "Floor" && int.TryParse(parts[2], out int y))
                {
                    floors.Add((r, y));
                }
            }
        }
        foreach (var (r, y) in floors)
        {
            if (r != null) SetVisible(r, y == selectedLevel);
        }

        foreach (ChessPiece piece in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
        {
            BoardPosition pos = piece.CurrentPosition;
            bool onBoard = pos.IsValid() && !piece.IsInTray();
            bool visible = !onBoard || pos.y == selectedLevel;
            foreach (Renderer r in piece.GetComponentsInChildren<Renderer>()) SetVisible(r, visible);
            foreach (Collider c in piece.GetComponentsInChildren<Collider>()) SetEnabled(c, visible);
        }

        foreach (QueenBlitzBlock block in FindObjectsByType<QueenBlitzBlock>(FindObjectsSortMode.None))
        {
            HideIfOtherLevel(block.gameObject, block.at.y);
        }
        foreach (PlacementTargetData target in FindObjectsByType<PlacementTargetData>(FindObjectsSortMode.None))
        {
            HideIfOtherLevel(target.gameObject, target.targetPosition.y);
        }
        foreach (MoveTargetData target in FindObjectsByType<MoveTargetData>(FindObjectsSortMode.None))
        {
            HideIfOtherLevel(target.gameObject, target.targetPosition.y);
        }
    }

    private void HideIfOtherLevel(GameObject go, int level)
    {
        bool visible = level == selectedLevel;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) SetVisible(r, visible);
        foreach (Collider c in go.GetComponentsInChildren<Collider>()) SetEnabled(c, visible);
    }

    // Only re-enable what this script disabled, so other systems' visibility is left alone
    private void SetVisible(Renderer r, bool visible)
    {
        if (!visible && r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
        else if (visible && hiddenRenderers.Remove(r)) { r.enabled = true; }
    }

    private void SetEnabled(Collider c, bool enabled)
    {
        if (!enabled && c.enabled) { c.enabled = false; disabledColliders.Add(c); }
        else if (enabled && disabledColliders.Remove(c)) { c.enabled = true; }
    }

    private void ShowAll()
    {
        selectedLevel = -1;
        foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
        foreach (Collider c in disabledColliders) if (c != null) c.enabled = true;
        hiddenRenderers.Clear();
        disabledColliders.Clear();
    }

    private void OnGUI()
    {
        if (!InGame() || TurnPlannerUI.IsOpen) return;

        Vector2 ui = TouchGUI.Begin();
        GUI.skin.button.fontSize = 14;

        int levels = LevelCount;
        float width = 72f;
        float gap = 4f;
        // Leave room at the bottom for the Menu button
        // Below the ? button; leave room at the bottom for the Menu button
        float y = 76f;
        float height = Mathf.Min(40f, (ui.y - y - 20f - 70f) / (levels + 1) - gap);
        float x = ui.x - width - 10f;

        NeonTheme.SceneLabel(new Rect(x, y, width, 18f), "Floors", NeonTheme.GUILabelStyle(NeonTheme.Cyan, 14, true, TextAnchor.MiddleCenter));
        y += 20f;
        if (TouchGUI.NeonButton(new Rect(x, y, width, height), "All", NeonTheme.Pink, NeonTheme.Cyan, outlined: selectedLevel >= 0, fontSize: 16))
        {
            ShowAll();
        }
        // Highest level at the top, like floors of a building
        for (int level = levels - 1; level >= 0; level--)
        {
            y += height + gap;
            if (TouchGUI.NeonButton(new Rect(x, y, width, height), $"{level + 1}", NeonTheme.Pink, NeonTheme.Cyan, outlined: selectedLevel != level, fontSize: 16))
            {
                ShowAll();
                selectedLevel = level;
            }
        }

        if (TouchGUI.NeonButton(new Rect(x, ui.y - 56f, width, 44f), "Menu", NeonTheme.Cyan, NeonTheme.Pink, fontSize: 16))
        {
            if (GameMenuUI.Instance != null) GameMenuUI.Instance.ShowMenu();
        }

        TouchGUI.End();
    }
}
