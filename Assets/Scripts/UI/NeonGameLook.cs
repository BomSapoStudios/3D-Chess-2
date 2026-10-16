using UnityEngine;

/// <summary>
/// Gives the game scene the neon look: deep violet backdrop instead of the sky,
/// cyan wireframe and tinted floor squares. Runs once the board has been built.
/// </summary>
public class NeonGameLook : MonoBehaviour
{
    private static Material clearFloor;
    private static Material ClearFloor
    {
        get
        {
            if (clearFloor != null) return clearFloor;
            Shader shader = Resources.Load<Shader>("Shaders/ClearFloor");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            clearFloor = new Material(shader) { name = "See-through Floor" };
            return clearFloor;
        }
    }

    private static Material solidFloor;
    /// <summary>A fresh solid-floor material (each square gets its own copy when its colour is set)</summary>
    private static Material SolidFloor
    {
        get
        {
            if (solidFloor != null) return solidFloor;
            // Our own shader (Resources/Shaders/SolidFloor): two-sided and writing depth, so see-through
            // things around the board (backdrop fog, glows) can't sort in front of the squares
            Shader shader = Resources.Load<Shader>("Shaders/SolidFloor");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Sprites/Default");
            solidFloor = new Material(shader) { name = "Solid Floor" };
            return solidFloor;
        }
    }

    // Queen Blitz ice floors
    private static readonly Color IceLight = NeonTheme.Hex("E4FBFF");
    private static readonly Color IceDark = NeonTheme.Hex("8FD3FF");

    private EmergencyChessBoard board;
    private int styledFloors;
    private bool backdropApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        GameObject go = new GameObject("Neon Game Look");
        go.AddComponent<NeonGameLook>();
        DontDestroyOnLoad(go);
    }

    private void LateUpdate()
    {
        if (board == null)
        {
            board = FindFirstObjectByType<EmergencyChessBoard>();
            styledFloors = 0;
            backdropApplied = false;
            if (board == null) return;
        }

        Camera cam = Camera.main;
        if (cam != null && !backdropApplied)
        {
            backdropApplied = Backdrops.Apply(cam, Backdrops.Current);
        }
        Backdrops.Drift(0.4f);

        // The board is built a frame or two after the scene starts; style it once it exists
        MeshRenderer[] renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int floors = 0;
        foreach (MeshRenderer r in renderers) if (r.name.StartsWith("Floor_")) floors++;
        if (floors == 0 || floors == styledFloors) return;
        styledFloors = floors;

        foreach (MeshRenderer r in renderers)
        {
            if (!r.name.StartsWith("Floor_")) continue;
            string[] parts = r.name.Split('_');
            if (parts.Length != 4) continue;
            int.TryParse(parts[1], out int x);
            int.TryParse(parts[2], out int y);
            int.TryParse(parts[3], out int z);
            bool light = (x + y + z) % 2 == 0;
            Color c = light ? NeonTheme.Cyan : NeonTheme.Pink;
            if (QueenBlitz.Active && QueenBlitz.Current.ice)
            {
                // Ice: frosty white-blue squares, a little more opaque so the board reads as frozen
                c = light ? IceLight : IceDark;
                c.a = BoardLook.SolidFloors ? 1f : light ? 0.42f : 0.32f;
                if (BoardLook.SolidFloors) c = Color.Lerp(NeonTheme.Ground, c, light ? 0.75f : 0.6f);
                r.material = BoardLook.SolidFloors ? SolidFloor : ClearFloor;
                r.material.color = c;
                continue;
            }
            if (BoardLook.SolidFloors)
            {
                // Solid: deep versions of the neon colours, so the pieces still stand out
                c = Color.Lerp(NeonTheme.Ground, c, light ? 0.45f : 0.32f);
                c.a = 1f;
                // Unlit, two-sided (squares above the camera show from underneath too) and solid
                r.material = SolidFloor;
            }
            else
            {
                c.a = light ? 0.22f : 0.14f;
                // Two-sided, so squares above the camera show from underneath too (the plain floor
                // material only draws its top face, which hid most of the board from Black's side)
                r.material = ClearFloor;
            }
            r.material.color = c;
        }

        Color wire = QueenBlitz.Active && QueenBlitz.Current.ice ? IceLight : NeonTheme.Cyan;
        foreach (LineRenderer line in FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
        {
            if (!line.name.StartsWith("WireframeLine")) continue;
            line.material.color = wire;
            line.startColor = wire;
            line.endColor = wire;
            if (line.name.EndsWith("Vertical") || line.name.EndsWith("Top")) line.enabled = !BoardLook.HideVerticalLines;
        }
    }
}
