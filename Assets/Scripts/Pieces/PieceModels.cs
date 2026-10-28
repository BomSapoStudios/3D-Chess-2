using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The modeled pieces: cyberpunk (Art/Pieces, exported to Resources/Pieces/Cyberpunk), and the
/// Abyss Glow set (Art/Pieces/Abyss, exported to Resources/Pieces/Abyss) when PieceShapes picks it.
/// Each model has a "Body" mesh, which takes the piece set's material, and a "NeonStrip"
/// mesh of light strips that glow in the team color. Pieces are still built from simple
/// shapes first; Swap replaces those with the model when the piece wakes up.
/// </summary>
public static class PieceModels
{
    public const string ModelName = "Cyberpunk Model";
    public const string StripName = "NeonStrip";

    private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, Texture2D> details = new Dictionary<string, Texture2D>();

    private static string Folder => PieceShapes.Folder(PieceShapes.Active);

    private static readonly Dictionary<string, Texture2D> twoTones = new Dictionary<string, Texture2D>();

    /// <summary>
    /// The detail map coloured in two tones: its dark parts (carving, spots, stripes) in `dark`,
    /// its light parts in `light`, at half size. The finishes multiply by it, so a textured set
    /// keeps its markings in every finish. Null if the set has no detail map.
    /// </summary>
    public static Texture2D TwoTone(ChessPieceType type, Color dark, Color light)
    {
        string key = $"Pieces/{Folder}/{type}_Detail {ColorUtility.ToHtmlStringRGB(dark)} {ColorUtility.ToHtmlStringRGB(light)}";
        if (twoTones.TryGetValue(key, out Texture2D tex)) return tex;
        Texture2D source = Detail(type);
        if (source != null && source.isReadable)
        {
            int w = Mathf.Max(1, source.width / 2), h = Mathf.Max(1, source.height / 2);
            Color32[] from = source.GetPixels32();
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * 2) * source.width + x * 2;
                float d = (from[i].r + from[i + 1].r + from[i + source.width].r + from[i + source.width + 1].r) / (4f * 255f);
                px[y * w + x] = Color.Lerp(dark, light, d);
            }
            tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.SetPixels32(px);
            tex.Apply(true, true);
        }
        twoTones[key] = tex;
        return tex;
    }

    /// <summary>The chosen set's detail map for a piece (grayscale; the material tints it), if it has one</summary>
    public static Texture2D Detail(ChessPieceType type)
    {
        string path = "Pieces/" + Folder + "/" + type + "_Detail";
        if (!details.TryGetValue(path, out Texture2D tex))
        {
            tex = Resources.Load<Texture2D>(path);
            details[path] = tex;
        }
        return tex;
    }

    public static ChessPieceType TypeOf(ChessPiece piece)
    {
        switch (piece)
        {
            case King _: return ChessPieceType.King;
            case Queen _: return ChessPieceType.Queen;
            case Bishop _: return ChessPieceType.Bishop;
            case Knight _: return ChessPieceType.Knight;
            case Rook _: return ChessPieceType.Rook;
            default: return ChessPieceType.Pawn;
        }
    }

    /// <summary>Replace the piece's placeholder shapes with its model. True if it has one.</summary>
    public static bool Swap(ChessPiece piece)
    {
        if (piece.transform.Find(ModelName) != null) return true;

        ChessPieceType type = TypeOf(piece);
        string path = "Pieces/" + Folder + "/" + type;
        if (!cache.TryGetValue(path, out GameObject source))
        {
            source = Resources.Load<GameObject>(path);
            cache[path] = source;
        }
        if (source == null) return false;

        // Drop the placeholder shapes, keeping selection/check indicators
        var placeholders = new List<GameObject>();
        foreach (Transform child in piece.transform)
        {
            if (child.GetComponent<MeshRenderer>() == null) continue;
            if (child.name.Contains("Glow") || child.name.Contains("Indicator")) continue;
            placeholders.Add(child.gameObject);
        }
        foreach (GameObject shape in placeholders) Object.DestroyImmediate(shape);

        GameObject model = Object.Instantiate(source, piece.transform, false);
        model.name = ModelName;
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r.name == StripName) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return true;
    }

    /// <summary>The body mesh, which stands in for the piece's main renderer</summary>
    public static MeshRenderer Body(ChessPiece piece)
    {
        Transform model = piece.transform.Find(ModelName);
        if (model == null) return null;
        foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.name != StripName) return r;
        }
        return null;
    }

    /// <summary>Turn the model so each side's knights look across the board at the other side</summary>
    public static void Face(ChessPiece piece)
    {
        Transform model = piece.transform.Find(ModelName);
        if (model == null) return;
        model.localRotation = Quaternion.Euler(0f, FacingYaw(piece.pieceColor), 0f);
    }

    private static float FacingYaw(PieceColor color)
    {
        switch (color)
        {
            case PieceColor.White: return -90f;
            case PieceColor.Black: return 90f;
            case PieceColor.Green: return 0f;
            case PieceColor.Purple: return 180f;
            default: return 0f;
        }
    }
}
