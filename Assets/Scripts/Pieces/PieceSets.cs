using UnityEngine;

/// <summary>
/// Looks for the chess pieces. Each set restyles every piece's material; the piece shapes
/// stay the same. Add a set by adding a value to Kind, a name in DisplayName and a case
/// in CreateMaterial.
/// </summary>
public static class PieceSets
{
    public enum Kind
    {
        Classic = 0,
        NeonGlow = 1,
        Chrome = 2,
        Crystal = 3,
    }

    public static readonly Kind[] All = { Kind.Classic, Kind.NeonGlow, Kind.Chrome, Kind.Crystal };

    private const string PrefKey = "PieceSet";

    public static Kind Current
    {
        get => (Kind)PlayerPrefs.GetInt(PrefKey, (int)Kind.NeonGlow);
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
        }
    }

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
    public static Color Tint(Kind kind, PieceColor player)
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
    public static Material CreateMaterial(PieceColor player) => CreateMaterial(Current, player);

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
        if (Current != Kind.NeonGlow) return CreateMaterial(player);
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

    /// <summary>
    /// The Abyss Glow set's material (PieceShapes): the model's detail map runs between two tints
    /// for each side, and the rim glows in the side's color. The detail map is set per piece.
    /// </summary>
    public static Material CreateAbyssMaterial(PieceColor player) => CreateShapeMaterial(PieceShapes.Kind.AbyssGlow, player);

    /// <summary>
    /// The material for a modeled set that brings its own colors (PieceShapes): the piece's
    /// detail map runs between two tints for each side, with a rim glowing in the side's color.
    /// The dark side's upper tint stays low so it reads as clearly dark.
    /// </summary>
    public static Material CreateShapeMaterial(PieceShapes.Kind set, PieceColor player)
    {
        Color tint = Tint(Kind.NeonGlow, player);
        Shader shader = Shader.Find("Chess/AbyssPiece");
        if (shader == null) return CreateMaterial(player);
        var material = new Material(shader);
        Color dark, light, rim;
        if (set == PieceShapes.Kind.MushroomGrove && player == PieceColor.White) { dark = new Color(0.5f, 0.58f, 0.56f); light = new Color(0.95f, 0.98f, 0.96f); rim = NeonTheme.Cyan; }
        else if (set == PieceShapes.Kind.WildKingdom && player == PieceColor.White) { dark = new Color(0.42f, 0.5f, 0.5f); light = new Color(0.95f, 0.98f, 0.96f); rim = NeonTheme.Cyan; } // deeper low end so the fur and feathers show
        else if ((set == PieceShapes.Kind.MushroomGrove || set == PieceShapes.Kind.WildKingdom) && player == PieceColor.Black) { dark = new Color(0.03f, 0.01f, 0.05f); light = new Color(0.28f, 0.12f, 0.4f); rim = new Color(1f, 0.2f, 0.8f); }
        else if (player == PieceColor.White) { dark = new Color(0.45f, 0.62f, 0.68f); light = new Color(0.95f, 0.98f, 0.97f); rim = NeonTheme.Cyan; }
        else if (player == PieceColor.Black) { dark = new Color(0.04f, 0.02f, 0.08f); light = new Color(0.35f, 0.18f, 0.5f); rim = new Color(1f, 0.2f, 0.8f); }
        else { dark = tint * 0.12f; light = tint * 0.85f; rim = tint; }
        material.SetColor("_DarkTint", dark);
        material.SetColor("_LightTint", light);
        material.SetColor("_RimColor", rim);
        material.SetColor("_Color", light);
        return material;
    }

    private static Material FromTemplate(string path)
    {
        Material template = Resources.Load<Material>(path);
        if (template != null) return new Material(template);
        return new Material(Shader.Find("Standard"));
    }
}
