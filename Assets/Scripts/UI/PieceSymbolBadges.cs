using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Color-Blind Symbols on the board: a small badge with the player's symbol floats over each
/// piece (on the board and on the benches), a warning sign over a king in check and a crosshair
/// over each piece giving check, so neither the side nor the danger relies on color.
/// The badges are kept apart from the pieces (not their children), so nothing that measures or
/// copies a piece ever sees them. Nothing is made while the option is off.
/// </summary>
public class PieceSymbolBadges : MonoBehaviour
{
    private class Badge
    {
        public ChessPiece piece;
        public Transform symbol, alert, target;
        public SpriteRenderer symbolRenderer;
        public Renderer[] pieceRenderers;
        public float top;          // height of the piece's top above its origin
        public PieceColor color;
    }

    private readonly Dictionary<ChessPiece, Badge> badges = new Dictionary<ChessPiece, Badge>();
    private readonly List<ChessPiece> gone = new List<ChessPiece>();
    private float nextScan;
    private static readonly Dictionary<Texture2D, Sprite> sprites = new Dictionary<Texture2D, Sprite>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("Piece Symbol Badges");
        DontDestroyOnLoad(go);
        go.AddComponent<PieceSymbolBadges>();
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (!PlayerSymbols.On || ChessBoard.Instance == null || cam == null)
        {
            if (badges.Count > 0) Clear();
            return;
        }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.25f;
            foreach (ChessPiece piece in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
                if (!badges.ContainsKey(piece)) badges[piece] = Make(piece);
            foreach (Badge b in badges.Values) Measure(b); // pieces change size between bench and board
        }

        float cell = ChessBoard.Instance.cellSize;
        Quaternion facing = cam.transform.rotation;
        ICollection<ChessPiece> attackers = ThreatIndicatorManager.Instance != null ? ThreatIndicatorManager.Instance.Attackers : null;

        foreach (var entry in badges)
        {
            Badge b = entry.Value;
            if (b.piece == null) { gone.Add(entry.Key); continue; }
            bool shown = b.piece.gameObject.activeInHierarchy && AnyVisible(b.pieceRenderers);
            if (b.piece.pieceColor != b.color) Recolor(b); // a conquered piece changes sides
            Vector3 head = b.piece.transform.position + Vector3.up * (b.top + cell * 0.2f);

            SetShown(b.symbol, shown, head, facing, cell * 0.3f);
            bool inCheck = shown && b.piece.pieceType == ChessPieceType.King && b.piece.transform.Find("CheckIndicator") != null;
            SetShown(b.alert, inCheck, head + Vector3.up * cell * 0.34f, facing, cell * 0.36f);
            bool attacking = shown && attackers != null && attackers.Contains(b.piece);
            SetShown(b.target, attacking, head + Vector3.up * cell * 0.3f, facing, cell * 0.3f);
        }
        foreach (ChessPiece p in gone) { Destroy(badges[p].symbol.parent.gameObject); badges.Remove(p); }
        gone.Clear();
    }

    private Badge Make(ChessPiece piece)
    {
        var holder = new GameObject($"Badge {piece.pieceColor} {piece.pieceType}").transform;
        holder.SetParent(transform, false);
        var b = new Badge { piece = piece, color = piece.pieceColor };
        b.symbol = Part(holder, "Symbol", PlayerSymbols.Texture(piece.pieceColor), PlayerSymbols.Tint(piece.pieceColor), 0, out b.symbolRenderer);
        b.alert = Part(holder, "Check", PlayerSymbols.Alert, new Color(1f, 0.35f, 0.45f), 2, out _);
        b.target = Part(holder, "Giving Check", PlayerSymbols.Target, new Color(1f, 0.6f, 0.2f), 1, out _);

        // Renderers of the piece itself (not its highlight bubbles)
        var parts = new List<Renderer>();
        foreach (MeshRenderer r in piece.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("glow") || n.Contains("indicator") || n.Contains("highlight")) continue;
            parts.Add(r);
        }
        b.pieceRenderers = parts.ToArray();
        Measure(b);
        return b;
    }

    /// <summary>How far the piece's top stands above its origin</summary>
    private static void Measure(Badge b)
    {
        if (b.piece == null) return;
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer r in b.pieceRenderers)
        {
            if (r == null) continue;
            if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }
        b.top = any ? bounds.max.y - b.piece.transform.position.y : ChessBoard.Instance.cellSize * 0.6f;
    }

    private static Transform Part(Transform holder, string name, Texture2D texture, Color color, int order, out SpriteRenderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(holder, false);
        renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = SpriteOf(texture);
        renderer.color = color;
        renderer.sortingOrder = 50 + order;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.SetActive(false);
        return go.transform;
    }

    private static Sprite SpriteOf(Texture2D texture)
    {
        if (!sprites.TryGetValue(texture, out Sprite s) || s == null)
        {
            s = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            sprites[texture] = s;
        }
        return s;
    }

    private static void Recolor(Badge b)
    {
        b.color = b.piece.pieceColor;
        b.symbolRenderer.sprite = SpriteOf(PlayerSymbols.Texture(b.color));
        b.symbolRenderer.color = PlayerSymbols.Tint(b.color);
    }

    private static bool AnyVisible(Renderer[] parts)
    {
        foreach (Renderer r in parts) if (r != null && r.enabled && r.gameObject.activeInHierarchy) return true;
        return false;
    }

    private static void SetShown(Transform t, bool shown, Vector3 position, Quaternion facing, float size)
    {
        if (t.gameObject.activeSelf != shown) t.gameObject.SetActive(shown);
        if (!shown) return;
        t.SetPositionAndRotation(position, facing);
        t.localScale = Vector3.one * size;
    }

    private void Clear()
    {
        foreach (Badge b in badges.Values) if (b.symbol != null) Destroy(b.symbol.parent.gameObject);
        badges.Clear();
    }
}
