using UnityEngine;

/// <summary>
/// Pulsing animation for threat indicators on attacking pieces.
/// Provides different visual rhythm from king check indicators to distinguish attacker vs attackee.
/// </summary>
public class ThreatIndicatorPulse : MonoBehaviour
{
    [Header("Pulse Settings")]
    public float pulseSpeed = 2f;
    public float minScale = 1.2f;
    public float maxScale = 1.8f;
    public float minAlpha = 0.4f;
    public float maxAlpha = 0.8f;
    
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
            Debug.LogError("ThreatIndicatorPulse: No Renderer found on threat indicator");
        }
    }
    
    private void Update()
    {
        if (pulseRenderer == null || pulseMaterial == null) return;
        
        // Calculate pulse factor using sine wave (faster than king check indicator)
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