using UnityEngine;

/// <summary>Rocks a UI element gently back and forth around its resting tilt (main menu buttons)</summary>
public class Swivel : MonoBehaviour
{
    public float degrees = 1.6f;   // how far either side
    public float seconds = 3f;     // one full back-and-forth

    private float baseAngle, phase;

    private void Start()
    {
        baseAngle = transform.localEulerAngles.z;
        if (baseAngle > 180f) baseAngle -= 360f;
        phase = Random.value * Mathf.PI * 2f;   // each button on its own beat
        seconds *= Random.Range(0.85f, 1.15f);
    }

    private void Update()
    {
        float angle = baseAngle + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / seconds + phase) * degrees;
        transform.localEulerAngles = new Vector3(0f, 0f, angle);
    }
}
