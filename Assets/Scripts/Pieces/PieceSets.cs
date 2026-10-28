using UnityEngine;

/// <summary>
/// Finishes for the chess pieces (Customize > Pieces > Finish). A finish restyles every piece's
/// material on whichever piece set (PieceShapes) is chosen; textured sets keep their two-tone
/// detail under it (CreateTexturedMaterial). A piece's main colour is always its side's colour.
/// Add a finish by adding a value to Kind, a name in DisplayName and a case in CreateMaterial.
/// </summary>
public static class PieceSets
{
    public enum Kind
    {
        Classic = 0,
        NeonGlow = 1,
        Chrome = 2,
        Crystal = 3,
        SetColors = 4,   // retired (sets' own palettes): read as Neon Glow
    }

    public static readonly Kind[] All = { Kind.NeonGlow, Kind.Chrome, Kind.Crystal, Kind.Classic };

    private const string PrefKey = "PieceSet";

    public static Kind Current
    {
        get
        {
            var picked = (Kind)PlayerPrefs.GetInt(PrefKey, (int)Kind.NeonGlow);
            return picked == Kind.SetColors ? Kind.NeonGlow : picked;
        }
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>The finish the pieces get (same as Current)</summary>
    public static Kind Effective => Current;

    public static string DisplayName(Kind kind)
    {
        switch (kind)
        {
            case Kind.NeonGlow: return "Neon Glow";
            case Kind.Chrome: return "Chrome";
            case Kind.Crystal: return "Crystal";
            default: return "Classic";
        }
    }

    public static string Description(Kind kind)
    {
        switch (kind)
        {
            case Kind.NeonGlow: return "Pieces glow like light tubes";
            case Kind.Chrome: return "Mirror-polished, reflects the backdrop";
            case Kind.Crystal: return "See-through glass with a sheen";
            default: return "Plain white and dark pieces";
        }
    }

    /// <summary>
    /// The player's color in this set. Sides must stay easy to tell apart.
    /// </summary>
    public static Color Tint(Kind kind, PieceColor player) => PlayerSymbols.Nudge(player, BaseTint(kind, player));

    private static Color BaseTint(Kind kind, PieceColor player)
    {
        if (kind == Kind.Classic)
        {
            switch (player)
            {
                case PieceColor.White: return new Color(0.95f, 0.95f, 0.95f);
                case PieceColor.Black: return new Color(0.25f, 0.25f, 0.25f);
                case PieceColor.Green: return new Color(0.15f, 0.5f, 0.15f);
                case PieceColor.Purple: return new Color(0.45f, 0.15f, 0.6f);
                case PieceColor.Yellow: return new Color(0.9f, 0.9f, 0.2f);
                case PieceColor.Orange: return new Color(1f, 0.5f, 0f);
            }
        }

        // Neon-friendly palette for the other sets: White reads as cool white, Black as hot pink
        switch (player)
        {
            case PieceColor.White: return new Color(0.88f, 0.98f, 1f);
            case PieceColor.Black: return NeonTheme.Pink;
            case PieceColor.Green: return NeonTheme.Lime;
            case PieceColor.Purple: return new Color(0.62f, 0.38f, 1f);
            case PieceColor.Yellow: return NeonTheme.Yellow;
            case PieceColor.Orange: return new Color(1f, 0.55f, 0.15f);
        }
        return Color.gray;
    }

    /// <summary>
    /// A new material for one player's pieces in the current set.
    /// Templates in Resources/PieceSets keep the needed shader variants in the build.
    /// </summary>
    public static Material CreateMaterial(PieceColor player) => CreateMaterial(Effective, player);

    public static Material CreateMaterial(Kind kind, PieceColor player)
    {
        Color tint = Tint(kind, player);
        Material material;

        switch (kind)
        {
            case Kind.NeonGlow:
                material = FromTemplate("PieceSets/Glow");
                material.color = Color.Lerp(tint, Color.black, 0.35f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", tint * 1.4f);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Glossiness", 0.7f);
                break;

            case Kind.Chrome:
                material = FromTemplate("PieceSets/Chrome");
                material.color = Color.Lerp(tint, Color.white, 0.35f);
                material.SetFloat("_Metallic", 0.8f);
                material.SetFloat("_Glossiness", 0.88f);
                break;

            case Kind.Crystal:
                material = FromTemplate("PieceSets/Crystal");
                Color glass = Color.Lerp(tint, Color.white, 0.2f);
                glass.a = 0.55f;
                material.color = glass;
                material.SetFloat("_Metallic", 0.1f);
                material.SetFloat("_Glossiness", 0.97f);
                break;

            default:
                material = FromTemplate("PieceSets/Chrome");
                material.color = tint;
                material.SetFloat("_Metallic", player == PieceColor.Black ? 0.2f : 0.1f);
                material.SetFloat("_Glossiness", player == PieceColor.White ? 0.6f : 0.45f);
                break;
        }
        return material;
    }

    /// <summary>
    /// Material for a modeled piece's body. The model's light strips carry the glow, so in
    /// the Neon Glow set the body is dark tinted metal instead of glowing all over.
    /// </summary>
    public static Material CreateBodyMaterial(PieceColor player)
    {
        if (Effective != Kind.NeonGlow) return CreateMaterial(player);
        Color tint = Tint(Kind.NeonGlow, player);
        Material material = FromTemplate("PieceSets/Glow");
        bool light = player == PieceColor.White;
        material.color = light ? new Color(0.82f, 0.84f, 0.92f) : Color.Lerp(tint, new Color(0.1f, 0.05f, 0.2f), 0.6f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", tint * 0.12f);
        material.SetFloat("_Metallic", 0.6f);
        material.SetFloat("_Glossiness", 0.75f);
        return material;
    }

    /// <summary>
    /// The two tones of a textured set's pieces for a side: the light body is the side's colour in
    /// the finish, the dark one an accent that sits in the detail map's carving, spots and stripes
    /// (deep teal on the white side, deep violet on the dark side, like the sets' original looks)
    /// </summary>
    public static (Color dark, Color light) DetailTones(Kind kind, PieceColor player)
    {
        Color light = Tint(kind, player);
        Color dark;
        if (player == PieceColor.White) dark = kind == Kind.Classic ? new Color(0.3f, 0.42f, 0.48f) : new Color(0.12f, 0.45f, 0.58f);
        else if (player == PieceColor.Black) dark = kind == Kind.Classic ? new Color(0.03f, 0.02f, 0.05f) : new Color(0.16f, 0.03f, 0.26f);
        else dark = Color.Lerp(light * 0.22f, new Color(0.1f, 0.03f, 0.18f), 0.35f);
        return (dark, light);
    }

    /// <summary>
    /// A textured set's piece in the chosen finish: the two-tone detail is the colour, and the
    /// finish brings the surface (Neon Glow glows softer than on plain pieces, so the markings
    /// aren't washed out).
    /// </summary>
    public static Material CreateTexturedMaterial(PieceColor player, ChessPieceType type)
    {
        Kind kind = Effective;
        Material material = CreateMaterial(kind, player);
        var (dark, light) = DetailTones(kind, player);
        Texture2D tones = PieceModels.TwoTone(type, dark, light);
        if (tones == null) return material;
        material.mainTexture = tones;
        float alpha = material.color.a;
        switch (kind)
        {
            case Kind.NeonGlow:
                material.color = new Color(0.75f, 0.75f, 0.75f, alpha);
                material.SetTexture("_EmissionMap", tones);
                material.SetColor("_EmissionColor", Color.white * 0.55f);
                break;
            case Kind.Chrome:
                material.color = new Color(1f, 1f, 1f, alpha);
                break;
            default:
                material.color = new Color(1f, 1f, 1f, alpha);
                break;
        }
        return material;
    }

    /// <summary>The glowing light strips on modeled pieces, in the team's neon color</summary>
    public static Material CreateStripMaterial(PieceColor player)
    {
        Color tint = player == PieceColor.White ? NeonTheme.Cyan : Tint(Kind.NeonGlow, player);
        Material material = FromTemplate("PieceSets/Glow");
        material.color = tint;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", tint * 2.2f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0.5f);
        return material;
    }

    private static Material FromTemplate(string path)
    {
        Material template = Resources.Load<Material>(path);
        if (template != null) return new Material(template);
        return new Material(Shader.Find("Standard"));
    }
}
