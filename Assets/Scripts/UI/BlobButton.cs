using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A soft, wobbling blob drawn as a button's face (home screen). The edge is a rounded
/// rectangle (a superellipse) whose outline slowly ripples and bulges, each button on its
/// own rhythm. The button's own Image stays (invisible) to catch taps; the neon border and
/// hard shadow are the usual Outline and Shadow effects, applied to the blob instead.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class BlobGraphic : MaskableGraphic
{
    private const int Points = 72;
    public float wobble = 0.07f;   // how far the edge moves, as a share of the shorter side
    public float minSeconds = 2.5f, maxSeconds = 3.5f; // each ripple takes this long to come round (the swivel stays 6-8 s)
    private float seed, omega;

    protected override void Awake()
    {
        base.Awake();
        seed = Random.value * 100f;
        omega = Mathf.PI * 2f / Random.Range(minSeconds, maxSeconds); // this button's own rhythm
    }

    private bool still; // Reduce Motion: a fixed blob shape

    private void Update()
    {
        bool reduce = AccessibilityPrefs.ReduceMotion;
        if (reduce && still) return;
        still = reduce;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float shorter = Mathf.Min(r.width, r.height);
        float amp = shorter * wobble;
        // Inset so the bulges reach, but don't pass, the button's edge
        float a = r.width / 2f - amp, b = r.height / 2f - amp;
        Vector2 c = r.center;
        float t = Application.isPlaying && !still ? Time.unscaledTime * omega : 0f;

        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        v.position = c;
        vh.AddVert(v);
        for (int i = 0; i < Points; i++)
        {
            float th = i * Mathf.PI * 2f / Points;
            float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
            // Superellipse: a rounded rectangle with soft corners
            const float p = 2f / 4.5f;
            Vector2 edge = new Vector2(a * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), p), b * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), p));
            // Ripples: a few slow waves running round the edge at different speeds
            // (all three come round once per period, two one way and one the other)
            float n = Mathf.Sin(th * 2f + t + seed) * 0.5f
                    + Mathf.Sin(th * 3f - t + seed * 1.7f) * 0.35f
                    + Mathf.Sin(th * 5f + t + seed * 0.6f) * 0.15f;
            Vector2 dir = edge.sqrMagnitude > 0.001f ? edge.normalized : Vector2.up;
            v.position = c + edge + dir * (amp * (0.4f + 0.6f * n));
            vh.AddVert(v);
        }
        for (int i = 0; i < Points; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % Points);
    }

    /// <summary>Give this button a blob face in place of its rounded rectangle</summary>
    public static void Apply(Button button)
    {
        Image face = button.GetComponent<Image>();
        if (face == null || button.GetComponentInChildren<BlobGraphic>() != null) return;
        var go = new GameObject("Blob", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(button.transform, false);
        go.transform.SetSiblingIndex(0); // behind the label
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var blob = go.AddComponent<BlobGraphic>();
        blob.color = face.color;
        blob.raycastTarget = false;
        // Move the border and shadow from the old face to the blob
        foreach (Shadow fx in face.GetComponents<Shadow>())
        {
            Shadow copy = fx is Outline ? go.AddComponent<Outline>() : go.AddComponent<Shadow>();
            copy.effectColor = fx.effectColor;
            copy.effectDistance = fx.effectDistance;
            Destroy(fx);
        }
        face.color = new Color(0, 0, 0, 0); // still catches taps
        button.targetGraphic = blob;        // pressing darkens the blob
    }
}
