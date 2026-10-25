using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fade (the free promotion animation): the pawn fades away while the new piece fades in on
/// the same square. Both are drawn with see-through copies of their materials while fading.
/// </summary>
public partial class PieceEffects
{
    private IEnumerator FadeRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
    {
        Transform t = promoted.transform;
        SetSquash(t, 0f, 0f);
        yield return null;
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        RestScale(t);
        SetSquash(t, 1f, 1f);

        var outgoing = SeeThrough(pawnCopy);
        var incoming = SeeThrough(t.gameObject);
        SetAlpha(incoming, 0f);

        const float duration = 1.0f;
        for (float time = 0; time < duration; time += Time.deltaTime)
        {
            float k = time / duration;
            SetAlpha(outgoing, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.6f)));       // out over the first 60%
            SetAlpha(incoming, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.3f) / 0.7f)));   // in from 30%
            yield return null;
        }
        if (pawnCopy != null) Destroy(pawnCopy);
        // Back to the piece's own materials
        foreach (var f in incoming) if (f.renderer != null) f.renderer.sharedMaterials = f.original;
    }

    /// <summary>
    /// Reduce Motion: in place of travelling, a piece fades out where it is, appears at its new
    /// spot and fades back in, taking the same time the move would have
    /// </summary>
    public static IEnumerator FadeHop(Transform piece, Vector3 endLocalPosition, float duration)
    {
        if (piece == null) yield break;
        var fading = SeeThrough(piece.gameObject);
        bool moved = false;
        for (float time = 0; time < duration && piece != null; time += Time.deltaTime)
        {
            float k = time / duration;
            if (!moved && k >= 0.5f) { piece.localPosition = endLocalPosition; moved = true; }
            SetAlpha(fading, k < 0.5f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.4f))
                                      : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.6f) / 0.4f)));
            yield return null;
        }
        if (piece == null) yield break;
        piece.localPosition = endLocalPosition;
        foreach (var f in fading) if (f.renderer != null) f.renderer.sharedMaterials = f.original;
    }

    /// <summary>Reduce Motion: a copy of a piece fades out where it stands, then is removed</summary>
    private IEnumerator FadeAwayRoutine(GameObject copy, float duration)
    {
        if (copy == null) yield break;
        var fading = SeeThrough(copy);
        for (float time = 0; time < duration && copy != null; time += Time.deltaTime)
        {
            SetAlpha(fading, 1f - time / duration);
            yield return null;
        }
        if (copy != null) Destroy(copy);
    }

    private class Fading { public Renderer renderer; public Material[] original, faded; }

    // Swap each part's materials for see-through copies with the same colour and texture
    private static List<Fading> SeeThrough(GameObject go)
    {
        var list = new List<Fading>();
        if (go == null) return list;
        Material template = Resources.Load<Material>("PieceSets/Crystal");
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("glow") || n.Contains("indicator") || n.Contains("highlight")) continue;
            Material[] original = r.sharedMaterials;
            var faded = new Material[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                Material src = original[i];
                Material m = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
                Color c = Color.white;
                if (src != null && src.HasProperty("_LightTint")) c = src.GetColor("_LightTint");
                else if (src != null && src.HasProperty("_Color")) c = src.color;
                c.a = 1f;
                m.color = c;
                if (src != null && src.HasProperty("_MainTex")) m.mainTexture = src.mainTexture;
                if (src != null && src.HasProperty("_Metallic")) m.SetFloat("_Metallic", src.GetFloat("_Metallic"));
                if (src != null && src.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", src.GetFloat("_Glossiness"));
                faded[i] = m;
            }
            r.sharedMaterials = faded;
            list.Add(new Fading { renderer = r, original = original, faded = faded });
        }
        return list;
    }

    private static void SetAlpha(List<Fading> list, float alpha)
    {
        foreach (var f in list)
            foreach (Material m in f.faded)
            {
                Color c = m.color;
                c.a = alpha;
                m.color = c;
            }
    }
}
