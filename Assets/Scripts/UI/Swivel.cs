using UnityEngine;

/// <summary>Rocks a UI element gently back and forth around its resting tilt (main menu buttons)</summary>
public class Swivel : MonoBehaviour
{
    public float degrees = 1.6f;   // how far either side
    public float minSeconds = 6f, maxSeconds = 8f; // one full back-and-forth, spread across the buttons
    private float seconds;

    private float baseAngle, phase;
    private static int next;

    private void Start()
    {
        baseAngle = transform.localEulerAngles.z;
        if (baseAngle > 180f) baseAngle -= 360f;
        phase = Random.value * Mathf.PI * 2f;   // each button on its own beat
        // and at its own speed: six evenly spaced from min to max, never two the same
        seconds = minSeconds + (maxSeconds - minSeconds) * (next++ % 6) / 5f;
    }

    private void Update()
    {
        if (AccessibilityPrefs.ReduceMotion)
        {
            transform.localEulerAngles = new Vector3(0f, 0f, baseAngle); // Reduce Motion: holds still at its tilt
            return;
        }
        float angle = baseAngle + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / seconds + phase) * degrees;
        transform.localEulerAngles = new Vector3(0f, 0f, angle);
    }
}
