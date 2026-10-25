using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// High Contrast (Accessibility) for a uGUI canvas: small text that isn't a button label gets a
/// dark plate behind its words, and faded small text is drawn at full strength, so it reads at
/// 4.5:1 or better over pictures and backdrops. Plates are siblings placed just behind the text
/// (never its children, which would draw on top) and follow it every frame. Nothing is added
/// while the option is off, and everything is removed when it's switched off.
/// </summary>
public class ContrastPlates : MonoBehaviour
{
    public float maxFontSize = 16f;

    private class Plate { public TMP_Text text; public RectTransform rect; public float alpha; }
    private readonly Dictionary<TMP_Text, Plate> plates = new Dictionary<TMP_Text, Plate>();
    private readonly List<TMP_Text> gone = new List<TMP_Text>();
    private float nextScan;

    private void LateUpdate()
    {
        if (!AccessibilityPrefs.HighContrast)
        {
            if (plates.Count > 0) Clear();
            return;
        }

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.4f; // screens build rows as they open
            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(false))
                if (!plates.ContainsKey(text) && Wants(text)) plates[text] = Make(text);
        }

        foreach (var entry in plates)
        {
            Plate p = entry.Value;
            if (p.text == null || p.rect == null) { gone.Add(entry.Key); continue; }
            bool show = p.text.isActiveAndEnabled && !string.IsNullOrWhiteSpace(p.text.text) && p.text.textBounds.size.x > 0.5f;
            if (p.rect.gameObject.activeSelf != show) p.rect.gameObject.SetActive(show);
            if (!show) continue;
            if (p.text.alpha < 1f) p.text.alpha = 1f;
            Follow(p);
        }
        foreach (TMP_Text t in gone)
        {
            if (plates[t].rect != null) Destroy(plates[t].rect.gameObject);
            plates.Remove(t);
        }
        gone.Clear();
    }

    private bool Wants(TMP_Text text)
    {
        float size = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
        if (size > maxFontSize) return false;
        if (text.GetComponentInParent<Button>() != null) return false; // labels on neon button faces are already dark on bright
        if (text.color.r + text.color.g + text.color.b < 0.6f) return false; // dark text sits on a bright fill
        return true;
    }

    private Plate Make(TMP_Text text)
    {
        var go = new GameObject("Contrast Plate", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(text.transform.parent, false);
        rect.SetSiblingIndex(text.transform.GetSiblingIndex()); // just behind the text
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        Image image = go.AddComponent<Image>();
        image.sprite = NeonTheme.Rounded;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f; // tighter corners on small plates
        image.color = new Color(NeonTheme.Ground.r, NeonTheme.Ground.g, NeonTheme.Ground.b, 0.88f);
        image.raycastTarget = false;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        return new Plate { text = text, rect = rect, alpha = text.alpha };
    }

    /// <summary>Cover the words themselves (with a little margin), wherever the text has put them</summary>
    private static void Follow(Plate p)
    {
        RectTransform parent = p.rect.parent as RectTransform;
        if (parent == null) return;
        Bounds b = p.text.textBounds;
        Transform t = p.text.transform;
        Vector3 min = parent.InverseTransformPoint(t.TransformPoint(b.min));
        Vector3 max = parent.InverseTransformPoint(t.TransformPoint(b.max));
        Vector2 size = new Vector2(Mathf.Abs(max.x - min.x) + 12f, Mathf.Abs(max.y - min.y) + 6f);
        Vector2 centre = (Vector2)(min + max) / 2f - parent.rect.center;
        p.rect.anchoredPosition = centre;
        p.rect.sizeDelta = size;
    }

    private void Clear()
    {
        foreach (Plate p in plates.Values)
        {
            if (p.rect != null) Destroy(p.rect.gameObject);
            if (p.text != null) p.text.alpha = p.alpha;
        }
        plates.Clear();
    }
}
