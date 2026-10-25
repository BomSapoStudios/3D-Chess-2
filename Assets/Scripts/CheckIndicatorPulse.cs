using UnityEngine;

/// <summary>
/// Simple pulsing animation for check indicators on kings.
/// Makes the red check indicator pulse to draw attention to the threatened king.
/// </summary>
public class CheckIndicatorPulse : MonoBehaviour
{
    [Header("Pulse Settings")]
    public float pulseSpeed = 2f;
    public float minScale = 2.0f;
    public float maxScale = 3.0f;
    public float minAlpha = 0.05f;
    public float maxAlpha = 0.15f;
    
    private Renderer pulseRenderer;
    private Material pulseMaterial;
    private Vector3 baseScale;
    private Color baseColor;
    
    private void Start()
    {
        pulseRenderer = GetComponent<Renderer>();
        if (pulseRenderer != null)
        {
            pulseMaterial = pulseRenderer.material;
            baseScale = transform.localScale;
            baseColor = pulseMaterial.color;
        }
        else
        {
            Debug.LogError("CheckIndicatorPulse: No Renderer found on check indicator");
        }
    }
    
    private void Update()
    {
        if (pulseRenderer == null || pulseMaterial == null) return;
        
        // Calculate pulse factor using sine wave
        float time = Time.time * pulseSpeed;
        float pulseFactor = (Mathf.Sin(time) + 1f) * 0.5f; // 0 to 1
        if (AccessibilityPrefs.ReduceMotion) pulseFactor = 0.75f; // Reduce Motion: holds steady
        
        // Animate scale
        float currentScale = Mathf.Lerp(minScale, maxScale, pulseFactor);
        transform.localScale = Vector3.one * currentScale;
        
        // Animate alpha
        float currentAlpha = Mathf.Lerp(minAlpha, maxAlpha, pulseFactor);
        if (AccessibilityPrefs.HighContrast) currentAlpha = Mathf.Max(currentAlpha, 0.62f); // High Contrast: more solid
        Color newColor = baseColor;
        newColor.a = currentAlpha;
        pulseMaterial.color = newColor;
    }
    
    private void OnDestroy()
    {
        // Clean up material reference
        if (pulseMaterial != null)
        {
            DestroyImmediate(pulseMaterial);
        }
    }
}