#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Development-only: renders the Customize screen's picture cards. `defaults write BomSapo.Chess3D
/// DebugCapture -int 1` renders each piece set and promotion animation from the menu's preview
/// stage; 2 (with DebugRematchDemo) renders each backdrop around a game board (ThumbCapture).
/// PNGs land in the app's Documents/Thumbs, to copy into Resources/CustomizeThumbs.
/// </summary>
public partial class NeonMenu
{
    private IEnumerator CaptureMenuThumbs()
    {
        yield return new WaitForSeconds(1f);
        freezeTurntable = true;
        foreach (PieceShapes.Kind k in PieceShapes.All)
        {
            PieceShapes.Showing = k;
            ShowShapePreview(k);
            yield return new WaitForSeconds(2f);
            ThumbCapture.Save(previewTexture, "Pieces_" + k);
        }
        freezeTurntable = false;
        PieceShapes.Showing = null;
        foreach (PromotionStyle.Kind k in System.Enum.GetValues(typeof(PromotionStyle.Kind)))
        {
            ShowPromotionPreview(k);
            yield return new WaitForSeconds(k == PromotionStyle.Kind.BlackHole ? 1.0f : k == PromotionStyle.Kind.Splat ? 0.95f : 0.75f); // mid-animation, where each looks most like itself
            ThumbCapture.Save(previewTexture, "Promo_" + k);
        }
        ClosePromotionPreview();
        Debug.Log("THUMBS: menu done");
    }
}

/// <summary>Development-only: renders each backdrop around the game board (DebugCapture 2)</summary>
public class ThumbCapture : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (PlayerPrefs.GetInt("DebugCapture", 0) != 2 || FindFirstObjectByType<ThumbCapture>() != null) return;
        var go = new GameObject("Thumb Capture");
        DontDestroyOnLoad(go);
        go.AddComponent<ThumbCapture>().StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        // Wait for a game with pieces on the board
        while (FindFirstObjectByType<EmergencyChessBoard>() == null) yield return null;
        yield return new WaitForSeconds(9f);
        Backdrops.Kind saved = Backdrops.Current;
        foreach (Backdrops.Kind k in Backdrops.All)
        {
            // The demo game may restart under us: start over on this backdrop if the camera goes
            Camera cam = null;
            for (int tries = 0; tries < 5 && cam == null; tries++)
            {
                while (Camera.main == null || FindFirstObjectByType<EmergencyChessBoard>() == null) yield return null;
                cam = Camera.main;
                while (cam != null && !Backdrops.Apply(cam, k)) yield return null;
                yield return new WaitForSeconds(3f);
                if (cam == null || cam != Camera.main) cam = null;
            }
            if (cam == null) continue;
            var rt = new RenderTexture(960, 600, 24);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            Save(rt, "Backdrop_" + k);
            rt.Release();
        }
        Backdrops.Apply(Camera.main, saved);
        Debug.Log("THUMBS: backdrops done");
    }

    public static void Save(RenderTexture rt, string name)
    {
        RenderTexture before = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = before;
        string dir = Path.Combine(Application.persistentDataPath, "Thumbs");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log("THUMBS: saved " + name);
    }
}
#endif
