using UnityEngine;

/// <summary>
/// Gives the game scene the neon look: deep violet backdrop instead of the sky,
/// cyan wireframe and tinted floor squares. Runs once the board has been built.
/// </summary>
public class NeonGameLook : MonoBehaviour
{
    /// <summary>Switch a floor's see-through material to a solid one</summary>
    private static void MakeOpaque(Material m)
    {
        m.SetFloat("_Surface", 0);
        m.SetFloat("_Mode", 0);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        m.SetInt("_ZWrite", 1);
        m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }

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
            if (BoardLook.SolidFloors)
            {
                // Solid: deep versions of the neon colours, so the pieces still stand out
                c = Color.Lerp(NeonTheme.Ground, c, light ? 0.45f : 0.32f);
                c.a = 1f;
                MakeOpaque(r.material);
            }
            else c.a = light ? 0.22f : 0.14f;
            r.material.color = c;
        }

        foreach (LineRenderer line in FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
        {
            if (!line.name.StartsWith("WireframeLine")) continue;
            line.material.color = NeonTheme.Cyan;
            line.startColor = NeonTheme.Cyan;
            line.endColor = NeonTheme.Cyan;
            if (line.name.EndsWith("Vertical")) line.enabled = !BoardLook.HideVerticalLines;
        }
    }
}
