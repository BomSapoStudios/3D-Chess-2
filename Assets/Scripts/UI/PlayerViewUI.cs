using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A row of player view buttons along the bottom of the game screen. Tapping one swings the
/// camera to look at the board from that player's side, as if sitting behind their pieces.
/// Yellow (bottom face) looks up from underneath; Orange (top face) looks down from above.
/// </summary>
public class PlayerViewUI : MonoBehaviour
{
    private const float SideElevation = 32f; // degrees above the board for side players

    private EmergencyChessBoard board;
    private CameraController cameraController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("Player View UI");
        go.AddComponent<PlayerViewUI>();
        DontDestroyOnLoad(go);
    }

    private bool InGame()
    {
        if (board == null) board = FindFirstObjectByType<EmergencyChessBoard>();
        if (board == null || ChessBoard.Instance == null) return false;
        if (GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.GameOver) return false;
        return true;
    }

    private List<PieceColor> Players()
    {
        if (PlayerManager.Instance != null) return PlayerManager.Instance.GetAllPlayers();
        return new List<PieceColor> { PieceColor.White, PieceColor.Black };
    }

    /// <summary>Set before a game starts to begin looking from this side (e.g. playing Black in Quick Play)</summary>
    public static PieceColor? StartView;
    private float inGameSince = -1f;

    private void Update()
    {
        if (!InGame()) { inGameSince = -1f; return; }
        if (inGameSince < 0f) inGameSince = Time.time;
        // Wait a moment for the camera to frame the board, then swing round
        if (StartView.HasValue && Time.time - inGameSince > 0.8f)
        {
            if (StartView.Value != PieceColor.White) ViewFrom(StartView.Value);
            StartView = null;
        }
    }

    private void OnGUI()
    {
        if (!InGame() || TurnPlannerUI.IsOpen) return;
        List<PieceColor> players = Players();
        if (players.Count == 0) return;

        Vector2 ui = TouchGUI.Begin();
        const float w = 74f, h = 40f, gap = 6f;
        float total = players.Count * w + (players.Count - 1) * gap + 52f;
        float x = (ui.x - total) / 2f;
        float y = ui.y - h - 12f;

        GUI.Label(new Rect(x, y, 48f, h), "View", NeonTheme.GUILabelStyle(NeonTheme.Cyan, 14, true, TextAnchor.MiddleCenter));
        x += 52f;
        foreach (PieceColor color in players)
        {
            bool out_ = PlayerManager.Instance != null && PlayerManager.Instance.IsPlayerEliminated(color);
            Color tint = color == PieceColor.White ? NeonTheme.Cyan : PieceSets.Tint(PieceSets.Kind.NeonGlow, color);
            if (TouchGUI.NeonButton(new Rect(x, y, w, h), color.ToString(), tint, NeonTheme.Ground, outlined: out_, fontSize: 14))
            {
                ViewFrom(color);
            }
            x += w + gap;
        }
        TouchGUI.End();
    }

    private void ViewFrom(PieceColor color)
    {
        if (cameraController == null) cameraController = FindFirstObjectByType<CameraController>();
        if (cameraController == null) return;

        Vector3 outward = HomeSideDirection(color, out Vector3 up);
        Vector3 from;
        if (color == PieceColor.Yellow || color == PieceColor.Orange)
        {
            from = outward; // straight from beneath / above (the camera keeps a little tilt)
        }
        else
        {
            float e = SideElevation * Mathf.Deg2Rad;
            from = outward * Mathf.Cos(e) + up * Mathf.Sin(e);
        }
        cameraController.SnapToView(from);
    }

    /// <summary>World direction from the board's centre toward this player's home face</summary>
    private static Vector3 HomeSideDirection(PieceColor color, out Vector3 up)
    {
        Vector3 local;
        switch (color)
        {
            case PieceColor.White: local = Vector3.left; break;     // starts at x = 0, advances +x
            case PieceColor.Black: local = Vector3.right; break;    // starts at x = max
            case PieceColor.Green: local = Vector3.back; break;     // starts at z = 0
            case PieceColor.Purple: local = Vector3.forward; break; // starts at z = max
            case PieceColor.Yellow: local = Vector3.down; break;    // starts at y = 0
            default: local = Vector3.up; break;                     // Orange starts at y = max
        }

        // Board axes follow the Pieces Container, which turns with the board (and Chaos Mode)
        GameObject container = GameObject.Find("Pieces Container");
        Transform t = container != null ? container.transform : null;
        up = t != null ? t.up : Vector3.up;
        return t != null ? t.TransformDirection(local) : local;
    }
}
