using UnityEngine;

/// <summary>
/// A checkmate party: chunky neon confetti (squares, strips, little pawns and stars in the game's
/// pink, lime, cyan, yellow and violet) fired from two cannons in the bottom corners, with a
/// shower from the top, for about two and a half seconds. Drawn over the board and under the
/// result card (GUI depth between the two), and only drawn, so it never takes a tap. The pieces
/// are a fixed pool, so a burst allocates nothing. Reduce Motion: a small, slow sprinkle instead.
/// </summary>
public class ConfettiBurst : MonoBehaviour
{
    private const int Pool = 150, Gentle = 36;
    private const float Life = 2.5f;

    private struct Bit
    {
        public Vector2 pos, vel;       // in screen heights (so it looks the same on every screen)
        public float size, aspect, angle, spin, flutter, phase, born, life;
        public Color color;
        public int shape;              // 0 square, 1 strip, 2 pawn, 3 star
    }

    private static ConfettiBurst instance;
    private readonly Bit[] bits = new Bit[Pool];
    private int count;
    private float startedAt = -10f;
    private bool gentle;
    private static Texture2D square;

    private static readonly Color Violet = NeonTheme.Hex("9B5CFF");
    private static readonly Color[] Palette = { NeonTheme.Pink, NeonTheme.Lime, NeonTheme.Cyan, NeonTheme.Yellow, Violet };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("Confetti Burst");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ConfettiBurst>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => Stop(); // Rematch or Home
    }

    /// <summary>Fire the burst (a checkmate win)</summary>
    public static void Fire()
    {
        if (instance != null) instance.Begin();
    }

    /// <summary>Stop drawing (leaving the game)</summary>
    public static void Stop()
    {
        if (instance != null) instance.count = 0;
    }

    private void Begin()
    {
        gentle = AccessibilityPrefs.ReduceMotion;
        startedAt = Time.unscaledTime;
        count = gentle ? Gentle : Pool;
        float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 2f;
        for (int i = 0; i < count; i++)
        {
            ref Bit b = ref bits[i];
            int source = gentle ? 2 : i % 3; // 0 left cannon, 1 right cannon, 2 top shower
            if (source == 2)
            {
                b.pos = new Vector2(Random.Range(0f, aspect), Random.Range(-0.25f, -0.02f));
                b.vel = new Vector2(Random.Range(-0.08f, 0.08f), Random.Range(0.05f, 0.2f)) * (gentle ? 0.5f : 1f);
                b.born = Random.Range(0f, gentle ? 1.2f : 0.7f);
            }
            else
            {
                bool left = source == 0;
                b.pos = new Vector2(left ? -0.02f : aspect + 0.02f, 1.02f);
                float a = Random.Range(52f, 78f) * Mathf.Deg2Rad, speed = Random.Range(1.1f, 1.75f);
                b.vel = new Vector2((left ? 1f : -1f) * Mathf.Cos(a) * speed * 0.75f, -Mathf.Sin(a) * speed);
                b.born = Random.Range(0f, 0.25f);
            }
            float pick = Random.value;
            b.shape = pick < 0.5f ? 0 : pick < 0.82f ? 1 : pick < 0.92f ? 2 : 3;
            b.size = Random.Range(0.014f, 0.026f) * (b.shape >= 2 ? 1.6f : 1f);
            b.aspect = b.shape == 1 ? Random.Range(2.2f, 3.4f) : 1f;
            b.angle = Random.Range(0f, 360f);
            b.spin = Random.Range(-540f, 540f) * (gentle ? 0.3f : 1f);
            b.flutter = Random.Range(4f, 9f);
            b.phase = Random.Range(0f, Mathf.PI * 2f);
            b.life = (gentle ? Life + 1f : Life) - b.born;
            b.color = Palette[i % Palette.Length];
        }
    }

    private void Update()
    {
        if (count == 0) return;
        float age = Time.unscaledTime - startedAt;
        if (age > Life + 1.2f) { count = 0; return; }
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float gravity = gentle ? 0.35f : 1.6f, drag = gentle ? 0.6f : 1.4f;
        for (int i = 0; i < count; i++)
        {
            ref Bit b = ref bits[i];
            if (age < b.born) continue;
            // Gravity, air drag (paper slows fast), and a side-to-side flutter as it falls
            b.vel.y += gravity * dt;
            b.vel *= 1f - Mathf.Min(1f, drag * dt);
            b.vel.y = Mathf.Min(b.vel.y, gentle ? 0.18f : 0.42f);
            float sway = Mathf.Sin((age + b.phase) * b.flutter) * 0.12f;
            b.pos += (b.vel + new Vector2(sway, 0f)) * dt;
            b.angle += b.spin * dt;
        }
    }

    private void OnGUI()
    {
        if (count == 0 || Event.current.type != EventType.Repaint) return;
        if (square == null) square = Texture2D.whiteTexture;
        int oldDepth = GUI.depth;
        GUI.depth = -12; // over the game's controls, under the result card (-15)
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        float h = Screen.height, age = Time.unscaledTime - startedAt;
        for (int i = 0; i < count; i++)
        {
            Bit b = bits[i];
            float t = age - b.born;
            if (t < 0f || t > b.life) continue;
            float alpha = Mathf.Clamp01((b.life - t) / 0.5f);
            // Paper turning over: the width flips through zero now and then
            float turn = Mathf.Abs(Mathf.Cos((age + b.phase) * b.flutter * 0.5f));
            float w = b.size * h * b.aspect, ht = b.size * h * (b.shape >= 2 ? 1f : Mathf.Max(0.15f, turn));
            Vector2 c = b.pos * h;
            GUI.matrix = oldMatrix;
            GUIUtility.RotateAroundPivot(b.angle, c);
            GUI.color = new Color(b.color.r, b.color.g, b.color.b, alpha);
            Texture2D tex = b.shape == 2 ? NeonIcons.Pawn : b.shape == 3 ? NeonIcons.Star : square;
            GUI.DrawTexture(new Rect(c.x - w / 2f, c.y - ht / 2f, w, ht), tex);
        }
        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
        GUI.depth = oldDepth;
    }
}
