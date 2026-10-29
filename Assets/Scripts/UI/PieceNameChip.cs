using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tapping a piece shows its name in a small chip over it for a couple of seconds ("White Knight",
/// "Black Rook"), so a themed set's alien or elephant still reads as the chess piece it is.
/// Works on the board and the 3D benches, for anyone's piece, in setup and in play.
/// </summary>
public class PieceNameChip : MonoBehaviour
{
    private const float ShowFor = 2.2f, FadeFor = 0.4f;

    private static PieceNameChip instance;
    private ChessPiece piece;
    private float shownAt;
    private readonly List<Renderer> parts = new List<Renderer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("Piece Name Chip");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PieceNameChip>();
    }

    /// <summary>Name this piece over it (a tap on the board or a bench)</summary>
    public static void Show(ChessPiece piece)
    {
        if (instance == null || piece == null || QueenBlitz.Active) return; // a queen among pawns needs no names
        instance.piece = piece;
        instance.shownAt = Time.unscaledTime;
        instance.parts.Clear();
        foreach (MeshRenderer r in piece.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("glow") || n.Contains("indicator") || n.Contains("highlight")) continue;
            instance.parts.Add(r);
        }
    }

    public static string Name(ChessPiece piece)
    {
        if (ZombieMode.Active && piece.pieceColor == ZombieMode.ZombieColor)
            return piece.pieceType == ChessPieceType.Pawn ? "Zombie" : $"Zombie {piece.pieceType}";
        return $"{piece.pieceColor} {piece.pieceType}";
    }

    private void OnGUI()
    {
        if (piece == null) return;
        float age = Time.unscaledTime - shownAt;
        Camera cam = Camera.main;
        if (age > ShowFor || cam == null || !piece.gameObject.activeInHierarchy) { piece = null; return; }

        // Over the top of the piece (and above its Color-Blind Symbol, when that's on)
        bool any = false, visible = false;
        Bounds bounds = default;
        foreach (Renderer r in parts)
        {
            if (r == null) continue;
            visible |= r.enabled;
            if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }
        if (!visible) return; // hidden setup: someone else's piece out of sight
        float cell = ChessBoard.Instance != null ? ChessBoard.Instance.cellSize : 1f;
        Vector3 top = any ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) : piece.transform.position;
        if (PlayerSymbols.On) top += Vector3.up * cell * 0.55f;
        Vector3 screen = cam.WorldToScreenPoint(top);
        if (screen.z <= 0f) return;

        int oldDepth = GUI.depth;
        GUI.depth = -20; // over the other in-game GUI
        TouchGUI.Begin();
        Rect safe = Screen.safeArea;
        float scale = TouchGUI.Scale;
        Vector2 at = new Vector2((screen.x - safe.x) / scale, (Screen.height - screen.y - (Screen.height - safe.yMax)) / scale);

        string text = Name(piece).ToUpperInvariant();
        GUIStyle style = NeonTheme.GUILabelStyle(NeonTheme.White, 13, true, TextAnchor.MiddleCenter, display: true);
        style.wordWrap = false;
        Vector2 size = style.CalcSize(new GUIContent(text));
        Rect chip = new Rect(at.x - size.x / 2f - 10f, at.y - size.y - 14f, size.x + 20f, size.y + 6f);

        float alpha = Mathf.Clamp01((ShowFor - age) / FadeFor);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        NeonTheme.GUIPanel(chip, PlayerSymbols.Tint(piece.pieceColor));
        GUI.Label(chip, text, style);
        GUI.color = old;
        TouchGUI.End();
        GUI.depth = oldDepth;
    }
}
